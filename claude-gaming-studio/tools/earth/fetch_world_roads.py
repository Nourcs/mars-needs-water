# Downloads every motorway, trunk and primary road on Earth from Overture Maps (© OpenStreetMap contributors, Overture
# Maps Foundation; ODbL) and keeps a simplified copy: one shard per source file, resumable.
# usage: python3 fetch_world_roads.py <out dir> [first file] [last file]
import sys, os, io, pickle, time, numpy as np, pyarrow.parquet as pq, pyarrow.compute as pc, shapely
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'sf'))
import ov
from multiprocessing import Pool
WANT = {'motorway': 0, 'trunk': 1, 'primary': 2}
TOL = 0.0012                                             # degrees: about 0.5 m on the game's planet
def one(args):
    i, (k, size), out = args; dst = f'{out}/{i:03d}.pkl'
    if os.path.exists(dst): return i, -1
    f = pq.ParquetFile(io.BufferedReader(ov.RF(k, size), buffer_size=4 << 20)); res = []
    for g in range(f.metadata.num_row_groups):
        t = f.read_row_group(g, columns=['class', 'subtype'])
        m = pc.and_(pc.equal(t.column('subtype'), 'road'), pc.is_in(t.column('class'), value_set=__import__('pyarrow').array(list(WANT))))
        if pc.sum(m).as_py() in (0, None): continue
        cls = t.column('class').filter(m).to_pylist()
        geo = f.read_row_group(g, columns=['geometry']).column('geometry').filter(m).to_pylist()
        gs = shapely.simplify(shapely.from_wkb(geo), TOL, preserve_topology=False)
        for c, gg in zip(cls, gs):
            for part in (gg.geoms if gg.geom_type.startswith('Multi') else [gg]):
                if part.geom_type == 'LineString' and len(part.coords) >= 2: res.append((WANT[c], np.asarray(part.coords, np.float32)[:, :2]))
    pickle.dump(res, open(dst + '.tmp', 'wb'), protocol=4); os.replace(dst + '.tmp', dst)
    return i, len(res)
if __name__ == '__main__':
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    files = ov.listing(ov.REL + 'theme=transportation/type=segment/'); a = int(sys.argv[2]) if len(sys.argv) > 2 else 0; b = int(sys.argv[3]) if len(sys.argv) > 3 else len(files)
    t0 = time.time()
    with Pool(8) as pool:
        for i, n in pool.imap_unordered(one, [(i, files[i], out) for i in range(a, b)]): print(i, n, round(time.time() - t0), 's', flush=True)
    print('finished', flush=True)
