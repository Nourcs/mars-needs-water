using System;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The landmark models, each written from the real structure's measurements in metres.
    /// Unless noted, a model's front faces +z and Landmark.Yaw turns that to its true bearing.
    /// </summary>
    public static class LandmarkModels
    {
        const uint Gold = 0xe8c14a, Marble = 0xf2efe6, Slate = 0x5a6068, Dark = 0x3a3a3e, Concrete = 0xcfcac0, White = 0xf2f4f5;
        static double Rad(double d) { return d * Geo.Deg; }

        // ---- shapes shared by several models

        /// <summary>A skin stretched over a row of rings (each the same number of x,y,z points, going round).</summary>
        static void Loft(Sculpt s, double[][] rings, uint col, bool capStart = true, bool capEnd = true, uint[] cols = null)
        {
            int n = rings[0].Length / 3;
            for (int r = 0; r + 1 < rings.Length; r++)
            {
                double[] a = rings[r], b = rings[r + 1];
                uint cc = cols != null ? cols[Math.Min(r, cols.Length - 1)] : col;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    uint sc = Sculpt.Shade(cc, 0.93 + 0.07 * Math.Cos(i * 4 * Math.PI / n));
                    s.Tri(a[i * 3], a[i * 3 + 1], a[i * 3 + 2], a[j * 3], a[j * 3 + 1], a[j * 3 + 2], b[j * 3], b[j * 3 + 1], b[j * 3 + 2], sc);
                    s.Tri(a[i * 3], a[i * 3 + 1], a[i * 3 + 2], b[j * 3], b[j * 3 + 1], b[j * 3 + 2], b[i * 3], b[i * 3 + 1], b[i * 3 + 2], sc);
                }
            }
            foreach (int end in new[] { 0, rings.Length - 1 })
            {
                if (end == 0 ? !capStart : !capEnd) continue;
                double[] a = rings[end];
                for (int i = 1; i + 1 < n; i++) s.Tri(a[0], a[1], a[2], a[i * 3], a[i * 3 + 1], a[i * 3 + 2], a[i * 3 + 3], a[i * 3 + 4], a[i * 3 + 5], cols != null ? cols[Math.Min(end, cols.Length - 1)] : col);
            }
        }

        /// <summary>An oval ring of n points lying flat (axis 'y'), or standing across x ('x') or across z ('z').</summary>
        static double[] Oval(char axis, double cx, double cy, double cz, double ra, double rb, int n, double turn = 0)
        {
            var o = new double[n * 3];
            for (int i = 0; i < n; i++)
            {
                double a = turn + i * 2 * Math.PI / n, u = ra * Math.Cos(a), v = rb * Math.Sin(a);
                o[i * 3] = cx + (axis == 'x' ? 0 : u); o[i * 3 + 1] = cy + (axis == 'y' ? 0 : axis == 'x' ? u : v); o[i * 3 + 2] = cz + (axis == 'z' ? 0 : v);
            }
            return o;
        }

        /// <summary>
        /// A wall cut out of a flat outline: xy pairs give its shape seen from the front, and it runs from z0 to z1.
        /// With it an arch is a real opening. turned puts the outline in the z-y plane instead, running from x = z0 to z1.
        /// </summary>
        static void Plate(Sculpt s, double[] xy, double z0, double z1, uint col, bool turned = false, uint face = 0)
        {
            int n = xy.Length / 2;
            if (face == 0) face = col;
            Func<int, double, double[]> at = (i, z) => turned ? new[] { z, xy[i * 2 + 1], xy[i * 2] } : new[] { xy[i * 2], xy[i * 2 + 1], z };
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                s.Quad(at(i, z0), at(j, z0), at(j, z1), at(i, z1), Sculpt.Shade(col, Math.Abs(xy[i * 2] - xy[j * 2]) > Math.Abs(xy[i * 2 + 1] - xy[j * 2 + 1]) ? 0.82 : 0.9));
            }
            var t = Earcut.Triangulate(xy, null);
            foreach (double z in new[] { z0, z1 })
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    double[] a = at(t[i], z), b = at(t[i + 1], z), c = at(t[i + 2], z);
                    s.Tri(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2], face);
                }
        }

        /// <summary>The outline of a wall w wide and h high (centred on x = cx, standing on y0) with round-headed openings: each is x, width, height to the crown.</summary>
        static double[] Arched(double cx, double y0, double w, double h, params double[] openings)
        {
            var o = new System.Collections.Generic.List<double> { cx + w / 2, y0, cx + w / 2, y0 + h, cx - w / 2, y0 + h, cx - w / 2, y0 };
            for (int k = 0; k + 2 < openings.Length; k += 3)
            {
                double x = cx + openings[k], r = openings[k + 1] / 2, top = openings[k + 2];
                o.Add(x - r); o.Add(y0);
                for (int i = 0; i <= 8; i++) { double a = Math.PI - i * Math.PI / 8; o.Add(x + r * Math.Cos(a)); o.Add(y0 + top - r + r * Math.Sin(a)); }
                o.Add(x + r); o.Add(y0);
            }
            return o.ToArray();
        }

        /// <summary>Like Arched, but the openings come to a point (as in Mughal and Gothic arches).</summary>
        static double[] Pointed(double cx, double y0, double w, double h, params double[] openings)
        {
            var o = new System.Collections.Generic.List<double> { cx + w / 2, y0, cx + w / 2, y0 + h, cx - w / 2, y0 + h, cx - w / 2, y0 };
            for (int k = 0; k + 2 < openings.Length; k += 3)
            {
                double x = cx + openings[k], r = openings[k + 1] / 2, top = openings[k + 2], rise = r * 1.15;
                o.Add(x - r); o.Add(y0);
                for (int i = 0; i <= 8; i++)
                {
                    double t = i / 4.0 - 1, m = 1 - Math.Abs(t);
                    o.Add(x + r * Math.Sign(t) * (1 - m * m)); o.Add(y0 + top - rise + rise * m);
                }
                o.Add(x + r); o.Add(y0);
            }
            return o.ToArray();
        }

        /// <summary>A flat panel standing on a wall (its foot at x, y0, z; turned by yaw): a window, door or niche. kind 1 has a round head, 2 a pointed one.</summary>
        static void Panel(Sculpt s, double x, double y0, double z, double w, double h, double yaw, uint col, int kind = 0, double deep = 0.5)
        {
            s.Push(x, y0, z, yaw);
            double r = w / 2, rise = kind == 2 ? r * 1.3 : r, hh = kind == 0 ? h : h - rise;
            s.Box(0, 0, 0, w, hh, deep, col);
            if (kind != 0)
                foreach (double zz in new[] { -deep / 2, deep / 2 })
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 6, b = (i + 1) * Math.PI / 6;
                        double ya = kind == 2 ? rise * (1 - Math.Pow(Math.Abs(Math.Cos(a)), 1.7)) : r * Math.Sin(a), yb = kind == 2 ? rise * (1 - Math.Pow(Math.Abs(Math.Cos(b)), 1.7)) : r * Math.Sin(b);
                        s.Tri(0, hh, zz, r * Math.Cos(a), hh + ya, zz, r * Math.Cos(b), hh + yb, zz, col);
                    }
            s.Pop();
        }

        /// <summary>n things spaced evenly along a wall from a to b: panels (kind 0, 1 round-headed, 2 pointed) or round columns (kind 3).</summary>
        static void Row(Sculpt s, double ax, double az, double bx, double bz, double y0, int n, double w, double h, uint col, int kind = 0, double deep = 0.5)
        {
            double yaw = -Math.Atan2(bz - az, bx - ax) / Geo.Deg;
            for (int i = 0; i < n; i++)
            {
                double f = (i + 0.5) / n, x = ax + (bx - ax) * f, z = az + (bz - az) * f;
                if (kind == 3) s.Cyl(x, y0, z, w / 2, h, 8, col); else Panel(s, x, y0, z, w, h, yaw, col, kind, deep);
            }
        }

        /// <summary>The same round a circle (or an oval when sz is not 1), over the angles from..to in degrees.</summary>
        static void Ring(Sculpt s, double cx, double cz, double r, double y0, int n, double w, double h, uint col, int kind = 0, double deep = 0.5, double sz = 1, double from = 0, double to = 360)
        {
            for (int i = 0; i < n; i++)
            {
                double a = Rad(from + (i + 0.5) * (to - from) / n), x = cx + r * Math.Cos(a), z = cz + r * sz * Math.Sin(a);
                if (kind == 3) s.Cyl(x, y0, z, w / 2, h, 8, col);
                else Panel(s, x, y0, z, w, h, -Math.Atan2(sz * Math.Cos(a), -Math.Sin(a)) / Geo.Deg, col, kind, deep);
            }
        }

        // ---- towers of iron

        static void Lattice(Sculpt s, double[] y, double[] half, double[] thick, uint a, uint b, double tip, double[] decks)
        {
            int n = y.Length;
            for (int k = 0; k + 1 < n; k++)
            {
                uint col = k % 2 == 0 ? a : b;
                for (int q = 0; q < 4; q++)
                {
                    double sx = q == 0 || q == 3 ? 1 : -1, sz = q < 2 ? 1 : -1;
                    s.Beam(sx * half[k], y[k], sz * half[k], sx * half[k + 1], y[k + 1], sz * half[k + 1], thick[k], col);
                }
                // criss-cross bracing on each face, in a few storeys per section
                int parts = Math.Max(1, (int)Math.Round((y[k + 1] - y[k]) / Math.Max(18, half[k] * 1.2)));
                for (int j = 0; j < parts; j++)
                {
                    double t0 = (double)j / parts, t1 = (double)(j + 1) / parts;
                    double y0 = y[k] + (y[k + 1] - y[k]) * t0, y1 = y[k] + (y[k + 1] - y[k]) * t1;
                    double h0 = half[k] + (half[k + 1] - half[k]) * t0, h1 = half[k] + (half[k + 1] - half[k]) * t1;
                    double bt = Math.Max(0.8, thick[k] * 0.22);
                    for (int f = 0; f < 4; f++)
                    {
                        s.Push(0, 0, 0, f * 90);
                        s.Beam(-h0, y0, h0, h1, y1, h1, bt, col); s.Beam(h0, y0, h0, -h1, y1, h1, bt, col);
                        s.Beam(-h1, y1, h1, h1, y1, h1, bt, col);
                        s.Pop();
                    }
                }
            }
            for (int i = 0; i + 2 < decks.Length; i += 3) s.Box(0, decks[i], 0, decks[i + 1], decks[i + 2], decks[i + 1], b);
            s.Beam(0, y[n - 1], 0, 0, tip, 0, Math.Max(1.2, thick[n - 2] * 0.5), a);
        }

        /// <summary>One storey of iron lattice between two four-cornered frames (corners given going round).</summary>
        static void Truss(Sculpt s, double[][] lo, double[][] hi, double t, double brace, uint col, bool ring = true)
        {
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                s.Beam(lo[i][0], lo[i][1], lo[i][2], hi[i][0], hi[i][1], hi[i][2], t, col);
                s.Beam(lo[i][0], lo[i][1], lo[i][2], hi[j][0], hi[j][1], hi[j][2], brace, col);
                s.Beam(lo[j][0], lo[j][1], lo[j][2], hi[i][0], hi[i][1], hi[i][2], brace, col);
                if (ring) s.Beam(hi[i][0], hi[i][1], hi[i][2], hi[j][0], hi[j][1], hi[j][2], brace * 1.3, col);
            }
        }

        static double[][] Frame4(double cx, double y, double cz, double half)
        {
            return new[] { new[] { cx - half, y, cz - half }, new[] { cx + half, y, cz - half }, new[] { cx + half, y, cz + half }, new[] { cx - half, y, cz + half } };
        }

        /// <summary>A value read off a list of (at, value) pairs; curved like a tower's flare between them.</summary>
        static double Curve(double[] k, double at)
        {
            int n = k.Length / 2;
            if (at <= k[0]) return k[1];
            for (int i = 0; i + 1 < n; i++)
                if (at <= k[i * 2 + 2])
                {
                    double f = (at - k[i * 2]) / (k[i * 2 + 2] - k[i * 2]);
                    return k[i * 2 + 1] * Math.Pow(k[i * 2 + 3] / k[i * 2 + 1], f);
                }
            return k[n * 2 - 1];
        }

        public static void Eiffel(Sculpt s)
        {
            // 330 m to the tip; 125 m across the feet; platforms at 57.6, 115.7 and 276.1 m
            uint iron = 0x7b5f47, dark = 0x5e4735, stone = 0xb9b2a4;
            double[] outer = { 0, 62.5, 57.6, 33.0, 115.7, 19.0, 160, 13.2, 200, 9.6, 240, 7.0, 276, 5.4 };   // half-width of the tower
            double[] leg = { 0, 25, 57.6, 15.5, 115.7, 10.5 };                                                    // width of each of the four legs
            // the four legs, each a lattice column leaning in along the curve, up to the second platform
            double[] ly = { 0, 14, 28, 42, 54.5, 61.5, 75, 89, 102, 113 };
            for (int q = 0; q < 4; q++)
            {
                double sx = q == 0 || q == 3 ? 1 : -1, sz = q < 2 ? 1 : -1;
                s.Box(sx * 50, -6, sz * 50, 30, 8, 30, stone);
                for (int k = 0; k + 1 < ly.Length; k++)
                {
                    double y0 = ly[k], y1 = ly[k + 1];
                    double w0 = Curve(leg, y0) / 2, w1 = Curve(leg, y1) / 2, c0 = Curve(outer, y0) - w0, c1 = Curve(outer, y1) - w1;
                    Truss(s, Frame4(sx * c0, y0, sz * c0, w0), Frame4(sx * c1, y1, sz * c1, w1), 2.6 - k * 0.12, 0.9, iron);
                }
            }
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90);
                // the great arch under the first platform, with its ribs up to the platform
                int n = 16;
                double px = 0, py = 0;
                for (int i = 0; i <= n; i++)
                {
                    double a = Math.PI * i / n, x = -37 * Math.Cos(a), ay = 12 + 38 * Math.Sin(a), z = Curve(outer, ay) - 1.5;
                    if (i > 0) { s.Beam(px, py, Curve(outer, py) - 1.5, x, ay, z, 2.0, dark); s.Beam(px * 0.93, py - 2.5, Curve(outer, py) - 1.5, x * 0.93, ay - 2.5, z, 1.0, dark); }
                    if (i > 1 && i < n - 1) s.Beam(x, ay, z, x, 54, Curve(outer, 54) - 1.5, 0.7, iron);
                    px = x; py = ay;
                }
                // first platform: a deep gallery all round, with its row of arches
                double h1 = Curve(outer, 57.6);
                s.Box(0, 54.5, h1 - 2, h1 * 2 + 2, 3.2, 5, dark);
                s.Box(0, 57.7, h1 - 0.5, h1 * 2 + 5, 4.6, 3, iron);
                for (int i = -9; i <= 9; i++) s.Box(i * 3.5, 57.9, h1 + 1.05, 2.2, 3.4, 0.2, dark);
                s.Box(0, 62.3, h1 - 0.5, h1 * 2 + 6, 0.8, 4, dark);
                // second platform
                double h2 = Curve(outer, 115.7);
                s.Box(0, 113, h2 - 1, h2 * 2 + 1, 2.2, 3.5, dark);
                s.Box(0, 115.2, h2, h2 * 2 + 4, 3.6, 2.5, iron);
                for (int i = -5; i <= 5; i++) s.Box(i * 3.4, 115.5, h2 + 1.3, 2.0, 2.6, 0.2, dark);
                s.Box(0, 118.8, h2, h2 * 2 + 5, 0.7, 3.2, dark);
                // between the legs from the first to the second platform: light cross-bracing
                for (int k = 0; k < 3; k++)
                {
                    double y0 = 63 + k * 17, y1 = y0 + 17, a0 = Curve(outer, y0) - Curve(leg, y0), a1 = Curve(outer, y1) - Curve(leg, y1), z0 = Curve(outer, y0) - 1, z1 = Curve(outer, y1) - 1;
                    s.Beam(-a0, y0, z0, a1, y1, z1, 0.7, iron); s.Beam(a0, y0, z0, -a1, y1, z1, 0.7, iron);
                    s.Beam(-a1, y1, z1, a1, y1, z1, 0.8, iron);
                }
                s.Pop();
            }
            s.Box(0, 56.5, 0, 46, 1.2, 46, dark);                        // floors of the platforms
            s.Box(0, 114.5, 0, 34, 1.2, 34, dark);
            // the single shaft from the second platform to the top, in storeys that shorten as it narrows
            double y = 119.5;
            int storey = 0;
            while (y < 272)
            {
                double step = Math.Max(7.5, Curve(outer, y) * 1.25), yn = Math.Min(276, y + step);
                if (276 - yn < 5) yn = 276;
                Truss(s, Frame4(0, y, 0, Curve(outer, y)), Frame4(0, yn, 0, Curve(outer, yn)), Math.Max(1.1, 2.0 - storey * 0.07), 0.6, iron);
                y = yn; storey++;
            }
            for (int q = 0; q < 4; q++)                                   // from the platform into the shaft
            {
                double sx = q == 0 || q == 3 ? 1 : -1, sz = q < 2 ? 1 : -1, h = Curve(outer, 119.5);
                s.Beam(sx * h, 113, sz * h, sx * h, 119.5, sz * h, 2.0, iron);
            }
            s.Box(0, 196, 0, 23, 1.6, 23, dark);                          // the intermediate landing
            // the top: third platform, the cabin over it, the lantern and the aerials
            s.Box(0, 273.5, 0, 15, 2.6, 15, dark);
            s.Box(0, 276.1, 0, 17.5, 3.4, 17.5, iron);
            s.Box(0, 279.5, 0, 18.5, 0.6, 18.5, dark);
            s.Box(0, 280.1, 0, 12.5, 4.5, 12.5, iron);
            s.Box(0, 284.6, 0, 14, 0.5, 14, dark);
            s.Taper(0, 285.1, 0, 9, 9, 6, 6, 6, iron);
            s.Lathe(0, 291.1, 0, new[] { 3.6, 0.0, 3.4, 2.5, 2.4, 4.6, 1.2, 5.8, 0.9, 9 }, 8, dark);
            s.Box(0, 300, 0, 4.2, 0.6, 4.2, iron);
            s.Beam(0, 300, 0, 0, 312, 0, 1.6, 0xd8d8d8);
            s.Beam(0, 312, 0, 0, 330, 0, 0.8, 0xc23b2e);
        }

        public static void TokyoTower(Sculpt s)
        {
            Lattice(s, new double[] { 0, 60, 150, 250 }, new double[] { 40, 24, 10, 3.5 }, new double[] { 9, 6, 4 }, 0xe8502a, 0xf2f2ee, 333, new double[] { 145, 30, 12, 247, 12, 8 });
            s.Box(0, 0, 0, 50, 18, 50, 0xd9d4cc);             // FootTown, the building under the tower
        }

        public static void WashingtonMonument(Sculpt s)
        {
            s.Taper(0, 0, 0, 16.8, 16.8, 10.5, 10.5, 152.4, 0xe6e2d8);
            s.Taper(0, 152.4, 0, 10.5, 10.5, 0, 0, 16.8, 0xeeeae0);
        }

        public static void GatewayArch(Sculpt s)
        {
            const double a = 3.0022;
            double px = 0, py = 0;
            for (int i = 0; i <= 44; i++)
            {
                double x = -96 + 192.0 * i / 44;
                double y = 192 * (1 - (Math.Cosh(a * x / 96) - 1) / (Math.Cosh(a) - 1));
                if (i > 0) s.Beam(px, py, 0, x, y, 0, 5.2 + 11.3 * (1 - (py + y) / 384), 0xc9ced3);
                px = x; py = y;
            }
        }

        public static void SpaceNeedle(Sculpt s)
        {
            uint w = 0xf0f0eb, o = 0xe07a2e;
            s.Cyl(0, 0, 0, 4, 150, 10, w);
            for (int k = 0; k < 3; k++)
                foreach (double da in new[] { -8.0, 8.0 })
                {
                    double a = Rad(k * 120 + da), c = Math.Cos(a), z = Math.Sin(a);
                    s.Path(new[] { 20 * c, 0, 20 * z, 9 * c, 60, 9 * z, 5 * c, 113, 5 * z, 9 * c, 150, 9 * z }, 2.6, w);
                }
            s.Lathe(0, 148, 0, new double[] { 5, 0, 14, 3, 21, 7, 21, 9, 19, 11, 17, 14, 8, 17, 3, 18, 0, 18 }, 24, w, 1, 1, new uint[] { w, w, o, 0x4a5563, w, w, w, w });
            s.Lathe(0, 30, 0, new double[] { 0, 0, 10, 0, 10, 2, 0, 2 }, 16, w);
            s.Beam(0, 166, 0, 0, 184, 0, 1, w);
        }

        public static void CNTower(Sculpt s)
        {
            // 553 m: a hollow six-sided concrete shaft with three tapering wings, the seven-storey pod at 335 m, the Sky Pod at 447 m and the antenna
            uint c = Concrete, glass = 0x3f4f63, red = 0xc23b2e;
            double[] reach = { 0, 33, 60, 25.5, 150, 17.5, 250, 12.6, 330, 10.2 }, thick = { 0, 7.5, 330, 4.2 };
            var rings = new System.Collections.Generic.List<double[]>();
            foreach (double y in new[] { 0.0, 30, 60, 100, 150, 200, 250, 290, 330 })
            {
                double r = Curve(reach, y), t = Curve(thick, y) / 2, v = 5.5 + (1 - y / 330) * 3;
                var ring = new double[9 * 3];
                for (int w = 0; w < 3; w++)
                {
                    double a = Rad(w * 120 + 90), ca = Math.Cos(a), sa = Math.Sin(a), b = a + Rad(60);
                    double[][] p = { new[] { r * ca + t * sa, r * sa - t * ca }, new[] { r * ca - t * sa, r * sa + t * ca }, new[] { v * Math.Cos(b), v * Math.Sin(b) } };
                    for (int i = 0; i < 3; i++) { ring[(w * 3 + i) * 3] = p[i][0]; ring[(w * 3 + i) * 3 + 1] = y; ring[(w * 3 + i) * 3 + 2] = p[i][1]; }
                }
                rings.Add(ring);
            }
            Loft(s, rings.ToArray(), c);
            s.Lathe(0, 0, 0, new double[] { 9, 300, 7, 345 }, 6, c);
            // the main pod: white radome ring below, then the glass decks
            s.Lathe(0, 334, 0, new double[] { 7, 0, 17.5, 2, 19.5, 6, 18, 9.5, 20.5, 9.5, 20.5, 13.5, 19, 13.5, 19, 21, 20, 21, 20, 24.5, 16, 27, 11, 30.5, 7, 31.5 }, 28, c, 1, 1,
                new uint[] { White, White, White, c, glass, c, glass, c, red, c, c, c });
            s.Lathe(0, 365.5, 0, new double[] { 6.2, 0, 5.4, 78, 7.6, 80, 7.6, 87, 6.4, 87, 4, 92, 2.6, 92 }, 12, c, 1, 1, new uint[] { c, c, glass, c, c, c });
            s.Lathe(0, 457.5, 0, new double[] { 2.6, 0, 2.2, 25, 1.7, 25, 1.4, 55, 1, 55, 0.7, 80, 0.3, 95.8 }, 8, White, 1, 1, new uint[] { White, White, red, red, White, White });
        }

        public static void Skytree(Sculpt s)
        {
            // 634 m. A three-cornered foot (68 m a side) that turns into a circle by 300 m, in a white steel lattice; two decks, then the mast
            uint c = 0xe4eaee, core = 0x9aa7b3, glass = 0x4f6072, band = 0x7aa7c7;
            Func<double, double, double> radius = (y, a) =>
            {
                double f = Math.Min(1, y / 300.0), circ = 34 - (34 - 13.5) * Math.Pow(Math.Min(1, y / 340.0), 0.8);
                double seg = (a % (2 * Math.PI / 3)) - Math.PI / 3, tri = circ * 1.18 * Math.Cos(Math.PI / 3) / Math.Cos(seg) / 0.82;
                tri = Math.Min(tri, circ * 1.3);
                return circ * f + tri * (1 - f) * 0.9 + circ * 0.1 * (1 - f);
            };
            s.Lathe(0, 0, 0, new double[] { 5, 0, 5, 340 }, 10, core);
            int levels = 17, n = 24;
            var pts = new double[levels + 1][][];
            for (int l = 0; l <= levels; l++)
            {
                double y = 340.0 * l / levels; pts[l] = new double[n][];
                for (int i = 0; i < n; i++) { double a = (i + (l % 2) * 0.5) * 2 * Math.PI / n, r = radius(y, a); pts[l][i] = new[] { r * Math.Sin(a), y, r * Math.Cos(a) }; }
            }
            for (int l = 0; l < levels; l++)
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n, up = l % 2 == 0 ? i : j, up2 = l % 2 == 0 ? (i + n - 1) % n : i;
                    double t = 1.5 - l * 0.035;
                    double[] p = pts[l][i], q = pts[l + 1][up], q2 = pts[l + 1][up2], r = pts[l][j];
                    s.Beam(p[0], p[1], p[2], q[0], q[1], q[2], t, c); s.Beam(p[0], p[1], p[2], q2[0], q2[1], q2[2], t, c);
                    s.Beam(p[0], p[1], p[2], r[0], r[1], r[2], t * 0.7, c);
                }
            // Tembo Deck (350 m) and Tembo Galleria (450 m): each wider at the top than the bottom
            s.Lathe(0, 338, 0, new double[] { 12.5, 0, 17.5, 5, 19, 12, 20, 19, 20.5, 26, 19, 26, 14, 29, 9, 30 }, 28, c, 1, 1, new uint[] { c, glass, band, glass, c, c, c });
            s.Lathe(0, 368, 0, new double[] { 9, 0, 8.2, 70 }, 14, c);
            for (int k = 0; k < 14; k++) s.Lathe(0, 370 + k * 5, 0, new double[] { 8.9 - k * 0.05, 0, 8.9 - k * 0.05, 1.6 }, 14, core);
            s.Lathe(0, 438, 0, new double[] { 8.2, 0, 12, 4, 13.5, 9, 14.5, 15, 13.5, 15, 10, 18, 7, 19 }, 24, c, 1, 1, new uint[] { c, glass, band, c, c, c });
            s.Lathe(0, 457, 0, new double[] { 7, 0, 5.5, 38, 4.2, 38, 3.6, 100, 2.4, 100, 1.8, 150, 0.8, 177 }, 10, c, 1, 1, new uint[] { c, c, White, White, White, White });
            for (int k = 0; k < 7; k++) s.Lathe(0, 462 + k * 5, 0, new double[] { 7 - k * 0.2, 0, 7 - k * 0.2, 1.4 }, 10, core);
        }

        public static void OrientalPearl(Sculpt s)
        {
            uint pink = 0xb5486b, c = 0xd8d5cf;
            for (int k = 0; k < 3; k++)
            {
                double a = Rad(k * 120), x = Math.Cos(a), z = Math.Sin(a);
                s.Cyl(7 * x, 0, 7 * z, 4.5, 300, 10, c);
                s.Beam(-34 * x, 0, -34 * z, -6 * x, 92, -6 * z, 6, c);
                for (int j = 0; j < 5; j++) s.Ball(7 * x, 135 + j * 22, 7 * z, 6, pink, 1, false, 10);
            }
            s.Ball(0, 93, 0, 25, pink);
            s.Ball(0, 267, 0, 22.5, pink);
            s.Cyl(0, 285, 0, 4, 50, 8, c);
            s.Ball(0, 342, 0, 8, pink);
            s.Lathe(0, 350, 0, new double[] { 2.5, 0, 1.5, 70, 0.5, 118 }, 6, White);
        }

        public static void Atomium(Sculpt s)
        {
            uint st = 0xc7ccd1, tube = 0xa9afb5;
            var p = new double[9][];
            p[0] = new double[] { 0, 51, 0 }; p[1] = new double[] { 0, 12, 0 }; p[2] = new double[] { 0, 93, 0 };
            for (int k = 0; k < 3; k++)
            {
                double a = Rad(k * 120), b = Rad(k * 120 + 60);
                p[3 + k] = new[] { 27 * Math.Cos(a), 37.0, 27 * Math.Sin(a) };
                p[6 + k] = new[] { 27 * Math.Cos(b), 65.0, 27 * Math.Sin(b) };
            }
            Action<int, int> link = (i, j) => s.Beam(p[i][0], p[i][1], p[i][2], p[j][0], p[j][1], p[j][2], 3, tube);
            for (int i = 1; i < 9; i++) link(0, i);
            for (int k = 0; k < 3; k++) { link(1, 3 + k); link(2, 6 + k); link(3 + k, 6 + k); link(3 + (k + 1) % 3, 6 + k); }
            for (int k = 0; k < 3; k++) s.Beam(p[3 + k][0], 37, p[3 + k][2], p[3 + k][0] * 1.35, 0, p[3 + k][2] * 1.35, 2.5, tube);
            foreach (double[] q in p) s.Ball(q[0], q[1], q[2], 9, st);
        }

        // ---- statues

        public static void Liberty(Sculpt s)
        {
            // 93 m from the ground to the torch: Fort Wood's star, a 27 m granite pedestal, a 46 m copper figure
            uint stone = 0xb8ad98, granite = 0xc9bfa8, g = 0x74b9a4, gd = Sculpt.Shade(g, 0.88);
            var star = new double[44];
            for (int i = 0; i < 22; i++) { double a = Rad(i * 360.0 / 22), r = i % 2 == 0 ? 52 : 36; star[i * 2] = r * Math.Cos(a); star[i * 2 + 1] = r * Math.Sin(a); }
            s.Poly(star, -8, 15, stone);                      // Fort Wood, the star-shaped base
            s.Box(0, 7, 0, 42, 2, 42, Sculpt.Shade(stone, 0.95));
            s.Box(0, 9, 0, 28, 7, 28, stone);                  // the stepped foot of the pedestal
            s.Taper(0, 16, 0, 24, 24, 20, 20, 3, granite);
            s.Taper(0, 19, 0, 19, 19, 16, 16, 4, granite);
            s.Box(0, 23, 0, 16, 13, 16, granite);
            for (int f = 0; f < 4; f++)                        // the loggia: a row of openings on each face
            {
                s.Push(0, 0, 0, f * 90);
                for (int i = -2; i <= 2; i++) s.Box(i * 2.4, 26.5, 8.0, 1.3, 6.5, 0.3, Dark);
                foreach (double sx in new[] { -1.0, 1.0 }) s.Box(sx * 7.2, 23, 8.0, 1.6, 13, 0.5, Sculpt.Shade(granite, 1.04));
                for (int i = -4; i <= 4; i++) s.Box(i * 1.9, 19.5, 9.35, 1.1, 1.1, 0.3, Sculpt.Shade(granite, 0.8));
                s.Pop();
            }
            s.Box(0, 36, 0, 17, 1.5, 17, stone);               // the balcony
            s.Taper(0, 37.5, 0, 14, 14, 12, 12, 7, granite);
            s.Box(0, 44.5, 0, 14, 1, 14, stone);
            s.Box(0, 45.5, 0, 11, 1.5, 11, gd);                // the copper foot she stands on
            // the robe: wide at the hem, gathered at the waist, the mantle over the left shoulder
            const int n = 14;
            double[] ys = { 47, 52, 58, 64, 69, 73, 76.5, 79 }, rx = { 5.4, 5.0, 4.5, 4.0, 3.8, 3.9, 3.4, 1.5 }, rz = { 4.6, 4.2, 3.6, 3.1, 2.9, 2.8, 2.4, 1.3 };
            var rings = new double[ys.Length][];
            for (int k = 0; k < ys.Length; k++) rings[k] = Oval('y', 0.25 * Math.Max(0, k - 4), ys[k], 0, rx[k], rz[k], n);
            Loft(s, rings, g);
            for (int i = 0; i < 6; i++) s.Beam(-4.4 + i * 1.7, 47.2, 4.0 - Math.Abs(i - 2.5) * 0.3, -2.6 + i * 0.9, 70 - i * 0.6, 2.9, 0.5, gd);   // long folds down the front
            s.Beam(-3.6, 77, 1.2, 3.4, 60, 3.4, 1.5, gd);      // the fold of the mantle across the body
            // neck, head, the hair bun, the crown with its seven rays
            s.Cyl(0.4, 79, 0, 1.0, 1.6, 8, g);
            s.Ball(0.4, 82.4, 0.2, 2.1, g, 1.25, false, 10);
            s.Ball(0.4, 82.2, -1.9, 1.3, gd, 1, false, 8);
            s.Lathe(0.4, 83.6, 0.2, new[] { 2.35, 0.0, 2.5, 1.0, 2.2, 1.1 }, 12, gd);
            for (int i = 0; i < 7; i++) { double a = Rad(-66 + i * 22); s.Beam(0.4 + Math.Sin(a) * 2.2, 84.3, 0.5 + Math.Cos(a) * 1.4, 0.4 + Math.Sin(a) * 5.0, 86.6 + Math.Cos(a) * 1.6, 0.9 + Math.Cos(a) * 2.6, 0.45, g); }
            // right arm raised high with the torch
            Loft(s, new[] { Oval('y', 2.9, 76.5, -0.2, 1.5, 1.4, 8), Oval('y', 4.6, 81.5, -0.4, 1.25, 1.2, 8), Oval('y', 5.6, 87, -0.5, 1.0, 1.0, 8), Oval('y', 6.0, 90.3, -0.5, 0.8, 0.8, 8) }, g);
            s.Ball(6.0, 90.8, -0.5, 1.0, g, 1, false, 8);      // the hand
            s.Cyl(6.0, 90.6, -0.5, 0.5, 2.6, 8, gd);            // the handle
            s.Lathe(6.0, 93.0, -0.5, new[] { 0.6, 0.0, 1.9, 0.5, 1.9, 0.9, 0.9, 1.0 }, 10, g);   // the balcony round the flame
            s.Lathe(6.0, 93.9, -0.5, new[] { 0.9, 0.0, 1.25, 1.0, 0.9, 2.2, 0.35, 3.3, 0, 4.0 }, 8, Gold);
            // left arm bent, holding the tablet against her side
            Loft(s, new[] { Oval('y', -3.0, 76.3, 0.3, 1.5, 1.4, 8), Oval('y', -4.3, 72, 1.0, 1.3, 1.2, 8), Oval('y', -4.2, 68.6, 2.6, 1.0, 1.0, 8) }, g);
            s.Push(-4.9, 66.2, 3.0, 20);
            s.Box(0, 0, 0, 0.8, 7.2, 4.1, gd);
            s.Pop();
        }

        public static void Redeemer(Sculpt s)
        {
            // 30 m figure on an 8 m pedestal, 28 m from fingertip to fingertip; soapstone, arms held straight out
            uint c = 0xe6e3d6, robe = Sculpt.Shade(c, 0.94), fold = Sculpt.Shade(c, 0.86);
            s.Stand(0, 0, 3);
            s.Box(0, -12, 0, 9, 12, 9, 0x55524e);
            s.Taper(0, 0, 0, 7.4, 7.4, 6.4, 6.4, 8, 0x3f3e40);
            const int n = 14;
            // the robe, falling straight to the feet; chest and shoulders; the head bowed a little
            double[] ys = { 8, 12, 18, 24, 28.5, 31, 32.6, 33.6 }, rx = { 2.5, 2.35, 2.2, 2.25, 2.6, 3.0, 2.2, 0.9 }, rz = { 2.0, 1.85, 1.7, 1.7, 1.8, 1.75, 1.4, 0.85 };
            var rings = new double[ys.Length][];
            for (int k = 0; k < ys.Length; k++) rings[k] = Oval('y', 0, ys[k], 0, rx[k], rz[k], n);
            Loft(s, rings, c);
            for (int i = -2; i <= 2; i++) s.Beam(i * 0.85, 8.3, 1.75 - Math.Abs(i) * 0.12, i * 0.7, 26, 1.5, 0.3, fold);   // folds of the robe
            s.Beam(-2.2, 31.5, 1.3, 1.6, 22, 1.75, 0.9, robe);    // the mantle crossing the chest
            s.Cyl(0, 33.2, 0.1, 0.8, 1.2, 8, c);
            s.Ball(0, 35.9, 0.35, 1.5, c, 1.25, false, 10);
            s.Ball(0, 35.6, -0.5, 1.6, robe, 1.15, false, 8);     // hair falling to the shoulders
            s.Box(0, 33.4, -0.9, 2.6, 2.4, 1.0, robe);
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                // the arm inside its wide sleeve, tapering to the wrist; the sleeve hangs open below the forearm
                Loft(s, new[] { Oval('x', sx * 2.2, 31.3, 0, 1.45, 1.25, 8), Oval('x', sx * 6.5, 31.5, 0, 1.3, 1.15, 8), Oval('x', sx * 10.6, 31.6, 0, 1.15, 1.0, 8), Oval('x', sx * 11.4, 31.6, 0, 0.55, 0.5, 8) }, c);
                s.Quad(new[] { sx * 3.0, 30.2, 0.3 }, new[] { sx * 10.9, 30.6, 0.3 }, new[] { sx * 9.6, 26.4, 0.2 }, new[] { sx * 4.2, 27.6, 0.25 }, robe);
                s.Quad(new[] { sx * 3.0, 30.2, -0.5 }, new[] { sx * 10.9, 30.6, -0.5 }, new[] { sx * 9.6, 26.4, -0.4 }, new[] { sx * 4.2, 27.6, -0.45 }, fold);
                s.Beam(sx * 11.2, 31.6, 0, sx * 12.6, 31.55, 0, 0.75, c);          // wrist
                s.Box(sx * 13.3, 31.3, 0.1, 1.6, 0.45, 1.1, c);                    // the open hand
            }
        }

        public static void Moai(Sculpt s)
        {
            // Ahu Tongariki: fifteen moai in a row on their stone platform, backs to the sea, the tallest about 8.7 m; one wears a red topknot
            uint tuff = 0x8f8477, dk = 0x5e564c, ahu = 0x6f675e;
            s.Taper(0, -3, -1, 100, 12, 98, 7, 5, ahu);
            s.Taper(0, -3, 9, 100, 14, 100, 12, 3.6, Sculpt.Shade(ahu, 0.9));        // the sloping ramp of sea stones in front
            double[] hs = { 5.6, 6.4, 6.0, 7.0, 6.2, 8.7, 6.6, 7.4, 6.0, 6.8, 5.4, 6.3, 7.0, 5.8, 6.1 };
            for (int i = 0; i < 15; i++)
            {
                double x = -44.8 + i * 6.4, h = hs[i], u = h / 7.0;
                uint c = Sculpt.Shade(tuff, 0.94 + (i * 5 % 4) * 0.035);
                s.Push(x, 2, -1, 0, 0);
                s.Box(0, 0, 0, 3.1 * u, 0.35, 2.3 * u, Sculpt.Shade(ahu, 1.1));       // its pedestal stone
                // torso: wide at the hips, arms held to the sides, hands meeting on the belly
                Loft(s, new[] { Oval('y', 0, 0.35, 0, 1.25 * u, 0.8 * u, 10), Oval('y', 0, h * 0.3, 0.05, 1.3 * u, 0.85 * u, 10), Oval('y', 0, h * 0.52, 0, 1.2 * u, 0.7 * u, 10), Oval('y', 0, h * 0.58, 0, 0.75 * u, 0.55 * u, 10) }, c);
                foreach (double sx in new[] { -1.0, 1.0 }) { s.Beam(sx * 1.25 * u, h * 0.5, 0.1, sx * 1.3 * u, h * 0.2, 0.25, 0.4 * u, Sculpt.Shade(c, 0.93)); s.Beam(sx * 1.3 * u, h * 0.2, 0.25, sx * 0.25 * u, h * 0.13, 0.85 * u, 0.3 * u, Sculpt.Shade(c, 0.93)); }
                // the long head: heavy brow, deep eye sockets, long nose, thin lips, jutting chin, long ears
                Loft(s, new[] { Oval('y', 0, h * 0.57, 0.1, 0.72 * u, 0.62 * u, 10), Oval('y', 0, h * 0.63, 0.22, 0.86 * u, 0.74 * u, 10), Oval('y', 0, h * 0.8, 0.1, 0.84 * u, 0.7 * u, 10), Oval('y', 0, h * 0.99, -0.05, 0.8 * u, 0.62 * u, 10), Oval('y', 0, h, -0.05, 0.6 * u, 0.5 * u, 10) }, c);
                s.Box(0, h * 0.88, 0.62 * u, 1.5 * u, h * 0.045, 0.3 * u, c);
                s.Box(0, h * 0.835, 0.6 * u, 1.3 * u, h * 0.045, 0.14 * u, dk);
                s.Taper(0, h * 0.71, 0.82 * u, 0.5 * u, 0.5 * u, 0.3 * u, 0.2 * u, h * 0.17, Sculpt.Shade(c, 1.04));
                s.Box(0, h * 0.665, 0.74 * u, 0.7 * u, h * 0.022, 0.2 * u, dk);
                foreach (double sx in new[] { -1.0, 1.0 }) s.Box(sx * 0.86 * u, h * 0.66, 0, 0.14 * u, h * 0.2, 0.36 * u, Sculpt.Shade(c, 0.9));
                if (i == 1) s.Lathe(0, h, -0.05, new double[] { 0.95 * u, 0, 1.0 * u, 1.3, 0.5 * u, 1.3, 0.45 * u, 1.7, 0, 1.7 }, 12, 0x9a4a32);
                s.Pop();
            }
        }

        // ---- ancient stone

        public static void Pyramids(Sculpt s)
        {
            uint sand = 0xd9c38a;
            // each pyramid rests on its own patch of the plateau; the courses of stone show as faint bands
            Action<double, double, double, double, int> pyr = (x, z, side, h, bands) =>
            {
                s.Stand(x, z, side * 0.5);
                s.Box(x, -40, z, side, 40, side, Sculpt.Shade(sand, 0.9));
                for (int k = 0; k < bands; k++)
                {
                    double t0 = (double)k / bands, t1 = (double)(k + 1) / bands;
                    s.Taper(x, h * t0, z, side * (1 - t0), side * (1 - t0), side * (1 - t1), side * (1 - t1), h / bands, Sculpt.Shade(sand, k % 2 == 0 ? 1.0 : 0.955));
                }
            };
            pyr(0, 0, 230.3, 138.5, 14);                      // Khufu
            pyr(-331, -350, 215.3, 136.4, 14);                // Khafre, with what is left of its smooth casing
            s.Taper(-331, 110, -350, 43.5, 43.5, 0, 0, 27.6, 0xeee4c8);
            pyr(-573, -743, 103.4, 61, 8);                    // Menkaure
            for (int i = 0; i < 3; i++) pyr(172, 12 - i * 55, 46, 29, 5);        // the queens' pyramids east of Khufu
            for (int i = 0; i < 3; i++) pyr(-612 + i * 42, -833, i == 0 ? 44 : 31, i == 0 ? 28 : 12, 4);
            // the Great Sphinx: 73 m long, 20 m high, looking east
            uint rock = Sculpt.Shade(sand, 0.93);
            s.Stand(325, -435, 30);
            s.Box(325, -20, -435, 78, 20, 24, Sculpt.Shade(sand, 0.85));
            s.Taper(318, 0, -435, 48, 15, 44, 10, 11, rock);                     // body
            s.Taper(298, 0, -435, 16, 19, 12, 12, 12.5, rock);                   // haunches
            s.Taper(343, 0, -435, 11, 14, 7, 9, 13, rock);                       // chest
            s.Taper(344, 11, -435, 7, 12, 6, 8, 7, Sculpt.Shade(sand, 0.88));    // the headcloth
            s.Box(346, 13, -435, 5.5, 7, 5.5, sand);                             // the face
            foreach (double sz in new[] { -5.0, 5.0 }) s.Box(356, 0, -435 + sz, 17, 3.2, 4, rock);   // paws
        }

        public static void Chichen(Sculpt s)
        {
            uint lm = 0xdbd1b2;
            for (int k = 0; k < 9; k++) s.Box(0, k * 2.67, 0, 55.3 - k * 4.4, 2.67, 55.3 - k * 4.4, k % 2 == 0 ? lm : Sculpt.Shade(lm, 0.93));
            s.Box(0, 24, 0, 13.4, 5, 13.4, lm);
            s.Box(0, 29, 0, 14.4, 1, 14.4, Sculpt.Shade(lm, 0.9));
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90);
                s.Quad(new[] { -4.4, 0, 30.5 }, new[] { 4.4, 0, 30.5 }, new[] { 4.4, 24.2, 8.5 }, new[] { -4.4, 24.2, 8.5 }, Sculpt.Shade(lm, 1.06));
                s.Tri(-4.4, 0, 30.5, -4.4, 24.2, 8.5, -4.4, 0, 8.5, Sculpt.Shade(lm, 0.85));
                s.Tri(4.4, 0, 30.5, 4.4, 24.2, 8.5, 4.4, 0, 8.5, Sculpt.Shade(lm, 0.85));
                s.Box(0, 24, 6.75, 3, 3.6, 0.3, Dark);
                s.Pop();
            }
        }

        public static void Stonehenge(Sculpt s)
        {
            uint st = 0x9e9e94;
            for (int i = 0; i < 30; i++)
            {
                double a = i * 12, x = 16.5 * Math.Cos(Rad(a)), z = 16.5 * Math.Sin(Rad(a));
                if (i == 7 || i == 19 || i == 23) { s.Box(x * 1.1, 0, z * 1.1, 4.1, 1.0, 2.1, st, -(a + 90) + 30); continue; }   // fallen
                s.Box(x, 0, z, 2.1, 4.1 + i % 3 * 0.15, 1.1, i % 4 == 0 ? st : Sculpt.Shade(st, 0.94), -(a + 90));
                if (i < 6 || i > 25 || i == 12 || i == 13)
                {
                    double b = a + 6;
                    s.Box(16.5 * Math.Cos(Rad(b)), 4.2, 16.5 * Math.Sin(Rad(b)), 3.5, 0.8, 1.1, Sculpt.Shade(st, 0.9), -(b + 90));
                }
            }
            for (int t = 0; t < 5; t++)                       // the five great trilithons, open to the north-east
            {
                double a = 225 + (t - 2) * 52, h = t == 2 ? 7.3 : 6.2;
                s.Push(7.5 * Math.Cos(Rad(a)), 0, 7.5 * Math.Sin(Rad(a)), -(a + 90));
                s.Box(-1.7, 0, 0, 2.3, h, 1.2, st); s.Box(1.7, 0, 0, 2.3, h, 1.2, st);
                if (t != 3) s.Box(0, h, 0, 6, 1, 1.3, Sculpt.Shade(st, 0.92));
                s.Pop();
            }
            for (int i = 0; i < 20; i++) { double a = i * 18 + 5; s.Box(11.5 * Math.Cos(Rad(a)), 0, 11.5 * Math.Sin(Rad(a)), 0.8, 1.6, 0.6, 0x7f8894, -(a + 90)); }
        }

        public static void Parthenon(Sculpt s)
        {
            uint m = Marble;
            s.Box(0, -3, 0, 72, 3.6, 33.5, m); s.Box(0, 0.6, 0, 70.8, 0.6, 32.2, m); s.Box(0, 1.2, 0, 69.5, 0.6, 30.9, m);
            for (int i = 0; i < 17; i++) { double x = -33.8 + i * 4.225; s.Cyl(x, 1.8, -14.5, 0.95, 10.4, 8, m); s.Cyl(x, 1.8, 14.5, 0.95, 10.4, 8, m); }
            for (int j = 1; j < 7; j++) { double z = -14.5 + j * 4.143; s.Cyl(-33.8, 1.8, z, 0.95, 10.4, 8, m); s.Cyl(33.8, 1.8, z, 0.95, 10.4, 8, m); }
            s.Box(0, 1.8, 0, 48, 10.4, 19, Sculpt.Shade(m, 0.86));
            s.Box(0, 12.2, 0, 69.5, 2.6, 30.9, m);
            s.Gable(0, 14.8, 0, 69.5, 30.9, 4.2, Sculpt.Shade(m, 0.95));
        }

        public static void Colosseum(Sculpt s)
        {
            // an oval 189 x 156 m. The full 48.5 m outer wall survives only on the north side (+z); elsewhere the lower inner ring shows.
            uint tr = 0xd9c39a, in2 = 0xc7af86, hole = 0x4f4335, brick = 0xb98f66, sand = 0xcdb98f;
            const double k = 156.0 / 189.0;
            // seating bowl, inner ring wall and arena
            s.Lathe(0, -6, 0, new double[] { 78, 0, 78, 37, 74, 37, 74, 33, 60, 24, 58, 20, 46, 12, 43.5, 10, 43.5, 6 }, 64, in2, 1, k, new uint[] { in2, tr, brick, brick, in2, brick, in2, in2 });
            s.Lathe(0, 0, 0, new double[] { 0, 0.5, 43.5, 0.5 }, 48, sand, 1, k);
            for (int i = -4; i <= 4; i++) s.Box(0, 0.4, i * 6.5 * k, Math.Sqrt(1 - i * i / 30.0) * 78, 0.5, 2.2, hole);       // the open cellars under the arena
            s.Box(0, 0.4, 0, 3, 0.6, 62 * k, hole);
            Ring(s, 0, 0, 78.1, 1.5, 80, 3.6, 8.6, hole, 1, 0.4, k);
            Ring(s, 0, 0, 78.1, 13.5, 80, 3.6, 8.6, hole, 1, 0.4, k);
            // the outer wall: bays 0..79 round the oval; it stands from about 20 to 200 degrees
            const double a0 = 22, a1 = 203;
            Func<double, double, double[]> at = (r, deg) => new[] { r * Math.Cos(Rad(deg)), r * k * Math.Sin(Rad(deg)) };
            int n = 40;
            for (int i = 0; i < n; i++)
            {
                double d0 = a0 + (a1 - a0) * i / n, d1 = a0 + (a1 - a0) * (i + 1) / n;
                double[] o0 = at(94.5, d0), o1 = at(94.5, d1), i0 = at(88.5, d0), i1 = at(88.5, d1);
                double[] hs = { -6, 10.5, 22.4, 34, 48.5 };
                for (int t = 0; t < 4; t++)
                {
                    uint c = Sculpt.Shade(tr, t % 2 == 0 ? 1 : 0.96);
                    s.Quad(new[] { o0[0], hs[t], o0[1] }, new[] { o1[0], hs[t], o1[1] }, new[] { o1[0], hs[t + 1], o1[1] }, new[] { o0[0], hs[t + 1], o0[1] }, c);
                    s.Quad(new[] { i0[0], hs[t], i0[1] }, new[] { i1[0], hs[t], i1[1] }, new[] { i1[0], hs[t + 1], i1[1] }, new[] { i0[0], hs[t + 1], i0[1] }, Sculpt.Shade(in2, 0.95));
                }
                s.Quad(new[] { o0[0], 48.5, o0[1] }, new[] { o1[0], 48.5, o1[1] }, new[] { i1[0], 48.5, i1[1] }, new[] { i0[0], 48.5, i0[1] }, tr);
                // the floor between the outer wall and the inner ring
                double[] m0 = at(78, d0), m1 = at(78, d1);
                s.Quad(new[] { i0[0], 31, i0[1] }, new[] { i1[0], 31, i1[1] }, new[] { m1[0], 31, m1[1] }, new[] { m0[0], 31, m0[1] }, brick);
            }
            foreach (double d in new[] { a0, a1 })
            {
                double[] o = at(94.5, d), i = at(88.5, d), far = at(94.5, d + (d == a0 ? -9 : 9));
                s.Quad(new[] { o[0], -6, o[1] }, new[] { i[0], -6, i[1] }, new[] { i[0], 48.5, i[1] }, new[] { o[0], 48.5, o[1] }, brick);
                // the brick buttress that props each broken end
                s.Tri(o[0], -6, o[1], o[0], 46, o[1], far[0], -6, far[1], brick);
                double[] fi = at(88.5, d + (d == a0 ? -9 : 9));
                s.Tri(i[0], -6, i[1], i[0], 46, i[1], fi[0], -6, fi[1], brick);
                s.Quad(new[] { o[0], 46, o[1] }, new[] { i[0], 46, i[1] }, new[] { fi[0], -6, fi[1] }, new[] { far[0], -6, far[1] }, Sculpt.Shade(brick, 1.06));
            }
            foreach (double y in new[] { 9.6, 21.5, 33.1, 47.4 }) Ring(s, 0, 0, 94.8, y, 60, 9.4, 1.1, Sculpt.Shade(tr, 1.05), 0, 0.9, k, a0, a1);   // cornices
            for (int t = 0; t < 3; t++)
            {
                Ring(s, 0, 0, 94.6, 1 + t * 11.8, n, 4.2, 7.6, hole, 1, 0.5, k, a0, a1);
                Ring(s, 0, 0, 94.9, 0.5 + t * 11.8, n, 0.9, 9.2, tr, 3, 0.5, k, a0 - (a1 - a0) / n / 2, a1 - (a1 - a0) / n / 2);     // half columns between the arches
            }
            Ring(s, 0, 0, 94.6, 38, n / 2, 1.6, 1.9, hole, 0, 0.5, k, a0, a1);
            Ring(s, 0, 0, 94.8, 34.2, n, 0.8, 13, Sculpt.Shade(tr, 1.03), 0, 0.5, k, a0 - (a1 - a0) / n / 2, a1 - (a1 - a0) / n / 2);
        }

        public static void Angkor(Sculpt s)
        {
            // the temple proper: three rising rings of roofed galleries, 215 x 187 m at the outside, and the five lotus-bud towers (65 m); west is -x
            uint sd = 0x9c9279, dk = 0x4d473a, rf = 0x857c66, lt = 0xaaa087;
            // a gallery: a long narrow hall with a curved-looking stone roof, drawn along x or along z
            Action<double, double, double, double, double, double> hall = (x0, z0, x1, z1, y, w) =>
            {
                bool alongX = Math.Abs(x1 - x0) > Math.Abs(z1 - z0);
                double len = alongX ? Math.Abs(x1 - x0) : Math.Abs(z1 - z0), cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
                s.Box(cx, y, cz, alongX ? len : w, 4.2, alongX ? w : len, sd);
                s.Push(cx, y + 4.2, cz, alongX ? 0 : 90);
                s.Taper(0, 0, 0, len, w + 1, len, w * 0.45, 1.8, rf); s.Taper(0, 1.8, 0, len, w * 0.45, len, 0.3, 1.2, Sculpt.Shade(rf, 0.92));
                s.Pop();
                int n = (int)(len / 4.5);
                if (alongX) { Row(s, x0, cz + w / 2 + 0.05, x1, cz + w / 2 + 0.05, y + 0.8, n, 1.4, 2.6, dk, 0, 0.2); Row(s, x0, cz - w / 2 - 0.05, x1, cz - w / 2 - 0.05, y + 0.8, n, 1.4, 2.6, dk, 0, 0.2); }
                else { Row(s, cx + w / 2 + 0.05, z0, cx + w / 2 + 0.05, z1, y + 0.8, n, 1.4, 2.6, dk, 0, 0.2); Row(s, cx - w / 2 - 0.05, z0, cx - w / 2 - 0.05, z1, y + 0.8, n, 1.4, 2.6, dk, 0, 0.2); }
            };
            Action<double, double, double, double> ring = (hx, hz, y, w) =>
            {
                hall(-hx, hz, hx, hz, y, w); hall(-hx, -hz, hx, -hz, y, w); hall(hx, -hz, hx, hz, y, w); hall(-hx, -hz, -hx, hz, y, w);
            };
            // a tower shaped like a lotus bud: stepped rings drawing in to a point
            Action<double, double, double, double, double> tower = (x, z, y0, h, w) =>
            {
                s.Box(x, y0, z, w * 0.9, h * 0.22, w * 0.9, sd);
                foreach (int f in new[] { 0, 1, 2, 3 }) { s.Push(x, y0, z, f * 90); Panel(s, 0, 0, w * 0.46, w * 0.3, h * 0.2, 0, dk, 2, 0.4); s.Gable(0, h * 0.2, w * 0.56, w * 0.5, w * 0.3, h * 0.07, lt, 90); s.Pop(); }
                var ry = new System.Collections.Generic.List<double>();
                const int tiers = 9;
                for (int i = 0; i < tiers; i++)
                {
                    double t = (double)i / tiers, r = w / 2 * (1 - Math.Pow(t, 1.7)) * (1 - 0.15 * t), yy = h * 0.22 + (h * 0.72) * t, step = h * 0.72 / tiers;
                    ry.AddRange(new[] { r * 1.08, yy, r * 1.08, yy + step * 0.25, r * 0.94, yy + step * 0.35, r * 0.9, yy + step });
                }
                ry.AddRange(new[] { w * 0.07, h * 0.94, w * 0.1, h * 0.96, 0, h });
                s.Lathe(x, y0, z, ry.ToArray(), 16, lt);
            };
            s.Box(0, -6, 0, 225, 7.5, 197, sd);                                  // the outer platform
            ring(107.5, 93.5, 1.5, 6);                                            // first gallery, with the long bas-reliefs
            s.Box(0, 1.5, 0, 150, 6, 130, Sculpt.Shade(sd, 0.95));
            ring(57.5, 50, 7.5, 5); 
            foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 }) tower(sx * 57.5, sz * 50, 7.5, 17, 9);
            s.Taper(0, 7.5, 0, 82, 80, 62, 60, 13, sd);                           // the steep-sided top platform
            for (int f = 0; f < 4; f++) { s.Push(0, 0, 0, f * 90); foreach (double x in new[] { -22.0, 0, 22 }) s.Taper(x, 7.5, 35, 7, 12, 7, 1, 13, lt); s.Pop(); }
            ring(29, 28, 20.5, 4.5);
            hall(-29, 0, 29, 0, 20.5, 4.5); hall(0, -28, 0, 28, 20.5, 4.5);       // the cross of galleries to the middle tower
            tower(0, 0, 20.5, 44.5, 20);
            foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 }) tower(sx * 29, sz * 28, 20.5, 26, 12);
            // the west entrance: the cross-shaped cloister, the terrace and the causeway; the gate towers on each side
            hall(-107.5, 0, -57.5, 0, 1.5, 6); hall(-83, -22, -83, 22, 1.5, 6);
            foreach (double x in new[] { 0.0, 1 }) foreach (double sz in new[] { -1.0, 0, 1 }) if (x == 0) tower(-107.5, sz * 30, 1.5, sz == 0 ? 16 : 12, 9);
            foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 }) tower(sx * 107.5, sz * 93.5, 1.5, 9, 8);
            s.Box(-125, -6, 0, 26, 7, 40, sd);
            s.Box(-190, -3, 0, 105, 4, 9.5, sd);
            foreach (double sz in new[] { -1.0, 1.0 }) { s.Box(-190, 1, sz * 4.6, 105, 0.8, 0.5, lt); s.Box(-165, -3, sz * 40, 18, 9, 12, sd); s.Taper(-165, 6, sz * 40, 19, 13, 19, 1, 3, rf); }
        }

        // ---- churches, mosques and temples

        public static void Taj(Sculpt s)
        {
            // the tomb on its 95 m marble plinth, with the four minarets; 73 m to the finial
            uint m = Marble, dk = 0x8f8778, in2 = 0xb9b2a2, sand = 0xb5694d;
            s.Box(0, -10, 0, 95, 17, 95, m);
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90);
                Row(s, -45, 47.6, 45, 47.6, 1.2, 15, 3.4, 4.6, in2, 2, 0.3);
                s.Pop();
            }
            // the body: a square with its corners cut off
            const double a = 28.5, c = 9;
            s.Poly(new[] { a - c, a, a, a - c, a, -(a - c), a - c, -a, -(a - c), -a, -a, -(a - c), -a, a - c, -(a - c), a }, 7, 23, m);
            s.Poly(new[] { a - c, a, a, a - c, a, -(a - c), a - c, -a, -(a - c), -a, -a, -(a - c), -a, a - c, -(a - c), a }, 30, 0.9, Sculpt.Shade(m, 0.9));
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90);
                // the great gateway arch (pishtaq), standing proud of the wall and above it
                Plate(s, Pointed(0, 7, 21, 29, 0, 13, 23.5), 25.5, 30, m, false, Sculpt.Shade(m, 1.02));
                s.Box(0, 7, 26.2, 14, 24, 0.6, in2);
                Panel(s, 0, 7, 26.7, 5, 9, 0, dk, 2, 0.4); Panel(s, 0, 18.5, 26.7, 5, 7, 0, dk, 2, 0.4);
                s.Box(0, 36, 27.7, 21.6, 0.8, 5, Sculpt.Shade(m, 0.93));
                foreach (double sx in new[] { -1.0, 1.0 })
                {
                    s.Lathe(sx * 10.6, 7, 29.6, new double[] { 0.75, 0, 0.75, 31, 1.1, 31.5, 0.5, 33, 0, 35 }, 8, m);
                    Panel(s, sx * 15.2, 9, a + 0.1, 5.2, 8.6, 0, in2, 2, 0.5); Panel(s, sx * 15.2, 20, a + 0.1, 5.2, 8.6, 0, in2, 2, 0.5);
                    Panel(s, sx * 15.2, 9, a + 0.3, 2.2, 4, 0, dk, 2, 0.3); Panel(s, sx * 15.2, 20, a + 0.3, 2.2, 4, 0, dk, 2, 0.3);
                }
                // the cut corner
                s.Push(0, 0, 0, 45);
                double d = (2 * a - c) / Math.Sqrt(2);
                Panel(s, 0, 9, d + 0.1, 6.4, 8.6, 0, in2, 2, 0.5); Panel(s, 0, 20, d + 0.1, 6.4, 8.6, 0, in2, 2, 0.5);
                Panel(s, 0, 9, d + 0.3, 2.6, 4, 0, dk, 2, 0.3); Panel(s, 0, 20, d + 0.3, 2.6, 4, 0, dk, 2, 0.3);
                foreach (double sx in new[] { -1.0, 1.0 }) s.Lathe(sx * c / Math.Sqrt(2), 7, d, new double[] { 0.6, 0, 0.6, 24.5, 0.9, 25, 0.4, 26.5, 0, 28 }, 8, m);
                s.Pop();
                s.Pop();
            }
            // drum and the great onion dome
            s.Lathe(0, 30.9, 0, new double[] { 10.6, 0, 10.6, 9.5, 11, 9.8, 11, 10.6 }, 28, m, 1, 1, new uint[] { m, Sculpt.Shade(m, 0.9), Sculpt.Shade(m, 0.9) });
            s.Lathe(0, 41.5, 0, new double[] { 10.8, 0, 12.1, 3, 12.9, 7, 12.9, 10.5, 12, 14.5, 10.2, 18, 7.6, 21, 4.6, 23.3, 2, 24.6, 2.6, 25.2, 1.2, 26.4 }, 28, m, 1, 1,
                new uint[] { m, m, m, m, m, m, m, m, Sculpt.Shade(m, 0.9), Sculpt.Shade(m, 0.9) });
            s.Lathe(0, 67.9, 0, new double[] { 0.5, 0, 0.9, 0.8, 0.3, 1.6, 0.7, 2.4, 0.25, 3.2, 0.45, 3.8, 0.1, 4.4, 0.1, 5.1 }, 8, Gold);
            // the four roof pavilions (chhatris)
            foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 })
            {
                double x = sx * 16.5, z = sz * 16.5;
                s.Cyl(x, 30.9, z, 5, 0.6, 8, m);
                Ring(s, x, z, 4.2, 31.5, 8, 0.7, 4.6, m, 3);
                s.Cyl(x, 36.1, z, 5.3, 0.5, 8, Sculpt.Shade(m, 0.9));
                s.Lathe(x, 36.6, z, new double[] { 4.3, 0, 4.7, 1.5, 4.5, 3.2, 3.4, 5, 1.8, 6.2, 0.4, 6.8, 0.15, 8.5 }, 14, m);
            }
            // the minarets at the corners of the plinth, leaning out very slightly
            foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 })
            {
                double x = sx * 44, z = sz * 44;
                s.Lathe(x, 7, z, new double[] { 2.9, 0, 2.6, 12, 3.5, 12.4, 3.5, 13.3, 2.5, 13.3, 2.3, 24, 3.2, 24.4, 3.2, 25.3, 2.2, 25.3, 2.0, 34.5, 3.0, 34.9, 3.0, 35.8 }, 12, m, 1, 1,
                    new uint[] { m, in2, in2, m, m, in2, in2, m, m, in2, in2 });
                Ring(s, x, z, 2.1, 42.8, 8, 0.4, 3, m, 3);
                s.Lathe(x, 45.8, z, new double[] { 2.9, 0, 2.9, 0.4, 2.3, 0.4, 2.5, 1.6, 2.2, 2.8, 1.2, 3.8, 0.2, 4.3, 0.1, 5.6 }, 12, m);
            }
        }

        public static void StBasil(Sculpt s)
        {
            // nine churches on one base: the tall tented church in the middle (47.5 m), four big towers on the compass points, four small between them
            uint brick = 0xb04f3c, cream = 0xf0e8d6, green = 0x3f8f5c, dk = 0x3e2c26;
            // an onion dome whose facets alternate two colours: twisted (1), in straight stripes (0) or as a chequer (2)
            Action<double, double, double, double, uint, uint, int> onion = (x, y, z, r, c1, c2, kind) =>
            {
                double[] pr = { 0.74, 0, 1.02, 0.3, 1.2, 0.72, 1.24, 1.1, 1.1, 1.6, 0.8, 2.15, 0.45, 2.65, 0.18, 3.1, 0, 3.6 };
                const int sides = 16;
                for (int k = 0; k + 1 < pr.Length / 2; k++)
                    for (int i = 0; i < sides; i++)
                    {
                        double a0 = i * 2 * Math.PI / sides + (kind == 1 ? k * 0.2 : 0), a1 = a0 + 2 * Math.PI / sides, b0 = a0 + (kind == 1 ? 0.2 : 0), b1 = a1 + (kind == 1 ? 0.2 : 0);
                        double r0 = pr[k * 2] * r, y0 = y + pr[k * 2 + 1] * r, r1 = pr[k * 2 + 2] * r, y1 = y + pr[k * 2 + 3] * r;
                        uint c = (kind == 2 ? (i + k) % 2 : i % 2) == 0 ? c1 : c2;
                        s.Tri(x + r0 * Math.Cos(a0), y0, z + r0 * Math.Sin(a0), x + r0 * Math.Cos(a1), y0, z + r0 * Math.Sin(a1), x + r1 * Math.Cos(b1), y1, z + r1 * Math.Sin(b1), c);
                        s.Tri(x + r0 * Math.Cos(a0), y0, z + r0 * Math.Sin(a0), x + r1 * Math.Cos(b1), y1, z + r1 * Math.Sin(b1), x + r1 * Math.Cos(b0), y1, z + r1 * Math.Sin(b0), c);
                    }
                s.Beam(x, y + 3.5 * r, z, x, y + 3.5 * r + 2.6, z, 0.22, Gold); s.Beam(x - 0.7, y + 3.5 * r + 1.6, z, x + 0.7, y + 3.5 * r + 1.6, z, 0.2, Gold);
            };
            s.Box(0, -4, 0, 36, 10, 36, brick); s.Box(0, 6, 0, 37, 0.7, 37, cream);            // the arcaded base and gallery
            for (int f = 0; f < 4; f++) { s.Push(0, 0, 0, f * 90); Row(s, -17, 18.05, 17, 18.05, 0.5, 8, 2.6, 4.6, dk, 1, 0.3); Row(s, -17, 18.2, 17, 18.2, 0.3, 9, 0.7, 5.6, cream, 0, 0.3); s.Pop(); }
            s.Box(0, 6.7, 0, 30, 3.2, 30, Sculpt.Shade(brick, 0.93)); s.Gable(0, 9.9, 0, 30, 30, 2, green);
            // the middle church: octagon, tiers of pointed gables (kokoshniki), the tent roof, a small gold dome
            s.Lathe(0, 6, 0, new double[] { 6.4, 0, 6.4, 15, 7, 15.4, 7, 16.2, 5.6, 16.2, 5.6, 19, 4.8, 19, 4.8, 21, 4.4, 21, 1.5, 36, 1.5, 38, 1.9, 38.2, 1.9, 38.8 }, 8, brick, 1, 1,
                new uint[] { brick, cream, cream, cream, brick, cream, cream, cream, 0x7a5a48, cream, cream, cream });
            Ring(s, 0, 0, 6.0, 9, 8, 1.3, 9, cream, 1, 0.3, 1, 22.5, 382.5);
            Ring(s, 0, 0, 5.4, 16.4, 8, 3.4, 3.4, cream, 2, 0.5, 1, 22.5, 382.5); Ring(s, 0, 0, 4.6, 19.2, 8, 2.6, 2.6, 0xd9a441, 2, 0.5);
            for (int i = 0; i < 8; i++) { double a = Rad(i * 45); s.Beam(4.4 * Math.Cos(a), 27, 4.4 * Math.Sin(a), 1.5 * Math.Cos(a), 42, 1.5 * Math.Sin(a), 0.5, green); }
            onion(0, 44.8, 0, 1.25, Gold, 0xd9aa33, 0);
            uint[][] domes = {
                new uint[] { 0x2f8f58, 0xe6c23a, 1 }, new uint[] { 0xd8452f, 0xf0ead8, 0 }, new uint[] { 0x2a63b8, 0xf0ead8, 1 }, new uint[] { 0x3a9a55, 0xd9482f, 2 },
                new uint[] { 0xe08a2a, 0x2f7f4f, 2 }, new uint[] { 0xc23a2f, 0x2f7f4f, 1 }, new uint[] { 0x2f6fb8, 0xe6c23a, 0 }, new uint[] { 0xd9b23a, 0x2f8f58, 2 } };
            for (int d = 0; d < 8; d++)
            {
                bool big = d % 2 == 0;
                double dist = big ? 12.5 : 11, r = big ? 3.3 : 2.3, h = big ? 15 : 8.5, x = dist * Math.Cos(Rad(d * 45)), z = dist * Math.Sin(Rad(d * 45));
                s.Lathe(x, 6, z, new double[] { r * 1.15, 0, r * 1.15, h * 0.45, r * 1.3, h * 0.47, r * 1.3, h * 0.52, r * 0.92, h * 0.6, r * 0.92, h, r * 1.05, h + 0.3, r * 1.05, h + 0.9, r * 0.74, h + 0.9 }, 8, big ? brick : cream, 1, 1,
                    new uint[] { big ? brick : cream, cream, cream, brick, big ? cream : brick, cream, cream, cream });
                Ring(s, x, z, r * 1.17, 7, 8, r * 0.45, h * 0.3, dk, 1, 0.2, 1, 22.5, 382.5);
                Ring(s, x, z, r * 0.94, 6 + h * 0.62, 8, r * 0.3, h * 0.28, dk, 1, 0.2, 1, 22.5, 382.5);
                Ring(s, x, z, r * 1.2, 6 + h * 0.47, 8, r * 0.8, r * 0.8, big ? 0xd9a441 : green, 2, 0.4, 1, 22.5, 382.5);
                onion(x, 6.9 + h, z, r, domes[d][0], domes[d][1], (int)domes[d][2]);
            }
            // the tented bell tower by the south-east corner, and the two roofed porches
            s.Lathe(22, -4, -16, new double[] { 4, 0, 4, 22, 4.4, 22.3, 4.4, 23, 3.6, 23, 0.4, 37 }, 8, brick, 1, 1, new uint[] { brick, cream, cream, cream, green });
            Ring(s, 22, -16, 4.05, 15, 8, 1.6, 5, dk, 1, 0.3, 1, 22.5, 382.5);
            s.Beam(22, 33, -16, 22, 36.5, -16, 0.2, Gold);
            foreach (double sx in new[] { -1.0, 1.0 }) { s.Box(sx * 13, -4, 21, 6, 13, 7, cream); s.Taper(sx * 13, 9, 21, 7, 8, 0.6, 0.6, 8, green); }
        }

        public static void Sagrada(Sculpt s)
        {
            // as it stands today: nave 90 m long (the Glory front at -z, apse at +z), Nativity front on +x, Passion front on -x;
            // eight bell towers, four Evangelist towers (135 m), the tower of Mary (138 m) and the tower of Jesus (172.5 m)
            uint st = 0xc9b38d, pale = 0xddd0b4, dark = 0x5f5340, roof = 0xb7a784;
            uint[] fruit = { 0xf26633, 0xf2cc33, 0x4db266, 0xd94073 };
            int n = 0;
            // a Gaudí spire: a parabola, pierced by tiers of slots, with a bright mosaic tip
            Action<double, double, double, double, uint, int> spire = (x, z, h, r, col, kind) =>
            {
                var ry = new double[26];
                for (int i = 0; i <= 12; i++) { double t = i / 12.0; ry[i * 2] = r * Math.Sqrt(Math.Max(0, 1 - Math.Pow(t, 2.4))) * (t < 0.3 ? 1 : 1 - (t - 0.3) * 0.25); ry[i * 2 + 1] = h * t; }
                ry[24] = 0.25;
                s.Lathe(x, 0, z, ry, 12, col);
                for (int i = 4; i < 11; i++) Ring(s, x, z, ry[i * 2] + 0.05, h * i / 12.0, 12, ry[i * 2] * 0.22, h / 16, dark, 0, 0.3);
                if (kind == 0)
                {
                    uint c = fruit[n++ % 4];
                    s.Lathe(x, h, z, new double[] { 0.3, 0, 1.0, 1.5, 1.3, 3, 0.4, 4.2, 1.5, 5.5, 1.5, 6.4, 0, 7 }, 8, c, 1, 1, new uint[] { Gold, c, c, 0xf4f0e4, 0xf4f0e4, Gold });
                }
            };
            s.Box(0, -5, -5, 45, 50, 80, st); s.Gable(0, 45, -5, 80, 45, 12, roof, 90);          // nave and aisles
            s.Box(0, -5, 12, 60, 50, 30, st); s.Gable(0, 45, 12, 60, 30, 12, roof);               // transept
            s.Box(0, 45, -5, 22, 24, 78, pale); s.Gable(0, 69, -5, 78, 22, 6, roof, 90);          // the high nave vault
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                Row(s, sx * 22.6, -42, sx * 22.6, -4, 8, 6, 3.2, 20, dark, 2, 0.4);
                Row(s, sx * 22.6, -42, sx * 22.6, -4, 31, 6, 3.6, 3.6, 0x7fa0c9, 1, 0.4);
                Row(s, sx * 11.1, -42, sx * 11.1, 30, 50, 10, 2.6, 14, dark, 2, 0.4);
                for (int i = 0; i < 7; i++) s.Lathe(sx * 22.4, 45, -42 + i * 6.3, new double[] { 1.2, 0, 0.9, 5, 0, 9 }, 6, pale);      // the row of pinnacles
            }
            s.Cyl(0, -5, 40, 23, 40, 14, st); s.Cone(0, 35, 40, 23, 10, 14, roof);                 // the apse and its ring of chapels
            Ring(s, 0, 40, 23.1, 6, 7, 3, 20, dark, 2, 0.4, 1, 15, 165);
            for (int i = 0; i < 7; i++) { double a = Rad(15 + i * 25); s.Lathe(26 * Math.Cos(a), 0, 40 + 26 * Math.Sin(a), new double[] { 2.4, -5, 2.2, 30, 1.2, 40, 0, 46 }, 8, pale); }
            // the three fronts
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                Plate(s, Pointed(12, 0, 40, 34, -11.5, 6, 18, 0, 9, 26, 11.5, 6, 18), sx * 30, sx * 34.5, sx > 0 ? st : pale, true, sx > 0 ? Sculpt.Shade(st, 0.96) : pale);
                s.Box(sx * 30.5, 0, 12, 1, 26, 30, dark);
                if (sx > 0) { s.Lathe(36, 30, 12, new double[] { 3.2, 0, 2.2, 8, 1, 14, 0, 18 }, 8, 0x4f8a4a); }                          // the cypress on the Nativity front
                else for (int i = 0; i < 6; i++) s.Beam(-36.5, 0, -6 + i * 7.2, -33, 22, -3 + i * 6, 0.9, pale);                          // the leaning "bone" columns of the Passion front
                double[] hs = { 98, 107, 107, 98 }; double[] zs = { -9, 3.5, 20.5, 33 };
                for (int i = 0; i < 4; i++) spire(sx * 34, zs[i], hs[i] + (sx < 0 ? 5 : 0), 3.7, sx > 0 ? st : pale, 0);
            }
            s.Box(0, 0, -47, 45, 38, 5, pale); Row(s, -18, -49.6, 18, -49.6, 0, 5, 4.4, 20, dark, 2, 0.4);      // the Glory front, still without its towers
            // the central towers
            foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 })
            {
                spire(sx * 13, 12 + sz * 13, 128, 5.4, pale, 1);
                s.Lathe(sx * 13, 126, 12 + sz * 13, new double[] { 1.4, 0, 1.8, 2, 0.6, 4.5, 2.2, 6, 0.3, 9 }, 6, 0xf4f0e4);
            }
            spire(0, 38, 127, 6.4, pale, 1);
            s.Lathe(0, 125, 38, new double[] { 2.2, 0, 3, 2, 3, 4.2, 1.4, 6 }, 12, 0x9fc2e6);                                             // Mary's crown and star
            for (int i = 0; i < 6; i++) { double a = Rad(i * 60); s.Beam(0, 134, 38, 3.6 * Math.Cos(a), 134 + 3.6 * Math.Sin(a), 38, 0.9, 0xf8f4e0); s.Beam(0, 134, 38, 0, 134 + 3.6 * Math.Cos(a), 38 + 3.6 * Math.Sin(a), 0.9, 0xf8f4e0); }
            s.Beam(0, 129, 38, 0, 134, 38, 0.8, 0xf8f4e0);
            spire(0, 12, 156, 8.4, pale, 1);
            s.Lathe(0, 152, 12, new double[] { 2.2, 0, 2.6, 3, 1.4, 5 }, 8, 0xf4f0e4);                                                    // the cross of the Jesus tower
            s.Box(0, 156, 12, 2.4, 16.5, 2.4, 0xf4f0e4); s.Box(0, 163, 12, 13.5, 2.4, 2.4, 0xf4f0e4); s.Box(0, 163, 12, 2.4, 2.4, 13.5, 0xf4f0e4);
        }

        public static void NotreDame(Sculpt s)
        {
            // 128 m long, towers 69 m, roof ridge 43 m, spire 96 m. The west front faces -z.
            uint st = 0xd2c8ae, sd = Sculpt.Shade(st, 0.9), lead = 0x6c757c, glass = 0x39486a;
            double zf = -64, zt = 0;                              // the west front; the crossing
            // aisles and chapels low and wide, the nave and choir high and narrow above them
            s.Box(0, 0, -5, 40, 13, 96, st);
            s.Gable(-14.5, 13, -5, 96, 11, 3, lead, 90); s.Gable(14.5, 13, -5, 96, 11, 3, lead, 90);
            s.Box(0, 0, -5, 13.5, 33, 96, st);
            s.Gable(0, 33, -5, 96, 14.5, 10, lead, 90);
            for (int i = 0; i < 11; i++)                           // the tall windows of the clerestory
                foreach (double sx in new[] { -1.0, 1.0 }) { double z = -48 + i * 8.6; if (Math.Abs(z - zt) < 8) continue; s.Box(sx * 6.8, 17, z, 0.3, 12, 3.6, glass); }
            // transept with its gables and rose windows
            s.Box(0, 0, zt, 48, 33, 13.5, st);
            s.Gable(0, 33, zt, 48, 14.5, 10, lead);
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                s.Tri(sx * 24.05, 33, zt - 7, sx * 24.05, 33, zt + 7, sx * 24.05, 43, zt, st);
                s.Lathe(sx * 24.1, 22, zt, new[] { 0, -0.2, 6.2, -0.2, 6.2, 0.2, 0, 0.2 }, 16, glass, 0.05, 1);
                s.Push(sx * 24.2, 22, zt, 0, 90); s.Lathe(0, 0, 0, new[] { 0, 0, 6.2, 0, 6.2, 0.4, 0, 0.4 }, 16, glass); s.Pop();
                s.Box(sx * 24.2, 0, zt, 0.5, 10, 5, sd);
            }
            // the round east end: ambulatory, then the high apse, with cone roofs
            s.Lathe(0, 0, 43, new[] { 0, 0, 20, 0, 20, 13, 7, 16, 6.75, 16, 6.75, 33, 0, 43 }, 14, st, 1, 1, new[] { st, st, lead, st, st, lead });
            // flying buttresses along both sides and round the apse
            for (int i = 0; i < 12; i++)
                foreach (double sx in new[] { -1.0, 1.0 })
                {
                    double z = -52 + i * 8.6;
                    if (Math.Abs(z - zt) < 9) continue;
                    s.Box(sx * 21.5, 0, z, 3, 22, 1.4, st); s.Taper(sx * 21.5, 22, z, 1.6, 1.4, 0.3, 0.3, 5, sd);
                    s.Beam(sx * 21, 21, z, sx * 7, 30, z, 0.9, st);
                }
            for (int i = 0; i <= 8; i++)
            {
                double a = Rad(-90 + i * 22.5), cx = Math.Sin(a), cz = Math.Cos(a);
                s.Push(21.5 * cx, 0, 43 + 21.5 * cz, -a / Geo.Deg + 90);
                s.Box(0, 0, 0, 3, 22, 1.4, st); s.Taper(0, 22, 0, 1.6, 1.4, 0.3, 0.3, 5, sd);
                s.Pop();
                s.Beam(21 * cx, 21, 43 + 21 * cz, 7 * cx, 30, 43 + 7 * cz, 0.9, st);
            }
            // the west front: three portals, the gallery of kings, the rose, the open arcade, then the two towers
            Plate(s, Arched(0, 0, 41, 20, -13.2, 6.2, 12.5, 0, 7.2, 14, 13.2, 6.2, 12.5), zf - 3, zf + 9, st);
            foreach (double x in new[] { -13.2, 0, 13.2 }) s.Box(x, 0, zf + 1, x == 0 ? 6.6 : 5.6, x == 0 ? 11 : 9.8, 0.4, 0x5b4634);
            s.Box(0, 20, zf + 3, 41, 3, 12.6, sd);                 // the gallery of kings
            for (int i = -13; i <= 13; i++) s.Box(i * 1.5, 20.3, zf - 3.2, 0.8, 2.4, 0.3, st);
            s.Box(0, 23, zf + 3, 41, 10, 12, st);
            s.Push(0, 28, zf - 3.1, 0, 0); 
            s.Pop();
            for (int k = 0; k < 16; k++) { double a = k * Math.PI / 8; s.Tri(0, 28, zf - 3.1, 4.8 * Math.Cos(a), 28 + 4.8 * Math.Sin(a), zf - 3.1, 4.8 * Math.Cos(a + Math.PI / 8), 28 + 4.8 * Math.Sin(a + Math.PI / 8), zf - 3.1, k % 2 == 0 ? glass : Sculpt.Shade(glass, 1.25)); }
            foreach (double sx in new[] { -1.0, 1.0 }) s.Box(sx * 13.2, 24, zf - 3.1, 4.5, 8, 0.3, glass);
            s.Box(0, 33, zf + 3, 41, 1.2, 12.6, sd);
            for (int i = -9; i <= 9; i++) s.Box(i * 2.15, 34.2, zf - 2.6, 0.5, 8.3, 0.5, st);          // the open arcade between the towers
            s.Box(0, 42.5, zf - 2.6, 41, 1.2, 1.2, sd);
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                s.Box(sx * 13.7, 33, zf + 3, 13.6, 10.5, 12, st);
                s.Box(sx * 13.7, 43.5, zf + 3, 13.2, 24, 11.6, st);
                for (int f = 0; f < 4; f++)                         // the pairs of tall belfry openings
                {
                    s.Push(sx * 13.7, 0, zf + 3, f * 90);
                    foreach (double o in new[] { -2.9, 2.9 }) { s.Box(o, 45.5, f % 2 == 0 ? 5.85 : 6.65, 2.6, 16, 0.3, Dark); s.Box(o, 61.5, f % 2 == 0 ? 5.85 : 6.65, 1.9, 1.6, 0.3, Dark); }
                    s.Pop();
                }
                s.Box(sx * 13.7, 67.5, zf + 3, 14, 1.5, 12.4, sd);
            }
            // the spire over the crossing
            s.Box(0, 43, zt, 5, 6, 5, lead);
            s.Lathe(0, 49, zt, new[] { 3.2, 0, 2.6, 5, 2.9, 5.2, 1.6, 14, 0, 47 }, 8, lead);
        }

        public static void StPeters(Sculpt s)
        {
            // a Latin cross 187 m long; the facade on the square is at -z. Dome 136.6 m to the cross. The square with Bernini's colonnades lies in front.
            uint tr = 0xe6dfcd, sh = 0xcfc7b3, dome = 0x8f9ca6, roof = 0xa9a79c, win = 0x5f5a50;
            s.Box(0, -4, 20, 112, 42, 112, Sculpt.Shade(tr, 0.97));             // the square body around the crossing
            s.Box(0, 0, -30, 60, 46, 136, tr); s.Gable(0, 46, -30, 136, 60, 6, roof, 90);      // nave
            s.Box(0, 0, 25, 138, 46, 52, tr); s.Gable(0, 46, 25, 138, 52, 6, roof);            // transept
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                s.Cyl(sx * 69, 0, 25, 26, 46, 16, tr); s.Cone(sx * 69, 46, 25, 26, 5, 16, roof);
                Ring(s, sx * 69, 25, 26.1, 8, 7, 3, 10, win, 1, 0.4, 1, sx > 0 ? -70 : 110, sx > 0 ? 70 : 250);
                Ring(s, sx * 69, 25, 26.1, 30, 7, 3.4, 5, win, 0, 0.4, 1, sx > 0 ? -70 : 110, sx > 0 ? 70 : 250);
                Row(s, sx * 56.2, -30, sx * 56.2, 72, 6, 9, 3, 10, win, 1, 0.4); Row(s, sx * 56.2, -30, sx * 56.2, 72, 28, 9, 3.4, 5, win, 0, 0.4);
                Row(s, sx * 30.2, -95, sx * 30.2, -36, 6, 5, 3, 10, win, 1, 0.4); Row(s, sx * 30.2, -95, sx * 30.2, -36, 30, 5, 3.4, 5, win, 0, 0.4);
            }
            s.Box(0, 0, 62, 60, 46, 44, tr); s.Cyl(0, 0, 84, 30, 46, 16, tr); s.Cone(0, 46, 84, 30, 5, 16, roof);   // choir and apse
            Ring(s, 0, 84, 30.1, 8, 7, 3, 10, win, 1, 0.4, 1, 20, 160); Ring(s, 0, 84, 30.1, 30, 7, 3.4, 5, win, 0, 0.4, 1, 20, 160);
            s.Box(0, 42, -30, 114, 1.5, 138, sh); s.Box(0, 42, 25, 140, 1.5, 54, sh);
            // Maderno's facade: giant columns, a pediment over the middle, the attic and the thirteen statues
            s.Box(0, 0, -100, 115, 36, 14, Sculpt.Shade(tr, 1.03));
            s.Box(0, 36, -100, 116.4, 2.6, 15.4, tr);
            s.Box(0, 38.6, -100, 115, 7, 14, Sculpt.Shade(tr, 1.03));
            s.Box(0, 45.6, -100, 116, 1, 15, tr);
            double[] cx = { -30, -22.5, -13.5, -6.5, 6.5, 13.5, 22.5, 30 };
            foreach (double x in cx) s.Cyl(x, 0, -108, 1.5, 27.5, 10, tr);
            foreach (double x in new[] { -53.0, -44, -37, 37, 44, 53 }) s.Box(x, 0, -107.2, 2.8, 27.5, 0.9, tr);
            s.Box(0, 27.5, -107.6, 110, 3.2, 1.4, tr);
            s.Push(0, 0, -107.6, 0); s.Gable(0, 30.7, 0, 30, 2.4, 5.4, tr); s.Pop();
            foreach (double x in new[] { -47.0, -26, -10, 0, 10, 26, 47 }) Panel(s, x, 0.5, -107.3, Math.Abs(x) > 40 ? 8 : 4.4, Math.Abs(x) > 40 ? 16 : 8.5, 0, win, Math.Abs(x) > 40 ? 1 : 0, 0.4);
            foreach (double x in new[] { -47.0, -26, -18, -10, 0, 10, 18, 26, 47 }) { Panel(s, x, 14, -107.3, 3.4, 7.5, 0, win, 1, 0.4); Panel(s, x, 39.6, -107.3, 3, 4.2, 0, win, 0, 0.4); }
            for (int i = 0; i < 13; i++) s.Lathe(-54 + i * 9, 46.6, -105, new double[] { 1, 0, 0.8, 3, 1.1, 4, 0.5, 5, 0.5, 5.8, 0, 6 }, 6, tr);
            foreach (double sx in new[] { -1.0, 1.0 }) { s.Push(sx * 51, 46.6, -105.4, 0, 0); s.Cyl(0, 0, 0, 0.01, 0.01, 4, tr); s.Pop(); s.Lathe(sx * 51, 46.6, -103, new double[] { 2.6, 0, 2.6, 0.3 }, 12, 0xf2ead2); }
            // Michelangelo's dome: drum with sixteen pairs of columns, ribbed dome, lantern, ball and cross
            s.Cyl(0, 46, 25, 25, 6, 32, tr);
            s.Lathe(0, 52, 25, new double[] { 21, 0, 21, 19, 23.4, 19.4, 23.4, 21, 21.4, 21, 21.4, 25.5, 22, 25.5 }, 32, tr, 1, 1, new uint[] { tr, sh, sh, sh, tr, sh });
            Ring(s, 0, 25, 21.1, 55, 16, 3.4, 9, win, 0, 0.4);
            for (int i = 0; i < 16; i++)
            {
                double a = i * 22.5 + 11.25;
                s.Push(0, 0, 25, a);
                s.Box(0, 52, 22.6, 3.6, 19, 3.4, tr); s.Cyl(-1, 52, 24, 0.75, 17.5, 8, Sculpt.Shade(tr, 1.04)); s.Cyl(1, 52, 24, 0.75, 17.5, 8, Sculpt.Shade(tr, 1.04));
                s.Pop();
            }
            var prof = new double[22];
            for (int i = 0; i <= 10; i++) { double a = i * (Math.PI / 2 - 0.22) / 10; prof[i * 2] = 21.2 * Math.Cos(a); prof[i * 2 + 1] = 35 * Math.Sin(a); }
            s.Lathe(0, 77.5, 25, prof, 32, dome);
            for (int i = 0; i < 16; i++)
            {
                double a = Rad(i * 22.5 + 11.25), c = Math.Cos(a), z = Math.Sin(a);
                var p = new double[33];
                for (int k = 0; k <= 10; k++) { p[k * 3] = (prof[k * 2] + 0.3) * c; p[k * 3 + 1] = 77.5 + prof[k * 2 + 1]; p[k * 3 + 2] = 25 + (prof[k * 2] + 0.3) * z; }
                s.Path(p, 1.3, 0xb9c0c2);
            }
            double ty = 77.5 + prof[21];
            s.Lathe(0, ty, 25, new double[] { 6.4, 0, 6.4, 1.6, 3.6, 1.6, 3.6, 11.5, 5.2, 11.8, 5.2, 13, 3.8, 13, 2.4, 17.5, 0.9, 19.5 }, 16, tr, 1, 1, new uint[] { tr, sh, win, tr, sh, tr, dome, dome });
            Ring(s, 0, 25, 4.7, ty + 1.6, 16, 0.7, 10, tr, 3);
            s.Ball(0, ty + 20.6, 25, 1.25, Gold); s.Beam(0, ty + 21.5, 25, 0, ty + 25.4, 25, 0.45, Gold); s.Beam(-1.1, ty + 24, 25, 1.1, ty + 24, 25, 0.45, Gold);
            // the two small domes beside it
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                s.Cyl(sx * 40, 43, -32, 8.5, 11, 12, tr); Ring(s, sx * 40, -32, 8.6, 45, 8, 2.2, 7, win, 1, 0.3);
                s.Ball(sx * 40, 54, -32, 8.3, dome, 1.25, true, 16); s.Cyl(sx * 40, 64, -32, 1.6, 4, 8, tr); s.Cone(sx * 40, 68, -32, 1.8, 2, 8, dome);
            }
            // St Peter's Square: the straight arms from the facade, the two curved colonnades and the obelisk
            const double oz = -305;
            Action<double, double, double, double> arm = (ax, az, bx, bz) =>
            {
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)), yaw = -Math.Atan2(bz - az, bx - ax) / Geo.Deg;
                s.Push((ax + bx) / 2, 0, (az + bz) / 2, yaw);
                s.Box(0, 13, 0, len, 3.4, 17, tr); s.Box(0, 16.4, 0, len, 1.3, 15.4, sh);
                foreach (double off in new[] { -7.3, -2.6, 2.6, 7.3 }) Row(s, -len / 2, off, len / 2, off, 0, (int)(len / 5.2), 1.5, 13, tr, 3);
                s.Pop();
            };
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                double ex = sx * (40 + 75 * Math.Cos(Rad(84))), ez = oz + 75 * Math.Sin(Rad(84));
                arm(sx * 63, -112, ex, ez + 4);
                int n = 22;
                for (int i = 0; i < n; i++)
                {
                    double a0 = Rad(84 - 168.0 * i / n), a1 = Rad(84 - 168.0 * (i + 1) / n);
                    arm(sx * (40 + 75 * Math.Cos(a0)), oz + 75 * Math.Sin(a0), sx * (40 + 75 * Math.Cos(a1)), oz + 75 * Math.Sin(a1));
                }
                s.Lathe(sx * 62, 0, oz, new double[] { 5, 0, 5, 1, 1, 1.4, 0.8, 5, 2.6, 5.4, 0.6, 6, 0.4, 8 }, 12, sh);      // the two fountains
            }
            s.Box(0, 0, oz, 8, 8.2, 8, sh); s.Taper(0, 8.2, oz, 2.8, 2.8, 1.9, 1.9, 23, 0xb5746a); s.Taper(0, 31.2, oz, 1.9, 1.9, 0, 0, 2.2, 0xb5746a); s.Beam(0, 33.4, oz, 0, 36.8, oz, 0.3, Gold);
        }

        public static void HagiaSophia(Sculpt s)
        {
            // nave 82 x 73 m, the 31 m dome 55.6 m up, half domes to east and west (along z), great buttresses to north and south, four minarets
            uint wall = 0xd19c84, pale = 0xe3d6c2, dome = 0x8a959e, win = 0x4f4a48, lead = 0x7f8a92;
            s.Box(0, -4, 0, 72, 23, 78, wall);                                   // aisles and galleries
            s.Box(0, 19, 0, 73, 1, 79, pale);
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                Row(s, sx * 36.1, -36, sx * 36.1, 36, 3, 9, 2.6, 5.5, win, 1, 0.4); Row(s, sx * 36.1, -36, sx * 36.1, 36, 11.5, 9, 2.6, 5.5, win, 1, 0.4);
                Row(s, -33, sx * 39.1, 33, sx * 39.1, 11.5, 8, 2.6, 5.5, win, 1, 0.4);
            }
            s.Box(0, 20, 0, 60, 4, 70, Sculpt.Shade(wall, 0.96)); s.Box(0, 24, 0, 60.6, 0.8, 70.6, lead);
            s.Box(0, 20, 0, 35, 19.5, 35, Sculpt.Shade(wall, 1.04));             // the square that carries the dome
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                // the great arch on each side with its wall of windows
                Plate(s, Arched(0, 20, 34, 19.5, 0, 28, 17), sx * 17.6, sx * 19.2, Sculpt.Shade(wall, 1.04), true, Sculpt.Shade(wall, 1.08));
                s.Box(sx * 17.8, 20, 0, 0.8, 16, 27, pale);
                Row(s, sx * 18.3, -12, sx * 18.3, 12, 25, 7, 2, 4, win, 1, 0.3); Row(s, sx * 18.3, -8, sx * 18.3, 8, 30.5, 5, 2, 3.6, win, 1, 0.3);
                foreach (double sz in new[] { -1.0, 1.0 })
                {
                    s.Box(sx * 27, 0, sz * 14.5, 20, 31, 8.5, wall);              // the four great buttresses
                    s.Taper(sx * 27, 31, sz * 14.5, 20, 8.5, 19, 5, 6, wall, 0, lead);
                    s.Cyl(sx * 26, 24.8, sz * 30, 7, 3, 14, wall); s.Ball(sx * 26, 27.8, sz * 30, 7.2, dome, 0.75, true, 14);
                }
            }
            // the dome on its ring of forty windows
            s.Lathe(0, 39.5, 0, new double[] { 17.4, 0, 17.4, 1, 16.6, 1, 16.6, 5.2 }, 40, pale, 1, 1, new uint[] { pale, pale, wall });
            Ring(s, 0, 0, 16.7, 40.8, 40, 1.3, 3.6, win, 1, 0.3);
            Ring(s, 0, 0, 17.2, 40.5, 40, 0.9, 4.2, pale, 0, 1.4, 1, 4.5, 364.5);
            var prof = new double[18];
            for (int i = 0; i <= 8; i++) { double a = i * Math.PI / 16; prof[i * 2] = 16.6 * Math.Cos(a); prof[i * 2 + 1] = 11.8 * Math.Sin(a); }
            prof[16] = 0;
            s.Lathe(0, 44.7, 0, prof, 40, dome);
            s.Lathe(0, 56.4, 0, new double[] { 0.7, 0, 0.9, 0.8, 0.3, 1.5, 0.5, 2.2, 0.1, 2.8, 0.1, 4 }, 8, Gold);
            // the half domes east and west, each with its small side domes, then the apse and the narthex
            foreach (double sz in new[] { -1.0, 1.0 })
            {
                s.Cyl(0, 24, sz * 22, 16.8, 5, 28, Sculpt.Shade(wall, 1.02));
                Ring(s, 0, sz * 22, 16.9, 25, 7, 1.5, 3, win, 1, 0.3, 1, sz > 0 ? 30 : 210, sz > 0 ? 150 : 330);
                s.Ball(0, 29, sz * 22, 16.4, dome, 0.72, true, 28);
            }
            s.Cyl(0, 0, 40, 8.5, 22, 12, wall); s.Ball(0, 22, 40, 8.6, dome, 0.7, true, 14);
            Ring(s, 0, 40, 8.6, 6, 5, 1.8, 8, win, 1, 0.3, 1, 30, 150);
            s.Box(0, -4, -45, 66, 17, 12, wall); s.Box(0, 13, -45, 67, 0.8, 13, lead);
            Row(s, -30, -51.1, 30, -51.1, 2, 9, 2.4, 6, win, 1, 0.3);
            s.Box(0, 0, -41, 62, 22, 6, Sculpt.Shade(wall, 0.97));
            // minarets: fluted shaft, a balcony, lead cone
            foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 })
            {
                uint mc = sx < 0 && sz > 0 ? 0xb5694d : pale;                    // the south-east one is brick
                s.Box(sx * 41, -4, sz * 45, 8, 16, 8, mc);
                s.Lathe(sx * 41, 12, sz * 45, new double[] { 3.6, 0, 2.7, 3, 2.5, 33, 3.6, 34, 3.6, 35.6, 2.2, 35.6, 2.1, 42, 2.5, 42, 0, 53 }, 12, mc, 1, 1, new uint[] { mc, mc, pale, pale, pale, mc, lead, lead });
            }
        }

        public static void Kaaba(Sculpt s)
        {
            // 13.1 m high on a marble base, draped in the black kiswah with its gold band two thirds up; door on the north-east face (+z)
            uint black = 0x1a1a1c, gold = 0xcfa93a, marble = 0xe2ddd0;
            s.Taper(0, 0, 0, 12.2, 14, 11.3, 13.1, 0.5, marble);
            s.Box(0, 0.5, 0, 11.03, 12.6, 12.86, black);
            s.Box(0, 12.9, 0, 11.4, 0.25, 13.2, 0xe8e4da);
            s.Box(0, 8.6, 0, 11.13, 0.95, 12.96, gold);
            for (int f = 0; f < 4; f++)
            {
                double half = f % 2 == 0 ? 6.43 : 5.515, len = f % 2 == 0 ? 11.03 : 12.86;
                s.Push(0, 0, 0, f * 90);
                Row(s, -len / 2 + 0.6, half + 0.06, len / 2 - 0.6, half + 0.06, 8.8, 9, 0.85, 0.55, black, 0, 0.04);          // lettering on the band
                Row(s, -len / 2 + 1, half + 0.04, len / 2 - 1, half + 0.04, 6.9, 4, 1.1, 0.9, gold, 0, 0.04);                  // the panels below it
                s.Pop();
            }
            s.Box(2.2, 2.2, 6.48, 1.9, 3.2, 0.14, gold); s.Box(2.2, 2.2, 6.5, 2.3, 3.6, 0.06, 0x8a6f20);                           // the door
            s.Lathe(5.6, 1.1, 6.5, new double[] { 0.32, 0, 0.32, 0.75, 0, 0.75 }, 10, 0xc9c9c9);                                    // the Black Stone's silver frame
            // the Hijr Ismail: a low half-round wall off the north-west face
            for (int i = 0; i < 12; i++)
            {
                double a0 = Rad(180 + i * 15), a1 = Rad(180 + (i + 1) * 15);
                s.Beam(-5.9 + 7.9 * Math.Cos(a0) * 1.0 + 0, 0.65, 5.9 * Math.Sin(a0), -5.9 + 7.9 * Math.Cos(a1), 0.65, 5.9 * Math.Sin(a1), 1.3, marble);
            }
            s.Lathe(0, 0, 14.5, new double[] { 0.9, 0, 0.9, 1, 0.6, 1.2, 0.6, 2.4, 0.2, 3 }, 8, gold);                             // Maqam Ibrahim
        }

        // ---- gates and arches

        public static void Brandenburg(Sculpt s)
        {
            // 26 m high with the quadriga, 65.5 m wide with its two gatehouses, 11 m deep; six Doric columns on each face, five passages
            uint st = 0xdccba6, sh = 0xc4b48f, bz = 0x4f8f78;
            for (int i = 0; i < 6; i++)
            {
                double x = -14.2 + i * 5.68 + (i > 2 ? 0 : 0);
                foreach (double z in new[] { -4.6, 4.6 })
                {
                    s.Lathe(x, 0, z, new double[] { 1.1, 0, 1.1, 0.5, 0.88, 0.5, 0.74, 13.2, 1.0, 13.7, 1.05, 14.2 }, 12, st);
                    s.Box(x, 14.2, z, 2.2, 0.5, 2.2, st);
                }
                s.Box(x, 0, 0, 1.3, 14.7, 7.2, sh);                              // the walls between the passages
            }
            s.Box(0, 14.7, 0, 32.4, 1.5, 11.4, st);                               // architrave
            s.Box(0, 16.2, 0, 32.2, 1.5, 11.2, sh);                               // frieze, with its triglyphs
            foreach (double z in new[] { -5.65, 5.65 }) Row(s, -16, z, 16, z, 16.3, 28, 0.45, 1.3, st, 0, 0.2);
            s.Box(0, 17.7, 0, 33.6, 0.8, 12.6, st);                               // cornice
            s.Box(0, 18.5, 0, 31.6, 2.2, 10.6, st);                               // attic, stepped up to the middle
            s.Box(0, 20.7, 0, 22, 0.9, 9.6, st); s.Box(0, 21.6, 0, 12, 0.7, 8.6, st);
            s.Box(0, 18.9, 5.35, 14, 1.5, 0.2, sh);
            // the quadriga: four horses abreast, the chariot, and Victoria with her staff
            s.Box(0, 22.3, 0, 6.6, 0.5, 6, bz);
            for (int i = 0; i < 4; i++)
            {
                double x = -2.25 + i * 1.5;
                s.Lathe(x, 24.2, 1.2, new double[] { 0, -1.5, 0.42, -1.2, 0.5, 0, 0.42, 1.1, 0, 1.4 }, 8, bz, 1, 1);
                s.Push(x, 24.2, 1.2, 0, 0);
                s.Box(0, -0.45, 0.1, 0.75, 0.95, 2.6, bz);                       // body
                s.Beam(0, 0.3, 1.2, 0, 1.5, 2.0, 0.55, bz);                       // neck
                s.Beam(0, 1.5, 1.9, 0, 1.15, 2.75, 0.42, bz);                     // head
                foreach (double sx in new[] { -0.25, 0.25 }) { s.Beam(sx, -0.4, -1, sx, -1.9, -1.1, 0.2, bz); s.Beam(sx, -0.4, 1.1, sx, -1.2, 1.9, 0.2, bz); s.Beam(sx, -1.2, 1.9, sx, -1.6, 1.5, 0.18, bz); }
                s.Beam(0, 0.2, -1.2, 0, -0.9, -1.9, 0.2, bz);                     // tail
                s.Pop();
            }
            s.Box(0, 22.8, -1.9, 2, 1.5, 1.5, bz);
            foreach (double sx in new[] { -1.15, 1.15 }) { s.Push(sx, 23.6, -1.9, 0, 0); s.Lathe(0, 0, 0, new double[] { 0, 0, 0, 0 }, 3, bz); s.Pop(); s.Box(sx, 22.8, -1.9, 0.15, 1.7, 1.7, bz, 0); }
            s.Lathe(0, 24.3, -1.9, new double[] { 0.55, 0, 0.4, 1.3, 0.5, 1.9, 0.3, 2.5, 0.25, 2.8, 0.32, 3.1, 0, 3.4 }, 8, bz);
            s.Beam(0.5, 24.6, -1.7, 0.5, 29.6, -1.5, 0.14, bz); s.Box(0.5, 29.6, -1.5, 0.9, 0.9, 0.12, bz); s.Box(0.5, 30.5, -1.5, 1.3, 0.5, 0.3, bz);
            // the gatehouses, each with an open Doric colonnade round it
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                double x = sx * 25;
                s.Box(x, 0, -0.5, 10.5, 9, 8, st);
                s.Box(x, 9, 0, 15.8, 1.6, 12.4, st);
                s.Gable(x, 10.6, 0, 16.2, 12.8, 2.6, sh);
                Row(s, x - 7.2, 5.6, x + 7.2, 5.6, 0, 6, 1.1, 9, st, 3); Row(s, x - 7.2, -5.6, x + 7.2, -5.6, 0, 6, 1.1, 9, st, 3);
                Row(s, x + sx * 7.2, -4.2, x + sx * 7.2, 4.2, 0, 3, 1.1, 9, st, 3);
                Row(s, x - 4, 3.55, x + 4, 3.55, 1.5, 3, 1.3, 4, 0x5a5448, 0, 0.3);
            }
        }

        public static void ArcDeTriomphe(Sculpt s)
        {
            // 50 m high, 45 m wide, 22 m deep; the great arch is 29.2 m high and 14.6 m wide, the side arches 18.7 by 8.4
            uint st = 0xdbd1ba, dk = 0x8a806e, relief = 0xcdc2aa;
            Plate(s, Arched(0, 0, 44.8, 37, 0, 14.6, 29.2), -11, 11, st);                    // the body, with the great arch right through
            foreach (double sx in new[] { -1.0, 1.0 })
            {
                // the side arch through each pier, seen on the end and inside the great arch
                foreach (double x in new[] { sx * 22.45, sx * 7.25 })
                {
                    s.Box(x, 0, 0, 0.3, 14.5, 8.4, dk);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 8, b = (i + 1) * Math.PI / 8; s.Tri(x + sx * 0.0, 14.5, 4.2 * Math.Cos(a), x, 14.5, 4.2 * Math.Cos(b), x, 14.5 + 4.2 * Math.Sin((a + b) / 2), 4.2 * Math.Cos((a + b) / 2), dk); }
                    s.Box(x, 14.5, 0, 0.3, 3.4, 6.4, dk);
                }
                foreach (double sz in new[] { -1.0, 1.0 })
                {
                    s.Box(sx * 14.9, 0, sz * 11.6, 15.6, 5.5, 1.6, Sculpt.Shade(st, 0.95));        // plinth
                    s.Taper(sx * 14.9, 5.5, sz * 11.9, 8.5, 2.4, 5, 1.2, 11.5, relief);           // the great sculpted groups
                    s.Box(sx * 14.9, 21, sz * 11.1, 9, 5, 0.4, relief);                           // the reliefs above them
                }
            }
            s.Box(0, 31.5, 0, 46, 1.2, 23.2, Sculpt.Shade(st, 0.92));                       // cornice lines and the frieze between
            s.Box(0, 32.7, 0, 45.2, 3.3, 22.4, relief);
            s.Box(0, 36, 0, 47.4, 1.6, 24.6, Sculpt.Shade(st, 1.03));
            s.Box(0, 37.6, 0, 44.2, 10.4, 21.4, st);                                         // the attic
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90);
                double half = f % 2 == 0 ? 10.75 : 22.15; int nn = f % 2 == 0 ? 10 : 4;
                for (int i = -nn; i <= nn; i++) s.Box(i * 2.1, 40, half, 1.2, 5.6, 0.3, Sculpt.Shade(st, 0.86));   // the row of shields
                s.Pop();
            }
            s.Box(0, 48, 0, 45.6, 1.2, 22.8, Sculpt.Shade(st, 1.03));
            s.Box(0, 49.2, 0, 43, 0.8, 20.2, Sculpt.Shade(st, 0.9));
        }

        // ---- bridges and wheels

        public static void GoldenGate(Sculpt s)
        {
            uint o = 0xc0362c, road = 0x55575c;
            foreach (double tz in new[] { -640.0, 640.0 })
            {
                s.Box(0, -40, tz, 48, 46, 30, Concrete);
                foreach (double tx in new[] { -14.0, 14.0 }) s.Taper(tx, 6, tz, 8, 12, 5, 8, 221, o);
                foreach (double y in new[] { 52.0, 110, 150, 185, 214 }) s.Box(0, y, tz, 26, 7, 6, o);
                s.Box(0, 221, tz, 34, 6, 9, o);
            }
            // the deck between the two abutments (main span 1280 m, side spans 343 m each), in pieces
            for (int i = 0; i < 8; i++) { s.Box(0, 61, -875 + i * 250, 27, 7.5, 250, o); s.Box(0, 68.5, -875 + i * 250, 22, 0.5, 250, road); }
            foreach (double z in new[] { -990.0, 990 }) s.Box(0, -20, z, 30, 88.5, 20, Concrete);   // the anchorages at each shore
            foreach (double cx in new[] { -14.0, 14.0 })
            {
                double pz = -983, py = 72;
                for (int i = 1; i <= 60; i++)
                {
                    double z = -983 + 1966.0 * i / 60, y;
                    if (Math.Abs(z) <= 640) y = 76 + 151 * (z / 640) * (z / 640);
                    else y = 227 - 155 * (Math.Abs(z) - 640) / 343;
                    s.Beam(cx, py, pz, cx, y, z, 1.6, o);
                    if (i % 2 == 0 && y > 74) s.Beam(cx, 69, z, cx, y, z, 0.5, o);
                    pz = z; py = y;
                }
            }
        }

        public static void TowerBridge(Sculpt s)
        {
            uint st = 0xbfb49c, blue = 0x4f9ad1;
            foreach (double z in new[] { -40.0, 40.0 })
            {
                s.Box(0, -15, z, 26, 21, 32, Sculpt.Shade(st, 0.9));
                s.Box(0, 6, z, 18, 42, 20, st);
                s.Taper(0, 48, z, 18, 20, 6, 7, 12, Slate);
                foreach (double sx in new[] { -1.0, 1.0 }) foreach (double sz in new[] { -1.0, 1.0 })
                {
                    s.Cyl(sx * 8.5, 6, z + sz * 9.5, 2.2, 50, 8, st); s.Cone(sx * 8.5, 56, z + sz * 9.5, 2.6, 9, 8, Slate);
                }
                s.Box(0, 6, z, 9, 12, 20.4, Dark);            // the road arch through the tower
                double end = z < 0 ? -1 : 1;
                s.Box(0, 8, end * 91, 16, 2, 82, blue);
                foreach (double cx in new[] { -9.0, 9.0 })
                {
                    double pz = end * 50, py = 44;
                    for (int i = 1; i <= 10; i++)
                    {
                        double t = i / 10.0, cz = end * (50 + 82 * t), cy = 12 + 32 * (1 - t) * (1 - t);
                        s.Beam(cx, py, pz, cx, cy, cz, 1.4, blue);
                        if (i < 10) s.Beam(cx, 10, cz, cx, cy, cz, 0.4, blue);
                        pz = cz; py = cy;
                    }
                }
                s.Box(0, -8, end * 136, 20, 30, 10, st); s.Taper(0, 22, end * 136, 20, 10, 8, 4, 6, Slate);
            }
            s.Box(-6, 42, 0, 4, 5, 62, blue); s.Box(6, 42, 0, 4, 5, 62, blue);
            s.Box(0, 8, 0, 16, 2, 62, blue);
        }

        public static void HarbourBridge(Sculpt s)
        {
            uint steel = 0x6f7a80, granite = 0xcfc4a8;
            foreach (double x in new[] { -15.0, 15.0 })
            {
                double pz = 0, pl = 0, pu = 0;
                for (int i = 0; i <= 28; i++)
                {
                    double z = -251.5 + 503.0 * i / 28, q = 1 - (z / 251.5) * (z / 251.5);
                    double lo = 10 + 108 * q, up = 60 + 74 * q;
                    if (i > 0)
                    {
                        s.Beam(x, pl, pz, x, lo, z, 3.2, steel); s.Beam(x, pu, pz, x, up, z, 3.2, steel);
                        if (i % 2 == 0) s.Beam(x, pl, pz, x, up, z, 1.3, steel); else s.Beam(x, pu, pz, x, lo, z, 1.3, steel);
                    }
                    s.Beam(x, lo, z, x, up, z, 1.3, steel);
                    if (lo > 54) s.Beam(x, 52, z, x, lo, z, 0.7, steel);
                    pz = z; pl = lo; pu = up;
                }
            }
            for (int i = 0; i <= 14; i++)
            {
                double z = -251.5 + 503.0 * i / 14, q = 1 - (z / 251.5) * (z / 251.5);
                s.Beam(-15, 60 + 74 * q, z, 15, 60 + 74 * q, z, 1.2, steel);
            }
            // the deck across the arch, between the pylons; the approaches are raised by the map's own bridges
            for (int i = 0; i < 4; i++) { s.Box(0, 49, -210 + i * 140, 49, 3, 140, steel); s.Box(0, 52, -210 + i * 140, 44, 0.4, 140, 0x55575c); }
            foreach (double z in new[] { -268.0, 268.0 }) foreach (double x in new[] { -24.0, 24.0 }) { s.Box(x, -10, z, 13, 99, 20, granite); }
        }

        public static void LondonEye(Sculpt s)
        {
            uint w = White, pod = 0xbcd7e0;
            const double r = 60, cy = 70;
            for (int i = 0; i < 32; i++)
            {
                double a0 = Rad(i * 11.25), a1 = Rad((i + 1) * 11.25);
                s.Beam(r * Math.Cos(a0), cy + r * Math.Sin(a0), 0, r * Math.Cos(a1), cy + r * Math.Sin(a1), 0, 1.6, w);
                s.Beam((r - 4) * Math.Cos(a0), cy + (r - 4) * Math.Sin(a0), 0, (r - 4) * Math.Cos(a1), cy + (r - 4) * Math.Sin(a1), 0, 1.0, w);
                s.Beam(r * Math.Cos(a0), cy + r * Math.Sin(a0), 0, (r - 4) * Math.Cos(a1), cy + (r - 4) * Math.Sin(a1), 0, 0.5, w);
                s.Ball((r + 3.2) * Math.Cos(a0), cy + (r + 3.2) * Math.Sin(a0), 0, 2.2, pod, 1, false, 8);
                if (i % 2 == 0) s.Beam(0, cy, 0, (r - 4) * Math.Cos(a0), cy + (r - 4) * Math.Sin(a0), 0, 0.35, w);
            }
            s.Beam(0, cy, -5, 0, cy, 12, 5, w);
            s.Beam(0, cy, 11, -24, 0, 30, 3, w); s.Beam(0, cy, 11, 24, 0, 30, 3, w);
            s.Box(0, 0, 0, 50, 4, 14, 0xc9c5bb);
        }

        // ---- modern towers

        public static void Burj(Sculpt s)
        {
            // 828 m. A six-sided core with three wings, each stepping back in turn (27 setbacks winding up the tower), then the spire.
            uint g = 0xa9b6c4, steel = 0xd5dbe0, band = 0x7f8c9a;
            double[] at = new double[27];
            for (int n = 0; n < 27; n++) at[n] = 118 + 470 * Math.Pow(n / 26.0, 0.93);
            s.Lathe(0, 0, 0, new double[] { 15, 0, 13, 420, 9.5, 600 }, 12, Sculpt.Shade(g, 0.95));
            for (int w = 0; w < 3; w++)
            {
                s.Push(0, 0, 0, w * 120);
                for (int j = 0; j < 9; j++)
                {
                    double top = at[j * 3 + w], len = 60 - j * 6.1, wid = 23.5 - j * 1.45, low = j == 0 ? 0 : at[(j - 1) * 3 + w] - 2;
                    uint c = Sculpt.Shade(g, 1 - (j % 2) * 0.05);
                    s.Box(0, low, len / 2 - wid / 4, wid, top - low, len - wid / 2, c);
                    s.Lathe(0, low, len - wid / 2, new double[] { wid / 2, 0, wid / 2, top - low, 0, top - low }, 14, c);
                    // the steel fins, and a dark band at each mechanical floor
                    foreach (double sx in new[] { -1.0, 1.0 }) for (int k = 1; k < 4; k++) s.Box(sx * (wid / 2 + 0.1), low, k * (len - wid / 2) / 4, 0.5, top - low, 0.7, steel);
                    s.Lathe(0, top - 5, len - wid / 2, new double[] { wid / 2 + 0.15, 0, wid / 2 + 0.15, 3 }, 14, band);
                    s.Box(0, top - 5, len / 2 - wid / 4, wid + 0.3, 3, len - wid / 2, band);
                }
                // the low wings of the podium
                s.Box(0, 0, 70, 30, 12, 26, Sculpt.Shade(g, 0.9));
                s.Pop();
            }
            s.Lathe(0, 0, 0, new double[] { 0, 600, 8.5, 600, 7.5, 640, 6.2, 640, 5.6, 690, 4.4, 690, 3.6, 740, 2.4, 740, 1.6, 790, 0.9, 790, 0.5, 828 }, 10, steel);
        }

        public static void Petronas(Sculpt s)
        {
            // twin 452 m towers, each an eight-pointed star in plan filled out with round bays; joined by the skybridge 170 m up
            uint st = 0xc3cbd0, gl = 0x8f9ca6;
            double[] ys = { 0, 264, 264, 300, 300, 328, 328, 350, 350, 366, 366, 376 }, rs = { 23.1, 23.1, 20.6, 20.6, 18, 18, 15.2, 15.2, 12, 12, 8.6, 8.6 };
            foreach (double x in new[] { -52.0, 52.0 })
            {
                var rings = new System.Collections.Generic.List<double[]>(); var cols = new System.Collections.Generic.List<uint>();
                for (int k = 0; k + 1 < ys.Length; k += 2)
                {
                    int n = Math.Max(1, (int)Math.Round((ys[k + 1] - ys[k]) / 8.2));
                    for (int j = 0; j <= n; j++)
                    {
                        double y = ys[k] + (ys[k + 1] - ys[k]) * j / n, r = rs[k];
                        var ring = new double[32 * 3];
                        for (int i = 0; i < 32; i++)
                        {
                            double a = i * Math.PI / 16, f = i % 4 == 0 ? 1 : i % 4 == 2 ? 0.9 : 0.8;
                            ring[i * 3] = x + r * f * Math.Cos(a); ring[i * 3 + 1] = y; ring[i * 3 + 2] = r * f * Math.Sin(a);
                        }
                        rings.Add(ring); cols.Add(j == n ? st : j % 2 == 0 ? st : gl);
                    }
                }
                Loft(s, rings.ToArray(), st, true, true, cols.ToArray());
                // the pinnacle: a ringed cone, the ball and the mast
                s.Lathe(x, 376, 0, new double[] { 8, 0, 6.4, 10, 6.9, 10, 5.4, 20, 5.9, 20, 4.4, 30, 4.9, 30, 3.4, 40, 2.2, 46, 2.9, 48.5, 2.2, 51, 0.9, 53, 0.5, 76 }, 16, st);
                // the round annexe ("bustle") on the outer side
                double bx = x + Math.Sign(x) * 27;
                s.Lathe(bx, 0, 0, new double[] { 11.5, 0, 11.5, 150, 9.5, 150, 9.5, 174, 7, 174, 7, 186, 0, 186 }, 20, st, 1, 1, new uint[] { st, st, gl, st, gl, st });
                for (int j = 0; j < 18; j++) s.Lathe(bx, 4 + j * 8.2, 0, new double[] { 11.6, 0, 11.6, 4 }, 20, gl);
            }
            // the skybridge on its two-legged arch
            s.Box(0, 170, 0, 60, 9, 5.5, st); s.Box(0, 172, 0, 60.2, 2, 5.7, gl); s.Box(0, 176, 0, 60.2, 2, 5.7, gl);
            foreach (double sx in new[] { -1.0, 1.0 }) { s.Beam(sx * 29, 118, 1.6, 0, 169.5, 0.6, 1.3, st); s.Beam(sx * 29, 118, -1.6, 0, 169.5, -0.6, 1.3, st); }
            s.Box(0, -2, 0, 170, 22, 78, Sculpt.Shade(gl, 0.9)); s.Box(0, 20, 0, 172, 1.5, 80, st);
        }

        public static void Taipei101(Sculpt s)
        {
            // 508 m: a tapering base of 25 floors, eight flaring modules of eight floors each (like a stalk of bamboo), then the top and spire
            uint g = 0x5f9c8c, lip = 0x8fbdb0, steel = 0xc9d2cf, coin = 0xd9c68a;
            s.Box(0, 0, 0, 96, 30, 96, Sculpt.Shade(g, 0.82));                         // the shopping podium
            s.Taper(0, 0, 0, 64, 64, 52.5, 52.5, 113, g);
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90);
                for (int i = 1; i < 7; i++) s.Box(0, i * 15, 32 - i * 15 * 0.0509 + 0.1, 62 - i * 1.5, 0.9, 0.5, Sculpt.Shade(g, 0.8));
                s.Push(0, 100, 26.9, 0, 0); s.Lathe(0, 0, 0, new double[] { 0, 0, 4, 0, 4, 0.01 }, 16, coin, 1, 1); s.Pop();
                s.Pop();
            }
            for (int k = 0; k < 8; k++)
            {
                double y = 113 + k * 33.6;
                s.Taper(0, y, 0, 45.5, 45.5, 53.5, 53.5, 31, k % 2 == 0 ? Sculpt.Shade(g, 1.05) : g);
                s.Box(0, y + 31, 0, 55, 1.3, 55, lip);
                s.Box(0, y + 32.3, 0, 46, 1.3, 46, Sculpt.Shade(g, 0.8));
                for (int f = 0; f < 4; f++)
                {
                    s.Push(0, 0, 0, f * 90);
                    for (int i = 1; i < 4; i++) s.Box(0, y + i * 7.75, 22.75 + i * 1.0 + 0.1, 45.5 + i * 2 - 1, 0.6, 0.4, Sculpt.Shade(g, 0.85));
                    s.Box(0, y + 24, 26.4, 5, 5, 1.2, steel);                           // the ruyi emblem on each face
                    foreach (double sx in new[] { -1.0, 1.0 }) s.Box(sx * 26.8, y + 27.5, 26.8, 2.6, 4.5, 2.6, steel, 45);   // the corner dragons
                    s.Pop();
                }
            }
            double top = 113 + 8 * 33.6;
            s.Taper(0, top, 0, 44, 44, 38, 38, 12, g);
            s.Taper(0, top + 12, 0, 32, 32, 28, 28, 10, Sculpt.Shade(g, 1.05));
            s.Box(0, top + 22, 0, 22, 14, 22, Sculpt.Shade(g, 0.9));
            s.Taper(0, top + 36, 0, 16, 16, 10, 10, 10, g);
            s.Lathe(0, top + 46, 0, new double[] { 4.2, 0, 3.6, 10, 2.6, 10, 2.2, 28, 1.4, 28, 1, 46, 0.3, 61.2 }, 8, steel);
        }

        public static void MarinaBaySands(Sculpt s)
        {
            // three 194 m hotel towers, each a pair of slabs that lean together, carrying the 340 m SkyPark
            uint glass = 0x8fb0c4, rib = 0xdfe3e2, deck = 0xd9d4cc, pool = 0x3fa9d6, green = 0x4f8a4a, dark = 0x5d7484;
            double[] sec = { -13, 0, -13, 194, 13, 194, 13.6, 130, 16, 90, 21, 50, 30.5, 0, 20, 0, 14.5, 32, 8, 58, 2, 78, -3.5, 62, -4.5, 0 };
            foreach (double z in new[] { -90.0, 0, 90 })
            {
                Plate(s, sec, z - 37, z + 37, rib, false, 0xc9d1d4);
                // the glass skins: the upright west face and the curved east face, with a line at each few floors
                for (int i = 0; i < 24; i++) s.Box(-13.15, i * 8.05, z, 0.3, 7.2, 72, i % 2 == 0 ? glass : Sculpt.Shade(glass, 0.93));
                double[] ex = { 30.5, 21, 16, 13.6, 13 }, ey = { 0, 50, 90, 130, 194 };
                for (int k = 0; k < 4; k++)
                    for (int j = 0; j < 5; j++)
                    {
                        double f0 = j / 5.0, f1 = (j + 0.86) / 5.0;
                        double x0 = ex[k] + (ex[k + 1] - ex[k]) * f0 + 0.15, y0 = ey[k] + (ey[k + 1] - ey[k]) * f0, x1 = ex[k] + (ex[k + 1] - ex[k]) * f1 + 0.15, y1 = ey[k] + (ey[k + 1] - ey[k]) * f1;
                        s.Quad(new[] { x0, y0, z - 36 }, new[] { x0, y0, z + 36 }, new[] { x1, y1, z + 36 }, new[] { x1, y1, z - 36 }, (k * 5 + j) % 2 == 0 ? dark : Sculpt.Shade(dark, 1.1));
                    }
                s.Box(4, 0, z, 46, 9, 70, Sculpt.Shade(glass, 0.8));          // the atrium between the legs
            }
            // the SkyPark: a long boat-shaped deck, hanging 67 m past the north tower
            var o = new System.Collections.Generic.List<double>();
            for (int i = 0; i <= 20; i++) { double t = i / 20.0, z = -136 + 340 * t; o.Add(19 - 12 * Math.Pow(Math.Abs(2 * t - 1), 2.6) + 3 * Math.Sin(t * Math.PI)); o.Add(z); }
            for (int i = 20; i >= 0; i--) { double t = i / 20.0, z = -136 + 340 * t; o.Add(-(17 - 12 * Math.Pow(Math.Abs(2 * t - 1), 2.6))); o.Add(z); }
            s.Poly(o.ToArray(), 195.5, 5, deck);
            var u = new System.Collections.Generic.List<double>();
            for (int i = 0; i < o.Count; i += 2) { u.Add(o[i] * 0.72); u.Add(34 + (o[i + 1] - 34) * 0.97); }
            s.Poly(u.ToArray(), 191, 4.5, Sculpt.Shade(deck, 0.82));
            s.Box(-11.5, 200.5, -20, 6, 0.5, 150, pool);                      // the infinity pool, on the city side
            for (int i = 0; i < 26; i++) s.Ball(6 + (i * 7 % 5) * 1.6, 201.5, -120 + i * 12.2, 2.4, green, 0.8, false, 6);
            s.Box(4, 200.5, 190, 14, 3.5, 16, 0xc7c2b8);
        }

        public static void LouvrePyramid(Sculpt s)
        {
            // glass on a diamond grid of steel, 35.42 m at the base and 21.64 m high, with its three small pyramids and pools
            uint glass = 0xa9c6d2, steel = 0x6f7f88, water = 0x4f6f80, stone = 0xcfc8b8;
            const double b = 17.71, h = 21.64;
            s.Taper(0, 0, 0, 2 * b, 2 * b, 0, 0, h, glass);
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90);
                const int n = 9;
                for (int i = 0; i <= n; i++)
                {
                    double t = (double)i / n, e = 0.05;
                    // lines parallel to each sloping edge of this face
                    s.Beam(-b + 2 * b * t, 0, b + e, b * t, h * (1 - t), b * t + e, i == 0 || i == n ? 0.35 : 0.16, steel);
                    s.Beam(b - 2 * b * t, 0, b + e, -b * t, h * (1 - t), b * t + e, i == 0 || i == n ? 0.35 : 0.16, steel);
                }
                s.Pop();
            }
            s.Box(-b - 0.2, 0, 0, 0.5, 3.2, 4.2, 0x39454c);                 // the entrance, on the side towards the Tuileries
            foreach (var p in new[] { new[] { 0.0, 33 }, new[] { 0.0, -33 }, new[] { 33.0, 0 } })
            {
                s.Taper(p[0], 0, p[1], 7.6, 7.6, 0, 0, 4.9, glass);
                for (int f = 0; f < 4; f++) { s.Push(p[0], 0, p[1], f * 90); s.Beam(-3.8, 0, 3.85, 0, 4.9, 0.05, 0.14, steel); s.Beam(-1.9, 0, 3.85, 1.9, 2.45, 1.95, 0.1, steel); s.Beam(1.9, 0, 3.85, -1.9, 2.45, 1.95, 0.1, steel); s.Pop(); }
            }
            // the triangular pools round the big pyramid
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90 + 45);
                double r0 = b * Math.Sqrt(2) + 4;
                s.Poly(new[] { 0, r0, 15, r0 + 15, -15, r0 + 15 }, 0, 0.5, stone);
                s.Poly(new[] { 0, r0 + 1.6, 12.2, r0 + 14, -12.2, r0 + 14 }, 0.3, 0.3, water);
                s.Pop();
            }
        }

        public static void Capitol(Sculpt s)
        {
            // 229 m from north to south (along z); the east front, with its three porticoes and stairs, faces +x. 88 m to the statue.
            uint w = 0xf1efe9, sh = 0xdcd9d0, win = 0x6f7780, roof = 0xb9c0bd, iron = 0xf4f3ee;
            Action<double, double, double, double, double> block = (cx, cz, wx, dz, h) =>
            {
                s.Box(cx, -4, cz, wx, 9.5, dz, sh);                        // rusticated ground floor
                s.Box(cx, 5.5, cz, wx - 0.6, h - 5.5, dz - 0.6, w);
                s.Box(cx, h, cz, wx + 0.8, 1.2, dz + 0.8, w);              // cornice
                s.Box(cx, h + 1.2, cz, wx - 0.4, 1.6, dz - 0.4, sh);       // balustrade
                s.Box(cx, h + 1.2, cz, wx - 3, 1.9, dz - 3, roof);
                foreach (double sx in new[] { -1.0, 1.0 })
                {
                    int n = (int)(dz / 5.2);
                    Row(s, cx + sx * (wx / 2 - 0.2), cz - dz / 2 + 1.5, cx + sx * (wx / 2 - 0.2), cz + dz / 2 - 1.5, 1, n, 1.5, 3, win, 0, 0.4);
                    Row(s, cx + sx * (wx / 2 - 0.5), cz - dz / 2 + 1.5, cx + sx * (wx / 2 - 0.5), cz + dz / 2 - 1.5, 7.5, n, 1.6, 4.6, win, 0, 0.4);
                    Row(s, cx + sx * (wx / 2 - 0.5), cz - dz / 2 + 1.5, cx + sx * (wx / 2 - 0.5), cz + dz / 2 - 1.5, 14, n, 1.6, 3, win, 0, 0.4);
                    n = (int)(wx / 5.2);
                    Row(s, cx - wx / 2 + 1.5, cz + sx * (dz / 2 - 0.5), cx + wx / 2 - 1.5, cz + sx * (dz / 2 - 0.5), 7.5, n, 1.6, 4.6, win, 0, 0.4);
                    Row(s, cx - wx / 2 + 1.5, cz + sx * (dz / 2 - 0.5), cx + wx / 2 - 1.5, cz + sx * (dz / 2 - 0.5), 14, n, 1.6, 3, win, 0, 0.4);
                }
            };
            // a temple front: columns, entablature and pediment, facing +x (dir 1) or -x (dir -1)
            Action<double, double, double, double, int, bool> portico = (x, cz, len, deep, n, ped) =>
            {
                double dir = Math.Sign(x), x0 = x - dir * deep;
                s.Box((x + x0) / 2, -4, cz, deep + 2, 9.5, len + 2, sh);
                Row(s, x - dir * 0.9, cz - len / 2, x - dir * 0.9, cz + len / 2, 5.5, n, 1.5, 12, w, 3);
                s.Box((x + x0) / 2, 17.5, cz, deep, 2.9, len, w);
                s.Box((x + x0) / 2, 20.4, cz, deep + 0.8, 0.8, len + 0.8, w);
                if (ped) s.Gable((x + x0) / 2, 21.2, cz, deep + 0.8, len + 0.8, len * 0.11, w);
                else s.Box((x + x0) / 2, 21.2, cz, deep - 0.4, 1.6, len - 0.4, sh);
            };
            block(0, 0, 54, 107, 20.4);                                    // the old centre building
            block(0, 0, 76, 48, 20.4);                                     // its east and west projections
            foreach (double sz in new[] { -1.0, 1.0 })
            {
                block(0, sz * 64, 22, 24, 20.4);                           // the connecting corridors
                Row(s, 11.6, sz * 54, 11.6, sz * 74, 5.5, 5, 1.2, 12, w, 3);
                Row(s, -11.6, sz * 54, -11.6, sz * 74, 5.5, 5, 1.2, 12, w, 3);
                block(0, sz * 93, 73, 43, 20.4);                           // Senate (north) and House (south) wings
                portico(46, sz * 93, 30, 9.5, 8, true);
                portico(-42, sz * 93, 30, 5.5, 8, false);
                s.Push(0, 0, sz * 93, sz * 90); portico(27, 0, 44, 5.5, 10, false); s.Pop();
                s.Taper(55, -4, sz * 93, 16, 26, 4, 26, 9.5, sh);          // stairs
            }
            portico(50, 0, 49, 12, 8, true);
            Row(s, 44, -24.5, 44, 24.5, 5.5, 8, 1.5, 12, w, 3);
            s.Taper(62, -4, 0, 22, 36, 4, 36, 9.5, sh);
            portico(-44, 0, 46, 6, 10, false);
            // the west terraces towards the Mall
            s.Box(-52, -4, 0, 30, 5, 240, sh); s.Box(-66, -4, 0, 16, 2, 150, sh);
            // the cast-iron dome
            s.Lathe(0, 20.4, 0, new double[] { 20.5, 0, 20.5, 6, 19, 6, 19, 9.6 }, 32, w, 1, 1, new uint[] { w, sh, w });
            Ring(s, 0, 0, 20.6, 21.6, 16, 1.6, 3.2, win, 0, 0.3);
            s.Lathe(0, 30, 0, new double[] { 19.5, 0, 19.5, 0.9, 15, 0.9, 15, 13.4, 19.3, 13.4, 19.5, 15.4, 18.6, 15.4, 18.6, 17, 15.2, 17 }, 36, iron, 1, 1, new uint[] { iron, sh, w, sh, iron, sh, iron, sh });
            Ring(s, 0, 0, 18.3, 30.9, 36, 1.15, 12.5, iron, 3);
            Ring(s, 0, 0, 15.1, 32.5, 36, 1.3, 7.5, win, 1, 0.3, 1, 5, 365);
            s.Lathe(0, 47, 0, new double[] { 15.2, 0, 15.2, 8.6, 16.2, 9, 16.2, 10, 14.6, 10 }, 36, iron);   // attic above the colonnade
            Ring(s, 0, 0, 15.25, 48.2, 36, 1.2, 5.6, win, 1, 0.3, 1, 5, 365);
            Ring(s, 0, 0, 15.5, 47, 36, 0.7, 8.6, w, 0, 0.5);
            var dome = new double[20];
            for (int i = 0; i < 10; i++) { double a = i * (Math.PI / 2 - 0.2) / 9; dome[i * 2] = 14.6 * Math.Cos(a) + 0.2; dome[i * 2 + 1] = 19.5 * Math.Sin(a); }
            s.Lathe(0, 57, 0, dome, 36, iron);
            for (int i = 0; i < 36; i++)                                    // ribs
            {
                double a = Rad(i * 10), c = Math.Cos(a), z = Math.Sin(a);
                var p = new double[30];
                for (int k = 0; k < 10; k++) { p[k * 3] = (dome[k * 2] + 0.15) * c; p[k * 3 + 1] = 57 + dome[k * 2 + 1]; p[k * 3 + 2] = (dome[k * 2] + 0.15) * z; }
                s.Path(p, 0.5, sh);
            }
            double ty = 57 + dome[19];
            s.Lathe(0, ty, 0, new double[] { 5.6, 0, 5.6, 1.4, 3.3, 1.4, 3.3, 8.6, 4.6, 8.8, 4.6, 9.8, 3.6, 9.8, 2.6, 12.4, 1.4, 13, 1.4, 14.2 }, 16, iron, 1, 1, new uint[] { iron, sh, win, iron, sh, iron, iron, iron });
            Ring(s, 0, 0, 4.3, ty + 1.4, 12, 0.6, 7.3, iron, 3);
            // the Statue of Freedom
            s.Lathe(0, ty + 14.2, 0, new double[] { 1.1, 0, 0.8, 1.6, 0.95, 3.2, 0.6, 4.3, 0.45, 4.8, 0.6, 5.3, 0.2, 5.9 }, 8, 0x55604f);
        }

        // ---- signs

        public static void Hollywood(Sculpt s)
        {
            string[] font = { "H101101111101101", "O111101101101111", "L100100100100111", "Y101101010010010", "W101101101111101", "D110101101101110" };
            const string word = "HOLLYWOOD";
            for (int i = 0; i < word.Length; i++)
            {
                string rows = null;
                foreach (string f in font) if (f[0] == word[i]) rows = f;
                double x0 = -51 + i * 11.6;
                s.Stand(-(x0 + 4.65), 0, 5);
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 3; c++)
                        if (rows[1 + r * 3 + c] == '1') s.Box(-(x0 + c * 3.1 + 1.55), 3 + (4 - r) * 2.74, 0, 3.12, 2.76, 0.6, White);
                s.Box(-(x0 + 1.5), -14, -0.6, 0.5, 30, 0.5, 0x7a7a7a); s.Box(-(x0 + 7.8), -14, -0.6, 0.5, 30, 0.5, 0x7a7a7a);
            }
        }

        // ---- the Sydney sails

        static void Sail(Sculpt s, double cx, double y0, double cz, double w, double len, double h, double dir, uint col, uint glass)
        {
            // peak at the front leaning out, a foot on each side, the ridge running back and down to the tail
            double zf = cz + dir * len / 2;
            double[] peak = { cx, y0 + h, zf + dir * h * 0.1 }, tail = { cx, y0 + h * 0.1, cz - dir * len / 2 };
            foreach (double side in new[] { -1.0, 1.0 })
            {
                double[] foot = { cx + side * w / 2, y0, zf - dir * len * 0.3 };
                // the sphere of radius 75 m through the three corners (or as near as the shell allows)
                double[] ab = { foot[0] - peak[0], foot[1] - peak[1], foot[2] - peak[2] }, ac = { tail[0] - peak[0], tail[1] - peak[1], tail[2] - peak[2] };
                double[] nn = { ab[1] * ac[2] - ab[2] * ac[1], ab[2] * ac[0] - ab[0] * ac[2], ab[0] * ac[1] - ab[1] * ac[0] };
                double nl = Math.Sqrt(nn[0] * nn[0] + nn[1] * nn[1] + nn[2] * nn[2]);
                for (int i = 0; i < 3; i++) nn[i] /= nl;
                if (nn[0] * side < 0) for (int i = 0; i < 3; i++) nn[i] = -nn[i];          // pointing out from the hall
                double ab2 = ab[0] * ab[0] + ab[1] * ab[1] + ab[2] * ab[2], ac2 = ac[0] * ac[0] + ac[1] * ac[1] + ac[2] * ac[2];
                double[] abxac = { ab[1] * ac[2] - ab[2] * ac[1], ab[2] * ac[0] - ab[0] * ac[2], ab[0] * ac[1] - ab[1] * ac[0] };
                double den = 2 * (abxac[0] * abxac[0] + abxac[1] * abxac[1] + abxac[2] * abxac[2]);
                double[] t1 = { abxac[1] * ab[2] - abxac[2] * ab[1], abxac[2] * ab[0] - abxac[0] * ab[2], abxac[0] * ab[1] - abxac[1] * ab[0] };
                double[] t2 = { ac[1] * abxac[2] - ac[2] * abxac[1], ac[2] * abxac[0] - ac[0] * abxac[2], ac[0] * abxac[1] - ac[1] * abxac[0] };
                var cc = new double[3];
                for (int i = 0; i < 3; i++) cc[i] = (t1[i] * ac2 + t2[i] * ab2) / den;
                double r2 = cc[0] * cc[0] + cc[1] * cc[1] + cc[2] * cc[2], R = Math.Max(75, Math.Sqrt(r2) * 1.08), back = Math.Sqrt(R * R - r2);
                double[] centre = { peak[0] + cc[0] - nn[0] * back, peak[1] + cc[1] - nn[1] * back, peak[2] + cc[2] - nn[2] * back };
                const int n = 7;
                var grid = new double[n + 1][][];
                for (int i = 0; i <= n; i++)
                {
                    grid[i] = new double[n + 1 - i][];
                    for (int j = 0; i + j <= n; j++)
                    {
                        double u = (double)i / n, v = (double)j / n, q = 1 - u - v;
                        double[] pt = { peak[0] * q + foot[0] * u + tail[0] * v - centre[0], peak[1] * q + foot[1] * u + tail[1] * v - centre[1], peak[2] * q + foot[2] * u + tail[2] * v - centre[2] };
                        double l = Math.Sqrt(pt[0] * pt[0] + pt[1] * pt[1] + pt[2] * pt[2]);
                        grid[i][j] = new[] { centre[0] + pt[0] / l * R, Math.Max(y0, centre[1] + pt[1] / l * R), centre[2] + pt[2] / l * R };
                    }
                }
                for (int i = 0; i < n; i++)
                    for (int j = 0; i + j < n; j++)
                    {
                        uint sc = Sculpt.Shade(col, 0.9 + 0.1 * (1 - (double)i / n));       // the ribs of tile catch the light differently
                        double[] a = grid[i][j], b = grid[i + 1][j], c = grid[i][j + 1];
                        s.Tri(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2], sc);
                        if (i + j + 1 < n) { double[] d = grid[i + 1][j + 1]; s.Tri(b[0], b[1], b[2], d[0], d[1], d[2], c[0], c[1], c[2], sc); }
                    }
                // the glass wall that closes the mouth, set a little way in, and the wall below the shell's edge
                for (int i = 0; i < n; i++)
                {
                    double[] a = grid[i][0], b = grid[i + 1][0];
                    s.Tri(a[0], a[1], a[2], b[0], b[1], b[2], cx, y0, a[2] - dir * 2, glass);
                    s.Tri(b[0], b[1], b[2], cx, y0, b[2] - dir * 2, cx, y0, a[2] - dir * 2, glass);
                }
                for (int j = 0; j < n; j++)
                {
                    double[] a = grid[n - j][j], b = grid[n - j - 1][j + 1];
                    s.Quad(a, b, new[] { b[0], y0, b[2] }, new[] { a[0], y0, a[2] }, Sculpt.Shade(col, 0.82));
                }
            }
        }

        public static void OperaHouse(Sculpt s)
        {
            // three groups of shells on a 185 x 120 m podium of pink granite; every shell is cut from one 75 m sphere
            uint shell = 0xf5f2e8, glass = 0x4a4038, granite = 0xcf9f84;
            s.Box(0, -8, 0, 118, 18, 185, granite);
            s.Box(0, -8, -100, 95, 14, 22, Sculpt.Shade(granite, 0.95));
            for (int i = 0; i < 6; i++) s.Box(0, 4 + i, -112 - i * 1.6, 90, 1, 3, Sculpt.Shade(granite, 0.9 + 0.02 * i));    // the great stair
            foreach (double side in new[] { -1.0, 1.0 })
            {
                double k = side < 0 ? 1.0 : 0.87, x = side * 28;
                Sail(s, x, 10, 46, 48 * k, 70 * k, 57 * k, 1, shell, glass);          // facing the harbour, the tallest
                Sail(s, x, 10, 2, 42 * k, 54 * k, 47 * k, 1, shell, glass);
                Sail(s, x, 10, -32, 34 * k, 40 * k, 35 * k, 1, shell, glass);
                Sail(s, x, 10, -66, 32 * k, 34 * k, 29 * k, -1, shell, glass);        // facing the city
            }
            Sail(s, -34, 10, -92, 17, 20, 15, -1, shell, glass);                      // the restaurant
            Sail(s, -34, 10, -74, 13, 16, 11, 1, shell, glass);
        }

        public static void BigBen(Sculpt s)
        {
            uint stone = 0xc9ae7e, trim = 0xb49a6a, roof = 0x4b525c, face = 0xf3ecd6, win = 0x4f463a, gold = 0xd9b84a;
            // Elizabeth Tower: 12 m square, 96 m to the tip; panelled shaft, the clock stage, belfry, and the roof in two steps
            s.Box(0, 0, 0, 12, 55, 12, stone);
            for (int q = 0; q < 4; q++) s.Box(q == 0 || q == 3 ? 5.7 : -5.7, 0, q < 2 ? 5.7 : -5.7, 1.8, 58, 1.8, trim);
            for (int f = 0; f < 4; f++)
            {
                s.Push(0, 0, 0, f * 90);
                for (int k = 0; k < 6; k++) Row(s, -4.6, 6.05, 4.6, 6.05, 3 + k * 8.4, 3, 1.5, 6.8, Sculpt.Shade(stone, 0.86), 2, 0.2);
                for (int k = 0; k < 6; k++) Row(s, -4.6, 6.15, 4.6, 6.15, 4 + k * 8.4, 3, 0.5, 3, win, 0, 0.1);
                s.Box(0, 55, 6.6, 13.6, 13.5, 1, trim);
                s.Push(0, 61.8, 7.15, 0, 0);
                for (int i = 0; i < 20; i++)
                {
                    double a = i * Math.PI / 10, b = (i + 1) * Math.PI / 10;
                    s.Tri(0, 0, 0.1, 3.45 * Math.Cos(a), 3.45 * Math.Sin(a), 0.1, 3.45 * Math.Cos(b), 3.45 * Math.Sin(b), 0.1, face);
                    s.Beam(3.6 * Math.Cos(a), 3.6 * Math.Sin(a), 0.1, 3.6 * Math.Cos(b), 3.6 * Math.Sin(b), 0.1, 0.35, gold);
                    if (i % 5 == 0 || true) s.Beam(2.7 * Math.Cos(a), 2.7 * Math.Sin(a), 0.16, 3.2 * Math.Cos(a), 3.2 * Math.Sin(a), 0.16, 0.12, Dark);
                }
                s.Beam(0, 0, 0.25, 0, 3.0, 0.25, 0.28, Dark); s.Beam(0, 0, 0.25, 1.7, -0.9, 0.25, 0.38, Dark);
                s.Pop();
                Row(s, -5, 7.15, 5, 7.15, 69.2, 7, 0.8, 2.6, win, 2, 0.2);        // belfry openings
                s.Push(0, 0, 0, 0, 0); s.Gable(0, 80.5, 4.6, 3, 1.6, 2.6, gold, 0); s.Pop();
                s.Pop();
            }
            s.Box(0, 68.5, 0, 13.2, 4, 13.2, trim);
            for (int q = 0; q < 4; q++) s.Lathe(q == 0 || q == 3 ? 6.3 : -6.3, 58, q < 2 ? 6.3 : -6.3, new double[] { 0.9, 0, 0.9, 15, 0, 20 }, 4, trim);
            s.Taper(0, 72.5, 0, 13.6, 13.6, 8.6, 8.6, 7.5, roof);
            s.Box(0, 80, 0, 8.2, 4.6, 8.2, gold); for (int f = 0; f < 4; f++) { s.Push(0, 0, 0, f * 90); Row(s, -3.6, 4.15, 3.6, 4.15, 80.4, 5, 0.8, 3.6, win, 2, 0.1); s.Pop(); }
            s.Taper(0, 84.6, 0, 9, 9, 0.5, 0.5, 9, roof);
            s.Lathe(0, 93.6, 0, new double[] { 0.25, 0, 0.6, 0.5, 0.2, 1, 0.12, 2.4 }, 6, gold);
            // the Palace of Westminster runs south from the tower along the river (river side is +x)
            s.Box(22, 0, -150, 56, 20, 262, stone);
            s.Gable(22, 20, -150, 262, 20, 7, roof, 90);
            s.Box(51, 0, -150, 4, 23, 262, trim);                       // the river front: bays of tall windows between turrets
            for (int k = 0; k < 3; k++) Row(s, 53.1, -278, 53.1, -22, 2 + k * 7, 60, 1.6, 5.4, win, 2, 0.2);
            Row(s, -6.05, -255, -6.05, -110, 4, 30, 1.6, 9, win, 2, 0.2);
            for (int i = 0; i < 13; i++) { s.Box(52.5, 0, -22 - i * 21.4, 3.6, 27, 3.6, stone); s.Lathe(52.5, 27, -22 - i * 21.4, new double[] { 2.2, 0, 0, 7 }, 4, roof); }
            foreach (double z in new[] { -22.0, -150, -278 }) { s.Box(49, 0, z, 12, 30, 14, stone); s.Lathe(49, 30, z, new double[] { 6.5, 0, 3, 8, 3, 9 }, 4, roof); }
            for (int i = 0; i < 9; i++) foreach (double x in new[] { 10.0, 34 }) s.Lathe(x, 20, -40 - i * 27, new double[] { 1.2, 0, 1.2, 9, 0, 14 }, 4, trim);     // the ventilation turrets on the roofs
            s.Box(-18, 0, -70, 26, 20, 74, stone); s.Gable(-18, 20, -70, 74, 26, 14, roof, 90);   // Westminster Hall
            Row(s, -31.05, -105, -31.05, -35, 5, 10, 3, 11, win, 2, 0.3); Panel(s, -18, 4, -32.9, 12, 20, 0, win, 2, 0.4);
            foreach (double sx in new[] { -1.0, 1.0 }) { s.Box(-18 + sx * 10.5, 0, -32, 5, 30, 5, stone); s.Lathe(-18 + sx * 10.5, 30, -32, new double[] { 3, 0, 0, 8 }, 4, roof); }
            // the Central Tower: an octagonal lantern and spire, 91 m
            s.Lathe(22, 0, -150, new double[] { 11, 0, 11, 34, 9, 34, 9, 46, 9.8, 46.4, 9.8, 48, 7.5, 48, 1.2, 84, 0, 91 }, 8, stone, 1, 1, new uint[] { stone, trim, stone, trim, trim, trim, trim, trim });
            Ring(s, 22, -150, 9.05, 35.5, 8, 3, 9, win, 2, 0.3, 1, 22.5, 382.5);
            // Victoria Tower, 98.5 m, at the south-west corner
            s.Box(-2, 0, -270, 23, 86, 23, stone);
            for (int f = 0; f < 4; f++)
            {
                s.Push(-2, 0, -270, f * 90);
                Panel(s, 0, 0, 11.6, 9, 20, 0, win, 2, 0.3);
                Row(s, -7, 11.6, 7, 11.6, 26, 3, 3, 18, Sculpt.Shade(stone, 0.85), 2, 0.3); Row(s, -7, 11.75, 7, 11.75, 28, 3, 1.4, 13, win, 2, 0.2);
                Row(s, -7, 11.6, 7, 11.6, 50, 3, 3, 16, Sculpt.Shade(stone, 0.85), 2, 0.3); Row(s, -7, 11.75, 7, 11.75, 52, 3, 1.4, 11, win, 2, 0.2);
                Row(s, -8, 11.6, 8, 11.6, 71, 6, 1.4, 11, win, 2, 0.2);
                s.Pop();
            }
            for (int q = 0; q < 4; q++) { double x = -2 + (q == 0 || q == 3 ? 11.2 : -11.2), z = -270 + (q < 2 ? 11.2 : -11.2); s.Lathe(x, 0, z, new double[] { 2.2, 0, 2.2, 90, 2.6, 90.5, 2.6, 92, 1.6, 92, 0, 99 }, 8, trim); s.Beam(x, 99, z, x, 101, z, 0.2, gold); }
            s.Box(-2, 86, -270, 24, 2, 24, trim);
            s.Taper(-2, 88, -270, 19, 19, 11, 11, 5, roof);
            s.Beam(-2, 93, -270, -2, 108, -270, 0.5, trim); s.Box(-2, 104, -269.6, 3.6, 2.2, 0.1, 0xb23a3a);
        }

        public static void Pisa(Sculpt s)
        {
            // 55.9 m of white marble: a blind arcade below, six open galleries, and the smaller bell chamber on top
            uint m = 0xece8dd, cornice = Marble, inner = 0xbdb6a6, dark = 0x6b665d;
            s.Push(0, 0, 0, 90, 3.97);                        // leans a little under four degrees, to the south
            s.Lathe(0, 0, 0, new double[] { 7.75, -3, 7.75, 0.6, 7.55, 0.6, 7.55, 10.6, 8.2, 10.9, 8.2, 11.8, 6.1, 11.8 }, 30, m, 1, 1, new uint[] { cornice, cornice, m, cornice, cornice, cornice });
            Ring(s, 0, 0, 7.6, 0.6, 15, 0.7, 8, cornice, 3);
            Ring(s, 0, 0, 7.5, 0.6, 15, 2.3, 9.4, inner, 1, 0.3, 1, 12, 372);
            Panel(s, 0, 0.6, 7.6, 1.6, 4.4, 0, dark, 1, 0.4);
            for (int k = 0; k < 6; k++)
            {
                double y = 11.8 + k * 6.15;
                s.Lathe(0, y, 0, new double[] { 6.1, 0, 6.1, 6.15 }, 30, inner);                                     // the wall behind the gallery
                Ring(s, 0, 0, 6.15, y + 0.3, 15, 1.1, 3.6, dark, 1, 0.2, 1, 12, 372);
                Ring(s, 0, 0, 7.45, y, 30, 0.42, 4.1, cornice, 3);
                s.Lathe(0, y + 4.1, 0, new double[] { 7.2, 0, 7.75, 0, 7.75, 1.3, 8.15, 1.5, 8.15, 2.05, 6.1, 2.05 }, 30, m, 1, 1, new uint[] { m, m, cornice, cornice, cornice });
                Ring(s, 0, 0, 7.72, y + 3.3, 30, 1.05, 1.7, inner, 1, 0.12, 1, 6, 366);                          // the arch heads between the columns
            }
            double top = 11.8 + 6 * 6.15;
            s.Lathe(0, top, 0, new double[] { 6.3, 0, 6.3, 6.2, 6.7, 6.4, 6.7, 7, 5.6, 7, 5.6, 6.2 }, 24, m, 1, 1, new uint[] { m, cornice, cornice, cornice, inner });
            s.Lathe(0, top + 6.2, 0, new double[] { 0, 0, 5.6, 0 }, 24, inner);
            Ring(s, 0, 0, 6.3, top + 0.4, 6, 2.4, 4.9, dark, 1, 0.3);
            Ring(s, 0, 0, 6.35, top + 0.2, 12, 0.35, 4.4, cornice, 3, 0.5, 1, 15, 375);
            s.Beam(0, top + 6.2, 0, 0, top + 9, 0, 0.12, Dark);
            s.Pop();
        }

    }
}
