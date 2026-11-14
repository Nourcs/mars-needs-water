# Trees and plants for the world's biomes. Authored in metres at natural size, base at y = 0. Each is drawn in
# its hundreds, so every model stays near 300 triangles (exact=True keeps the segment counts as given).
from forge import *
from world import GREEN, GREEN_D, GREEN_L, BARK

LEAF = (245, 245, 245)        # white foliage: the game tints it per tree (oak greens, autumn reds, cherry pink)
def trunk(r0, r1, h, col=BARK, n=6): return lathe([(r0 * 1.25, 0), (r0, h * 0.12), (r1, h), (0, h)], n=n, col=col, exact=True, smooth=True)
def limb_(path, r0, r1, col=BARK, n=5): return tube(path, np.linspace(r0, r1, len(path)), n=n, col=col, caps=False) if False else _tube(path, r0, r1, col, n)
def _tube(path, r0, r1, col, n):
    import forge
    old = forge.SMOOTH; forge.SMOOTH = False            # keep the low segment count
    d = np.abs(np.asarray(path[-1], float) - np.asarray(path[0], float)); up = np.eye(3)[int(np.argmin(d))]   # never along the tube
    try: return tube(path, np.linspace(r0, r1, len(path)), n=n, col=col, up=up)
    finally: forge.SMOOTH = old

def round_tree():   # oak, maple, cherry... (tinted in the game)
    return join(trunk(0.3, 0.2, 3.4), blob((0, 5.0, 0), (2.6, 2.1, 2.6), seed=1, col=LEAF, shade=0.35),
                blob((1.5, 4.1, 0.7), (1.7, 1.4, 1.7), seed=2, col=LEAF, shade=0.35), blob((-1.4, 4.4, -0.8), (1.8, 1.5, 1.8), seed=3, col=LEAF, shade=0.35))

def birch():
    t = lathe([(0.2, 0), (0.15, 0.8), (0.14, 2.0), (0.14, 2.25), (0.12, 4.0), (0.12, 4.2), (0.09, 6.2), (0.05, 7.6), (0, 7.6)], n=6, exact=True, smooth=True,
              cols=[(70, 66, 62), (236, 233, 226), (60, 58, 56), (236, 233, 226), (60, 58, 56), (236, 233, 226), (236, 233, 226), (236, 233, 226)])
    c = (156, 204, 96)
    return join(t, blob((0, 7.0, 0), (1.5, 2.2, 1.5), seed=4, col=c), blob((0.7, 5.3, 0.3), (1.2, 1.5, 1.2), seed=5, col=c), blob((-0.7, 5.8, -0.4), (1.2, 1.6, 1.2), seed=6, col=c))

def spruce():       # snowy conifer of the far north and the high mountains
    parts = [trunk(0.26, 0.16, 2.2)]
    for i, (y, r, h) in enumerate([(1.4, 1.9, 3.6), (3.8, 1.5, 3.4), (6.2, 1.05, 3.6)]):
        pr = [(0, 0.15), (r * 0.7, 0.0), (r * 0.97, 0.25), (r, 0.6), (r * 0.72, h * 0.38), (r * 0.3, h * 0.78), (0, h)]
        parts.append(xf(lathe(pr, n=9, cols=[(30, 84, 62), (30, 84, 62), (44, 106, 76), (236, 242, 246), (236, 242, 246), (236, 242, 246)], exact=True, smooth=True), T(0, y, 0)))
    return join(*parts)

def palm():
    path = [(0, 0, 0), (0.25, 2.0, 0), (0.7, 4.2, 0.1), (1.3, 6.2, 0.15), (1.7, 7.6, 0.2)]
    parts = [_tube(path, 0.26, 0.16, (158, 128, 92), 6)]
    top = np.array(path[-1])
    for i in range(8):
        a = i * np.pi / 4 + 0.2; d = np.array([np.cos(a), 0, np.sin(a)]); up = np.array([0, 1.0, 0]); lift = 0.75 if i % 2 else 0.4
        pts = [top, top + d * 1.2 + up * lift, top + d * 2.5 + up * (lift - 0.25), top + d * 3.5 - up * (1.1 - lift)]
        import forge
        old = forge.SMOOTH; forge.SMOOTH = False
        parts.append(tube(pts, [0.3, 0.52, 0.42, 0.04], [0.05, 0.05, 0.04, 0.02], n=4, col=(74, 156, 66) if i % 2 else (58, 134, 58)))
        forge.SMOOTH = old
    for a in (0.5, 2.6, 4.6): parts.append(blob(top + np.array([np.cos(a) * 0.3, -0.3, np.sin(a) * 0.3]), (0.2, 0.22, 0.2), col=(120, 84, 50), sub=0, jitter=0))
    return join(*parts)

def acacia():       # flat-topped savanna tree
    c = (104, 138, 62)
    parts = [_tube([(0, 0, 0), (0.15, 1.6, 0), (0.1, 3.0, 0.1)], 0.3, 0.2, (112, 90, 68), 6)]
    for a, r in ((0.3, 2.2), (2.4, 2.0), (4.4, 1.8)):
        e = (0.1 + np.cos(a) * r, 4.9, 0.1 + np.sin(a) * r)
        parts.append(_tube([(0.1, 2.9, 0.1), (0.1 + np.cos(a) * r * 0.5, 4.2, 0.1 + np.sin(a) * r * 0.5), e], 0.16, 0.07, (112, 90, 68), 4))
    parts += [blob((0.1, 5.3, 0.1), (3.6, 0.7, 3.2), seed=7, col=c, jitter=0.1), blob((1.8, 5.0, 1.0), (2.2, 0.55, 2.0), seed=8, col=c, jitter=0.1),
              blob((-1.7, 5.05, -0.9), (2.3, 0.55, 2.1), seed=9, col=c, jitter=0.1)]
    return join(*parts)

def baobab():
    bark = (136, 114, 98)
    parts = [lathe([(1.9, 0), (1.6, 0.6), (1.75, 2.5), (1.5, 5.0), (1.0, 6.4), (0.5, 7.0), (0, 7.1)], n=9, col=bark, exact=True, smooth=True)]
    for i in range(5):
        a = i * 1.257 + 0.3; d = np.array([np.cos(a), 0, np.sin(a)]); e = d * 2.6 + np.array([0, 8.3 + 0.4 * (i % 2), 0])
        parts.append(_tube([d * 0.7 + np.array([0, 6.3, 0]), d * 1.8 + np.array([0, 7.4, 0]), e], 0.3, 0.1, bark, 4))
        parts.append(blob(e + np.array([0, 0.3, 0]), (1.1, 0.7, 1.1), seed=10 + i, col=(110, 150, 70), sub=0 if i > 2 else 1))
    return join(*parts)

def cypress():      # the tall dark column of Mediterranean hills
    return join(trunk(0.16, 0.12, 1.0), lathe([(0, 0.6), (0.45, 0.9), (0.8, 2.6), (0.72, 5.5), (0.42, 8.0), (0, 9.6)], n=8, exact=True, smooth=True,
                                              cols=[(34, 82, 50), (40, 96, 56), (46, 106, 60), (54, 118, 66), (62, 128, 72)]))

def gum():          # eucalyptus: pale trunk, sparse grey-green crown
    bark = (208, 198, 178); c = (126, 154, 112)
    return join(_tube([(0, 0, 0), (0.2, 2.5, 0), (0.1, 5.5, 0.2)], 0.3, 0.18, bark, 6),
                _tube([(0.1, 5.3, 0.2), (1.2, 7.0, 0.5), (1.8, 8.6, 0.6)], 0.15, 0.06, bark, 4), _tube([(0.1, 5.3, 0.2), (-0.9, 7.2, -0.3), (-1.2, 9.2, -0.5)], 0.15, 0.06, bark, 4),
                blob((1.9, 9.0, 0.6), (1.7, 1.0, 1.6), seed=20, col=c), blob((-1.3, 9.7, -0.5), (1.8, 1.1, 1.7), seed=21, col=c), blob((0.3, 8.0, 0.9), (1.3, 0.8, 1.3), seed=22, col=c, sub=0))

def cactus():       # saguaro
    g = (92, 146, 86)
    return join(lathe([(0.3, 0), (0.34, 1.0), (0.34, 3.6), (0.26, 4.2), (0, 4.4)], n=8, col=g, exact=True, smooth=True),
                _tube([(0.25, 1.7, 0), (0.85, 1.75, 0), (0.95, 2.2, 0), (0.95, 3.3, 0)], 0.2, 0.17, g, 6), _tube([(-0.25, 1.2, 0), (-0.75, 1.25, 0), (-0.82, 1.7, 0), (-0.82, 2.5, 0)], 0.19, 0.16, g, 6),
                blob((0.95, 3.38, 0), (0.17, 0.12, 0.17), col=g, sub=0, jitter=0), blob((-0.82, 2.58, 0), (0.16, 0.12, 0.16), col=g, sub=0, jitter=0))

def bamboo():
    parts = []; rng = np.random.RandomState(3)
    for i in range(5):
        a = i * 1.257; b = np.array([np.cos(a), 0, np.sin(a)]) * (0.25 + 0.2 * (i % 3)); lean = b * (1.2 + rng.rand())
        h = 5.5 + rng.rand() * 2.5; top = b + lean + np.array([0, h, 0])
        parts.append(_tube([b, b + lean * 0.3 + np.array([0, h * 0.5, 0]), top], 0.07, 0.035, (150, 178, 78), 4))
        if i % 2 == 0: parts.append(blob(top + np.array([0, -0.6, 0]), (1.3, 1.3, 1.3), seed=30 + i, col=(118, 176, 72)))
    return join(*parts)

def jungle():       # tall rainforest giant with a wide umbrella crown
    bark = (126, 104, 82); c = (44, 124, 56)
    parts = [lathe([(0.75, 0), (0.5, 1.2), (0.42, 5.0), (0.34, 10.5), (0, 10.5)], n=7, col=bark, exact=True, smooth=True)]
    for i in range(3): parts.append(xf(prism([(0.3, 0), (1.5, 0), (0.3, 2.4)], 0.14, bark), R('y', i * 120 + 20)))
    parts += [blob((0, 12.0, 0), (5.0, 1.9, 4.6), seed=40, col=c, jitter=0.12), blob((2.6, 11.0, 1.5), (3.0, 1.4, 2.8), seed=41, col=c, jitter=0.12),
              blob((-2.5, 11.2, -1.4), (3.1, 1.4, 2.9), seed=42, col=(58, 140, 60), jitter=0.12)]
    return join(*parts)

def snag():         # dead tree of deserts and the outback
    w = (140, 124, 106)
    return join(_tube([(0, 0, 0), (0.1, 1.5, 0.05), (-0.05, 3.0, 0)], 0.2, 0.11, w, 5), _tube([(0.05, 1.8, 0), (0.8, 2.6, 0.2), (1.1, 3.6, 0.3)], 0.09, 0.03, w, 4),
                _tube([(-0.02, 2.4, 0), (-0.7, 3.2, -0.3), (-0.8, 4.2, -0.5)], 0.08, 0.03, w, 4), _tube([(-0.05, 3.0, 0), (0.2, 3.9, -0.3), (0.1, 4.7, -0.5)], 0.08, 0.02, w, 4))

FLORA = {'own-tree-round': round_tree, 'own-tree-birch': birch, 'own-tree-spruce': spruce, 'own-tree-palm': palm, 'own-tree-acacia': acacia,
         'own-tree-baobab': baobab, 'own-tree-cypress': cypress, 'own-tree-gum': gum, 'own-tree-cactus': cactus, 'own-tree-bamboo': bamboo,
         'own-tree-jungle': jungle, 'own-tree-snag': snag}
if __name__ == '__main__':
    import sys, os
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for k, f in FLORA.items():
        t, size = save(f(), f'{out}/{k}.bytes'); print(f'{k:22s}{t:5d} tris  {np.round(size, 1)}')
    if len(sys.argv) > 2:
        from sheets import sheet
        import world
        g = lambda f, c: (lambda: tint(f(), np.array(c) / 255.0))
        sheet([(world.tree, 'Pine (north, mountains)', -30, 8), (spruce, 'Snowy spruce (far north)', -30, 8), (birch, 'Birch (north)', -30, 8),
               (g(round_tree, (112, 176, 70)), 'Oak (temperate)', -30, 8), (g(round_tree, (236, 130, 50)), 'Autumn maple (E. America)', -30, 8),
               (g(round_tree, (250, 170, 196)), 'Cherry blossom (Japan)', -30, 8), (cypress, 'Cypress (Mediterranean)', -30, 8), (palm, 'Palm (tropics, oases)', -30, 8),
               (jungle, 'Rainforest giant', -30, 6), (bamboo, 'Bamboo (East Asia)', -30, 8), (acacia, 'Acacia (savanna)', -30, 8), (baobab, 'Baobab (Africa)', -30, 8),
               (gum, 'Gum tree (Australia)', -30, 8), (cactus, 'Saguaro (American deserts)', -30, 8), (snag, 'Dead tree (deserts)', -30, 8), (world.bush, 'Bush', -30, 18)],
              sys.argv[2], 400, 440)
