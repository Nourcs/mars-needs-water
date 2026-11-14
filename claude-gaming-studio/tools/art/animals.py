# Wildlife. Rigid toy-like animals in metres, feet on y = 0, facing +z. A few hundred triangles each.
from forge import *
import forge

EYE = (24, 24, 28)
def ell(c, r, col, n=10, rings=6, cols=None, axis='y'):
    """A smooth ellipsoid; cols = one colour per ring band (stripes); axis = the direction the bands stack along."""
    pr = [(np.sin(a), -np.cos(a)) for a in np.linspace(0, np.pi, rings + 1)]
    p = lathe(pr, n=n, smooth=True, exact=True, col=col, cols=cols)
    M = {'y': np.eye(4), 'z': R('x', 90), 'x': R('z', -90)}[axis]
    return xf(p, T(*c) @ S(*r) @ M)
def tb(path, r0, r1, col, n=6):
    old = forge.SMOOTH; forge.SMOOTH = False
    d = np.abs(np.asarray(path[-1], float) - np.asarray(path[0], float)); up = np.eye(3)[int(np.argmin(d))]
    try: return tube(path, np.linspace(r0, r1, len(path)), n=n, col=col, up=up)
    finally: forge.SMOOTH = old
def leg(x, z, h, r, col, hoof=None, n=7, cols=None):
    hoof = hoof or col
    return xf(lathe([(0, 0), (r * 0.9, 0), (r * 0.85, h * 0.12), (r * 0.75, h * 0.5), (r * 1.15, h), (0, h)], n=n, exact=True, smooth=True,
                    cols=cols or [hoof, hoof, col, col, col]), T(x, 0, z))
def eyes(c, dx, r=0.03):
    return join(*[ell((c[0] + s * dx, c[1], c[2]), (r, r, r), EYE, n=6, rings=3) for s in (-1, 1)])

def quad(L, H, bw, bh, lr, neck, head, col, leg_col=None, hoof=None, head_col=None, stripes=None, leg_cols=None, tail=None, rings=8):
    """Four legs, a body, a neck and a head. neck = (length, angle up from level, radius); head = (length, height, width)."""
    leg_col = leg_col or col; head_col = head_col or col
    by = H + bh * 0.2
    cols = None
    if stripes: cols = [stripes[i % len(stripes)] for i in range(rings)]
    parts = [ell((0, by, 0), (bw / 2, bh / 2, L / 2), col, n=14, rings=rings, cols=cols, axis='z')]
    for sx in (-1, 1):
        for sz in (-1, 1): parts.append(leg(sx * bw * 0.28, sz * L * 0.3, H + bh * 0.05, lr, leg_col, hoof, cols=leg_cols))
    nl, na, nr = neck; a = np.radians(na)
    n0 = np.array([0, by + bh * 0.15, L * 0.36]); n1 = n0 + nl * np.array([0, np.sin(a), np.cos(a)])
    ncols = None
    parts.append(tb([n0 - np.array([0, 0.1 * bh, 0.1 * L]), (n0 + n1) / 2, n1], nr * 1.25, nr, col if not stripes else stripes[0], 7))
    hl, hh, hw = head
    hc = n1 + np.array([0, -hh * 0.05, hl * 0.28])
    parts.append(ell(hc, (hw / 2, hh / 2, hl / 2), head_col, n=12, rings=7, axis='z'))
    parts.append(eyes(hc + np.array([0, hh * 0.15, hl * 0.12]), hw * 0.46, max(0.02, hw * 0.09)))
    if tail:
        tl, tc = tail; t0 = np.array([0, by + bh * 0.1, -L * 0.47])
        parts.append(tb([t0, t0 + np.array([0, -tl * 0.5, -tl * 0.25]), t0 + np.array([0, -tl, -tl * 0.3])], lr * 0.3, lr * 0.2, tc, 4))
    return parts, hc, n1, by

GREY = (150, 152, 162); TAN = (214, 172, 104); BROWN = (132, 88, 56); WHITE_ = (240, 240, 235); BLACK = (40, 40, 44)

def elephant():
    p, hc, n1, by = quad(3.0, 1.75, 1.75, 1.8, 0.3, (0.45, 15, 0.55), (1.05, 1.15, 1.05), GREY, hoof=(120, 120, 130), tail=(1.0, GREY))
    p.append(tb([hc + np.array([0, -0.1, 0.45]), hc + np.array([0, -0.9, 0.75]), hc + np.array([0, -1.7, 0.65]), hc + np.array([0, -2.0, 0.9])], 0.24, 0.1, GREY, 7))
    for s in (-1, 1):
        p.append(xf(ell((0, 0, 0), (0.09, 0.62, 0.5), (168, 160, 168), n=8, rings=5), T(hc[0] + s * 0.62, hc[1] + 0.05, hc[2] - 0.25) @ R('y', s * 25)))
        p.append(tb([hc + np.array([s * 0.28, -0.35, 0.4]), hc + np.array([s * 0.33, -0.7, 0.75]), hc + np.array([s * 0.3, -0.75, 1.05])], 0.07, 0.03, (244, 240, 226), 5))
    return join(*p)

def giraffe():
    a, b = (232, 186, 96), (176, 116, 62)
    p, hc, n1, by = quad(2.0, 1.95, 0.85, 1.05, 0.11, (2.3, 70, 0.19), (0.62, 0.3, 0.28), a, hoof=(70, 56, 48), stripes=[a, b], leg_cols=[(70, 56, 48), a, b, a, b], tail=(0.9, b))
    n0 = np.array([0, by + 0.16, 0.72])
    for i, t in enumerate(np.linspace(0.12, 0.88, 5)):   # patches up the neck
        c = n0 + (n1 - n0) * t; p.append(ell(c, (0.235 - 0.03 * t, 0.16, 0.235 - 0.03 * t), b, n=7, rings=3))
    for s in (-1, 1):
        p.append(tb([hc + np.array([s * 0.07, 0.12, -0.12]), hc + np.array([s * 0.08, 0.32, -0.16])], 0.03, 0.035, b, 4))
        p.append(ell(hc + np.array([s * 0.17, 0.1, -0.2]), (0.09, 0.05, 0.05), a, n=6, rings=3))
    return join(*p)

def _equine(body, stripes, mane, legc, hoof):
    p, hc, n1, by = quad(1.9, 1.3, 0.66, 0.82, 0.095, (0.85, 52, 0.2), (0.62, 0.3, 0.27), body, hoof=hoof, stripes=stripes, leg_cols=legc, tail=(0.9, mane))
    n0 = np.array([0, by + 0.12, 0.68])
    p.append(tb([n0 + np.array([0, 0.22, -0.2]), (n0 + n1) / 2 + np.array([0, 0.2, -0.12]), n1 + np.array([0, 0.16, -0.05])], 0.07, 0.06, mane, 4))
    for s in (-1, 1): p.append(ell(hc + np.array([s * 0.09, 0.2, -0.18]), (0.04, 0.09, 0.03), body if not stripes else stripes[0], n=6, rings=3))
    p.append(ell(hc + np.array([0, -0.03, 0.27]), (0.11, 0.1, 0.09), BLACK if stripes else (90, 60, 44), n=6, rings=3))
    return join(*p)
def zebra(): return _equine(WHITE_, [WHITE_, BLACK], BLACK, [BLACK, WHITE_, BLACK, WHITE_, BLACK], BLACK)
def horse(): return _equine((148, 96, 62), None, (56, 40, 32), None, (50, 40, 36))

def _cat(col, stripes, mane):
    p, hc, n1, by = quad(1.7, 0.82, 0.6, 0.66, 0.1, (0.36, 28, 0.24), (0.42, 0.4, 0.42), col, stripes=stripes, tail=(0.95, col))
    if mane: p.append(ell(hc + np.array([0, 0.0, -0.14]), (0.42, 0.44, 0.3), (156, 104, 52), n=10, rings=6)); p.append(ell((0, by - 0.42, -1.12), (0.09, 0.09, 0.12), (156, 104, 52), n=6, rings=3))
    for s in (-1, 1): p.append(ell(hc + np.array([s * 0.15, 0.2, -0.08]), (0.07, 0.07, 0.04), (156, 104, 52) if mane else col, n=6, rings=3))
    p.append(ell(hc + np.array([0, -0.06, 0.2]), (0.14, 0.11, 0.1), (250, 240, 224), n=6, rings=3)); p.append(ell(hc + np.array([0, -0.01, 0.29]), (0.045, 0.035, 0.03), (60, 40, 40), n=5, rings=3))
    return join(*p)
def lion(): return _cat(TAN, None, True)
def tiger(): return _cat((238, 142, 52), [(238, 142, 52), BLACK], False)

def kangaroo():
    c = (196, 144, 104); d = (170, 120, 86)
    p = [xf(ell((0, 0, 0), (0.3, 0.55, 0.36), c, n=10, rings=7), T(0, 0.85, 0) @ R('x', 20)),
         tb([(0, 0.5, -0.25), (0, 0.15, -0.8), (0, 0.06, -1.35)], 0.16, 0.05, c, 6), tb([(0, 1.25, 0.2), (0, 1.5, 0.3)], 0.14, 0.11, c, 6),
         ell((0, 1.62, 0.4), (0.12, 0.13, 0.2), c, n=8, rings=5, axis='z'), eyes((0, 1.66, 0.46), 0.1, 0.025), ell((0, 1.6, 0.6), (0.035, 0.03, 0.03), EYE, n=5, rings=3),
         ell((0, 0.78, 0.26), (0.2, 0.3, 0.14), (226, 196, 160), n=8, rings=5)]
    for s in (-1, 1):
        p += [ell((s * 0.24, 0.5, -0.05), (0.13, 0.32, 0.26), d, n=8, rings=5), tb([(s * 0.24, 0.3, 0.05), (s * 0.24, 0.06, 0.1), (s * 0.24, 0.05, 0.55)], 0.07, 0.06, d, 5),
              tb([(s * 0.2, 1.15, 0.22), (s * 0.2, 0.95, 0.42), (s * 0.18, 0.86, 0.5)], 0.05, 0.035, d, 4), ell((s * 0.07, 1.86, 0.33), (0.04, 0.14, 0.03), c, n=6, rings=3)]
    return join(*p)

def camel():
    c = (212, 172, 122)
    p, hc, n1, by = quad(2.3, 1.55, 0.72, 0.9, 0.1, (1.1, 62, 0.17), (0.52, 0.28, 0.24), c, hoof=(150, 120, 90), tail=(0.7, c))
    p.append(ell((0, by + 0.5, -0.1), (0.3, 0.42, 0.52), (200, 158, 108), n=8, rings=5))
    for s in (-1, 1): p.append(ell(hc + np.array([s * 0.1, 0.14, -0.16]), (0.04, 0.05, 0.03), c, n=5, rings=3))
    return join(*p)

def _bear(body, legs, head, ear, panda=False):
    stripes = [body, body, body, body, body, BLACK, BLACK, body] if panda else None
    p, hc, n1, by = quad(1.8, 0.82, 0.95, 1.0, 0.17, (0.3, 12, 0.36), (0.52, 0.48, 0.5), body, leg_col=legs, head_col=head, stripes=stripes)
    for s in (-1, 1):
        p.append(ell(hc + np.array([s * 0.2, 0.22, -0.1]), (0.09, 0.09, 0.05), ear, n=6, rings=3))
        if panda: p.append(ell(hc + np.array([s * 0.13, 0.07, 0.19]), (0.07, 0.09, 0.05), BLACK, n=6, rings=3))
    p.append(ell(hc + np.array([0, -0.08, 0.24]), (0.15, 0.12, 0.13), (226, 208, 184) if not panda and body != WHITE_ else WHITE_, n=6, rings=3))
    p.append(ell(hc + np.array([0, -0.04, 0.36]), (0.05, 0.04, 0.03), EYE, n=5, rings=3))
    p.append(ell((0, by + 0.05, -0.9), (0.1, 0.1, 0.08), body, n=5, rings=3))
    return join(*p)
def bear(): return _bear((124, 84, 54), (104, 70, 46), (124, 84, 54), (104, 70, 46))
def polarbear(): return _bear(WHITE_, (228, 228, 222), WHITE_, WHITE_)
def panda(): return _bear(WHITE_, BLACK, WHITE_, BLACK, True)

def deer():
    c = (176, 124, 82)
    p, hc, n1, by = quad(1.4, 0.98, 0.46, 0.6, 0.055, (0.62, 60, 0.11), (0.36, 0.2, 0.18), c, hoof=(60, 48, 40))
    p.append(ell((0, by + 0.12, -0.68), (0.08, 0.1, 0.06), WHITE_, n=5, rings=3)); p.append(ell(hc + np.array([0, -0.02, 0.17]), (0.04, 0.035, 0.03), EYE, n=5, rings=3))
    for s in (-1, 1):
        p.append(ell(hc + np.array([s * 0.11, 0.08, -0.12]), (0.07, 0.035, 0.03), c, n=5, rings=3))
        b0 = hc + np.array([s * 0.05, 0.08, -0.08]); b1 = b0 + np.array([s * 0.12, 0.3, -0.05]); b2 = b1 + np.array([s * 0.12, 0.25, 0.06])
        p += [tb([b0, b1, b2], 0.022, 0.012, (226, 210, 180), 4), tb([b1, b1 + np.array([s * 0.02, 0.16, 0.14])], 0.016, 0.01, (226, 210, 180), 4)]
    return join(*p)

def _bovine(body, stripes, hump):
    p, hc, n1, by = quad(2.0, 1.05, 0.82, 0.96, 0.105, (0.42, 14, 0.26), (0.5, 0.36, 0.34), body, hoof=(50, 44, 40), stripes=stripes, tail=(0.8, body if not stripes else BLACK),
                         head_col=(84, 56, 40) if hump else body)
    p.append(ell(hc + np.array([0, -0.06, 0.2]), (0.13, 0.1, 0.1), (240, 190, 186) if not hump else (60, 44, 36), n=6, rings=3))
    for s in (-1, 1):
        p.append(tb([hc + np.array([s * 0.14, 0.14, -0.08]), hc + np.array([s * 0.26, 0.2, -0.06]), hc + np.array([s * 0.29, 0.33, -0.04])], 0.035, 0.012, (236, 228, 206), 4))
        p.append(ell(hc + np.array([s * 0.2, 0.06, -0.14]), (0.08, 0.04, 0.03), body if not stripes else BLACK, n=5, rings=3))
    if hump: p.append(ell((0, by + 0.32, 0.5), (0.44, 0.5, 0.6), (84, 56, 40), n=8, rings=5))
    else: p.append(ell((0, by - 0.48, -0.5), (0.16, 0.1, 0.18), (240, 190, 186), n=6, rings=3))
    return join(*p)
def cow(): return _bovine(WHITE_, [WHITE_, BLACK, BLACK, WHITE_, WHITE_, BLACK, WHITE_, WHITE_], False)
def bison(): return _bovine((112, 76, 52), None, True)

def sheep():
    d = (64, 58, 58)
    p, hc, n1, by = quad(1.05, 0.5, 0.5, 0.5, 0.05, (0.2, 25, 0.1), (0.28, 0.2, 0.18), d, leg_col=d, head_col=d)
    p.append(blob((0, by + 0.06, -0.02), (0.4, 0.38, 0.62), seed=5, col=(244, 242, 232), jitter=0.2, shade=0.2)); p.append(blob(hc + np.array([0, 0.1, -0.1]), (0.14, 0.1, 0.12), seed=6, col=(244, 242, 232), sub=0))
    for s in (-1, 1): p.append(ell(hc + np.array([s * 0.12, 0.02, -0.08]), (0.07, 0.03, 0.03), d, n=5, rings=3))
    return join(*p)

def llama():
    c = (238, 228, 208)
    p, hc, n1, by = quad(1.4, 1.02, 0.52, 0.72, 0.065, (0.9, 76, 0.15), (0.36, 0.22, 0.2), c, hoof=(110, 96, 84), tail=(0.25, c))
    for s in (-1, 1): p.append(ell(hc + np.array([s * 0.06, 0.2, -0.1]), (0.035, 0.1, 0.03), c, n=5, rings=3))
    p.append(ell(hc + np.array([0, -0.03, 0.17]), (0.06, 0.05, 0.04), (120, 96, 84), n=5, rings=3)); p.append(blob((0, by + 0.12, 0), (0.33, 0.36, 0.72), seed=9, col=c, jitter=0.12, shade=0.15))
    return join(*p)

def penguin():
    p = [ell((0, 0.5, 0), (0.24, 0.46, 0.22), BLACK, n=10, rings=7), ell((0, 0.46, 0.07), (0.19, 0.36, 0.17), WHITE_, n=10, rings=6),
         ell((0, 1.0, 0.02), (0.15, 0.15, 0.15), BLACK, n=8, rings=5), eyes((0, 1.03, 0.13), 0.07, 0.022),
         xf(lathe([(0.045, 0), (0, 0.14)], n=5, exact=True, col=(244, 150, 40)), T(0, 0.98, 0.14) @ R('x', 90)), ell((0, 0.9, 0.1), (0.1, 0.05, 0.07), (250, 210, 90), n=6, rings=3)]
    for s in (-1, 1):
        p.append(xf(ell((0, 0, 0), (0.04, 0.26, 0.1), BLACK, n=6, rings=4), T(s * 0.25, 0.56, 0) @ R('z', s * 14)))
        p.append(ell((s * 0.1, 0.03, 0.1), (0.08, 0.03, 0.13), (244, 150, 40), n=6, rings=3))
    return join(*p)

def _fish(L, r, col, belly, fin, dorsal=True, beak=0.0):
    pr = [(0, -L / 2), (r * 0.35, -L * 0.42), (r * 0.8, -L * 0.2), (r, 0.02 * L), (r * 0.85, L * 0.22), (r * 0.45, L * 0.4), (r * 0.2, L * 0.47), (0, L / 2)]
    p = [xf(lathe(pr, n=14, smooth=True, exact=True, col=col), R('x', 90) @ S(1, 1, 1)), ell((0, -r * 0.25, L * 0.05), (r * 0.8, r * 0.75, L * 0.36), belly, n=10, rings=6, axis='z')]
    if dorsal: p.append(xf(prism([(L * 0.02, r * 0.85), (-L * 0.1, r * 1.6), (-L * 0.16, r * 0.8)], r * 0.12, fin), np.eye(4)))
    if beak: p.append(ell((0, -r * 0.2, L * 0.5), (r * 0.28, r * 0.2, beak), col, n=8, rings=5, axis='z'))
    for s in (-1, 1):
        p.append(xf(ell((0, 0, 0), (L * 0.11, r * 0.07, L * 0.07), fin, n=6, rings=4), T(s * L * 0.07, 0, -L * 0.49) @ R('y', s * 35)))
        p.append(xf(ell((0, 0, 0), (L * 0.09, r * 0.06, L * 0.045), fin, n=6, rings=4), T(s * r * 1.0, -r * 0.4, L * 0.17) @ R('y', -s * 30) @ R('z', -s * 25)))
        p.append(ell((s * r * 0.5, r * 0.18, L * 0.34), (0.03 * L / 2.2, 0.03 * L / 2.2, 0.03 * L / 2.2), EYE, n=5, rings=3))
    return join(*p)
def dolphin(): return _fish(2.3, 0.3, (112, 144, 176), (226, 232, 238), (92, 122, 154), beak=0.22)
def whale():
    w = xf(_fish(13.0, 1.9, (58, 78, 104), (190, 200, 210), (46, 64, 88), dorsal=False), T(0, 0, 0))
    return join(w, ell((0, 0.1, 3.6), (1.75, 1.6, 3.2), (58, 78, 104), n=12, rings=7, axis='z'), ell((0, 1.5, -2.2), (0.12, 0.3, 0.5), (46, 64, 88), n=6, rings=4))

def bird():         # white: the game colours it. Wings are added by the game so they can flap.
    return join(ell((0, 0, 0), (0.13, 0.12, 0.38), WHITE_, n=8, rings=6, axis='z'), ell((0, 0.07, 0.36), (0.1, 0.1, 0.12), WHITE_, n=7, rings=4),
                xf(lathe([(0.04, 0), (0, 0.16)], n=5, exact=True, col=(255, 190, 80)), T(0, 0.06, 0.45) @ R('x', 90)),
                xf(prism([(-0.3, 0), (-0.58, 0.16), (-0.58, -0.16)], 0.02, WHITE_), R('z', 90)), eyes((0, 0.1, 0.42), 0.085, 0.02))

ANIMALS = {'elephant': elephant, 'giraffe': giraffe, 'zebra': zebra, 'horse': horse, 'lion': lion, 'tiger': tiger, 'kangaroo': kangaroo, 'camel': camel,
           'bear': bear, 'polarbear': polarbear, 'panda': panda, 'deer': deer, 'cow': cow, 'bison': bison, 'sheep': sheep, 'llama': llama,
           'penguin': penguin, 'dolphin': dolphin, 'whale': whale, 'bird': bird}
if __name__ == '__main__':
    import sys, os
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for k, f in ANIMALS.items():
        t, size = save(f(), f'{out}/own-animal-{k}.bytes'); print(f'{k:10s}{t:5d} {np.round(size, 1)}', end=' | ')
    if len(sys.argv) > 2:
        from sheets import sheet
        sheet([(f, k.capitalize(), -55, 10) for k, f in ANIMALS.items()], sys.argv[2], 360, 330, cols=5)
