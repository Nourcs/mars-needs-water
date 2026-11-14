using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>Paints polygons and lines into a tile's ground picture, plus a mask of what each pixel is.</summary>
    public sealed class Raster
    {
        public const byte Land = 0, Ocean = 1, Water = 2, Forest = 3, Paved = 4, Green = 5, Built = 6, Keep = 255;

        public readonly int N;
        public readonly byte[] Rgba;
        public readonly byte[] Mask;
        /// <summary>What the ground is made of at each pixel (GroundKind).</summary>
        public readonly byte[] Kind;
        readonly List<float>[] rows;

        public Raster(int n, uint baseColour)
        {
            N = n;
            Rgba = new byte[n * n * 4];
            Mask = new byte[n * n];
            Kind = new byte[n * n];
            rows = new List<float>[n];
            byte r = (byte)(baseColour >> 16), g = (byte)(baseColour >> 8), b = (byte)baseColour;
            for (int i = 0; i < n * n; i++) { Rgba[i * 4] = r; Rgba[i * 4 + 1] = g; Rgba[i * 4 + 2] = b; Rgba[i * 4 + 3] = 255; }
        }

        void Span(int y, int x0, int x1, byte r, byte g, byte b, byte mask, byte kind)
        {
            if (x0 < 0) x0 = 0;
            if (x1 > N - 1) x1 = N - 1;
            int o = (y * N + x0) * 4, m = y * N + x0;
            for (int x = x0; x <= x1; x++, o += 4, m++)
            {
                Rgba[o] = r; Rgba[o + 1] = g; Rgba[o + 2] = b;
                if (mask != Keep) { Mask[m] = mask; Kind[m] = kind; }
            }
        }

        static byte KindFor(byte mask, byte kind) { return kind == GroundKind.FromMask ? GroundKind.OfMask(mask) : kind; }

        /// <summary>Even-odd fill of rings given as x,y pixel pairs.</summary>
        public void Fill(List<float[]> rings, uint colour, byte mask, byte kind = GroundKind.FromMask)
        {
            kind = KindFor(mask, kind);
            byte r = (byte)(colour >> 16), g = (byte)(colour >> 8), b = (byte)colour;
            int yMin = N, yMax = -1;
            foreach (float[] ring in rings)
            {
                int n = ring.Length / 2;
                if (n < 3) continue;
                for (int i = 0, j = n - 1; i < n; j = i, i++)
                {
                    float x0 = ring[j * 2], y0 = ring[j * 2 + 1], x1 = ring[i * 2], y1 = ring[i * 2 + 1];
                    if (y0 == y1) continue;
                    if (y0 > y1) { float t = x0; x0 = x1; x1 = t; t = y0; y0 = y1; y1 = t; }
                    int ya = (int)Math.Ceiling(y0 - 0.5f), yb = (int)Math.Ceiling(y1 - 0.5f) - 1;
                    if (ya < 0) ya = 0;
                    if (yb > N - 1) yb = N - 1;
                    float slope = (x1 - x0) / (y1 - y0);
                    for (int y = ya; y <= yb; y++)
                    {
                        if (rows[y] == null) rows[y] = new List<float>(8);
                        rows[y].Add(x0 + (y + 0.5f - y0) * slope);
                    }
                    if (ya < yMin) yMin = ya;
                    if (yb > yMax) yMax = yb;
                }
            }
            for (int y = yMin; y <= yMax; y++)
            {
                List<float> xs = rows[y];
                if (xs == null || xs.Count == 0) continue;
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    int a = (int)Math.Ceiling(xs[i] - 0.5f), c = (int)Math.Ceiling(xs[i + 1] - 0.5f) - 1;
                    if (c >= 0 && a <= N - 1 && c >= a) Span(y, a, c, r, g, b, mask, kind);
                }
                xs.Clear();
            }
        }

        /// <summary>A line with round ends and joints, width in pixels.</summary>
        public void Line(float[] pts, float width, uint colour, byte mask, byte kind = GroundKind.FromMask)
        {
            kind = KindFor(mask, kind);
            byte r = (byte)(colour >> 16), g = (byte)(colour >> 8), b = (byte)colour;
            float h = Math.Max(width * 0.5f, 0.5f), h2 = h * h;
            int n = pts.Length / 2;
            for (int i = 0; i + 1 < n; i++)
            {
                float ax = pts[i * 2], ay = pts[i * 2 + 1], bx = pts[i * 2 + 2], by = pts[i * 2 + 3];
                float minX = Math.Min(ax, bx) - h, maxX = Math.Max(ax, bx) + h, minY = Math.Min(ay, by) - h, maxY = Math.Max(ay, by) + h;
                if (maxX < 0 || maxY < 0 || minX > N || minY > N) continue;
                int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(N - 1, (int)Math.Ceiling(maxY));
                int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(N - 1, (int)Math.Ceiling(maxX));
                float dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                for (int y = y0; y <= y1; y++)
                {
                    float py = y + 0.5f - ay;
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f - ax;
                        float t = len2 > 0 ? (px * dx + py * dy) / len2 : 0;
                        if (t < 0) t = 0; else if (t > 1) t = 1;
                        float ex = px - t * dx, ey = py - t * dy;
                        if (ex * ex + ey * ey <= h2)
                        {
                            int o = (y * N + x) * 4;
                            Rgba[o] = r; Rgba[o + 1] = g; Rgba[o + 2] = b;
                            if (mask != Keep) { Mask[y * N + x] = mask; Kind[y * N + x] = kind; }
                        }
                    }
                }
            }
        }

        public byte MaskAt(double u, double v)
        {
            int x = (int)(u * N), y = (int)(v * N);
            if (x < 0) x = 0; else if (x > N - 1) x = N - 1;
            if (y < 0) y = 0; else if (y > N - 1) y = N - 1;
            return Mask[y * N + x];
        }
    }
}
