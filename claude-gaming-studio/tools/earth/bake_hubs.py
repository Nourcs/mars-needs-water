# Bakes the world's real airports and sea ports (Natural Earth 10m airports and ports, public domain) into
# Resources/Earth/earth_hubs.bytes for Runtime/World/Hubs.cs.
# usage: python3 bake_hubs.py ne_10m_airports.geojson ne_10m_ports.geojson earth_places.txt out.bytes
# The planet is 255 times smaller than Earth but an airport has to stay big enough to walk around, so an airport
# that would land inside a town is moved straight out along its real bearing until it clears the houses, and where
# several airports of one city would overlap only the most important ones are kept. Ports stay where they are
# (the game finds the nearest shore itself) except inside a rebuilt city core, where they move to its edge.
import json, sys, struct, math, zlib, numpy as np
from scipy.spatial import cKDTree
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
from cities import CITIES
R = 25000.0; FARM = (36.75, -119.77)
def vec(lat, lon):
    la, lo = np.radians(lat), np.radians(lon)
    return np.stack([-np.cos(la) * np.sin(lo), np.sin(la), np.cos(la) * np.cos(lo)], -1)      # EarthData.Dir
def latlon(v):
    v = v / np.linalg.norm(v); return math.degrees(math.asin(max(-1, min(1, v[1])))), math.degrees(math.atan2(-v[0], v[2]))
def frame(p):
    d = p / np.linalg.norm(p); e = np.cross(d, [0, 1, 0]); e /= np.linalg.norm(e); return d, e, np.cross(e, d)
def runway(rank): return 420 if rank <= 3 else 340 if rank <= 5 else 280 if rank <= 7 else 230               # Hubs.cs RunwayFor

towns = []
for raw in open(sys.argv[3], encoding='utf-8'):
    s = raw.strip()
    if not s or s[0] == '#': continue
    parts = s.rsplit(',', 3)
    try: towns.append((float(parts[1]), float(parts[2]), float(parts[3])))
    except Exception: pass
T = np.array(towns); TP = vec(T[:, 0], T[:, 1]) * R; trad = np.clip(22 + np.sqrt(T[:, 2]) * 0.075, 30, 260)   # Places.RadiusFor
# The rebuilt city cores and the farm are obstacles too.
obs = [(TP[i], trad[i]) for i in range(len(TP))]
for c in CITIES.values():
    lo0, la0, lo1, la1 = c['box']; la, lo = c.get('site', ((la0 + la1) / 2, (lo0 + lo1) / 2))
    hw = (lo1 - lo0) * 111320 * math.cos(math.radians((la0 + la1) / 2)) / 5.5 / 2; hd = (la1 - la0) * 110574 / 5.5 / 2
    ap = c.get('apron', (0, 0, 0, 0)); obs.append((vec(la, lo) * R, max(hw + max(ap[0], ap[1]), hd + max(ap[2], ap[3])) + 40))
ncore = len(CITIES)
obs.append((vec(*FARM) * R, 330.0))
OP = np.array([o[0] for o in obs]); OR = np.array([o[1] for o in obs]); otree = cKDTree(OP)

out = []; placed = []                                    # placed airports: (position, clearance)
feats = sorted(json.load(open(sys.argv[1]))['features'], key=lambda f: f['properties'].get('scalerank') or 9)
moved = dropped = 0
for f in feats:
    pr = f['properties']; rank = int(pr.get('scalerank') or 9); lon, lat = f['geometry']['coordinates'][:2]
    real = vec(lat, lon) * R; L = runway(rank); reach = math.hypot(L / 2 + 25, 100)
    # The nearest thing it must stay clear of gives the bearing it is pushed along.
    ii = otree.query_ball_point(real, 900.0); p = real; toward = None
    if ii:
        j = min(ii, key=lambda i: np.linalg.norm(real - OP[i]) - OR[i])
        d, e, n = frame(OP[j]); off = real - OP[j]; x, z = off @ e, off @ n
        if math.hypot(x, z) < 2: a = (zlib.crc32((pr.get('name') or '').encode()) % 360) * math.pi / 180; x, z = math.cos(a), math.sin(a)
        b0 = math.atan2(z, x); ok = False
        for turn in (0, 25, -25, 50, -50, 80, -80, 120, -120, 160, 200):
            b = b0 + math.radians(turn); u = e * math.cos(b) + n * math.sin(b)
            dist = max(math.hypot(off @ e, off @ n), OR[j] + 125)
            p = OP[j] + u * dist; p = p / np.linalg.norm(p) * R
            near = otree.query_ball_point(p, 700.0)
            if any(np.linalg.norm(p - OP[i]) < OR[i] + 105 for i in near if i != j): continue
            if any(np.linalg.norm(p - q) < r + reach + 20 for q, r in placed): continue
            ok = True; toward = OP[j]; break
        if not ok: dropped += 1; continue
        if np.linalg.norm(p - real) > 5: moved += 1
    elif any(np.linalg.norm(p - q) < r + reach + 20 for q, r in placed): dropped += 1; continue
    # The runway lies across the line to the town, terminal side (local +x) facing it.
    d, e, n = frame(p)
    if toward is None: yaw = float(zlib.crc32((pr.get('name') or '').encode()) % 180)
    else:
        t = toward - p; bearing = math.degrees(math.atan2(t @ e, t @ n)); yaw = (bearing - 90) % 360
    la, lo = latlon(p); placed.append((p, reach))
    out.append((0, rank, la, lo, yaw, pr.get('name') or ''))
print('airports', sum(1 for o in out if o[0] == 0), 'moved', moved, 'dropped', dropped)

pk = []; pdrop = 0
for f in sorted(json.load(open(sys.argv[2]))['features'], key=lambda f: f['properties'].get('scalerank') or 9):
    pr = f['properties']; rank = int(pr.get('scalerank') or 9); lon, lat = f['geometry']['coordinates'][:2]; p = vec(lat, lon) * R
    for j in range(len(obs) - ncore - 1, len(obs)):       # out of a city core or the farm, along its bearing
        off = p - OP[j]; dist = np.linalg.norm(off)
        if dist < OR[j] + 30:
            d, e, n = frame(OP[j]); x, z = off @ e, off @ n
            if math.hypot(x, z) < 2: x, z = 1.0, 0.0
            u = (e * x + n * z) / math.hypot(x, z); p = OP[j] + u * (OR[j] + 30); p = p / np.linalg.norm(p) * R
    if any(np.linalg.norm(p - q) < 120 for q in pk) or any(np.linalg.norm(p - q) < r * 0.7 + 50 for q, r in placed): pdrop += 1; continue
    pk.append(p); la, lo = latlon(p); out.append((1, rank, la, lo, 0.0, pr.get('name') or ''))
print('ports', len(pk), 'dropped', pdrop)

with open(sys.argv[4], 'wb') as fo:
    fo.write(b'MNWH'); fo.write(struct.pack('<i', len(out)))
    for kind, rank, la, lo, yaw, name in out:
        nb = name.encode('utf-8')[:255]
        fo.write(struct.pack('<BBfffB', kind, rank, la, lo, yaw, len(nb))); fo.write(nb)
print('hubs', len(out), 'bytes', fo.tell() if not fo.closed else __import__('os').path.getsize(sys.argv[4]))
