# Bakes the real centre of every town (fetched by fetch_towns.py) into one file the game reads town by town
# (Runtime/World/CityReplica.cs, Resources/Towns/towns.bytes). Each town keeps the size it has in the game
# (Places.RadiusFor) and stands on the game's own landscape; what changes is that its streets and buildings are the
# real ones from the middle of the real town, at 5.5 real metres per game metre like the big city cores.
# Data: Overture Maps, (c) OpenStreetMap contributors, Overture Maps Foundation.
# usage: python3 bake_towns.py <towns dir> out.bytes [preview.png]
import sys, os, glob, math, pickle, struct, numpy as np
from multiprocessing import Pool
from shapely.geometry import LineString, Polygon, Point
from shapely.ops import unary_union
from shapely.strtree import STRtree
from cities import PALS
SCALE = 5.5; Q = 0.02                                   # file units: 2 cm
WIDTH = {'motorway': 5.0, 'trunk': 5.0, 'primary': 4.6, 'secondary': 4.2, 'tertiary': 3.8, 'residential': 3.4, 'unclassified': 3.4, 'living_street': 2.8, 'pedestrian': 2.8}
CLS = {k: i for i, k in enumerate(WIDTH)}
PAL_TOWER = [(115, 173, 230), (97, 148, 191), (140, 191, 217), (77, 115, 158), (150, 170, 190), (176, 196, 210)]
PAL_NAMES = sorted(PALS)
def gh(real): return real * 0.5 if real < 12 else 6 + (real - 12) * 0.25
def region(lat, lon):
    """The colour family of a town's walls, by where it is."""
    if lon < -30: return 'us' if lat > 25 else 'med'                                   # the Americas
    if lon > 110 and lat < -10: return 'us'                                            # Australia, New Zealand
    if lon > 95: return 'asia'
    if 60 < lon <= 95 and lat < 38: return 'desert'                                    # South Asia
    if lat > 45 and lon > 22: return 'east'
    if lat > 49.5 and lon < 2: return 'london'
    if 42 < lat <= 51.5 and -5 < lon < 8.5: return 'paris'
    if lat > 44: return 'east'
    if lat > 35.5 and lon < 45: return 'med'
    if lat > 12: return 'desert'                                                       # North Africa, Arabia
    return 'med'
def ears(pts):
    n = len(pts); idx = list(range(n))
    area = sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n))
    if area < 0: idx.reverse()
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    out = []; guard = 0
    while len(idx) > 3 and guard < 3000:
        guard += 1; done = False
        for k in range(len(idx)):
            i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % len(idx)]
            a, b_, c = pts[i0], pts[i1], pts[i2]
            if cross(a, b_, c) <= 1e-9: continue
            if any(cross(a, b_, pts[j]) >= 0 and cross(b_, c, pts[j]) >= 0 and cross(c, a, pts[j]) >= 0 for j in idx if j not in (i0, i1, i2)): continue
            out.append((i0, i1, i2)); idx.pop(k); done = True; break
        if not done: idx.pop(0)
    if len(idx) == 3: out.append(tuple(idx))
    return out
def polys_of(g):
    if g.geom_type == 'Polygon': return [] if g.is_empty else [g]
    return [q for q in getattr(g, 'geoms', []) if q.geom_type == 'Polygon' and not q.is_empty]

def bake(f):
    """One town: (place index, lat, lon, radius, record bytes, preview data) or None if the map has nothing there."""
    try:
        d = pickle.load(open(f, 'rb')); idx = int(os.path.basename(f)[:-4]); R = d['radius']
        kx = 111320 * math.cos(math.radians(d['clat'])) / SCALE; kz = 110574 / SCALE
        def P(c): return np.column_stack(((c[:, 0] - d['clon']) * kx, (c[:, 1] - d['clat']) * kz))
        circle = Point(0, 0).buffer(R, 24); rng = np.random.RandomState(idx)
        streets = []
        for cls, c in d['roads']:
            if cls not in WIDTH or len(c) < 2: continue
            ls = LineString(P(c)).intersection(circle)
            for part in ([ls] if ls.geom_type == 'LineString' else list(getattr(ls, 'geoms', []))):
                if part.geom_type != 'LineString' or part.length < 1.5: continue
                p = np.array(part.simplify(0.25).coords)
                for a in range(0, len(p) - 1, 200): streets.append((cls, p[a:a + 201]))
        own = []; mids = []
        for c, h, floors, name in d['buildings']:
            if len(c) < 4: continue
            q = Polygon(P(c))
            if not q.is_valid or q.area < 0.3: continue
            ct = q.centroid
            if ct.x * ct.x + ct.y * ct.y > (R - 1.5) ** 2: continue
            real_h = max(3.5, h or ((floors or 2) * 3.5))
            if real_h >= 35 or q.area * SCALE * SCALE >= 1800:
                s = q.simplify(2.0 / SCALE); own.append((real_h, s if s.geom_type == 'Polygon' and s.area > 0.5 else q))
            else: mids.append((max(real_h, 6.5), q))
        if len(streets) < 2 and len(own) + len(mids) < 4: return None
        blocks = []
        for lo, hi in ((0, 12.5), (12.5, 18), (18, 26), (26, 1e9)):
            band = [(h, q) for h, q in mids if lo <= h < hi]
            if not band: continue
            u = unary_union([q.buffer(0.45, join_style=2) for _, q in band]).buffer(-0.3, join_style=2)
            tree = STRtree([q.centroid for _, q in band]); hs = np.array([h for h, _ in band]); ar = np.array([q.area for _, q in band])
            for pg in polys_of(u):
                pg = Polygon(pg.exterior).simplify(0.45)
                if pg.geom_type != 'Polygon' or pg.area < 5 or not pg.is_valid: continue
                ix = [i for i in tree.query(pg) if pg.contains(band[i][1].centroid)]
                blocks.append((float((hs[ix] * ar[ix]).sum() / ar[ix].sum()) if len(ix) else (max(lo, 7) + min(hi, 34)) / 2, pg))
        pal = PALS[region(d['lat'], d['lon'])]
        rec = [struct.pack('<H', len(streets))]
        for cls, p in streets:
            rec.append(struct.pack('<BB', CLS[cls], len(p))); rec.append(np.round(p / Q).astype('<i2').tobytes())
        bigs = []
        for real_h, s in own + blocks:
            pts = list(s.exterior.coords)[:-1]
            if len(pts) > 120: pts = list(s.simplify(1.2).exterior.coords)[:-1]
            if len(pts) < 3 or len(pts) > 200: continue
            tr = ears(pts)
            if not tr: continue
            cols = PAL_TOWER if real_h >= 60 else pal['mid'] if real_h >= 12.5 else pal['lots']
            bigs.append((gh(real_h), cols[rng.randint(len(cols))], pts, tr))
        rec.append(struct.pack('<H', len(bigs)))
        for h, col, pts, tr in bigs:
            rec.append(struct.pack('<BBBBB', int(min(255, round(h * 2))), col[0], col[1], col[2], len(pts))); rec.append(np.round(np.asarray(pts) / Q).astype('<i2').tobytes())
            rec.append(struct.pack('<B', len(tr))); rec.append(np.asarray(tr, np.uint8).tobytes())
        return idx, d['lat'], d['lon'], R, b''.join(rec), (d['name'], streets, bigs) if idx % 577 == 0 or d['pop'] > 6e6 else None
    except Exception as e:
        print('FAILED', f, repr(e)[:200]); return None

if __name__ == '__main__':
    files = sorted(glob.glob(sys.argv[1] + '/[0-9]*.pkl'), key=lambda f: int(os.path.basename(f)[:-4]))
    with Pool(8) as pool: res = [r for r in pool.imap(bake, files, chunksize=16) if r]
    # In parts of at most 18 MB each (towns.bytes, towns_b.bytes, ...): every part is a whole file of its own.
    parts = [[]]; size = 0
    for r in res:
        if size + len(r[4]) + 20 > 18 << 20 and parts[-1]: parts.append([]); size = 0
        parts[-1].append(r); size += len(r[4]) + 20
    stem = sys.argv[2][:-6] if sys.argv[2].endswith('.bytes') else sys.argv[2]; total = 0
    for k, part in enumerate(parts):
        off = 8 + 20 * len(part); index = []; body = []
        for idx, lat, lon, R, rec, pv in part:
            index.append(struct.pack('<ifffi', idx, lat, lon, R, off)); body.append(rec); off += len(rec)
        name = stem + ('' if k == 0 else '_' + chr(97 + k)) + '.bytes'
        with open(name, 'wb') as fo:
            fo.write(b'MNWT'); fo.write(struct.pack('<i', len(part)))
            for e in index: fo.write(e)                # place index, lat, lon, radius, offset of its record
            for r in body: fo.write(r)
            fo.write(struct.pack('<i', off))           # end marker, so the last record has a length too
        total += os.path.getsize(name)
    print(len(files), 'fetched', len(res), 'towns baked', len(parts), 'files', total >> 10, 'KB', flush=True)
    if len(sys.argv) > 3:
        from PIL import Image, ImageDraw
        pv = [r[5] for r in res if r[5]][:15]; W = 360; sh = Image.new('RGB', (W * 5, (W + 24) * 3), (30, 30, 34)); dr = ImageDraw.Draw(sh)
        for i, (name, streets, bigs) in enumerate(pv):
            ox, oy = (i % 5) * W, (i // 5) * (W + 24); ext = max([np.abs(p).max() for _, p in streets] + [1.0]) * 1.05; k = (W - 8) / (2 * ext)
            def pp(x, z): return (ox + W / 2 + x * k, oy + 24 + W / 2 - z * k)
            dr.ellipse([pp(-ext, ext), pp(ext, -ext)], fill=(205, 203, 196)); dr.text((ox + 6, oy + 6), name, fill=(255, 255, 255))
            for c, p in streets: dr.line([pp(*q) for q in p], fill=(70, 72, 80), width=max(1, int(WIDTH[c] * k)))
            for h, col, pts, tr in bigs: dr.polygon([pp(*q) for q in pts], fill=tuple(int(v * min(1.0, 0.6 + h / 60)) for v in col))
        sh.save(sys.argv[3])
