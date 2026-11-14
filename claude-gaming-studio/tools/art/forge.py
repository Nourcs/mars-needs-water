# Mars Needs Water's own models, built in code. Coordinates are Unity's (x right, y up, z forward, metres).
# A part is (P, N, C): triangle-soup positions (n*3,3), vertex normals, sRGB colours 0-255. Winding is fixed at
# export so that cross(b-a, c-a) agrees with the normal (what Unity wants). Output: the game's 'MNWK' kit format.
import numpy as np, struct
from scipy.spatial import ConvexHull

ORANGE = (255, 107, 13); WHITE = (237, 237, 232); TEAL = (26, 158, 153); TEAL_D = (15, 96, 98)
DARK = (38, 38, 44); STEEL = (176, 182, 190); STEEL_D = (110, 116, 126); GLASS = (70, 150, 215)

def _n(v):
    l = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(l, 1e-12)

def part(tris, norms, col):
    P = np.asarray(tris, float).reshape(-1, 3); N = np.asarray(norms, float).reshape(-1, 3)
    C = np.asarray(col, float)
    if C.ndim == 1: C = np.tile(C, (len(P), 1))
    return P, N, C

def join(*parts):
    return tuple(np.concatenate([p[i] for p in parts]) for i in range(3))

def xf(p, M):
    """Transform a part by a 4x4 matrix (normals by the inverse transpose)."""
    P, N, C = p
    A = M[:3, :3]
    return P @ A.T + M[:3, 3], _n(N @ np.linalg.inv(A)), C

def T(x=0, y=0, z=0):
    M = np.eye(4); M[:3, 3] = (x, y, z); return M
def S(x=1, y=None, z=None):
    y = x if y is None else y; z = x if z is None else z
    return np.diag([x, y, z, 1.0])
def R(axis, deg):
    a = np.radians(deg); c, s = np.cos(a), np.sin(a); M = np.eye(4)
    i, j = {'x': (1, 2), 'y': (2, 0), 'z': (0, 1)}[axis]
    M[i, i] = c; M[i, j] = -s; M[j, i] = s; M[j, j] = c
    return M

SMOOTH = True      # the house look: rounded edges and smooth curves (False = the first, faceted look)

def lathe(profile, n=20, cols=None, smooth=False, col=WHITE, exact=False, fillet=0.35):
    """Revolve (r, y) points round the y axis; cols = one colour per segment. Sharp corners of the profile are
    rounded off with a small fillet whose normals blend from one face to the next, so faces stay flat but no
    edge is hard. smooth=True (balls, domes) just averages the normals. exact=True keeps n as given (models
    that are drawn in their hundreds); otherwise the count round the axis is raised until it reads as round."""
    pr = [np.asarray(p, float) for p in profile]; k = len(pr) - 1
    cs = list(cols) if cols is not None else [col] * k
    if SMOOTH and not exact: n = max(24, int(n * 1.6))
    def segn(a, b):
        d = b - a; return _n(np.array([d[1], -d[0]]))
    pts, nrm, pc = [], [], []                          # points, their normals, colour of the segment after each
    for i in range(k + 1):
        P = pr[i]
        na = segn(pr[i - 1], P) if i > 0 else None; nb = segn(P, pr[i + 1]) if i < k else None
        c = cs[min(i, k - 1)]
        if na is None or nb is None:
            pts.append(P); nrm.append(nb if na is None else na); pc.append(c); continue
        turn = np.degrees(np.arccos(np.clip(na @ nb, -1, 1)))
        if smooth or not SMOOTH and False or turn < 35 or not SMOOTH or P[0] < 1e-9:
            hard = (not smooth) and (not SMOOTH)
            pts.append(P); nrm.append(None if hard else _n(na + nb)); pc.append(c); continue
        la, lb = np.linalg.norm(pr[i - 1] - P), np.linalg.norm(pr[i + 1] - P)
        f = fillet * min(la, lb)
        p1 = P + (pr[i - 1] - P) / la * f; p2 = P + (pr[i + 1] - P) / lb * f; mid = (p1 + 2 * P + p2) / 4
        pts += [p1, mid, p2]; nrm += [na, _n(na + nb), nb]; pc += [cs[i - 1], c, c]
    pts = np.array(pts); m = len(pts) - 1
    ang = np.linspace(0, 2 * np.pi, n + 1); ca, sa = np.cos(ang), np.sin(ang)
    def pt(i, j): return np.array([pts[i, 0] * ca[j], pts[i, 1], pts[i, 0] * sa[j]])
    def nm(v, j): return np.array([v[0] * ca[j], v[1], v[0] * sa[j]])
    tris, norms, colors = [], [], []
    for i in range(m):
        sn = segn(pts[i], pts[i + 1])
        n0 = nrm[i] if nrm[i] is not None else sn; n1 = nrm[i + 1] if nrm[i + 1] is not None else sn
        if np.linalg.norm(pts[i + 1] - pts[i]) < 1e-9: continue
        c = pc[i]
        for j in range(n):
            a, b, c2, d2 = pt(i, j), pt(i, j + 1), pt(i + 1, j + 1), pt(i + 1, j)
            na, nb, nc, nd = nm(n0, j), nm(n0, j + 1), nm(n1, j + 1), nm(n1, j)
            if pts[i, 0] > 1e-9: tris += [a, b, c2]; norms += [na, nb, nc]; colors += [c] * 3
            if pts[i + 1, 0] > 1e-9: tris += [a, c2, d2]; norms += [na, nc, nd]; colors += [c] * 3
    return part(tris, norms, colors)

def hull(points, col):
    """Flat-shaded convex solid through the points."""
    pts = np.asarray(points, float); h = ConvexHull(pts)
    tris = pts[h.simplices]; nn = h.equations[:, :3]
    return part(tris, np.repeat(nn, 3, axis=0), col)

def box(sx, sy, sz, bevel=0.0, col=WHITE):
    """A box centred on the origin. With a bevel its edges and corners are rounded, with smooth normals."""
    hx, hy, hz = sx / 2, sy / 2, sz / 2
    if bevel <= 0:
        return hull([(X * hx, Y * hy, Z * hz) for X in (-1, 1) for Y in (-1, 1) for Z in (-1, 1)], col)
    if not SMOOTH:
        b = bevel; pts = []
        for X in (-1, 1):
            for Y in (-1, 1):
                for Z in (-1, 1):
                    pts += [(X * (hx - b), Y * (hy - b), Z * hz), (X * (hx - b), Y * hy, Z * (hz - b)), (X * hx, Y * (hy - b), Z * (hz - b))]
        return hull(pts, col)
    h = np.array([hx, hy, hz]); r = min(bevel * 1.5, 0.48 * 2 * h.min())
    steps = 1 if r < 0.06 * 2 * h.max() else (2 if r < 0.16 * 2 * h.max() else 3)
    tn = np.tan(np.linspace(0, np.pi / 4, steps + 1))[1:]
    def coords(hh):
        inner = hh - r
        return np.concatenate([-(inner + r * tn[::-1]), [-inner, inner], inner + r * tn])
    tris, norms = [], []
    for ax in range(3):
        u, v = (ax + 1) % 3, (ax + 2) % 3
        cu, cv = coords(h[u]), coords(h[v])
        for sgn in (-1, 1):
            g = np.zeros((len(cu), len(cv), 3)); g[..., ax] = sgn * h[ax]; g[..., u] = cu[:, None]; g[..., v] = cv[None, :]
            inner = np.clip(g, -(h - r), h - r); nn = _n(g - inner); pos = inner + r * nn
            for i in range(len(cu) - 1):
                for j in range(len(cv) - 1):
                    q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
                    for t in ((0, 1, 2), (0, 2, 3)):
                        tris += [pos[q[x]] for x in t]; norms += [nn[q[x]] for x in t]
    return part(tris, norms, col)

def loft(sections, n=28, cols=None, col=WHITE, power=3.2):
    """A smooth solid through rounded-rectangle cross-sections stacked along y: (y, width x, depth z[, z offset])."""
    t = np.linspace(0, 2 * np.pi, n, endpoint=False); ct, st = np.cos(t), np.sin(t)
    ux = np.sign(ct) * np.abs(ct) ** (2 / power); uz = np.sign(st) * np.abs(st) ** (2 / power)
    rings = np.array([[(ux[j] * s[1] / 2, s[0], uz[j] * s[2] / 2 + (s[3] if len(s) > 3 else 0)) for j in range(n)] for s in sections])
    m = len(sections)
    fn = np.zeros((m, n, 3))                             # smooth vertex normals from the faces round each vertex
    for i in range(m - 1):
        for j in range(n):
            j2 = (j + 1) % n
            a, b, c, d = rings[i, j], rings[i, j2], rings[i + 1, j2], rings[i + 1, j]
            f = np.cross(c - a, b - d)
            for (ii, jj) in ((i, j), (i, j2), (i + 1, j2), (i + 1, j)): fn[ii, jj] += f
    ctr = rings.mean(axis=1)
    out = np.einsum('ijk,ijk->ij', fn, rings - ctr[:, None, :]).sum() < 0
    fn = _n(-fn if out else fn)
    tris, norms, colors = [], [], []
    for i in range(m - 1):
        c_ = cols[i] if cols is not None else col
        for j in range(n):
            j2 = (j + 1) % n
            q = [(i, j), (i, j2), (i + 1, j2), (i + 1, j)]
            for tt in ((0, 1, 2), (0, 2, 3)):
                tris += [rings[q[x]] for x in tt]; norms += [fn[q[x]] for x in tt]; colors += [c_] * 3
    for i, sg in ((0, -1.0), (m - 1, 1.0)):              # close the ends
        c_ = cols[min(i, m - 2)] if cols is not None else col
        for j in range(n):
            tris += [ctr[i], rings[i, j], rings[i, (j + 1) % n]]; norms += [(0, sg, 0)] * 3; colors += [c_] * 3
    return part(tris, norms, colors)

def prism(outline, thick, col):
    """A flat convex plate: outline is (z, y) points, extruded +-thick/2 along x."""
    pts = [(s * thick / 2, y, z) for z, y in outline for s in (-1, 1)]
    return hull(pts, col)

def tube(path, rx, ry=None, n=10, col=WHITE, cols=None, up=(0, 1, 0), caps=True):
    """Sweep an ellipse along a path. rx/ry: one number or one per path point (rx across, ry along 'up')."""
    if SMOOTH: n = max(n, 12)
    P = np.asarray(path, float); m = len(P)
    rx = np.full(m, rx, float) if np.isscalar(rx) else np.asarray(rx, float)
    ry = rx if ry is None else (np.full(m, ry, float) if np.isscalar(ry) else np.asarray(ry, float))
    tan = np.zeros_like(P); tan[1:-1] = P[2:] - P[:-2]; tan[0] = P[1] - P[0]; tan[-1] = P[-1] - P[-2]; tan = _n(tan)
    ang = np.linspace(0, 2 * np.pi, n + 1)
    rings, rn = [], []
    upv = np.asarray(up, float)
    for i in range(m):
        side = _n(np.cross(upv, tan[i])); u = _n(np.cross(tan[i], side))
        ring = P[i] + np.outer(np.cos(ang) * rx[i], side) + np.outer(np.sin(ang) * ry[i], u)
        nr = _n(np.outer(np.cos(ang) / max(rx[i], 1e-9), side) + np.outer(np.sin(ang) / max(ry[i], 1e-9), u))
        rings.append(ring); rn.append(nr)
    tris, norms, colors = [], [], []
    for i in range(m - 1):
        c = cols[i] if cols is not None else col
        for j in range(n):
            a, b, c2, d = rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]
            na, nb, nc, nd = rn[i][j], rn[i][j + 1], rn[i + 1][j + 1], rn[i + 1][j]
            tris += [a, b, c2, a, c2, d]; norms += [na, nb, nc, na, nc, nd]; colors += [c] * 6
    if caps:
        for i, sgn in ((0, -1), (m - 1, 1)):
            c = cols[min(i, m - 2)] if cols is not None else col
            for j in range(n):
                tris += [P[i], rings[i][j], rings[i][j + 1]]; norms += [sgn * tan[i]] * 3; colors += [c] * 3
    return part(tris, norms, colors)

def arc(cx, cy, r, a0, a1, steps, plane='xy', off=0.0):
    out = []
    for t in np.linspace(np.radians(a0), np.radians(a1), steps):
        u, v = cx + r * np.cos(t), cy + r * np.sin(t)
        out.append((u, v, off) if plane == 'xy' else (off, v, u))
    return out

def save(p, path):
    P, N, C = p
    tri = P.reshape(-1, 3, 3)
    g = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]); ln = np.linalg.norm(g, axis=1)
    ok = ln > 1e-12                                       # no zero-area triangles (NaN normals flash white)
    P = tri[ok].reshape(-1, 3); N = _n(N.reshape(-1, 3, 3)[ok].reshape(-1, 3)); C = C.reshape(-1, 3, 3)[ok].reshape(-1, 3)
    g = g[ok]
    flip = np.einsum('ij,ij->i', g, N.reshape(-1, 3, 3).mean(1)) < 0
    idx = np.arange(len(P)).reshape(-1, 3); idx[flip] = idx[flip][:, ::-1]
    assert np.isfinite(P).all() and np.isfinite(N).all()
    mn, mx = P.min(0), P.max(0)
    with open(path, 'wb') as fo:
        fo.write(b'MNWK'); fo.write(struct.pack('<iii', 1, len(P), idx.size)); fo.write(struct.pack('<6f', *mn, *mx))
        rec = np.zeros(len(P), dtype=[('p', '<f4', 3), ('n', '<f4', 3), ('c', 'u1', 4)])
        rec['p'] = P; rec['n'] = N; rec['c'][:, :3] = np.clip(C, 0, 255); rec['c'][:, 3] = 255
        fo.write(rec.tobytes()); fo.write(idx.reshape(-1).astype('<i4').tobytes())
    return len(P) // 3, mx - mn

def blob(center, radii, n=12, seed=0, jitter=0.18, col=WHITE, sub=1, shade=0.3):
    """A soft lump (foliage, rocks): a rounded ball with gentle bumps, smooth normals, lighter towards the top."""
    if not SMOOTH:
        rng = np.random.RandomState(seed); pts = []
        for i in range(n):
            y = 1 - 2 * (i + 0.5) / n; r = np.sqrt(max(0, 1 - y * y)); a = i * 2.39996
            pts.append(np.asarray(center) + np.array([np.cos(a) * r, y, np.sin(a) * r]) * (1 + rng.uniform(-jitter, jitter)) * np.asarray(radii))
        return hull(pts, col)
    import trimesh
    ico = trimesh.creation.icosphere(subdivisions=sub); V = np.array(ico.vertices); F = np.array(ico.faces)
    rng = np.random.RandomState(seed); K = rng.normal(size=(3, 3)) * 1.6; ph = rng.uniform(0, 6.28, 3)
    bump = 1 + jitter * 0.6 * np.sin(V @ K.T + ph).sum(1) / 1.5
    P = V * bump[:, None] * np.asarray(radii)
    vn = np.zeros_like(P); fnn = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]])
    for kk in range(3): np.add.at(vn, F[:, kk], fnn)
    vn = _n(vn); vn = np.where((np.einsum('ij,ij->i', vn, P) < 0)[:, None], -vn, vn)
    C = np.asarray(col, float)[None, :] * (1 - shade / 2 + shade * (V[:, 1:2] + 1) / 2)
    return part((P + np.asarray(center))[F].reshape(-1, 3), vn[F].reshape(-1, 3), np.clip(C, 0, 255)[F].reshape(-1, 3))

def tint(p, k):
    return p[0], p[1], np.clip(p[2] * np.asarray(k, float), 0, 255)

def bx(cx, cy, cz, sx, sy, sz, col, bevel=0.0):
    """A box by centre and size."""
    return xf(box(sx, sy, sz, bevel, col), T(cx, cy, cz))
