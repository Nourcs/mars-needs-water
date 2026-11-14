# Bakes the world's real roads into Resources/Earth/earth_roads.bytes: either every motorway, trunk and primary road
# from Overture Maps (a folder of shards from fetch_world_roads.py; © OpenStreetMap contributors, Overture Maps
# Foundation) or the much sparser Natural Earth 10m roads (a geojson file, public domain).
# usage: python3 bake_roads.py <shards folder | ne_10m_roads.geojson> earth_places.txt Places.cs out.bytes [ne_10m_railroads.geojson]
# Roads are smoothed into curves, bent round the farm, cut into chunks the game can stream, and every town or
# landmark that no real road reaches gets a short link road to the nearest one. With the railroads file, the real
# railways are added as class 3 and a station as class 4 wherever a line passes a town.
import json, sys, struct, re, numpy as np
from scipy.spatial import cKDTree
R = 25000.0; FARM = (36.75, -119.77); FARM_R = 135.0
def vec(lat, lon):
    la, lo = np.radians(lat), np.radians(lon)
    return np.stack([np.cos(la) * np.cos(lo), np.sin(la), np.cos(la) * np.sin(lo)], -1)   # only used for distances here
def latlon(v):
    v = v / np.linalg.norm(v, axis=-1, keepdims=True)
    return np.degrees(np.arcsin(np.clip(v[..., 1], -1, 1))), np.degrees(np.arctan2(v[..., 2], v[..., 0]))
def dp(P, tol):
    """Douglas-Peucker on 3D points (metres)."""
    keep = np.zeros(len(P), bool); keep[0] = keep[-1] = True; st = [(0, len(P) - 1)]
    while st:
        a, b = st.pop()
        if b <= a + 1: continue
        ab = P[b] - P[a]; l = np.linalg.norm(ab)
        q = P[a + 1:b] - P[a]
        d = np.linalg.norm(q, axis=1) if l < 1e-9 else np.linalg.norm(np.cross(q, ab / l), axis=1)
        i = int(np.argmax(d))
        if d[i] > tol: keep[a + 1 + i] = True; st += [(a, a + 1 + i), (a + 1 + i, b)]
    return P[keep]
def smooth(P, step=12.0):
    """Catmull-Rom through the points, sampled every `step` metres."""
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

def natural_earth(path):
    """Natural Earth roads: (class, lon/lat points) per line."""
    out = []
    for f in json.load(open(path))['features']:
        ty = f['properties'].get('type') or ''
        if 'Ferry' in ty: continue
        cls = 0 if ty in ('Major Highway', 'Beltway', 'Bypass') or (f['properties'].get('expressway') == 1) else 1
        g = f['geometry']
        for ls in ([g['coordinates']] if g['type'] == 'LineString' else g['coordinates']):
            a = np.array(ls, float)[:, :2]
            if len(a) >= 2: out.append((cls, a))
    return out

def overture(folder):
    """
    Every motorway, trunk and primary road on Earth (shards from fetch_world_roads.py). The map has them as millions of
    short pieces, both carriageways of a motorway, and far more primary roads than a planet 255 times smaller has
    room for. So: pieces are joined end to end into long roads; then, region by region and most important first, a
    road is kept only where no kept road already runs alongside it (within 8 m for highways, 55 m for primary
    roads), and only if it is a long, continuous road. Crossing another road never cuts a road.
    """
    import glob, pickle, collections
    bins = collections.defaultdict(list)
    for fn in sorted(glob.glob(folder + '/*.pkl')):
        for cls, a in pickle.load(open(fn, 'rb')):
            m = a[len(a) // 2]; bins[(int(m[0] // 10), int(m[1] // 10))].append((cls, a))
    out = []; GRID = (8.0, 55.0); kept_km = [0.0, 0.0]
    def k(p): return (round(float(p[0]), 5), round(float(p[1]), 5))
    for key in sorted(bins):
        segs = bins[key]; occ = [set(), set()]
        # Join pieces that meet end to end, whatever their class (a highway changes class along its way); at a
        # junction a road carries straight on into the piece that turns least. Longest pieces lead.
        ends = collections.defaultdict(list)
        for i, (c, a) in enumerate(segs): ends[k(a[0])].append(i); ends[k(a[-1])].append(i)
        used = [False] * len(segs); chains = []
        # Roundabouts and split carriageways break a road without ending it: a road that stops dead also looks a
        # little way ahead (about 300 real metres) for a piece that carries on in the same direction.
        nearby = collections.defaultdict(list)
        for i, (c, a) in enumerate(segs):
            for e in (0, -1): nearby[(int(a[e][0] // 0.004), int(a[e][1] // 0.004))].append((i, e))
        start = sorted(range(len(segs)), key=lambda i: (segs[i][0], -len(segs[i][1])))
        for i in start:
            if used[i]: continue
            used[i] = True; chain = [segs[i][1]]; cl = [segs[i][0]]
            for front in (False, True):
                while True:
                    tip = chain[0][0] if front else chain[-1][-1]
                    come = (chain[0][0] - chain[0][1]) if front else (chain[-1][-1] - chain[-1][-2]); best = None
                    for c in ends[k(tip)]:
                        if used[c]: continue
                        b = segs[c][1]; go = (b[1] - b[0]) if k(b[0]) == k(tip) else (b[-2] - b[-1])
                        cs = float(come @ go) / (float(np.linalg.norm(come)) * float(np.linalg.norm(go)) + 1e-12)
                        if cs > 0.8 and (best is None or cs > best[0]): best = (cs, c)
                    jump = None
                    if best is None:
                        cx, cy = int(tip[0] // 0.004), int(tip[1] // 0.004); cl_ = np.cos(np.radians(float(tip[1])))
                        cn = come / (float(np.linalg.norm(come)) + 1e-12)
                        for gx in (cx - 1, cx, cx + 1):
                            for gy in (cy - 1, cy, cy + 1):
                                for c, e in nearby[(gx, gy)]:
                                    if used[c]: continue
                                    b = segs[c][1]; gap = b[e] - tip; dist = float(np.hypot(gap[0] * cl_, gap[1]))
                                    if dist > 0.003 or dist < 1e-7: continue
                                    go = (b[1] - b[0]) if e == 0 else (b[-2] - b[-1])
                                    cs = float(cn @ go) / (float(np.linalg.norm(go)) + 1e-12)
                                    ahead = float(cn @ gap) / (float(np.linalg.norm(gap)) + 1e-12)
                                    if cs > 0.75 and ahead > 0.6:
                                        sc = cs + ahead - dist * 200
                                        if jump is None or sc > jump[0]: jump = (sc, c, e)
                        if jump is None: break
                        j = jump[1]; used[j] = True; b = segs[j][1]; cl.append(segs[j][0])
                        b = b if jump[2] == 0 else b[::-1]
                        if front: chain.insert(0, np.vstack([b[::-1], tip[None]]))
                        else: chain.append(np.vstack([tip[None], b]))
                        continue
                    j = best[1]; used[j] = True; b = segs[j][1]; cl.append(segs[j][0])
                    if front: chain.insert(0, b if k(b[-1]) == k(tip) else b[::-1])
                    else: chain.append(b if k(b[0]) == k(tip) else b[::-1])
            pts = np.vstack([chain[0]] + [c[1:] for c in chain[1:]])
            chains.append((0 if sum(1 for c in cl if c < 2) * 2 >= len(cl) else 1, pts))
        P3 = [vec(c[:, 1].astype(float), c[:, 0].astype(float)) * R for _, c in chains]
        length = [np.linalg.norm(p[1:] - p[:-1], axis=1).sum() for p in P3]
        for ci in sorted(range(len(chains)), key=lambda i: (chains[i][0], -length[i])):
            cls = chains[ci][0]; P = P3[ci]; seg = np.linalg.norm(P[1:] - P[:-1], axis=1); total = seg.sum()
            least = 30 if cls == 0 else 60
            if total < least: continue
            g = GRID[cls]; step = min(g / 2, 6.0)
            cum = np.concatenate([[0], np.cumsum(seg)]); n = max(2, int(total / step) + 1); d = np.linspace(0, total, n)
            S = np.stack([np.interp(d, cum, P[:, j]) for j in range(3)], 1)
            cells = np.floor(S / g).astype(np.int64); keys = (cells[:, 0] + 8192) << 42 | (cells[:, 1] + 8192) << 21 | (cells[:, 2] + 8192)
            blocked = np.fromiter((kk in occ[cls] for kk in keys.tolist()), bool, len(keys))
            # Short blocked stretches are crossings: they stay. Long ones run beside a kept road: they go.
            keep = ~blocked; i = 0; cross = int(2.5 * g / step) + 1
            while i < n:
                if keep[i]: i += 1; continue
                j = i
                while j < n and not keep[j]: j += 1
                if j - i <= cross: keep[i:j] = True
                i = j
            i = 0
            while i < n:
                if not keep[i]: i += 1; continue
                j = i
                while j < n and keep[j]: j += 1
                if d[j - 1] - d[i] >= least:
                    lo, hi = d[i], d[j - 1]; inner = (cum > lo) & (cum < hi)
                    Q = np.vstack([S[i][None], P[inner], S[j - 1][None]])
                    la, lon_ = latlon(Q); out.append((cls, np.column_stack([lon_, la])))
                    kept_km[cls] += (hi - lo) / 1000
                    for q, gq in enumerate(GRID):
                        cq = np.floor(S[i:j] / gq).astype(np.int64)
                        occ[q].update(((cq[:, 0] + 8192) << 42 | (cq[:, 1] + 8192) << 21 | (cq[:, 2] + 8192)).tolist())
                i = j
    # Last, loose ends: a short road that joins nothing at one end (or a longer one that joins nothing at all) goes,
    # so what is left is a network and not a scatter of pieces.
    from scipy.spatial import cKDTree
    for rnd in range(3):
        P3 = [vec(q[:, 1].astype(float), q[:, 0].astype(float)) * R for _, q in out]
        samp = []; ids = []
        for i, P in enumerate(P3):
            seg = np.linalg.norm(P[1:] - P[:-1], axis=1); cum = np.concatenate([[0], np.cumsum(seg)])
            d = np.linspace(0, cum[-1], max(2, int(cum[-1] / 10) + 1))
            samp.append(np.stack([np.interp(d, cum, P[:, j]) for j in range(3)], 1)); ids += [i] * len(d)
        ids = np.array(ids); tree = cKDTree(np.vstack(samp)); keep = []
        for i, P in enumerate(P3):
            L = float(np.linalg.norm(P[1:] - P[:-1], axis=1).sum())
            free = sum(1 for e in (P[0], P[-1]) if not np.any(ids[tree.query_ball_point(e, 28.0)] != i))
            cls = out[i][0]
            drop = (free == 2 and L < (150 if cls == 0 else 1500)) or (free == 1 and L < (60 if cls == 0 else 300))
            keep.append(not drop)
        gone = len(out) - sum(keep); out = [o for o, kp in zip(out, keep) if kp]
        print('loose ends dropped', gone, flush=True)
        if gone == 0: break
    print('overture roads kept, game km: highways %.0f primary roads %.0f' % tuple(kept_km), flush=True)
    return out

def around(P, c, rad):
    """A line that would cross the farm goes round it instead: each stretch inside the circle becomes an arc on the
    side the line was already on (pushing points straight out would send a line through the middle)."""
    d = P - c; dist = np.linalg.norm(d, axis=1); near = dist < rad
    if not near.any(): return P
    up = c / np.linalg.norm(c); out = []; i = 0; n = len(P)
    while i < n:
        if not near[i]: out.append(P[i]); i += 1; continue
        j = i
        while j < n and near[j]: j += 1
        a = d[i - 1] if i > 0 else d[i]; b = d[j] if j < n else d[j - 1]
        m = d[i:j].mean(axis=0); m = m - up * (m @ up)
        if np.linalg.norm(m) < 0.05 * rad: m = np.cross(up, b - a)
        if np.linalg.norm(m) < 1e-6: m = np.cross(up, a)
        us = [v / np.linalg.norm(v) for v in (a - up * (a @ up), m, b - up * (b @ up))]
        for u0, u1 in ((us[0], us[1]), (us[1], us[2])):
            ang = np.arccos(np.clip(u0 @ u1, -1, 1)); steps = max(1, int(ang * rad / 8))
            for t in range(steps + 1):
                v = u0 * (1 - t / steps) + u1 * (t / steps); nv = np.linalg.norm(v)
                if nv > 1e-6: out.append(c + v / nv * rad)
        i = j
    return onsphere(np.array(out))

def resample(P, step):
    seg = np.linalg.norm(P[1:] - P[:-1], axis=1); cum = np.concatenate([[0], np.cumsum(seg)])
    n = max(2, int(round(cum[-1] / step)) + 1); d = np.linspace(0, cum[-1], n)
    return onsphere(np.stack([np.interp(d, cum, P[:, j]) for j in range(3)], 1))
def turn_angles(P):
    v = P[1:] - P[:-1]; u = v / np.maximum(np.linalg.norm(v, axis=1), 1e-9)[:, None]
    return np.arccos(np.clip((u[:-1] * u[1:]).sum(1), -1, 1))
def soften(P, deg=15.0):
    """No hard turns. The line is laid out again at one point every 4 m and eased wherever it turns more than `deg`
    degrees at a point (15 degrees is a curve about 15 m in radius; spikes and zigzags smaller than that melt away),
    then thinned again, never back into a corner. The two ends stay where they are, so joins stay joined."""
    if len(P) < 3: return P
    if np.linalg.norm(P[1:] - P[:-1], axis=1).sum() < 12: return P[[0, -1]]      # too short to bend at all
    Q = resample(P, 4.0); lim = np.radians(deg)
    for it in range(400):
        if len(Q) < 3: break
        bad = turn_angles(Q) > lim
        if not bad.any(): break
        m = np.zeros(len(Q), bool); m[1:-1] = bad; m[2:-1] |= bad[:-1]; m[1:-2] |= bad[1:]
        A = Q.copy(); A[1:-1] = 0.25 * Q[:-2] + 0.5 * Q[1:-1] + 0.25 * Q[2:]
        Q[m] = A[m]; Q = onsphere(Q)
        if it % 25 == 24: Q = resample(Q, 4.0)
    for tol in (0.25, 0.1, 0.0):
        T = dp(Q, tol) if tol > 0 else Q
        if len(T) < 3 or turn_angles(T).max() <= np.radians(deg * 2.0): break
    k = np.ones(len(T), bool); k[1:] = np.linalg.norm(T[1:] - T[:-1], axis=1) > 0.6
    if not k[-1]: k[-1] = True; k[-2] = False
    return T[k]

def declutter(lines, rank, width=7.0, run=20.0, least=25.0, tracks=18.0):
    """No two lines on top of each other. Every line is walked 4 m at a time; wherever it runs alongside a more
    important line (closer than `width`, heading the same way) for more than `run` metres, that stretch is taken
    out, and what is left of it ends beside the other line, like a road joining another. Crossings stay.
    A railway is never cut for a road (it keeps to one side of it instead, see roadfix.keep_side), only for another
    railway, and two tracks side by side within `tracks` metres become one."""
    S = []; ids = []; tans = []; spans = []
    for i, (cls, P) in enumerate(lines):
        Q = resample(P, 4.0); t = np.gradient(Q, axis=0); t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
        spans.append((len(ids), len(ids) + len(Q))); S.append(Q); tans.append(t); ids += [i] * len(Q)
    S = np.vstack(S); tans = np.vstack(tans); ids = np.array(ids); rk = np.array(rank)[ids]
    israil = np.array([c == 3 for c, _ in lines])[ids]
    pairs = cKDTree(S).query_pairs(width, output_type='ndarray')
    a, b = pairs[:, 0], pairs[:, 1]; k = (ids[a] != ids[b]) & (israil[a] == israil[b]); a, b = a[k], b[k]
    ri = np.nonzero(israil)[0]
    if len(ri):
        rp = cKDTree(S[ri]).query_pairs(tracks, output_type='ndarray'); ra, rb = ri[rp[:, 0]], ri[rp[:, 1]]; k = ids[ra] != ids[rb]
        a = np.concatenate([a, ra[k]]); b = np.concatenate([b, rb[k]])
    k = np.abs((tans[a] * tans[b]).sum(1)) > 0.8; a, b = a[k], b[k]
    over = np.zeros(len(S), bool); lose_a = rk[a] > rk[b]; over[a[lose_a]] = True; over[b[~lose_a]] = True
    partner = np.full(len(S), -1); partner[a[lose_a]] = b[lose_a]; partner[b[~lose_a]] = a[~lose_a]
    out = []; cut = 0
    for i, (cls, P) in enumerate(lines):
        s0, s1 = spans[i]; o = over[s0:s1].copy()
        if not o.any(): out.append((cls, P)); continue
        n = len(o); j = 0; need = int(run / 4.0)
        while j < n:                                  # short overlaps are crossings: they stay
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
            if (e - j - 1) * 4.0 >= least:
                piece = [Q[j:e]]
                # where it was cut it runs on to the centre of the line it joins: a real junction, not a gap
                if j > 0 and partner[s0 + j - 1] >= 0: piece.insert(0, S[partner[s0 + j - 1]][None])
                if e < n and partner[s0 + e] >= 0: piece.append(S[partner[s0 + e]][None])
                out.append((cls, np.vstack(piece)))
            j = e
    print('overlaps: lines cut', cut, 'of', len(lines), '->', len(out), flush=True)
    return out

import os
# The game's own ground and water (the baked terrain and lake map beside the places file), so a line knows where the
# shore is exactly as the game draws it.
import gzip
from scipy import ndimage
EARTH = os.path.dirname(os.path.abspath(sys.argv[2]))
def load_water():
    blob = b''.join(open(f'{EARTH}/earth_terrain_{c}.bytes', 'rb').read() for c in 'abcdefgh' if os.path.exists(f'{EARTH}/earth_terrain_{c}.bytes'))
    W, H, st = struct.unpack('<iif', blob[4:16]); p = gzip.decompress(blob[16:]); n = W * H
    a = np.empty(n * 2, np.uint8); a[0::2] = np.frombuffer(p[:n], np.uint8); a[1::2] = np.frombuffer(p[n:], np.uint8)
    h = np.cumsum(np.cumsum(a.view('<i2').reshape(H, W).astype(np.int32), axis=1), axis=0).astype(np.int16).astype(np.float32) * st
    lakes = np.fromfile(f'{EARTH}/earth_lakes.bytes', np.uint8); LW = 4096 if lakes.size == 4096 * 2048 else 2048
    lakes = lakes.reshape(LW // 2, LW).astype(np.float32) * 1.09
    return h, lakes
TERRAIN, LAKES = load_water()
def ground_at(lat, lon):
    hh, ww = TERRAIN.shape; fx = (lon + 180) / 360 * ww - 0.5; fy = np.clip((90 - lat) / 180 * hh - 0.5, 0, hh - 1.001)
    x0 = np.floor(fx).astype(int); y0 = np.floor(fy).astype(int); tx = fx - x0; ty = fy - y0; xa = x0 % ww; xb = (x0 + 1) % ww
    return (TERRAIN[y0, xa] * (1 - tx) + TERRAIN[y0, xb] * tx) * (1 - ty) + (TERRAIN[y0 + 1, xa] * (1 - tx) + TERRAIN[y0 + 1, xb] * tx) * ty
def lake_at(lat, lon):
    hh, ww = LAKES.shape
    return LAKES[np.clip(np.floor((90 - lat) / 180 * hh).astype(int), 0, hh - 1), np.floor((lon + 180) / 360 * ww).astype(int) % ww]
def is_wet(P):
    la, lo = latlon(P); return ground_at(la, lo) < np.maximum(lake_at(la, lo), 0.0) + 0.5
# For every cell of the terrain that is water: the nearest cell that is dry land.
def _nearest_dry():
    hh, ww = TERRAIN.shape
    lk = np.repeat(np.repeat(LAKES, hh // LAKES.shape[0], 0), ww // LAKES.shape[1], 1)
    wet = TERRAIN < np.maximum(lk, 0.0) + 0.9          # a little inland, so a moved line stands on firm ground
    idx = ndimage.distance_transform_edt(wet, return_distances=False, return_indices=True)
    return idx[0].astype(np.int16), idx[1].astype(np.int16)
DRY_Y, DRY_X = _nearest_dry()
def ashore(P, reach=70.0, bridge=100.0):
    """Lines keep to the land. The map the lines come from and the game's coast differ by a few dozen metres, which
    would put a coast road out in the sea on an endless bridge. So: a stretch over water shorter than `bridge` is a
    bridge and stays; a longer one is moved to the nearest shore if that is within `reach`; where it is not, the line
    is cut and each part ends on the shore; a line never ends out in the water. Returns the pieces."""
    P = resample(P, 5.0); wet = is_wet(P)
    if not wet.any(): return [P]
    hh, ww = TERRAIN.shape; la, lo = latlon(P)
    cy = np.clip(np.floor((90 - la) / 180 * hh).astype(int), 0, hh - 1); cx = np.floor((lo + 180) / 360 * ww).astype(int) % ww
    ty = DRY_Y[cy, cx].astype(int); tx = DRY_X[cy, cx].astype(int)
    T = vec(90 - (ty + 0.5) / hh * 180, (tx + 0.5) / ww * 360 - 180) * R; far = np.linalg.norm(T - P, axis=1)
    n = len(P); cut = np.zeros(n, bool); i = 0
    while i < n:
        if not wet[i]: i += 1; continue
        j = i
        while j < n and wet[j]: j += 1
        at_end = i == 0 or j == n
        if at_end: cut[i:j] = True                                    # never end in the water
        elif (j - i) * 5.0 > bridge:
            ok = far[i:j] <= reach
            P[i:j][ok] = T[i:j][ok]; cut[i:j] = ~ok
        i = j
    out = []; i = 0
    while i < n:
        if cut[i]: i += 1; continue
        j = i
        while j < n and not cut[j]: j += 1
        if j - i >= 2:
            Q = P[i:j]; k = np.ones(len(Q), bool); k[1:] = np.linalg.norm(Q[1:] - Q[:-1], axis=1) > 0.6
            if k.sum() >= 2: out.append(onsphere(Q[k]))
        i = j
    return out

def overture_cached(folder):
    # Joining the shards takes about nine minutes; the result is kept beside them and reused until a shard changes.
    import glob, pickle
    cache = folder.rstrip('/') + '_joined.pkl'
    if os.path.exists(cache) and os.path.getmtime(cache) > max(os.path.getmtime(f) for f in glob.glob(folder + '/*.pkl')):
        return pickle.load(open(cache, 'rb'))
    out = overture(folder); pickle.dump(out, open(cache, 'wb')); return out
raw_lines = overture_cached(sys.argv[1]) if os.path.isdir(sys.argv[1]) else natural_earth(sys.argv[1])
farm = vec(*FARM) * R
lines = []   # (class, points in metres on the sphere)
for cls, a in raw_lines:
    if True:
        if True:
            if len(a) < 2: continue
            P = vec(a[:, 1], a[:, 0]) * R
            k = np.ones(len(P), bool); k[1:] = np.linalg.norm(P[1:] - P[:-1], axis=1) > 0.5
            P = P[k]
            if len(P) < 2: continue
            P = dp(P, 3.0)                    # wiggles under 3 m (750 real metres) are not worth their corners
            P = onsphere(smooth(P))
            for P in ashore(P):
                P = around(P, farm, FARM_R)       # bend round the farm
                P = soften(P)
                if len(P) >= 2 and np.linalg.norm(P[1:] - P[:-1], axis=1).sum() > 20: lines.append((cls, P))
# A ring road round the farm: every road that would have crossed the farm meets it, instead of bending round the farm
# on its own and stopping.
_up = farm / np.linalg.norm(farm); _e = np.cross([0, 0, 1.0], _up); _e /= np.linalg.norm(_e); _n = np.cross(_up, _e)
_a = np.linspace(0, 2 * np.pi, 121)[:, None]
lines.append((1, onsphere(farm + (_e * np.cos(_a) + _n * np.sin(_a)) * FARM_R)))
def is_ring(P): return len(P) > 20 and np.abs(np.linalg.norm(P - farm, axis=1) - FARM_R).max() < 4.0
print('roads', len(lines), 'points', sum(len(p) for _, p in lines), 'km', sum(np.linalg.norm(p[1:] - p[:-1], axis=1).sum() for _, p in lines) / 1000)

# Link roads: towns and landmarks that no road reaches.
allp = np.vstack([p for _, p in lines]); tree = cKDTree(allp)
towns = []
for raw in open(sys.argv[2], encoding='utf-8'):
    s = raw.strip()
    if not s or s[0] == '#': continue
    parts = s.rsplit(',', 3)
    try: towns.append((float(parts[1]), float(parts[2]), float(parts[3])))
    except Exception: pass
T = np.array(towns); TP = vec(T[:, 0], T[:, 1]) * R; trad = np.clip(22 + np.sqrt(T[:, 2]) * 0.075, 30, 260)   # Places.RadiusFor
ttree = cKDTree(TP); links = 0
linked = set()
def link(a, b):
    global links
    if np.linalg.norm(a - farm) < FARM_R + 40 or np.linalg.norm(b - farm) < FARM_R + 40: return
    key = tuple(sorted((tuple(np.round(a, 0)), tuple(np.round(b, 0)))))
    if key in linked: return                      # the same link, asked for from the other end
    linked.add(key)
    P = onsphere(smooth(np.array([a, b]), 40.0))
    if is_wet(resample(P, 5.0)).mean() > 0.2: return   # a link across water is no link
    lines.append((2, P)); links += 1
dd, ii = tree.query(TP)
for i in range(len(TP)):
    if dd[i] <= max(6.0, trad[i] * 0.3): continue
    if dd[i] < 900: link(TP[i], allp[ii[i]]); continue
    d2, j2 = ttree.query(TP[i], k=2)
    if d2[1] < 1800: link(TP[i], TP[j2[1]])
for m in re.finditer(r'Add\(Kind\.\w+,\s*"[^"]*",\s*"[^"]*",\s*(-?[\d.]+)f,\s*(-?[\d.]+)f', open(sys.argv[3], encoding='utf-8').read()):
    L = vec(float(m.group(1)), float(m.group(2))) * R
    d, i = tree.query(L); d2, j = ttree.query(L)
    tgt = allp[i] if d < d2 + 200 else TP[j]
    if 45 < np.linalg.norm(tgt - L) < 2500: link(L + (tgt - L) / np.linalg.norm(tgt - L) * 38, tgt)
print('links', links)
def tidy(lines):
    # Most important first: highways, roads, links, then railways (a track gives way, a road is never cut for one);
    # longer before shorter.
    pr = {0: 0, 1: 1, 2: 2, 3: 3}
    order = sorted(range(len(lines)), key=lambda i: (0 if is_ring(lines[i][1]) else 1, pr[lines[i][0]], -np.linalg.norm(lines[i][1][1:] - lines[i][1][:-1], axis=1).sum()))
    rank = np.empty(len(lines), int); rank[order] = np.arange(len(lines))
    return [(c, soften(P, 9.0 if c == 3 else 15.0) if len(P) > 2 else P) for c, P in declutter(lines, rank)]
lines = tidy(lines)

# No road stops in the middle of nowhere. An end is fine beside another road, in a town, at a shore or by the farm.
# Any other end is carried on, in a gentle curve, to the nearest road or town ahead of it (in reality a lesser road
# carries on there); a short stub with nowhere to go is dropped.
def lead_on(lines, reach=500.0, stub=250.0):
    S = []; ids = []
    for i, (c, P) in enumerate(lines):
        Q = resample(P, 5.0); S.append(Q); ids += [i] * len(Q)
    S = np.vstack(S); ids = np.array(ids); tr = cKDTree(S)
    out = []; made = 0; dropped = 0; left = 0
    for i, (c, P) in enumerate(lines):
        length = np.linalg.norm(P[1:] - P[:-1], axis=1).sum(); dead = 0; extra = []
        for E, B in ((P[0], P[min(len(P) - 1, 2)]), (P[-1], P[max(0, len(P) - 3)])):
            t = E - B; t /= max(np.linalg.norm(t), 1e-9)
            near = [j for j in tr.query_ball_point(E, 12.0) if ids[j] != i]
            if near: continue
            d, j = ttree.query(E)
            if d < trad[j] + 30: continue
            if is_ring(P): continue
            if is_wet(onsphere(np.array([E + t * 15, E + t * 30, E + t * 45]))).any(): continue
            done = False
            for ahead, far in ((0.5, reach), (0.0, reach * 1.6)):      # straight ahead first, then anywhere in front
                best = None
                for j in tr.query_ball_point(E, far):
                    if ids[j] == i: continue
                    v = S[j] - E; dist = np.linalg.norm(v)
                    if dist < 12 or (v / dist) @ t < ahead: continue
                    if best is None or dist < best[0]: best = (dist, S[j])
                for j in ttree.query_ball_point(E, far):
                    v = TP[j] - E; dist = np.linalg.norm(v)
                    if dist < 1 or (v / dist) @ t < ahead: continue
                    if best is None or dist < best[0]: best = (dist, TP[j])
                if best is None: continue
                dist, tgt = best; ctl = E + t * min(dist * 0.5, 80.0); u = np.linspace(0, 1, max(4, int(dist / 8)))[:, None]
                L = onsphere((1 - u) ** 2 * E + 2 * (1 - u) * u * ctl + u ** 2 * tgt)
                if is_wet(resample(L, 5.0)).mean() < 0.1 and np.linalg.norm(L - farm, axis=1).min() > FARM_R - 4: extra.append((2, L)); made += 1; done = True; break
            if done: continue
            dead += 1
        if dead and length < stub: dropped += 1; continue
        left += dead
        out.append((c, P)); out += extra
    print('dead ends: carried on', made, 'stubs dropped', dropped, 'left as they are', left, flush=True)
    return out
lines = lead_on(lines)
hw = np.vstack([resample(p, 5.0) for c, p in lines]); hwtree = cKDTree(hw)
def repel(P, gap=10.0, rounds=10):
    """A railway beside a highway would lie on top of it on a planet this small: wherever the track runs within `gap`
    of a highway for 25 m or more it is pushed out to `gap`, eased, and pushed again until it stands clear."""
    P = resample(P, 5.0)
    for r in range(rounds):
        dd, ii = hwtree.query(P); close = dd < gap - 0.5; close[0] = close[-1] = False
        j = 0; n = len(P); m = np.zeros(n, bool)
        while j < n:
            if not close[j]: j += 1; continue
            e = j
            while e < n and close[e]: e += 1
            if e - j >= 5: m[j:e] = True
            j = e
        if not m.any(): break
        away = P - hw[ii]; d = np.linalg.norm(away, axis=1)
        t = np.gradient(P, axis=0); t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9); sd = np.cross(P / R, t)
        sgn = np.sign((away * sd).sum(1)); sgn[sgn == 0] = 1
        dirn = np.where((d > 1.0)[:, None], away / np.maximum(d, 1e-9)[:, None], sd * sgn[:, None])
        P[m] = hw[ii[m]] + dirn[m] * gap; P = onsphere(P)
        P[1:-1] = 0.25 * P[:-2] + 0.5 * P[1:-1] + 0.25 * P[2:]; P = onsphere(P)
    return P

# Railways (class 3). On a planet this small a line beside a highway would lie on top of it, so tracks keep 10 m
# clear of every road except where they really cross; a track that still lies on a road gives way to it.
if len(sys.argv) > 5:
    nr = 0; import roadfix
    parts = [np.array(f['geometry']['coordinates'], float)[:, :2] for f in json.load(open(sys.argv[5]))['features'] if 'ferry' not in (f['properties'].get('featurecla') or '')]
    parts = [a for a in parts if len(a) >= 2]; whole = roadfix.chain_tracks(parts)
    print('railway pieces', len(parts), 'joined into lines', len(whole), flush=True)
    for a in whole:
        P = vec(a[:, 1], a[:, 0]) * R
        k = np.ones(len(P), bool); k[1:] = np.linalg.norm(P[1:] - P[:-1], axis=1) > 0.5
        P = P[k]
        if len(P) < 2: continue
        P = onsphere(smooth(dp(P, 3.0)))
        for P in ashore(P):
            P = around(P, farm, FARM_R + 25)
            if len(P) < 2 or np.linalg.norm(P[1:] - P[:-1], axis=1).sum() < 30: continue
            P = repel(P)
            P = soften(P, 9.0)
            if len(P) >= 2 and np.linalg.norm(P[1:] - P[:-1], axis=1).sum() > 30: lines.append((3, P)); nr += 1
    print('railways', nr, 'km', sum(np.linalg.norm(p[1:] - p[:-1], axis=1).sum() for c, p in lines if c == 3) / 1000)

# Stations (class 4): where a railway passes a town, one platform on the town's side of the track. Three points:
# the spot on the track, the next spot along it, and a spot 10 m out on the platform side.
# Everything together once more: a railway on top of a road, two tracks on top of each other.
lines = tidy(lines)
import roadfix
for _ in range(2):
    lines, jn = roadfix.join_road_ends(lines, keep_out=(farm, FARM_R - 4)); print('road ends run on to the next road', jn, flush=True)
# A join that slides in alongside the road it meets would lie on top of it: such a stretch is cut back to where the two
# first touch, and a sliver that only loops off a road and back is dropped. Then the few ends that opened are closed.
rd = [(c, P) for c, P in lines if c <= 2]; rest = [(c, P) for c, P in lines if c > 2]
order = sorted(range(len(rd)), key=lambda i: (0 if is_ring(rd[i][1]) else 1, rd[i][0], -np.linalg.norm(rd[i][1][1:] - rd[i][1][:-1], axis=1).sum()))
rank = np.empty(len(rd), int); rank[order] = np.arange(len(rd))
lines = declutter(rd, rank) + rest
lines, jn = roadfix.join_road_ends(lines, reach=14.0, keep_out=(farm, FARM_R - 4)); print('road ends run on to the next road', jn, flush=True)
lines, jn, fr = roadfix.join_ends(lines); print('track ends run on to the next track', jn, flush=True)
for _ in range(3):
    lines, dr = roadfix.drop_stubs(lines); print('short railway leftovers dropped', dr, flush=True)
    if dr == 0: break
# A railway beside a road stays on one side of it and changes sides once, in one smooth bend.
import roadfix
print('railway crossings under 40 degrees', roadfix.count_shallow(lines), flush=True)
for _ in range(4):
    lines, sq = roadfix.keep_side(lines); print('tracks kept to one side', sq, flush=True)
    if sq == 0: break
print('railway crossings under 40 degrees left', roadfix.count_shallow(lines), flush=True)
lines, jn, fr = roadfix.join_ends(lines); print('track ends run on to the next track', jn, 'track ends in the open', fr, 'of', 2 * sum(1 for c, _ in lines if c == 3), flush=True)
allp = np.vstack([resample(p, 6.0) for c, p in lines if c != 3]); tree = cKDTree(allp)
for c in range(4):
    a = [np.degrees(turn_angles(p).max()) for cc, p in lines if cc == c and len(p) > 2]
    if a: print('class', c, 'lines', len(a), 'sharpest turn at a point, degrees', round(max(a), 1), 'lines over 35:', sum(1 for v in a if v > 35), flush=True)
rails = [p for c, p in lines if c == 3]
if rails:
    dense = []; tang = []
    for P in rails:
        for a, b in zip(P[:-1], P[1:]):
            l = np.linalg.norm(b - a); n = max(1, int(l / 8))
            dense.append(a + (b - a) * np.linspace(0, 1, n, endpoint=False)[:, None]); tang.append(np.repeat(((b - a) / max(l, 1e-9))[None], n, 0))
    dense = np.vstack(dense); tang = np.vstack(tang); rtree = cKDTree(dense); made = []
    dd, ii = rtree.query(TP)
    for i in np.argsort(-T[:, 2]):                                   # bigger towns first
        if dd[i] > trad[i] + 25: continue
        p = dense[ii[i]]; t = tang[ii[i]]
        if np.linalg.norm(p - farm) < FARM_R + 90: continue
        if any(np.linalg.norm(p - q) < 150 for q in made[-400:] if abs(p[0] - q[0]) < 150): continue
        sd = np.cross(p / R, t); sd /= np.linalg.norm(sd); k = 1.0 if (TP[i] - p) @ sd >= 0 else -1.0
        if tree.query(p + sd * k * 8)[0] < 9 and tree.query(p - sd * k * 8)[0] >= 9: k = -k   # keep the platform off a road
        made.append(p); lines.append((4, onsphere(np.array([p, p + t * 12, p + sd * k * 10]))))
    print('stations', len(made))

# Points that add nothing (within 15 cm of the straight line between their neighbours) are left out.
n0 = sum(len(p) for c, p in lines); lines = [(c, dp(p, 0.15) if c <= 3 and len(p) > 2 else p) for c, p in lines]
print('points', n0, '->', sum(len(p) for c, p in lines), flush=True)
# Chunks: at most 20 points and 500 m each; a chunk knows whether a real end of the road is at its start/end.
chunks = []; pts = []
for cls, P in lines:
    i = 0
    while i < len(P) - 1:
        j = i + 1; ln = 0.0
        while j < len(P) - 1 and j - i < 19 and ln + np.linalg.norm(P[j] - P[j - 1]) < 500: ln += np.linalg.norm(P[j] - P[j - 1]); j += 1
        seg = P[i:j + 1]; c = seg.mean(0); c = c / np.linalg.norm(c) * R
        rad = np.linalg.norm(seg - c, axis=1).max()
        clat, clon = latlon(c)
        chunks.append((len(pts), len(seg), cls, (1 if i == 0 else 0) | (2 if j == len(P) - 1 else 0), float(clat), float(clon), float(rad)))
        la, lo = latlon(seg); pts += list(zip(la, lo))
        i = j
with open(sys.argv[4], 'wb') as fo:
    fo.write(b'MNWR'); fo.write(struct.pack('<iii', 1, len(chunks), len(pts)))
    for c in chunks: fo.write(struct.pack('<iHBBfff', *c))
    fo.write(np.array(pts, '<f4').tobytes())
print('chunks', len(chunks), 'points', len(pts), 'bytes', 16 + len(chunks) * 20 + len(pts) * 8)
