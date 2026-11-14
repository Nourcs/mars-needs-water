# Bakes the planet's ground from real data: measured land heights and sea depths (Terrarium elevation tiles: Mapzen /
# AWS open data, built from SRTM, GMTED, ETOPO1 and GEBCO), the real coastline, lakes and glaciers (Natural Earth 10m,
# public domain). Replaces the first bake, which read heights off a picture.
# usage: python3 bake_terrain.py <tiles dir> <natural earth dir> <old Resources/Earth dir> <places.geojson> <out dir>
# Writes: earth_terrain_a/b.bytes  heights, 8192 x 4096 int16 (2 cm steps, game metres = real metres x 0.0235), gzipped
#         earth_lakes.bytes    4096 x 2048 uint8, lake surface in 1.09 m steps (0 = the sea), reaching past each shore
#         earth_ice.bytes      4096 x 2048 uint8, glaciers and ice sheets
#         earth_biome.bytes    2048 x 1024, the old biomes fitted to the new coastline
#         earth_water.bytes + earth_waters.txt   which body of water each 2048 x 1024 cell belongs to (old indices kept)
import sys, os, json, math, gzip, struct, numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
Image.MAX_IMAGE_PIXELS = None
tiles, ne, old, places_f, out = sys.argv[1:6]
W, H = 8192, 4096; LW, LH = 4096, 2048; BW, BH = 2048, 1024
K = 0.0235; STEP = 1.09

# ---- real elevation, mercator tiles -> equirectangular
merc = np.empty((8192, 8192), np.float32)
for x in range(32):
    for y in range(32):
        a = np.asarray(Image.open(f'{tiles}/{x}_{y}.png').convert('RGB')).astype(np.float32)
        merc[y * 256:(y + 1) * 256, x * 256:(x + 1) * 256] = a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768
lat = 90 - (np.arange(H) + 0.5) / H * 180
fy = (1 - np.arcsinh(np.tan(np.radians(np.clip(lat, -85.0, 85.0)))) / math.pi) / 2 * 8192 - 0.5
y0 = np.clip(np.floor(fy).astype(int), 0, 8190); t = np.clip(fy - y0, 0, 1)[:, None].astype(np.float32)
elev = merc[y0] * (1 - t) + merc[y0 + 1] * t
del merc
elev[lat > 85.0] = -3000.0                                   # the Arctic Ocean beyond the tiles
south = lat < -85.0
if south.any(): elev[south] = np.maximum(ndimage.uniform_filter1d(elev[np.argmax(south) - 1], 65, mode='wrap'), 2400.0)[None, :]
print('elevation', elev.min(), elev.max(), flush=True)

# ---- masks from Natural Earth
def px(c, w, h): return ((c[0] + 180) / 360 * w, (90 - c[1]) / 180 * h)
def polys(g): return [g['coordinates']] if g['type'] == 'Polygon' else g['coordinates'] if g['type'] == 'MultiPolygon' else []
def feats(name): return json.load(open(f'{ne}/{name}.geojson'))['features']
def fill(d, p, w, h, v, hole):
    ext = [px(c, w, h) for c in p[0]]
    if len(ext) > 2: d.polygon(ext, fill=v)
    for ring in p[1:]:
        pts = [px(c, w, h) for c in ring]
        if len(pts) > 2: d.polygon(pts, fill=hole)
img = Image.new('L', (W, H), 0); d = ImageDraw.Draw(img)
for name in ('ne_land', 'ne_minor_islands'):
    for f in feats(name):
        for p in polys(f['geometry']): fill(d, p, W, H, 1, 0)
land = np.asarray(img) > 0
land[lat < -85.0] = True

MAXD = {'Lake Baikal': 1642, 'Lake Tanganyika': 1470, 'Lake Malawi': 706, 'Issyk-Kul': 668, 'Great Slave Lake': 614, 'Great Bear Lake': 446,
        'Lake Superior': 406, 'Lake Michigan': 281, 'Lago Titicaca': 281, 'Lake Titicaca': 281, 'Lake Ontario': 244, 'Lake Ladoga': 230,
        'Lake Huron': 229, 'Lake Van': 451, 'Lake Tahoe': 501, 'Lake Kivu': 475, 'Dead Sea': 306, 'Lake Geneva': 310, 'Lake Onega': 127,
        'Vänern': 106, 'Lake Victoria': 84, 'Lake Erie': 64, 'Lake Winnipeg': 36, 'Lake Turkana': 109, 'Lake Albert': 58, 'Lake Nasser': 130,
        'Lake Athabasca': 243, 'Lake Volta': 75, 'Lake Balkhash': 26, 'Lake Chad': 11, 'Great Salt Lake': 10, 'Lake Nicaragua': 26,
        'Lago de Nicaragua': 26, 'Lake Kariba': 97, 'Lake Edward': 112, 'Crater Lake': 594, 'Lake Como': 425, 'Lake Garda': 346,
        'Lake Maggiore': 372, 'Lake Constance': 251, 'Loch Ness': 227, 'Lake Toba': 505, 'Lake Matano': 590, 'Great Lake Tasmania': 20}
lake_names = ['']
limg = Image.new('I', (W, H), 0); d = ImageDraw.Draw(limg)
for name in ('ne_lakes_europe', 'ne_lakes_north_america', 'ne_lakes'):          # the main set last, so it wins
    for f in feats(name):
        pr = f['properties']
        if pr.get('featurecla') == 'Playa': continue
        lake_names.append(pr.get('name') or ''); k = len(lake_names) - 1
        for p in polys(f['geometry']): fill(d, p, W, H, k, 0)
lid = np.asarray(limg).astype(np.int32)
lid[~land] = 0                                               # a "lake" drawn over the sea is the sea
# No lake may reach the farm (the farm digs below its own ground, and that pit must stay dry).
FARM = (36.75, -119.77); fy_, fx_ = int((90 - FARM[0]) / 180 * H), int((FARM[1] + 180) / 360 * W)
for k in np.unique(lid[fy_ - 14:fy_ + 15, fx_ - 14:fx_ + 15]):
    if k: lid[lid == k] = 0; print('dropped a lake beside the farm:', lake_names[k])
lake = lid > 0
print('land', land.mean(), 'lake px', lake.sum(), 'lakes drawn', len(np.unique(lid)) - 1, flush=True)

# ---- heights in game metres
h = np.where(land, np.maximum(elev * K, 0.6), np.minimum(elev * K, -0.7)).astype(np.float32)
# Lakes: the surface is the low side of the rim, in whole 1.09 m steps; the bed is deepest in the middle.
dist = ndimage.distance_transform_edt(lake)
surf = np.zeros((H, W), np.float32)
st8 = np.ones((3, 3), bool)
for k, sl in enumerate(ndimage.find_objects(lid), start=1):
    if sl is None: continue
    sl = tuple(slice(max(a.start - 2, 0), a.stop + 2) for a in sl)
    m = lid[sl] == k
    rim = ndimage.binary_dilation(m, structure=st8) & ~lake[sl] & land[sl]
    top = float(np.percentile(elev[sl][m], 95))               # the lake's own surface (or its shallows, where the data has depths)
    ref = min(float(np.percentile(elev[sl][rim], 10)), top) if rim.any() else top
    lv = max(1.0, round(max(ref, 0.0) * K / STEP))
    surf[sl][m] = lv
    dd = dist[sl][m]; maxd = MAXD.get(lake_names[k], 40.0)
    depth = np.maximum((maxd * np.clip(dd / max(dd.max(), 1.0), 0, 1) ** 0.6 + 2) * K, 1.3)
    h[sl][m] = lv * STEP - depth
# The lake map at half resolution, grown one cell past every shore; the land inside it stands clear of the water.
lo = surf.reshape(LH, 2, LW, 2).max(axis=(1, 3))
grown = ndimage.grey_dilation(lo, size=(3, 3), mode='wrap')
up = np.repeat(np.repeat(grown, 2, axis=0), 2, axis=1)
dry = land & ~lake & (up > 0)
h[dry] = np.maximum(h[dry], up[dry] * STEP + 0.6)
lakes_lo = grown.copy()
wet_sea = (~land).reshape(LH, 2, LW, 2).any(axis=(1, 3))
lakes_lo[wet_sea & (lo == 0)] = 0
up = np.repeat(np.repeat(lakes_lo, 2, axis=0), 2, axis=1)
bad = land & ~lake & (up > 0) & (h < up * STEP + 0.6); h[bad] = up[bad] * STEP + 0.6
print('heights', h.min(), h.max(), 'lake cells', (lakes_lo > 0).sum(), flush=True)

os.makedirs(out, exist_ok=True)
q = np.clip(np.round(h * 25), -32768, 32767).astype(np.int16)
# Each cell is stored as what is left after guessing it from its west, north and north-west neighbours, with all the
# low bytes first and then all the high bytes: that packs to under half the size.
r = q.copy(); r[:, 1:] -= q[:, :-1]
r[1:, :] -= q[:-1, :] - np.concatenate([np.zeros((H - 1, 1), np.int16), q[:-1, :-1]], axis=1)
raw = r.astype('<i2').tobytes(); planes = raw[0::2] + raw[1::2]
blob = b'MNWF' + struct.pack('<iif', W, H, 0.04) + gzip.compress(planes, 9)
PART = 14 << 20                                              # in parts: the game joins earth_terrain_a, _b, ... end to end
for i in range(0, len(blob), PART): open(f'{out}/earth_terrain_{chr(97 + i // PART)}.bytes', 'wb').write(blob[i:i + PART])
np.clip(lakes_lo, 0, 255).astype(np.uint8).tofile(f'{out}/earth_lakes.bytes')
print('terrain bytes', len(blob), flush=True)

# ---- ice: glaciers and ice shelves at full detail, plus whatever the old map called ice
iimg = Image.new('L', (LW, LH), 0); d = ImageDraw.Draw(iimg)
for name in ('ne_glaciated_areas', 'ne_antarctic_ice_shelves_polys'):
    for f in feats(name):
        for p in polys(f['geometry']): fill(d, p, LW, LH, 255, 0)
ice = np.asarray(iimg) > 0
oldice = np.fromfile(f'{old}/earth_ice.bytes', np.uint8)
oldice = oldice.reshape(1024, 2048) if oldice.size == 2048 * 1024 else oldice.reshape(LH, LW)[::2, ::2]
ice |= np.repeat(np.repeat(oldice > 0, 2, axis=0), 2, axis=1)
(ice * 255).astype(np.uint8).tofile(f'{out}/earth_ice.bytes')

# ---- biomes on the new coastline
bio = np.fromfile(f'{old}/earth_biome.bytes', np.uint8).reshape(BH, BW).copy()
water_lo = (~land | lake).reshape(BH, 4, BW, 4).mean(axis=(1, 3)) > 0.5
wasland = bio != 0
idx = ndimage.distance_transform_edt(~wasland, return_distances=False, return_indices=True)
bio = bio[idx[0], idx[1]]                                    # every cell takes the nearest land biome ...
bio[water_lo] = 0                                            # ... and water is water
bio[(ice.reshape(BH, 2, BW, 2).mean(axis=(1, 3)) > 0.5) & ~water_lo] = 1
bio.tofile(f'{out}/earth_biome.bytes')

# ---- water bodies (old indices kept so the simulation's source ids do not move)
bodies = []
for line in open(f'{old}/earth_waters.txt', encoding='utf-8'):
    if line.startswith('#') or not line.strip(): continue
    i, rest = line.rstrip('\n').split(',', 1); nm, L = rest.rsplit(',', 1); bodies.append((nm, float(L)))
def body(name, litres):
    for i, (nm, _) in enumerate(bodies):
        if nm == name: return i + 1
    bodies.append((name, litres)); return len(bodies)
SEAS = {'Pacific Ocean': 7.1e20, 'Atlantic Ocean': 3.1e20, 'Indian Ocean': 2.64e20, 'Southern Ocean': 7.18e19, 'Arctic Ocean': 1.87e19,
        'Mediterranean Sea': 3.75e18, 'Caribbean Sea': 6.86e18, 'Gulf of Mexico': 2.43e18, 'Black Sea': 5.47e17, 'Red Sea': 2.33e17,
        'Baltic Sea': 2.17e16, 'Hudson Bay': 1.2e17, 'Caspian Sea': 7.8e16}
LAKES = {'Lake Baikal': 2.36e16, 'Lake Tanganyika': 1.89e16, 'Lake Superior': 1.21e16, 'Lake Malawi': 8.4e15, 'Lake Michigan': 4.9e15,
         'Lake Huron': 3.54e15, 'Lake Victoria': 2.75e15, 'Great Bear Lake': 2.24e15, 'Great Slave Lake': 2.09e15, 'Issyk-Kul': 1.74e15,
         'Lake Ontario': 1.64e15, 'Lago Titicaca': 8.93e14, 'Lake Ladoga': 8.38e14, 'Lake Erie': 4.8e14, 'Lake Onega': 2.85e14,
         'Lake Winnipeg': 2.84e14, 'Lake Tahoe': 1.51e14, 'Vänern': 1.53e14, 'Lake Turkana': 2.04e14, 'Lake Kivu': 5.0e14,
         'Lake Nasser': 1.32e14, 'Lake Volta': 1.48e14, 'Lake Balkhash': 1.06e14, 'Lake Van': 6.07e14, 'Lake Geneva': 8.9e13,
         'Dead Sea': 1.47e14, 'Great Salt Lake': 1.9e13, 'Lake Chad': 7.2e10, 'Lake Athabasca': 2.04e14, 'Lake Nicaragua': 1.08e14,
         'Lago de Nicaragua': 1.08e14, 'Lake Albert': 2.8e14, 'Lake Edward': 3.95e13, 'Lake Kariba': 1.85e14, 'Bratsk Reservoir': 1.69e14}
def basin(la, lo):
    if 36 < la < 47.5 and 46 < lo < 55.5: return 'Caspian Sea'
    if la < -60: return 'Southern Ocean'
    if 51 < la < 66 and -95 < lo < -75: return 'Hudson Bay'
    if la > 66: return 'Arctic Ocean'
    if 40.5 < la < 47.5 and 27 < lo < 42: return 'Black Sea'
    if 30 < la < 46 and -6 < lo < 36.5 and not (la > 42.5 and lo < 2): return 'Mediterranean Sea'
    if 12 < la < 30.5 and 32 < lo < 44: return 'Red Sea'
    if 53 < la < 66 and 9.5 < lo < 30.5: return 'Baltic Sea'
    if 18 < la < 31 and -98 < lo < -81.2: return 'Gulf of Mexico'
    if 9 < la < 22 and -89 < lo < -60 and not (la < 16 and lo < -77 - (la - 8) * 1.6): return 'Caribbean Sea'
    if lo >= 20:
        if la >= 31: return 'Pacific Ocean' if lo > 100 else 'Atlantic Ocean'
        if lo < 100: return 'Indian Ocean'
        if la > -10: return 'Pacific Ocean'
        return 'Indian Ocean' if (lo < 130 or (la < -30 and lo < 147)) else 'Pacific Ocean'
    edge = -70 if la < 8 else -77 - (la - 8) * 1.6 if la < 18 else -100
    return 'Atlantic Ocean' if lo > edge else 'Pacific Ocean'
blat = 90 - (np.arange(BH) + 0.5) / BH * 180; blon = -180 + (np.arange(BW) + 0.5) / BW * 360
cell_km2 = (40075 / BW) * (20004 / BH) * np.cos(np.radians(blat))
ids = np.zeros((BH, BW), np.uint16)
sea_lo = (~land).reshape(BH, 4, BW, 4).mean(axis=(1, 3)) >= 0.5
cache = {}
for y, x in zip(*np.nonzero(sea_lo)):
    key = (int(blat[y] * 2), int(blon[x] * 2))
    if key not in cache: nm = basin(blat[y], blon[x]); cache[key] = body(nm, SEAS[nm])
    ids[y, x] = cache[key]
pl = [(f['properties'].get('NAME') or f['properties'].get('name'), f['geometry']['coordinates']) for f in json.load(open(places_f))['features']]
pc = np.array([c for _, c in pl]); pn = [p for p, _ in pl]
lid_lo = lid.reshape(BH, 4, BW, 4).max(axis=(1, 3))          # a lake smaller than a cell still claims it
for k in np.unique(lid_lo):
    if k == 0: continue
    m = lid_lo == k; ys, xs = np.nonzero(m); nm = lake_names[k]
    area = float((lid == k).sum()) / 16 * cell_km2[int(ys.mean())]
    if not nm:
        la, lo_ = blat[int(ys.mean())], blon[int(xs.mean())]
        dd = (pc[:, 1] - la) ** 2 + ((pc[:, 0] - lo_) * math.cos(math.radians(la))) ** 2
        near = pn[int(dd.argmin())]; nm = near if near.startswith('Lake') else f'Lake {near}'
    ids[m] = body(nm.replace(',', ' '), LAKES.get(nm, max(area, 1.0) * 1e6 * 15 * 1000))
ids.tofile(f'{out}/earth_water.bytes')
with open(f'{out}/earth_waters.txt', 'w', encoding='utf-8') as fo:
    fo.write('# index,name,litres  (index matches earth_water.bytes; oceans and big lakes use real volumes, others area x mean depth)\n')
    for i, (nm, L) in enumerate(bodies, start=1): fo.write(f'{i},{nm},{L:.4g}\n')
print('bodies', len(bodies), flush=True)

# ---- checks and a picture
def at(la, lo_): return h[int((90 - la) / 180 * H), int((lo_ + 180) / 360 * W)]
for nm, la, lo_ in [('Everest', 27.988, 86.925), ('Fresno', 36.75, -119.77), ('Denver', 39.74, -104.99), ('Mont Blanc', 45.833, 6.865), ('Tahoe', 39.09, -120.03),
                    ('Titicaca', -15.9, -69.4), ('Baikal', 53.5, 108.2), ('Mariana', 11.35, 142.2), ('Dead Sea', 31.5, 35.48), ('Amsterdam', 52.37, 4.9), ('mid Atlantic', 30, -40)]:
    y, x = int((90 - la) / 180 * H), int((lo_ + 180) / 360 * W)
    print(f'{nm:12s} game {at(la, lo_):8.2f} m  real {elev[y, x]:8.0f} m  lake level {lakes_lo[y // 2, x // 2] * STEP:6.2f}')
s = h[::4, ::4]; lk = np.repeat(np.repeat(lakes_lo, 2, 0), 2, 1)[::4, ::4]; water = (s < 0) | ((lk > 0) & (s < lk * STEP))
rgb = np.zeros(s.shape + (3,), np.float32)
tland = np.clip(s / 120, 0, 1)[..., None]; rgb[:] = np.array([96, 150, 84]) * (1 - tland) + np.array([238, 232, 220]) * tland
shade = np.clip(1 + (np.roll(s, 1, 1) - np.roll(s, -1, 1)) * 0.06, 0.6, 1.4)[..., None]; rgb *= shade
tsea = np.clip(-s / 150, 0, 1)[..., None]; rgb[water] = (np.array([120, 190, 230]) * (1 - tsea) + np.array([16, 50, 120]) * tsea)[water]
rgb[water & (s >= 0)] = [60, 150, 235]
rgb[(ice[::2, ::2]) & ~water] = [245, 248, 252]
Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)).save(f'{out}/preview.png')
