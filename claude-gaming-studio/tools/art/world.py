# Our own character, farm gear, nature and town models. Same conventions as models.py.
from forge import *

GREEN = (104, 168, 74); GREEN_D = (66, 128, 66); GREEN_L = (140, 196, 92); BARK = (122, 86, 58)
WOOD = (196, 152, 98); WOOD_D = (150, 110, 68); BLUE = (40, 108, 184); BLUE_D = (28, 78, 140)
WALL = (242, 234, 222); ROOF = (206, 96, 64); ROOF_D = (170, 74, 52); WIN = (78, 124, 156); DOOR = (132, 88, 62)
STONE = (190, 186, 178); STONE_D = (150, 146, 140); WATER = (64, 150, 214); RED = (214, 70, 52); YELLOW = (246, 196, 40)
RUBBER = (30, 30, 34); VISOR = (26, 48, 66)

def ny(p, L):   # author in metres along y, then normalise so that y = 1 is at L (limbs are stretched to bone length)
    return xf(p, S(1, 1.0 / L, 1))

# ------------------------------------------------------------------ character parts
def limb(r0, r1, col, extra=None, n=12):
    prof = [(0, 0), (r0, 0), (r1, 1), (0, 1)]; cols = [col, col, col]
    if extra:  # (y_from, radius, colour): a cuff / boot shaft over the far end
        y, r, c = extra
        rm = r0 + (r1 - r0) * y
        prof = [(0, 0), (r0, 0), (rm, y), (r, y), (r, 1), (0, 1)]; cols = [col, col, c, c, c]
    return lathe(prof, n=n, cols=cols)

def char_upperarm(): return limb(0.07, 0.06, ORANGE)
def char_forearm(): return limb(0.06, 0.053, ORANGE, (0.72, 0.066, WHITE))
def char_thigh(): return limb(0.1, 0.08, ORANGE)
def char_shin(): return limb(0.08, 0.068, ORANGE, (0.6, 0.086, DARK))
def _ball(col):
    pr = [(np.sin(a), -np.cos(a)) for a in np.linspace(0, np.pi, 9)]
    return lathe(pr, n=14, smooth=True, col=col)
def char_joint_o(): return _ball(ORANGE)
def char_joint_d(): return _ball(DARK)

def char_pelvis():
    return join(bx(0, -0.03, 0, 0.33, 0.2, 0.215, ORANGE, 0.04), bx(0, 0.065, 0, 0.345, 0.05, 0.228, DARK, 0.008),
                bx(0, 0.065, 0.116, 0.06, 0.045, 0.012, TEAL, 0.004))

def char_chest(L=0.42):
    body = loft([(-0.165, 0.16, 0.12), (-0.15, 0.25, 0.17), (-0.1, 0.285, 0.195), (0.05, 0.325, 0.22), (0.2, 0.37, 0.235), (0.31, 0.395, 0.24),
                 (0.37, 0.38, 0.23), (0.41, 0.31, 0.2), (0.435, 0.2, 0.15), (0.445, 0.1, 0.09)], n=28, col=ORANGE)
    parts = [body,
             bx(0, 0.2, 0.117, 0.2, 0.15, 0.018, WHITE, 0.006),               # chest panel
             bx(-0.05, 0.225, 0.128, 0.05, 0.05, 0.01, TEAL, 0.003),           # badge
             bx(0.045, 0.24, 0.128, 0.07, 0.016, 0.008, DARK), bx(0.045, 0.205, 0.128, 0.07, 0.016, 0.008, DARK),
             bx(0.06, 0.165, 0.128, 0.026, 0.026, 0.012, ORANGE, 0.004),
             bx(0, 0.17, -0.165, 0.27, 0.36, 0.13, WHITE, 0.03),               # water pack on the back
             bx(0, 0.17, -0.232, 0.16, 0.2, 0.012, TEAL, 0.004),
             xf(lathe([(0.045, 0), (0.045, 0.04), (0.03, 0.05), (0, 0.05)], n=10, cols=[DARK, TEAL, TEAL]), T(0.07, 0.35, -0.165)),
             xf(lathe([(0.078, 0), (0.078, 0.03), (0.06, 0.04), (0.06, 0)], n=12, col=DARK), T(0, 0.425, 0))]   # collar
    for sx in (-1, 1):
        parts.append(bx(sx * 0.11, 0.25, 0.0, 0.04, 0.34, 0.25, DARK, 0.006))  # pack straps
    return ny(join(*parts), L)

def char_helmet():
    pr = [(0.17 * np.sin(a), -0.17 * np.cos(a)) for a in np.linspace(np.radians(22), np.pi, 9)]
    shell = xf(lathe([(0.05, pr[0][1])] + pr, n=16, smooth=True, col=WHITE), T(0, 0.12, 0))
    vz = [(0.179 * np.sin(a), 0.179 * np.cos(a)) for a in np.linspace(np.radians(58), 0, 7)]
    visor = xf(lathe(vz, n=18, smooth=True, col=VISOR), T(0, 0.12, 0) @ R('x', 90))
    rim = xf(lathe([(0.142, 0.108), (0.158, 0.103), (0.166, 0.086), (0.156, 0.078)], n=18, col=ORANGE), T(0, 0.12, 0) @ R('x', 90))
    stripe = tube([(0, 0.12 + 0.176 * np.sin(a), 0.176 * np.cos(a)) for a in np.linspace(np.radians(72), np.radians(200), 12)], 0.03, 0.007, n=6, col=ORANGE, up=(1, 0, 0))
    parts = [shell, visor, rim, stripe, xf(lathe([(0.085, 0), (0.085, 0.035), (0.06, 0.045), (0.06, 0)], n=12, col=DARK), T(0, -0.03, 0))]
    for sx in (-1, 1):
        parts.append(xf(lathe([(0.055, 0), (0.055, 0.02), (0.035, 0.03), (0, 0.03)], n=10, cols=[TEAL, TEAL, TEAL_D]), T(sx * 0.16, 0.12, -0.01) @ R('z', -sx * 90)))
    parts.append(tube([(0.18, 0.13, -0.01), (0.2, 0.2, -0.02), (0.2, 0.33, -0.02)], 0.007, n=5, col=STEEL))
    parts.append(xf(_ball(ORANGE), T(0.2, 0.34, -0.02) @ S(0.016)))
    return xf(join(*parts), S(1.12))

def char_glove():
    # y along the arm, z across the palm, x through it (thin).
    return xf(_glove(), S(1.2))
def char_fist():
    # The same glove closed round a handle that runs across the palm (along z), fingers curled towards +x.
    p = [lathe([(0, -0.01), (0.054, -0.01), (0.054, 0.02), (0.046, 0.03), (0, 0.03)], n=10, col=ORANGE),
         bx(-0.008, 0.07, 0, 0.04, 0.085, 0.09, WHITE, 0.02), bx(0.002, 0.112, 0, 0.052, 0.03, 0.09, WHITE, 0.014),
         bx(0.036, 0.086, 0, 0.03, 0.062, 0.09, WHITE, 0.014), bx(0.02, 0.052, -0.004, 0.036, 0.024, 0.08, WHITE, 0.01),
         bx(0.042, 0.064, 0.03, 0.026, 0.03, 0.062, WHITE, 0.012), bx(-0.027, 0.075, 0, 0.006, 0.07, 0.06, DARK, 0.002)]
    for z in (-0.023, 0.0, 0.023): p.append(bx(0.0515, 0.092, z, 0.003, 0.046, 0.004, DARK))
    return xf(join(*p), S(1.2))
def _glove():
    return join(xf(lathe([(0, -0.01), (0.054, -0.01), (0.054, 0.02), (0.046, 0.03), (0, 0.03)], n=10, col=ORANGE), T()),
                bx(0, 0.075, 0, 0.05, 0.11, 0.09, WHITE, 0.02), bx(0, 0.135, -0.005, 0.045, 0.05, 0.08, WHITE, 0.018),
                xf(box(0.04, 0.07, 0.035, 0.012, WHITE), T(0, 0.065, 0.058) @ R('x', 28)),
                bx(-0.027, 0.075, 0, 0.006, 0.07, 0.06, DARK, 0.002))

def char_boot():
    # Origin at the ankle, 0.09 above the sole; toes to +z.
    return join(bx(0, -0.045, 0.045, 0.135, 0.08, 0.27, DARK, 0.022), bx(0, -0.05, 0.16, 0.14, 0.062, 0.08, WHITE, 0.02),
                bx(0, -0.082, 0.045, 0.148, 0.018, 0.285, TEAL, 0.004), bx(0, -0.02, -0.02, 0.13, 0.06, 0.13, DARK, 0.02))

CHAR = {'own-char-upperarm': char_upperarm, 'own-char-forearm': char_forearm, 'own-char-thigh': char_thigh, 'own-char-shin': char_shin,
        'own-char-joint': char_joint_o, 'own-char-pelvis': char_pelvis, 'own-char-chest': char_chest,
        'own-char-helmet': char_helmet, 'own-char-glove': char_glove, 'own-char-fist': char_fist, 'own-char-boot': char_boot}

def _from_to(d):
    d = np.asarray(d, float); d = d / np.linalg.norm(d); u = np.array([0, 1.0, 0]); v = np.cross(u, d); c = u @ d
    if np.linalg.norm(v) < 1e-9: return np.eye(4) if c > 0 else R('x', 180)
    K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]]); M = np.eye(4)
    M[:3, :3] = np.eye(3) + K + K @ K / (1 + c); return M

def character():
    """The whole figure in a relaxed pose, for the preview (the game assembles the same parts on the animated bones)."""
    J = dict(hips=(0, 0.98, 0), spine=(0, 1.08, 0), neck=(0, 1.5, 0), head=(0, 1.57, 0))
    parts = [xf(char_pelvis(), T(*J['hips'])), xf(char_chest(), T(*J['spine']) @ S(1, 0.42, 1)), xf(char_helmet(), T(*J['head']))]
    for s in (-1, 1):
        sh, el, wr = np.array([s * 0.215, 1.44, 0]), np.array([s * 0.31, 1.19, 0.02]), np.array([s * 0.36, 0.95, 0.09])
        hp, kn, an = np.array([s * 0.1, 0.95, 0]), np.array([s * 0.11, 0.52, 0.01]), np.array([s * 0.115, 0.09, 0])
        for a, b, m, jm, jr in ((sh, el, char_upperarm, char_joint_o, 0.082), (el, wr, char_forearm, char_joint_o, 0.068),
                                (hp, kn, char_thigh, char_joint_o, 0.104), (kn, an, char_shin, char_joint_o, 0.088)):
            parts.append(xf(m(), T(*a) @ _from_to(b - a) @ S(1, np.linalg.norm(b - a), 1)))
            parts.append(xf(jm(), T(*a) @ S(jr)))
        d = (wr - el) / np.linalg.norm(wr - el); z = np.array([0, 0, 1.0]); z = z - d * (z @ d); z /= np.linalg.norm(z); x = np.cross(d, z)
        M = np.eye(4); M[:3, 0] = x; M[:3, 1] = d; M[:3, 2] = z; M[:3, 3] = wr
        parts.append(xf(char_glove(), M)); parts.append(xf(char_boot(), T(*an)))
    return join(*parts)

# ------------------------------------------------------------------ farm gear
def crate():
    parts = [bx(0, 0.3, 0, 0.76, 0.56, 0.76, WOOD, 0.01)]
    for y in (0.06, 0.3, 0.54): parts.append(bx(0, y, 0, 0.8, 0.09, 0.8, WOOD_D, 0.012))
    for sx in (-1, 1):
        for sz in (-1, 1):
            parts.append(bx(sx * 0.375, 0.3, sz * 0.375, 0.07, 0.6, 0.07, WOOD_D, 0.012))
            parts.append(bx(sx * 0.385, 0.56, sz * 0.385, 0.07, 0.07, 0.07, STEEL_D, 0.01)); parts.append(bx(sx * 0.385, 0.04, sz * 0.385, 0.07, 0.07, 0.07, STEEL_D, 0.01))
    parts.append(bx(0, 0.3, -0.39, 0.5, 0.15, 0.022, ORANGE, 0.004))
    return join(*parts)

def barrel():
    pr = [(0, 0), (0.22, 0), (0.25, 0.03), (0.25, 0.25), (0.262, 0.26), (0.262, 0.28), (0.25, 0.29), (0.25, 0.51), (0.262, 0.52), (0.262, 0.54),
          (0.25, 0.55), (0.25, 0.75), (0.24, 0.78), (0.2, 0.8), (0, 0.8)]
    cols = [BLUE_D, BLUE_D, BLUE, BLUE_D, BLUE_D, BLUE_D, BLUE, BLUE_D, BLUE_D, BLUE_D, BLUE, BLUE, BLUE_D, BLUE_D]
    return join(lathe(pr, n=20, cols=cols), xf(lathe([(0.04, 0), (0.04, 0.025), (0, 0.025)], n=10, col=WHITE), T(0.12, 0.8, 0.08)),
                xf(lathe([(0.025, 0), (0.025, 0.02), (0, 0.02)], n=8, col=ORANGE), T(-0.12, 0.8, -0.06)))

def drum():
    pr = [(0, 0), (0.3, 0), (0.31, 0.01), (0.31, 0.03), (0.3, 0.04), (0.3, 0.29), (0.312, 0.3), (0.3, 0.31), (0.3, 0.59), (0.312, 0.6), (0.3, 0.61),
          (0.3, 0.86), (0.31, 0.87), (0.31, 0.9), (0.295, 0.9), (0.29, 0.875), (0, 0.875)]
    cols = [STEEL_D, DARK, DARK, DARK, STEEL, STEEL_D, STEEL_D, STEEL, STEEL_D, STEEL_D, STEEL, DARK, DARK, DARK, STEEL_D, STEEL_D]
    return join(lathe(pr, n=22, cols=cols), xf(lathe([(0.04, 0), (0.04, 0.02), (0, 0.02)], n=10, col=ORANGE), T(0.14, 0.875, 0.1)),
                xf(lathe([(0.02, 0), (0.02, 0.018), (0, 0.018)], n=8, col=DARK), T(-0.15, 0.875, -0.08)))

def ibc():
    h = 1.15; parts = [bx(0, 0.035, 0, 1.04, 0.05, 1.24, WOOD_D, 0.008), bx(0, 0.125, 0, 1.04, 0.03, 1.24, STEEL_D, 0.006)]
    for sx in (-1, 0, 1):
        for sz in (-1, 0, 1): parts.append(bx(sx * 0.45, 0.085, sz * 0.54, 0.12, 0.06, 0.14, WOOD, 0.008))
    parts.append(bx(0, 0.14 + 0.47, 0, 0.94, 0.94, 1.14, (232, 234, 228), 0.07))             # the bottle
    parts.append(xf(lathe([(0.11, 0), (0.11, 0.04), (0.09, 0.05), (0, 0.05)], n=14, cols=[DARK, ORANGE, ORANGE]), T(0, 1.08, 0)))
    parts.append(xf(lathe([(0.035, 0), (0.035, 0.09), (0.05, 0.09), (0.05, 0.13), (0, 0.13)], n=10, cols=[DARK, DARK, ORANGE, ORANGE]), T(0, 0.24, -0.57) @ R('x', -90)))
    for sx in (-1, 1):
        for sz in (-1, 1): parts.append(bx(sx * 0.5, h / 2 + 0.06, sz * 0.6, 0.035, h - 0.12, 0.035, STEEL, 0.008))
    for y in (0.42, 0.72, 1.02, h - 0.02):
        for sz in (-1, 1): parts.append(bx(0, y, sz * 0.6, 1.03, 0.028, 0.028, STEEL, 0.006))
        for sx in (-1, 1): parts.append(bx(sx * 0.5, y, 0, 0.028, 0.028, 1.23, STEEL, 0.006))
    for x in (-0.25, 0, 0.25):
        for sz in (-1, 1): parts.append(bx(x, h / 2 + 0.06, sz * 0.6, 0.022, h - 0.14, 0.022, STEEL, 0.005))
    for z in (-0.3, 0, 0.3):
        for sx in (-1, 1): parts.append(bx(sx * 0.5, h / 2 + 0.06, z, 0.022, h - 0.14, 0.022, STEEL, 0.005))
    return join(*parts)

def pump():
    parts = [bx(0, 0.025, 0, 0.62, 0.05, 0.72, DARK, 0.012)]
    for sx in (-1, 1): parts.append(bx(sx * 0.27, 0.07, 0, 0.05, 0.06, 0.68, STEEL_D, 0.01))
    parts.append(bx(0, 0.28, 0, 0.5, 0.44, 0.6, ORANGE, 0.05))
    parts.append(bx(0, 0.3, -0.296, 0.34, 0.16, 0.012, WHITE, 0.004))                            # name plate (label sits on it)
    parts.append(xf(lathe([(0.15, 0), (0.15, 0.03), (0.13, 0.07), (0.07, 0.1), (0, 0.105)], n=16, cols=[DARK, STEEL, STEEL, STEEL]), T(0, 0.5, 0.1)))
    parts.append(xf(lathe([(0.05, 0), (0.05, 0.012), (0.042, 0.014), (0, 0.014)], n=12, cols=[DARK, DARK, WHITE]), T(-0.14, 0.4, -0.3) @ R('x', -90)))  # dial
    for sx in (-1, 1):
        for z in (-0.18, 0, 0.18): parts.append(bx(sx * 0.252, 0.3, z, 0.012, 0.3, 0.05, (214, 86, 8), 0.003))   # cooling ribs
    for sx in (-1, 1):
        for sz in (-1, 1): parts.append(xf(lathe([(0.025, 0), (0.025, 0.014), (0, 0.014)], n=6, col=STEEL), T(sx * 0.27, 0.1, sz * 0.3)))
    return join(*parts)

def motorkit():
    pr = [(0, 0), (0.15, 0), (0.175, 0.02)]; cols = [DARK, DARK]
    for i in range(5):
        y = 0.03 + i * 0.045; pr += [(0.175, y + 0.02), (0.155, y + 0.025), (0.155, y + 0.04), (0.175, y + 0.045)]; cols += [STEEL, STEEL_D, STEEL_D, STEEL_D]
    pr += [(0.175, 0.27), (0.15, 0.3), (0.05, 0.3), (0.05, 0.33), (0, 0.33)]; cols += [STEEL, ORANGE, ORANGE, DARK, DARK]
    return join(lathe(pr, n=16, cols=cols), bx(0, 0.16, -0.185, 0.12, 0.12, 0.05, ORANGE, 0.012))

def inlet():
    return lathe([(0, 0), (0.09, 0), (0.09, 0.025), (0.06, 0.03), (0.06, 0.085), (0.07, 0.085), (0.07, 0.1), (0.045, 0.1), (0.045, 0.03), (0, 0.03)], n=14,
                 cols=[DARK, YELLOW, YELLOW, STEEL, DARK, DARK, DARK, DARK, DARK])

def tower():
    H, s = 5.2, 0.3; parts = []
    for sx in (-1, 1):
        for sz in (-1, 1):
            parts.append(bx(sx * s, H / 2, sz * s, 0.08, H, 0.08, ORANGE, 0.012)); parts.append(bx(sx * s, 0.03, sz * s, 0.2, 0.06, 0.2, DARK, 0.01))
    ys = np.arange(0.8, H, 0.8)
    for i, y in enumerate(ys):
        for sz in (-1, 1): parts.append(bx(0, y, sz * s, 2 * s, 0.05, 0.05, STEEL, 0.008))
        for sx in (-1, 1): parts.append(bx(sx * s, y, 0, 0.05, 0.05, 2 * s, STEEL, 0.008))
        y0 = y - 0.8
        for sz in (-1, 1):                                # zig-zag braces on the two faces you see from the apron
            a = (-s, y0 + 0.02, sz * s) if i % 2 == 0 else (s, y0 + 0.02, sz * s); b = (s, y, sz * s) if i % 2 == 0 else (-s, y, sz * s)
            parts.append(tube([a, b], 0.016, n=5, col=STEEL_D, up=(0, 0, 1)))
    parts.append(bx(0, H + 0.02, 0, 0.74, 0.05, 0.74, STEEL_D, 0.01))
    parts.append(bx(-0.68, 3.6, 0, 0.75, 0.09, 0.18, STEEL, 0.015)); parts.append(bx(-1.03, 3.52, 0, 0.1, 0.2, 0.22, ORANGE, 0.02))
    parts.append(tube([(-0.3, 3.0, 0), (-0.9, 3.58, 0)], 0.018, n=5, col=STEEL_D, up=(0, 0, 1)))
    return join(*parts)

GEAR = {'own-crate': crate, 'own-barrel': barrel, 'own-drum': drum, 'own-ibc': ibc, 'own-pump': pump, 'own-motorkit': motorkit,
        'own-inlet': inlet, 'own-tower': tower}

# ------------------------------------------------------------------ nature and towns (same names and sizes as the Kenney models they replace)
def tree():
    parts = [lathe([(0.075, 0), (0.05, 0.5), (0, 0.5)], n=7, col=BARK, exact=True, smooth=True)]
    for i, (y, r, h, c) in enumerate([(0.3, 0.33, 0.7, GREEN_D), (0.76, 0.26, 0.64, GREEN), (1.22, 0.18, 0.7, GREEN_L)]):
        pr = [(0, 0.03), (r * 0.7, 0.0), (r * 0.97, 0.05), (r, 0.12), (r * 0.72, h * 0.38), (r * 0.3, h * 0.78), (0, h)]
        parts.append(xf(lathe(pr, n=10, cols=[GREEN_D, GREEN_D, c, c, c, c], exact=True, smooth=True), T(0, y, 0)))
    return join(*parts)

def bush():
    return join(blob((0.02, 0.15, 0), (0.2, 0.15, 0.19), 12, 1, 0.14, GREEN), blob((0.13, 0.09, 0.07), (0.13, 0.1, 0.13), 10, 2, 0.14, GREEN_D, sub=0),
                blob((-0.12, 0.1, -0.06), (0.14, 0.11, 0.14), 10, 3, 0.14, GREEN_L))

def _gable(w, d, y, h, col, over=0.04):     # ridge along x
    pts = [(sx * (w / 2 + over), y, sz * (d / 2 + over)) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * (w / 2 + over), y + h, 0) for sx in (-1, 1)]
    return hull(pts, col)
def _win(x, y, z, w=0.1, h=0.12, face='z', col=WIN):
    if face == 'z': return join(bx(x, y, z, w + 0.03, h + 0.03, 0.012, WHITE), bx(x, y, z + np.sign(z) * 0.006, w, h, 0.012, col))
    return join(bx(x, y, z, 0.012, h + 0.03, w + 0.03, WHITE), bx(x + np.sign(x) * 0.006, y, z, 0.012, h, w, col))

def house_a():      # cottage, 0.95 tall
    w, d, h = 0.78, 0.66, 0.46
    parts = [bx(0, 0.02, 0, 0.92, 0.04, 0.9, STONE), bx(0, 0.04 + h / 2, 0, w, h, d, WALL), _gable(w, d, 0.04 + h, 0.36, ROOF),
             hull([(sx * w / 2, 0.04 + h, sz * 0.001 + (d / 2 - 0.001) * sz) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * w / 2, 0.04 + h + 0.34, 0) for sx in (-1, 1)], WALL),
             bx(0.2, 0.82, -0.1, 0.1, 0.24, 0.1, STONE_D), bx(0.2, 0.95 - 0.015, -0.1, 0.13, 0.03, 0.13, DARK),
             bx(-0.12, 0.04 + 0.13, d / 2, 0.13, 0.26, 0.02, DOOR), bx(-0.12, 0.04 + 0.285, d / 2 + 0.03, 0.22, 0.02, 0.09, ROOF_D),
             bx(-0.12, 0.03, d / 2 + 0.05, 0.2, 0.05, 0.1, STONE_D),
             _win(0.18, 0.3, d / 2), _win(-0.2, 0.3, -d / 2), _win(0.2, 0.3, -d / 2), _win(w / 2, 0.3, 0, face='x'), _win(-w / 2, 0.3, 0.1, face='x')]
    return join(*parts)

def house_d():      # bungalow with a porch, 1.0 tall, front slightly out (z to 0.55)
    w, d, h = 0.84, 0.6, 0.5
    parts = [bx(0, 0.02, 0, 0.94, 0.04, 0.92, STONE), bx(0, 0.04 + h / 2, -0.08, w, h, d, WALL)]
    pts = [(sx * (w / 2 + 0.05), 0.04 + h, -0.08 + sz * (d / 2 + 0.05)) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * 0.2, 1.0 - 0.06, -0.08) for sx in (-1, 1)]
    parts += [hull(pts, ROOF_D), bx(0, 0.47, 0.37, 0.7, 0.03, 0.3, ROOF), bx(0, 0.06, 0.37, 0.7, 0.05, 0.3, WOOD)]
    for sx in (-1, 1): parts.append(bx(sx * 0.31, 0.26, 0.49, 0.04, 0.4, 0.04, WHITE))
    parts += [bx(0.0, 0.2, 0.222, 0.13, 0.27, 0.02, TEAL_D), _win(-0.25, 0.3, 0.22), _win(0.25, 0.3, 0.22), _win(w / 2, 0.3, -0.1, face='x'),
              _win(-w / 2, 0.3, -0.1, face='x'), _win(0, 0.3, -0.38), bx(-0.25, 0.9, -0.15, 0.09, 0.2, 0.09, STONE_D)]
    return join(*parts)

def _block(w, d, floors, fh, roof_col, shop=None):
    parts = [bx(0, 0.02, 0, 0.94, 0.04, 0.94, STONE)]; h = floors * fh
    parts.append(bx(0, 0.04 + h / 2, 0, w, h, d, WALL))
    for f in range(floors):
        y = 0.04 + f * fh + fh * 0.55
        if f > 0: parts.append(bx(0, 0.04 + f * fh, 0, w + 0.02, 0.02, d + 0.02, STONE))
        for x in (-0.24, 0, 0.24):
            if not (f == 0 and shop): parts.append(_win(x, y, d / 2, 0.11, fh * 0.42))
            parts.append(_win(x, y, -d / 2, 0.11, fh * 0.42))
        for z in (-0.2, 0.2):
            for sx in (-1, 1): parts.append(_win(sx * w / 2, y, z, 0.11, fh * 0.42, face='x'))
    if shop:
        parts += [bx(0, 0.04 + fh * 0.42, d / 2, w * 0.86, fh * 0.6, 0.014, WIN), bx(0.22, 0.04 + fh * 0.36, d / 2 + 0.004, 0.13, fh * 0.72, 0.016, DOOR)]
        parts.append(hull([(sx * w * 0.47, 0.04 + fh * 0.95, d / 2) for sx in (-1, 1)] + [(sx * w * 0.47, 0.04 + fh * 0.78, d / 2 + 0.12) for sx in (-1, 1)]
                          + [(sx * w * 0.47, 0.04 + fh * 0.82, d / 2) for sx in (-1, 1)], shop))
    return parts, 0.04 + h

def house_b():      # three-storey shop house with a flat roof, 1.63 tall
    parts, top = _block(0.8, 0.74, 3, 0.49, ROOF, shop=ORANGE)
    parts += [bx(0, top + 0.03, 0, 0.86, 0.06, 0.8, STONE_D), bx(0, top + 0.07, 0, 0.74, 0.03, 0.68, DARK),
              bx(0.2, top + 0.11, -0.15, 0.2, 0.1, 0.2, STEEL), xf(lathe([(0.07, 0), (0.07, 0.03), (0, 0.03)], n=8, col=STEEL_D), T(0.2, top + 0.16, -0.15))]
    return join(*parts)

def house_c():      # tall townhouse with a pitched roof and dormer, 1.75 tall
    parts, top = _block(0.76, 0.7, 3, 0.44, ROOF_D)
    parts += [_gable(0.76, 0.7, top, 1.75 - top, ROOF_D), hull([(sx * 0.38, top, sz * 0.349) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * 0.38, 1.73, 0) for sx in (-1, 1)], WALL),
              bx(0, top + 0.14, 0.26, 0.2, 0.16, 0.2, WALL), bx(0, top + 0.235, 0.27, 0.25, 0.03, 0.24, ROOF), bx(0, top + 0.14, 0.362, 0.11, 0.1, 0.01, WIN),
              bx(0.05, 0.04 + 0.15, 0.352, 0.13, 0.3, 0.016, DOOR), bx(-0.26, top + 0.3, -0.12, 0.09, 0.3, 0.09, STONE_D)]
    return join(*parts)

def garage():       # 0.55 tall
    parts = [bx(0, 0.02, 0, 0.94, 0.04, 0.94, STONE), bx(0, 0.04 + 0.21, -0.05, 0.84, 0.42, 0.72, WALL), bx(0, 0.485, -0.05, 0.9, 0.05, 0.78, STONE_D),
             bx(-0.12, 0.2, 0.312, 0.46, 0.32, 0.014, STEEL), bx(0.28, 0.19, 0.312, 0.13, 0.3, 0.014, DOOR), bx(0, 0.525, 0.3, 0.5, 0.05, 0.03, TEAL)]
    for i in range(4): parts.append(bx(-0.12, 0.08 + i * 0.075, 0.321, 0.46, 0.012, 0.006, STEEL_D))
    return join(*parts)

def _wheel(x, y, z, r=0.2, w=0.14):
    return xf(lathe([(0, 0), (r * 0.5, 0), (r * 0.55, 0.015), (r, 0.015), (r, w - 0.015), (r * 0.55, w - 0.015), (r * 0.5, w), (0, w)], n=12, exact=True, fillet=0.0,
                    cols=[STEEL, STEEL, RUBBER, RUBBER, RUBBER, STEEL, STEEL]), T(x, y, z) @ R('z', 90) @ T(0, -w / 2, 0))
def _truck_base(cab):
    parts = [bx(0, 0.32, 0, 1.1, 0.1, 2.7, DARK, 0.02), bx(0, 0.72, 0.98, 1.3, 0.8, 0.74, cab, 0.08), bx(0, 0.86, 1.352, 1.06, 0.36, 0.02, WIN),
             bx(0, 0.36, 1.37, 1.36, 0.14, 0.08, STEEL_D, 0.02)]
    for sx in (-1, 1):
        parts += [bx(sx * 0.652, 0.86, 1.02, 0.02, 0.32, 0.42, WIN), bx(sx * 0.45, 0.5, 1.36, 0.18, 0.1, 0.03, YELLOW, 0.01)]
        for z in (0.95, -0.55, -1.0): parts.append(_wheel(sx * 0.62, 0.21, z))
    return parts
def truck_red():    # box lorry in company orange, 1.3 tall
    return join(*_truck_base(ORANGE), bx(0, 0.82, -0.42, 1.4, 0.92, 1.9, WHITE, 0.04), bx(0, 0.82, -0.42, 1.42, 0.2, 1.6, TEAL, 0.01), bx(0, 0.82, -1.38, 1.2, 0.8, 0.02, STEEL))
def truck_yellow():  # water tanker, 1.55 tall
    pr = [(0, 0), (0.42, 0.04), (0.58, 0.16), (0.58, 1.74), (0.42, 1.86), (0, 1.9)]
    tank = xf(lathe(pr, n=16, cols=[STEEL, STEEL, WHITE, STEEL, STEEL]), T(0, 0.96, 0.55) @ R('x', -90))
    return join(*_truck_base(TEAL), tank, xf(lathe([(0.6, 0), (0.6, 0.3)], n=16, col=TEAL), T(0, 0.96, -0.55) @ R('x', -90)),
                xf(lathe([(0.16, 0), (0.16, 0.06), (0, 0.06)], n=10, col=DARK), T(0, 1.49, -0.4)), bx(0, 0.44, -0.4, 1.2, 0.14, 1.7, STEEL_D, 0.02))

def fountain():     # tile +-0.5, 0.54 tall
    basin = lathe([(0, 0), (0.48, 0), (0.48, 0.04), (0.44, 0.045), (0.44, 0.13), (0.4, 0.14), (0.38, 0.13), (0.38, 0.06), (0, 0.06)], n=16,
                  cols=[STONE_D, STONE_D, STONE_D, STONE, STONE, STONE, STONE_D, STONE_D])
    water = lathe([(0, 0.105), (0.38, 0.105)], n=16, col=WATER)
    col_ = lathe([(0.06, 0.06), (0.045, 0.3), (0.16, 0.34), (0.17, 0.38), (0.14, 0.385), (0.13, 0.36), (0, 0.36)], n=12, cols=[STONE, STONE_D, STONE, STONE, STONE_D, STONE_D])
    return join(basin, water, col_, lathe([(0, 0.372), (0.13, 0.372)], n=12, col=WATER), lathe([(0.03, 0.36), (0.02, 0.5), (0.035, 0.52), (0, 0.54)], n=8, cols=[STONE, TEAL, TEAL]))

def statue():       # the company water drop on a plinth, 1.34 tall
    drop = [(0, 0)] + [(0.24 * np.sin(a), 0.24 * (1 - np.cos(a))) for a in np.linspace(0.35, 2.2, 7)] + [(0.1, 0.56), (0.03, 0.7), (0, 0.74)]
    return join(bx(0, 0.04, 0, 0.7, 0.08, 0.7, STONE_D, 0.02), bx(0, 0.3, 0, 0.46, 0.44, 0.46, STONE, 0.03), bx(0, 0.54, 0, 0.54, 0.05, 0.54, STONE_D, 0.015),
                bx(0, 0.3, 0.232, 0.3, 0.14, 0.012, TEAL_D, 0.003), xf(lathe(drop, n=14, smooth=True, col=WATER), T(0, 0.58, 0)))

def flag():         # pole at the origin, flag towards +z, 1.24 tall
    return join(lathe([(0.03, 0), (0.03, 0.03), (0.012, 0.04), (0.012, 1.2), (0, 1.2)], n=6, cols=[STONE_D, STONE_D, STEEL, STEEL]),
                xf(_ball(ORANGE), T(0, 1.215, 0) @ S(0.025)), bx(0, 1.0, 0.3, 0.012, 0.32, 0.55, TEAL), bx(0, 1.0, 0.3, 0.016, 0.09, 0.55, WHITE),
                xf(_ball(WATER), T(0, 1.0, 0.2) @ S(0.012, 0.07, 0.07)))

WORLD = {'own-tree': tree, 'own-grass': bush, 'own-building-small-a': house_a, 'own-building-small-d': house_d, 'own-building-small-b': house_b,
         'own-building-small-c': house_c, 'own-building-garage': garage, 'own-vehicle-truck-red': truck_red, 'own-vehicle-truck-yellow': truck_yellow,
         'own-pavement-fountain': fountain, 'own-statue': statue, 'own-flag': flag}
ALL = {**CHAR, **GEAR, **WORLD}
if __name__ == '__main__':
    import sys, os, struct
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for k, f in ALL.items():
        t, size = save(f(), f'{out}/{k}.bytes')
        b = open(f'{out}/{k}.bytes', 'rb').read(40)
        print(f'{k:28s}{t:5d} tris  min {np.round(struct.unpack("<6f", b[16:])[:3], 2)} max {np.round(struct.unpack("<6f", b[16:])[3:], 2)}')
