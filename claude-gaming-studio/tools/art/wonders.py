# The world wonders as our own smooth models: the 23 the game already had as blocky shapes.
# Each function returns (model, ground): ground = (width, depth, colour, top) of the plaza it stands on, or None.
# The game loads own-wonder-<kind>.bytes and falls back to its old shapes if the file is missing.
from wkit import *
C, S_ = np.cos, np.sin
def rad(a): return np.radians(a)

def lattice(a, b, k=1.0):
    """Eiffel-style tower: four curved legs, platforms, arches and bracing. a, b: its two colours."""
    ys = [0, 10, 22, 32, 42, 57, 70]; hw = [13, 9.6, 7, 5.3, 4, 2.1, 0.8]; th = [1.5, 1.3, 1.1, 0.9, 0.8, 0.55, 0.4]
    p = []
    for sx in (-1, 1):
        for sz in (-1, 1):
            path = [(sx * w, y, sz * w) for y, w in zip(ys, hw)]
            for i in range(len(ys) - 1):
                p.append(PIPE(path[i:i + 2] if False else [path[i], path[i + 1]], [th[i], th[i + 1]], a if i % 2 == 0 else b, n=6, up=(sx, 0, -sz)))
    for i in range(len(ys) - 1):                       # cross-bracing on each face
        c = a if i % 2 == 0 else b
        for f in range(4):
            def P(w, y, s): v = np.array([s * w, y, w]); return R('y', f * 90)[:3, :3] @ v
            p.append(BEAM(P(hw[i], ys[i], -1), P(hw[i + 1], ys[i + 1], 1), 0.22, c, n=4))
            p.append(BEAM(P(hw[i], ys[i], 1), P(hw[i + 1], ys[i + 1], -1), 0.22, c, n=4))
    p += [B(0, 21, 0, 17, 1.8, 17, b, 0.3), B(0, 41, 0, 10.4, 1.6, 10.4, b, 0.3), B(0, 62, 0, 4.2, 3, 4.2, b, 0.4)]
    for f in range(4):                                 # the great arches between the legs
        arc_ = [(9.6 * C(t), 2 + 10.5 * S_(t), 10.6) for t in np.linspace(rad(12), rad(168), 11)]
        p.append(RY(PIPE(arc_, 0.6, a, n=6, up=(0, 0, 1)), f * 90))
    p += [CY(0, 65, 0, 1.4, 5, b, n=10), LA(0, 70, 0, [(0.5, 0), (0.25, 7), (0.08, 13), (0, 13)], col=GOLD, n=6)]
    return xf(join(*p), S(1, k, 1))

def eiffel(): return lattice((140, 107, 77), (122, 92, 66)), (26, 26, PAVING, 0.3)
def tokyotower(): return lattice((235, 77, 38), (245, 245, 240), 0.85), (24, 24, PAVING, 0.3)

def liberty():
    g = (115, 191, 163); p = []
    for i in range(11):                                # the star-shaped fort
        a0 = i * 360 / 11
        pts = [(11 * C(rad(a0 + s * 16.4)), y, 11 * S_(rad(a0 + s * 16.4))) for s in (-1, 1) for y in (0, 3)] + [(18 * C(rad(a0)), y, 18 * S_(rad(a0))) for y in (0, 3)] + [(0, 0, 0), (0, 3, 0)]
        p.append(hull(pts, STONE))
    p += [B(0, 3, 0, 11, 10, 11, STONE, 0.35), B(0, 13, 0, 12, 1.2, 12, sh(STONE, 0.95), 0.25), B(0, 14.2, 0, 8.6, 1.8, 8.6, STONE, 0.25)]
    for f in range(4): p.append(radial(arch(2.2, 5, 0.3, sh(STONE, 0.7)), f * 90, 5.45, 4))
    p.append(LA(0, 16, 0, [(3.3, 0), (3.0, 3), (2.5, 8), (2.1, 12), (1.9, 13.4), (1.1, 14.4), (0.7, 14.9), (0, 14.9)], col=g, n=16, smooth=True))   # robe
    p.append(SPH(0, 32.2, 0, 1.45, g, 1.15))                                                                                          # head
    for i in range(7):                                 # crown rays
        a = rad(-60 + i * 20); d = np.array([S_(a) * 0.9, 0.55, -C(a) * 0.9])
        p.append(BEAM(np.array([0, 33.1, 0]) + d * 1.1, np.array([0, 33.1, 0]) + d * 3.1, 0.2, g, n=5, r1=0.03))
    p.append(PIPE([(1.5, 28.6, 0), (2.6, 32, -0.2), (3.3, 36, -0.3), (3.5, 38.5, -0.3)], [0.9, 0.75, 0.6, 0.5], g, n=8, up=(0, 0, 1)))   # raised arm
    p += [LA(3.5, 38.3, -0.3, [(0.35, 0), (0.9, 1.2), (0.9, 1.5), (0, 1.5)], col=g, n=10), ONION(3.5, 39.7, -0.3, 0.8, 2.6, col=GOLD, n=10)]
    p.append(PIPE([(-1.5, 28.6, 0), (-2.5, 26.5, -0.9), (-2.3, 25, -1.6)], [0.85, 0.7, 0.55], g, n=8, up=(0, 0, 1)))
    p.append(xf(box(1.0, 3.6, 2.2, 0.15, sh(g, 0.92)), T(-2.9, 26.2, -1.5) @ R('z', 12)))                                                # tablet
    return join(*p), (40, 40, LAWN, 0.3)

def pyramids():
    p = [pyr(0, -6, 0, 64 * 1.19, 48, SAND), pyr(-60, -6, -55, 58 * 1.2, 46, sh(SAND, 0.97)), pyr(-105, -6, -105, 30 * 1.3, 26, sh(SAND, 0.94))]
    p.append(pyr(-60, 28, -55, 58 * 0.26 * 0.99, 12.2, (245, 232, 200)))            # the smooth casing left on Khafre's tip
    for i, (x, z) in enumerate([(-128, -128), (-142, -128), (-156, -128)]): p.append(pyr(x, -3, z, 11, 10, sh(SAND, 0.92)))
    # The Sphinx, guarding the way in (it faces +z here).
    s = sh(SAND, 0.95)
    p += [B(55, -1, 30, 6, 5.5, 20, s, 0.9), B(53, -1, 43.5, 1.8, 2.4, 8, s, 0.5), B(57, -1, 43.5, 1.8, 2.4, 8, s, 0.5), B(55, 3, 37.5, 4.2, 6, 4.4, SAND, 0.8)]
    p.append(xf(hull([(-3.4, 0, 0), (3.4, 0, 0), (-2.2, 5.6, 0), (2.2, 5.6, 0), (-3.4, 0, -1.6), (3.4, 0, -1.6), (-2.2, 5.6, -2.4), (2.2, 5.6, -2.4)], sh(SAND, 0.9)), T(55, 4.4, 38.4)))
    return join(*p), None

def taj():
    p = [B(0, 0.3, -32, 6, 0.25, 44, POOL), B(0, 0, 14, 44, 4, 44, MARBLE, 0.4), B(0, 4, 14, 24, 16, 24, MARBLE, 0.5)]
    for sx in (-1, 1):                                  # chamfered corners of the main block
        for sz in (-1, 1): p.append(AT(B(0, 4, 0, 6.5, 16, 6.5, MARBLE, 0.4), sx * 11.2, 0, 14 + sz * 11.2, 45))
    niche = sh(MARBLE, 0.8)
    for f in range(4):
        q = [arch(8.4, 13, 0.5, niche, True)] + [AT(arch(2.6, 4.6, 0.4, niche, True), sx * 8.2, y) for sx in (-1, 1) for y in (1.2, 8)]
        p.append(xf(radial(join(*q), f * 90 + 90, 11.95, 4.6), T(0, 0, 14)))
        p.append(xf(radial(B(0, 0, 0, 11, 3.2, 0.8, MARBLE, 0.2), f * 90 + 90, 12.0, 18.2), T(0, 0, 14)))
    p += [CY(0, 20, 14, 7, 5, MARBLE, n=20), LA(0, 25, 14, [(7, 0), (8.5, 3.5), (8.3, 8), (6.2, 12.5), (3, 16), (0.9, 18), (0.3, 19.5), (0, 19.5)], col=MARBLE, n=24, smooth=True),
          LA(0, 44.3, 14, [(0.25, 0), (0.5, 0.8), (0.15, 1.6), (0.3, 2.4), (0, 3.6)], col=GOLD, n=8, smooth=True)]
    for x, z in ((-8, 6), (8, 6), (-8, 22), (8, 22)):
        for i in range(6): p.append(CY(x + 1.9 * C(i * 1.047), 20, z + 1.9 * S_(i * 1.047), 0.28, 3, MARBLE, n=6))
        p += [CY(x, 23, z, 2.5, 0.5, MARBLE, n=12), ONION(x, 23.5, z, 2.4, 4.4, col=MARBLE, n=14)]
    for x, z in ((-20, -6), (20, -6), (-20, 34), (20, 34)):
        p.append(LA(x, 4, z, [(1.5, 0), (1.35, 9.6), (2.0, 9.9), (2.0, 10.4), (1.3, 10.6), (1.2, 19), (1.85, 19.3), (1.85, 19.8), (1.15, 20), (1.05, 27.4), (1.8, 27.7), (1.8, 28.2), (0, 28.2)], col=MARBLE, n=12))
        for i in range(6): p.append(CY(x + 1.3 * C(i * 1.047), 32.2, z + 1.3 * S_(i * 1.047), 0.2, 2.2, MARBLE, n=5))
        p.append(ONION(x, 34.4, z, 1.7, 3, col=MARBLE, n=12))
    return join(*p), (70, 90, LAWN, 0.3)

def colosseum():
    tr = (219, 194, 148); dk = (102, 87, 71); p = []
    lower = [(30, 0), (30, 11), (28.6, 11), (28.6, 9.5), (18, 2.6), (18, 1), (0, 1)]
    upper = [(30, 11), (30, 19.5), (30.4, 19.5), (30.4, 20.3), (28.6, 20.3), (28.6, 15.4), (27.8, 9.5)]
    p.append(LA(0, 0, 0, lower, cols=[tr, tr, sh(tr, 0.9), sh(tr, 0.86), sh(tr, 0.8), (224, 199, 143)], n=40, fillet=0.03))
    p.append(cut(LA(0, 0, 0, upper, cols=[tr, tr, tr, tr, sh(tr, 0.9), sh(tr, 0.86)], n=40, fillet=0.03), 150, 50))
    for a in (50, 150):                                # close the broken ends of the tall wall
        t = rad(a); p.append(hull([((r_ * C(t + e), y, r_ * S_(t + e))) for r_ in (28.6, 30.2) for y in (11, 20.2) for e in (-0.004, 0.004)], sh(tr, 0.82)))
    for i in range(48):                                # three tiers of arches
        a = i * 7.5 + 3.75
        tiers = 3 if not 50 < a < 150 else 1
        for t in range(tiers): p.append(radial(arch(2.3, 3.7, 0.3, dk), a, 29.95, 1.2 + t * 5.6))
        if tiers == 3: p.append(radial(B(0, 0, 0, 1.1, 1.4, 0.3, dk), a, 30.0, 17.6))
    p += [ring(lambda i: B(0, 0, 0, 1.2, 1.3, 26, sh(tr, 0.75)), 0, 0)] if False else []
    for i in range(5): p.append(B(-12 + i * 6, 1, 0, 0.5, 0.7, 20 - abs(i - 2) * 3, sh(tr, 0.7)))      # the open cellars under the arena
    return xf(join(*p), S(1, 1, 0.8)), (70, 60, PAVING, 0.3)

def bigben():
    s = SANDST; p = [B(0, 0, 0, 8, 40, 8, s, 0.3), B(0, 40, 0, 9.2, 2.2, 9.2, s, 0.3), B(0, 29.2, 0, 8.8, 0.8, 8.8, sh(s, 0.9), 0.15), B(0, 37.2, 0, 8.8, 0.8, 8.8, sh(s, 0.9), 0.15)]
    for f in range(4):
        face = join(xf(lathe([(2.9, 0), (2.9, 0.3), (0, 0.3)], n=20, cols=[GOLD, (247, 242, 217)], fillet=0), R('x', 90)),
                    B(0, -0.15, 0.32, 0.22, 2.3, 0.1, DARK), xf(B(0, -0.12, 0.34, 0.2, 1.6, 0.1, DARK), R('z', -65)))
        p.append(radial(face, f * 90 + 90, 4.02, 33.6))
        for i in range(4): p.append(radial(B(-2.7 + i * 1.8, 0, 0, 0.5, 26, 0.3, sh(s, 0.85)), f * 90 + 90, 4.02, 2))
        p.append(radial(B(0, 0, 0, 0.9, 5, 0.9, s, 0.2), f * 90 + 45, 6.0, 40))
        p.append(radial(CN(0, 5, 0, 0.6, 2.4, SLATE, n=6), f * 90 + 45, 6.0, 40))
    p += [pyr(0, 42.2, 0, 8.6, 5, SLATE, top=0.62), B(0, 47.2, 0, 5.2, 2.4, 5.2, s, 0.2), pyr(0, 49.6, 0, 5.6, 8.5, SLATE), LA(0, 57.6, 0, [(0.25, 0), (0.12, 3.4), (0, 3.4)], col=GOLD, n=6)]
    # The Houses of Parliament alongside.
    p += [B(42, 0, 0, 62, 16, 16, s, 0.3), gable(42, 16, 0, 62, 16, 5, SLATE), B(79, 0, 0, 12, 34, 12, s, 0.3)]
    for t in range(7):
        x = 14 + t * 9.4
        p += [B(x, 0, -8.1, 1.8, 21, 1.8, s, 0.2), CN(x, 21, -8.1, 1.3, 3.4, SLATE, n=6)]
        if t < 6:
            for u in range(3): p.append(B(x + 2.4 + u * 2.3, 2, -8.05, 1.1, 11, 0.2, (120, 110, 90)))
    for sx in (-1, 1):
        for sz in (-1, 1): p += [B(79 + sx * 5.6, 0, sz * 5.6, 1.8, 39, 1.8, s, 0.2), CN(79 + sx * 5.6, 39, sz * 5.6, 1.3, 4, SLATE, n=6)]
    p.append(LA(79, 34, 0, [(0.3, 0), (0.2, 9), (0, 9)], col=DARK, n=5))
    return join(*p), (100, 30, PAVING, 0.3, 36)

def opera():
    w = (250, 247, 240); gl = (120, 90, 70); p = []
    for x in (-10.5, 10.5):
        p += [AT(shell(21, 7.8, 104, w, gl), x, 4, -25), AT(shell(17.5, 6.8, 102, w, gl), x, 4, -9), AT(shell(13.5, 5.6, 100, w, gl), x, 4, 5)]
        p.append(AT(shell(10, 4.6, 98, w, gl), x, 4, 30, 180))
    p.append(AT(shell(8, 3.6, 100, w, gl), 0, 4, -37))
    p.append(B(0, 0, -44, 30, 3.4, 9, (209, 158, 128), 0.3))       # the great steps down to the harbour
    return join(*p), (46, 84, (209, 158, 128), 4.0)

def redeemer():
    s = (237, 235, 222); p = [B(0, 0, 0, 7, 8, 7, sh(s, 0.93), 0.4)]
    p.append(LA(0, 8, 0, [(2.7, 0), (2.4, 5), (2.1, 12), (2.0, 16.5), (2.3, 18), (1.2, 19.6), (0.7, 20.2), (0, 20.2)], col=s, n=16, smooth=True))
    xs = np.linspace(-14, 14, 9)
    p.append(xf(tube([(x, 25.7 - 0.012 * x * x * 0.2, 0) for x in xs], [0.55, 0.9, 1.15, 1.5, 1.9, 1.5, 1.15, 0.9, 0.55], [0.5, 0.8, 1.2, 1.5, 1.6, 1.5, 1.2, 0.8, 0.5], n=12, col=s, up=(0, 1, 0)), T(0, 0, 0)))
    for sx in (-1, 1): p.append(xf(box(4.2, 3.4, 0.7, 0.3, sh(s, 0.97)), T(sx * 8.5, 24.2, 0.1) @ R('z', sx * 4)))   # sleeves
    p += [SPH(0, 29.9, 0, 1.45, s, 1.15), SPH(0, 29.5, 0.3, 1.6, sh(s, 0.95), 1.0)]
    return join(*p), (20, 20, PAVING, 0.3)

def goldengate():
    o = (219, 77, 43); half, tower, deck, span = 140.0, 55.0, 16.0, 70.0
    p = [B(0, deck, 0, 12, 1.6, half * 2, o), B(0, deck + 1.55, 0, 10.4, 0.12, half * 2, (89, 89, 97)), B(5.7, deck + 1.6, 0, 0.3, 1, half * 2, o), B(-5.7, deck + 1.6, 0, 0.3, 1, half * 2, o)]
    for tz in (-span, span):
        p.append(B(0, -30, tz, 17, 33, 9, CONCRETE, 0.6))
        for tx in (-6.5, 6.5):
            p += [B(tx, 3, tz, 3.2, 30, 3.4, o, 0.25), B(tx, 33, tz, 2.7, 13, 3.0, o, 0.25), B(tx, 46, tz, 2.2, 9.5, 2.6, o, 0.25)]
        for y in (deck + 11, deck + 22, deck + 31, tower - 2.5): p.append(B(0, y, tz, 12.4, 2.2, 2.2, o, 0.2))
    for ez in (-half, half):
        for tx in (-5, 5): p.append(B(tx, -30, ez, 2.2, 30 + deck, 2.2, o, 0.2))
    for cx in (-6.5, 6.5):
        zs = np.linspace(-half, half, 41); pts = []
        for z in zs:
            if abs(z) <= span: y = deck + 3 + (tower - deck - 3) * (z / span) ** 2
            else: y = tower + (deck + 2 - tower) * (abs(z) - span) / (half - span)
            pts.append((cx, y, z))
        p.append(PIPE(pts, 0.38, o, n=6, up=(1, 0, 0)))
        for (x, y, z) in pts[1:-1:1]:
            if y > deck + 3.5: p.append(BEAM((cx, deck + 1.6, z), (cx, y, z), 0.09, o, n=4))
    return join(*p), None

def burj():
    s = STEELB; tops = [45, 80, 108, 128, 142]; wing = [14, 11, 8, 5.5, 3.5]; p = []; y0 = 0
    for t in range(5):
        p.append(CY(0, y0, 0, wing[t] * 0.46, tops[t] - y0, sh(s, 0.96), n=18))
        for w in range(3):
            h = tops[t] - y0 - (w + t) % 3 * 4
            lobe = join(B(0, 0, wing[t] * 0.5, wing[t] * 0.52, h, wing[t] * 0.95, sh(s, 1 - w * 0.03), min(1.2, wing[t] * 0.12)),
                        CY(0, 0, wing[t] * 0.98, wing[t] * 0.26, h - 1.5, sh(s, 1.03), n=12))
            p.append(AT(lobe, 0, y0, 0, w * 120))
        y0 = tops[t]
    p.append(LA(0, y0, 0, [(1.4, 0), (0.9, 12), (0.4, 24), (0.1, 34), (0, 34)], col=s, n=10, smooth=True))
    for w in range(3): p.append(AT(B(0, 0.3, 17, 9, 0.3, 14, POOL), 0, 0, 0, w * 120 + 60))
    return join(*p), (50, 50, PAVING, 0.3)

def pisa():
    rr = (191, 89, 64); p = [B(-28, 0, 0, 34, 13, 14, MARBLE, 0.3), gable(-28, 13, 0, 34, 14, 5, rr), B(-28, 0, 0, 12, 15, 22, MARBLE, 0.3), gable(-28, 15, 0, 22, 12, 4, rr, 'z'),
                             CY(-28, 15, 0, 4.2, 5, MARBLE, n=16), DOME(-28, 20, 0, 4.4, rr, 1.25)]
    for i in range(9): p.append(AT(arch(1.6, 6, 0.3, sh(MARBLE, 0.82)), -42 + i * 3.4, 3, -7.05))
    p += [CY(-62, 0, 0, 9, 10, MARBLE, n=24), DOME(-62, 10, 0, 9, rr, 0.9)]        # the baptistery
    prof = []
    for t in range(7):
        y = t * 4.2; r = 4.0 if t else 4.3
        prof += ([(r, y)] if t == 0 else []) + [(r, y + 3.7), (4.5, y + 3.8), (4.5, y + 4.15), (4.0 if t < 6 else 3.1, y + 4.2)]
    prof += [(3.1, 33.4), (3.4, 33.5), (3.4, 33.9), (0, 33.9)]
    tw = [LA(0, 0, 0, prof, col=MARBLE, n=24, fillet=0.02)]
    for t in range(1, 7):
        for i in range(16): tw.append(radial(arch(0.9, 2.9, 0.2, sh(MARBLE, 0.78)), i * 22.5, 3.95, t * 4.2 + 0.5))
    for i in range(12): tw.append(radial(arch(1.1, 4.4, 0.2, sh(MARBLE, 0.78)), i * 30, 4.25, 0.2))
    p.append(xf(join(*tw), T(10, 0, 0) @ R('z', -4)))
    return join(*p), (100, 40, LAWN, 0.3, -22)

def machupicchu():
    rock = ROCK; grass = (107, 184, 77); th = (184, 153, 82); p = []
    for t in range(7): p += [B(0, -30, -10 - t * 7, 80 - t * 4, 30 - t * 2.5 - 0.3, 7.5, rock, 0.3), B(0, -t * 2.5 - 0.35, -10 - t * 7, 79.6 - t * 4, 0.35, 7.3, grass)]
    p += [B(0, -30, 20, 80, 30, 50, rock), B(0, 0, 20, 79.8, 0.3, 49.8, grass)]
    rng = np.random.RandomState(5)
    for hx in range(5):
        for hz in range(3):
            x = -28 + hx * 14 + rng.rand() * 3; z = 6 + hz * 13
            p += [B(x, 0, z, 7, 3.2, 5, sh(rock, 1.05), 0.25), gable(x, 3.2, z, 7.6, 5.6, 3.4, th), B(x, 0.3, z - 2.52, 1.2, 2.1, 0.15, DARK)]
    p.append(LA(0, -10, 85, [(31, 0), (27, 14), (20, 34), (13, 52), (7.5, 66), (3.5, 76), (0, 80)], cols=[(77, 140, 64)] * 4 + [(100, 150, 80), rock], n=18, smooth=True))
    p.append(LA(-42, -10, 70, [(16, 0), (12, 14), (6, 28), (0, 36)], col=(84, 148, 66), n=14, smooth=True))
    return join(*p), None

def stonehenge():
    s = (158, 158, 148); p = []
    for i in range(30):
        a = i * 12
        if i in (7, 19): p.append(radial(xf(box(1.9, 4.6, 1.1, 0.3, s), R('x', 84)), a, 17.5, 0.5)); continue
        p.append(radial(box(1.9, 5.6 + (i % 3) * 0.15, 1.1, 0.3, sh(s, 0.95 + (i % 4) * 0.03)), a, 16, 2.4))
        if i % 2 == 0 and i != 18 and i != 6: p.append(radial(box(4.3, 0.95, 1.2, 0.28, sh(s, 0.93)), a + 6, 16, 5.5))
    for t in range(5):
        a = (0.2 + t * 0.4) * 180 + 90; h = 8.6 if t == 2 else 7.0
        tri = join(xf(box(1.9, h, 1.3, 0.35, s), T(-1.35, h / 2 - 0.6, 0)), xf(box(1.9, h, 1.3, 0.35, s), T(1.35, h / 2 - 0.6, 0)), xf(box(4.9, 1.15, 1.45, 0.3, sh(s, 0.94)), T(0, h - 0.1, 0)))
        p.append(radial(tri, a, 8))
    for i in range(12): p.append(radial(box(0.8, 1.7, 0.6, 0.22, (120, 130, 140)), i * 30 + 8, 11.5, 0.7))     # bluestones
    p.append(xf(box(1.2, 0.7, 4.4, 0.25, sh(s, 0.9)), T(0.5, 0.25, 1.0) @ R('y', 20)))                           # the altar stone
    return join(*p), None

def moai():
    tuff = (115, 102, 92); p = [B(0, 0, 0, 36, 1.6, 6.5, DARKSTONE, 0.3)]
    for i in range(7):
        x = -15 + i * 5; k = 0.9 + (i % 3) * 0.1
        m = [loft([(0, 2.5, 1.7), (1.6, 2.8, 2.0), (3.4, 2.3, 1.8), (3.7, 1.7, 1.5)], n=16, col=tuff),
             loft([(3.5, 1.6, 1.5), (4.2, 2.1, 2.1), (6.4, 2.2, 2.3), (7.5, 2.0, 2.0), (7.9, 1.6, 1.6)], n=16, col=tuff),
             B(0, 6.25, -1.1, 2.3, 0.55, 0.7, sh(tuff, 0.88), 0.2), B(0, 4.5, -1.3, 0.62, 1.9, 0.8, sh(tuff, 0.96), 0.25), B(0, 3.95, -1.05, 1.3, 0.3, 0.3, sh(tuff, 0.85), 0.1)]
        for sx in (-1, 1): m.append(B(sx * 1.12, 4.3, 0.1, 0.25, 2.2, 0.7, sh(tuff, 0.92), 0.1))
        if i == 3: m.append(CY(0, 7.9, 0, 1.25, 1.3, (158, 77, 51), n=14, fillet=0.2))
        p.append(xf(join(*m), T(x, 1.6, 0) @ S(k)))
    return join(*p), (40, 14, (102, 166, 77), 0.3)

def spaceneedle():
    w = (240, 240, 235); o = (242, 140, 51); p = []
    ys = [0, 8, 17, 26, 36, 46]; rs = [9.4, 5.6, 2.9, 1.8, 2.3, 3.6]
    for s in range(3):
        for da in (-9, 9):
            path = [(r * C(rad(s * 120 + da * (r / 9.4))), y, r * S_(rad(s * 120 + da * (r / 9.4)))) for y, r in zip(ys, rs)]
            p.append(PIPE(path, [0.7, 0.6, 0.5, 0.45, 0.45, 0.5], w, n=8, up=(0, 0, 1) if s else (0, 0, 1)))
    p.append(CY(0, 0, 0, 1.3, 46, w, n=12))
    p.append(LA(0, 44, 0, [(1.4, 0), (4, 0.8), (10.4, 4), (11, 4.6), (11, 5.2), (10.2, 5.8), (9.6, 6.6), (4, 9.2), (1.2, 10), (0, 10)], cols=[w, w, o, o, o, (70, 80, 95), w, w, w], n=28, fillet=0.15))
    p += [CY(0, 26, 0, 3.2, 0.7, w, n=16), LA(0, 54, 0, [(0.5, 0), (0.25, 8), (0.08, 13), (0, 13)], col=w, n=6)]
    return join(*p), (30, 30, PAVING, 0.3)

def cntower():
    c = CONCRETE; p = [LA(0, 0, 0, [(3.6, 0), (2.9, 40), (2.5, 84), (2.4, 104), (0, 104)], col=c, n=12, smooth=True)]
    for w in range(3):
        fin = hull([(sx * 1.4, 0, 1.5) for sx in (-1, 1)] + [(sx * 1.4, 0, 9.5) for sx in (-1, 1)] + [(sx * 0.9, 82, 1.5) for sx in (-1, 1)] + [(sx * 0.9, 82, 3.0) for sx in (-1, 1)], c)
        p.append(RY(fin, w * 120))
    p.append(LA(0, 82, 0, [(2.6, 0), (7.4, 2.2), (7.9, 3), (8.3, 4.4), (8.3, 7.4), (7.0, 8.6), (6.6, 10.8), (3, 12), (0, 12)], cols=[c, c, (255, 255, 255), (64, 82, 102), c, c, c, c], n=28, fillet=0.2))
    p += [CY(0, 102, 0, 3.4, 3.2, c, n=16), LA(0, 105, 0, [(1.1, 0), (0.8, 12), (0.5, 24), (0.2, 36), (0, 36)], cols=[RED, WHITE_, RED, WHITE_], n=8)]
    return join(*p), (30, 30, PAVING, 0.3)

def stbasil():
    br = BRICK; cr = CREAM; p = [B(0, 0, 0, 26, 8, 26, br, 0.4), B(0, 8, 0, 26.4, 0.6, 26.4, cr, 0.2)]
    p += [CY(0, 8.6, 0, 4.6, 15, br, n=8, cap=cr), CY(0, 23, 0, 5.0, 1, cr, n=8), LA(0, 24, 0, [(4.6, 0), (1.2, 14.5), (1.0, 16), (0, 16)], cols=[(77, 153, 89), cr, cr], n=8),
          ONION(0, 39.6, 0, 1.6, 3.6, col=GOLD, n=12), LA(0, 43, 0, [(0.12, 0), (0.12, 2), (0, 2)], col=GOLD, n=4)]
    dom = [((51, 158, 97), (240, 220, 80)), ((230, 191, 51), (60, 120, 60)), ((51, 128, 209), (245, 245, 240)), ((230, 82, 64), (245, 245, 240)),
           ((64, 178, 166), (230, 200, 60)), ((242, 153, 38), (60, 130, 80)), ((140, 89, 191), (245, 235, 200)), ((217, 51, 89), (60, 110, 190))]
    for d in range(8):
        a = d * 45; big = d % 2 == 0; r = 10.2 if big else 8.2; hgt = 16 if big else 11; w = 2.5 if big else 1.8
        x, z = C(rad(a)) * r, S_(rad(a)) * r; c1, c2 = dom[d]
        p += [CY(x, 8.6, z, w, hgt, cr if d % 4 else br, n=10, cap=cr), CY(x, 8.6 + hgt, z, w * 1.12, 0.6, cr, n=10)]
        pr = [(w * 0.75, 0), (w * 1.05, w * 0.3), (w * 1.3, w * 0.8), (w * 1.22, w * 1.3), (w * 0.85, w * 1.9), (w * 0.4, w * 2.5), (w * 0.12, w * 2.95), (0, w * 3.3)]
        p.append(LA(x, 9.2 + hgt, z, pr, cols=[c1, c2, c1, c2, c1, c2, c1], n=14, smooth=True))
        p.append(LA(x, 9.2 + hgt + w * 3.2, z, [(0.1, 0), (0.1, 1.5), (0, 1.5)], col=GOLD, n=4))
    for f in range(4): p.append(radial(arch(3, 5, 0.3, sh(br, 0.6)), f * 90 + 45 if False else f * 90, 13.05, 1))
    return join(*p), (40, 40, PAVING, 0.3)

def sagrada():
    st = (204, 178, 133); p = [B(0, 0, 0, 50, 24, 24, st, 0.5), gable(0, 24, 0, 50, 24, 8, sh(st, 0.9))]
    fruit = [(242, 102, 51), (242, 204, 51), (77, 178, 102), (217, 64, 115)]; f = 0
    def spire(x, z, h, r):
        q = [LA(x, 0, z, [(r, 0), (r, h * 0.5), (r * 0.82, h * 0.72), (r * 0.5, h * 0.88), (r * 0.2, h * 0.97), (0, h)], col=st, n=14, smooth=True)]
        for t in range(6):
            for i in range(6): q.append(xf(radial(B(0, 0, 0, 0.5, 1.6, 0.25, sh(st, 0.6)), i * 60 + t * 30, r * (1 - 0.05 * t) * 0.98, h * (0.34 + t * 0.085)), T(x, 0, z)))
        return q
    for z in (-14, 14):
        for s in range(4):
            x = -15 + s * 10; h = 58 if s in (1, 2) else 50
            p += spire(x, z, h, 2.3); p.append(SPH(x, h + 0.6, z, 1.1, fruit[f % 4])); f += 1
    for i in range(7): p.append(AT(arch(2.6, 12, 0.3, (90, 110, 150), True), -18 + i * 6, 5, -12.05))
    p += spire(0, 0, 84, 4.6); p += [SPH(0, 85, 0, 1.6, (245, 245, 250)), B(0, 86, 0, 0.4, 5, 0.4, GOLD), B(0, 89, 0, 3, 0.4, 0.4, GOLD)]
    return join(*p), (70, 50, PAVING, 0.3)

def angkor():
    sd = (158, 148, 122); p = []
    for (x, z, w, d) in ((0, -62, 132, 14), (0, 62, 132, 14), (-62, 0, 14, 110), (62, 0, 14, 110)): p.append(B(x, -1.5, z, w, 1.6, d, POOL))
    p += [B(0, -1.5, -62, 8, 1.9, 14, sd), B(0, 0, 0, 70, 5, 70, sd, 0.3), B(0, 5, 0, 52, 5, 52, sh(sd, 0.97), 0.3), B(0, 10, 0, 36, 6, 36, sh(sd, 0.94), 0.3)]
    def lotus(x, z, y0, h, w):
        pr = [(w * 0.5, 0), (w * 0.5, h * 0.18), (w * 0.44, h * 0.2), (w * 0.46, h * 0.34), (w * 0.38, h * 0.36), (w * 0.4, h * 0.5), (w * 0.31, h * 0.52), (w * 0.32, h * 0.66), (w * 0.22, h * 0.7),
              (w * 0.2, h * 0.82), (w * 0.1, h * 0.9), (w * 0.03, h * 0.98), (0, h)]
        return LA(x, y0, z, pr, col=sh(sd, 1.03), n=16, fillet=0.2)
    p.append(lotus(0, 0, 16, 32, 12))
    for x, z in ((-15, -15), (15, -15), (-15, 15), (15, 15)): p.append(lotus(x, z, 10, 22, 8))
    for x, z in ((-32, -32), (32, -32), (-32, 32), (32, 32)): p.append(lotus(x, z, 5, 11, 5))
    for f in range(4):                                  # galleries with long roofs, and a gate in each side
        p.append(RY(join(gable(0, 5, -33, 56, 4, 2.2, sh(sd, 0.85)), gable(0, 10, -24.5, 40, 3.6, 2, sh(sd, 0.85)), B(0, 0, -35.2, 7, 8, 3, sd, 0.3), AT(arch(3, 5, 0.3, DARK), 0, 0.5, -36.75)), f * 90))
    return join(*p), (100, 100, LAWN, 0.3)

def chichen():
    lm = (219, 209, 178); p = []
    for t in range(9):
        w = 52 - t * 4.2; p.append(B(0, t * 2.8, 0, w, 2.8, w, sh(lm, 1 if t % 2 == 0 else 0.94), 0.2))
    for s in range(4):
        st = hull([(sx * 4, 0, -28.5) for sx in (-1, 1)] + [(sx * 4, 25.2, -7.6) for sx in (-1, 1)] + [(sx * 4, 0, -7.6) for sx in (-1, 1)], sh(lm, 1.05))
        rails = [hull([(sx * 4.6 + e, 0, -29) for e in (-0.5, 0.5)] + [(sx * 4.6 + e, 26, -7.4) for e in (-0.5, 0.5)] + [(sx * 4.6 + e, 0, -7.4) for e in (-0.5, 0.5)], sh(lm, 0.9)) for sx in (-1, 1)]
        heads = [B(sx * 4.6, 0, -29.6, 1.5, 1.4, 2, (150, 170, 130), 0.3) for sx in (-1, 1)] if s == 0 else []
        p.append(RY(join(st, *rails, *heads), s * 90))
        p.append(RY(AT(B(0, 0, 0, 3.6, 4.4, 0.3, DARK), 0, 25.6, -7.1), s * 90))
    p += [B(0, 25.2, 0, 14, 6.4, 14, lm, 0.3), B(0, 31.6, 0, 14.6, 1.2, 14.6, sh(lm, 0.9), 0.2)]
    return join(*p), (70, 70, LAWN, 0.3)

def parthenon():
    m = MARBLE; p = [B(0, s * 0.8, 0, 40 - s * 1.4, 0.8, 20 - s * 1.4, m) for s in range(3)]
    y0, h = 2.4, 11.0
    def col(x, z): return LA(x, y0, z, [(0.95, 0), (0.8, h - 0.9), (1.1, h - 0.6), (1.1, h), (0, h)], col=m, n=10, fillet=0.02)
    for i in range(14):
        x = -17 + i * (34 / 13); p += [col(x, -7.6), col(x, 7.6)]
    for i in range(1, 7):
        z = -7.6 + i * (15.2 / 7); p += [col(-17, z), col(17, z)]
    p += [B(0, y0, 0, 26, 9, 10, sh(m, 0.86)), B(0, y0 + h, 0, 37, 2.2, 18, m), B(0, y0 + h + 0.9, 0, 37.1, 0.5, 18.1, sh(m, 0.85)), gable(0, y0 + h + 2.2, 0, 37, 18, 3.2, sh(m, 0.96), 'z')]
    p[-1] = xf(prism([(-9, 0), (9, 0), (0, 3.2)], 37, sh(m, 0.96)), T(0, y0 + h + 2.2, 0))
    return join(*p), (50, 30, (194, 178, 143), 0.3)

def brandenburg():
    s = (224, 209, 173); bz = (89, 158, 128); p = []
    for i in range(6):
        x = -15 + i * 6
        for z in (-4, 4): p.append(LA(x, 0, z, [(1.0, 0), (0.85, 13), (1.15, 13.4), (1.15, 14), (0, 14)], col=s, n=10, fillet=0.02))
    for sx in (-1, 1): p += [B(sx * 18.5, 0, 0, 4, 14, 10, s, 0.3), B(sx * 25.5, 0, 0, 9, 9, 8, s, 0.3), gable(sx * 25.5, 9, 0, 8, 9, 2.4, sh(s, 0.9), 'z')]
    p += [B(0, 14, 0, 40, 3, 11, s, 0.25), B(0, 17, 0, 18, 3.6, 8, s, 0.25)]
    for hz in range(4):
        x = -3 + hz * 2
        p += [blob((x, 22.1, -2.2), (0.55, 0.8, 1.3), seed=hz, col=bz, jitter=0.05), PIPE([(x, 22.4, -3.1), (x, 23.6, -3.6), (x, 23.9, -4.2)], [0.4, 0.3, 0.22], bz, n=6, up=(1, 0, 0))]
        for dz in (-3.0, -1.5): p.append(BEAM((x, 20.6, dz), (x, 22, dz), 0.14, bz, n=4))
    p += [B(0, 20.6, 0.3, 2.6, 1.6, 2.6, bz, 0.4), LA(0, 22.2, 0.3, [(0.6, 0), (0.45, 2), (0.3, 2.6), (0, 2.6)], col=bz, n=8, smooth=True), SPH(0, 25.2, 0.3, 0.4, bz), BEAM((0.7, 22.5, 0.3), (0.7, 27.4, 0.3), 0.07, bz, n=4)]
    return join(*p), (64, 30, PAVING, 0.3)

WONDERS = {'eiffel': eiffel, 'tokyotower': tokyotower, 'liberty': liberty, 'pyramids': pyramids, 'tajmahal': taj, 'colosseum': colosseum, 'bigben': bigben, 'operahouse': opera,
           'redeemer': redeemer, 'goldengate': goldengate, 'burj': burj, 'pisa': pisa, 'machupicchu': machupicchu, 'stonehenge': stonehenge, 'moai': moai, 'spaceneedle': spaceneedle,
           'cntower': cntower, 'stbasil': stbasil, 'sagrada': sagrada, 'angkorwat': angkor, 'chichenitza': chichen, 'parthenon': parthenon, 'brandenburg': brandenburg}
LABELS = {'eiffel': 'Eiffel Tower', 'tokyotower': 'Tokyo Tower', 'liberty': 'Statue of Liberty', 'pyramids': 'Pyramids of Giza', 'tajmahal': 'Taj Mahal', 'colosseum': 'Colosseum', 'bigben': 'Big Ben',
          'operahouse': 'Sydney Opera House', 'redeemer': 'Christ the Redeemer', 'goldengate': 'Golden Gate Bridge', 'burj': 'Burj Khalifa', 'pisa': 'Leaning Tower of Pisa',
          'machupicchu': 'Machu Picchu', 'stonehenge': 'Stonehenge', 'moai': 'Moai', 'spaceneedle': 'Space Needle', 'cntower': 'CN Tower', 'stbasil': "St Basil's Cathedral",
          'sagrada': 'Sagrada Familia', 'angkorwat': 'Angkor Wat', 'chichenitza': 'Chichen Itza', 'parthenon': 'Parthenon', 'brandenburg': 'Brandenburg Gate'}

def full(fn):
    """The model as the game gets it: with its plaza reaching down into the ground."""
    m, g = fn()
    if g is None: return m
    return join(plinth(g[0], g[1], g[2], g[3], x=g[4] if len(g) > 4 else 0), m)
def shown(fn):
    """The model for a preview picture: on a thin slab instead."""
    m, g = fn()
    if g is None: return m
    return join(B(g[4] if len(g) > 4 else 0, g[3] - 1.2, 0, g[0], 1.2, g[1], g[2]), m)

def bake(table, labels, argv, cols=6):
    import os
    out = argv[1]; os.makedirs(out, exist_ok=True)
    only = argv[3].split(',') if len(argv) > 3 else None
    for k, f in table.items():
        if only and k not in only: continue
        t, size = save(full(f), f'{out}/own-wonder-{k}.bytes'); print(f'{k:14s}{t:6d} {np.round(size, 1)}')
    if len(argv) > 2 and argv[2] != '-':
        from sheets import sheet
        ks = [k for k in table if not only or k in only]
        sheet([((lambda f=table[k]: shown(f)), labels[k], -28, 14) for k in ks], argv[2], 380, 400, cols=min(cols, len(ks)), SS=1)

if __name__ == '__main__':
    import sys
    bake(WONDERS, LABELS, sys.argv)
