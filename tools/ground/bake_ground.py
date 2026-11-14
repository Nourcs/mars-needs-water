"""Bakes the ground detail textures for Mars Needs Water's mini Earth.

Everything is made from code (no photos): seamless 512 x 512 squares, one per ground kind.
Writes <out>/<kind>_a.png (RGB albedo, A height) and <kind>_n.png (RG normal, B occlusion, A smoothness),
plus <out>/means.txt with each kind's average colour (sRGB hex), used for the far-away map colours.
Usage: python3 bake_ground.py <outdir> [preview.png]
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

N = 512
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


# ---------------------------------------------------------------- the kinds

def k_grass(seed):
    r = rng(seed)
    patch = s01(spectral(seed + 1, 2.4, 1, 24))
    soil = lerp(col("2c3016"), col("3c3a1e"), patch)
    cv = Canvas(soil, 0.0)
    greens = [col(c) for c in ("4d7a2a", "5a8a30", "3f6b25", "6b9a3a", "7aa645", "567f2d", "8aa84f", "4a7030")]
    yellows = [col(c) for c in ("9a9a4a", "a59f55", "7f8a3c")]
    blades(cv, r, 26000, greens, (7, 20), (1, 2), 0.15, 1.0, 0.4, field=patch)
    blades(cv, r, 2600, yellows, (6, 16), (1, 2), 0.3, 0.9, 0.4)
    # a little clover
    for i in range(70):
        cx, cy = r.uniform(0, N), r.uniform(0, N)
        for k in range(3):
            a = r.uniform(0, 6.3)
            cv.poly(ellipse(cx + math.cos(a) * 3, cy + math.sin(a) * 3, 3.2, 2.6, a, 8), col("4f8a34") * r.uniform(0.9, 1.15), 0.95)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.45)
    h = blur(h, 0.8)
    # big soft light and dark patches so a field never looks like one stamp
    rgb *= (0.9 + 0.2 * blur(patch, 3))[..., None]
    return rgb, h, 3.0, 0.12


def k_drygrass(seed):
    r = rng(seed)
    patch = s01(spectral(seed + 1, 2.4, 1, 24))
    soil = lerp(col("6e5a3c"), col("8a7350"), s01(spectral(seed + 2, 1.6, 4, 200)))
    cv = Canvas(soil, 0.0)
    straw = [col(c) for c in ("b39e6a", "a68f5a", "c4ae78", "9c8550", "bba774", "8f7a4c", "a99a6c")]
    olive = [col(c) for c in ("7d7d4a", "6f7340", "8a8a55")]
    blades(cv, r, 15000, straw, (8, 22), (1, 2), 0.1, 1.0, 0.25, field=patch)
    blades(cv, r, 3000, olive, (6, 14), (1, 2), 0.2, 0.8, 0.3)
    # seed heads
    for i in range(500):
        x, y = r.uniform(0, N), r.uniform(0, N)
        cv.poly(ellipse(x, y, r.uniform(1.5, 3), 1.2, r.uniform(0, 6.3), 6), col("d2bf8c") * r.uniform(0.9, 1.1), 1.0)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.45)
    h = blur(h, 0.8)
    rgb *= (0.9 + 0.2 * blur(patch, 3))[..., None]
    return rgb, h, 2.6, 0.1


def k_forest(seed):
    r = rng(seed)
    soil = lerp(col("2e2216"), col("4a3622"), s01(spectral(seed + 1, 1.8, 2, 200)))
    cv = Canvas(soil, 0.0)
    leaves = [col(c) for c in ("6b4a26", "7f5a2c", "5a3d20", "8c6a34", "4a3a22", "6f5a30", "94703a", "57452a")]
    for i in range(5200):
        x, y = r.uniform(0, N), r.uniform(0, N)
        rx = r.uniform(3.5, 8); ry = rx * r.uniform(0.4, 0.7)
        c = leaves[r.integers(len(leaves))] * r.uniform(0.75, 1.15)
        cv.poly(ellipse(x, y, rx, ry, r.uniform(0, 6.3), 10, 0.12, r), c * (0.8 + 0.3 * i / 5200), 0.2 + 0.6 * i / 5200)
    # needles and twigs
    for i in range(4000):
        x, y = r.uniform(0, N), r.uniform(0, N); a = r.uniform(0, 6.3); L = r.uniform(5, 12)
        cv.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], col("7a4f2a") * r.uniform(0.7, 1.2), 1, 0.85)
    for i in range(120):
        x, y = r.uniform(0, N), r.uniform(0, N); a = r.uniform(0, 6.3); L = r.uniform(20, 60)
        pts = [(x, y)]
        for s in range(4):
            a += r.uniform(-0.3, 0.3); x += math.cos(a) * L / 4; y += math.sin(a) * L / 4; pts.append((x, y))
        cv.line(pts, col("4a3826") * r.uniform(0.8, 1.2), int(r.integers(2, 4)), 1.0)
    rgb, h = cv.arrays()
    # moss
    moss = s01(spectral(seed + 5, 2.2, 2, 60))
    mm = np.clip((moss - 0.62) * 5, 0, 1)
    mtex = lerp(col("3f5a22"), col("5f7a2e"), s01(spectral(seed + 6, 1.2, 30, 256)))
    rgb = lerp(rgb, mtex, mm * 0.85)
    h = h * (1 - mm * 0.3) + mm * 0.5
    rgb = blur(rgb, 0.5); h = blur(h, 1.0)
    return rgb, h, 2.4, 0.12


def k_farm(seed):
    r = rng(seed)
    clods = s01(spectral(seed + 1, 1.4, 8, 256))
    big = s01(spectral(seed + 2, 2.2, 1, 30))
    soil = lerp(col("5a4630"), col("7a6040"), clods * 0.7 + big * 0.3)
    hs = clods * 0.5
    cv = Canvas(soil, hs)
    greens = [col(c) for c in ("5d7f30", "6d8f3a", "4f6f2a", "7f9a45")]
    for i in range(3200):
        x, y = r.uniform(0, N), r.uniform(0, N)
        if big[int(y) % N, int(x) % N] < r.uniform(0.0, 0.7):
            continue
        c = greens[r.integers(len(greens))] * r.uniform(0.85, 1.15)
        for k in range(int(r.integers(3, 6))):
            a = r.uniform(0, 6.3); L = r.uniform(4, 10)
            cv.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], c, 2, 0.8 + k * 0.04)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.5); h = blur(h, 0.9)
    return rgb, h, 2.2, 0.08


def k_sand(seed):
    r = rng(seed)
    grain = spectral(seed + 1, 0.0, 120, 256)
    fine = s01(blur(grain, 0.6))
    big = s01(spectral(seed + 2, 2.4, 1, 20))
    warp = spectral(seed + 3, 2.6, 1, 8) * 18
    yy = np.arange(N)[:, None].astype(np.float64)
    rip = np.sin((yy + warp) * 2 * math.pi * 11 / N)
    rip = (np.abs(rip) ** 0.7) * np.sign(rip)
    h = 0.45 + 0.035 * rip * (0.3 + 0.7 * big) + 0.12 * (fine - 0.5) + 0.1 * (big - 0.5)
    rgb = lerp(col("b9a27a"), col("d8c69c"), fine * 0.6 + big * 0.4)
    # darker and lighter grains, a few shell bits
    spots = r.random((N, N))
    rgb = np.where((spots < 0.035)[..., None], rgb * 0.62, rgb)
    rgb = np.where((spots > 0.985)[..., None], np.minimum(rgb * 1.22, 1), rgb)
    rgb *= (0.97 + 0.04 * (rip * 0.5 + 0.5))[..., None]
    rgb = blur(rgb, 0.35)
    return rgb, np.clip(h, 0, 1), 1.6, 0.18


def k_rock(seed):
    base = spectral(seed + 1, 2.2, 1, 256)
    plates = 1 - np.abs(spectral(seed + 2, 2.6, 2, 60))
    fine = spectral(seed + 3, 1.0, 30, 256)
    h = s01(base * 0.8 + plates ** 2 * 0.8 + fine * 0.12)
    ridge = 1 - np.abs(spectral(seed + 7, 2.0, 3, 200))
    cracks = np.clip((ridge ** 60) * 3, 0, 1)
    h = np.clip(h - cracks * 0.18, 0, 1)
    tint = s01(spectral(seed + 4, 2.4, 1, 30))
    speck = s01(fine)
    grey = lerp(col("6f6b65"), col("a29c92"), s01(base) * 0.55 + speck * 0.45)
    brown = lerp(col("7d6a55"), col("a68f72"), s01(base) * 0.7 + speck * 0.3)
    rgb = lerp(grey, brown, tint * 0.55)
    lich = np.clip((s01(spectral(seed + 6, 1.8, 6, 120)) - 0.8) * 6, 0, 1)
    rgb = lerp(rgb, col("8f9468"), lich * 0.45)
    rgb *= (1 - cracks * 0.4)[..., None]
    rgb *= (0.85 + 0.25 * h)[..., None]
    rgb = blur(rgb, 0.4)
    return rgb, h, 5.0, 0.2


def k_snow(seed):
    big = s01(spectral(seed + 1, 2.6, 1, 30))
    mid = s01(spectral(seed + 2, 2.0, 4, 120))
    h = big * 0.7 + mid * 0.3
    rgb = lerp(col("c9d2de"), col("f4f6fa"), np.clip(h * 1.3 - 0.1, 0, 1))
    r = rng(seed)
    sp = r.random((N, N)) > 0.997
    rgb = np.where(sp[..., None], 1.0, rgb)
    rgb = blur(rgb, 0.4)
    return rgb, h, 1.2, 0.45


def k_paved(seed):
    agg = s01(spectral(seed + 1, 0.2, 90, 256))
    big = s01(spectral(seed + 2, 2.2, 1, 30))
    stain = np.clip((s01(spectral(seed + 3, 2.0, 2, 80)) - 0.6) * 3, 0, 1)
    v = 0.48 + 0.1 * (agg - 0.5) + 0.06 * (big - 0.5) - 0.12 * stain
    crack = 1 - np.abs(spectral(seed + 4, 2.0, 2, 150))
    cr = np.clip((crack ** 24) * 2, 0, 1) * (s01(spectral(seed + 5, 2.4, 1, 10)) > 0.6)
    v = v - cr * 0.18
    r = rng(seed)
    sp = r.random((N, N))
    v = np.where(sp < 0.02, v * 0.75, v); v = np.where(sp > 0.985, v * 1.2, v)
    rgb = np.stack([v * 1.0, v * 0.99, v * 0.97], -1)
    rgb = blur(rgb, 0.35)
    h = 0.5 + 0.25 * (agg - 0.5) - cr * 0.3
    return rgb, np.clip(h, 0, 1), 1.0, 0.15


def k_dirt(seed):
    r = rng(seed)
    a = s01(spectral(seed + 1, 1.6, 3, 256))
    big = s01(spectral(seed + 2, 2.4, 1, 24))
    soil = lerp(col("7a6044"), col("a08562"), a * 0.6 + big * 0.4)
    cv = Canvas(soil, a * 0.4)
    for i in range(1100):
        x, y = r.uniform(0, N), r.uniform(0, N); rr = r.uniform(1.2, 4.5)
        g = r.uniform(0.8, 1.12)
        c = (col("857a6a") if r.random() < 0.4 else col("9a8466")) * g
        cv.poly(ellipse(x, y, rr, rr * r.uniform(0.6, 1), r.uniform(0, 6.3), 9, 0.15, r), c, 0.55 + rr / 12)
    rgb, h = cv.arrays()
    crack = 1 - np.abs(spectral(seed + 4, 2.0, 3, 120))
    cr = np.clip((crack ** 16) * 2, 0, 1)
    rgb *= (1 - cr * 0.35)[..., None]
    h = h - cr * 0.2
    rgb = blur(rgb, 0.45); h = blur(h, 0.9)
    return rgb, np.clip(h, 0, 1), 2.6, 0.1


def k_mud(seed):
    r = rng(seed)
    a = s01(spectral(seed + 1, 2.0, 2, 200))
    h = a * 0.6 + s01(spectral(seed + 2, 1.2, 20, 256)) * 0.2
    rgb = lerp(col("2e261c"), col("4f4230"), a)
    wet = np.clip((0.45 - a) * 4, 0, 1)
    rgb = rgb * (1 - wet * 0.25)[..., None]
    cv = Canvas(rgb, h)
    greens = [col(c) for c in ("4a5a2a", "5a6a30", "3f4f25")]
    field = s01(spectral(seed + 3, 2.2, 1, 20))
    blades(cv, r, 3500, greens, (6, 16), (1, 2), 0.6, 1.0, 0.3, field=field)
    rgb, h = cv.arrays()
    rgb = blur(rgb, 0.45); h = blur(h, 0.8)
    smooth = 0.25 + wet * 0.5
    return rgb, h, 1.4, smooth


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
