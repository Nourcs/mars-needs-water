# Fetches the transit furniture (bus stops, tram stops, metro and railway stations) of every rebuilt city core.
# usage: python3 fetch_infra.py <cities dir>
import sys, os, ov
from cities import CITIES
for name, c in CITIES.items():
    out = f'{sys.argv[1]}/{name}/infra.parquet'
    if os.path.exists(out): continue
    try: ov.get('infra', out, c['box']); print(name, 'ok', flush=True)
    except Exception as e: print(name, 'FAILED', repr(e)[:200], flush=True)
