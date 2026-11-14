# Downloads the 1,024 zoom-5 Terrarium elevation tiles (Mapzen / AWS open data: real land heights and sea depths).
import sys, os, requests
from concurrent.futures import ThreadPoolExecutor
out = sys.argv[1]; os.makedirs(out, exist_ok=True); s = requests.Session()
def get(xy):
    x, y = xy; f = f'{out}/{x}_{y}.png'
    if os.path.exists(f) and os.path.getsize(f) > 100: return 0
    for t in range(5):
        try:
            r = s.get(f'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/5/{x}/{y}.png', timeout=60)
            if r.status_code == 200: open(f, 'wb').write(r.content); return 1
        except Exception: pass
    print('FAILED', x, y, flush=True); return 0
with ThreadPoolExecutor(6) as ex: n = sum(ex.map(get, [(x, y) for x in range(32) for y in range(32)]))
print('fetched', n, 'have', len(os.listdir(out)))
