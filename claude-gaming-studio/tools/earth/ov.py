# Reads a bounding box out of the Overture Maps open data on S3 (GeoParquet) using HTTP range requests.
import io, sys, re, json, time, requests, pyarrow.parquet as pq, pyarrow as pa
from concurrent.futures import ThreadPoolExecutor
BASE = 'https://overturemaps-us-west-2.s3.amazonaws.com/'
S = requests.Session(); S.verify = '/root/.ccr/ca-bundle.crt'
class RF(io.RawIOBase):
    def __init__(self, key, size): self.url = BASE + key; self.size = size; self.pos = 0
    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos
    def seek(self, off, wh=0):
        self.pos = off if wh == 0 else self.pos + off if wh == 1 else self.size + off; return self.pos
    def read(self, n=-1):
        if n < 0: n = self.size - self.pos
        if n == 0: return b''
        for a in range(5):
            try:
                r = S.get(self.url, headers={'Range': f'bytes={self.pos}-{self.pos + n - 1}'}, timeout=120); r.raise_for_status(); break
            except Exception as e:
                if a == 4: raise
                time.sleep(2)
        self.pos += len(r.content); return r.content
    def readinto(self, b):
        d = self.read(len(b)); b[:len(d)] = d; return len(d)
def listing(prefix):
    out = []; tok = None
    while True:
        u = BASE + '?list-type=2&prefix=' + prefix + ('&continuation-token=' + requests.utils.quote(tok, safe='') if tok else '')
        x = S.get(u, timeout=60).text
        out += [(k, int(s)) for k, s in re.findall(r'<Key>([^<]+)</Key>.*?<Size>(\d+)</Size>', x)]
        m = re.search(r'<NextContinuationToken>([^<]+)<', x)
        if not m: return out
        tok = m.group(1)
def fetch(prefix, box, cols):
    xmin, ymin, xmax, ymax = box; files = listing(prefix); print(len(files), 'files', file=sys.stderr)
    def one(ks):
        k, size = ks
        f = pq.ParquetFile(io.BufferedReader(RF(k, size), buffer_size=1 << 20))
        md = f.metadata; names = [md.schema.column(i).path for i in range(md.num_columns)]
        ix = {n: names.index('bbox.' + n) for n in ('xmin', 'xmax', 'ymin', 'ymax')}
        rgs = []
        for g in range(md.num_row_groups):
            rg = md.row_group(g); st = {n: rg.column(i).statistics for n, i in ix.items()}
            if any(s is None for s in st.values()): rgs.append(g); continue
            if st['xmax'].max < xmin or st['xmin'].min > xmax or st['ymax'].max < ymin or st['ymin'].min > ymax: continue
            rgs.append(g)
        if not rgs: return None
        t = f.read_row_groups(rgs, columns=cols + ['bbox'])
        b = t.column('bbox').combine_chunks()
        import pyarrow.compute as pc
        m = pc.and_(pc.and_(pc.greater(b.field('xmax'), xmin), pc.less(b.field('xmin'), xmax)), pc.and_(pc.greater(b.field('ymax'), ymin), pc.less(b.field('ymin'), ymax)))
        t = t.filter(m); print(k[-60:], len(rgs), t.num_rows, file=sys.stderr)
        return t
    with ThreadPoolExecutor(12) as ex: ts = [t for t in ex.map(one, files) if t is not None and t.num_rows]
    return pa.concat_tables(ts, promote_options='default') if ts else None
REL = 'release/2026-10-21.0/'
LAYERS = {'buildings': ('theme=buildings/type=building/', ['id', 'geometry', 'height', 'num_floors', 'class', 'names', 'roof_shape']),
          'roads': ('theme=transportation/type=segment/', ['id', 'geometry', 'subtype', 'class', 'names']),
          'water': ('theme=base/type=water/', ['id', 'geometry', 'subtype', 'class']),
          'land': ('theme=base/type=land/', ['id', 'geometry', 'subtype', 'class']),
          'infra': ('theme=base/type=infrastructure/', ['id', 'geometry', 'subtype', 'class', 'names']),
          'landuse': ('theme=base/type=land_use/', ['id', 'geometry', 'subtype', 'class'])}
def get(kind, out, box):
    t = fetch(REL + LAYERS[kind][0], box, LAYERS[kind][1])
    if t is None: t = pa.table({c: pa.array([], pa.binary() if c == 'geometry' else pa.string()) for c in LAYERS[kind][1]})
    pq.write_table(t, out); print(kind, t.num_rows, 'rows'); return t.num_rows
if __name__ == '__main__':
    # usage: python3 ov.py <buildings|roads|water|land|landuse> out.parquet lon0 lat0 lon1 lat1
    get(sys.argv[1], sys.argv[2], tuple(float(v) for v in sys.argv[3:7]))
