using System;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Seas, lakes and rivers for one square. The map says where the water is; this gives it a bed to lie in
    /// (the real sea floor where the terrain data has one, a sloping lake bed otherwise) and a surface mesh at
    /// its true level: sea level for the ocean, the measured level for lakes and rivers.
    /// </summary>
    public static class WaterMesh
    {
        const int G = TileResult.Grid, V = G + 1;
        public const byte Dry = 0, Sea = 1, Lake = 2;
        /// <summary>WCell value of a grid cell that holds only narrow water (a stream, a small pond): it lies on the ground.</summary>
        public const byte StreamCell = 2;
        /// <summary>How far narrow water sits above the ground under it, game metres.</summary>
        public const float StreamDepth = 0.08f;
        /// <summary>
        /// The ground under water is painted as a muddy bed, never blue: all the blue you see is real water.
        /// </summary>
        public const uint BedColour = 0x6e6a55;

        /// <summary>How far past the square's own edge the heights are worked out, in grid cells (see Heights).</summary>
        public const int Margin = 16;

        public static byte KindOf(byte mask) { return mask == Raster.Ocean ? Sea : mask == Raster.Water ? Lake : Dry; }

        /// <summary>
        /// Beds and banks. Works on a grid that reaches Margin cells past the square on every side (E points across),
        /// and every rule looks no further than Margin cells, so two neighbouring squares work out exactly the same
        /// height along the edge they share: no step, no wall. hs: ground heights in real metres (changed to the bed
        /// under water); lv: the middle of the terrain readings round each point on the way in, the water level in
        /// real metres on the way out; kind: Dry, Sea or Lake at each point.
        /// </summary>
        public static void Heights(float[] hs, float[] lv, byte[] kind, int E, float cellReal)
        {
            bool any = false;
            for (int k = 0; k < E * E; k++) if (kind[k] != Dry) { any = true; break; }
            if (!any) { for (int k = 0; k < E * E; k++) lv[k] = hs[k]; return; }

            // distance from the shore in cells, no further than the margin: past that the bed stops getting deeper
            var dist = new float[E * E];
            for (int k = 0; k < E * E; k++) dist[k] = kind[k] == Dry ? 0f : 1e4f;
            Chamfer(dist, E);

            for (int k = 0; k < E * E; k++)
            {
                float dReal = Math.Min(dist[k], Margin) * cellReal;
                if (kind[k] == Sea)
                {
                    lv[k] = 0f;
                    hs[k] = Math.Min(hs[k], -(1.2f + dReal * 0.05f));
                }
                else if (kind[k] == Lake)
                {
                    // lake and river levels: the middle of the 5 x 5 terrain readings round each point
                    hs[k] = lv[k] - Math.Min(40f, 0.8f + dReal * 0.08f);
                }
            }

            // dry ground close to the sea is never below it: the terrain data has pits there (piers, bridge
            // footings) that would otherwise be holes under sea level with no water in them. Only the sea: a pond
            // or a stream on a hillside has lower ground below it by right, and lifting that to the water's level
            // built a flat shelf round the water with a cliff on its downhill side.
            for (int j = 0; j < E; j++)
                for (int i = 0; i < E; i++)
                {
                    int k = j * E + i;
                    if (kind[k] != Dry || hs[k] >= 0.4f) continue;
                    bool sea = false;
                    for (int jj = Math.Max(0, j - 4); jj <= Math.Min(E - 1, j + 4) && !sea; jj++)
                        for (int ii = Math.Max(0, i - 4); ii <= Math.Min(E - 1, i + 4); ii++)
                            if (kind[jj * E + ii] == Sea) { sea = true; break; }
                    if (sea) hs[k] = 0.4f;
                }

            // the dry rim round the water: its surface carries on to the bank, and the bank stays just above it
            for (int j = 0; j < E; j++)
                for (int i = 0; i < E; i++)
                {
                    int k = j * E + i;
                    if (kind[k] != Dry) continue;
                    bool sea = false; int lakes = 0;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int ii = i + di, jj = j + dj;
                            if (ii < 0 || jj < 0 || ii > E - 1 || jj > E - 1) continue;
                            int kk = jj * E + ii;
                            if (kind[kk] == Sea) sea = true;
                            else if (kind[kk] == Lake) lakes++;
                        }
                    if (sea)
                    {
                        lv[k] = 0f;
                        if (hs[k] < 0.6f) hs[k] = 0.6f;
                    }
                    else if (lakes > 0)
                    {
                        if (hs[k] < lv[k] + 0.4f) hs[k] = lv[k] + 0.4f;
                    }
                    else lv[k] = hs[k];
                }
        }

        /// <summary>Which grid cells hold water, for the surface mesh. kind: Dry, Sea or Lake at each grid vertex.</summary>
        public static void Shape(TileResult t, byte[] kind, Raster ras)
        {
            // the map's water pixels, and which grid cells hold any
            int N = ras.N;
            var mask = new byte[N * N];
            var pix = new bool[G * G];
            bool anyPix = false;
            for (int y = 0; y < N; y++)
            {
                int cj = y * G / N;
                for (int x = 0; x < N; x++)
                {
                    byte m = ras.Mask[y * N + x];
                    if (m != Raster.Ocean && m != Raster.Water) continue;
                    mask[y * N + x] = 255;
                    pix[cj * G + x * G / N] = true;
                    anyPix = true;
                }
            }

            bool any = false;
            for (int k = 0; k < V * V; k++) if (kind[k] != Dry) { any = true; break; }
            if (!any && !anyPix) return;
            t.WMask = mask;
            t.MaskN = N;

            t.WPos = new float[V * V * 3];
            t.WCell = new byte[G * G];
            t.WKind = new byte[V * V];
            for (int j = 0; j < G; j++)
                for (int i = 0; i < G; i++)
                {
                    int a = j * V + i;
                    byte ka = kind[a], kb = kind[a + 1], kc = kind[a + V], kd = kind[a + V + 1];
                    if (ka == Dry && kb == Dry && kc == Dry && kd == Dry) { if (pix[j * G + i]) t.WCell[j * G + i] = StreamCell; continue; }
                    t.WCell[j * G + i] = 1;
                    byte w = (ka == Sea || kb == Sea || kc == Sea || kd == Sea) ? Sea : Lake;
                    Mark(t.WKind, a, w); Mark(t.WKind, a + 1, w); Mark(t.WKind, a + V, w); Mark(t.WKind, a + V + 1, w);
                }
        }

        /// <summary>Two-pass chamfer distance (1, 1.414 cells) from the cells holding 0.</summary>
        static void Chamfer(float[] dist, int E)
        {
            for (int j = 0; j < E; j++)
                for (int i = 0; i < E; i++)
                {
                    int k = j * E + i;
                    if (dist[k] == 0) continue;
                    float d = dist[k];
                    if (i > 0) d = Math.Min(d, dist[k - 1] + 1f);
                    if (j > 0) d = Math.Min(d, dist[k - E] + 1f);
                    if (i > 0 && j > 0) d = Math.Min(d, dist[k - E - 1] + 1.414f);
                    if (i < E - 1 && j > 0) d = Math.Min(d, dist[k - E + 1] + 1.414f);
                    dist[k] = d;
                }
            for (int j = E - 1; j >= 0; j--)
                for (int i = E - 1; i >= 0; i--)
                {
                    int k = j * E + i;
                    if (dist[k] == 0) continue;
                    float d = dist[k];
                    if (i < E - 1) d = Math.Min(d, dist[k + 1] + 1f);
                    if (j < E - 1) d = Math.Min(d, dist[k + E] + 1f);
                    if (i < E - 1 && j < E - 1) d = Math.Min(d, dist[k + E + 1] + 1.414f);
                    if (i > 0 && j < E - 1) d = Math.Min(d, dist[k + E - 1] + 1.414f);
                    dist[k] = d;
                }
        }

        static void Mark(byte[] wk, int k, byte w) { if (wk[k] == Dry || w == Sea) wk[k] = w; }

        /// <summary>The water surface, cut into the same patches as the ground so it hides and shows with them.</summary>
        public static void Build(TileResult t)
        {
            if (t.WPos == null) return;
            int P = t.Patches, c = G / P;
            t.Water = new MeshData[P * P];
            var index = new int[V * V];
            double R = Geo.R;
            for (int pj = 0; pj < P; pj++)
                for (int pi = 0; pi < P; pi++)
                {
                    var m = new MeshData();
                    for (int k = 0; k < index.Length; k++) index[k] = -1;
                    for (int j = pj * c; j < (pj + 1) * c; j++)
                        for (int i = pi * c; i < (pi + 1) * c; i++)
                        {
                            byte cell = t.WCell[j * G + i];
                            if (cell == 0) continue;
                            if (cell == StreamCell) { Stream(t, m, i, j, R); continue; }
                            int a = Vert(t, m, index, j * V + i, R), b = Vert(t, m, index, j * V + i + 1, R);
                            int cc = Vert(t, m, index, (j + 1) * V + i, R), d = Vert(t, m, index, (j + 1) * V + i + 1, R);
                            // the same diagonal as the ground, so the shore line falls where the two meshes cross
                            m.I.Add(a); m.I.Add(b); m.I.Add(cc);
                            m.I.Add(b); m.I.Add(d); m.I.Add(cc);
                        }
                    t.Water[pj * P + pi] = m.I.Count > 0 ? m : null;
                }
        }

        /// <summary>A cell of narrow water: a sheet just above the ground, cut to shape by the map's water pixels in the shader.</summary>
        static void Stream(TileResult t, MeshData m, int i, int j, double R)
        {
            int a = SVert(t, m, j * V + i, R), b = SVert(t, m, j * V + i + 1, R);
            int c = SVert(t, m, (j + 1) * V + i, R), d = SVert(t, m, (j + 1) * V + i + 1, R);
            m.I.Add(a); m.I.Add(b); m.I.Add(c);
            m.I.Add(b); m.I.Add(d); m.I.Add(c);
        }

        static int SVert(TileResult t, MeshData m, int k, double R)
        {
            int o = k * 3;
            float x = t.Pos[o], y = t.Pos[o + 1] + StreamDepth, z = t.Pos[o + 2];
            double nx = x, ny = y + R, nz = z, len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return m.Add(x, y, z, (float)(nx / len), (float)(ny / len), (float)(nz / len), StreamDepth, 2f);
        }

        /// <summary>One surface vertex. u = depth of water here in game metres (below zero on a dry bank), v = 0 sea, 1 lake or river, 2 narrow water lying on the ground.</summary>
        static int Vert(TileResult t, MeshData m, int[] index, int k, double R)
        {
            if (index[k] >= 0) return index[k];
            int o = k * 3;
            float x = t.WPos[o], y = t.WPos[o + 1], z = t.WPos[o + 2];
            double nx = x, ny = y + R, nz = z, len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            float depth = y - t.Pos[o + 1];
            index[k] = m.Add(x, y, z, (float)(nx / len), (float)(ny / len), (float)(nz / len), depth, t.WKind[k] == Sea ? 0f : 1f);
            return index[k];
        }
    }
}
