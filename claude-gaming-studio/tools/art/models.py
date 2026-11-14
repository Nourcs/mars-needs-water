# The sample set: spoon, bucket, jerrycan, rocket. Each matches the size and origin of the greybox shape it replaces.
from forge import *

def spoon():
    # Bowl: a shallow shell (outside, rim, inside), squashed to an oval. Origin as the old view: bowl at z 0.03.
    prof = [(0, -1)] + [(np.sin(a), -np.cos(a)) for a in np.radians([25, 50, 72, 90])]
    prof += [(0.93, 0.02)] + [(0.9 * np.sin(a), -0.86 * np.cos(a)) for a in np.radians([72, 50, 25])] + [(0, -0.86)]
    bowl = xf(lathe(prof, n=18, smooth=True, col=STEEL), T(0, 0.004, 0.03) @ S(0.034, 0.017, 0.044))
    # Handle: thin neck out of the bowl, widening to a chunky orange grip.
    z = [-0.008, -0.03, -0.06, -0.10, -0.105, -0.16, -0.205, -0.212]
    y = [0.0, 0.004, 0.009, 0.013, 0.0135, 0.016, 0.016, 0.016]
    rx = [0.007, 0.005, 0.0055, 0.008, 0.0125, 0.0135, 0.012, 0.006]
    ry = [0.0025, 0.003, 0.003, 0.0035, 0.007, 0.0075, 0.007, 0.004]
    cols = [STEEL] * 3 + [DARK] + [ORANGE] * 2 + [DARK]
    handle = tube([(0, yy, zz) for yy, zz in zip(y, z)], rx, ry, n=10, cols=cols)
    return join(bowl, handle)

def bucket():
    # Centred on the origin, 0.30 tall like the old pail. Tapered, rolled rim, two ribs, bail with a grip.
    prof = [(0, -0.15), (0.100, -0.15), (0.108, -0.142), (0.119, -0.05), (0.124, -0.045), (0.125, -0.02), (0.123, -0.015),
            (0.134, 0.08), (0.139, 0.085), (0.140, 0.11), (0.138, 0.115), (0.141, 0.136), (0.151, 0.14), (0.151, 0.15),
            (0.14, 0.152), (0.135, 0.136), (0.104, -0.136), (0, -0.136)]
    cols = [DARK, DARK, ORANGE, ORANGE, WHITE, ORANGE, ORANGE, ORANGE, WHITE, ORANGE, ORANGE, DARK, DARK, DARK, DARK, STEEL, STEEL]
    pail = lathe(prof, n=24, cols=cols)
    parts = [pail]
    for sx in (-1, 1):
        parts.append(xf(box(0.022, 0.05, 0.05, 0.006, DARK), T(sx * 0.15, 0.11, 0)))
    bail = tube(arc(0, 0.115, 0.158, 0, 180, 15), 0.006, n=8, col=STEEL, up=(0, 0, 1))
    grip = tube(arc(0, 0.115, 0.158, 70, 110, 5), 0.014, n=10, col=DARK, up=(0, 0, 1))
    return join(*parts, bail, grip)

def jerrycan():
    # Stands on the origin, 0.18 x 0.45 x 0.32 like the old box. Spout at the front (-z), three-bar handle.
    h = 0.40
    body = xf(box(0.18, h, 0.32, 0.028, TEAL), T(0, h / 2, 0))
    parts = [body]
    for sx in (-1, 1):                                    # pressed panel with the X on each side
        parts.append(xf(box(0.008, 0.27, 0.22, 0.003, TEAL_D), T(sx * 0.089, h / 2, 0)))
        for a in (50, -50):
            parts.append(xf(box(0.01, 0.29, 0.03, 0.004, TEAL), T(sx * 0.091, h / 2, 0) @ R('x', a)))
    parts.append(xf(box(0.15, 0.03, 0.26, 0.012, TEAL_D), T(0, h + 0.004, 0)))        # shoulder
    for dx in (-0.045, 0, 0.045):                         # the three-bar handle
        parts.append(tube([(dx, h - 0.005, 0.125), (dx, h + 0.04, 0.105), (dx, h + 0.045, 0.02), (dx, h + 0.01, -0.03)], 0.011, n=8, col=TEAL))
    spout = lathe([(0.03, h), (0.03, h + 0.03), (0.037, h + 0.03), (0.037, h + 0.055), (0.03, h + 0.062), (0, h + 0.062)], n=14,
                  cols=[DARK, DARK, ORANGE, ORANGE, ORANGE])
    parts.append(xf(spout, T(0, 0, -0.115)))
    parts.append(xf(box(0.12, 0.012, 0.012, 0.003, STEEL_D), T(0, h + 0.048, -0.115)))  # cap lever
    return join(*parts)

def rocket():
    # Origin at the body centre; stands on its fins at y = -2.0; the body is 0.5 radius where the labels sit.
    nose = [(0.5, 1.32), (0.485, 1.7), (0.44, 2.08), (0.36, 2.44), (0.25, 2.76), (0.13, 2.98), (0.05, 3.07), (0, 3.1)]
    prof = [(0, -1.72), (0.2, -1.72), (0.26, -1.86), (0.36, -2.0), (0.31, -2.0), (0.22, -1.8), (0, -1.74)]   # engine bell (out, lip, in)
    bell = lathe(prof, n=20, cols=[DARK, STEEL_D, STEEL_D, DARK, DARK, DARK])
    hullp = [(0.2, -1.72), (0.4, -1.72), (0.5, -1.5), (0.515, -1.5), (0.515, -1.36), (0.5, -1.36),
             (0.5, 0.98), (0.52, 1.0), (0.52, 1.3), (0.5, 1.32)]
    hull_ = lathe(hullp, n=28, cols=[DARK, DARK, DARK, DARK, DARK, WHITE, TEAL, TEAL, TEAL])
    cone = lathe(nose, n=28, smooth=True, col=ORANGE)
    tip = lathe([(0.0, 3.06), (0.04, 3.08), (0.025, 3.3), (0, 3.34)], n=8, col=STEEL)                       # antenna spike
    parts = [bell, hull_, cone, tip]
    for i in range(3):                                    # swept fins, same angles as before
        fin = prism([(0.46, -0.75), (0.98, -1.75), (1.02, -2.0), (0.78, -2.0), (0.46, -1.62)], 0.07, ORANGE)
        foot = xf(box(0.11, 0.05, 0.26, 0.012, DARK), T(0, -1.975, 0.9))
        parts.append(xf(join(fin, foot), R('y', i * 120 + 30)))
    # Porthole on the -x side: a chunky ring and a domed glass.
    ring = lathe([(0.2, 0), (0.2, 0.05), (0.15, 0.065), (0.14, 0.03)], n=20, cols=[TEAL_D, TEAL, TEAL])
    glass = lathe([(0.14, 0.03), (0.1, 0.05), (0.05, 0.062), (0, 0.066)], n=20, smooth=True, col=GLASS)
    parts.append(xf(join(ring, glass), T(-0.49, 0.55, 0) @ R('z', 90)))
    for a in range(0, 360, 30):                           # rivets under the band
        parts.append(xf(box(0.03, 0.03, 0.03, 0.008, STEEL), R('y', a) @ T(0, 0.9, 0.505)))
    return join(*parts)

ALL = {'own-spoon': spoon, 'own-bucket': bucket, 'own-jerrycan': jerrycan, 'own-rocket': rocket}
if __name__ == '__main__':
    import sys, os
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for k, f in ALL.items():
        t, size = save(f(), f'{out}/{k}.bytes'); print(k, t, 'tris', np.round(size, 3))
