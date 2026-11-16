using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Every tree model and picture the game needs, built once at start-up (on a worker thread; nothing here
    /// touches Unity): per kind and variant a full model, a lighter one and an impostor, plus the leaf atlas,
    /// the bark textures and the impostor pictures, which are drawn here from the full models.
    /// </summary>
    public sealed class TreeBank
    {
        public const int ImpCell = 256, ImpCols = 10;
        public readonly TreeMesh[] Meshes = new TreeMesh[TreeSpecies.Count * TreeSpecies.Variants * TreeModels.Lods];
        public List<byte[]> Atlas;            // mip levels, AtlasN square at level 0
        public byte[][] Bark;                 // one per layer, BarkW x BarkH
        public List<byte[]> Impostors;        // mip levels
        public int ImpW, ImpH;

        public static int Index(int species, int variant, int lod) { return (species * TreeSpecies.Variants + variant) * TreeModels.Lods + lod; }

        public TreeMesh Get(int species, int variant, int lod) { return Meshes[Index(species, variant, lod)]; }

        public static TreeBank Build()
        {
            var b = new TreeBank();
            b.Atlas = TreeTextures.Atlas();
            b.Bark = new byte[TreeTextures.BarkLayers][];
            for (int l = 0; l < TreeTextures.BarkLayers; l++) b.Bark[l] = TreeTextures.Bark(l);

            int models = TreeSpecies.Count * TreeSpecies.Variants;
            int rows = (models * 2 + ImpCols - 1) / ImpCols;
            b.ImpW = ImpCols * ImpCell; b.ImpH = rows * ImpCell;
            var imp = new float[b.ImpW * b.ImpH * 4];
            int atlasMip = 2, an = TreeTextures.AtlasN >> atlasMip;
            byte[] atlasSmall = b.Atlas[atlasMip];

            for (int sp = 0; sp < TreeSpecies.Count; sp++)
                for (int v = 0; v < TreeSpecies.Variants; v++)
                {
                    TreeMesh full = TreeModels.Build((byte)sp, v, 0);
                    TreeMesh lite = TreeModels.Build((byte)sp, v, 1);
                    // every model exactly 1 unit tall, so a tree's height is its scale
                    float k = full.Top > 0.01f ? 1f / full.Top : 1f;
                    full.Scale(k); lite.Scale(k);
                    full.Measure(); lite.Measure();
                    int n = sp * TreeSpecies.Variants + v;
                    int cf = n * 2, cs = n * 2 + 1;
                    float[] rf = Draw(full, false, atlasSmall, an, b.Bark, imp, b.ImpW, cf % ImpCols * ImpCell, cf / ImpCols * ImpCell);
                    float[] rs = Draw(full, true, atlasSmall, an, b.Bark, imp, b.ImpW, cs % ImpCols * ImpCell, cs / ImpCols * ImpCell);
                    float pu = 1.5f / b.ImpW, pv = 1.5f / b.ImpH;
                    TreeMesh far = TreeModels.Impostor(full,
                        (float)(cf % ImpCols * ImpCell) / b.ImpW + pu, (float)(cf / ImpCols * ImpCell) / b.ImpH + pv,
                        (float)(cf % ImpCols * ImpCell + ImpCell) / b.ImpW - pu, (float)(cf / ImpCols * ImpCell + ImpCell) / b.ImpH - pv,
                        (float)(cs % ImpCols * ImpCell) / b.ImpW + pu, (float)(cs / ImpCols * ImpCell) / b.ImpH + pv,
                        (float)(cs % ImpCols * ImpCell + ImpCell) / b.ImpW - pu, (float)(cs / ImpCols * ImpCell + ImpCell) / b.ImpH - pv);
                    b.Meshes[Index(sp, v, 0)] = full;
                    b.Meshes[Index(sp, v, 1)] = lite;
                    b.Meshes[Index(sp, v, 2)] = far;
                }
            b.Impostors = TreeTextures.Mips(imp, b.ImpW, b.ImpH, ImpCell);
            return b;
        }

        /// <summary>
        /// Draw a model into one impostor cell, seen from the front (looking along +z) or the side (along -x),
        /// stretched to fill the cell exactly as the impostor quad will show it. Four samples a pixel.
        /// </summary>
        static float[] Draw(TreeMesh m, bool side, byte[] atlas, int an, byte[][] bark, float[] dst, int dstW, int ox, int oy)
        {
            const int S = 2, N = ImpCell * S;
            float top = m.Top * 1.02f, bottom = -0.03f, half = (side ? m.HalfZ : m.HalfX) * 1.04f;
            var col = new float[N * N * 3];
            var depth = new float[N * N];
            var hit = new bool[N * N];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;
            int count = m.Count;
            var sx = new float[count]; var sy = new float[count]; var sz = new float[count];
            for (int i = 0; i < count; i++)
            {
                float x = m.P[i * 3], y = m.P[i * 3 + 1], z = m.P[i * 3 + 2];
                float h = side ? z : x;      // seen from +x, +z is to the right (Unity is left-handed)
                sx[i] = (h + half) / (2 * half) * N;
                sy[i] = (y - bottom) / (top - bottom) * N;
                sz[i] = side ? -x : z;
            }
            for (int t = 0; t < m.I.Count; t += 3)
            {
                int a = m.I[t], b = m.I[t + 1], c = m.I[t + 2];
                float area = (sx[b] - sx[a]) * (sy[c] - sy[a]) - (sx[c] - sx[a]) * (sy[b] - sy[a]);
                if (Math.Abs(area) < 1e-6f) continue;
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(sx[a], Math.Min(sx[b], sx[c])))), x1 = Math.Min(N - 1, (int)Math.Ceiling(Math.Max(sx[a], Math.Max(sx[b], sx[c]))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(sy[a], Math.Min(sy[b], sy[c])))), y1 = Math.Min(N - 1, (int)Math.Ceiling(Math.Max(sy[a], Math.Max(sy[b], sy[c]))));
                float w = m.U[a * 3 + 2];
                bool leaf = w > 9.5f;
                int layer = leaf ? 0 : Math.Max(0, Math.Min(bark.Length - 1, (int)Math.Round(w)));
                for (int py = y0; py <= y1; py++)
                    for (int px = x0; px <= x1; px++)
                    {
                        float qx = px + 0.5f, qy = py + 0.5f;
                        float wa = ((sx[b] - qx) * (sy[c] - qy) - (sx[c] - qx) * (sy[b] - qy)) / area;
                        float wb = ((sx[c] - qx) * (sy[a] - qy) - (sx[a] - qx) * (sy[c] - qy)) / area;
                        float wc = 1 - wa - wb;
                        if (wa < 0 || wb < 0 || wc < 0) continue;
                        float z = wa * sz[a] + wb * sz[b] + wc * sz[c];
                        int o = py * N + px;
                        if (z >= depth[o]) continue;
                        float u = wa * m.U[a * 3] + wb * m.U[b * 3] + wc * m.U[c * 3];
                        float v = wa * m.U[a * 3 + 1] + wb * m.U[b * 3 + 1] + wc * m.U[c * 3 + 1];
                        float ao = (wa * m.C[a * 4 + 2] + wb * m.C[b * 4 + 2] + wc * m.C[c * 4 + 2]) / 255f;
                        float r, g, bl;
                        if (leaf)
                        {
                            int tx = Math.Max(0, Math.Min(an - 1, (int)(u * an))), ty = Math.Max(0, Math.Min(an - 1, (int)(v * an)));
                            int k = (ty * an + tx) * 4;
                            if (atlas[k + 3] < 128) continue;
                            r = atlas[k] / 255f; g = atlas[k + 1] / 255f; bl = atlas[k + 2] / 255f;
                        }
                        else
                        {
                            float fu = u - (float)Math.Floor(u), fv = v - (float)Math.Floor(v);
                            int tx = Math.Min(TreeTextures.BarkW - 1, (int)(fu * TreeTextures.BarkW)), ty = Math.Min(TreeTextures.BarkH - 1, (int)(fv * TreeTextures.BarkH));
                            int k = (ty * TreeTextures.BarkW + tx) * 4;
                            byte[] bk = bark[layer];
                            r = bk[k] / 255f; g = bk[k + 1] / 255f; bl = bk[k + 2] / 255f;
                        }
                        depth[o] = z; hit[o] = true;
                        col[o * 3] = r * ao; col[o * 3 + 1] = g * ao; col[o * 3 + 2] = bl * ao;
                    }
            }
            // down to the cell, alpha = share of samples that hit something
            for (int y = 0; y < ImpCell; y++)
                for (int x = 0; x < ImpCell; x++)
                {
                    float r = 0, g = 0, bl = 0; int n = 0;
                    for (int j = 0; j < S; j++)
                        for (int i = 0; i < S; i++)
                        {
                            int o = (y * S + j) * N + x * S + i;
                            if (!hit[o]) continue;
                            r += col[o * 3]; g += col[o * 3 + 1]; bl += col[o * 3 + 2]; n++;
                        }
                    int d = ((oy + y) * dstW + ox + x) * 4;
                    if (n > 0) { dst[d] = r / n; dst[d + 1] = g / n; dst[d + 2] = bl / n; }
                    dst[d + 3] = (float)n / (S * S);
                }
            return null;
        }
    }
}
