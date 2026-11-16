using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The pictures trees are drawn with, painted here in code at start-up: a leaf atlas (sprigs of real-looking
    /// leaves and needles with see-through gaps, one cell per kind of foliage) and a set of tileable bark textures.
    /// All images are RGBA, bottom row first (Unity's order), so texture v runs up the picture.
    /// </summary>
    public static class TreeTextures
    {
        public const int AtlasN = 2048, Cell = 512, Cols = 4;
        public const int BarkW = 256, BarkH = 512;

        // leaf atlas cells
        public const int Oak = 0, Maple = 1, Birch = 2, Jungle = 3, Spruce = 4, Fir = 5, PineTuft = 6, Cypress = 7,
            Frond = 8, DeadFrond = 9, Acacia = 10, Eucalyptus = 11, Scrub = 12;
        public const int CellCount = 13;

        // bark layers
        public const int BarkOak = 0, BarkSpruce = 1, BarkPine = 2, BarkBirch = 3, BarkEuca = 4, BarkPalm = 5, BarkSmooth = 6, BarkScrub = 7;
        public const int BarkLayers = 8;

        /// <summary>Texture coordinates of a leaf atlas cell (a little inset so neighbours never bleed in).</summary>
        public static void CellRect(int cell, out float u0, out float v0, out float u1, out float v1)
        {
            int cx = cell % Cols, cy = cell / Cols;
            float pad = 3f / AtlasN, s = (float)Cell / AtlasN;
            u0 = cx * s + pad; u1 = (cx + 1) * s - pad;
            v0 = cy * s + pad; v1 = (cy + 1) * s - pad;
        }

        // ------------------------------------------------------------ leaf atlas

        sealed class Canvas
        {
            public readonly int W, H;
            public readonly float[] C;      // r, g, b, a (linear-ish 0..1 values of sRGB colour)
            public Canvas(int w, int h) { W = w; H = h; C = new float[w * h * 4]; }

            public void Over(int x, int y, float r, float g, float b, float a)
            {
                if (a <= 0 || x < 0 || y < 0 || x >= W || y >= H) return;
                if (a > 1) a = 1;
                int o = (y * W + x) * 4;
                float k = 1 - a;
                C[o] = r * a + C[o] * k; C[o + 1] = g * a + C[o + 1] * k; C[o + 2] = b * a + C[o + 2] * k;
                C[o + 3] = a + C[o + 3] * k;
            }
        }

        /// <summary>A leaf outline: half width (0..1 of the leaf's width) at t along it (0 stalk end, 1 tip).</summary>
        delegate float Shape(float t);

        static float Ovate(float t) { return (float)Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.75)), 0.85); }
        static float Lance(float t) { return (float)Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.9)), 1.25); }
        static float Needle(float t) { return t > 0.85f ? (1 - t) / 0.15f : 1f; }
        static float Lobed(float t) { return Ovate(t) * (0.78f + 0.22f * (float)Math.Abs(Math.Cos(t * Math.PI * 4.5))); }
        static float Palmate(float t) { return (float)Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.55)), 0.7) * (0.75f + 0.25f * (float)Math.Abs(Math.Cos(t * Math.PI * 2.5))); }
        static float Birchy(float t) { return (float)Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.6)), 0.8) * (1f - 0.04f * (float)Math.Abs(Math.Sin(t * 60))); }
        static float Sickle(float t) { return (float)Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.7)), 1.4); }

        /// <summary>Paint one leaf: from (x, y) at angle ang (radians, 0 = +x), len and wid in pixels.</summary>
        static void Leaf(Canvas cv, float x, float y, float ang, float len, float wid, Shape shape,
            float r, float g, float b, float vein, float sheen, float bend, Random rnd)
        {
            float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
            float reach = len + wid;
            int x0 = (int)Math.Floor(x - reach), x1 = (int)Math.Ceiling(x + reach);
            int y0 = (int)Math.Floor(y - reach), y1 = (int)Math.Ceiling(y + reach);
            if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0; if (x1 > cv.W - 1) x1 = cv.W - 1; if (y1 > cv.H - 1) y1 = cv.H - 1;
            float hw = wid * 0.5f;
            float side = rnd.NextDouble() < 0.5 ? 1 : -1;     // which half catches the light
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    float dx = px + 0.5f - x, dy = py + 0.5f - y;
                    float s = dx * ca + dy * sa, q = -dx * sa + dy * ca;
                    if (s < -1 || s > len + 1) continue;
                    float t = s / len;
                    q -= bend * len * t * t;                       // the leaf curls to one side
                    float w = shape(Math.Max(0, Math.Min(1, t))) * hw;
                    float cov = Math.Min(w - Math.Abs(q) + 0.5f, Math.Min(s + 0.5f, len - s + 0.5f));
                    if (cov <= 0) continue;
                    if (cov > 1) cov = 1;
                    float across = w > 0.01f ? q / w : 0;
                    // darker at the stalk, one half brighter, a pale midrib, a glossy band
                    float k = 0.78f + 0.3f * t;
                    k *= 1f + 0.12f * across * side;
                    float mid = Math.Abs(q) < 0.9f ? vein : 0;
                    float gloss = sheen * (float)Math.Exp(-Math.Pow((across * side - 0.35f) / 0.3f, 2));
                    float rr = r * k + mid * 0.18f + gloss, gg = g * k + mid * 0.16f + gloss, bb = b * k + mid * 0.06f + gloss * 0.9f;
                    cv.Over(px, py, rr, gg, bb, cov);
                }
        }

        /// <summary>A stalk or twig: a line from a to b, width w0 to w1.</summary>
        static void Twig(Canvas cv, float ax, float ay, float bx, float by, float w0, float w1, float r, float g, float b)
        {
            float dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            if (l2 < 1e-4f) return;
            float m = Math.Max(w0, w1) * 0.5f + 1;
            int x0 = (int)Math.Floor(Math.Min(ax, bx) - m), x1 = (int)Math.Ceiling(Math.Max(ax, bx) + m);
            int y0 = (int)Math.Floor(Math.Min(ay, by) - m), y1 = (int)Math.Ceiling(Math.Max(ay, by) + m);
            if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0; if (x1 > cv.W - 1) x1 = cv.W - 1; if (y1 > cv.H - 1) y1 = cv.H - 1;
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    float qx = px + 0.5f - ax, qy = py + 0.5f - ay;
                    float t = Math.Max(0, Math.Min(1, (qx * dx + qy * dy) / l2));
                    float ex = qx - dx * t, ey = qy - dy * t;
                    float d = (float)Math.Sqrt(ex * ex + ey * ey);
                    float hw = (w0 + (w1 - w0) * t) * 0.5f;
                    float cov = hw - d + 0.5f;
                    if (cov <= 0) continue;
                    float k = 0.85f + 0.3f * (1 - Math.Abs(d / Math.Max(hw, 0.5f)));
                    cv.Over(px, py, r * k, g * k, b * k, Math.Min(1, cov));
                }
        }

        static float Rf(Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        struct Green { public float R, G, B; public Green(uint c) { R = (c >> 16 & 255) / 255f; G = (c >> 8 & 255) / 255f; B = (c & 255) / 255f; } }

        static Green Pick(Random rnd, Green a, Green b)
        {
            float t = (float)rnd.NextDouble(), k = Rf(rnd, 0.82f, 1.12f);
            return new Green(0) { R = (a.R + (b.R - a.R) * t) * k, G = (a.G + (b.G - a.G) * t) * k, B = (a.B + (b.B - a.B) * t) * k };
        }

        /// <summary>
        /// A sprig of broad leaves: a stem from the bottom middle up the cell, side twigs off it, leaves along all
        /// of them. Leaves are painted far side first so the near ones overlap them.
        /// </summary>
        static void Sprig(Canvas cv, Random rnd, int leaves, float lenMin, float lenMax, float widRatio, Shape shape,
            Green a, Green b, float sheen, int sideTwigs, float droop, float twigW)
        {
            int W = cv.W;
            var pts = new List<float[]>();      // x, y, direction of each spot where a leaf can hang
            float sx = W * 0.5f + Rf(rnd, -20, 20), sy = 4;
            float ang = (float)(Math.PI / 2) + Rf(rnd, -0.15f, 0.15f);
            float step = (cv.H - 50) / 12f;
            var stem = new List<float[]>();
            for (int i = 0; i <= 12; i++)
            {
                stem.Add(new[] { sx, sy, ang });
                float nx = sx + (float)Math.Cos(ang) * step, ny = sy + (float)Math.Sin(ang) * step;
                Twig(cv, sx, sy, nx, ny, twigW * (1 - i / 14f) + 1, twigW * (1 - (i + 1) / 14f) + 1, 0.36f, 0.27f, 0.18f);
                sx = nx; sy = ny; ang += Rf(rnd, -0.12f, 0.12f) - droop * 0.02f * (float)Math.Cos(ang);
                pts.Add(new[] { sx, sy, ang });
            }
            for (int s = 0; s < sideTwigs; s++)
            {
                float[] at = stem[2 + rnd.Next(9)];
                float side = s % 2 == 0 ? 1 : -1;
                float a2 = at[2] + side * Rf(rnd, 0.5f, 1.1f);
                float x = at[0], y = at[1];
                float l = Rf(rnd, 0.22f, 0.4f) * W;
                int segs = 5;
                for (int i = 0; i < segs; i++)
                {
                    float nx = x + (float)Math.Cos(a2) * l / segs, ny = y + (float)Math.Sin(a2) * l / segs;
                    Twig(cv, x, y, nx, ny, twigW * 0.6f + 0.6f, twigW * 0.45f + 0.5f, 0.38f, 0.29f, 0.2f);
                    x = nx; y = ny;
                    a2 += -side * 0.05f - droop * 0.08f;
                    pts.Add(new[] { x, y, a2 });
                }
            }
            for (int i = 0; i < leaves; i++)
            {
                float[] p = pts[rnd.Next(pts.Count)];
                float side = rnd.NextDouble() < 0.5 ? 1 : -1;
                float la = p[2] + side * Rf(rnd, 0.35f, 1.25f) - droop * 0.4f * (float)Math.Cos(p[2]);
                float len = Rf(rnd, lenMin, lenMax);
                // keep leaves inside the cell
                float ex = p[0] + (float)Math.Cos(la) * len, ey = p[1] + (float)Math.Sin(la) * len;
                if (ex < 6 || ex > W - 6 || ey > cv.H - 6 || ey < 6) { len *= 0.6f; ex = p[0] + (float)Math.Cos(la) * len; ey = p[1] + (float)Math.Sin(la) * len; if (ex < 4 || ex > W - 4 || ey > cv.H - 4 || ey < 4) continue; }
                Green c = Pick(rnd, a, b);
                // a short stalk first
                float stalk = len * 0.12f;
                float lx = p[0] + (float)Math.Cos(la) * stalk, ly = p[1] + (float)Math.Sin(la) * stalk;
                Twig(cv, p[0], p[1], lx, ly, 1.6f, 1.2f, c.R * 0.8f, c.G * 0.8f, c.B * 0.6f);
                Leaf(cv, lx, ly, la, len, len * widRatio, shape, c.R, c.G, c.B, 0.35f, sheen, Rf(rnd, -0.12f, 0.12f), rnd);
            }
        }

        /// <summary>A conifer spray: a twig with side twigs, all bristling with needles.</summary>
        static void Spray(Canvas cv, Random rnd, Green a, Green b, float needleLen, float needleW, float comb, int density, float spread)
        {
            int W = cv.W;
            var twigs = new List<float[]>();     // x0, y0, x1, y1
            float sx = W * 0.5f, sy = 2, ang = (float)(Math.PI / 2);
            int segs = 10; float step = (cv.H - 30) / (float)segs;
            for (int i = 0; i < segs; i++)
            {
                float nx = sx + (float)Math.Cos(ang) * step, ny = sy + (float)Math.Sin(ang) * step;
                twigs.Add(new[] { sx, sy, nx, ny, 1f - i / (float)segs });
                // side twigs, alternating, shorter towards the tip
                float sl = W * spread * (1 - i / (float)(segs + 2)) * Rf(rnd, 0.8f, 1.1f);
                if (i > 0 && i < segs - 1)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float a2 = ang + side * Rf(rnd, 0.75f, 1.0f);
                        twigs.Add(new[] { sx, sy, sx + (float)Math.Cos(a2) * sl, sy + (float)Math.Sin(a2) * sl, 0.5f });
                    }
                sx = nx; sy = ny; ang += Rf(rnd, -0.06f, 0.06f);
            }
            foreach (float[] t in twigs) Twig(cv, t[0], t[1], t[2], t[3], 1.5f + 2.5f * t[4], 1.2f, 0.33f, 0.25f, 0.17f);
            foreach (float[] t in twigs)
            {
                float dx = t[2] - t[0], dy = t[3] - t[1], l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l < 1) continue;
                float ta = (float)Math.Atan2(dy, dx);
                int n = (int)(l / W * density);
                for (int k = 0; k < n; k++)
                {
                    float f = (float)rnd.NextDouble();
                    float px = t[0] + dx * f, py = t[1] + dy * f;
                    float side = rnd.NextDouble() < 0.5 ? 1 : -1;
                    // comb = needles in two flat rows (fir); otherwise all round (spruce), seen as a fan
                    float na = ta + side * (comb > 0 ? Rf(rnd, 1.0f, 1.45f) : Rf(rnd, 0.35f, 1.4f));
                    float nl = needleLen * Rf(rnd, 0.75f, 1.15f) * (0.65f + 0.35f * (1 - f * 0.5f));
                    Green c = Pick(rnd, a, b);
                    Leaf(cv, px, py, na, nl, needleW, Needle, c.R, c.G, c.B, 0.05f, 0.04f, 0, rnd);
                }
            }
        }

        /// <summary>A pine tuft: long needles bursting from the middle of the cell.</summary>
        static void Tuft(Canvas cv, Random rnd, Green a, Green b, int count)
        {
            float cx = cv.W * 0.5f, cy = cv.H * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float ang = Rf(rnd, 0, 6.2832f);
                float len = cv.W * Rf(rnd, 0.24f, 0.47f);
                float start = Rf(rnd, 4, 30);
                Green c = Pick(rnd, a, b);
                Leaf(cv, cx + (float)Math.Cos(ang) * start, cy + (float)Math.Sin(ang) * start, ang + Rf(rnd, -0.15f, 0.15f), len, Rf(rnd, 2.6f, 3.6f), Needle,
                    c.R, c.G, c.B, 0.03f, 0.05f, Rf(rnd, -0.08f, 0.08f), rnd);
            }
            Twig(cv, cx, cy - 20, cx, cy + 20, 9, 7, 0.45f, 0.32f, 0.2f);
        }

        /// <summary>A palm frond: a midrib up the middle, long narrow leaflets both sides sweeping towards the tip.</summary>
        static void PaintFrond(Canvas cv, Random rnd, Green a, Green b, bool dead)
        {
            int W = cv.W, H = cv.H;
            float cx = W * 0.5f;
            Green rib = dead ? new Green(0x8a7454) : new Green(0xb8b070);
            int n = 46;
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                float y = 6 + t * (H - 20);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (dead && rnd.NextDouble() < 0.3) continue;
                    float ang = (float)(Math.PI / 2) - side * Rf(rnd, 0.95f, 1.25f) * (1 - 0.35f * t);
                    float len = W * 0.5f * (float)Math.Sin(Math.PI * (0.12 + 0.88 * t)) * Rf(rnd, 0.9f, 1.05f) * (dead ? Rf(rnd, 0.5f, 1f) : 1f);
                    Green c = Pick(rnd, a, b);
                    Leaf(cv, cx + side * 2, y, ang, len, Rf(rnd, 9, 13), Lance, c.R, c.G, c.B, 0.25f, dead ? 0 : 0.08f, side * Rf(rnd, 0.02f, 0.08f), rnd);
                }
            }
            Twig(cv, cx, 0, cx, H - 8, 12, 3, rib.R, rib.G, rib.B);
        }

        /// <summary>Fine feathery acacia leaves: little twigs with rows of tiny leaflets.</summary>
        static void Feather(Canvas cv, Random rnd, Green a, Green b, int pinnae, float leafLen)
        {
            int W = cv.W;
            for (int i = 0; i < pinnae; i++)
            {
                float x = Rf(rnd, 40, W - 40), y = Rf(rnd, 40, cv.H - 40);
                float ang = Rf(rnd, 0, 6.2832f), len = Rf(rnd, 50, 110);
                float ex = x + (float)Math.Cos(ang) * len, ey = y + (float)Math.Sin(ang) * len;
                Twig(cv, x, y, ex, ey, 1.6f, 1.0f, 0.4f, 0.36f, 0.25f);
                int k = (int)(len / 5);
                Green c0 = Pick(rnd, a, b);
                for (int j = 0; j < k; j++)
                {
                    float f = (j + 0.5f) / k;
                    float px = x + (ex - x) * f, py = y + (ey - y) * f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float k2 = Rf(rnd, 0.9f, 1.1f);
                        Leaf(cv, px, py, ang + side * 1.25f, leafLen * Rf(rnd, 0.8f, 1.1f), leafLen * 0.42f, Ovate, c0.R * k2, c0.G * k2, c0.B * k2, 0f, 0.03f, 0, rnd);
                    }
                }
            }
        }

        /// <summary>Cypress scale foliage: dense little fans of branchlets filling an oval.</summary>
        static void Scales(Canvas cv, Random rnd, Green a, Green b, int fans)
        {
            float cx = cv.W * 0.5f, cy = cv.H * 0.5f;
            Twig(cv, cx, 0, cx, cv.H * 0.85f, 6, 2, 0.35f, 0.26f, 0.18f);
            for (int i = 0; i < fans; i++)
            {
                float ang = Rf(rnd, 0, 6.2832f), d = (float)Math.Sqrt(rnd.NextDouble());
                float x = cx + (float)Math.Cos(ang) * d * cv.W * 0.42f, y = cy + (float)Math.Sin(ang) * d * cv.H * 0.46f;
                float fa = (float)(Math.PI / 2) + Rf(rnd, -0.9f, 0.9f);
                Green c = Pick(rnd, a, b);
                for (int k = 0; k < 5; k++)
                {
                    float la = fa + (k - 2) * 0.32f;
                    Leaf(cv, x, y, la, Rf(rnd, 22, 38), Rf(rnd, 6, 9), Lance, c.R, c.G, c.B, 0.02f, 0.03f, 0, rnd);
                }
            }
        }

        static void PaintCell(int cell, Canvas cv)
        {
            var rnd = new Random(1000 + cell * 17);
            switch (cell)
            {
                case Oak:
                    Sprig(cv, rnd, 62, 70, 105, 0.5f, Lobed, new Green(0x3e5a22), new Green(0x5f7a2c), 0.05f, 6, 0.3f, 6); break;
                case Maple:
                    Sprig(cv, rnd, 48, 80, 115, 0.78f, Palmate, new Green(0x4a6e22), new Green(0x78953a), 0.06f, 5, 0.2f, 5); break;
                case Birch:
                    Sprig(cv, rnd, 110, 38, 56, 0.6f, Birchy, new Green(0x5e8a2a), new Green(0x8aa83e), 0.07f, 7, 1.4f, 3); break;
                case Jungle:
                    Sprig(cv, rnd, 16, 150, 210, 0.42f, Ovate, new Green(0x2a5018), new Green(0x46702a), 0.16f, 4, 0.5f, 7); break;
                case Spruce:
                    Spray(cv, rnd, new Green(0x22402a), new Green(0x3a5a3a), 26, 3.2f, 0, 900, 0.36f); break;
                case Fir:
                    Spray(cv, rnd, new Green(0x1e3e22), new Green(0x33582c), 30, 3.8f, 1, 760, 0.40f); break;
                case PineTuft:
                    Tuft(cv, rnd, new Green(0x34522a), new Green(0x5a7438), 430); break;
                case Cypress:
                    Scales(cv, rnd, new Green(0x1f3a1c), new Green(0x35552a), 520); break;
                case Frond:
                    PaintFrond(cv, rnd, new Green(0x3e6420), new Green(0x6a8a30), false); break;
                case DeadFrond:
                    PaintFrond(cv, rnd, new Green(0x7a6040), new Green(0xa08a60), true); break;
                case Acacia:
                    Feather(cv, rnd, new Green(0x4e6224), new Green(0x748a36), 130, 7); break;
                case Eucalyptus:
                    Sprig(cv, rnd, 34, 120, 175, 0.2f, Sickle, new Green(0x5a7458), new Green(0x84987a), 0.05f, 5, 0.6f, 4); break;
                case Scrub:
                    Sprig(cv, rnd, 120, 22, 36, 0.42f, Lance, new Green(0x5e6640), new Green(0x8a8c60), 0.02f, 8, 0.1f, 4); break;
            }
        }

        /// <summary>
        /// The leaf atlas with its smaller copies (each level RGBA, bottom row first). The smaller copies keep the
        /// same share of solid pixels at the 0.5 cut-off, so trees do not go bald in the distance.
        /// </summary>
        public static List<byte[]> Atlas()
        {
            var full = new float[AtlasN * AtlasN * 4];
            for (int cell = 0; cell < CellCount; cell++)
            {
                var cv = new Canvas(Cell, Cell);
                PaintCell(cell, cv);
                int ox = cell % Cols * Cell, oy = cell / Cols * Cell;
                for (int y = 0; y < Cell; y++)
                    Array.Copy(cv.C, y * Cell * 4, full, ((oy + y) * AtlasN + ox) * 4, Cell * 4);
            }
            Unpremultiply(full);
            return Mips(full, AtlasN, AtlasN, Cell);
        }

        /// <summary>Colour stays as painted where there is any; see-through pixels take the colour of solid ones nearby.</summary>
        static void Unpremultiply(float[] c)
        {
            for (int i = 0; i < c.Length; i += 4)
            {
                float a = c[i + 3];
                if (a > 0.004f) { c[i] /= a; c[i + 1] /= a; c[i + 2] /= a; }
            }
        }

        /// <summary>Fill see-through pixels with the colour of the nearest solid ones (no dark fringe when blurred).</summary>
        internal static void Bleed(float[] c, int w, int h, int passes)
        {
            var solid = new bool[w * h];
            for (int i = 0; i < w * h; i++) solid[i] = c[i * 4 + 3] > 0.05f;
            var next = new List<int>();
            for (int pass = 0; pass < passes; pass++)
            {
                next.Clear();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (solid[i]) continue;
                        float r = 0, g = 0, b = 0; int n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                int j = yy * w + xx;
                                if (!solid[j]) continue;
                                r += c[j * 4]; g += c[j * 4 + 1]; b += c[j * 4 + 2]; n++;
                            }
                        if (n == 0) continue;
                        c[i * 4] = r / n; c[i * 4 + 1] = g / n; c[i * 4 + 2] = b / n;
                        next.Add(i);
                    }
                if (next.Count == 0) break;
                foreach (int i in next) solid[i] = true;
            }
        }

        /// <summary>
        /// A full mip chain from an unpremultiplied image. block: size of independent cells (no averaging across
        /// cell borders), 0 for none. Alpha is rescaled per level to keep the coverage at a 0.5 cut-off.
        /// </summary>
        internal static List<byte[]> Mips(float[] img, int w, int h, int block)
        {
            if (block > 0)
            {
                // see-through pixels first take the average colour of their cell, then the nearest solid colour
                for (int by = 0; by < h; by += block)
                    for (int bx = 0; bx < w; bx += block)
                    {
                        double r = 0, g = 0, b = 0, n = 0;
                        for (int y = by; y < Math.Min(h, by + block); y++)
                            for (int x = bx; x < Math.Min(w, bx + block); x++)
                            {
                                int o = (y * w + x) * 4;
                                float a = img[o + 3];
                                if (a < 0.05f) continue;
                                r += img[o] * a; g += img[o + 1] * a; b += img[o + 2] * a; n += a;
                            }
                        if (n <= 0) { r = 0.25; g = 0.3; b = 0.15; n = 1; }
                        for (int y = by; y < Math.Min(h, by + block); y++)
                            for (int x = bx; x < Math.Min(w, bx + block); x++)
                            {
                                int o = (y * w + x) * 4;
                                if (img[o + 3] >= 0.05f) continue;
                                img[o] = (float)(r / n); img[o + 1] = (float)(g / n); img[o + 2] = (float)(b / n);
                            }
                    }
                Bleed(img, w, h, 3);
            }
            else Bleed(img, w, h, 24);
            var levels = new List<byte[]>();
            float baseCov = Coverage(img, 1f);
            float[] cur = img; int cw = w, ch = h;
            while (true)
            {
                float scale = 1f;
                if (levels.Count > 0 && baseCov > 0)
                {
                    // find the alpha scale that keeps the same share of pixels above 0.5
                    float lo = 0.5f, hi = 4f;
                    for (int it = 0; it < 14; it++)
                    {
                        float m = (lo + hi) * 0.5f;
                        if (Coverage(cur, m) < baseCov) lo = m; else hi = m;
                    }
                    scale = (lo + hi) * 0.5f;
                }
                levels.Add(ToBytes(cur, scale));
                if (cw == 1 && ch == 1) break;
                int nw = Math.Max(1, cw / 2), nh = Math.Max(1, ch / 2);
                var nx = new float[nw * nh * 4];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                        for (int k = 0; k < 4; k++)
                        {
                            int x0 = Math.Min(cw - 1, x * 2), x1 = Math.Min(cw - 1, x * 2 + 1), y0 = Math.Min(ch - 1, y * 2), y1 = Math.Min(ch - 1, y * 2 + 1);
                            nx[(y * nw + x) * 4 + k] = 0.25f * (cur[(y0 * cw + x0) * 4 + k] + cur[(y0 * cw + x1) * 4 + k] + cur[(y1 * cw + x0) * 4 + k] + cur[(y1 * cw + x1) * 4 + k]);
                        }
                cur = nx; cw = nw; ch = nh;
            }
            return levels;
        }

        static float Coverage(float[] c, float scale)
        {
            int n = 0, total = c.Length / 4;
            for (int i = 3; i < c.Length; i += 4) if (c[i] * scale >= 0.5f) n++;
            return (float)n / total;
        }

        static byte[] ToBytes(float[] c, float alphaScale)
        {
            var b = new byte[c.Length];
            for (int i = 0; i < c.Length; i++)
            {
                float v = (i & 3) == 3 ? c[i] * alphaScale : c[i];
                int q = (int)(v * 255f + 0.5f);
                b[i] = (byte)(q < 0 ? 0 : q > 255 ? 255 : q);
            }
            return b;
        }

        // ------------------------------------------------------------ bark

        static uint Hash(int x, int y, int s)
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)s * 0xcb1ab31fu;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return h;
        }

        static float Rnd(int x, int y, int s) { return (Hash(x, y, s) & 0xffffff) / 16777215f; }

        /// <summary>Value noise that repeats every px by py lattice cells (u, v in 0..1 over the tile).</summary>
        static float Noise(float u, float v, int px, int py, int seed)
        {
            float x = u * px, y = v * py;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int x0 = ((ix % px) + px) % px, x1 = (x0 + 1) % px, y0 = ((iy % py) + py) % py, y1 = (y0 + 1) % py;
            float a = Rnd(x0, y0, seed), b = Rnd(x1, y0, seed), c = Rnd(x0, y1, seed), d = Rnd(x1, y1, seed);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        static float Fbm(float u, float v, int px, int py, int oct, int seed)
        {
            float s = 0, amp = 0.5f, norm = 0;
            for (int o = 0; o < oct; o++)
            {
                s += Noise(u, v, px, py, seed + o * 31) * amp;
                norm += amp; amp *= 0.5f; px *= 2; py *= 2;
            }
            return s / norm;
        }

        /// <summary>Tileable cells: distance to the nearest and second-nearest jittered point, and the nearest cell's id.</summary>
        static void Worley(float u, float v, int px, int py, int seed, out float d1, out float d2, out uint id)
        {
            float x = u * px, y = v * py;
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            d1 = d2 = 9; id = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = ix + dx, cy = iy + dy;
                    int wx = ((cx % px) + px) % px, wy = ((cy % py) + py) % py;
                    float jx = cx + Rnd(wx, wy, seed), jy = cy + Rnd(wx, wy, seed + 1);
                    float ex = x - jx, ey = y - jy, d = (float)Math.Sqrt(ex * ex + ey * ey);
                    if (d < d1) { d2 = d1; d1 = d; id = Hash(wx, wy, seed + 2); }
                    else if (d < d2) d2 = d;
                }
        }

        static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        /// <summary>One bark texture, RGBA bottom row first, BarkW wide (once round the trunk) by BarkH (up it).</summary>
        public static byte[] Bark(int layer)
        {
            var img = new byte[BarkW * BarkH * 4];
            for (int y = 0; y < BarkH; y++)
                for (int x = 0; x < BarkW; x++)
                {
                    float u = (x + 0.5f) / BarkW, v = (y + 0.5f) / BarkH;
                    float r, g, b;
                    BarkAt(layer, u, v, out r, out g, out b);
                    int o = (y * BarkW + x) * 4;
                    img[o] = (byte)(Clamp01(r) * 255); img[o + 1] = (byte)(Clamp01(g) * 255); img[o + 2] = (byte)(Clamp01(b) * 255); img[o + 3] = 255;
                }
            return img;
        }

        static void BarkAt(int layer, float u, float v, out float r, out float g, out float b)
        {
            float n, d1, d2; uint id;
            switch (layer)
            {
                case BarkOak:
                {
                    // deep vertical furrows that wander and join, grey-brown ridges, a little lichen
                    float warp = Fbm(u, v, 4, 4, 3, 11) - 0.5f;
                    float ridge = Fbm(u + warp * 0.08f, v, 14, 3, 4, 12);
                    ridge = 1 - Math.Abs(ridge * 2 - 1);
                    float k = (float)Math.Pow(Clamp01(ridge * 1.25f), 1.6);
                    float fine = Fbm(u, v, 32, 32, 3, 13);
                    r = 0.13f + 0.30f * k + 0.06f * fine; g = 0.10f + 0.25f * k + 0.05f * fine; b = 0.08f + 0.19f * k + 0.04f * fine;
                    float lichen = Clamp01((Fbm(u, v, 6, 6, 3, 14) - 0.62f) * 5) * k;
                    r += (0.42f - r) * lichen * 0.6f; g += (0.46f - g) * lichen * 0.6f; b += (0.34f - b) * lichen * 0.6f;
                    break;
                }
                case BarkSpruce:
                {
                    // thin grey-brown scales
                    Worley(u, v, 10, 22, 21, out d1, out d2, out id);
                    float edge = Clamp01((d2 - d1) * 5f);
                    float tone = (id & 255) / 255f;
                    n = Fbm(u, v, 24, 24, 3, 22);
                    float k = edge * (0.75f + 0.25f * tone);
                    r = 0.16f + 0.22f * k + 0.05f * n; g = 0.12f + 0.17f * k + 0.04f * n; b = 0.10f + 0.13f * k + 0.03f * n;
                    break;
                }
                case BarkPine:
                {
                    // orange-brown plates split by dark cracks
                    Worley(u, v, 6, 9, 31, out d1, out d2, out id);
                    float edge = Clamp01((d2 - d1) * 4f);
                    float tone = (id & 255) / 255f;
                    n = Fbm(u, v, 20, 20, 4, 32);
                    float k = (float)Math.Pow(edge, 0.6);
                    r = (0.10f + (0.36f + 0.10f * tone) * k) * (0.85f + 0.3f * n);
                    g = (0.08f + (0.24f + 0.05f * tone) * k) * (0.85f + 0.3f * n);
                    b = (0.06f + (0.17f + 0.03f * tone) * k) * (0.85f + 0.3f * n);
                    break;
                }
                case BarkBirch:
                {
                    // chalk white with dark horizontal slits and a few black patches
                    n = Fbm(u, v, 8, 8, 4, 41);
                    r = 0.86f + 0.08f * n; g = 0.84f + 0.08f * n; b = 0.80f + 0.08f * n;
                    float slit = 0;
                    {
                        float x = u * 6, y = v * 64;
                        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int cx = ((ix + dx) % 6 + 6) % 6;
                            if (Rnd(cx, iy, 42) > 0.45f) continue;
                            float px = ix + dx + Rnd(cx, iy, 43), len = 0.25f + 0.5f * Rnd(cx, iy, 44);
                            float ex = (x - px) / len, ey = (y - iy - 0.5f) / 0.18f;
                            float q = 1 - (ex * ex + ey * ey);
                            if (q > slit) slit = q;
                        }
                    }
                    float patch = Clamp01((Fbm(u, v, 4, 6, 4, 45) - 0.66f) * 8);
                    float dark = Math.Max(Clamp01(slit * 3), patch);
                    r += (0.12f - r) * dark; g += (0.11f - g) * dark; b += (0.10f - b) * dark;
                    break;
                }
                case BarkEuca:
                {
                    // smooth, shedding in patches: cream, grey, olive and salmon
                    Worley(u + (Fbm(u, v, 4, 4, 2, 51) - 0.5f) * 0.2f, v, 3, 6, 52, out d1, out d2, out id);
                    float tone = (id & 1023) / 1023f;
                    n = Fbm(u, v, 16, 16, 3, 53);
                    if (tone < 0.35f) { r = 0.82f; g = 0.78f; b = 0.68f; }
                    else if (tone < 0.6f) { r = 0.62f; g = 0.62f; b = 0.58f; }
                    else if (tone < 0.82f) { r = 0.58f; g = 0.60f; b = 0.46f; }
                    else { r = 0.80f; g = 0.60f; b = 0.48f; }
                    float edge = Clamp01((d2 - d1) * 12f);
                    float k = (0.75f + 0.25f * edge) * (0.92f + 0.12f * n);
                    r *= k; g *= k; b *= k;
                    break;
                }
                case BarkPalm:
                {
                    // grey-brown rings where old fronds fell, fibrous between
                    float y = v * 10 + (Fbm(u, v, 4, 2, 2, 61) - 0.5f) * 0.6f;
                    float f = y - (float)Math.Floor(y);
                    float ring = (float)Math.Exp(-Math.Pow((f - 0.15f) / 0.06f, 2));
                    n = Fbm(u, v, 40, 8, 3, 62);
                    float k = 0.82f + 0.25f * n - ring * 0.45f + 0.12f * f;
                    r = 0.42f * k; g = 0.37f * k; b = 0.31f * k;
                    break;
                }
                case BarkSmooth:
                {
                    // smooth grey bark with soft mottling
                    n = Fbm(u, v, 6, 10, 5, 71);
                    float m = Fbm(u, v, 24, 6, 3, 72);
                    float k = 0.8f + 0.3f * n - 0.1f * Clamp01((m - 0.6f) * 4);
                    r = 0.40f * k; g = 0.39f * k; b = 0.36f * k;
                    break;
                }
                default:
                {
                    // dark, twisted, stringy
                    float warp = Fbm(u, v, 3, 3, 2, 81) - 0.5f;
                    float ridge = Fbm(u + warp * 0.2f + v * 0.15f, v, 18, 4, 4, 82);
                    ridge = 1 - Math.Abs(ridge * 2 - 1);
                    float k = (float)Math.Pow(ridge, 1.3);
                    r = 0.12f + 0.22f * k; g = 0.10f + 0.18f * k; b = 0.08f + 0.14f * k;
                    break;
                }
            }
        }
    }
}
