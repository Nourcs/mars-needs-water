# Preview renderer: z-buffer, smooth normals, soft cel shading like the game's look.
import numpy as np, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from forge import _n
import models

def render(p, W, H, yaw=35, pitch=20, bg=(232, 226, 214)):
    P, N, C = p
    P = P.copy(); N = N.copy()
    P[:, 0] *= -1; N[:, 0] *= -1                          # view Unity's left-handed space the right way round
    ya, pa = np.radians(yaw), np.radians(pitch)
    Ry = np.array([[np.cos(ya), 0, np.sin(ya)], [0, 1, 0], [-np.sin(ya), 0, np.cos(ya)]])
    Rx = np.array([[1, 0, 0], [0, np.cos(pa), -np.sin(pa)], [0, np.sin(pa), np.cos(pa)]])
    M = Rx @ Ry
    ymin = P[:, 1].min()
    Q = P @ M.T; Nq = N @ M.T
    lo, hi = Q.min(0), Q.max(0); ctr = (lo + hi) / 2
    s = min(W * 0.8 / (hi[0] - lo[0]), H * 0.78 / (hi[1] - lo[1]))
    X = W / 2 + (Q[:, 0] - ctr[0]) * s; Y = H * 0.48 - (Q[:, 1] - ctr[1]) * s; Z = Q[:, 2]
    img = np.tile(np.array(bg, float), (H, W, 1)); zb = np.full((H, W), -1e9)
    # contact shadow: the footprint squashed onto the ground
    G = P.copy(); G[:, 1] = ymin; Gq = G @ M.T
    sh = Image.new('L', (W, H), 0); d = ImageDraw.Draw(sh)
    gx = W / 2 + (Gq[:, 0] - ctr[0]) * s; gy = H * 0.48 - (Gq[:, 1] - ctr[1]) * s
    for t in range(0, len(P), 3): d.polygon([(gx[t + k], gy[t + k]) for k in range(3)], fill=90)
    sh = np.asarray(sh.filter(ImageFilter.GaussianBlur(W / 60)), float) / 255
    img *= (1 - 0.55 * sh[..., None])
    L = _n(np.array([-0.45, 0.75, 0.5])); V = np.array([0, 0, 1.0])
    for t in range(0, len(P), 3):
        x, y, z = X[t:t + 3], Y[t:t + 3], Z[t:t + 3]
        x0, x1 = int(max(np.floor(x.min()), 0)), int(min(np.ceil(x.max()), W - 1))
        y0, y1 = int(max(np.floor(y.min()), 0)), int(min(np.ceil(y.max()), H - 1))
        if x1 < x0 or y1 < y0: continue
        den = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(den) < 1e-9: continue
        gx_, gy_ = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        a = ((y[1] - y[2]) * (gx_ - x[2]) + (x[2] - x[1]) * (gy_ - y[2])) / den
        b = ((y[2] - y[0]) * (gx_ - x[2]) + (x[0] - x[2]) * (gy_ - y[2])) / den
        c = 1 - a - b
        m = (a >= -1e-4) & (b >= -1e-4) & (c >= -1e-4)
        zz = a * z[0] + b * z[1] + c * z[2]
        sub = zb[y0:y1 + 1, x0:x1 + 1]; m &= zz > sub
        if not m.any(): continue
        nn = _n(a[..., None] * Nq[t] + b[..., None] * Nq[t + 1] + c[..., None] * Nq[t + 2])
        nn = np.where((nn[..., 2:3] < 0), -nn, nn) if False else nn
        ndl = nn @ L
        lit = np.clip((ndl + 0.15) / 0.5, 0, 1)                    # soft two-band cel ramp
        lit = lit * lit * (3 - 2 * lit)
        shade = 0.5 + 0.55 * lit + 0.08 * np.clip(nn[..., 1], 0, 1)
        rim = np.clip(1 - nn @ V, 0, 1) ** 3 * 0.25
        hv = _n(L + V); spec = np.clip(nn @ hv, 0, 1) ** 40 * 0.35 * lit
        col = C[t][None, None, :] * shade[..., None] + 255 * (rim + spec)[..., None]
        reg = img[y0:y1 + 1, x0:x1 + 1]
        reg[m] = np.clip(col, 0, 255)[m]; sub[m] = zz[m]
    return Image.fromarray(img.astype(np.uint8))

if __name__ == '__main__':
    out = sys.argv[1]; SS = 2
    cells = [('own-spoon', 'Spoon', 215, 38), ('own-bucket', 'Bucket', 30, 22), ('own-jerrycan', 'Jerrycan (10 L tank)', 140, 16), ('own-rocket', 'Rocket', 62, 8)]
    cw, ch = 520, 560
    sheet = Image.new('RGB', (cw * len(cells), ch), (232, 226, 214)); d = ImageDraw.Draw(sheet)
    f = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 26)
    for i, (k, label, yaw, pit) in enumerate(cells):
        im = render(models.ALL[k](), cw * SS, ch * SS, yaw, pit).resize((cw, ch), Image.LANCZOS)
        sheet.paste(im, (i * cw, 0)); d.text((i * cw + 20, 16), label, fill=(40, 40, 46), font=f)
    sheet.save(out)
