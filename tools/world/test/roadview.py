# Top-down picture of a tile's road mesh, drawn the way EarthStreet.shader draws it (shapes, colours, order),
# to check the geometry without starting Unity.
#   dotnet run -c Release -- roads LAT LON /tmp/r      then      python3 roadview.py /tmp/r CX CY SPAN [PX] out.png
# CX, CY: centre in tile metres (east, south; the "at" line the first command prints), SPAN: metres across.
import sys, struct, numpy as np
from PIL import Image
base, cx, cy, span = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4])
px = int(sys.argv[5]) if len(sys.argv) > 6 else 1400
out = sys.argv[-1]
d = open(base + ".road", "rb").read()
size, nv = struct.unpack_from("<fi", d, 0); o = 8
P = np.frombuffer(d, "<f4", nv * 3, o).reshape(nv, 3); o += nv * 12
U = np.frombuffer(d, "<f4", nv * 2, o).reshape(nv, 2); o += nv * 8
X = np.frombuffer(d, "<f4", nv * 4, o).reshape(nv, 4); o += nv * 16
Y = np.frombuffer(d, "<f4", nv * 4, o).reshape(nv, 4); o += nv * 16
ni = struct.unpack_from("<i", d, o)[0]; o += 4
I = np.frombuffer(d, "<i4", ni, o).reshape(-1, 3)
tex = np.frombuffer(open(base + ".tex", "rb").read(), np.uint8)
n = int(round((len(tex) // 4) ** 0.5))
tex = tex.reshape(n, n, 4)[:, :, :3]
m = span / px                                   # metres per pixel
x0, y0 = cx - span / 2, cy - span / 2
ys, xs = np.mgrid[0:px, 0:px]
gx = np.clip(((x0 + (xs + 0.5) * m) / size * n).astype(int), 0, n - 1); gy = np.clip(((y0 + (ys + 0.5) * m) / size * n).astype(int), 0, n - 1)
img = tex[gy, gx].astype(np.float32) / 255.0
ex = P[:, 0] + size / 2; ey = size / 2 - P[:, 2]            # east, south metres
sx = (ex - x0) / m; sy = (ey - y0) / m
def hashn(a, b): return np.modf(np.sin(a * 12.9898 + b * 78.233) * 43758.5453)[0] % 1.0
for t in I:
    ax, ay, bx, by, cxx, cyy = sx[t[0]], sy[t[0]], sx[t[1]], sy[t[1]], sx[t[2]], sy[t[2]]
    minx, maxx = int(max(0, np.floor(min(ax, bx, cxx)))), int(min(px - 1, np.ceil(max(ax, bx, cxx))))
    miny, maxy = int(max(0, np.floor(min(ay, by, cyy)))), int(min(px - 1, np.ceil(max(ay, by, cyy))))
    if minx > maxx or miny > maxy: continue
    den = (by - cyy) * (ax - cxx) + (cxx - bx) * (ay - cyy)
    if abs(den) < 1e-9: continue
    yy, xx = np.mgrid[miny:maxy + 1, minx:maxx + 1]
    fx, fy = xx + 0.5, yy + 0.5
    w0 = ((by - cyy) * (fx - cxx) + (cxx - bx) * (fy - cyy)) / den
    w1 = ((cyy - ay) * (fx - cxx) + (ax - cxx) * (fy - cyy)) / den
    w2 = 1 - w0 - w1
    e = 0.5 / max(1.0, abs(den) ** 0.5)
    ins = (w0 >= -e) & (w1 >= -e) & (w2 >= -e)
    if not ins.any(): continue
    a = w0 * U[t[0], 0] + w1 * U[t[1], 0] + w2 * U[t[2], 0]; b = w0 * U[t[0], 1] + w1 * U[t[1], 1] + w2 * U[t[2], 1]
    hx, hy, rr, soft = X[t[0]]; col, kind, seed, extra = Y[t[0]]
    qx = np.abs(a) - (hx - rr); qy = np.abs(b) - (hy - rr)
    sd = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - rr
    if rr < 0: sd = -rr - np.hypot(a, b)
    cov = np.clip(-sd / max(soft, m), 0, 1) * ins
    c = int(col); rgb = np.array([(c >> 16) & 255, (c >> 8) & 255, c & 255], np.float32) / 255.0
    rgb = np.broadcast_to(rgb, a.shape + (3,)).copy()
    k = int(kind)
    if k == 2:      # slabs
        acr = sd + hx - extra
        j = (np.abs(np.modf(acr / 0.42)[0]) < 0.06) | (np.abs(np.modf(b / 0.6)[0]) < 0.04)
        rgb[j] *= 0.9
    elif k == 10: cov *= 0.6
    elif k == 11:   # sleepers
        sl = (np.modf(np.abs(b) / 0.5)[0] < 0.42) & (np.abs(a) < 0.5)
        rgb[sl] = np.array([0.42, 0.32, 0.24])
    elif k == 6:
        rgb[np.modf(np.abs(b) / 0.3)[0] < 0.25] *= 0.8
    elif k == 9:
        rgb[np.abs(np.abs(a) - hx * 0.45) < hx * 0.22] *= 0.86
    sub = img[miny:maxy + 1, minx:maxx + 1]
    sub += (rgb - sub) * cov[..., None]
Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(out)
print("saved", out, "tris", len(I))
