# Bakes the public transport of every rebuilt city core into Resources/Transit/<city>.bytes (Runtime/World/Transit.cs):
# real bus stops, bus routes along the real main streets, real tram and light-rail lines with their stops, and real
# metro stations. Data: Overture Maps, (c) OpenStreetMap contributors, Overture Maps Foundation.
# usage: python3 bake_transit.py <cities dir> <out dir> [preview.png]
import sys, os, math, struct, collections, numpy as np, pyarrow.parquet as pq, shapely
from shapely.geometry import LineString, Point, box as sbox
from shapely.strtree import STRtree
from cities import CITIES
SCALE = 5.5
WIDTH = {'motorway': 5.0, 'trunk': 5.0, 'primary': 4.6, 'secondary': 4.2, 'tertiary': 3.8, 'residential': 3.4, 'unclassified': 3.4, 'living_street': 2.8, 'pedestrian': 2.8}
MAJOR = ('trunk', 'primary', 'secondary', 'tertiary')

def chains(lines, through=False):
    """
    Joins lines that meet end to end. Normally only where exactly two meet; with `through`, a line carries straight on
    across a junction into whichever unused line turns least (under 35 degrees), which is how a bus follows an avenue.
    """
    def k(p): return (round(p[0], 2), round(p[1], 2))
    ends = collections.defaultdict(list)
    for i, a in enumerate(lines): ends[k(a[0])].append(i); ends[k(a[-1])].append(i)
    used = [False] * len(lines); out = []
    order = sorted(range(len(lines)), key=lambda i: -np.linalg.norm(lines[i][1:] - lines[i][:-1], axis=1).sum()) if through else range(len(lines))
    for i in order:
        if used[i]: continue
        used[i] = True; ch = [lines[i]]
        for front in (False, True):
            while True:
                tip = ch[0][0] if front else ch[-1][-1]; cand = [j for j in ends[k(tip)] if not used[j]]
                if not through:
                    if len(ends[k(tip)]) != 2 or not cand: break
                    j = cand[0]
                else:
                    come = (ch[0][0] - ch[0][1]) if front else (ch[-1][-1] - ch[-1][-2]); best = None
                    for c in cand:
                        b = lines[c]; go = (b[1] - b[0]) if k(b[0]) == k(tip) else (b[-2] - b[-1])
                        cs = float(come @ go) / (np.linalg.norm(come) * np.linalg.norm(go) + 1e-9)
                        if cs > 0.82 and (best is None or cs > best[0]): best = (cs, c)
                    if best is None: break
                    j = best[1]
                used[j] = True; b = lines[j]
                if front: ch.insert(0, b if k(b[-1]) == k(tip) else b[::-1])
                else: ch.append(b if k(b[0]) == k(tip) else b[::-1])
        out.append(np.vstack([ch[0]] + [c[1:] for c in ch[1:]]))
    return out

def thin(pts, gap):
    kept = []
    for p in pts:
        if all((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 >= gap * gap for q in kept): kept.append(p)
    return kept

def city(name, data, out):
    cfg = CITIES[name]; B = cfg['box']; lon0, lat0 = (B[0] + B[2]) / 2, (B[1] + B[3]) / 2
    kx = 111320 * math.cos(math.radians(lat0)) / SCALE; kz = 110574 / SCALE
    hw, hd = (B[2] - lon0) * kx, (B[3] - lat0) * kz; clip = sbox(-hw + 3, -hd + 3, hw - 3, hd - 3)
    def P(c): return np.column_stack(((c[:, 0] - lon0) * kx, (c[:, 1] - lat0) * kz))
    t = pq.read_table(f'{data}/{name}/roads.parquet', columns=['geometry', 'subtype', 'class'])
    geo = shapely.from_wkb(t.column('geometry').to_pylist()); sub = t.column('subtype').to_pylist(); cls = t.column('class').to_pylist()
    streets = []; major = []; rails = []
    for g, s, c in zip(geo, sub, cls):
        if g is None or g.geom_type != 'LineString': continue
        p = P(np.asarray(g.coords)[:, :2])
        if s == 'road' and c in WIDTH:
            streets.append((c, LineString(p)))
            if c in MAJOR: major.append(p)
        elif s == 'rail' and c in ('tram', 'light_rail'): rails.append(p)
    def inside(chs, least, apart):
        res = []
        for ch in sorted(chs, key=lambda a: -LineString(a).length):
            g = LineString(ch).intersection(clip)
            for part in ([g] if g.geom_type == 'LineString' else list(getattr(g, 'geoms', []))):
                if part.geom_type != 'LineString' or part.length < least: continue
                part = part.simplify(0.3)
                probes = [part.interpolate(f, normalized=True) for f in (0.25, 0.5, 0.75)]
                if any(all(o.distance(q) < apart for q in probes) for o in res): continue      # the other carriageway or track
                res.append(part)
        return res
    bus = inside(chains(major, True), 180, 12)[:8]
    tram = inside(chains(rails), 120, 3)[:10]
    it = pq.read_table(f'{data}/{name}/infra.parquet', columns=['geometry', 'subtype', 'class'])
    ig = shapely.from_wkb(it.column('geometry').to_pylist()); icl = it.column('class').to_pylist()
    pts = collections.defaultdict(list)
    for g, c in zip(ig, icl):
        if g is None or g.is_empty: continue
        ct = g.centroid; x, z = (ct.x - lon0) * kx, (ct.y - lat0) * kz
        if abs(x) < hw - 6 and abs(z) < hd - 6: pts[c].append((x, z))
    stops = []                                              # kind, x, z, yaw
    if streets:
        tree = STRtree([s for _, s in streets])
        for x, z in thin(pts['bus_stop'], 22):
            i = int(tree.nearest(Point(x, z))); c, s = streets[i]
            if s.distance(Point(x, z)) > 14: continue
            on = s.interpolate(s.project(Point(x, z))); dx, dz = x - on.x, z - on.y; d = math.hypot(dx, dz)
            if d < 0.2:
                a = s.interpolate(min(s.project(on) + 1, s.length)); dx, dz = -(a.y - on.y), a.x - on.x; d = math.hypot(dx, dz) or 1
            dx /= d; dz /= d; off = WIDTH[c] / 2 + 0.9
            stops.append((0, on.x + dx * off, on.y + dz * off, math.degrees(math.atan2(-dx, -dz))))   # faces the street
    for x, z in thin(pts['stop_position'] + pts['platform'] + pts['tram_stop'], 25):
        for line in tram:
            if line.distance(Point(x, z)) < 5:
                on = line.interpolate(line.project(Point(x, z))); stops.append((1, on.x, on.y, 0.0)); break
    for x, z in thin(pts['subway_station'], 35): stops.append((2, x, z, 0.0))
    for x, z in thin(pts['railway_station'], 60): stops.append((3, x, z, 0.0))
    with open(f'{out}/{name}.bytes', 'wb') as fo:
        fo.write(b'MNWP'); fo.write(struct.pack('<iH', 1, len(bus) + len(tram)))
        for kind, group in ((0, bus), (1, tram)):
            for line in group:
                c = np.asarray(line.coords, '<f4')[:, :2]; fo.write(struct.pack('<BH', kind, len(c))); fo.write(c.tobytes())
        fo.write(struct.pack('<H', len(stops)))
        for s in stops: fo.write(struct.pack('<Bfff', *s))
    n = collections.Counter(s[0] for s in stops)
    print(f'{name:13s} bus routes {len(bus)} tram lines {len(tram)} bus stops {n[0]} tram stops {n[1]} metro {n[2]} rail stations {n[3]}', flush=True)
    return name, hw, hd, streets, bus, tram, stops

if __name__ == '__main__':
    data, out = sys.argv[1:3]; os.makedirs(out, exist_ok=True); res = [city(n, data, out) for n in CITIES]
    if len(sys.argv) > 3:
        from PIL import Image, ImageDraw
        W = 420; sh = Image.new('RGB', (W * 6, W * 4), (30, 30, 34)); dr = ImageDraw.Draw(sh)
        for i, (name, hw, hd, streets, bus, tram, stops) in enumerate(res):
            ox, oy = (i % 6) * W, (i // 6) * W; k = (W - 16) / (2 * max(hw, hd))
            def pp(x, z): return (ox + W / 2 + x * k, oy + W / 2 - z * k)
            for c, s in streets: dr.line([pp(*q) for q in s.coords], fill=(86, 88, 96), width=1)
            for line in bus: dr.line([pp(*q[:2]) for q in line.coords], fill=(240, 150, 40), width=3)
            for line in tram: dr.line([pp(*q[:2]) for q in line.coords], fill=(90, 200, 255), width=2)
            for kind, x, z, yaw in stops:
                px, pz = pp(x, z); r = 2 if kind < 2 else 5
                dr.ellipse([px - r, pz - r, px + r, pz + r], fill=[(255, 220, 90), (90, 200, 255), (255, 70, 70), (255, 255, 255)][kind])
            dr.text((ox + 8, oy + 6), name, fill=(255, 255, 255))
        sh.save(sys.argv[3])
