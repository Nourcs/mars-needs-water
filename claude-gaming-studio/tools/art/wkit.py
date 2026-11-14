# Helpers for the world wonders (wonders.py, wonders2.py). Units are metres, y up, the front faces -z (south),
# the origin is on the ground at the middle of the site: the same frame the game's LandmarkView builds in.
from forge import *
import forge

MARBLE = (242, 237, 224); STONE = (209, 189, 143); SANDST = (219, 189, 128); PAVING = (199, 194, 184); LAWN = (102, 178, 71)
POOL = (51, 153, 204); GOLD = (255, 204, 64); DARKSTONE = (115, 107, 102); CONCRETE = (209, 204, 194); SLATE = (77, 92, 102)
BRICK = (184, 66, 51); CREAM = (245, 230, 194); DARK = (64, 51, 41); SAND = (235, 204, 133); ROCK = (158, 153, 143)
WHITE_ = (248, 246, 240); RED = (214, 60, 44); GLASS_ = (120, 170, 205); STEELB = (199, 214, 230); WOOD_ = (150, 100, 60)

def sh(c, k): return tuple(min(255, v * k) for v in c)
def B(x, y0, z, w, h, d, col, bevel=0.0): return bx(x, y0 + h / 2, z, w, h, d, col, bevel)
def LA(x, y0, z, profile, col=None, cols=None, n=20, smooth=False, fillet=0.08, exact=None):
    """A lathe placed at (x, y0, z). Twelve sides or fewer are kept as given (columns, turrets)."""
    ex = (n <= 12) if exact is None else exact
    return xf(lathe(profile, n=n, col=col or MARBLE, cols=cols, smooth=smooth, exact=ex, fillet=fillet), T(x, y0, z))
def CY(x, y0, z, r, h, col, n=16, cap=None, fillet=0.06): return LA(x, y0, z, [(r, 0), (r, h), (0, h)], cols=[col, cap or col], n=n, fillet=fillet)
def CN(x, y0, z, r, h, col, n=16): return LA(x, y0, z, [(r, 0), (0, h)], col=col, n=n)
def DOME(x, y0, z, r, col, squash=1.0, n=20):
    return LA(x, y0, z, [(r * np.cos(a), r * squash * np.sin(a)) for a in np.linspace(0, np.pi / 2, 7)], col=col, n=n, smooth=True)
def SPH(x, y, z, r, col, sy=1.0, n=16):
    return LA(x, y, z, [(r * np.cos(a), r * sy * np.sin(a)) for a in np.linspace(-np.pi / 2, np.pi / 2, 11)], col=col, n=n, smooth=True)
def ONION(x, y0, z, r, h, col=None, cols=None, n=18):
    pr = [(r * 0.72, 0), (r, h * 0.2), (r * 0.95, h * 0.38), (r * 0.6, h * 0.62), (r * 0.22, h * 0.82), (r * 0.06, h * 0.93), (0, h)]
    return LA(x, y0, z, pr, col=col, cols=cols, n=n, smooth=True)
def BEAM(a, b, r, col, n=8, r1=None):
    a = np.asarray(a, float); b = np.asarray(b, float)
    up = np.eye(3)[int(np.argmin(np.abs(b - a)))]
    old = forge.SMOOTH; forge.SMOOTH = False
    try: return tube([a, b], [r, r if r1 is None else r1], n=n, col=col, up=up)
    finally: forge.SMOOTH = old
def PIPE(path, r, col, n=8, up=None):
    """A smooth tube along a path (cables, arches, legs). r: one number or one per point."""
    P = np.asarray(path, float)
    if up is None: up = np.eye(3)[int(np.argmin(np.abs(P[-1] - P[0]) + np.abs(P[len(P) // 2] - P[0])))]
    old = forge.SMOOTH; forge.SMOOTH = False
    try: return tube(P, r, n=n, col=col, up=up)
    finally: forge.SMOOTH = old
def RY(p, deg): return xf(p, R('y', deg))
def AT(p, x=0, y=0, z=0, ry=0): return xf(p, T(x, y, z) @ R('y', ry))
def radial(p, a, r, y=0):
    """Place a part whose front is +z at angle a (degrees) on a circle of radius r, facing outwards."""
    t = np.radians(a)
    return xf(p, T(r * np.cos(t), y, r * np.sin(t)) @ R('y', 90 - a))
def ring(fn, count, r, y=0, a0=0.0):
    return [radial(fn(i), a0 + i * 360.0 / count, r, y) for i in range(count)]
def arch(w, h, t, col, pointed=False):
    """An arch-shaped plate (door, window, niche): bottom centre at the origin, thickness t along z."""
    if pointed: o = [(-w / 2, 0), (w / 2, 0), (w / 2, h * 0.55), (w * 0.36, h * 0.78), (w * 0.2, h * 0.92), (0, h), (-w * 0.2, h * 0.92), (-w * 0.36, h * 0.78), (-w / 2, h * 0.55)]
    else: o = [(-w / 2, 0), (w / 2, 0)] + [(w / 2 * np.cos(a), h - w / 2 + w / 2 * np.sin(a)) for a in np.linspace(0, np.pi, 9)]
    return xf(prism(o, t, col), R('y', 90))
def gable(x, y0, z, length, w, h, col, along='x'):
    """A gable roof: ridge along x (or z), sitting at y0."""
    p = prism([(-w / 2, 0), (w / 2, 0), (0, h)], length, col)
    if along == 'z': p = xf(p, R('y', 90))
    return xf(p, T(x, y0, z))
def pyr(x, y0, z, w, h, col, d=None, top=0.0):
    d = w if d is None else d
    pts = [(sx * w / 2, 0, sz * d / 2) for sx in (-1, 1) for sz in (-1, 1)]
    pts += [(0, h, 0)] if top <= 0 else [(sx * w * top / 2, h, sz * d * top / 2) for sx in (-1, 1) for sz in (-1, 1)]
    return xf(hull(pts, col), T(x, y0, z))
def cut(p, a0, a1):
    """Keep the triangles of a part whose centre lies between two angles round the y axis (degrees, 0..360)."""
    P, N, C = p
    c = P.reshape(-1, 3, 3).mean(1); a = np.degrees(np.arctan2(c[:, 2], c[:, 0])) % 360
    k = ((a >= a0) & (a <= a1)) if a0 <= a1 else ((a >= a0) | (a <= a1))
    k = np.repeat(k, 3)
    return P[k], N[k], C[k]
def plinth(w, d, col, top=0.3, side=DARKSTONE, x=0, z=0):
    """A plaza that reaches well down into the ground, so the wonder never floats on a slope."""
    return join(B(x, -30, z, w, 30 + top - 0.02, d, side), B(x, top - 0.3, z, w - 0.02, 0.3, d - 0.02, col))
def shell(rho, a, beta=100, col=WHITE_, glass=None, m=8, n=14):
    """A pointed vault shell cut from a sphere (Sydney Opera House): ridge radius rho, mouth half-width a,
    mouth facing -z and leaning forward, the ridge running back along +z down to the ground."""
    Rr = (a + rho * rho / a) / 2; c = (rho * rho / a - a) / 2
    psi0 = np.arccos(c / Rr); tris, norms = [], []
    def half(sgn):
        ctr = np.array([0, 0, -c * sgn])
        g = np.zeros((m + 1, n + 1, 3)); gn = np.zeros_like(g)
        for i, ps in enumerate(np.linspace(0, psi0, m + 1)):
            for j, be in enumerate(np.radians(np.linspace(0, beta, n + 1))):
                d = np.array([-np.sin(ps) * np.cos(be), np.sin(ps) * np.sin(be), np.cos(ps) * sgn])
                g[i, j] = ctr + Rr * d; gn[i, j] = d
        for i in range(m):
            for j in range(n):
                for (p, q, r) in (((i, j), (i + 1, j), (i + 1, j + 1)), ((i, j), (i + 1, j + 1), (i, j + 1))):
                    tris.extend([g[p], g[q], g[r]]); norms.extend([gn[p], gn[q], gn[r]])
                    tris.extend([g[p] - gn[p] * 0.25, g[q] - gn[q] * 0.25, g[r] - gn[r] * 0.25]); norms.extend([-gn[p], -gn[q], -gn[r]])
        return g[:, n]
    e1 = half(1); e2 = half(-1)
    out = [part(tris, norms, col)]
    if glass is not None:
        be = np.radians(beta); nrm = np.array([np.sin(be), np.cos(be), 0.0]); O = np.zeros(3); gt, gn_ = [], []
        for e in (e1, e2):
            for i in range(m):
                gt += [O - nrm * 0.6, e[i] - nrm * 0.6, e[i + 1] - nrm * 0.6]; gn_ += [nrm] * 3
        out.append(part(gt, gn_, glass))
    return xf(join(*out), R('y', 90))
