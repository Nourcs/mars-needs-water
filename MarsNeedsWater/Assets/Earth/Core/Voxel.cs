using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The voxel look (an experiment, branch voxel-style): the whole world built from cubes. Set On to false and
    /// everything is built the smooth way again.
    ///
    /// Each square is cut into VN x VN columns of blocks. A column's top is a whole number of blocks high; water
    /// lies in columns whose bed is at least one block under the water level. Large flat runs of equal blocks are
    /// drawn as one rectangle, and the shaders give every block its own shade from its place in the grid, so no
    /// pattern ever repeats.
    /// </summary>
    public static class Voxel
    {
        public static bool On = true;

        /// <summary>How far the water surface sits under the top of its block, game metres.</summary>
        public const float WaterDrop = 0.12f;
        public const short Dry = short.MinValue;

        /// <summary>Block size (game metres) at a map zoom: half a metre up close, bigger for the far rings.</summary>
        public static float BlockFor(int z) { return z >= 14 ? 0.5f : z >= 12 ? 4f : z >= 10 ? 16f : 64f; }

        static int ColumnsFor(TileResult t)
        {
            float b = BlockFor(t.Z);
            int n = (int)Math.Round(t.Size / b / 4.0) * 4;
            // steep country makes many more faces: use blocks twice the size there to keep squares light
            return Math.Max(64, Math.Min(1024, n));
        }

        // ------------------------------------------------------------ terrain

        /// <summary>
        /// Turn the square's smooth ground (t.Pos) and water (t.WPos, t.WCell) into block columns, and build the
        /// ground and water meshes from them. elev: game metres above sea level at each grid point.
        /// </summary>
        public static void Terrain(TileResult t, float[] elev)
        {
            const int G = TileResult.Grid, GV = G + 1;
            int N = ColumnsFor(t);
            float B = BlockFor(t.Z);
            // steep squares: fewer, bigger blocks
            double rise = 0;
            for (int j = 0; j < G; j++)
                for (int i = 0; i < G; i++)
                    rise += Math.Abs(elev[j * GV + i + 1] - elev[j * GV + i]) + Math.Abs(elev[(j + 1) * GV + i] - elev[j * GV + i]);
            double steps = rise / B * N / G;          // about how many block steps the square holds
            while (steps > 260000 && N >= 256) { N /= 2; B *= 2; steps /= 4; }

            var bas = new float[GV * GV * 3];
            for (int k = 0; k < GV * GV; k++) { bas[k * 3] = t.Pos[k * 3]; bas[k * 3 + 1] = t.Pos[k * 3 + 1] - elev[k]; bas[k * 3 + 2] = t.Pos[k * 3 + 2]; }

            var top = new short[N * N];
            var wat = new short[N * N];
            var lake = new bool[N * N];
            bool anyWater = false;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    double u = (i + 0.5) / N, v = (j + 0.5) / N;
                    float x, y, z, bx, by, bz, wy;
                    t.Bed(u, v, out x, out y, out z);
                    TileResult.Interp(bas, u, v, out bx, out by, out bz);
                    int q = (int)Math.Round((y - by) / B);
                    int c = j * N + i;
                    wat[c] = Dry;
                    if (t.WaterAt(u, v, out wy) && wy > y + 0.02f)
                    {
                        int wq = (int)Math.Round((wy - by) / B);
                        if (q > wq - 1) q = wq - 1;
                        wat[c] = (short)wq;
                        lake[c] = t.LakeAt((float)((u - 0.5) * t.Size), (float)((0.5 - v) * t.Size));
                        anyWater = true;
                    }
                    top[c] = (short)q;
                }
            // dry land beside water never lies under the water level
            if (anyWater)
            {
                var up = new short[N * N];
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        int c = j * N + i;
                        short q = top[c];
                        if (wat[c] == Dry)
                        {
                            if (i > 0 && wat[c - 1] != Dry && wat[c - 1] > q) q = wat[c - 1];
                            if (i < N - 1 && wat[c + 1] != Dry && wat[c + 1] > q) q = wat[c + 1];
                            if (j > 0 && wat[c - N] != Dry && wat[c - N] > q) q = wat[c - N];
                            if (j < N - 1 && wat[c + N] != Dry && wat[c + N] > q) q = wat[c + N];
                        }
                        up[c] = q;
                    }
                top = up;
            }

            t.VN = N; t.VB = B; t.VTop = top; t.VWat = anyWater ? wat : null; t.VBase = bas;

            // where each corner of the column grid lies (x, y at sea level, z)
            int CV = N + 1;
            var corner = new float[CV * CV * 3];
            for (int j = 0; j <= N; j++)
                for (int i = 0; i <= N; i++)
                {
                    float x, y, z;
                    TileResult.Interp(bas, (double)i / N, (double)j / N, out x, out y, out z);
                    int o = (j * CV + i) * 3;
                    corner[o] = x; corner[o + 1] = y; corner[o + 2] = z;
                }

            int P = t.Z == 14 ? 1 : 4;
            t.Patches = P;
            t.Ground = new MeshData[P * P];
            t.Water = anyWater ? new MeshData[P * P] : null;
            int skirt = t.Z == 14 ? 8 : 4;
            var seen = new bool[N * N];
            int pc = N / P;
            for (int pj = 0; pj < P; pj++)
                for (int pi = 0; pi < P; pi++)
                {
                    int i0 = pi * pc, i1 = i0 + pc, j0 = pj * pc, j1 = j0 + pc;
                    var m = new MeshData { X = new List<float>(), Y = new List<float>() };
                    Tops(m, top, null, seen, N, B, corner, i0, i1, j0, j1, 0f, null);
                    Sides(m, top, N, B, corner, i0, i1, j0, j1, skirt);
                    t.Ground[pj * P + pi] = m;
                    if (anyWater)
                    {
                        var w = new MeshData();
                        Tops(w, wat, top, seen, N, B, corner, i0, i1, j0, j1, -WaterDrop, lake);
                        t.Water[pj * P + pi] = w.I.Count > 0 ? w : null;
                    }
                }
        }

        static void Extra(MeshData m, float a, float b)
        {
            if (m.X == null) return;
            m.X.Add(a); m.X.Add(b); m.X.Add(0); m.X.Add(0);
            m.Y.Add(0); m.Y.Add(0); m.Y.Add(0); m.Y.Add(0);
        }

        /// <summary>
        /// The flat tops of a patch, equal neighbours joined into rectangles. With 'bed' given this draws water
        /// instead: only wet columns, joined when level, kind and depth (up to a few blocks) agree.
        /// </summary>
        static void Tops(MeshData m, short[] h, short[] bed, bool[] seen, int N, float B, float[] corner, int i0, int i1, int j0, int j1, float lift, bool[] lake)
        {
            int CV = N + 1;
            for (int j = j0; j < j1; j++) for (int i = i0; i < i1; i++) seen[j * N + i] = false;
            for (int j = j0; j < j1; j++)
                for (int i = i0; i < i1; i++)
                {
                    int c = j * N + i;
                    if (seen[c]) continue;
                    short q = h[c];
                    if (bed != null && q == Dry) { seen[c] = true; continue; }
                    int depth = bed != null ? Math.Min(q - bed[c], 8) : 0;
                    bool lk = lake != null && lake[c];
                    int w = 1;
                    while (i + w < i1 && !seen[c + w] && h[c + w] == q && (bed == null || (Math.Min(q - bed[c + w], 8) == depth && lake[c + w] == lk))) w++;
                    int hh = 1;
                    for (; j + hh < j1; hh++)
                    {
                        int r = c + hh * N, k = 0;
                        for (; k < w; k++) if (seen[r + k] || h[r + k] != q || (bed != null && (Math.Min(q - bed[r + k], 8) != depth || lake[r + k] != lk))) break;
                        if (k < w) break;
                    }
                    for (int b = 0; b < hh; b++) for (int a = 0; a < w; a++) seen[c + b * N + a] = true;
                    float y = q * B + lift;
                    int v0 = -1;
                    for (int k = 0; k < 4; k++)
                    {
                        int ci = i + (k % 2) * w, cj = j + (k / 2) * hh, o = (cj * CV + ci) * 3;
                        float uu = bed != null ? depth * B + lift : (float)ci / N, vv = bed != null ? (lk ? 1f : 0f) : (float)cj / N;
                        int vi = m.Add(corner[o], corner[o + 1] + y, corner[o + 2], 0, 1, 0, uu, vv);
                        Extra(m, 0, 0);
                        if (k == 0) v0 = vi;
                    }
                    m.Tri(v0, v0 + 1, v0 + 2, 0, 1, 0);
                    m.Tri(v0 + 1, v0 + 3, v0 + 2, 0, 1, 0);
                }
        }

        /// <summary>The upright faces where a column stands above its neighbour, and a skirt round the patch.</summary>
        static void Sides(MeshData m, short[] top, int N, float B, float[] corner, int i0, int i1, int j0, int j1, int skirt)
        {
            // dir 0: faces looking east or west, on the line between columns i-1 and i; dir 1: north or south, between rows j-1 and j
            for (int dir = 0; dir < 2; dir++)
            {
                int a0 = dir == 0 ? i0 : j0, a1 = dir == 0 ? i1 : j1, b0 = dir == 0 ? j0 : i0, b1 = dir == 0 ? j1 : i1;
                for (int a = a0; a <= a1; a++)
                {
                    int runStart = -1, runHi = 0, runLo = 0, runSign = 0;
                    for (int b = b0; b <= b1; b++)
                    {
                        int hi = 0, lo = 0, sign = 0;
                        if (b < b1)
                        {
                            bool edge = a == a0 || a == a1;
                            int here = a < a1 ? (dir == 0 ? top[b * N + a] : top[a * N + b]) : 0;
                            int back = a > a0 ? (dir == 0 ? top[b * N + a - 1] : top[(a - 1) * N + b]) : 0;
                            if (edge)
                            {
                                // the rim of the patch: a skirt facing outwards, deep enough to meet whatever lies beyond
                                int own = a == a0 ? here : back;
                                hi = own; lo = own - skirt; sign = a == a0 ? -1 : 1;
                            }
                            else if (back > here) { hi = back; lo = here; sign = 1; }
                            else if (here > back) { hi = here; lo = back; sign = -1; }
                        }
                        if (sign != runSign || hi != runHi || lo != runLo)
                        {
                            if (runSign != 0) Side(m, N, B, corner, dir, a, runStart, b, runHi, runLo, runSign);
                            runStart = b; runHi = hi; runLo = lo; runSign = sign;
                        }
                    }
                }
            }
        }

        static void Side(MeshData m, int N, float B, float[] corner, int dir, int a, int b0, int b1, int hi, int lo, int sign)
        {
            int CV = N + 1;
            // sign +1: the face looks towards growing a (east for dir 0, south for dir 1)
            float nx = dir == 0 ? sign : 0, nz = dir == 0 ? 0 : -sign;
            int v0 = -1;
            for (int k = 0; k < 4; k++)
            {
                int bb = k % 2 == 0 ? b0 : b1;
                bool upper = k < 2;
                int ci = dir == 0 ? a : bb, cj = dir == 0 ? bb : a, o = (cj * CV + ci) * 3;
                int vi = m.Add(corner[o], corner[o + 1] + (upper ? hi : lo) * B, corner[o + 2], nx, 0, nz, (float)ci / N, (float)cj / N);
                Extra(m, upper ? 0 : hi - lo, 1);
                if (k == 0) v0 = vi;
            }
            m.Tri(v0, v0 + 1, v0 + 2, nx, 0, nz);
            m.Tri(v0 + 1, v0 + 3, v0 + 2, nx, 0, nz);
        }
    
        // ------------------------------------------------------------ faces of the shared block shader (EarthVoxel.shader)

        /// <summary>
        /// One flat rectangle of block faces. Corners in order: (u0,v0) (u1,v0) (u0,v1) (u1,v1) of the block
        /// coordinates the shader shades by. col2, kind, seed, f, p, rows, shift: see EarthVoxel.shader.
        /// </summary>
        public static void Quad(MeshData m, float[] p, float nx, float ny, float nz, float u0, float v0, float u1, float v1,
            uint col, uint col2 = 0, float kind = 0, float seed = 0, float f = 0, float bay = 0, float rows = 0, float shift = 0)
        {
            int a = m.Count;
            for (int k = 0; k < 4; k++)
            {
                m.Add(p[k * 3], p[k * 3 + 1], p[k * 3 + 2], nx, ny, nz, (k & 1) == 0 ? u0 : u1, k < 2 ? v0 : v1);
                m.X.Add(col); m.X.Add(col2); m.X.Add(kind); m.X.Add(seed);
                m.Y.Add(f); m.Y.Add(bay); m.Y.Add(rows); m.Y.Add(shift);
            }
            m.Tri(a, a + 1, a + 2, nx, ny, nz);
            m.Tri(a + 1, a + 3, a + 2, nx, ny, nz);
        }

        static readonly uint[] Panes = { 0x35506a, 0x2c4650, 0x4a4234, 0x232e38, 0x47708c, 0x3a5a48, 0x50586a, 0x2e3e5e };
        [ThreadStatic] static float[] quadTL;
        static float[] quad { get { return quadTL ?? (quadTL = new float[12]); } }

        // ------------------------------------------------------------ buildings

        /// <summary>
        /// One building (or part of one) as blocks: its outline filled into the square's columns, walls straight up
        /// with windows drawn block by block, and a stepped roof. rings: the outline and its holes as u, v pairs
        /// (0..1 across the square). top, bottom: tile-local heights. floating: the bottom is in the air (a part
        /// resting on another).
        /// </summary>
        public static void Building(TileResult t, MeshData walls, MeshData roofs, List<double[]> rings, float top, float bottom, bool floating, Look k, uint hash, double floors)
        {
            int N = t.VN; float B = t.VB;
            double minU = 1e9, minV = 1e9, maxU = -1e9, maxV = -1e9, su = 0, sv = 0; int sn = 0;
            foreach (double[] r in rings)
                for (int i = 0; i < r.Length; i += 2)
                {
                    if (r[i] < minU) minU = r[i]; if (r[i] > maxU) maxU = r[i];
                    if (r[i + 1] < minV) minV = r[i + 1]; if (r[i + 1] > maxV) maxV = r[i + 1];
                    if (r == rings[0]) { su += r[i]; sv += r[i + 1]; sn++; }
                }
            int i0 = (int)Math.Floor(minU * N) - 1, i1 = (int)Math.Ceiling(maxU * N) + 1, j0 = (int)Math.Floor(minV * N) - 1, j1 = (int)Math.Ceiling(maxV * N) + 1;
            int W = i1 - i0, H = j1 - j0;
            if (W <= 0 || H <= 0 || W > 2000 || H > 2000) return;
            var inside = new bool[W * H];
            int cells = 0;
            var xs = new List<double>();
            for (int j = 1; j < H - 1; j++)
            {
                double v = (j0 + j + 0.5) / N;
                xs.Clear();
                foreach (double[] r in rings)
                    for (int a = 0, n = r.Length / 2, b = n - 1; a < n; b = a, a++)
                    {
                        double va = r[a * 2 + 1], vb = r[b * 2 + 1];
                        if ((va > v) == (vb > v)) continue;
                        xs.Add(r[a * 2] + (v - va) / (vb - va) * (r[b * 2] - r[a * 2]));
                    }
                xs.Sort();
                for (int q = 0; q + 1 < xs.Count; q += 2)
                {
                    int a = (int)Math.Ceiling(xs[q] * N - 0.5) - i0, b = (int)Math.Ceiling(xs[q + 1] * N - 0.5) - i0;
                    for (int i = Math.Max(a, 1); i < b && i < W - 1; i++) if (!inside[j * W + i]) { inside[j * W + i] = true; cells++; }
                }
            }
            if (cells == 0)
            {
                // smaller than a block: it still gets one
                int ci = (int)Math.Floor(su / sn * N) - i0, cj = (int)Math.Floor(sv / sn * N) - j0;
                if (ci < 1 || cj < 1 || ci >= W - 1 || cj >= H - 1) return;
                inside[cj * W + ci] = true; cells = 1;
            }

            // the ground under it
            int gMin = int.MaxValue, gMax = int.MinValue; long gSum = 0;
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    if (!inside[j * W + i]) continue;
                    int ci = Math.Min(N - 1, Math.Max(0, i0 + i)), cj = Math.Min(N - 1, Math.Max(0, j0 + j));
                    int g = t.VTop[cj * N + ci];
                    if (t.VWat != null && t.VWat[cj * N + ci] != Dry) g = t.VWat[cj * N + ci];
                    if (g < gMin) gMin = g; if (g > gMax) gMax = g;
                    gSum += g;
                }
            float bx, by, bz;
            TileResult.Interp(t.VBase, su / sn, sv / sn, out bx, out by, out bz);
            int foot = (int)Math.Round((double)gSum / cells);
            int topI = (int)Math.Round((top - by) / B);
            int botI = floating ? (int)Math.Round((bottom - by) / B) : gMin - 1;
            if (topI < gMax + 2) topI = gMax + 2;
            if (botI >= topI) botI = gMin - 1;
            if (floating && botI < foot) { botI = gMin - 1; floating = false; }
            int footRow = floating ? botI : foot;

            // how far each block is from the edge of the outline (1 on the edge itself)
            var dist = new int[W * H];
            int deepest = 0;
            bool gableX = W > H;      // a gable roof's ridge runs along the longer side
            int shape = k.Struct != BK.StNone ? BK.ShFlat : k.RoofShape;
            bool gable = shape == BK.ShGable || shape == BK.ShGableAcross;
            for (int pass = 1; pass < 64; pass++)
            {
                bool any = false;
                for (int j = 1; j < H - 1; j++)
                    for (int i = 1; i < W - 1; i++)
                    {
                        int c = j * W + i;
                        if (!inside[c] || dist[c] != 0) continue;
                        bool edge;
                        if (gable) edge = gableX ? (Open(inside, dist, c - W, pass) || Open(inside, dist, c + W, pass)) : (Open(inside, dist, c - 1, pass) || Open(inside, dist, c + 1, pass));
                        else edge = Open(inside, dist, c - 1, pass) || Open(inside, dist, c + 1, pass) || Open(inside, dist, c - W, pass) || Open(inside, dist, c + W, pass);
                        if (edge) { dist[c] = pass; any = true; deepest = pass; }
                    }
                if (!any) break;
            }
            for (int c = 0; c < W * H; c++) if (inside[c] && dist[c] == 0) dist[c] = deepest + 1;

            uint s = hash * 2654435761u + 12345u; if (s == 0) s = 1;
            int body = topI - footRow;
            int layers = 0;
            if (shape != BK.ShFlat && deepest >= 2)
            {
                layers = shape == BK.ShPyramid ? deepest - 1 : shape == BK.ShMansard ? Math.Min(2, deepest - 1) : Math.Min(k.Cat == BK.CatHouse ? 4 : 3, deepest - 1);
                layers = Math.Max(0, Math.Min(layers, body - 2));
            }
            int wallTop = topI - layers;
            // per block: how high its top is
            var hgt = new int[W * H];
            bool parapet = layers == 0 && shape == BK.ShFlat && deepest >= 3 && k.Struct == BK.StNone && body >= 3;
            for (int c = 0; c < W * H; c++)
            {
                if (!inside[c]) { hgt[c] = int.MinValue; continue; }
                if (layers > 0) hgt[c] = wallTop + Math.Min(dist[c] - 1, layers);
                else hgt[c] = parapet && dist[c] > 1 ? topI - 1 : topI;
            }
            // things on a flat roof: a plant room or two, stepping up from the middle
            if (parapet && deepest >= 4 && k.Cat != BK.CatHouse)
            {
                int boxes = 1 + (int)(Next(ref s) % 2);
                for (int b = 0; b < boxes; b++)
                {
                    int bw = 2 + (int)(Next(ref s) % 3), bh = 2 + (int)(Next(ref s) % 3), up = 1 + (int)(Next(ref s) % 2);
                    int ci = 1 + (int)(Next(ref s) % (uint)Math.Max(1, W - 2 - bw)), cj = 1 + (int)(Next(ref s) % (uint)Math.Max(1, H - 2 - bh));
                    bool fits = true;
                    for (int j = cj; j < cj + bh && fits; j++) for (int i = ci; i < ci + bw; i++) if (j >= H || i >= W || dist[j * W + i] < 3) { fits = false; break; }
                    if (!fits) continue;
                    for (int j = cj; j < cj + bh; j++) for (int i = ci; i < ci + bw; i++) hgt[j * W + i] = topI - 1 + up + 1;
                }
            }

            // what the walls say to the shader
            float seed = (hash % 9973u) * 0.1f;
            float kind = 1;
            if (k.Struct != BK.StNone || k.Win == BK.WNone) kind = 0;
            else if (k.Mat == BK.Glass || k.Win == BK.WCurtain || k.Win == BK.WRibbon) kind = 3;
            int rows = wallTop - footRow;
            float F = Math.Max(2, Math.Min(4, (int)Math.Round(rows / Math.Max(1.0, floors))));
            float bayBlocks = Math.Max(2, Math.Min(4, (int)Math.Round(k.BayReal / Geo.Scale / B + 0.6)));
            float shift = Next(ref s) % 4;
            uint wallCol = k.Wall, roofCol = k.RoofCol, pane = Panes[(k.Glass + (int)(hash >> 5 & 1)) % Panes.Length];
            if (k.Struct != BK.StNone) roofCol = wallCol;

            // ---- tops, equal neighbours joined
            var seen = new bool[W * H];
            for (int j = 1; j < H - 1; j++)
                for (int i = 1; i < W - 1; i++)
                {
                    int c = j * W + i;
                    if (!inside[c] || seen[c]) continue;
                    int h = hgt[c], w = 1, hh = 1;
                    while (i + w < W - 1 && inside[c + w] && !seen[c + w] && hgt[c + w] == h) w++;
                    for (; j + hh < H - 1; hh++)
                    {
                        int r = c + hh * W, q = 0;
                        for (; q < w; q++) if (!inside[r + q] || seen[r + q] || hgt[r + q] != h) break;
                        if (q < w) break;
                    }
                    for (int b = 0; b < hh; b++) for (int a = 0; a < w; a++) seen[c + b * W + a] = true;
                    Corner(t, i0 + i, j0 + j, h, 0); Corner(t, i0 + i + w, j0 + j, h, 1); Corner(t, i0 + i, j0 + j + hh, h, 2); Corner(t, i0 + i + w, j0 + j + hh, h, 3);
                    Quad(roofs, quad, 0, 1, 0, i0 + i, j0 + j, i0 + i + w, j0 + j + hh, roofCol, 0, 0, seed + 1);
                    if (floating)
                    {
                        Corner(t, i0 + i, j0 + j, botI, 0); Corner(t, i0 + i + w, j0 + j, botI, 1); Corner(t, i0 + i, j0 + j + hh, botI, 2); Corner(t, i0 + i + w, j0 + j + hh, botI, 3);
                        Quad(roofs, quad, 0, -1, 0, i0 + i, j0 + j, i0 + i + w, j0 + j + hh, wallCol, 0, 0, seed + 2);
                    }
                }

            // ---- upright faces: outer walls, and the steps of the roof
            for (int dir = 0; dir < 2; dir++)
            {
                int A = dir == 0 ? W : H, Bn = dir == 0 ? H : W;
                for (int a = 1; a < A; a++)
                {
                    int runStart = -1, runHi = 0, runLo = 0, runSign = 0;
                    for (int b = 0; b <= Bn; b++)
                    {
                        int hi = 0, lo = 0, sign = 0;
                        if (b < Bn)
                        {
                            int here = dir == 0 ? hgt[b * W + a] : hgt[a * W + b], back = dir == 0 ? hgt[b * W + a - 1] : hgt[(a - 1) * W + b];
                            if (back > here) { hi = back; lo = here == int.MinValue ? botI : here; sign = 1; }
                            else if (here > back) { hi = here; lo = back == int.MinValue ? botI : back; sign = -1; }
                        }
                        if (sign != runSign || hi != runHi || lo != runLo)
                        {
                            if (runSign != 0)
                            {
                                float nx = dir == 0 ? runSign : 0, nz = dir == 0 ? 0 : -runSign;
                                int ia = i0 + (dir == 0 ? a : runStart), ja = j0 + (dir == 0 ? runStart : a), ib = i0 + (dir == 0 ? a : b), jb = j0 + (dir == 0 ? b : a);
                                float al0 = dir == 0 ? ja : ia, al1 = dir == 0 ? jb : ib;
                                // the part of the face under the eaves is wall, the rest roof
                                int split = Math.Max(runLo, Math.Min(runHi, runLo == botI ? wallTop : runLo));
                                if (split > runLo)
                                {
                                    Corner(t, ia, ja, split, 0); Corner(t, ib, jb, split, 1); Corner(t, ia, ja, runLo, 2); Corner(t, ib, jb, runLo, 3);
                                    Quad(walls, quad, nx, 0, nz, al0, split - footRow, al1, runLo - footRow, wallCol, pane, kind, seed + dir * 3 + runSign, F, bayBlocks, rows, shift);
                                }
                                if (runHi > split)
                                {
                                    Corner(t, ia, ja, runHi, 0); Corner(t, ib, jb, runHi, 1); Corner(t, ia, ja, split, 2); Corner(t, ib, jb, split, 3);
                                    Quad(roofs, quad, nx, 0, nz, al0, runHi, al1, split, layers > 0 ? roofCol : wallCol, 0, 0, seed + 4 + dir);
                                }
                            }
                            runStart = b; runHi = hi; runLo = lo; runSign = sign;
                        }
                    }
                }
            }
        }

        static bool Open(bool[] inside, int[] dist, int c, int pass) { return !inside[c] || (dist[c] != 0 && dist[c] < pass); }

        static uint Next(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s >> 4; }

        /// <summary>Corner i, j of the square's column grid at a height in blocks, into slot k of the scratch quad.</summary>
        static void Corner(TileResult t, int i, int j, int h, int k)
        {
            float x, y, z;
            TileResult.Interp(t.VBase, (double)i / t.VN, (double)j / t.VN, out x, out y, out z);
            quad[k * 3] = x; quad[k * 3 + 1] = y + h * t.VB; quad[k * 3 + 2] = z;
        }
    
        // ------------------------------------------------------------ trees

        static readonly uint[] LeafCol =
        {
            0x2f5d3a, 0x2b5a40, 0x3f6b3a, 0x4a7a3a, 0x4f8a34, 0x5a9a38, 0x7aab46, 0x2f5a3c,
            0x5a9440, 0x6a8f48, 0x7a9a4a, 0x6a8f70, 0x2f7a34, 0x7a8a50,
        };
        static readonly uint[] BarkCol =
        {
            0x5a4030, 0x5a4030, 0x6b4a2e, 0x7a5236, 0x5e4630, 0x6b4a2e, 0xd8d4c8, 0x5a4636,
            0x8a7458, 0x7a6248, 0x6b5438, 0xb8a890, 0x5e4a36, 0x6b5a3e,
        };

        static uint Tinted(uint c, uint tint, float k)
        {
            float r = ((c >> 16) & 255) * ((tint >> 16) & 255) / 128f * k, g = ((c >> 8) & 255) * ((tint >> 8) & 255) / 128f * k, b = (c & 255) * (tint & 255) / 128f * k;
            // calmer than the paint box: leaves a little greyer and darker, or the bright look turns them neon
            float grey = (r + g + b) / 3f;
            r = (r + (grey - r) * 0.3f) * 0.8f; g = (g + (grey - g) * 0.3f) * 0.8f; b = (b + (grey - b) * 0.3f) * 0.8f;
            return ((uint)Math.Min(255f, r) << 16) | ((uint)Math.Min(255f, g) << 8) | (uint)Math.Min(255f, b);
        }

        /// <summary>A box of blocks standing on the column grid: columns i0..i1, j0..j1 (end not included), heights h0..h1 in blocks.</summary>
        static void Box(TileResult t, MeshData m, int i0, int j0, int i1, int j1, int h0, int h1, uint col, float kind, float seed, bool under)
        {
            Corner(t, i0, j0, h1, 0); Corner(t, i1, j0, h1, 1); Corner(t, i0, j1, h1, 2); Corner(t, i1, j1, h1, 3);
            Quad(m, quad, 0, 1, 0, i0, j0, i1, j1, col, 0, kind, seed);
            if (under)
            {
                Corner(t, i0, j0, h0, 0); Corner(t, i1, j0, h0, 1); Corner(t, i0, j1, h0, 2); Corner(t, i1, j1, h0, 3);
                Quad(m, quad, 0, -1, 0, i0, j0, i1, j1, col, 0, kind, seed + 1);
            }
            for (int side = 0; side < 4; side++)
            {
                int ia = side == 0 ? i0 : side == 1 ? i1 : side == 2 ? i0 : i0, ja = side == 0 ? j0 : side == 1 ? j0 : side == 2 ? j1 : j0;
                int ib = side == 0 ? i1 : side == 1 ? i1 : side == 2 ? i1 : i0, jb = side == 0 ? j0 : side == 1 ? j1 : side == 2 ? j1 : j1;
                float nx = side == 1 ? 1 : side == 3 ? -1 : 0, nz = side == 0 ? 1 : side == 2 ? -1 : 0;     // row j0 is the north side (+z)
                Corner(t, ia, ja, h1, 0); Corner(t, ib, jb, h1, 1); Corner(t, ia, ja, h0, 2); Corner(t, ib, jb, h0, 3);
                bool alongI = side == 0 || side == 2;
                Quad(m, quad, nx, 0, nz, alongI ? ia : ja, h1, alongI ? ib : jb, h0, col, 0, kind, seed + 2 + side);
            }
        }

        /// <summary>Every tree of the square as a trunk of blocks with a crown of leaf blocks, shaped by its kind.</summary>
        public static void Trees(TileResult t)
        {
            var m = new MeshData { X = new List<float>(), Y = new List<float>() };
            t.Trees = m;
            if (t.TreeSpots == null) return;
            int N = t.VN; float B = t.VB;
            foreach (TreeSpot sp in t.TreeSpots)
            {
                int i = (int)Math.Floor((sp.X / t.Size + 0.5) * N), j = (int)Math.Floor((0.5 - sp.Z / t.Size) * N);
                if (i < 1 || j < 1 || i >= N - 1 || j >= N - 1) continue;
                int c = j * N + i;
                if (t.VWat != null && t.VWat[c] != Dry) continue;
                int g = t.VTop[c];
                uint s = (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ (uint)(sp.Rand * 65535f) * 83492791u; if (s == 0) s = 1;
                Next(ref s);
                int H = Math.Max(3, (int)Math.Round(sp.Height / B));
                float seed = (s % 9973u) * 0.1f;
                int kindOf = sp.Species;
                uint leaf = Tinted(LeafCol[kindOf % LeafCol.Length], sp.Tint, 0.9f + 0.25f * (Next(ref s) % 100) / 100f);
                uint bark = BarkCol[kindOf % BarkCol.Length];
                float wide = Math.Max(0.6f, Math.Min(1.5f, sp.Width));
                if (kindOf == TreeSpecies.Scrub)
                {
                    int r = H >= 4 ? 1 : 0, hh = Math.Max(1, Math.Min(3, H - 1));
                    Box(t, m, i - r, j - r, i + r + 1, j + r + 1, g, g + hh, leaf, 2, seed, false);
                    if (r > 0) Box(t, m, i, j, i + 1, j + 1, g + hh, g + hh + 1, leaf, 2, seed + 9, false);
                }
                else if (kindOf == TreeSpecies.Spruce || kindOf == TreeSpecies.Fir || kindOf == TreeSpecies.Cypress)
                {
                    bool thin = kindOf == TreeSpecies.Cypress;
                    int bare = thin ? 1 : Math.Max(1, H / 6);
                    Box(t, m, i, j, i + 1, j + 1, g, g + bare, bark, 0, seed, false);
                    int r = thin ? 1 : Math.Max(1, Math.Min(4, (int)Math.Round(H * 0.2f * wide)));
                    int y = g + bare, topY = g + H;
                    int tiers = r + 1;
                    for (int k = 0; k < tiers && y < topY; k++)
                    {
                        int rr = r - k;
                        int hh = k == tiers - 1 ? topY - y : Math.Max(1, (topY - y) / (tiers - k) + (thin && k == 0 ? (topY - y) / 2 : 0));
                        Box(t, m, i - rr, j - rr, i + rr + 1, j + rr + 1, y, y + hh, leaf, 2, seed + k * 13, true);
                        y += hh;
                    }
                }
                else if (kindOf == TreeSpecies.Palm || kindOf == TreeSpecies.DatePalm)
                {
                    int th = H - 1;
                    Box(t, m, i, j, i + 1, j + 1, g, g + th, bark, 0, seed, false);
                    int arm = Math.Max(2, Math.Min(4, (int)Math.Round(H * 0.28f * wide)));
                    int y = g + th;
                    Box(t, m, i - arm, j, i + arm + 1, j + 1, y, y + 1, leaf, 2, seed + 3, true);
                    Box(t, m, i, j - arm, i + 1, j + arm + 1, y, y + 1, leaf, 2, seed + 5, true);
                    Box(t, m, i - 1, j - 1, i + 2, j + 2, y, y + 1, leaf, 2, seed + 7, true);
                    // the tips hang down
                    Box(t, m, i - arm, j, i - arm + 1, j + 1, y - 1, y, leaf, 2, seed + 11, true);
                    Box(t, m, i + arm, j, i + arm + 1, j + 1, y - 1, y, leaf, 2, seed + 12, true);
                    Box(t, m, i, j - arm, i + 1, j - arm + 1, y - 1, y, leaf, 2, seed + 13, true);
                    Box(t, m, i, j + arm, i + 1, j + arm + 1, y - 1, y, leaf, 2, seed + 14, true);
                }
                else
                {
                    // a broad crown on a trunk; umbrella shapes (stone pine, acacia) carry it high and flat
                    bool flat = kindOf == TreeSpecies.StonePine || kindOf == TreeSpecies.Acacia;
                    bool tall = kindOf == TreeSpecies.Eucalyptus || kindOf == TreeSpecies.Birch || kindOf == TreeSpecies.Pine;
                    int th = Math.Max(1, (int)Math.Round(H * (flat ? 0.62f : tall ? 0.5f : 0.38f)));
                    int r = Math.Max(1, Math.Min(5, (int)Math.Round(H * (flat ? 0.36f : tall ? 0.2f : 0.3f) * wide)));
                    int ch = Math.Max(1, H - th - (r > 1 ? 1 : 0));
                    if (flat) ch = Math.Max(1, Math.Min(ch, 2));
                    Box(t, m, i, j, i + 1, j + 1, g, g + th, bark, 0, seed, false);
                    int ox = r > 1 ? (int)(Next(ref s) % 3) - 1 : 0, oz = r > 1 ? (int)(Next(ref s) % 3) - 1 : 0;
                    Box(t, m, i - r, j - r, i + r + 1, j + r + 1, g + th, g + th + ch, leaf, 2, seed + 20, true);
                    if (r > 1)
                    {
                        // a smaller cap above and a lip below round it off, set a block off-centre so no two trees match
                        Box(t, m, i - r + 1 + ox, j - r + 1 + oz, i + r + ox, j + r + oz, g + th + ch, g + th + ch + 1, leaf, 2, seed + 30, false);
                        if (!flat && th > 2) Box(t, m, i - r + 1 - ox, j - r + 1 - oz, i + r - ox, j + r - oz, g + th - 1, g + th, leaf, 2, seed + 40, true);
                    }
                }
            }
        }

        // ------------------------------------------------------------ any mesh into blocks

        /// <summary>
        /// Rebuild a mesh out of blocks. Every block a triangle passes through is filled with that triangle's colour
        /// (colour: from the triangle's first vertex), hollows are closed, and the outside faces are drawn, equal
        /// neighbours joined. Blocks are bx, by, bz across, lined up on whole multiples from the mesh's own origin.
        /// from, to: only triangles whose first vertex lies in this range (a part of the mesh).
        /// </summary>
        public static void FromMesh(MeshData dst, IList<float> P, IList<int> I, Func<int, uint> colour, float bx, float by, float bz, float seed, int from = 0, int to = int.MaxValue, float kind = 0)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool any = false;
            for (int q = 0; q + 2 < I.Count; q += 3)
            {
                if (I[q] < from || I[q] >= to) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = I[q + k] * 3;
                    float x = P[v], y = P[v + 1], z = P[v + 2];
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                }
                any = true;
            }
            if (!any) return;
            // too big to hold: use bigger blocks
            while ((double)((maxX - minX) / bx + 3) * ((maxY - minY) / by + 3) * ((maxZ - minZ) / bz + 3) > 9e7) { bx *= 2; by *= 2; bz *= 2; }
            int ox = (int)Math.Floor(minX / bx) - 1, oy = (int)Math.Floor(minY / by) - 1, oz = (int)Math.Floor(minZ / bz) - 1;
            int nx = (int)Math.Floor(maxX / bx) - ox + 2, ny = (int)Math.Floor(maxY / by) - oy + 2, nz = (int)Math.Floor(maxZ / bz) - oz + 2;
            var cell = new int[nx * ny * nz];      // 0 empty, colour + 1, or -1 for the closed-in hollow
            int sx = 1, sy = nx, sz = nx * ny;
            for (int q = 0; q + 2 < I.Count; q += 3)
            {
                if (I[q] < from || I[q] >= to) continue;
                int a = I[q] * 3, b = I[q + 1] * 3, c = I[q + 2] * 3;
                float ax = P[a] / bx - ox, ay = P[a + 1] / by - oy, az = P[a + 2] / bz - oz;
                float ux = P[b] / bx - ox - ax, uy = P[b + 1] / by - oy - ay, uz = P[b + 2] / bz - oz - az;
                float vx = P[c] / bx - ox - ax, vy = P[c + 1] / by - oy - ay, vz = P[c + 2] / bz - oz - az;
                float lu = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz), lv = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                int n = Math.Max(1, (int)Math.Ceiling(Math.Max(lu, lv) / 0.45f));
                int val = (int)(colour(I[q]) & 0xffffff) + 1;
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n - i; j++)
                    {
                        float fu = (float)i / n, fv = (float)j / n;
                        int cx = (int)(ax + ux * fu + vx * fv), cy = (int)(ay + uy * fu + vy * fv), cz = (int)(az + uz * fu + vz * fv);
                        if (cx < 1) cx = 1; else if (cx > nx - 2) cx = nx - 2;
                        if (cy < 1) cy = 1; else if (cy > ny - 2) cy = ny - 2;
                        if (cz < 1) cz = 1; else if (cz > nz - 2) cz = nz - 2;
                        cell[cz * sz + cy * sy + cx] = val;
                    }
            }
            // what the outside can reach stays empty; the rest of the empty blocks are inside something
            var outside = new bool[cell.Length];
            var stack = new Stack<int>();
            stack.Push(0); outside[0] = true;
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                int x = k % nx, y = k / nx % ny, z = k / sz;
                if (x > 0) Reach(cell, outside, stack, k - sx);
                if (x < nx - 1) Reach(cell, outside, stack, k + sx);
                if (y > 0) Reach(cell, outside, stack, k - sy);
                if (y < ny - 1) Reach(cell, outside, stack, k + sy);
                if (z > 0) Reach(cell, outside, stack, k - sz);
                if (z < nz - 1) Reach(cell, outside, stack, k + sz);
            }
            for (int k = 0; k < cell.Length; k++) if (cell[k] == 0 && !outside[k]) cell[k] = -1;
            outside = null;

            // faces, one direction and one layer at a time
            int[] dim = { nx, ny, nz }, step = { sx, sy, sz };
            float[] size = { bx, by, bz };
            int[] org = { ox, oy, oz };
            float[] p = quad;
            for (int d = 0; d < 3; d++)
            {
                // the two axes across the face: the second is "up" where there is one, so windows and rows read right
                int a = d == 0 ? 2 : 0, b = d == 1 ? 2 : 1;
                int na = dim[a], nb = dim[b];
                var mask = new int[na * nb];
                for (int sign = -1; sign <= 1; sign += 2)
                    for (int layer = 1; layer < dim[d] - 1; layer++)
                    {
                        bool some = false;
                        for (int jb = 0; jb < nb; jb++)
                            for (int ia = 0; ia < na; ia++)
                            {
                                int k = layer * step[d] + ia * step[a] + jb * step[b];
                                int v = cell[k];
                                mask[jb * na + ia] = v > 0 && cell[k + sign * step[d]] == 0 ? v : 0;
                                if (mask[jb * na + ia] != 0) some = true;
                            }
                        if (!some) continue;
                        float at = (layer + org[d] + (sign > 0 ? 1 : 0)) * size[d];
                        for (int jb = 0; jb < nb; jb++)
                            for (int ia = 0; ia < na; ia++)
                            {
                                int v = mask[jb * na + ia];
                                if (v == 0) continue;
                                int w = 1, h = 1;
                                while (ia + w < na && mask[jb * na + ia + w] == v) w++;
                                for (; jb + h < nb; h++)
                                {
                                    int r = (jb + h) * na + ia, q = 0;
                                    for (; q < w; q++) if (mask[r + q] != v) break;
                                    if (q < w) break;
                                }
                                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) mask[(jb + y) * na + ia + x] = 0;
                                for (int k = 0; k < 4; k++)
                                {
                                    float ca = (ia + ((k & 1) != 0 ? w : 0) + org[a]) * size[a], cb = (jb + (k >= 2 ? h : 0) + org[b]) * size[b];
                                    p[k * 3 + d] = at; p[k * 3 + a] = ca; p[k * 3 + b] = cb;
                                }
                                Quad(dst, p, d == 0 ? sign : 0, d == 1 ? sign : 0, d == 2 ? sign : 0, ia + org[a], jb + org[b], ia + org[a] + w, jb + org[b] + h,
                                    (uint)(v - 1), 0, kind, seed + layer * 0.61f + d * 7.3f + sign);
                            }
                    }
            }
        }

        static void Reach(int[] cell, bool[] outside, Stack<int> stack, int k)
        {
            if (outside[k] || cell[k] != 0) return;
            outside[k] = true; stack.Push(k);
        }

        /// <summary>A mesh coloured from a 16 x 16 swatch (texture coordinates point at a cell of 'palette') rebuilt in blocks.</summary>
        public static MeshData FromPalette(MeshData src, uint[] palette, float bx, float by, float bz, float seed)
        {
            var dst = new MeshData { X = new List<float>(), Y = new List<float>() };
            if (src == null || src.I.Count == 0) return dst;
            FromMesh(dst, src.P, src.I, v => palette[Math.Min(15, (int)(src.U[v * 2] * 16)) + 16 * Math.Min(15, (int)(src.U[v * 2 + 1] * 16))], bx, by, bz, seed);
            return dst;
        }

        /// <summary>A landmark model rebuilt in blocks, part by part (each part still settles on the ground by itself).</summary>
        public static LandmarkMesh Landmark(LandmarkMesh src, float block)
        {
            var dst = new MeshData { X = new List<float>(), Y = new List<float>() };
            var start = new int[src.Start.Length];
            MeshData sm = src.Mesh;
            for (int part = 0; part < src.Parts; part++)
            {
                start[part] = dst.Count;
                FromMesh(dst, sm.P, sm.I, v => src.Palette[Math.Min(15, (int)(sm.U[v * 2] * 16)) + 16 * Math.Min(15, (int)(sm.U[v * 2 + 1] * 16))], block, block, block, part * 17.3f, src.Start[part], src.Start[part + 1]);
            }
            start[src.Parts] = dst.Count;
            return new LandmarkMesh { Mesh = dst, Palette = src.Palette, Start = start, Stand = src.Stand };
        }
    }
}
