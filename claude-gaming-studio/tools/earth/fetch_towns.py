# Fetches the real centre of every town in earth_places.txt from Overture Maps (buildings and streets), for
# bake_towns.py. For each town it looks 1.2 km around the listed spot, finds where the buildings are densest (the
# listed spot is often a kilometre off), and keeps a window as wide as the town is in the game (at 5.5 real metres per
# game metre). One small pickle per town: <out>/<index>.pkl. Safe to stop and start again.
# usage: python3 fetch_towns.py earth_places.txt <out dir> [first] [count]
import sys, os, io, math, pickle, threading, time, numpy as np, pyarrow.parquet as pq, pyarrow.compute as pc
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from shapely import wkb
import ov
from cities import CITIES
SCALE = 5.5; SEARCH = 1200.0
def places(path):
    out = []
    for line in open(path, encoding='utf8'):
        line = line.strip()
        if not line or line[0] == '#': continue
        name, lat, lon, pop = line.rsplit(',', 3)
        out.append((name, float(lat), float(lon), float(pop)))
    return out
def radius(pop): return min(260.0, max(30.0, 22.0 + math.sqrt(pop) * 0.075))   # Places.RadiusFor
def radii(ps):
    """Every town's radius in the game: by population, then shrunk where two towns would overlap (they just touch)."""
    a = np.array([(p[1], p[2]) for p in ps]); la, lo = np.radians(a[:, 0]), np.radians(a[:, 1])
    v = np.column_stack((np.cos(la) * np.cos(lo), np.cos(la) * np.sin(lo), np.sin(la))); r = np.array([radius(p[3]) for p in ps]); out = r.copy()
    for i in range(len(ps)):
        d = np.arccos(np.clip(v @ v[i], -1, 1)) * 25000.0; d[i] = 1e9
        out[i] = max(18.0, min(r[i], float((d * r[i] / (r + r[i])).min())))
    return out
def in_core(lat, lon, r):
    """Is this town swallowed by one of the rebuilt city cores (CityReplica.Crowds)?"""
    for c in CITIES.values():
        b = c['box']; la, lo = c.get('site', ((b[1] + b[3]) / 2, (b[0] + b[2]) / 2))
        hw = (b[2] - b[0]) / 2 * 111320 * math.cos(math.radians((b[1] + b[3]) / 2)) / SCALE; hd = (b[3] - b[1]) / 2 * 110574 / SCALE
        ap = c.get('apron', (0, 0, 0, 0))
        x = (lon - lo) * 436.3 * math.cos(math.radians(la)); z = (lat - la) * 436.3      # game metres
        if -hw - ap[0] - r - 90 < x < hw + ap[1] + r + 90 and -hd - ap[2] - r - 90 < z < hd + ap[3] + r + 90: return True
    return False

class Layer:
    """One Overture layer: where each row group lies, and a small cache of the ones read lately."""
    def __init__(self, kind, cache_dir):
        self.kind = kind; self.cols = ov.LAYERS[kind][1]; self.lock = threading.Lock(); self.tables = OrderedDict(); self.mds = OrderedDict()
        f = f'{cache_dir}/index_{kind}.pkl'
        if os.path.exists(f): self.files, self.rg = pickle.load(open(f, 'rb'))
        else:
            self.files = ov.listing(ov.REL + ov.LAYERS[kind][0])
            def one(i):
                k, size = self.files[i]; md = pq.ParquetFile(io.BufferedReader(ov.RF(k, size), buffer_size=1 << 20)).metadata
                names = [md.schema.column(j).path for j in range(md.num_columns)]; ix = [names.index('bbox.' + n) for n in ('xmin', 'xmax', 'ymin', 'ymax')]
                rows = []
                for g in range(md.num_row_groups):
                    st = [md.row_group(g).column(j).statistics for j in ix]
                    rows.append((i, g, -180, 180, -90, 90) if any(s is None for s in st) else (i, g, st[0].min, st[1].max, st[2].min, st[3].max))
                return rows
            with ThreadPoolExecutor(16) as ex: self.rg = np.array([r for rows in ex.map(one, range(len(self.files))) for r in rows], np.float64)
            pickle.dump((self.files, self.rg), open(f, 'wb'))
        print(kind, len(self.files), 'files', len(self.rg), 'row groups', flush=True)
    def table(self, i, g):
        key = (i, g)
        with self.lock:
            if key in self.tables: self.tables.move_to_end(key); return self.tables[key]
            md = self.mds.get(i)
        k, size = self.files[i]
        f = pq.ParquetFile(io.BufferedReader(ov.RF(k, size), buffer_size=1 << 20), metadata=md)
        t = f.read_row_group(g, columns=self.cols + ['bbox'])
        with self.lock:
            self.mds[i] = f.metadata
            while len(self.mds) > 48: self.mds.popitem(last=False)
            self.tables[key] = t
            while len(self.tables) > 36: self.tables.popitem(last=False)
        return t
    def box(self, x0, y0, x1, y1):
        r = self.rg; hit = r[(r[:, 3] >= x0) & (r[:, 2] <= x1) & (r[:, 5] >= y0) & (r[:, 4] <= y1)]
        out = []
        for i, g in hit[:, :2].astype(int):
            t = self.table(i, g); b = t.column('bbox').combine_chunks()
            m = pc.and_(pc.and_(pc.greater(b.field('xmax'), x0), pc.less(b.field('xmin'), x1)), pc.and_(pc.greater(b.field('ymax'), y0), pc.less(b.field('ymin'), y1)))
            out += t.filter(m).drop_columns(['bbox']).to_pylist()
        return out

def town(idx, p, r, B, R, out):
    f = f'{out}/{idx}.pkl'
    if os.path.exists(f): return
    name, lat, lon, pop = p; half = r * SCALE                       # real metres
    kx = 111320 * math.cos(math.radians(lat)); kz = 110574.0
    dx, dz = (SEARCH + half) / kx, (SEARCH + half) / kz
    bl = B.box(lon - dx, lat - dz, lon + dx, lat + dz)
    # Where the buildings are densest: a coarse grid of built area, summed over the town's window.
    cell = 60.0; n = int(2 * (SEARCH + half) / cell) + 1; grid = np.zeros((n, n), np.float32); geo = []
    for row in bl:
        try: g = wkb.loads(row['geometry'])
        except Exception: continue
        if g.is_empty or g.geom_type not in ('Polygon', 'MultiPolygon'): continue
        c = g.centroid; x = (c.x - lon) * kx; z = (c.y - lat) * kz
        i, j = int((x + SEARCH + half) / cell), int((z + SEARCH + half) / cell)
        if 0 <= i < n and 0 <= j < n: grid[j, i] += g.area * kx * kz
        geo.append((x, z, g, row))
    cx = cz = 0.0
    if geo:
        w = max(1, int(round(2 * half / cell))); s = np.cumsum(np.cumsum(np.pad(grid, ((1, 0), (1, 0))), 0), 1)
        win = s[w:, w:] - s[:-w, w:] - s[w:, :-w] + s[:-w, :-w]
        # Prefer the listed spot unless somewhere else is clearly denser.
        jj, ii = np.mgrid[0:win.shape[0], 0:win.shape[1]]; d = np.hypot((ii + w / 2) * cell - SEARCH - half, (jj + w / 2) * cell - SEARCH - half)
        j, i = np.unravel_index(np.argmax(win * (1 - 0.35 * d / (SEARCH + 1))), win.shape)
        cx, cz = (i + w / 2) * cell - SEARCH - half, (j + w / 2) * cell - SEARCH - half
    clon, clat = lon + cx / kx, lat + cz / kz; pad = 40.0
    bx = ((half + pad) / kx, (half + pad) / kz)
    blds = []
    for x, z, g, row in geo:
        if abs(x - cx) > half + pad or abs(z - cz) > half + pad: continue
        for pg in ([g] if g.geom_type == 'Polygon' else list(g.geoms)):
            blds.append((np.asarray(pg.exterior.coords, np.float64), row['height'], row['num_floors'], ((row['names'] or {}).get('primary') or '')))
    rds = []
    for row in R.box(clon - bx[0], clat - bx[1], clon + bx[0], clat + bx[1]):
        if row['subtype'] != 'road' or not row['class']: continue
        try: g = wkb.loads(row['geometry'])
        except Exception: continue
        if g.geom_type == 'LineString': rds.append((row['class'], np.asarray(g.coords, np.float64)))
    pickle.dump(dict(name=name, lat=lat, lon=lon, pop=pop, radius=r, clat=clat, clon=clon, buildings=blds, roads=rds), open(f + '.tmp', 'wb'), protocol=4)
    os.rename(f + '.tmp', f)

if __name__ == '__main__':
    ps = places(sys.argv[1]); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
    first = int(sys.argv[3]) if len(sys.argv) > 3 else 0; count = int(sys.argv[4]) if len(sys.argv) > 4 else len(ps)
    rr = radii(ps)
    todo = [(i, p) for i, p in enumerate(ps) if not in_core(p[1], p[2], radius(p[3])) and not os.path.exists(f'{out}/{i}.pkl')]
    # Neighbours one after another, so the pieces of the map just read are used again.
    todo.sort(key=lambda t: (int((t[1][1] + 90) / 3), t[1][2] if int((t[1][1] + 90) / 3) % 2 == 0 else -t[1][2]))
    todo = todo[first:first + count]; print(len(todo), 'towns to fetch', flush=True)
    B = Layer('buildings', out); R = Layer('roads', out); t0 = time.time(); done = [0]
    def run(t):
        try: town(t[0], t[1], float(rr[t[0]]), B, R, out)
        except Exception as e: print('FAILED', t[0], t[1][0], repr(e)[:200], flush=True)
        done[0] += 1
        if done[0] % 50 == 0: print(done[0], 'done', round(time.time() - t0), 's', flush=True)
    with ThreadPoolExecutor(6) as ex: list(ex.map(run, todo))
    print('finished', done[0], round(time.time() - t0), 's', flush=True)
