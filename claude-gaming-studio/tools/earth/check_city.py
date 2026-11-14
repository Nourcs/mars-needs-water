# Reads a baked city file exactly the way CityReplica.Parse does, to prove the two agree. usage: check_city.py file...
import sys, struct
for f in sys.argv[1:]:
    b = open(f, 'rb').read(); assert b[:4] == b'MNWC' and struct.unpack_from('<i', b, 4)[0] == 2
    o = 8
    def rd(fmt):
        global o
        v = struct.unpack_from('<' + fmt, b, o); o += struct.calcsize('<' + fmt); return v
    site = rd('ffffffi'); hw, hd, sc, cell, gw, gd, gx0, gz0 = rd('ffffiiff'); o += 3 * gw * gd
    ns, = rd('i')
    for _ in range(ns): c, e, n = rd('BBH'); assert n >= 2 and c < 9; o += 8 * n
    nb, = rd('i'); mv = 0
    for _ in range(nb):
        h, r, g, bl, fl, n = rd('fBBBBH'); o += 8 * n; nt, = rd('H'); tr = rd(f'{3 * nt}H'); assert not tr or max(tr) < n; assert 0 < h < 400; mv = max(mv, n)
    nl, = rd('i'); o += 18 * nl
    nh, = rd('i'); o += 20 * nh
    nc, = rd('i'); o += 3 * nc
    assert o == len(b), (o, len(b))
    print(f.split('/')[-1], 'ok', ns, nb, nl, nh, nc, 'max verts', mv)
