"""Bakes the ground detail textures for Mars Needs Water's mini Earth.

Everything is made from code (no photos): seamless 512 x 512 squares, one per ground kind.
Writes <out>/<kind>_a.png (RGB albedo, A height) and <kind>_n.png (RG normal, B occlusion, A smoothness),
plus <out>/means.txt with each kind's average colour (sRGB hex), used for the far-away map colours.
Usage: python3 bake_ground.py <outdir> [preview.png]
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

N = 1024
KINDS = ["grass", "drygrass", "forest", "farm", "sand", "rock", "snow", "paved", "dirt", "mud"]


def rng(seed):
    return np.random.default_rng(seed)


def spectral(seed, beta=2.0, fmin=1.0, fmax=256.0, n=N):
    """Seamless fractal noise, zero mean, unit std. beta: spectrum slope (bigger = smoother)."""
    r = rng(seed)
    w = r.standard_normal((n, n))
    F = np.fft.fft2(w)
    fy = np.fft.fftfreq(n)[:, None] * n
    fx = np.fft.fftfreq(n)[None, :] * n
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    amp = f ** (-beta / 2.0)
    amp[(f < fmin) | (f > fmax)] = 0.0
    amp[0, 0] = 0.0
    out = np.real(np.fft.ifft2(F * amp))
    return (out - out.mean()) / (out.std() + 1e-9)


def blur(a, sigma):
    """Seamless gaussian blur (FFT)."""
    if sigma <= 0:
        return a
    n = a.shape[0]
    fy = np.fft.fftfreq(n)[:, None]
    fx = np.fft.fftfreq(n)[None, :]
    g = np.exp(-2 * (math.pi ** 2) * (sigma ** 2) * (fx * fx + fy * fy))
    if a.ndim == 3:
        return np.stack([np.real(np.fft.ifft2(np.fft.fft2(a[..., c]) * g)) for c in range(a.shape[2])], -1)
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def s01(a):
    lo, hi = np.percentile(a, 1), np.percentile(a, 99)
    return np.clip((a - lo) / (hi - lo + 1e-9), 0, 1)


def lerp(a, b, t):
    t = np.asarray(t)
    if t.ndim == 2:
        t = t[..., None]
    return a + (b - a) * t


def col(hexs):
    return np.array([int(hexs[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


class Canvas:
    """Draws strokes on an RGB + height picture that wraps round its edges."""

    def __init__(self, base_rgb, base_h=0.0):
        self.rgb = Image.fromarray((np.clip(base_rgb, 0, 1) * 255).astype(np.uint8), "RGB")
        self.h = Image.fromarray((np.clip(base_h, 0, 1) * 255).astype(np.uint8) if np.ndim(base_h) == 2 else np.full((N, N), int(base_h * 255), np.uint8), "L")
        self.dr = ImageDraw.Draw(self.rgb)
        self.dh = ImageDraw.Draw(self.h)

    def _offsets(self, xs, ys, pad):
        ox = [0]
        oy = [0]
        if min(xs) - pad < 0: ox.append(N)
        if max(xs) + pad >= N: ox.append(-N)
        if min(ys) - pad < 0: oy.append(N)
        if max(ys) + pad >= N: oy.append(-N)
        return [(a, b) for a in ox for b in oy]

    def line(self, pts, rgb, width, h):
        xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
        c = tuple(int(max(0, min(1, v)) * 255) for v in rgb)
        hv = int(max(0, min(1, h)) * 255)
        for dx, dy in self._offsets(xs, ys, width + 2):
            q = [(x + dx, y + dy) for x, y in pts]
            self.dr.line(q, fill=c, width=width, joint="curve")
            self.dh.line(q, fill=hv, width=width)

    def poly(self, pts, rgb, h):
        xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
        c = tuple(int(max(0, min(1, v)) * 255) for v in rgb)
        hv = int(max(0, min(1, h)) * 255)
        for dx, dy in self._offsets(xs, ys, 2):
            q = [(x + dx, y + dy) for x, y in pts]
            self.dr.polygon(q, fill=c)
            self.dh.polygon(q, fill=hv)

    def arrays(self):
        return np.asarray(self.rgb).astype(np.float64) / 255.0, np.asarray(self.h).astype(np.float64) / 255.0


def ellipse(cx, cy, rx, ry, ang, k=10, wob=0.0, r=None):
    pts = []
    ca, sa = math.cos(ang), math.sin(ang)
    for i in range(k):
        t = 2 * math.pi * i / k
        f = 1.0 + (r.uniform(-wob, wob) if r is not None and wob > 0 else 0.0)
        x, y = math.cos(t) * rx * f, math.sin(t) * ry * f
        pts.append((cx + x * ca - y * sa, cy + x * sa + y * ca))
    return pts


def blades(cv, r, count, palette, len_rng, width_rng, h0, h1, curve=0.35, field=None):
    """Grass blades seen from above and a little from the side: short tapered curved strokes."""
    for i in range(count):
        x, y = r.uniform(0, N), r.uniform(0, N)
        ang = r.uniform(0, 2 * math.pi)
        L = r.uniform(*len_rng)
        bend = r.uniform(-curve, curve)
        c = palette[r.integers(len(palette))] * r.uniform(0.82, 1.15)
        prog = i / count
        if field is not None:
            c = c * (0.85 + 0.3 * field[int(y) % N, int(x) % N])
        # later blades sit on top: they are taller and catch more light
        c = c * (0.8 + 0.35 * prog)
        pts = [(x, y)]
        a = ang
        for s in range(3):
            a += bend
            x += math.cos(a) * L / 3; y += math.sin(a) * L / 3
            pts.append((x, y))
        w = int(r.integers(width_rng[0], width_rng[1] + 1))
        cv.line(pts, c, w, h0 + (h1 - h0) * prog)


def normals(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.stack([-gx, gy, np.ones_like(h)], -1)   # image y runs down; texture v runs up
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n


def occlusion(h, radius=6, amount=1.6):
    cav = blur(h, radius) - h
    return np.clip(1.0 - np.maximum(cav, 0) * amount * 4, 0.35, 1.0)


# ---------------------------------------------------------------- shared shapes

from scipy.spatial import cKDTree


def voronoi(seed, count, jitter=1.0):
    """Seamless cells: distance to the nearest and second-nearest seed point (pixels) and the nearest's index."""
    r = rng(seed)
    side = int(round(math.sqrt(count)))
    g = (np.stack(np.meshgrid(np.arange(side), np.arange(side)), -1).reshape(-1, 2) + 0.5 + (r.random((side * side, 2)) - 0.5) * jitter) * (N / side)
    pts = np.mod(g, N)
    tree = cKDTree(pts, boxsize=N)
    yy, xx = np.mgrid[0:N, 0:N]
    q = np.stack([xx.ravel() + 0.5, yy.ravel() + 0.5], -1)
    d, i = tree.query(q, k=2)
    return d[:, 0].reshape(N, N), d[:, 1].reshape(N, N), i[:, 0].reshape(N, N)


def warp(a, seed, amount, fmax=12):
    """Push a picture around by smooth noise (wraps)."""
    dx = spectral(seed, 2.6, 1, fmax) * amount
    dy = spectral(seed + 1, 2.6, 1, fmax) * amount
    yy, xx = np.mgrid[0:N, 0:N]
    xi = np.mod(np.round(xx + dx).astype(int), N)
    yi = np.mod(np.round(yy + dy).astype(int), N)
    return a[yi, xi]


def tuft(cv, r, cx, cy, n, length, palette, h0, h1, spread=math.pi * 2, lean=0.0, width=(1, 2), tint=1.0):
    """A clump of blades bursting out of one root, seen from above."""
    base = r.uniform(0, 6.283)
    for b in range(n):
        a = base + r.uniform(0, spread) + lean
        L = length * r.uniform(0.5, 1.15)
        bend = r.uniform(-0.25, 0.25)
        c = palette[r.integers(len(palette))] * r.uniform(0.8, 1.15) * tint
        x, y = cx + r.normal(0, 1.2), cy + r.normal(0, 1.2)
        pts = [(x, y)]
        for s in range(4):
            a += bend
            x += math.cos(a) * L / 4; y += math.sin(a) * L / 4
            pts.append((x, y))
        t = b / max(n - 1, 1)
        cv.line(pts, c * (0.75 + 0.45 * t), int(r.integers(width[0], width[1] + 1)), h0 + (h1 - h0) * t)


# ---------------------------------------------------------------- the kinds (each its own shapes)

def k_grass(seed):
    """Lawn and meadow: tufts of blades radiating from roots, clover and weeds, earth in the gaps."""
    r = rng(seed)
    big = s01(spectral(seed + 1, 2.6, 1, 6))
    soil = lerp(col("2a2412"), col("3b3319"), s01(spectral(seed + 2, 1.5, 8, 300)))
    cv = Canvas(soil, 0.0)
    greens = [col(c) for c in ("4f7a2a", "5c8a2f", "426c26", "6d9a3a", "7da846", "577f2c", "3d6524")]
    blue = [col(c) for c in ("3f6b3a", "4a7a44", "557f48")]
    straw = [col(c) for c in ("9a9450", "8a8a46", "a8a060")]
    # undergrowth of loose short blades
    blades(cv, r, 30000, greens, (10, 26), (1, 2), 0.05, 0.45, 0.4, field=big)
    # tufts: the main look of real grass from above
    for i in range(2600):
        x, y = r.uniform(0, N), r.uniform(0, N)
        pal = blue if r.random() < 0.25 else greens
        tint = 0.85 + 0.35 * big[int(y) % N, int(x) % N]
        tuft(cv, r, x, y, int(r.integers(10, 22)), r.uniform(18, 34), pal, 0.45, 1.0, tint=tint)
    # some dry blades
    blades(cv, r, 2500, straw, (12, 26), (1, 2), 0.5, 0.9, 0.3)
    # clover patches: three round leaves each
    for i in range(30):
        cx, cy = r.uniform(0, N), r.uniform(0, N)
        for p in range(int(r.integers(4, 10))):
            px, py = cx + r.normal(0, 14), cy + r.normal(0, 14)
            a0 = r.uniform(0, 6.3)
            for k in range(3):
                a = a0 + k * 2.094
                cv.poly(ellipse(px + math.cos(a) * 3.2, py + math.sin(a) * 3.2, 3.4, 2.8, a, 10), col("4a8434") * r.uniform(0.85, 1.15), 0.92)
    # dandelion-like rosettes and a few tiny white and yellow flowers
    for i in range(30):
        cx, cy = r.uniform(0, N), r.uniform(0, N)
        for k in range(int(r.integers(6, 10))):
            a = r.uniform(0, 6.3); L = r.uniform(14, 26)
            cv.poly(ellipse(cx + math.cos(a) * L / 2, cy + math.sin(a) * L / 2, L / 2, 3.5, a, 10, 0.2, r), col("4c7d2c") * r.uniform(0.9, 1.1), 0.7)
    for i in range(90):
        x, y = r.uniform(0, N), r.uniform(0, N)
        c = col("f2f0e0") if r.random() < 0.6 else col("e8c840")
        cv.poly(ellipse(x, y, 2.5, 2.5, 0, 8), c, 1.0)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.5)
    h = blur(h, 1.0)
    rgb *= (0.85 + 0.3 * big)[..., None]
    return rgb, h, 3.2, 0.1


def k_drygrass(seed):
    """Dry grassland and scrub: pale straw tufts in clumps, bare dusty earth between, small stones."""
    r = rng(seed)
    soil = lerp(col("8f7650"), col("b39a70"), s01(spectral(seed + 2, 1.4, 6, 400)))
    soil = soil * (0.9 + 0.2 * s01(spectral(seed + 3, 2.4, 1, 10)))[..., None]
    cv = Canvas(soil, s01(spectral(seed + 4, 2.0, 4, 200)) * 0.2)
    # pebbles in the dust
    for i in range(900):
        x, y = r.uniform(0, N), r.uniform(0, N); rr = r.uniform(1.5, 4.5)
        cv.poly(ellipse(x, y, rr, rr * r.uniform(0.6, 1), r.uniform(0, 6.3), 8, 0.2, r), col("9a8a74") * r.uniform(0.75, 1.2), 0.3 + rr / 20)
    straw = [col(c) for c in ("c9b27a", "b89f68", "d6c08c", "a88f5a", "bfae80")]
    grey = [col(c) for c in ("8f8a62", "7d7a55", "9a9670")]
    # bunches of tufts with gaps of bare ground between
    clumps = [(r.uniform(0, N), r.uniform(0, N)) for i in range(140)]
    for (cx, cy) in clumps:
        for k in range(int(r.integers(4, 12))):
            x, y = cx + r.normal(0, 30), cy + r.normal(0, 30)
            pal = grey if r.random() < 0.2 else straw
            tuft(cv, r, x, y, int(r.integers(14, 30)), r.uniform(30, 55), pal, 0.35, 1.0, width=(1, 2))
    # broken stems lying about
    blades(cv, r, 3500, straw, (10, 22), (1, 1), 0.25, 0.4, 0.2)
    # seed heads
    for i in range(600):
        x, y = r.uniform(0, N), r.uniform(0, N)
        cv.poly(ellipse(x, y, r.uniform(2.5, 5), 1.6, r.uniform(0, 6.3), 6), col("e0cf9c") * r.uniform(0.9, 1.08), 1.0)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.5); h = blur(h, 1.0)
    return rgb, h, 2.8, 0.08


def leaf(cx, cy, size, ang, lobes, r):
    """Outline of a leaf: lobed (oak-like) or plain."""
    pts = []
    k = 24
    ca, sa = math.cos(ang), math.sin(ang)
    for i in range(k):
        t = 2 * math.pi * i / k
        rr = 1.0 + (0.22 * math.cos(t * lobes) if lobes else 0.0)
        x = math.cos(t) * size * rr
        y = math.sin(t) * size * 0.5 * rr * (1.0 - 0.35 * math.cos(t))
        pts.append((cx + x * ca - y * sa, cy + x * sa + y * ca))
    return pts


def k_forest(seed):
    """Forest floor: fallen leaves of different trees with midribs, pine needles, cones, twigs, roots and moss."""
    r = rng(seed)
    soil = lerp(col("2a1f14"), col("40301e"), s01(spectral(seed + 1, 1.6, 4, 300)))
    cv = Canvas(soil, 0.0)
    browns = [col(c) for c in ("6b4524", "7f562a", "5a3a1e", "8e6a32", "9a7438", "6a4e2a", "a07a3c", "4e3820", "7a3e22")]
    # roots snaking over the ground
    for i in range(14):
        x, y = r.uniform(0, N), r.uniform(0, N); a = r.uniform(0, 6.3)
        pts = [(x, y)]
        for s in range(12):
            a += r.uniform(-0.35, 0.35); x += math.cos(a) * 18; y += math.sin(a) * 18; pts.append((x, y))
        cv.line(pts, col("4a3622") * r.uniform(0.8, 1.1), int(r.integers(6, 12)), 0.75)
    # needles
    for i in range(9000):
        x, y = r.uniform(0, N), r.uniform(0, N); a = r.uniform(0, 6.3); L = r.uniform(10, 22)
        cv.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], col("8a5a30") * r.uniform(0.6, 1.15), 1, 0.4 + 0.2 * r.random())
    # leaves: big lobed ones and small plain ones, each with a darker midrib
    for i in range(3800):
        x, y = r.uniform(0, N), r.uniform(0, N)
        big = r.random() < 0.35
        size = r.uniform(12, 22) if big else r.uniform(6, 11)
        a = r.uniform(0, 6.3)
        c = browns[r.integers(len(browns))] * r.uniform(0.8, 1.15)
        t = i / 3800
        cv.poly(leaf(x, y, size, a, 5 if big else 0, r), c * (0.75 + 0.35 * t), 0.3 + 0.6 * t)
        cv.line([(x - math.cos(a) * size, y - math.sin(a) * size), (x + math.cos(a) * size * 1.2, y + math.sin(a) * size * 1.2)], c * 0.6, 1, 0.3 + 0.62 * t)
    # cones and twigs
    for i in range(40):
        x, y = r.uniform(0, N), r.uniform(0, N); a = r.uniform(0, 6.3)
        cv.poly(ellipse(x, y, 13, 7, a, 12), col("5a3a20"), 1.0)
        for k in range(10):
            cv.poly(ellipse(x + math.cos(a) * (k - 5) * 2.2, y + math.sin(a) * (k - 5) * 2.2, 3, 5, a, 6), col("7a5230") * r.uniform(0.85, 1.15), 1.0)
    for i in range(160):
        x, y = r.uniform(0, N), r.uniform(0, N); a = r.uniform(0, 6.3)
        pts = [(x, y)]
        for s in range(5):
            a += r.uniform(-0.4, 0.4); x += math.cos(a) * 14; y += math.sin(a) * 14; pts.append((x, y))
        cv.line(pts, col("5a4430") * r.uniform(0.8, 1.2), int(r.integers(2, 5)), 0.95)
    rgb, h = cv.arrays()
    # moss cushions: soft bright green with a fine bumpy texture
    moss = s01(spectral(seed + 5, 2.4, 2, 30))
    mm = np.clip((moss - 0.66) * 6, 0, 1)
    fine = s01(spectral(seed + 6, 0.8, 80, 512))
    mtex = lerp(col("3c5a1e"), col("6c8a2e"), fine)
    rgb = lerp(rgb, mtex, mm)
    h = h * (1 - mm) + mm * (0.55 + 0.25 * fine)
    rgb = blur(rgb, 0.5); h = blur(h, 1.0)
    return rgb, h, 2.6, 0.12


def k_farm(seed):
    """Ploughed field: straight furrows with ridges and clods, young crop plants along the ridges."""
    r = rng(seed)
    rows = 16                                    # furrows across the square
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    wob = spectral(seed + 1, 2.8, 1, 4) * 3.0
    ph = (yy + wob) / N * rows
    ridge = 0.5 + 0.5 * np.cos(ph * 2 * math.pi)
    ridge = ridge ** 0.8
    clods = s01(spectral(seed + 2, 0.9, 40, 512))
    h = ridge * 0.6 + clods * 0.3 + s01(spectral(seed + 3, 2.0, 2, 40)) * 0.1
    soil = lerp(col("4a3824"), col("7a6040"), np.clip(ridge * 0.7 + clods * 0.4, 0, 1))
    soil *= (0.9 + 0.2 * s01(spectral(seed + 4, 2.4, 1, 8)))[..., None]
    cv = Canvas(soil, np.clip(h, 0, 1))
    greens = [col(c) for c in ("5a8a2e", "6c9a38", "4c7a28", "7aa642")]
    for row in range(rows):
        yc = (row + 0.0) * N / rows
        x = r.uniform(0, 20)
        while x < N:
            y = yc + wob[int(yc) % N, int(x) % N] + r.normal(0, 2)
            n = int(r.integers(4, 7))
            for k in range(n):
                a = r.uniform(0, 6.3); L = r.uniform(7, 15)
                cv.poly(ellipse(x + math.cos(a) * L / 2, y + math.sin(a) * L / 2, L / 2, 2.8, a, 8), greens[r.integers(4)] * r.uniform(0.85, 1.15), 0.9)
            x += r.uniform(16, 30)
    rgb, h2 = cv.arrays()
    rgb = blur(rgb, 0.5); h2 = blur(h2, 1.0)
    return rgb, h2, 3.5, 0.08


def k_sand(seed):
    """Beach and dune sand: wind ripples with gentle backs and steep fronts, fine grains, shell bits, darker streaks."""
    r = rng(seed)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    w = spectral(seed + 1, 3.0, 1, 5) * 30 + spectral(seed + 2, 2.4, 2, 16) * 6
    per = 20                                      # ripples across the square
    p = np.mod((yy + w + 0.15 * xx * 0) / N * per, 1.0)
    rip = np.where(p < 0.75, p / 0.75, (1 - p) / 0.25)       # long gentle back, short steep front
    rip = rip * (0.55 + 0.45 * s01(spectral(seed + 3, 2.4, 1, 6)))
    grain = s01(spectral(seed + 4, 0.0, 200, 512))
    h = rip * 0.55 + grain * 0.1 + s01(spectral(seed + 5, 2.6, 1, 6)) * 0.25
    rgb = lerp(col("c4ad84"), col("dcc8a0"), np.clip(h * 0.9, 0, 1))
    # heavier dark grains settle in the troughs
    trough = np.clip(1 - rip * 1.6, 0, 1)
    rgb = lerp(rgb, col("8f7d62"), trough * 0.25 * s01(spectral(seed + 6, 1.6, 10, 200)))
    sp = r.random((N, N))
    rgb = np.where((sp < 0.02)[..., None], rgb * 0.7, rgb)
    rgb = np.where((sp > 0.992)[..., None], np.minimum(rgb * 1.2, 1), rgb)
    cv = Canvas(rgb, np.clip(h, 0, 1))
    for i in range(70):                           # shell fragments
        x, y = r.uniform(0, N), r.uniform(0, N)
        cv.poly(ellipse(x, y, r.uniform(2, 5), r.uniform(1.5, 3), r.uniform(0, 6.3), 7, 0.2, r), col("efe6d6") * r.uniform(0.9, 1.05), 0.9)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.45); h = blur(h, 1.2)
    return rgb, h, 2.2, 0.15


def k_rock(seed):
    """Bare rock: big weathered slabs split by irregular joints, rough pitted surface, strata, small lichen."""
    r = rng(seed)
    d1, d2, idx = voronoi(seed + 1, 18, 1.0)
    edge = warp(d2 - d1, seed + 9, 14, 16)
    idx = warp(idx, seed + 9, 14, 16)
    blockh = rng(seed + 2).random(idx.max() + 1)[idx]
    rough = spectral(seed + 4, 2.0, 2, 512)
    pits = np.clip(-spectral(seed + 7, 1.2, 30, 400) - 1.2, 0, 2)
    h = s01(rough) * 0.55 + blockh * 0.2 + np.clip(edge / 30, 0, 1) * 0.3 - pits * 0.1
    joint = np.clip(1 - edge / 2.5, 0, 1)
    fine = 1 - np.abs(warp(spectral(seed + 5, 2.0, 4, 200), seed + 11, 6, 20))
    hair = np.clip((fine ** 60) * 3, 0, 1) * 0.6
    crack = np.maximum(joint, hair)
    h = h - crack * 0.35
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    bands = 0.5 + 0.5 * np.sin((yy + spectral(seed + 6, 2.6, 1, 6) * 60) / N * 2 * math.pi * 5)
    grey = lerp(col("6a665f"), col("a8a197"), np.clip(s01(rough) * 0.7 + blockh * 0.3, 0, 1))
    rgb = lerp(grey, col("8f7a5e"), bands * 0.3 * s01(spectral(seed + 8, 2.4, 1, 8)))
    rgb *= (1 - crack * 0.5 - pits[..., ] * 0.1)[..., None]
    cv = Canvas(np.clip(rgb, 0, 1), s01(h))
    hh = s01(h)
    for i in range(500):
        x, y = r.uniform(0, N), r.uniform(0, N); rr = r.uniform(1.5, 6)
        c = col("a3a684") if r.random() < 0.75 else col("c4903e")
        cv.poly(ellipse(x, y, rr, rr * r.uniform(0.7, 1), r.uniform(0, 6.3), 12, 0.3, r), c * r.uniform(0.85, 1.1), float(hh[int(y) % N, int(x) % N]) + 0.01)
    rgb, h2 = cv.arrays()
    rgb = blur(rgb, 0.4)
    return rgb, h2, 6.0, 0.22


def k_snow(seed):
    """Snow: smooth wind-shaped drifts and long sastrugi ridges, faint sparkle, blue in the hollows."""
    r = rng(seed)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    drift = s01(spectral(seed + 1, 3.2, 1, 10))
    w = spectral(seed + 2, 3.0, 1, 6) * 50
    sast = 0.5 + 0.5 * np.sin((yy + w) / N * 2 * math.pi * 9 + spectral(seed + 3, 2.4, 1, 20) * 1.5)
    sast = sast ** 3 * s01(spectral(seed + 4, 2.6, 1, 6))
    soft = s01(spectral(seed + 5, 2.8, 4, 60))
    h = drift * 0.6 + sast * 0.32 + soft * 0.08
    rgb = lerp(col("c2cfe0"), col("f6f8fb"), np.clip(h * 1.25 - 0.05, 0, 1))
    sp = r.random((N, N)) > 0.9985
    rgb = np.where(sp[..., None], 1.0, rgb)
    rgb = blur(rgb, 0.5); h = blur(h, 1.5)
    return rgb, h, 1.5, 0.5


def k_paved(seed):
    """Paving and asphalt: aggregate stones in binder, patched repairs, a branching crack network, oil stains."""
    r = rng(seed)
    d1, d2, idx = voronoi(seed + 1, 9, 1.0)
    patch = rng(seed + 2).random(idx.max() + 1)[idx]
    stones = s01(blur(rng(seed + 3).standard_normal((N, N)), 1.3))
    v = 0.5 + 0.12 * (stones - 0.5)
    v += np.where(patch > 0.75, -0.025, 0.0) + np.where(patch < 0.12, 0.02, 0.0)    # newer and older patches
    seam = np.clip(1 - (d2 - d1) / 2.0, 0, 1) * (patch[..., ] >= 0)                 # patch edges
    stain = np.clip((s01(spectral(seed + 4, 2.4, 2, 40)) - 0.72) * 3, 0, 1)
    v -= stain * 0.06
    crack = 1 - np.abs(spectral(seed + 5, 2.0, 3, 200))
    cr = np.clip((crack ** 50) * 3, 0, 1) * (s01(spectral(seed + 6, 2.6, 1, 6)) > 0.55)
    v -= cr * 0.2 + seam * 0.04
    sp = r.random((N, N))
    v = np.where(sp < 0.015, v * 0.88, v); v = np.where(sp > 0.985, v * 1.12, v)
    rgb = np.stack([v, v * 0.995, v * 0.98], -1)
    rgb = blur(rgb, 0.4)
    h = 0.5 + 0.2 * (stones - 0.5) - cr * 0.35 - seam * 0.1
    return rgb, np.clip(h, 0, 1), 1.4, 0.15


def k_dirt(seed):
    """Bare dried earth: curled polygons of cracked mud, dust, scattered pebbles."""
    r = rng(seed)
    d1, d2, idx = voronoi(seed + 1, 110, 1.0)
    edge = warp(d2 - d1, seed + 9, 6, 24)
    idx = warp(idx, seed + 9, 6, 24)
    crack = np.clip(1 - edge / 4.0, 0, 1)
    plate = rng(seed + 2).random(idx.max() + 1)[idx]
    curl = np.clip(1 - edge / 22.0, 0, 1) ** 2               # plates curl up at their edges
    dust = s01(spectral(seed + 3, 1.2, 10, 400))
    h = 0.5 + curl * 0.25 - crack * 0.5 + dust * 0.1 + plate * 0.08
    rgb = lerp(col("8a6c4c"), col("b39470"), np.clip(dust * 0.6 + plate * 0.3 + curl * 0.2, 0, 1))
    rgb *= (1 - crack * 0.55)[..., None]
    big = s01(spectral(seed + 4, 2.6, 1, 6))
    rgb *= (0.9 + 0.2 * big)[..., None]
    cv = Canvas(np.clip(rgb, 0, 1), np.clip(h, 0, 1))
    for i in range(500):
        x, y = r.uniform(0, N), r.uniform(0, N); rr = r.uniform(2, 7)
        cv.poly(ellipse(x, y, rr, rr * r.uniform(0.6, 1), r.uniform(0, 6.3), 9, 0.2, r), col("8f8577") * r.uniform(0.75, 1.2), 0.75 + rr / 30)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.45); h = blur(h, 1.0)
    return rgb, h, 3.4, 0.08


def k_mud(seed):
    """Marsh mud: dark wet soil, shiny puddles, hoof and foot marks, flattened reeds."""
    r = rng(seed)
    a = s01(spectral(seed + 1, 2.4, 1, 40))
    h = a * 0.6 + s01(spectral(seed + 2, 1.0, 30, 400)) * 0.15
    wet = np.clip((0.38 - a) * 6, 0, 1)                      # puddles in the low spots
    rgb = lerp(col("2e2618"), col("4e4028"), s01(spectral(seed + 3, 1.6, 6, 300)))
    rgb = lerp(rgb, col("1f2018"), wet * 0.6)
    cv = Canvas(np.clip(rgb, 0, 1), np.clip(h, 0, 1))
    for i in range(60):                                       # prints pressed into the mud
        x, y = r.uniform(0, N), r.uniform(0, N); ang = r.uniform(0, 6.3)
        cv.poly(ellipse(x, y, 9, 6, ang, 12, 0.1, r), col("241c12"), 0.15)
    reeds = [col(c) for c in ("6a6a3a", "7a7646", "5a6030", "8a8050")]
    blades(cv, r, 900, reeds, (24, 60), (2, 3), 0.55, 0.8, 0.3)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.5); h = blur(h, 1.0)
    smooth = 0.25 + wet * 0.6
    return rgb, h, 1.6, smooth


MAKERS = {"grass": k_grass, "drygrass": k_drygrass, "forest": k_forest, "farm": k_farm, "sand": k_sand,
          "rock": k_rock, "snow": k_snow, "paved": k_paved, "dirt": k_dirt, "mud": k_mud}


def to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(np.maximum(c, 0), 1 / 2.4) - 0.055)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    means = []
    tiles = []
    for i, k in enumerate(KINDS):
        rgb, h, strength, smooth = MAKERS[k](1000 + i * 17)
        rgb = np.clip(rgb, 0, 1)
        h = np.clip(h, 0, 1)
        n = normals(h * 255.0 / 40.0, strength)
        ao = occlusion(h)
        sm = smooth if np.ndim(smooth) == 2 else np.full((N, N), smooth)
        a = np.dstack([rgb, h])
        b = np.dstack([n[..., 0] * 0.5 + 0.5, n[..., 1] * 0.5 + 0.5, ao, sm])
        Image.fromarray((a * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(out, k + "_a.png"), optimize=True)
        Image.fromarray((b * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(out, k + "_n.png"), optimize=True)
        lin = to_lin(rgb * ao[..., None] ** 0.5).reshape(-1, 3).mean(0)
        s = to_srgb(lin)
        means.append("%s %02x%02x%02x %.4f %.4f %.4f" % (k, *(int(round(v * 255)) for v in s), *lin))
        # shaded preview: light from the north-west
        L = np.array([-0.5, 0.5, 0.7]); L /= np.linalg.norm(L)
        sh = np.clip((n * L).sum(-1), 0, 1) * 0.75 + 0.35
        tiles.append(np.clip(rgb * ao[..., None] * sh[..., None], 0, 1))
        print(k, "done", flush=True)
    with open(os.path.join(out, "means.txt"), "w") as f:
        f.write("\n".join(means) + "\n")
    print("\n".join(means))
    if len(sys.argv) > 2:
        # each tile shown 2 x 2 so seams would show
        cells = []
        for t in tiles:
            big = np.tile(t, (2, 2, 1))
            cells.append(np.asarray(Image.fromarray((big * 255).astype(np.uint8)).resize((384, 384), Image.LANCZOS)))
        rows = [np.concatenate(cells[i:i + 5], 1) for i in (0, 5)]
        Image.fromarray(np.concatenate(rows, 0)).save(sys.argv[2])


if __name__ == "__main__":
    main()
