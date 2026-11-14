#!/usr/bin/env python3
"""Compare the mini Earth's data files with reference data about the real Earth.
usage: compare_earth.py <Assets/Game/Data/Resources/Earth> <reference folder> <out.json>
Reference folder: Natural Earth geojson (land, minor islands, lakes*, glaciers, rivers*, places, countries)."""
import sys, os, json, gzip, struct, math, glob
import numpy as np, shapely
from shapely.geometry import shape, Point
from shapely.strtree import STRtree

earth, ref, out = sys.argv[1:4]
SCALE = 0.0235          # game metres per real metre of height
res = {}

# ---- game data
blob = open(f'{earth}/earth_terrain_a.bytes', 'rb').read() + open(f'{earth}/earth_terrain_b.bytes', 'rb').read()
assert blob[:4] == b'MNWF'
W, H, step = struct.unpack('<iif', blob[4:16])
planes = gzip.decompress(blob[16:])
r = (np.frombuffer(planes[:W * H], np.uint8).astype(np.uint16) | (np.frombuffer(planes[W * H:], np.uint8).astype(np.uint16) << 8)).astype(np.int16).reshape(H, W)
terr = np.cumsum(np.cumsum(r.astype(np.int64), axis=0), axis=1).astype(np.int16).astype(np.float32) * step   # game metres
def grid(a, lat, lon):
    h, w = a.shape
    x = np.clip(((np.asarray(lon) + 180.0) / 360.0 * w).astype(int), 0, w - 1)
    y = np.clip(((90.0 - np.asarray(lat)) / 180.0 * h).astype(int), 0, h - 1)
    return a[y, x]
lakes = np.fromfile(f'{earth}/earth_lakes.bytes', np.uint8); lakes = lakes.reshape(-1, int(round(math.sqrt(lakes.size * 2))))
ice = np.fromfile(f'{earth}/earth_ice.bytes', np.uint8); ice = ice.reshape(-1, int(round(math.sqrt(ice.size * 2))))
towns = []
for line in open(f'{earth}/earth_places.txt', encoding='utf8'):
    if line.startswith('#') or not line.strip(): continue
    p = line.rstrip('\n').rsplit(',', 3); towns.append((p[0], float(p[1]), float(p[2]), float(p[3])))
tl = np.array([[t[1], t[2]] for t in towns])

def load(name):
    return [f for f in json.load(open(f'{ref}/{name}', encoding='utf8'))['features'] if f.get('geometry')]

# ---- 1. heights at known places (real metres above sea level)
known = [('La Paz', -16.4897, -68.1193, 3640), ('Lhasa', 29.65, 91.1, 3650), ('Quito', -0.2295, -78.5243, 2850), ('Bogota', 4.711, -74.0721, 2640),
         ('Addis Ababa', 9.03, 38.74, 2355), ('Mexico City', 19.4326, -99.1332, 2240), ('Kabul', 34.5553, 69.2075, 1790), ('Nairobi', -1.2921, 36.8219, 1795),
         ('Johannesburg', -26.2041, 28.0473, 1753), ('Denver', 39.7392, -104.9903, 1609), ('Tehran', 35.6892, 51.389, 1190), ('Madrid', 40.4168, -3.7038, 667),
         ('Ulaanbaatar', 47.8864, 106.9057, 1350), ('Kathmandu', 27.7172, 85.324, 1400), ('Salt Lake City', 40.7608, -111.891, 1288), ('Calgary', 51.0447, -114.0719, 1045),
         ('Munich', 48.1351, 11.582, 520), ('Sao Paulo', -23.5505, -46.6333, 760), ('Brasilia', -15.7939, -47.8828, 1172), ('Ankara', 39.9334, 32.8597, 938),
         ('Windhoek', -22.5609, 17.0658, 1655), ('Canberra', -35.2809, 149.13, 578), ('Alice Springs', -23.698, 133.8807, 545), ('Fresno', 36.7378, -119.7871, 94),
         ('Moscow', 55.7558, 37.6173, 156), ('Delhi', 28.6139, 77.209, 216), ('Paris', 48.8566, 2.3522, 35), ('Cairo', 30.0444, 31.2357, 23)]
peaks = [('Mount Everest', 27.9881, 86.925, 8849), ('Aconcagua', -32.6532, -70.0109, 6961), ('Denali', 63.0692, -151.007, 6190), ('Kilimanjaro', -3.0674, 37.3556, 5895),
         ('Mount Elbrus', 43.3499, 42.4453, 5642), ('Mont Blanc', 45.8326, 6.8652, 4806), ('Mount Fuji', 35.3606, 138.7274, 3776), ('Mauna Kea', 19.8207, -155.4681, 4207),
         ('Mount Whitney', 36.5786, -118.292, 4421), ('Vinson Massif', -78.5254, -85.6171, 4892), ('Puncak Jaya', -4.0833, 137.1833, 4884), ('Aoraki Mount Cook', -43.595, 170.1418, 3724)]
deeps = [('Challenger Deep', 11.373, 142.591, -10920), ('Puerto Rico Trench', 19.77, -66.5, -8380), ('Dead Sea shore', 31.5, 35.5, -430), ('Death Valley', 36.2399, -116.8258, -86)]
def near_max(lat, lon, cells=2, lowest=False):
    x = int((lon + 180) / 360 * W); y = int((90 - lat) / 180 * H)
    win = terr[max(0, y - cells):y + cells + 1, max(0, x - cells):x + cells + 1]
    return float(win.min() if lowest else win.max()) / SCALE
rows = [(n, real, float(grid(terr, la, lo)) / SCALE) for n, la, lo, real in known]
res['cities'] = [{'name': n, 'real': real, 'game': round(g)} for n, real, g in rows]
err = np.array([abs(g - real) for _, real, g in rows])
res['city_height_median_error_m'] = float(np.median(err)); res['city_height_worst_error_m'] = float(err.max())
res['peaks'] = [{'name': n, 'real': real, 'game': round(near_max(la, lo))} for n, la, lo, real in peaks]
res['deeps'] = [{'name': n, 'real': real, 'game': round(near_max(la, lo, 2, True))} for n, la, lo, real in deeps]

# ---- 2. land and sea, at random spots spread evenly over the globe
rng = np.random.default_rng(7)
N = 120000
z = rng.uniform(-1, 1, N); lon = rng.uniform(-180, 180, N); lat = np.degrees(np.arcsin(z))
land = [shape(f['geometry']) for f in load('ne_land.geojson') + load('ne_minor_islands.geojson')]
tree = STRtree(land)
pts = shapely.points(lon, lat)
hit = np.zeros(N, bool); hit[tree.query(pts, predicate='intersects')[0]] = True
game_land = grid(terr, lat, lon) > 0
keep = lat > -60                       # Antarctica's ice shelves are drawn differently in every data set
res['land_sea'] = {'samples': int(keep.sum()), 'agree_pct': float((hit == game_land)[keep].mean() * 100),
                   'real_land_game_sea_pct': float((hit & ~game_land)[keep].mean() * 100), 'real_sea_game_land_pct': float((~hit & game_land)[keep].mean() * 100),
                   'real_land_pct': float(hit.mean() * 100), 'game_land_pct': float(game_land.mean() * 100)}

# ---- 3. towns
places = load('places.geojson')
pl = np.array([[f['properties']['latitude'], f['properties']['longitude']] for f in places])
def km(a, b):
    la1, lo1 = np.radians(a[:, None, 0]), np.radians(a[:, None, 1]); la2, lo2 = np.radians(b[None, :, 0]), np.radians(b[None, :, 1])
    return 6371 * 2 * np.arcsin(np.sqrt(np.sin((la2 - la1) / 2) ** 2 + np.cos(la1) * np.cos(la2) * np.sin((lo2 - lo1) / 2) ** 2))
dmin = np.concatenate([km(pl[i:i + 500], tl).min(axis=1) for i in range(0, len(pl), 500)])
miss = [(places[i]['properties']['name'], int(places[i]['properties']['pop_max'])) for i in np.where(dmin > 15)[0]]
miss.sort(key=lambda m: -m[1])
wet_towns = [towns[i][0] for i in np.where(grid(terr, tl[:, 0], tl[:, 1]) <= 0)[0]]
res['towns'] = {'reference_places': len(places), 'game_towns': len(towns), 'reference_found_pct': float((dmin <= 15).mean() * 100),
                'missing': len(miss), 'largest_missing': miss[:12], 'towns_in_the_sea': len(wet_towns), 'towns_in_the_sea_examples': wet_towns[:12]}
caps = [f for f in places if f['properties'].get('adm0cap') == 1]
cd = np.array([dmin[places.index(f)] for f in caps])
res['capitals'] = {'reference': len(caps), 'found': int((cd <= 15).sum()), 'missing': [f['properties']['name'] for f, d in zip(caps, cd) if d > 15]}

# ---- 4. countries: does each one have a town?
countries = load('countries.geojson')
cg = [shape(f['geometry']) for f in countries]
ct = STRtree(cg)
pi, gi = ct.query(shapely.points(tl[:, 1], tl[:, 0]), predicate='intersects')
count = np.bincount(gi, minlength=len(cg))
empty = sorted(((countries[i]['properties'].get('NAME') or countries[i]['properties'].get('name'), int(countries[i]['properties'].get('POP_EST') or 0)) for i in np.where(count == 0)[0]), key=lambda m: -m[1])
res['countries'] = {'reference': len(cg), 'with_a_town': int((count > 0).sum()), 'without': [e[0] for e in empty]}

# ---- 5. lakes and glaciers
lk = []
for name in ('ne_lakes.geojson', 'ne_lakes_europe.geojson', 'ne_lakes_north_america.geojson'):
    if os.path.exists(f'{ref}/{name}'): lk += load(name)
def lake_at(g):
    p = g.representative_point(); h, w = lakes.shape
    x = int((p.x + 180) / 360 * w); y = int((90 - p.y) / 180 * h)
    return lakes[max(0, y - 1):y + 2, max(0, x - 1):x + 2].max() > 0
lg = [shape(f['geometry']) for f in lk]
found = np.array([lake_at(g) for g in lg])
area = np.array([g.area * 111.0 * 111.0 * math.cos(math.radians(g.representative_point().y)) for g in lg])      # rough km2
res['lakes'] = {'reference': len(lg), 'found_pct': float(found.mean() * 100), 'over_100km2': int((area > 100).sum()),
                'over_100km2_found_pct': float(found[area > 100].mean() * 100),
                'largest_missing': [lk[i]['properties'].get('name') for i in np.argsort(-area) if not found[i]][:10]}
levels = [('Lake Titicaca', -15.9, -69.4, 3812), ('Lake Baikal', 53.5, 108.2, 455), ('Lake Victoria', -1.0, 33.0, 1135), ('Lake Superior', 47.7, -87.5, 183),
          ('Lake Tahoe', 39.09, -120.03, 1897), ('Lake Geneva', 46.45, 6.55, 372), ('Lake Tanganyika', -6.0, 29.5, 773), ('Great Salt Lake', 41.1, -112.5, 1280),
          ('Issyk-Kul', 42.45, 77.25, 1607), ('Lake Constance', 47.6, 9.4, 395)]
res['lake_levels'] = [{'name': n, 'real': real, 'game': round(float(grid(lakes, la, lo)) * 1.09 / SCALE)} for n, la, lo, real in levels]
gl = [shape(f['geometry']) for f in load('ne_glaciated_areas.geojson')]
gp = [g.representative_point() for g in gl]
res['glaciers'] = {'reference': len(gl), 'found_pct': float(np.mean([grid(ice, p.y, p.x) > 100 for p in gp]) * 100)}

# ---- 6. rivers: length in the reference
def length_km(g):
    tot = 0.0
    for part in (g.geoms if hasattr(g, 'geoms') else [g]):
        c = np.array(part.coords)[:, :2]
        la1, lo1, la2, lo2 = map(np.radians, (c[:-1, 1], c[:-1, 0], c[1:, 1], c[1:, 0]))
        tot += float((6371 * 2 * np.arcsin(np.sqrt(np.sin((la2 - la1) / 2) ** 2 + np.cos(la1) * np.cos(la2) * np.sin((lo2 - lo1) / 2) ** 2))).sum())
    return tot
rv = 0.0
for name in ('ne_rivers_lake_centerlines.geojson', 'ne_rivers_europe.geojson', 'ne_rivers_north_america.geojson'):
    if os.path.exists(f'{ref}/{name}'): rv += sum(length_km(shape(f['geometry'])) for f in load(name))
res['rivers_reference_km'] = round(rv)
json.dump(res, open(out, 'w'), indent=1, ensure_ascii=False)
print(json.dumps(res, indent=1, ensure_ascii=False))
