# Bakes a real city core into a file the game can build (Runtime/World/CityReplica.cs).
# Data: Overture Maps (buildings with heights, street segments, land, water, parks; (c) OpenStreetMap contributors,
# Overture Maps Foundation), fetched with ov.py. The planet is 255 times smaller than Earth, so a walkable city is
# enlarged: 1 game metre = SCALE real metres across the ground; heights are squeezed less so towers still tower.
# usage: python3 bake_city.py <name> <data dir with roads/buildings/water/land/landuse.parquet> out.bytes preview.png
# The cities and their settings are in cities.py.
import sys, os, struct, math, numpy as np, pyarrow.parquet as pq
from shapely import wkb
from shapely.geometry import box as sbox, LineString, Polygon, Point
from shapely.ops import unary_union
from shapely.strtree import STRtree
from PIL import Image, ImageDraw
from scipy import ndimage
from cities import CITIES, PALS, LANDMARKS
NAME, DATA, OUT, PREVIEW = sys.argv[1:5]
CFG = CITIES[NAME]; PAL = PALS[CFG.get('pal', 'us')]
SCALE = 5.5
BOX = CFG['box']
LON0, LAT0 = (BOX[0] + BOX[2]) / 2, (BOX[1] + BOX[3]) / 2
SITE = CFG.get('site', (LAT0, LON0))
KX = 111320 * math.cos(math.radians(LAT0)) / SCALE; KZ = 110574 / SCALE
def P(lon, lat): return ((lon - LON0) * KX, (lat - LAT0) * KZ)
HW, HD = (BOX[2] - LON0) * KX, (BOX[3] - LAT0) * KZ    # half size in game metres
print(NAME, round(2 * HW), 'x', round(2 * HD), 'game metres')
WIDTH = {'motorway': 5.0, 'trunk': 5.0, 'primary': 4.6, 'secondary': 4.2, 'tertiary': 3.8, 'residential': 3.4, 'unclassified': 3.4, 'living_street': 2.8, 'pedestrian': 2.8}
CLS = {k: i for i, k in enumerate(WIDTH)}
def gh(real):                                           # game height of a building from its real height
    return real * 0.5 if real < 12 else 6 + (real - 12) * 0.25
def rows(kind):
    f = f'{DATA}/{kind}.parquet'
    return pq.read_table(f).to_pylist() if os.path.exists(f) else []
def polys_of(g):
    if g.geom_type == 'Polygon': return [] if g.is_empty else [g]
    return [q for q in getattr(g, 'geoms', []) if q.geom_type == 'Polygon' and not q.is_empty]
def local(pg):
    return Polygon([P(x, y) for x, y in pg.exterior.coords], [[P(x, y) for x, y in r.coords] for r in pg.interiors])
MARKS = []
for lm in LANDMARKS:
    mx, mz = P(lm[2], lm[1])
    if abs(mx) < HW and abs(mz) < HD: MARKS += [(lm[0], mx, mz, lm[3])] + [(lm[0], mx + dx, mz + dz, r) for dx, dz, r in (lm[4] if len(lm) > 4 else [])]
def at_mark(x, z, pad=0.0): return any((x - mx) ** 2 + (z - mz) ** 2 < (r + pad) ** 2 for _, mx, mz, r in MARKS)
print('wonders inside', sorted({m[0] for m in MARKS}))

# ---------------------------------------------------------------- streets
clip = sbox(-HW, -HD, HW, HD); clip_mw = sbox(-HW, -HD, HW + CFG.get('motorway_east', 0), HD)
streets = []
for row in rows('roads'):
    if row['subtype'] != 'road' or row['class'] not in WIDTH: continue
    g = wkb.loads(row['geometry'])
    if g.geom_type != 'LineString': continue
    ls = LineString([P(x, y) for x, y in g.coords]).intersection(clip_mw if row['class'] == 'motorway' else clip)
    for part in ([ls] if ls.geom_type == 'LineString' else list(getattr(ls, 'geoms', []))):
        if part.geom_type != 'LineString' or part.length < 1.0: continue
        part = part.simplify(0.25)
        streets.append((row['class'], np.array(part.coords)))
if not streets: sys.exit('no streets: wrong box or missing data')
for ex in CFG.get('extra', []):                         # roads added by hand ('join' = on to the nearest street corner)
    pts = [q for q in ex if q != 'join']
    if ex[-1] == 'join':
        v = np.vstack([p for c, p in streets if c != 'motorway']); pts.append(tuple(v[np.argmin(((v - np.array(pts[-1])) ** 2).sum(1))]))
    streets.append(('primary', np.array(pts, float)))
print('streets', len(streets), 'km', round(sum(np.linalg.norm(p[1:] - p[:-1], axis=1).sum() for _, p in streets) / 1000, 1))

# ---------------------------------------------------------------- rasters (PX pixels per game metre)
PX = 2; M = 40
RW, RH = int((2 * HW + 2 * M) * PX), int((2 * HD + 2 * M) * PX)
def rp(x, z): return ((x + HW + M) * PX, (HD + M - z) * PX)
def img(): return Image.new('L', (RW, RH), 0)
def fill(d, pg, v):
    d.polygon([rp(*q) for q in pg.exterior.coords], fill=v)
def samp(a, x, z):
    u, v = rp(x, z); u, v = int(u), int(v)
    return a[v, u] if 0 <= u < RW and 0 <= v < RH else 0
im_st = img(); d_st = ImageDraw.Draw(im_st)
for c, p in streets:
    if c == 'motorway': continue
    d_st.line([rp(*q) for q in p], fill=255, width=max(1, int(WIDTH[c] * PX)))
im_low = img(); d_low = ImageDraw.Draw(im_low); im_big = img(); d_big = ImageDraw.Draw(im_big); im_all = img(); d_all = ImageDraw.Draw(im_all)
wide = sbox(-HW - M, -HD - M, HW + M, HD + M)
TALL = CFG.get('tall', 35); BLOCKS = CFG.get('blocks', True); FLOORS = CFG.get('floors', 3); NAMED = CFG.get('named', {})
bigs = []; mids = []; nlow = 0
for row in rows('buildings'):
    for pg in polys_of(wkb.loads(row['geometry'])):
        ext = [P(x, y) for x, y in pg.exterior.coords]
        q = Polygon(ext)
        if not q.is_valid or q.area < 0.3 or not q.intersects(wide): continue
        d_all.polygon([rp(*v) for v in ext], fill=255)
        c = q.centroid
        if not (-HW < c.x < HW and -HD < c.y < HD) or at_mark(c.x, c.y): continue
        real_h = max(3.5, row['height'] or ((row['num_floors'] or (2 if not BLOCKS else FLOORS)) * 3.5))
        area_real = q.area * SCALE * SCALE
        name = ((row['names'] or {}).get('primary') or '')
        if real_h >= TALL or area_real >= 1800 or name in NAMED:
            s = q.simplify(2.0 / SCALE)
            if s.geom_type != 'Polygon' or s.area < 0.5: s = q
            bigs.append((real_h, s, name))
            d_big.polygon([rp(*v) for v in ext], fill=255)
        elif BLOCKS:
            mids.append((max(real_h, 6.5), q))
        else:
            nlow += 1
            d_low.polygon([rp(*v) for v in ext], fill=int(min(255, max(1, real_h * 8))))
# Mid-rise buildings that touch are merged into city blocks, one height band at a time, so a street of six-storey
# houses becomes one long block with the real outline instead of hundreds of separate boxes.
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
        h = float((hs[ix] * ar[ix]).sum() / ar[ix].sum()) if len(ix) else (max(lo, 7) + min(hi, 34)) / 2
        blocks.append((h, pg, '')); fill(d_big, pg, 255)
print('own buildings', len(bigs), 'blocks', len(blocks), 'low buildings', nlow)
st = np.asarray(im_st) > 0; low = np.asarray(im_low).astype(np.float32) / 8; big = np.asarray(im_big) > 0

# Land. 'built': everything close to a street or a building, closed over parks and squares; the rest is the bay.
# 'osm': the real land outline, less the real rivers, lakes and docks.
if CFG.get('land', 'osm') == 'built':
    built = st | (np.asarray(im_all) > 0)
    land = ndimage.binary_dilation(built, iterations=int(70 / SCALE * PX))
    land = ndimage.binary_erosion(land, iterations=int(58 / SCALE * PX), border_value=1)
    land = ndimage.binary_fill_holes(land)
else:
    im_land = img(); d_land = ImageDraw.Draw(im_land); n = 0
    for row in rows('land'):
        if row['subtype'] != 'land': continue
        for pg in polys_of(wkb.loads(row['geometry'])):
            q = local(pg).buffer(0).intersection(wide)
            for part in polys_of(q):
                fill(d_land, part, 255); n += 1
                for r in part.interiors: d_land.polygon([rp(*v) for v in r.coords], fill=0)
    if n == 0: d_land.rectangle([0, 0, RW, RH], fill=255)
    for row in rows('water'):
        if row['subtype'] in ('human_made', 'physical', 'spring', 'wastewater'): continue
        for pg in polys_of(wkb.loads(row['geometry'])):
            q = local(pg).buffer(0).intersection(wide)
            for part in polys_of(q):
                if part.area < 60: continue             # fountains and garden ponds stay land
                fill(d_land, part, 0)
                for r in part.interiors: d_land.polygon([rp(*v) for v in r.coords], fill=255)   # islands
    land = np.asarray(im_land) > 0
    land = land | (np.asarray(im_all) > 0)             # a building always stands on something
    land = ndimage.binary_opening(land, iterations=2) | (np.asarray(im_all) > 0)
print('land', round(100 * land.mean()), '%')

# Parks and gardens: grass and trees instead of paving.
im_pk = img(); d_pk = ImageDraw.Draw(im_pk)
for kind, ok in (('landuse', lambda r: r['subtype'] in ('park', 'horticulture', 'golf', 'cemetery') or r['class'] in ('grass', 'meadow', 'village_green', 'recreation_ground')),
                 ('land', lambda r: r['subtype'] in ('forest', 'grass', 'shrub', 'wetland'))):
    for row in rows(kind):
        if not ok(row): continue
        for pg in polys_of(wkb.loads(row['geometry'])):
            q = local(pg)
            if q.is_valid and q.area > 12 and q.intersects(wide): fill(d_pk, q, 255)
park = (np.asarray(im_pk) > 0) & land & ~big

# ---------------------------------------------------------------- house lots along the streets
LOT = 5.4; occ = Image.new('L', (RW, RH), 0); d_occ = ImageDraw.Draw(occ); occ_px = occ.load()
lots = []; rng = np.random.RandomState(7); NT = len(PAL['lots'])
order = sorted(range(len(streets)), key=lambda i: -WIDTH[streets[i][0]])
for i in order:
    c, p = streets[i]
    if c == 'motorway': continue
    seg = np.linalg.norm(p[1:] - p[:-1], axis=1); total = seg.sum()
    if total < LOT: continue
    cum = np.concatenate([[0], np.cumsum(seg)])
    n = int(total // (LOT + 0.25)); start = (total - n * (LOT + 0.25)) / 2 + (LOT + 0.25) / 2
    for k in range(n):
        s = start + k * (LOT + 0.25); j = min(len(seg) - 1, int(np.searchsorted(cum, s) - 1)); j = max(j, 0)
        t = (s - cum[j]) / max(seg[j], 1e-6); a = p[j] + (p[j + 1] - p[j]) * t
        f = (p[j + 1] - p[j]) / max(seg[j], 1e-6); nrm = np.array([-f[1], f[0]])
        for side in (-1, 1):
            ctr = a + nrm * side * (WIDTH[c] / 2 + 0.35 + LOT / 2)
            if not (-HW + 3 < ctr[0] < HW - 3 and -HD + 3 < ctr[1] < HD - 3) or at_mark(ctr[0], ctr[1], 3): continue
            pts = [ctr + f * u * LOT * 0.42 + nrm * w * LOT * 0.42 for u in (-1, 0, 1) for w in (-1, 0, 1)]
            if any(samp(st, *q) for q in pts): continue
            if sum(1 for q in pts if samp(big, *q)) > 0: continue
            hs = [samp(low, *q) for q in pts]
            if sum(1 for v in hs if v > 0) < 4: continue          # a house only where the real city has low buildings
            if not all(samp(land, *q) for q in pts): continue
            u0 = [rp(*q) for q in pts]
            if any(occ_px[int(u), int(v)] for u, v in u0 if 0 <= int(u) < RW and 0 <= int(v) < RH): continue
            corners = [ctr + f * u * LOT * 0.5 + nrm * w * LOT * 0.5 for u, w in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
            d_occ.polygon([rp(*q) for q in corners], fill=255)
            hh = float(np.mean([v for v in hs if v > 0]))
            face = -nrm * side                                     # the front door looks at the street
            yaw = math.degrees(math.atan2(face[0], face[1]))       # Unity yaw: 0 = +z (north), 90 = +x (east)
            lots.append((ctr[0], ctr[1], yaw, int(rng.randint(0, 4)), int(rng.randint(0, NT)), float(np.clip(hh / 9.0, 0.85, 1.9))))
print('house lots', len(lots))

# ---------------------------------------------------------------- buildings to extrude: footprints and roof triangles
def ears(pts):
    """Ear clipping for a simple polygon (list of (x, z)); returns index triples, counter-clockwise."""
    n = len(pts); idx = list(range(n))
    area = sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n))
    if area < 0: idx.reverse()
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    out = []; guard = 0
    while len(idx) > 3 and guard < 5000:
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
PAL_TOWER = [(115, 173, 230), (97, 148, 191), (140, 191, 217), (77, 115, 158), (150, 170, 190), (176, 196, 210)]
PAL_LOW = [(226, 222, 212), (206, 200, 190), (220, 208, 186)]
out_big = []
for real_h, s, name in bigs + blocks:
    pts = list(s.exterior.coords)[:-1]
    if len(pts) > 160: pts = list(s.simplify(1.2).exterior.coords)[:-1]
    if len(pts) < 3 or len(pts) > 400: continue
    tr = ears(pts)
    if not tr: continue
    flag = NAMED.get(name, 2 if real_h >= 230 else 0)
    pal = PAL_TOWER if real_h >= 60 else PAL['mid'] if real_h >= 12.5 else PAL['lots'] if BLOCKS else PAL_LOW
    col = pal[rng.randint(len(pal))]
    if 'Transamerica' in name: col = (236, 232, 222)
    if name == 'Coit Tower': col = (238, 234, 224)
    if 'Ferry Building' in name: col = (210, 205, 195)
    out_big.append((gh(real_h), col, flag, pts, tr))

# ---------------------------------------------------------------- ground rasters for the game: land, blend weight, parks
AP = CFG.get('apron', (0, 0, 0, 0)); CELL = 2.0
MW, ME, MS, MN = (60 + a for a in AP)                   # margins west, east, south, north (game metres)
gw, gd = int((2 * HW + MW + ME) / CELL) + 1, int((2 * HD + MS + MN) / CELL) + 1
gx0, gz0 = -HW - MW, -HD - MS
gl = np.zeros((gd, gw), np.uint8); wt = np.zeros((gd, gw), np.uint8); pk = np.zeros((gd, gw), np.uint8)
landf = ndimage.gaussian_filter(land.astype(np.float32), 2.0 * PX)
parkf = ndimage.gaussian_filter(park.astype(np.float32), 1.0 * PX)
for j in range(gd):
    z = gz0 + j * CELL
    for i in range(gw):
        x = gx0 + i * CELL
        inside = -HW <= x <= HW and -HD <= z <= HD
        dw, ds = max(0.0, -HW - x), max(0.0, -HD - z); de, dn = max(0.0, x - HW), max(0.0, z - HD)
        if inside: gl[j, i] = int(255 * samp(landf, x, z)); pk[j, i] = int(255 * samp(parkf, x, z))
        # Beyond the box: open water on a side with an apron, otherwise whatever the edge of the city has.
        elif (dw > 0 and AP[0]) or (de > 0 and AP[1]) or (ds > 0 and AP[2]) or (dn > 0 and AP[3]): gl[j, i] = 0
        else: gl[j, i] = int(255 * samp(landf, min(max(x, -HW + 1), HW - 1), min(max(z, -HD + 1), HD - 1)))
        w = 1.0
        w *= 1 - min(1.0, max(0.0, dw - (MW - 60)) / 50); w *= 1 - min(1.0, max(0.0, ds - (MS - 60)) / 50)
        w *= 1 - min(1.0, max(0.0, de - (ME - 60)) / 50); w *= 1 - min(1.0, max(0.0, dn - (MN - 60)) / 50)
        wt[j, i] = int(255 * w)
hills = list(CFG.get('hills_xz', [])) + [(*P(lon, lat), r0, r1, h) for lat, lon, r0, r1, h in CFG.get('hills', [])]

with open(OUT, 'wb') as fo:
    fo.write(b'MNWC'); fo.write(struct.pack('<i', 2))
    fo.write(struct.pack('<ffffffi', SITE[0], SITE[1], BOX[0], BOX[1], BOX[2], BOX[3], 1 if CFG.get('sea') else 0))
    fo.write(struct.pack('<ffffiiff', HW, HD, SCALE, CELL, gw, gd, gx0, gz0)); fo.write(gl.tobytes()); fo.write(wt.tobytes()); fo.write(pk.tobytes())
    fo.write(struct.pack('<i', len(streets)))
    for c, p in streets:
        fo.write(struct.pack('<BBH', CLS[c], 1 if c == 'motorway' else 0, len(p))); fo.write(np.asarray(p, '<f4').tobytes())
    fo.write(struct.pack('<i', len(out_big)))
    for h, col, flag, pts, tr in out_big:
        fo.write(struct.pack('<fBBBBH', h, col[0], col[1], col[2], flag, len(pts))); fo.write(np.asarray(pts, '<f4').tobytes())
        fo.write(struct.pack('<H', len(tr))); fo.write(np.asarray(tr, '<u2').tobytes())
    fo.write(struct.pack('<i', len(lots)))
    for l in lots: fo.write(struct.pack('<fffBBf', *l))
    fo.write(struct.pack('<i', len(hills)))
    for hl in hills: fo.write(struct.pack('<fffff', *hl))
    fo.write(struct.pack('<i', NT))
    for t in PAL['lots']: fo.write(bytes(t))
ntri = sum(len(tr) * (2 if h > 28 else 1) + 2 * len(pts) * (2 if h > 28 else 1) for h, col, flag, pts, tr in out_big)
print('bytes', os.path.getsize(OUT), 'extruded', len(out_big), 'triangles', ntri)

# ---------------------------------------------------------------- a top-down preview
S = 2; pv = Image.new('RGB', (gw * S, gd * S), (120, 170, 205)); d = ImageDraw.Draw(pv)
def pp(x, z): return ((x - gx0) / CELL * S, (gz0 + gd * CELL - z) / CELL * S)
lm = Image.fromarray(((gl > 127) * 255).astype(np.uint8)).transpose(Image.FLIP_TOP_BOTTOM).resize((gw * S, gd * S))
pv.paste((205, 203, 196), (0, 0), lm)
pm = Image.fromarray(((pk > 127) * 255).astype(np.uint8)).transpose(Image.FLIP_TOP_BOTTOM).resize((gw * S, gd * S))
pv.paste((150, 196, 120), (0, 0), pm)
for c, p in streets: d.line([pp(*q) for q in p], fill=(70, 72, 80) if c != 'motorway' else (40, 40, 46), width=max(1, int(WIDTH[c] / CELL * S)))
for (x, z, yaw, m, t, ys) in lots:
    d.rectangle([pp(x - 2.4, z + 2.4), pp(x + 2.4, z - 2.4)], fill=PAL['lots'][t])
for h, col, flag, pts, tr in sorted(out_big, key=lambda q: q[0]):
    k = min(1.0, 0.55 + h / 90); d.polygon([pp(*q) for q in pts], fill=tuple(int(v * k) for v in col), outline=(60, 60, 70) if h > 20 else None)
for k, mx, mz, r in MARKS: d.ellipse([pp(mx - r, mz + r), pp(mx + r, mz - r)], outline=(220, 40, 40), width=3)
pv.save(PREVIEW)
