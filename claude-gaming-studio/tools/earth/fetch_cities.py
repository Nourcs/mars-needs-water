# Downloads the map data for every city in cities.py that is not on disk yet (cities/<name>/<layer>.parquet).
# usage: python3 fetch_cities.py <data root> [name ...]
import sys, os, ov
from cities import CITIES
root = sys.argv[1]; names = sys.argv[2:] or list(CITIES)
for n in names:
    b = CITIES[n]['box']; pad = 0.004; box = (b[0] - pad, b[1] - pad, b[2] + pad, b[3] + pad)
    os.makedirs(f'{root}/{n}', exist_ok=True)
    for k in ('roads', 'water', 'land', 'landuse', 'buildings'):
        f = f'{root}/{n}/{k}.parquet'
        if os.path.exists(f): continue
        try: ov.get(k, f + '.tmp', box); os.rename(f + '.tmp', f)
        except Exception as e: print('FAILED', n, k, e)
    print('done', n, flush=True)
