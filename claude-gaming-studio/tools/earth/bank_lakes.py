# Stops lakes flooding the land beside them. A lake's surface height is stored per half-resolution cell and
# reaches one cell past the shore; any land in or right beside those cells that lies below the lake's surface
# used to be drawn under water, as a slab of water hanging in the air. This raises such land to two levels
# above the lake (a low bank). usage: python3 bank_lakes.py <Resources/Earth> <pre-depth earth_height.bytes>
import sys, numpy as np
from scipy import ndimage
earth, before = sys.argv[1], sys.argv[2]
W, H = 4096, 2048
h = np.fromfile(f'{earth}/earth_height.bytes', np.uint8).reshape(H, W)
water = np.fromfile(before, np.uint8).reshape(H, W) < 64
lo = np.fromfile(f'{earth}/earth_lakes.bytes', np.uint8).reshape(1024, 2048)
L = np.repeat(np.repeat(lo, 2, axis=0), 2, axis=1).astype(np.int32)
L = ndimage.grey_dilation(L, size=(3, 3), mode='wrap')          # one pixel past the cells too (heights are blended)
low = (L > 0) & ~water & (h.astype(np.int32) - 64 < L + 2)
out = h.copy(); out[low] = np.clip(64 + L[low] + 2, 0, 255).astype(np.uint8)
print('raised pixels', int(low.sum()), 'of', int((L > 0).sum()), 'lake-level pixels')
out.tofile(f'{earth}/earth_height.bytes')
