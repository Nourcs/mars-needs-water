# Reads towns.bytes exactly the way CityReplica.LoadTowns / ParseTown do, to prove the two agree. usage: check_towns.py file
import sys, struct
b = open(sys.argv[1], 'rb').read(); assert b[:4] == b'MNWT'
n, = struct.unpack_from('<i', b, 4); ends = [struct.unpack_from('<i', b, 8 + 20 * i + 16)[0] for i in range(n)] + [struct.unpack_from('<i', b, len(b) - 4)[0]]
assert ends[-1] == len(b) - 4 and ends[0] == 8 + 20 * n
ns_t = nb_t = 0; last = -1
for i in range(n):
    place, lat, lon, r, o = struct.unpack_from('<ifffi', b, 8 + 20 * i); assert place > last and -90 <= lat <= 90 and 10 <= r <= 260; last = place
    ns, = struct.unpack_from('<H', b, o); o += 2
    for _ in range(ns):
        cls, k = b[o], b[o + 1]; assert cls < 9 and k >= 2; o += 2 + 4 * k
    nb, = struct.unpack_from('<H', b, o); o += 2
    for _ in range(nb):
        k = b[o + 4]; assert k >= 3 and b[o] > 0; o += 5 + 4 * k; nt = b[o]; o += 1; assert nt >= 1 and max(b[o:o + 3 * nt]) < k; o += 3 * nt
    assert o == ends[i + 1], (i, o, ends[i + 1]); ns_t += ns; nb_t += nb
print('ok', n, 'towns', ns_t, 'streets', nb_t, 'buildings', len(b) >> 10, 'KB')
