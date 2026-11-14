# Regional buildings. One Kenney-style tile each: +-0.5 wide, front towards +z, the game scales a tile to a lot.
# Walls are near-white so the game can tint them per building; roofs and details keep their own colours.
from forge import *
from world import WALL, STONE, STONE_D, WIN, DOOR, ROOF, ROOF_D, _win, _gable, WOOD, WOOD_D
import forge

def nb(*a, **k):    # plain flat box (no rounding): buildings are drawn in their hundreds
    return bx(*a, **k)
def dome(x, y, z, r, col, n=10, squash=1.0):
    pr = [(r * np.cos(a), r * squash * np.sin(a)) for a in np.linspace(0, np.pi / 2, 5)]
    return xf(lathe(pr, n=n, smooth=True, exact=True, col=col), T(x, y, z))
def cone(x, y, z, r, h, col, n=8):
    return xf(lathe([(r, 0), (0, h)], n=n, exact=True, col=col), T(x, y, z))
def cyl(x, y, z, r, h, col, n=8, cap=None):
    return xf(lathe([(r, 0), (r, h), (0, h)], n=n, exact=True, fillet=0, cols=[col, cap or col]), T(x, y, z))
def hip(w, d, y, h, col, over=0.06, top=0.25):
    """Hip roof with wide eaves."""
    pts = [(sx * (w / 2 + over), y, sz * (d / 2 + over)) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * w * top / 2, y + h, sz * d * top * 0.2) for sx in (-1, 1) for sz in (-1, 1)]
    return hull(pts, col)
BASE = lambda: nb(0, 0.02, 0, 0.94, 0.04, 0.94, STONE)
SAND = (236, 222, 196); TILE = (52, 84, 92); TILE_D = (40, 64, 72); RED = (186, 52, 44); GOLD = (232, 186, 70); THATCH = (196, 164, 96); MUD = (190, 140, 100)

# --- North Africa, Middle East, South Asia: flat roofs, domes, courtyards
def adobe_a():
    return join(BASE(), nb(0, 0.27, -0.05, 0.7, 0.46, 0.66, WALL), nb(0, 0.52, -0.05, 0.74, 0.06, 0.7, WALL), nb(0, 0.5, -0.05, 0.62, 0.04, 0.58, SAND),
                nb(-0.12, 0.17, 0.282, 0.14, 0.26, 0.012, DOOR), _win(0.18, 0.32, 0.28, 0.08, 0.1), _win(0.35, 0.3, 0, 0.08, 0.1, face='x'), _win(-0.35, 0.3, -0.1, 0.08, 0.1, face='x'),
                nb(0.2, 0.62, -0.2, 0.26, 0.2, 0.26, WALL), dome(0.2, 0.72, -0.2, 0.12, (226, 214, 190)))
def adobe_b():
    return join(BASE(), nb(-0.08, 0.36, -0.05, 0.62, 0.64, 0.66, WALL), nb(-0.08, 0.7, -0.05, 0.66, 0.06, 0.7, WALL), dome(-0.08, 0.71, -0.05, 0.24, (120, 170, 190), n=12),
                nb(0.32, 0.2, 0.1, 0.2, 0.32, 0.4, WALL), nb(0.32, 0.38, 0.1, 0.23, 0.04, 0.43, WALL),
                nb(-0.1, 0.19, 0.282, 0.14, 0.3, 0.012, (40, 110, 130)), _win(-0.26, 0.5, 0.28, 0.07, 0.12), _win(0.08, 0.5, 0.28, 0.07, 0.12),
                _win(-0.39, 0.45, -0.05, 0.07, 0.12, face='x'), xf(lathe([(0.012, 0), (0.012, 0.1), (0.03, 0.12), (0, 0.16)], n=5, exact=True, col=GOLD), T(-0.08, 0.95, -0.05)))
def adobe_c():      # courtyard house with an awning
    p = [BASE(), nb(0, 0.22, -0.26, 0.8, 0.36, 0.26, WALL), nb(-0.29, 0.22, 0.05, 0.22, 0.36, 0.4, WALL), nb(0, 0.42, -0.26, 0.84, 0.05, 0.3, WALL), nb(-0.29, 0.42, 0.05, 0.26, 0.05, 0.44, WALL),
         nb(0.12, 0.33, 0.06, 0.5, 0.02, 0.32, (196, 90, 60)), nb(0.12, 0.1, -0.129, 0.12, 0.2, 0.012, DOOR), _win(-0.18, 0.26, 0.252, 0.07, 0.09)]
    for x in (-0.1, 0.34): p.append(nb(x, 0.18, 0.2, 0.025, 0.3, 0.025, WOOD_D))
    p.append(cyl(0.3, 0.04, 0.3, 0.05, 0.1, (170, 110, 80), n=7))
    return join(*p)
def mosque():       # town centre: dome and minaret
    p = [nb(0, 0.03, 0, 0.98, 0.06, 0.98, STONE), nb(-0.1, 0.3, -0.05, 0.62, 0.5, 0.62, WALL), nb(-0.1, 0.56, -0.05, 0.66, 0.05, 0.66, WALL),
         cyl(-0.1, 0.58, -0.05, 0.2, 0.1, WALL, n=12), dome(-0.1, 0.68, -0.05, 0.22, (70, 150, 170), n=14, squash=1.15),
         xf(lathe([(0.012, 0), (0.012, 0.08), (0.03, 0.1), (0, 0.15)], n=5, exact=True, col=GOLD), T(-0.1, 0.93, -0.05)),
         cyl(0.36, 0.06, 0.32, 0.06, 1.0, WALL, n=8), cyl(0.36, 0.78, 0.32, 0.085, 0.03, STONE_D, n=8), cyl(0.36, 1.06, 0.32, 0.075, 0.03, STONE_D, n=8),
         cone(0.36, 1.09, 0.32, 0.07, 0.2, (70, 150, 170)), nb(-0.1, 0.22, 0.262, 0.16, 0.34, 0.012, (40, 110, 130))]
    for x in (-0.32, 0.12): p.append(_win(x, 0.34, 0.26, 0.07, 0.16))
    for sx in (-1, 1):
        for sz in (-1, 1): p.append(dome(-0.1 + sx * 0.25, 0.585, -0.05 + sz * 0.25, 0.07, (70, 150, 170), n=7))
    return join(*p)

# --- East and South-East Asia: tiled roofs with wide eaves
def _eaves(w, d, y, col):
    p = []
    for sx in (-1, 1):
        for sz in (-1, 1): p.append(xf(prism([(0, 0), (0.1, 0.07), (0.02, 0.05)], 0.03, col), T(sx * (w / 2 + 0.05), y, sz * (d / 2 + 0.05)) @ R('y', np.degrees(np.arctan2(sx, sz)))))
    return p
def asia_a():
    p = [BASE(), nb(0, 0.09, 0, 0.8, 0.1, 0.7, STONE_D), nb(0, 0.3, 0, 0.66, 0.32, 0.56, WALL), hip(0.66, 0.56, 0.46, 0.26, TILE, over=0.13), nb(0, 0.735, 0, 0.3, 0.035, 0.05, TILE_D),
         nb(0, 0.26, 0.282, 0.16, 0.24, 0.012, RED), _win(-0.22, 0.32, 0.28, 0.1, 0.12, col=(120, 70, 50)), _win(0.22, 0.32, 0.28, 0.1, 0.12, col=(120, 70, 50))]
    for sx in (-1, 1):
        for sz in (-1, 1): p.append(cyl(sx * 0.36, 0.14, sz * 0.31, 0.022, 0.32, RED, n=6))
    return join(*p + _eaves(0.66, 0.56, 0.46, TILE_D))
def asia_b():       # two-storey shophouse
    p = [BASE(), nb(0, 0.24, 0, 0.72, 0.4, 0.64, WALL), hip(0.72, 0.64, 0.44, 0.1, TILE, over=0.1, top=0.82), nb(0, 0.7, 0, 0.6, 0.34, 0.52, WALL),
         hip(0.6, 0.52, 0.87, 0.24, TILE, over=0.12), nb(0, 1.125, 0, 0.26, 0.035, 0.05, TILE_D), nb(0, 0.22, 0.322, 0.5, 0.3, 0.012, (120, 70, 50)), nb(0.0, 0.36, 0.34, 0.5, 0.05, 0.03, RED)]
    for x in (-0.18, 0, 0.18): p.append(_win(x, 0.72, 0.26, 0.08, 0.14, col=(120, 70, 50)))
    for x in (-0.28, 0.28): p.append(xf(lathe([(0.0, 0), (0.03, 0.02), (0.03, 0.06), (0, 0.08)], n=6, exact=True, col=(230, 60, 50)), T(x, 0.3, 0.37)))
    return join(*p + _eaves(0.6, 0.52, 0.87, TILE_D))
def _pagoda(tiers, w0, base=True):
    p = [nb(0, 0.03, 0, 0.98, 0.06, 0.98, STONE)] if base else [BASE()]
    y = 0.06; w = w0
    for i in range(tiers):
        hh = 0.2 if i else 0.26
        p += [nb(0, y + hh / 2, 0, w, hh, w, WALL if i % 2 == 0 else (236, 200, 190)), hip(w, w, y + hh, 0.1, TILE, over=0.12, top=0.7)] + _eaves(w, w, y + hh, TILE_D)
        if i == 0: p.append(nb(0, y + 0.1, w / 2, 0.14, 0.2, 0.012, RED))
        else: p.append(_win(0, y + hh / 2, w / 2, 0.07, 0.09, col=(120, 70, 50)))
        y += hh + 0.09; w *= 0.8
    p += [cone(0, y - 0.02, 0, w * 0.75, 0.16, TILE, n=4), xf(lathe([(0.012, 0), (0.012, 0.14), (0.03, 0.16), (0, 0.24)], n=5, exact=True, col=GOLD), T(0, y + 0.1, 0))]
    return join(*p)
def asia_c(): return _pagoda(3, 0.56, False)
def pagoda(): return _pagoda(5, 0.62)

# --- Sub-Saharan villages: round huts with thatch
def _hut(x, z, r, h):
    return join(cyl(x, 0.04, z, r, h, MUD, n=10), xf(lathe([(r * 1.35, 0), (r * 0.5, h * 0.9), (0, h * 1.25)], n=10, exact=True, smooth=True, col=THATCH), T(x, 0.04 + h * 0.92, z)),
                nb(x, 0.04 + h * 0.4, z + r, r * 0.5, h * 0.7, 0.02, (90, 60, 44)))
def hut_a(): return join(nb(0, 0.02, 0, 0.94, 0.04, 0.94, (200, 170, 130)), _hut(0, -0.05, 0.3, 0.3))
def hut_b():
    p = [nb(0, 0.02, 0, 0.94, 0.04, 0.94, (200, 170, 130)), _hut(-0.2, -0.15, 0.2, 0.24), _hut(0.22, 0.1, 0.17, 0.2)]
    for i in range(7): p.append(nb(-0.42 + i * 0.07, 0.1, 0.4, 0.02, 0.14, 0.02, WOOD_D))
    return join(*p)
def hut_c():        # rectangular mud house, thatched
    return join(nb(0, 0.02, 0, 0.94, 0.04, 0.94, (200, 170, 130)), nb(0, 0.2, -0.05, 0.62, 0.32, 0.46, MUD), _gable(0.62, 0.46, 0.36, 0.26, THATCH, over=0.08),
                hull([(sx * 0.31, 0.36, sz * 0.229) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * 0.31, 0.6, 0) for sx in (-1, 1)], MUD),
                nb(-0.1, 0.16, 0.182, 0.12, 0.24, 0.012, (90, 60, 44)), _win(0.15, 0.24, 0.18, 0.07, 0.07, col=(70, 50, 40)))

# --- The far north: steep roofs, timber, bright paint
def nordic_a():
    w, d, h = 0.62, 0.56, 0.36
    return join(BASE(), nb(0, 0.04 + h / 2, 0, w, h, d, WALL), _gable(w, d, 0.04 + h, 0.44, (60, 62, 70), over=0.05),
                hull([(sx * w / 2, 0.04 + h, sz * (d / 2 - 0.001)) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * w / 2, 0.04 + h + 0.42, 0) for sx in (-1, 1)], WALL),
                nb(-0.1, 0.17, d / 2, 0.12, 0.26, 0.014, (250, 250, 246)), _win(0.14, 0.24, d / 2, 0.09, 0.11), _win(w / 2, 0.24, 0, 0.09, 0.11, face='x'), _win(w / 2, 0.56, 0, 0.07, 0.09, face='x'),
                _win(-w / 2, 0.24, 0, 0.09, 0.11, face='x'), nb(0.15, 0.78, -0.1, 0.07, 0.2, 0.07, STONE_D))
def nordic_b():     # A-frame cabin with a wood pile
    p = [BASE(), hull([(sx * 0.3, 0.04, sz * 0.36) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * 0.3, 0.74, 0) for sx in (-1, 1)], (60, 62, 70)),
         hull([(sx * 0.27, 0.04, sz * 0.3) for sx in (-1, 1) for sz in (-1, 1)] + [(sx * 0.27, 0.62, 0) for sx in (-1, 1)], WALL) if False else nb(0, 0.2, 0, 0.602, 0.3, 0.3, WALL),
         nb(0.301, 0.17, 0, 0.012, 0.24, 0.1, DOOR), nb(-0.301, 0.24, 0, 0.012, 0.12, 0.12, WIN)]
    for i in range(3): p.append(xf(cyl(0, 0, 0, 0.035, 0.22, WOOD, n=6, cap=WOOD_D), T(0.0, 0.08 + (i % 2) * 0.06, 0.4 + i * 0.0) @ R('z', 90) @ T(0, -0.11 + i * 0.01, 0)))
    return join(*p)
def church():       # town centre for Europe and the Americas
    p = [nb(0, 0.03, 0, 0.98, 0.06, 0.98, STONE), nb(0, 0.3, -0.12, 0.46, 0.48, 0.66, WALL), _gable(0.66, 0.46, 0.54, 0.3, ROOF_D, over=0.03) if False else
         xf(_gable(0.66, 0.46, 0.0, 0.3, (84, 88, 98), over=0.03), T(0, 0.54, -0.12) @ R('y', 90)),
         nb(0, 0.52, 0.3, 0.24, 0.92, 0.24, WALL), xf(lathe([(0.19, 0), (0.04, 0.5), (0, 0.56)], n=4, exact=True, col=(84, 88, 98)), T(0, 0.98, 0.3) @ R('y', 45)),
         nb(0, 1.6, 0.3, 0.014, 0.14, 0.014, GOLD), nb(0, 1.62, 0.3, 0.07, 0.014, 0.014, GOLD), nb(0, 0.2, 0.422, 0.11, 0.26, 0.012, DOOR),
         xf(lathe([(0.05, 0), (0.05, 0.012), (0, 0.012)], n=10, exact=True, cols=[WHITE, (250, 240, 200)]), T(0, 0.74, 0.421) @ R('x', 90))]
    for z in (-0.3, -0.1, 0.08):
        for sx in (-1, 1): p.append(_win(sx * 0.23, 0.32, z, 0.06, 0.2, face='x', col=(120, 150, 200)))
    return join(*p)

TOWNS = {'house-adobe-a': adobe_a, 'house-adobe-b': adobe_b, 'house-adobe-c': adobe_c, 'centre-mosque': mosque, 'house-asia-a': asia_a, 'house-asia-b': asia_b,
         'house-asia-c': asia_c, 'centre-pagoda': pagoda, 'house-hut-a': hut_a, 'house-hut-b': hut_b, 'house-hut-c': hut_c, 'house-nordic-a': nordic_a,
         'house-nordic-b': nordic_b, 'centre-church': church}
if __name__ == '__main__':
    import sys, os
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for k, f in TOWNS.items():
        t, size = save(f(), f'{out}/own-{k}.bytes'); print(f'{k:16s}{t:5d} {np.round(size, 2)}', end=' | ')
    if len(sys.argv) > 2:
        from sheets import sheet
        names = ['Desert house', 'Domed house', 'Courtyard house', 'Mosque', 'Tiled-roof house', 'Shophouse', 'Small pagoda', 'Pagoda', 'Round hut', 'Hut compound', 'Thatched house',
                 'Northern house', 'A-frame cabin', 'Church']
        sheet([(f, n, -32, 16) for (k, f), n in zip(TOWNS.items(), names)], sys.argv[2], 360, 380, cols=5)
