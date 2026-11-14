# Helpers for bake_roads.py: a railway beside a road keeps to one side of it and changes sides in one smooth bend.
import numpy as np
from scipy.spatial import cKDTree
R = 25000.0

def onsphere(P): return P / np.linalg.norm(P, axis=1, keepdims=True) * R

def resample(P, step):
    seg = np.linalg.norm(P[1:] - P[:-1], axis=1); cum = np.concatenate([[0], np.cumsum(seg)])
    if cum[-1] < 1e-6: return P[[0, -1]]
    n = max(1, int(round(cum[-1] / step))); at = np.linspace(0, cum[-1], n + 1)
    return onsphere(np.stack([np.interp(at, cum, P[:, k]) for k in range(3)], axis=1))

def tangents(Q):
    t = np.gradient(Q, axis=0); return t / np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)

def _roads(lines):
    RP = []; RT = []; RID = []
    for i, (c, P) in enumerate(lines):
        if c > 2 or len(P) < 2: continue
        Q = resample(P, 4.0); RP.append(Q); RT.append(tangents(Q)); RID += [i] * len(Q)
    return np.vstack(RP), np.vstack(RT), np.array(RID)

def _beside(Q, RP, RT, RID, tree, near):
    dd, ii = tree.query(Q); r = RT[ii]; nrm = np.cross(Q / R, r); nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
    d = Q - RP[ii]; s = (d * nrm).sum(1); al = (d * r).sum(1)
    rid = np.where((dd < near) & (np.abs(al) < 3.0), RID[ii], -1)
    return ii, r, nrm, s, al, rid

def count_shallow(lines, least=40.0):
    """Railway/road crossings shallower than `least` degrees."""
    RP, RT, RID = _roads(lines); tree = cKDTree(RP); n = 0
    for c, P in lines:
        if c != 3 or len(P) < 2: continue
        Q = resample(P, 4.0); ii, r, nrm, s, al, rid = _beside(Q, RP, RT, RID, tree, 9.0)
        t = Q[1:] - Q[:-1]; t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
        ang = np.degrees(np.arccos(np.minimum(1.0, np.abs((t * r[:-1]).sum(1)))))
        n += int(((rid[:-1] >= 0) & (rid[:-1] == rid[1:]) & (s[:-1] * s[1:] < 0) & (ang < least)).sum())
    return n

def keep_side(lines, near=20.0, gap=12.0, least=40.0, cross=7.0):
    """A railway that runs beside a road stays on one side of it. Where the track wanders across the road and back it is
    kept on the side it came from; where it really has to change sides it does so once, in one short S-bend at about
    60 degrees, and keeps `gap` metres off the road everywhere else. Crossings steeper than `least` are left alone."""
    RP, RT, RID = _roads(lines); tree = cKDTree(RP); out = []; fixed = 0
    for c, P in lines:
        if c != 3 or len(P) < 2: out.append((c, P)); continue
        Q = resample(P, 4.0); n = len(Q); ii, r, nrm, s, al, rid = _beside(Q, RP, RT, RID, tree, near)
        moved = np.zeros(n, bool); hold = np.zeros(n, bool); j = 0
        while j < n:
            if rid[j] < 0: j += 1; continue
            e = j
            while e < n and rid[e] == rid[j]: e += 1
            a, b = j, e
            if b - a >= 2:
                sg = np.sign(s[a:b]); sg[sg == 0] = 1; ch = np.nonzero(sg[1:] != sg[:-1])[0]
                ride = (np.abs(s[a:b]) < 8.0).sum() >= 7 and len(ch) == 0     # lies on the road without crossing it
                if len(ch) or ride:
                    if len(ch):
                        t = Q[a + ch + 1] - Q[a + ch]; t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
                        ang = np.degrees(np.arccos(np.minimum(1.0, np.abs((t * r[a + ch]).sum(1)))))
                    if ride or ang.min() < least:
                        mag = np.maximum(np.abs(s[a:b]), gap); idx = np.arange(a, b)
                        first = np.sign(sg[:3].sum()) if b - a >= 3 else sg[0]; last = np.sign(sg[-3:].sum()) if b - a >= 3 else sg[-1]
                        if first == last: sn = first * mag           # a stray first or last point does not count
                        else:
                            kc = a + ch[np.argmin(np.abs(ch - (b - a) / 2.0))] + 0.5
                            w = np.clip((idx - kc) * 4.0 / cross, -1.0, 1.0); sn = w * last * mag
                            hold[a:b] |= np.abs(w) < 0.7
                        Q[a:b] = RP[ii[a:b]] + r[a:b] * al[a:b, None] + nrm[a:b] * sn[:, None]
                        moved[a:b] = True; fixed += 1
            j = e
        if not moved.any(): out.append((c, P)); continue
        m = moved.copy()
        for k in range(1, 5): m[k:] |= moved[:-k]; m[:-k] |= moved[k:]
        m &= ~hold; m[0] = m[-1] = False                         # the crossing itself stays straight
        for it in range(4):
            A = Q.copy(); A[1:-1] = 0.25 * Q[:-2] + 0.5 * Q[1:-1] + 0.25 * Q[2:]; Q[m] = A[m]; Q = onsphere(Q)
        for it in range(12):                                         # no kink is left behind
            t = Q[1:] - Q[:-1]; t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
            kink = np.zeros(len(Q), bool); kink[1:-1] = (t[1:] * t[:-1]).sum(1) < 0.906      # more than 25 degrees at a point
            if not kink.any(): break
            k2 = kink.copy(); k2[1:] |= kink[:-1]; k2[:-1] |= kink[1:]; k2[2:] |= kink[:-2]; k2[:-2] |= kink[2:]; k2[0] = k2[-1] = False
            A = Q.copy(); A[1:-1] = 0.25 * Q[:-2] + 0.5 * Q[1:-1] + 0.25 * Q[2:]; Q[k2] = A[k2]; Q = onsphere(Q)
        out.append((c, Q))
    return out, fixed

def chain_tracks(parts, straight=-0.5):
    """The railway source comes in short pieces split at every junction. They are joined into whole lines: at each
    meeting point the two pieces that carry straight on become one line, and a branch ends there, on the track.
    `parts` are arrays of lon, lat; returns longer arrays of lon, lat."""
    from collections import defaultdict
    node = defaultdict(list)                                         # meeting point -> (piece, which end)
    for i, a in enumerate(parts):
        node[tuple(np.round(a[0], 5))].append((i, 0)); node[tuple(np.round(a[-1], 5))].append((i, 1))
    def outward(i, end):
        a = parts[i]; p, q = (a[0], a[min(1, len(a) - 1)]) if end == 0 else (a[-1], a[-2])
        d = (q - p) * np.array([np.cos(np.radians(p[1])), 1.0]); return d / max(np.linalg.norm(d), 1e-12)
    mate = {}
    for key, ends in node.items():
        left = list(ends)
        while len(left) >= 2:
            best = None
            for x in range(len(left)):
                for y in range(x + 1, len(left)):
                    if left[x][0] == left[y][0]: continue
                    d = float(outward(*left[x]) @ outward(*left[y]))
                    if best is None or d < best[0]: best = (d, x, y)
            if best is None or (best[0] > straight and len(ends) > 2): break
            _, x, y = best; mate[left[x]] = left[y]; mate[left[y]] = left[x]
            left = [v for k, v in enumerate(left) if k not in (x, y)]
    used = np.zeros(len(parts), bool); out = []
    def walk(i, end):                                                # leave piece i through `end`, collect what follows
        got = []
        while (i, end) in mate:
            j, e = mate[(i, end)]
            if used[j]: break
            used[j] = True; a = parts[j] if e == 0 else parts[j][::-1]; got.append(a[1:]); i, end = j, 1 - e
        return got
    for i in range(len(parts)):
        if used[i]: continue
        used[i] = True
        back = walk(i, 0); fwd = walk(i, 1)
        line = [parts[i]] + fwd
        if back: line = [np.vstack([b[::-1] for b in back[::-1]])] + [parts[i]] + fwd   # pieces walked backwards, turned round
        out.append(np.vstack(line))
    return out

def _tail(Q, target, n=8, rounds=6):
    n = min(len(Q), n); tail = resample(np.vstack([Q[-n:], target[None]]), 4.0)
    for it in range(rounds):
        A = tail.copy(); A[1:-1] = 0.25 * tail[:-2] + 0.5 * tail[1:-1] + 0.25 * tail[2:]; tail = onsphere(A)
    return np.vstack([Q[:-n], tail])

def _split_tail(Q, length):
    """The line without its last `length` metres, and those last metres laid out again every 4 m."""
    seg = np.linalg.norm(Q[1:] - Q[:-1], axis=1); back = np.concatenate([np.cumsum(seg[::-1])[::-1], [0.0]])   # distance to the end
    k = int(np.argmax(back <= length))                               # first point within `length` of the end
    if k == 0: return Q[:0], resample(Q, 4.0)
    u = (back[k - 1] - length) / max(seg[k - 1], 1e-9); cutp = Q[k - 1] + (Q[k] - Q[k - 1]) * u
    return Q[:k], resample(np.vstack([cutp[None], Q[k:]]), 4.0)

def join_road_ends(lines, reach=40.0, keep_out=None):
    """A road that stops a few metres short of the next road is run on to that road's centre line: a real junction,
    not two blunt ends facing each other. An end that overshot is trimmed back to the road; otherwise it curves on to
    a spot ahead of it, and only if there is none to the nearest spot. `keep_out` = (centre, radius) of the farm."""
    RP = []; RID = []
    for i, (c, P) in enumerate(lines):
        if c > 2 or len(P) < 2: continue
        Q = resample(P, 4.0); RP.append(Q); RID += [i] * len(Q)
    RP = np.vstack(RP); RID = np.array(RID); tree = cKDTree(RP); out = []; joined = 0
    for i, (c, P) in enumerate(lines):
        if c > 2 or len(P) < 2: out.append((c, P)); continue
        Q = P
        for end in (0, -1):
            if end == 0: Q = Q[::-1]
            E = Q[-1]; near = np.array([j for j in tree.query_ball_point(E, reach) if RID[j] != i], int)
            if len(near) and np.linalg.norm(RP[near] - E, axis=1).min() >= 1.0:
                head, tail = _split_tail(Q, 28.0); N = None
                for m in range(len(tail) - 2, 0, -1):                              # overshot: trim back to the road it passed
                    hit = [j for j in tree.query_ball_point(tail[m], 2.2) if RID[j] != i]
                    if hit:
                        k = hit[int(np.argmin(np.linalg.norm(RP[hit] - tail[m], axis=1)))]; N = np.vstack([tail[:max(1, m - 1)], RP[k][None]]); break     # one point further back, so the last piece still runs forward
                if N is None:
                    t = tail[-1] - tail[max(0, len(tail) - 3)]; t /= max(np.linalg.norm(t), 1e-9)
                    v = RP[near] - E; d = np.linalg.norm(v, axis=1); ahead = (v @ t) / d > 0.3
                    k = -1; z = int(np.argmin(d))
                    if ahead.any(): k = near[ahead][int(np.argmin(d[ahead]))]
                    elif d[z] < 8.0 and (v[z] @ t) / d[z] > -0.2: k = near[z]       # right beside it; never doubling back
                    if k >= 0:
                        N = resample(np.vstack([tail, RP[k][None]]), 4.0)
                        for it in range(5):
                            A = N.copy(); A[1:-1] = 0.25 * N[:-2] + 0.5 * N[1:-1] + 0.25 * N[2:]; N = onsphere(A)
                if N is not None and len(N) >= 2 and (keep_out is None or np.linalg.norm(N - keep_out[0], axis=1).min() > keep_out[1]):
                    Q = np.vstack([head, N]) if len(head) else N; joined += 1
            if end == 0: Q = Q[::-1]
        out.append((c, Q))
    return out, joined

def join_ends(lines, reach=45.0, clear=18.0, back=28.0):
    """A track end that stops short of another track (a branch whose main line was moved aside) is run on to it in a
    curve, to a spot ahead of it, the way a branch joins a main line. Two tracks never meet on top of a road: a
    junction closer than `clear` to a road is moved along the main line to open ground. Returns the new lines, how many
    ends were joined, and how many ends are still in the open."""
    RP = []; RID = []; RD = []
    for i, (c, P) in enumerate(lines):
        if c > 2 or len(P) < 2: continue
        RD.append(resample(P, 4.0))
    roads = cKDTree(np.vstack(RD))
    for i, (c, P) in enumerate(lines):
        if c != 3 or len(P) < 2: continue
        Q = resample(P, 4.0); RP.append(Q); RID += [i] * len(Q)
    RP = np.vstack(RP); RID = np.array(RID); tree = cKDTree(RP); offroad = roads.query(RP)[0] >= clear
    out = []; joined = 0; free = 0
    for i, (c, P) in enumerate(lines):
        if c != 3 or len(P) < 2: out.append((c, P)); continue
        Q = resample(P, 4.0)
        for end in (0, -1):
            if end == 0: Q = Q[::-1]                                  # always work on the last point
            E = Q[-1]
            near = np.array([j for j in tree.query_ball_point(E, reach) if RID[j] != i], int)
            if len(near) == 0: free += 1
            else:
                d = np.linalg.norm(RP[near] - E, axis=1); onroad = roads.query(E)[0] < clear
                if d.min() >= 1.5 or onroad:
                    done = False
                    cut = int(back / 4.0) if (onroad and d.min() < 1.5 and len(Q) > 3 * int(back / 4.0)) else 0   # step back off the road first
                    B = Q[:len(Q) - cut] if cut else Q; F = B[-1]; t = F - B[max(0, len(B) - 3)]; t /= max(np.linalg.norm(t), 1e-9)
                    v = RP[near] - F; df = np.linalg.norm(v, axis=1); ahead = ((v @ t) / np.maximum(df, 1e-9) > 0.35) & (df > 6.0)
                    for strict in (True, False):
                        if not strict and d.min() < 1.5: break        # already joined: leave it rather than join on a road again
                        want = ahead & offroad[near] if strict else ahead
                        for k in near[want][np.argsort(df[want])][:12]:
                            if strict:                                # the way there may not cross a road
                                u = np.linspace(0, 1, max(3, int(np.linalg.norm(RP[k] - F) / 3.0)))[:, None]
                                if roads.query(onsphere(F + (RP[k] - F) * u))[0].min() < 8.0: continue
                            Q = _tail(B, RP[k]); joined += 1; done = True; break
                        if done: break
                    if not done and d.min() > 6.0: free += 1
            if end == 0: Q = Q[::-1]
        out.append((c, Q))
    return out, joined, free

def drop_stubs(lines, shortest=120.0, touch=3.0):
    """A short piece of track with an end in the open is a leftover and goes; a short piece that joins two tracks stays."""
    RP = []; RID = []
    for i, (c, P) in enumerate(lines):
        if c != 3 or len(P) < 2: continue
        Q = resample(P, 4.0); RP.append(Q); RID += [i] * len(Q)
    RP = np.vstack(RP); RID = np.array(RID); tree = cKDTree(RP); out = []; dropped = 0
    for i, (c, P) in enumerate(lines):
        if c == 3 and (len(P) < 2 or np.linalg.norm(P[1:] - P[:-1], axis=1).sum() < shortest):
            if len(P) < 2 or any(not any(RID[j] != i for j in tree.query_ball_point(P[e], touch)) for e in (0, -1)): dropped += 1; continue
        out.append((c, P))
    return out, dropped
