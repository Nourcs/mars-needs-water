#!/usr/bin/env python3
"""Lake surfaces in the game against measured heights (Terrarium tiles, zoom 5).
usage: lake_levels.py <Earth data folder> <reference folder> <tiles folder with x_y.png or 5/x/y.png>"""
import sys, os, json, math, numpy as np
from PIL import Image
from shapely.geometry import shape
earth, ref, tiles = sys.argv[1:4]
lakes = np.fromfile(f'{earth}/earth_lakes.bytes', np.uint8).reshape(2048, 4096)
cache = {}
def real(lat, lon):
    n = 32; x = (lon + 180) / 360 * n; y = (1 - math.asinh(math.tan(math.radians(lat))) / math.pi) / 2 * n
    tx, ty = int(x), int(y)
    if (tx, ty) not in cache:
        for p in (f'{tiles}/{tx}_{ty}.png', f'{tiles}/5/{tx}/{ty}.png', f'{tiles}/5_{tx}_{ty}.png'):
            if os.path.exists(p): cache[(tx, ty)] = np.asarray(Image.open(p).convert('RGB')).astype(np.float64); break
        else: return None
    a = cache[(tx, ty)]; px = a[min(255, int((y - ty) * 256)), min(255, int((x - tx) * 256))]
    return px[0] * 256 + px[1] + px[2] / 256 - 32768
rows = []
for name in ('ne_lakes.geojson', 'ne_lakes_europe.geojson', 'ne_lakes_north_america.geojson'):
    for f in json.load(open(f'{ref}/{name}', encoding='utf8'))['features']:
        g = shape(f['geometry']); p = g.representative_point()
        if abs(p.y) > 84: continue
        area = g.area * 111 * 111 * math.cos(math.radians(p.y))
        if area < 100: continue
        b = lakes[int((90 - p.y) / 180 * 2048), int((p.x + 180) / 360 * 4096)]
        r = real(p.y, p.x)
        if b == 0 or r is None: continue
        rows.append((f['properties'].get('name') or '?', round(area), round(r), round(b * 1.09 / 0.0235)))
d = np.array([abs(r[3] - r[2]) for r in rows])
print('lakes over 100 km2 compared', len(rows), 'median difference m', float(np.median(d)), 'within 60 m', int((d <= 60).sum()), 'off by more than 150 m', int((d > 150).sum()))
for r in sorted([r for r in rows if abs(r[3] - r[2]) > 150], key=lambda r: -r[1])[:25]: print(' ', r)
