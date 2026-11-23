# bview.py in.bv out.png [azimuth] [elevation] [size] : a plain picture of what Program.cs "bview" wrote:
# walls in their own colour, windows as dark dots, steel lattice with its holes. For checking shapes without Unity.
import sys, math
import numpy as np
from PIL import Image
src, out = sys.argv[1], sys.argv[2]
az = float(sys.argv[3]) if len(sys.argv) > 3 else 30
el = float(sys.argv[4]) if len(sys.argv) > 4 else 16
W = int(sys.argv[5]) if len(sys.argv) > 5 else 900
b = open(src, "rb").read()
n = np.frombuffer(b, "<i4", 1)[0]
a = np.frombuffer(b, "<f4", n * 19, 4).reshape(n, 19)
P = a[:, :9].reshape(n, 3, 3).astype(float); UV = a[:, 9:15].reshape(n, 3, 2).astype(float)
A1, A2, COL, BITS = a[:, 15], a[:, 16], a[:, 17].astype(np.int64), a[:, 18].astype(np.int64)
th, e = math.radians(az), math.radians(el)
fwd = np.array([-math.sin(th) * math.cos(e), -math.sin(e), -math.cos(th) * math.cos(e)])
right = np.cross([0, 1, 0], fwd); right /= np.linalg.norm(right); up = np.cross(fwd, right)
X, Y, Z = P @ right, P @ up, P @ fwd
s = min((W - 20) / max(X.max() - X.min(), 1e-3), (W - 20) / max(Y.max() - Y.min(), 1e-3))
sx = W / 2 + (X - (X.max() + X.min()) / 2) * s; sy = W / 2 - (Y - (Y.max() + Y.min()) / 2) * s
img = np.zeros((W, W, 3)) + np.array([186, 208, 232]); zb = np.full((W, W), 1e9)
L = np.array([0.45, 0.8, -0.4]); L /= np.linalg.norm(L)
for t in range(n):
    x, y, z = sx[t], sy[t], Z[t]
    x0, x1 = int(max(0, math.floor(x.min()))), int(min(W - 1, math.ceil(x.max())))
    y0, y1 = int(max(0, math.floor(y.min()))), int(min(W - 1, math.ceil(y.max())))
    if x1 < x0 or y1 < y0: continue
    den = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
    if abs(den) < 1e-9: continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    w0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / den
    w1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / den
    w2 = 1 - w0 - w1
    ok = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
    zz = w0 * z[0] + w1 * z[1] + w2 * z[2]
    u = w0 * UV[t, 0, 0] + w1 * UV[t, 1, 0] + w2 * UV[t, 2, 0]; v = w0 * UV[t, 0, 1] + w1 * UV[t, 1, 1] + w2 * UV[t, 2, 1]
    c = int(COL[t]); col = np.array([(c >> 16) & 255, (c >> 8) & 255, c & 255], float)
    surf, mat = int(BITS[t]) & 7, (int(BITS[t]) >> 3) & 15
    nrm = np.cross(P[t, 1] - P[t, 0], P[t, 2] - P[t, 0]); ln = np.linalg.norm(nrm)
    if ln == 0: continue
    nrm /= ln
    if nrm @ fwd > 0: nrm = -nrm
    shade = np.full(gx.shape, 0.55 + 0.5 * max(0.0, float(nrm @ L)))
    if surf == 2 and mat == 13:
        w, h = max(A2[t], 0.01), max(A1[t], 0.01)
        fx, fy = np.clip(u, 0, 1), v - np.floor(v)
        tk = max(0.03 * w + 0.012, 0.8 / s)
        leg = np.minimum(fx, 1 - fx) * w - tk * 1.5; rail = np.minimum(fy, 1 - fy) * h - tk * 0.8
        diag = np.minimum(np.abs(fx - fy), np.abs(fx - (1 - fy))) * w * h / math.hypot(w, h) - tk * 0.7
        ok &= np.minimum(leg, np.minimum(rail, diag)) <= 0
    elif surf == 0 and A1[t] > 0.05:
        # a wall with windows: a dark dot in each bay of each floor, enough to tell it from a blank wall
        fy = v / A1[t] - np.floor(v / A1[t]); fx = u / 0.7 - np.floor(u / 0.7)
        shade = np.where((fy > 0.35) & (fy < 0.75) & (fx > 0.3) & (fx < 0.7) & (v > 0), shade * 0.45, shade)
    sub = zb[y0:y1 + 1, x0:x1 + 1]; hit = ok & (zz < sub)
    sub[hit] = zz[hit]
    img[y0:y1 + 1, x0:x1 + 1][hit] = np.clip(col[None, :] * shade[hit][:, None], 0, 255)
Image.fromarray(img.astype(np.uint8)).save(out)
