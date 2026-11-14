# Bakes every city in cities.py whose data is on disk. usage: python3 bake_all.py <data root> <out dir> [name ...]
import sys, os, subprocess
from cities import CITIES
root, out = sys.argv[1:3]; os.makedirs(out, exist_ok=True)
for n in sys.argv[3:] or list(CITIES):
    if not os.path.exists(f'{root}/{n}/buildings.parquet'): print(n, 'no data yet'); continue
    r = subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'bake_city.py'), n, f'{root}/{n}', f'{out}/{n}.bytes', f'{out}/{n}.png'], capture_output=True, text=True)
    print(r.stdout.strip().replace('\n', ' | ')); 
    if r.returncode: print('FAILED', n, r.stderr[-600:])
