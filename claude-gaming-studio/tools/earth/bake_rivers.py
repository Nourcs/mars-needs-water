# Bakes the world's real rivers (Natural Earth 10m river centre lines, plus the denser Europe and North America sets;
# public domain) into Resources/Earth/earth_rivers.bytes for Runtime/World/Rivers.cs.
# usage: python3 bake_rivers.py <natural earth dir> <Resources/Earth dir> out.bytes [preview.png]
# Every point carries the height of the water there: the floor of the valley the river runs in, read from the baked
# terrain, so a river can never stand above its banks. Rivers stop where they reach a lake or the sea, bend round the
# farm, and are cut into chunks the game can stream.
import sys, json, struct, gzip, numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree
R = 25000.0; FARM = (36.75, -119.77); FARM_R = 200.0; STEP = 1.09
ne, earth, outf = sys.argv[1:4]
def vec(lat, lon):
    la, lo = np.radians(lat), np.radians(lon)
    return np.stack([np.cos(la) * np.cos(lo), np.sin(la), np.cos(la) * np.sin(lo)], -1)
def latlon(v):
    v = v / np.linalg.norm(v, axis=-1, keepdims=True)
    return np.degrees(np.arcsin(np.clip(v[..., 1], -1, 1))), np.degrees(np.arctan2(v[..., 2], v[..., 0]))
def dp(P, tol):
    keep = np.zeros(len(P), bool); keep[0] = keep[-1] = True; st = [(0, len(P) - 1)]
    while st:
        a, b = st.pop()
        if b <= a + 1: continue
        ab = P[b] - P[a]; l = np.linalg.norm(ab); q = P[a + 1:b] - P[a]
        d = np.linalg.norm(q, axis=1) if l < 1e-9 else np.linalg.norm(np.cross(q, ab / l), axis=1)
        i = int(np.argmax(d))
        if d[i] > tol: keep[a + 1 + i] = True; st += [(a, a + 1 + i), (a + 1 + i, b)]
    return P[keep]
def smooth(P, step=12.0):
    if len(P) < 3:
        n = max(1, int(np.ceil(np.linalg.norm(P[-1] - P[0]) / step)))
        return P[0] + (P[-1] - P[0]) * np.linspace(0, 1, n + 1)[:, None]
    Q = np.vstack([2 * P[0] - P[1], P, 2 * P[-1] - P[-2]]); out = [P[0]]
    for i in range(len(P) - 1):
        p0, p1, p2, p3 = Q[i], Q[i + 1], Q[i + 2], Q[i + 3]
        n = max(1, int(np.ceil(np.linalg.norm(p2 - p1) / step)))
        for t in np.linspace(0, 1, n + 1)[1:]:
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    return np.array(out)
def onsphere(P): return P / np.linalg.norm(P, axis=1, keepdims=True) * R
def fill(P, most=25.0):
    out = [P[0]]
    for a, b in zip(P[:-1], P[1:]):
        n = max(1, int(np.ceil(np.linalg.norm(b - a) / most)))
        for t in np.linspace(0, 1, n + 1)[1:]: out.append(a + (b - a) * t)
    return np.array(out)

def resample(P, step):
    seg = np.linalg.norm(P[1:] - P[:-1], axis=1); cum = np.concatenate([[0], np.cumsum(seg)])
    n = max(2, int(round(cum[-1] / step)) + 1); d = np.linspace(0, cum[-1], n)
    return onsphere(np.stack([np.interp(d, cum, P[:, j]) for j in range(3)], 1))
def declutter(lines, rank, width=12.0, run=30.0, least=30.0):
    """The three river maps overlap, and a tributary can run beside its main river: wherever a river runs alongside
    a more important one (closer than `width`, heading the same way) for more than `run` metres, that stretch is
    taken out, so no two rivers lie on top of each other."""
    S = []; ids = []; tans = []; spans = []
    for i, (cls, P) in enumerate(lines):
        Q = resample(P, 5.0); t = np.gradient(Q, axis=0); t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
        spans.append((len(ids), len(ids) + len(Q))); S.append(Q); tans.append(t); ids += [i] * len(Q)
    S = np.vstack(S); tans = np.vstack(tans); ids = np.array(ids); rk = np.array(rank)[ids]
    pairs = cKDTree(S).query_pairs(width, output_type='ndarray')
    a, b = pairs[:, 0], pairs[:, 1]; k = (ids[a] != ids[b]) & (np.abs((tans[a] * tans[b]).sum(1)) > 0.7); a, b = a[k], b[k]
    over = np.zeros(len(S), bool); lose_a = rk[a] > rk[b]; over[a[lose_a]] = True; over[b[~lose_a]] = True
    out = []; cut = 0
    for i, (cls, P) in enumerate(lines):
        s0, s1 = spans[i]; o = over[s0:s1].copy()
        if not o.any(): out.append((cls, P)); continue
        n = len(o); j = 0; need = int(run / 5.0)
        while j < n:
            if not o[j]: j += 1; continue
            e = j
            while e < n and o[e]: e += 1
            if e - j <= need: o[j:e] = False
            j = e
        if not o.any(): out.append((cls, P)); continue
        cut += 1; Q = S[s0:s1]; j = 0
        while j < n:
            if o[j]: j += 1; continue
            e = j
            while e < n and not o[e]: e += 1
            if (e - j - 1) * 5.0 >= least: out.append((cls, Q[j:e].copy()))
            j = e
    print('overlapping rivers: cut', cut, 'of', len(lines), '->', len(out), flush=True)
    return out

# The baked terrain and lake map.
blob = b''.join(open(f'{earth}/earth_terrain_{c}.bytes', 'rb').read() for c in 'abcdefgh' if __import__('os').path.exists(f'{earth}/earth_terrain_{c}.bytes'))
W, H, st = struct.unpack('<iif', blob[4:16]); p = gzip.decompress(blob[16:]); n = W * H
a = np.empty(n * 2, np.uint8); a[0::2] = np.frombuffer(p[:n], np.uint8); a[1::2] = np.frombuffer(p[n:], np.uint8)
h = np.cumsum(np.cumsum(a.view('<i2').reshape(H, W).astype(np.int32), axis=1), axis=0).astype(np.int16).astype(np.float32) * st
lakes = np.fromfile(f'{earth}/earth_lakes.bytes', np.uint8); LW = 4096 if lakes.size == 4096 * 2048 else 2048; lakes = lakes.reshape(LW // 2, LW)
valley = ndimage.gaussian_filter(ndimage.minimum_filter(h, size=3, mode='wrap'), 1.2, mode='wrap')
def sample(img, lat, lon):
    hh, ww = img.shape; fx = (lon + 180) / 360 * ww - 0.5; fy = np.clip((90 - lat) / 180 * hh - 0.5, 0, hh - 1.001)
    x0 = np.floor(fx).astype(int); y0 = np.floor(fy).astype(int); tx = fx - x0; ty = fy - y0
    xa = x0 % ww; xb = (x0 + 1) % ww
    return (img[y0, xa] * (1 - tx) + img[y0, xb] * tx) * (1 - ty) + (img[y0 + 1, xa] * (1 - tx) + img[y0 + 1, xb] * tx) * ty
def lake_at(lat, lon):
    x = np.floor((lon + 180) / 360 * LW).astype(int) % LW; y = np.clip(np.floor((90 - lat) / 180 * (LW // 2)).astype(int), 0, LW // 2 - 1)
    return lakes[y, x] * STEP

farm = vec(*FARM) * R
cand = []; seen = set()
for name in ('ne_rivers_lake_centerlines', 'ne_rivers_europe', 'ne_rivers_north_america'):
    for f in json.load(open(f'{ne}/{name}.geojson'))['features']:
        pr = f['properties']; g = f['geometry']
        if g is None or 'Lake' in (pr.get('featurecla') or ''): continue
        rank = pr.get('scalerank'); rank = 9 if rank is None else float(rank)
        cls = 0 if rank <= 3 else 1 if rank <= 5 else 2 if rank <= 7 else 3 if rank <= 9 else 4
        for ls in ([g['coordinates']] if g['type'] == 'LineString' else g['coordinates']):
            c = np.array(ls, float)[:, :2]
            if len(c) < 2: continue
            key = (round(c[0, 0], 3), round(c[0, 1], 3), round(c[-1, 0], 3), round(c[-1, 1], 3), len(c))
            if key in seen: continue
            seen.add(key)
            P = vec(c[:, 1], c[:, 0]) * R
            k = np.ones(len(P), bool); k[1:] = np.linalg.norm(P[1:] - P[:-1], axis=1) > 0.5; P = P[k]
            if len(P) < 2: continue
            P = onsphere(smooth(dp(P, 1.5)))
            if np.linalg.norm(P[1:] - P[:-1], axis=1).sum() > 30: cand.append((cls, P))
order = sorted(range(len(cand)), key=lambda i: (cand[i][0], -np.linalg.norm(cand[i][1][1:] - cand[i][1][:-1], axis=1).sum()))
rank = np.empty(len(cand), int); rank[order] = np.arange(len(cand))
cand = declutter(cand, rank)

def to_valley(P):
    """A river runs along the bottom of its valley. The river map and the terrain disagree by a few dozen metres, which
    would leave rivers running along hillsides; each point moves sideways (up to 40 m) to the lowest ground there."""
    Q = resample(P, 10.0)
    if len(Q) < 3: return Q
    t = np.gradient(Q, axis=0); t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
    sd = np.cross(Q / R, t); sd /= np.maximum(np.linalg.norm(sd, axis=1, keepdims=True), 1e-9)
    offs = np.arange(-40.0, 40.1, 8.0); best = np.full(len(Q), np.inf); pick = np.zeros(len(Q))
    for o in offs:
        la, lo = latlon(Q + sd * o); g = sample(h, la, lo) + abs(o) * 0.03
        better = g < best; best[better] = g[better]; pick[better] = o
    pick[0] = pick[-1] = 0.0
    pick = ndimage.uniform_filter1d(pick, 5, mode='nearest')          # neighbours move together: no zigzag
    Q = onsphere(Q + sd * pick[:, None])
    for it in range(3): Q[1:-1] = 0.25 * Q[:-2] + 0.5 * Q[1:-1] + 0.25 * Q[2:]
    return onsphere(Q)

lines = []
for cls, P in cand:
            P = to_valley(P)
            d = P - farm; dist = np.linalg.norm(d, axis=1); near = dist < FARM_R
            if near.any():
                d[near] *= (FARM_R / np.maximum(dist[near], 1.0))[:, None]; P[near] = farm + d[near]; P = onsphere(P)
            P = onsphere(fill(dp(P, 0.35)))
            la, lo = latlon(P); ground = sample(h, la, lo); wl = lake_at(la, lo)
            surf = np.maximum(sample(valley, la, lo) - 0.35, wl + 0.1)
            wet = ground < np.maximum(wl, 0.0) - 0.05
            i = 0
            while i < len(P):                                    # the dry runs, each reaching one point into the water it meets
                if wet[i]: i += 1; continue
                j = i
                while j + 1 < len(P) and not wet[j + 1]: j += 1
                a0, b0 = max(i - 1, 0), min(j + 1, len(P) - 1)
                seg = P[a0:b0 + 1]
                if len(seg) >= 2 and np.linalg.norm(seg[1:] - seg[:-1], axis=1).sum() > 30:
                    s = surf[a0:b0 + 1].copy()
                    s = np.maximum(ndimage.uniform_filter1d(s, 5, mode='nearest'), wl[a0:b0 + 1] + 0.1)
                    lines.append((cls, seg, la[a0:b0 + 1], lo[a0:b0 + 1], s))
                i = j + 1
print('rivers', len(lines), 'points', sum(len(l[1]) for l in lines), 'km', sum(np.linalg.norm(l[1][1:] - l[1][:-1], axis=1).sum() for l in lines) / 1000,
      'by class', np.bincount([l[0] for l in lines], minlength=5))

chunks = []; pts = []
for cls, P, la, lo, s in lines:
    i = 0
    while i < len(P) - 1:
        j = i + 1; ln = 0.0
        while j < len(P) - 1 and j - i < 19 and ln + np.linalg.norm(P[j] - P[j - 1]) < 500: ln += np.linalg.norm(P[j] - P[j - 1]); j += 1
        seg = P[i:j + 1]; c = seg.mean(0); c = c / np.linalg.norm(c) * R
        clat, clon = latlon(c)
        chunks.append((len(pts), len(seg), cls, (1 if i == 0 else 0) | (2 if j == len(P) - 1 else 0), float(clat), float(clon), float(np.linalg.norm(seg - c, axis=1).max())))
        pts += list(zip(la[i:j + 1], lo[i:j + 1], s[i:j + 1]))
        i = j
with open(outf, 'wb') as fo:
    fo.write(b'MNWV'); fo.write(struct.pack('<iii', 1, len(chunks), len(pts)))
    for c in chunks: fo.write(struct.pack('<iHBBfff', *c))
    fo.write(np.array(pts, '<f4').tobytes())
print('chunks', len(chunks), 'points', len(pts), 'bytes', 16 + len(chunks) * 20 + len(pts) * 12)
if len(sys.argv) > 4:
    from PIL import Image, ImageDraw
    Wp = 2400; im = Image.new('RGB', (Wp, Wp // 2), (236, 234, 226)); d = ImageDraw.Draw(im)
    s4 = h[::4, ::4]; lk = np.repeat(np.repeat(lakes, 2, 0), 2, 1)[::4, ::4] if LW == 4096 else np.repeat(np.repeat(lakes, 4, 0), 4, 1)[::4, ::4]
    water = (s4 < 0) | ((lk > 0) & (s4 < lk * STEP))
    base = np.array(im.resize((s4.shape[1], s4.shape[0]))); base[water] = (150, 196, 232); im = Image.fromarray(base).resize((Wp, Wp // 2)); d = ImageDraw.Draw(im)
    for cls, P, la, lo, s in lines:
        xy = [((x + 180) / 360 * Wp, (90 - y) / 180 * Wp / 2) for y, x in zip(la, lo)]
        if abs(xy[0][0] - xy[-1][0]) < Wp / 2: d.line(xy, fill=(40, 100, 200), width=2 if cls <= 1 else 1)
    im.save(sys.argv[4])
