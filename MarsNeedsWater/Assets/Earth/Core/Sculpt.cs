using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// A small kit for building landmarks out of simple solids, working in real metres
    /// (x east, y up, z north before the landmark is turned to its true bearing).
    /// Finish() shrinks the result to game size, 1:5 in every direction, so a landmark keeps its true proportions.
    /// </summary>
    public sealed class Sculpt
    {
        readonly List<double> p = new List<double>();   // triangles, 9 numbers each
        readonly List<uint> c = new List<uint>();        // one colour per triangle
        readonly Stack<double[]> stack = new Stack<double[]>();
        readonly List<double> stands = new List<double> { 0, 0, 0 };   // x, z, radius of each part that rests on the ground by itself
        readonly List<int> standStart = new List<int> { 0 };          // first triangle of each such part
        double[] m = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };   // 3 x 4 transform

        // ---- placing parts

        /// <summary>Move, then turn clockwise (seen from above) by yaw degrees; applies to everything until Pop.</summary>
        public void Push(double tx, double ty, double tz, double yaw = 0, double leanDeg = 0)
        {
            stack.Push(m);
            double a = yaw * Geo.Deg, cs = Math.Cos(a), sn = Math.Sin(a);
            double[] r = { cs, 0, sn, tx, 0, 1, 0, ty, -sn, 0, cs, tz };
            if (leanDeg != 0)
            {
                double l = leanDeg * Geo.Deg, cl = Math.Cos(l), sl = Math.Sin(l);
                double[] lean = { cl, sl, 0, 0, -sl, cl, 0, 0, 0, 0, 1, 0 };   // tips towards +x
                r = Mul(r, lean);
            }
            m = Mul(m, r);
        }

        public void Pop() { m = stack.Pop(); }

        /// <summary>
        /// Everything added from here on is one part that rests on the ground under (x, z) by itself,
        /// so a wide site on uneven ground (the pyramids, a sign on a hillside) has no piece left in the air.
        /// radius is how far the part reaches; 0 works it out from the shape.
        /// </summary>
        public void Stand(double x, double z, double radius = 0)
        {
            double wx = m[0] * x + m[2] * z + m[3], wz = m[8] * x + m[10] * z + m[11];
            if (c.Count == standStart[standStart.Count - 1])
            {
                int k = (standStart.Count - 1) * 3;
                stands[k] = wx; stands[k + 1] = wz; stands[k + 2] = radius;
                return;
            }
            stands.Add(wx); stands.Add(wz); stands.Add(radius);
            standStart.Add(c.Count);
        }

        static double[] Mul(double[] a, double[] b)
        {
            var o = new double[12];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) o[i * 4 + j] = a[i * 4] * b[j] + a[i * 4 + 1] * b[4 + j] + a[i * 4 + 2] * b[8 + j];
                o[i * 4 + 3] = a[i * 4] * b[3] + a[i * 4 + 1] * b[7] + a[i * 4 + 2] * b[11] + a[i * 4 + 3];
            }
            return o;
        }

        void V(double x, double y, double z)
        {
            p.Add(m[0] * x + m[1] * y + m[2] * z + m[3]);
            p.Add(m[4] * x + m[5] * y + m[6] * z + m[7]);
            p.Add(m[8] * x + m[9] * y + m[10] * z + m[11]);
        }

        public void Tri(double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz, uint col)
        {
            V(ax, ay, az); V(bx, by, bz); V(cx, cy, cz); c.Add(col);
        }

        public void Quad(double[] a, double[] b, double[] cc, double[] d, uint col)
        {
            Tri(a[0], a[1], a[2], b[0], b[1], b[2], cc[0], cc[1], cc[2], col);
            Tri(a[0], a[1], a[2], cc[0], cc[1], cc[2], d[0], d[1], d[2], col);
        }

        static double[] P(double x, double y, double z) { return new[] { x, y, z }; }

        public static uint Shade(uint col, double f)
        {
            int r = (int)Math.Min(255, ((col >> 16) & 255) * f), g = (int)Math.Min(255, ((col >> 8) & 255) * f), b = (int)Math.Min(255, (col & 255) * f);
            return (uint)((r << 16) | (g << 8) | b);
        }

        // ---- solids

        /// <summary>A block standing on y0, centred on cx, cz. w runs east-west, d north-south (before yaw).</summary>
        public void Box(double cx, double y0, double cz, double w, double h, double d, uint col, double yaw = 0)
        {
            Taper(cx, y0, cz, w, d, w, d, h, col, yaw);
        }

        /// <summary>A block whose top (w1 x d1) differs from its base (w0 x d0); zero top makes a pyramid.</summary>
        public void Taper(double cx, double y0, double cz, double w0, double d0, double w1, double d1, double h, uint col, double yaw = 0, uint topCol = 0)
        {
            Push(cx, y0, cz, yaw);
            double a = w0 / 2, b = d0 / 2, e = w1 / 2, f = d1 / 2;
            double[][] lo = { P(-a, 0, -b), P(a, 0, -b), P(a, 0, b), P(-a, 0, b) };
            double[][] hi = { P(-e, h, -f), P(e, h, -f), P(e, h, f), P(-e, h, f) };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Quad(lo[i], lo[j], hi[j], hi[i], i % 2 == 0 ? col : Shade(col, 0.93));
            }
            if (w1 > 0 && d1 > 0) Quad(hi[0], hi[1], hi[2], hi[3], topCol != 0 ? topCol : Shade(col, 1.05));
            Quad(lo[0], lo[1], lo[2], lo[3], col);
            Pop();
        }

        /// <summary>A shape turned on a lathe: pairs of (radius, height). sx and sz stretch it into an oval.</summary>
        public void Lathe(double cx, double y0, double cz, double[] ry, int sides, uint col, double sx = 1, double sz = 1, uint[] cols = null)
        {
            Push(cx, y0, cz);
            int n = ry.Length / 2;
            for (int k = 0; k + 1 < n; k++)
            {
                double r0 = ry[k * 2], h0 = ry[k * 2 + 1], r1 = ry[k * 2 + 2], h1 = ry[k * 2 + 3];
                uint cc = cols != null ? cols[Math.Min(k, cols.Length - 1)] : col;
                for (int s = 0; s < sides; s++)
                {
                    double a0 = s * 2 * Math.PI / sides, a1 = (s + 1) * 2 * Math.PI / sides;
                    double c0 = Math.Cos(a0) * sx, s0 = Math.Sin(a0) * sz, c1 = Math.Cos(a1) * sx, s1 = Math.Sin(a1) * sz;
                    uint sc = Shade(cc, 0.94 + 0.06 * Math.Cos(a0 * 2));
                    if (r0 > 0) Tri(r0 * c0, h0, r0 * s0, r0 * c1, h0, r0 * s1, r1 * c1, h1, r1 * s1, sc);
                    if (r1 > 0) Tri(r0 * c0, h0, r0 * s0, r1 * c1, h1, r1 * s1, r1 * c0, h1, r1 * s0, sc);
                }
            }
            Pop();
        }

        public void Cyl(double cx, double y0, double cz, double r, double h, int sides, uint col)
        {
            Lathe(cx, y0, cz, new[] { 0, 0, r, 0, r, h, 0, h }, sides, col);
        }

        public void Cone(double cx, double y0, double cz, double r, double h, int sides, uint col)
        {
            Lathe(cx, y0, cz, new[] { r, 0, 0, h }, sides, col);
        }

        /// <summary>A ball, or a dome when half is true. tall stretches it upwards.</summary>
        public void Ball(double cx, double cy, double cz, double r, uint col, double tall = 1, bool half = false, int sides = 14)
        {
            int rings = half ? 5 : 9;
            var ry = new double[(rings + 1) * 2];
            for (int i = 0; i <= rings; i++)
            {
                double a = half ? i * Math.PI / 2 / rings : -Math.PI / 2 + i * Math.PI / rings;
                ry[i * 2] = Math.Max(0, r * Math.Cos(a)); ry[i * 2 + 1] = r * tall * Math.Sin(a);
            }
            ry[rings * 2] = 0;
            if (!half) ry[0] = 0;
            Lathe(cx, cy, cz, ry, sides, col);
        }

        /// <summary>An onion dome standing on y0.</summary>
        public void Onion(double cx, double y0, double cz, double r, uint col, int sides = 12)
        {
            Lathe(cx, y0, cz, new[] { r * 0.75, 0, r * 1.05, r * 0.35, r * 1.2, r * 0.8, r * 1.1, r * 1.3, r * 0.75, r * 1.9, r * 0.35, r * 2.5, r * 0.1, r * 3.0, 0, r * 3.5 }, sides, col);
        }

        /// <summary>A square bar from a to b.</summary>
        public void Beam(double ax, double ay, double az, double bx, double by, double bz, double t, uint col)
        {
            double dx = bx - ax, dy = by - ay, dz = bz - az, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-6) return;
            dx /= len; dy /= len; dz /= len;
            // two directions square to the bar
            double ux, uy, uz;
            if (Math.Abs(dy) < 0.9) { ux = -dz; uy = 0; uz = dx; } else { ux = 1; uy = 0; uz = 0; }
            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz); ux /= ul; uy /= ul; uz /= ul;
            double vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
            double h = t / 2;
            var lo = new double[4][]; var hi = new double[4][];
            for (int i = 0; i < 4; i++)
            {
                double su = (i == 0 || i == 3) ? -h : h, sv = i < 2 ? -h : h;
                double ox = ux * su + vx * sv, oy = uy * su + vy * sv, oz = uz * su + vz * sv;
                lo[i] = P(ax + ox, ay + oy, az + oz); hi[i] = P(bx + ox, by + oy, bz + oz);
            }
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; Quad(lo[i], lo[j], hi[j], hi[i], i % 2 == 0 ? col : Shade(col, 0.9)); }
            Quad(lo[0], lo[1], lo[2], lo[3], col); Quad(hi[0], hi[1], hi[2], hi[3], col);
        }

        /// <summary>Bars joined end to end through a list of x,y,z points.</summary>
        public void Path(double[] pts, double t, uint col)
        {
            for (int i = 0; i + 5 < pts.Length; i += 3) Beam(pts[i], pts[i + 1], pts[i + 2], pts[i + 3], pts[i + 4], pts[i + 5], t, col);
        }

        /// <summary>A ridge roof: w along x, d along z, ridge along x, h high.</summary>
        public void Gable(double cx, double y0, double cz, double w, double d, double h, uint col, double yaw = 0)
        {
            Push(cx, y0, cz, yaw);
            double a = w / 2, b = d / 2;
            Quad(P(-a, 0, -b), P(a, 0, -b), P(a, h, 0), P(-a, h, 0), col);
            Quad(P(a, 0, b), P(-a, 0, b), P(-a, h, 0), P(a, h, 0), Shade(col, 0.92));
            Tri(-a, 0, -b, -a, h, 0, -a, 0, b, Shade(col, 0.85));
            Tri(a, 0, -b, a, 0, b, a, h, 0, Shade(col, 0.85));
            Pop();
        }

        /// <summary>A flat outline (x,z pairs) raised from y0 by h.</summary>
        public void Poly(double[] xz, double y0, double h, uint col)
        {
            int n = xz.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Quad(P(xz[i * 2], y0, xz[i * 2 + 1]), P(xz[j * 2], y0, xz[j * 2 + 1]), P(xz[j * 2], y0 + h, xz[j * 2 + 1]), P(xz[i * 2], y0 + h, xz[i * 2 + 1]), i % 2 == 0 ? col : Shade(col, 0.92));
            }
            List<int> t = Earcut.Triangulate(xz, null);
            for (int i = 0; i + 2 < t.Count; i += 3)
                Tri(xz[t[i] * 2], y0 + h, xz[t[i] * 2 + 1], xz[t[i + 1] * 2], y0 + h, xz[t[i + 1] * 2 + 1], xz[t[i + 2] * 2], y0 + h, xz[t[i + 2] * 2 + 1], Shade(col, 1.05));
        }

        // ---- result

        public int Triangles { get { return c.Count; } }

        /// <summary>
        /// Turn to the true bearing and shrink to game size (the same 1:5 up as along, so the proportions are
        /// the real ones). Every triangle is given both faces, so a shape can never show up inside out.
        /// Colours go into a 16 x 16 swatch (returned in palette).
        /// </summary>
        public LandmarkMesh Finish(double yaw)
        {
            var mesh = new MeshData();
            var index = new Dictionary<uint, int>();
            var pal = new List<uint>();
            double a = yaw * Geo.Deg, cs = Math.Cos(a), sn = Math.Sin(a);
            var q = new float[9];
            int parts = standStart.Count;
            var result = new LandmarkMesh { Mesh = mesh, Start = new int[parts + 1], Stand = new float[parts * 3] };
            int part = -1;
            for (int t = 0; t < c.Count; t++)
            {
                while (part + 1 < parts && standStart[part + 1] <= t)
                {
                    part++;
                    result.Start[part] = mesh.Count;
                    double sx = stands[part * 3], sz = stands[part * 3 + 1], r = stands[part * 3 + 2];
                    if (r <= 0)
                    {
                        int end = part + 1 < parts ? standStart[part + 1] : c.Count;
                        for (int k = t * 9; k < end * 9; k += 3) r = Math.Max(r, Math.Max(Math.Abs(p[k] - sx), Math.Abs(p[k + 2] - sz)));
                    }
                    result.Stand[part * 3] = (float)((sx * cs + sz * sn) / Geo.Scale);
                    result.Stand[part * 3 + 1] = (float)((-sx * sn + sz * cs) / Geo.Scale);
                    result.Stand[part * 3 + 2] = (float)(r / Geo.Scale);
                }
                for (int k = 0; k < 3; k++)
                {
                    double x = p[t * 9 + k * 3], y = p[t * 9 + k * 3 + 1], z = p[t * 9 + k * 3 + 2];
                    q[k * 3] = (float)((x * cs + z * sn) / Geo.Scale);
                    q[k * 3 + 1] = (float)(y / Geo.Scale);
                    q[k * 3 + 2] = (float)((-x * sn + z * cs) / Geo.Scale);
                }
                float ux = q[3] - q[0], uy = q[4] - q[1], uz = q[5] - q[2], vx = q[6] - q[0], vy = q[7] - q[1], vz = q[8] - q[2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-12f) continue;
                nx /= len; ny /= len; nz /= len;
                int ci;
                if (!index.TryGetValue(c[t], out ci))
                {
                    if (pal.Count < 256) { ci = pal.Count; pal.Add(c[t]); index[c[t]] = ci; } else ci = 255;
                }
                float cu = (ci % 16 + 0.5f) / 16f, cv = (ci / 16 + 0.5f) / 16f;
                int i0 = mesh.Add(q[0], q[1], q[2], nx, ny, nz, cu, cv), i1 = mesh.Add(q[3], q[4], q[5], nx, ny, nz, cu, cv), i2 = mesh.Add(q[6], q[7], q[8], nx, ny, nz, cu, cv);
                mesh.I.Add(i0); mesh.I.Add(i1); mesh.I.Add(i2);
                int j0 = mesh.Add(q[0], q[1], q[2], -nx, -ny, -nz, cu, cv), j1 = mesh.Add(q[3], q[4], q[5], -nx, -ny, -nz, cu, cv), j2 = mesh.Add(q[6], q[7], q[8], -nx, -ny, -nz, cu, cv);
                mesh.I.Add(j0); mesh.I.Add(j2); mesh.I.Add(j1);
            }
            while (part + 1 < parts) { part++; result.Start[part] = mesh.Count; }
            result.Start[parts] = mesh.Count;
            var palette = new uint[256];
            for (int i = 0; i < 256; i++) palette[i] = i < pal.Count ? pal[i] : 0x808080;
            result.Palette = palette;
            return result;
        }
    }

    /// <summary>A finished landmark at game size. Each part (Start[i] .. Start[i+1] vertices) rests on the ground under Stand[i*3], Stand[i*3+1] by itself.</summary>
    public sealed class LandmarkMesh
    {
        public MeshData Mesh;
        public uint[] Palette;
        public int[] Start;
        public float[] Stand;     // x, z, reach of each part, game metres, in the landmark's own east/north
        public int Parts { get { return Start.Length - 1; } }
    }
}
