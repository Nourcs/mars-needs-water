using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Lays flat shapes on the ground of a tile, the way paint lies on a floor. Roads, pavements, lines and
    /// crossings are all drawn with it (Core/RoadNet.cs decides what to draw; this only draws).
    ///
    /// Every shape is cut along the ground's own triangles wherever the ground bends, so it lies exactly on the
    /// surface with no gaps under it and no corners sunk into a slope. Shapes go into numbered layers; the layers
    /// are written out lowest first and the shader draws without a depth write, so a higher layer simply paints
    /// over a lower one. That is what joins streets at a corner: all the kerbs first, then all the asphalt.
    ///
    /// What each vertex carries (EarthStreet.shader reads it):
    ///   uv0  x, y: where the point is inside its shape, in metres from the shape's middle
    ///   uv1  x, y: half the shape's size; z: how round its corners are (m; below zero: the shape is all that lies
    ///        outside a circle of that radius, for rounded street corners); w: how soft its edge is (m, 0 = crisp)
    ///   uv2  x: colour (r*65536 + g*256 + b); y: what it is made of (Ink kinds); z: a random number; w: spare
    /// </summary>
    public sealed class RoadPaint
    {
        // what a shape is made of (EarthStreet.shader has the same numbers)
        public const int Flat = 0, Asphalt = 1, Slabs = 2, Cobbles = 3, Earth = 4, Gravel = 5, Steps = 6, Kerb = 7, Line = 8, Track = 9, Verge = 10, Sleepers = 11, Steel = 12;

        public const float Lift = 0.05f;       // game metres above the ground
        const double Flatness = 0.025;          // the ground counts as flat where it strays less than this from a plane

        public struct Ink
        {
            public float Hx, Hy, Round, Soft, Seed, Extra;
            public uint Colour;
            public int Kind;

            public static Ink Band(float halfWidth, uint colour, int kind, float seed = 0, float soft = 0, float extra = 0)
            {
                return new Ink { Hx = halfWidth, Hy = 1e5f, Colour = colour, Kind = kind, Seed = seed, Soft = soft, Extra = extra };
            }

            public static Ink Disc(float radius, uint colour, int kind, float seed = 0, float soft = 0, float extra = 0)
            {
                return new Ink { Hx = radius, Hy = radius, Round = radius, Colour = colour, Kind = kind, Seed = seed, Soft = soft, Extra = extra };
            }

            public static Ink Box(float hx, float hy, float round, uint colour, int kind, float seed = 0)
            {
                return new Ink { Hx = hx, Hy = hy, Round = round, Colour = colour, Kind = kind, Seed = seed };
            }
        }

        /// <summary>For testing: when set, how many vertices each layer came to.</summary>
        public static int[] Counts;

        struct V { public double X, Y; public float A, B; }

        readonly TileResult t;
        readonly double S;
        readonly int G = TileResult.Grid;
        readonly MeshData[] layers;
        readonly float[] heights;               // the surface at every grid point
        readonly List<V[]> pool = new List<V[]>();
        readonly Dictionary<(int, int, int, int), int>[] weld;
        readonly Ink[] last;
        readonly int[] corner = new int[16];

        public RoadPaint(TileResult tile, int layerCount)
        {
            t = tile; S = tile.Size;
            layers = new MeshData[layerCount];
            weld = new Dictionary<(int, int, int, int), int>[layerCount];
            last = new Ink[layerCount];
            if (tile.Pos == null) return;
            heights = new float[(G + 1) * (G + 1)];
            for (int j = 0; j <= G; j++)
                for (int i = 0; i <= G; i++)
                {
                    float x, y, z;
                    t.Surface((double)i / G, (double)j / G, out x, out y, out z);
                    heights[j * (G + 1) + i] = y;
                }
        }

        MeshData Layer(int k)
        {
            MeshData m = layers[k];
            if (m == null) layers[k] = m = new MeshData { X = new List<float>(), Y = new List<float>() };
            return m;
        }

        /// <summary>Add everything drawn so far to a mesh, lowest layer first, and start afresh.</summary>
        public void Flush(MeshData into)
        {
            if (into.X == null) { into.X = new List<float>(); into.Y = new List<float>(); }
            for (int k = 0; k < layers.Length; k++)
            {
                MeshData m = layers[k];
                if (m == null) continue;
                if (Counts != null) Counts[k] += m.Count;
                int b = into.Count;
                into.P.AddRange(m.P); into.N.AddRange(m.N); into.U.AddRange(m.U); into.X.AddRange(m.X); into.Y.AddRange(m.Y);
                foreach (int i in m.I) into.I.Add(b + i);
                layers[k] = null;
                if (weld[k] != null) weld[k].Clear();
            }
        }

        // ------------------------------------------------------------ shapes on the ground

        /// <summary>A triangle on the ground. x, y are tile metres (east, south); a, b the place inside the shape.</summary>
        public void Tri(int layer, in Ink ink, double x0, double y0, float a0, float b0, double x1, double y1, float a1, float b1, double x2, double y2, float a2, float b2)
        {
            V[] p = Take();
            double k = G / S;
            p[0] = new V { X = x0 * k, Y = y0 * k, A = a0, B = b0 };
            p[1] = new V { X = x1 * k, Y = y1 * k, A = a1, B = b1 };
            p[2] = new V { X = x2 * k, Y = y2 * k, A = a2, B = b2 };
            Ground(layer, ink, p, 3);
            Give(p);
        }

        /// <summary>A four-cornered shape on the ground, corners given in order round it.</summary>
        public void Quad(int layer, in Ink ink, double x0, double y0, float a0, float b0, double x1, double y1, float a1, float b1,
                         double x2, double y2, float a2, float b2, double x3, double y3, float a3, float b3)
        {
            // two triangles rather than one four-cornered piece: a bent quad would not carry a, b evenly
            Tri(layer, ink, x0, y0, a0, b0, x1, y1, a1, b1, x2, y2, a2, b2);
            Tri(layer, ink, x0, y0, a0, b0, x2, y2, a2, b2, x3, y3, a3, b3);
        }

        /// <summary>A round spot on the ground (the end of a road, the middle of a junction).</summary>
        public void Disc(int layer, in Ink ink, double x, double y)
        {
            float r = ink.Hx;
            if (x < -r || y < -r || x > S + r || y > S + r) return;
            // a square round it; the shader trims it to a circle
            Quad(layer, ink, x - r, y - r, -r, -r, x + r, y - r, r, -r, x + r, y + r, r, r, x - r, y + r, -r, r);
        }

        /// <summary>A box lying at any angle: centre, the direction of its long side (unit), half sizes across and along.</summary>
        public void Bar(int layer, in Ink ink, double x, double y, double dx, double dy)
        {
            double nx = dy, ny = -dx;
            float hx = ink.Hx, hy = ink.Hy;
            Quad(layer, ink,
                x + nx * hx - dx * hy, y + ny * hx - dy * hy, hx, -hy,
                x + nx * hx + dx * hy, y + ny * hx + dy * hy, hx, hy,
                x - nx * hx + dx * hy, y - ny * hx + dy * hy, -hx, hy,
                x - nx * hx - dx * hy, y - ny * hx - dy * hy, -hx, -hy);
        }

        // ------------------------------------------------------------ shapes in the air (bridge decks)

        /// <summary>A four-cornered shape whose corners are given where they are in the tile (x, y, z), not on the ground.</summary>
        public void QuadAt(int layer, in Ink ink, float[] p0, float a0, float b0, float[] p1, float a1, float b1, float[] p2, float a2, float b2, float[] p3, float a3, float b3)
        {
            MeshData m = Layer(layer);
            int i0 = Put(m, ink, p0[0], p0[1], p0[2], 0, 1, 0, a0, b0), i1 = Put(m, ink, p1[0], p1[1], p1[2], 0, 1, 0, a1, b1);
            int i2 = Put(m, ink, p2[0], p2[1], p2[2], 0, 1, 0, a2, b2), i3 = Put(m, ink, p3[0], p3[1], p3[2], 0, 1, 0, a3, b3);
            m.Tri(i0, i1, i2, 0, 1, 0); m.Tri(i0, i2, i3, 0, 1, 0);
        }

        static int Put(MeshData m, in Ink ink, float x, float y, float z, float nx, float ny, float nz, float a, float b)
        {
            m.X.Add(ink.Hx); m.X.Add(ink.Hy); m.X.Add(ink.Round); m.X.Add(ink.Soft);
            m.Y.Add(ink.Colour); m.Y.Add(ink.Kind); m.Y.Add(ink.Seed); m.Y.Add(ink.Extra);
            return m.Add(x, y, z, nx, ny, nz, a, b);
        }

        // ------------------------------------------------------------ fitting a shape to the ground

        V[] Take()
        {
            if (pool.Count == 0) return new V[16];
            V[] p = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            return p;
        }

        void Give(V[] p) { pool.Add(p); }

        /// <summary>Keep the part of a shape where nx*x + ny*y is at most c. Returns how many corners are left.</summary>
        static int Cut(V[] src, int n, V[] dst, double nx, double ny, double c)
        {
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                V a = src[i], b = src[(i + 1) % n];
                double da = nx * a.X + ny * a.Y - c, db = nx * b.X + ny * b.Y - c;
                if (da <= 0) { if (m < dst.Length) dst[m++] = a; }
                if ((da < 0 && db > 0) || (da > 0 && db < 0))
                {
                    double f = da / (da - db);
                    if (m < dst.Length) dst[m++] = new V { X = a.X + (b.X - a.X) * f, Y = a.Y + (b.Y - a.Y) * f, A = a.A + (b.A - a.A) * (float)f, B = a.B + (b.B - a.B) * (float)f };
                }
            }
            return m;
        }

        /// <summary>A shape in grid units (0..G each way): trim it to the tile, then cut it up wherever the ground bends.</summary>
        void Ground(int layer, in Ink ink, V[] p, int n)
        {
            if (heights == null) return;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (p[i].X < minX) minX = p[i].X; if (p[i].X > maxX) maxX = p[i].X;
                if (p[i].Y < minY) minY = p[i].Y; if (p[i].Y > maxY) maxY = p[i].Y;
            }
            if (maxX <= 0 || maxY <= 0 || minX >= G || minY >= G) return;
            V[] w = Take(), q = Take(), sw;
            Array.Copy(p, w, n);
            if (minX < 0) { n = Cut(w, n, q, -1, 0, 0); sw = w; w = q; q = sw; }
            if (maxX > G) { n = Cut(w, n, q, 1, 0, G); sw = w; w = q; q = sw; }
            if (minY < 0) { n = Cut(w, n, q, 0, -1, 0); sw = w; w = q; q = sw; }
            if (maxY > G) { n = Cut(w, n, q, 0, 1, G); sw = w; w = q; q = sw; }
            if (n >= 3) Fit(layer, ink, w, n);
            Give(w); Give(q);
        }

        void Fit(int layer, in Ink ink, V[] p, int n)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (p[i].X < minX) minX = p[i].X; if (p[i].X > maxX) maxX = p[i].X;
                if (p[i].Y < minY) minY = p[i].Y; if (p[i].Y > maxY) maxY = p[i].Y;
            }
            const double E = 1e-7;
            int i0 = Math.Max(0, (int)Math.Floor(minX + E)), i1 = Math.Min(G, (int)Math.Ceiling(maxX - E));
            int j0 = Math.Max(0, (int)Math.Floor(minY + E)), j1 = Math.Min(G, (int)Math.Ceiling(maxY - E));
            if (i1 <= i0) i1 = Math.Min(G, i0 + 1);
            if (j1 <= j0) j1 = Math.Min(G, j0 + 1);
            if (i1 <= i0) i0 = i1 - 1;
            if (j1 <= j0) j0 = j1 - 1;
            if (Level(i0, i1, j0, j1)) { Out(layer, ink, p, n); return; }
            V[] a = Take(), b = Take();
            int na, nb;
            if (i1 - i0 == 1 && j1 - j0 == 1)
            {
                // one grid square that is not flat: its two triangles meet along the line x + y = i0 + j0 + 1
                double c = i0 + j0 + 1;
                na = Cut(p, n, a, 1, 1, c); nb = Cut(p, n, b, -1, -1, -c);
                if (na >= 3) Out(layer, ink, a, na);
                if (nb >= 3) Out(layer, ink, b, nb);
            }
            else if (i1 - i0 >= j1 - j0)
            {
                int mid = (i0 + i1) / 2;
                na = Cut(p, n, a, 1, 0, mid); nb = Cut(p, n, b, -1, 0, -mid);
                if (na >= 3) Fit(layer, ink, a, na);
                if (nb >= 3) Fit(layer, ink, b, nb);
            }
            else
            {
                int mid = (j0 + j1) / 2;
                na = Cut(p, n, a, 0, 1, mid); nb = Cut(p, n, b, 0, -1, -mid);
                if (na >= 3) Fit(layer, ink, a, na);
                if (nb >= 3) Fit(layer, ink, b, nb);
            }
            Give(a); Give(b);
        }

        /// <summary>Is the ground over this block of grid squares flat enough to lay a shape on in one piece?</summary>
        bool Level(int i0, int i1, int j0, int j1)
        {
            int W = G + 1;
            double h00 = heights[j0 * W + i0], hx = (heights[j0 * W + i1] - h00) / (i1 - i0), hy = (heights[j1 * W + i0] - h00) / (j1 - j0);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (Math.Abs(heights[j * W + i] - (h00 + hx * (i - i0) + hy * (j - j0))) > Flatness) return false;
            return true;
        }

        void Out(int layer, in Ink ink, V[] p, int n)
        {
            // drop slivers
            double area = 0;
            for (int i = 1; i + 1 < n; i++) area += (p[i].X - p[0].X) * (p[i + 1].Y - p[0].Y) - (p[i + 1].X - p[0].X) * (p[i].Y - p[0].Y);
            if (Math.Abs(area) < 1e-7) return;
            MeshData m = Layer(layer);
            // corners shared with the piece next door (same shape, same spot) are stored once
            Dictionary<(int, int, int, int), int> seen = weld[layer];
            if (seen == null) weld[layer] = seen = new Dictionary<(int, int, int, int), int>();
            if (!Same(ink, last[layer])) { seen.Clear(); last[layer] = ink; }
            for (int i = 0; i < n; i++)
            {
                var key = ((int)Math.Round(p[i].X * 8192), (int)Math.Round(p[i].Y * 8192), (int)Math.Round(p[i].A * 2000), (int)Math.Round(p[i].B * 2000));
                int at;
                if (!seen.TryGetValue(key, out at))
                {
                    double u = p[i].X / G, v = p[i].Y / G;
                    float x, y, z, nx, ny, nz;
                    t.Surface(u, v, out x, out y, out z);
                    t.Slope(u, v, out nx, out ny, out nz);
                    at = Put(m, ink, x + nx * Lift, y + ny * Lift, z + nz * Lift, nx, ny, nz, p[i].A, p[i].B);
                    seen[key] = at;
                }
                corner[i] = at;
            }
            for (int i = 1; i + 1 < n; i++)
                if (corner[0] != corner[i] && corner[i] != corner[i + 1] && corner[0] != corner[i + 1]) m.Tri(corner[0], corner[i], corner[i + 1], 0, 1, 0);
        }

        static bool Same(in Ink a, in Ink b)
        {
            return a.Hx == b.Hx && a.Hy == b.Hy && a.Round == b.Round && a.Soft == b.Soft && a.Seed == b.Seed && a.Extra == b.Extra && a.Colour == b.Colour && a.Kind == b.Kind;
        }
    }
}
