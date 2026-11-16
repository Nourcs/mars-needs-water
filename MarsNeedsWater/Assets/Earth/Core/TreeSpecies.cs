using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>One tree standing in a map square (tile-local game metres), drawn by EarthTrees.</summary>
    public struct TreeSpot
    {
        public float X, Y, Z;         // foot of the trunk, tile-local
        public float Height;          // game metres
        public float Width;           // crown width factor (1 = the model's own shape)
        public float Yaw;             // radians
        public byte Species, Variant;
        public uint Tint;             // leaf colour multiplier, RGB at 128 = 1.0
        public float Rand;            // 0..1, wind phase and the like
    }

    /// <summary>
    /// Which kind of tree grows where. A rough map of the world's forests from latitude, longitude, height above
    /// the sea and the ground under the tree (GroundKind): spruce and pine in the north and high up, oak, maple and
    /// birch in temperate lands, stone pine and cypress round the Mediterranean and in California, gum trees in
    /// Australia, acacias on the savanna, rainforest giants and palms in the tropics, scrub where it is dry.
    /// </summary>
    public static class TreeSpecies
    {
        public const byte Spruce = 0, Fir = 1, Pine = 2, StonePine = 3, Oak = 4, Maple = 5, Birch = 6, Cypress = 7,
            Palm = 8, DatePalm = 9, Acacia = 10, Eucalyptus = 11, Jungle = 12, Scrub = 13;
        public const int Count = 14;
        public const int Variants = 3;
        /// <summary>Trees against buildings: real tree heights would tower over the low buildings of the scaled world, and crowns are as wide as they are tall, so both are brought down.</summary>
        public const float HeightScale = 0.6f, CrownScale = 0.8f;
        public static readonly string[] Names =
        {
            "spruce", "fir", "pine", "stone pine", "oak", "maple", "birch", "cypress",
            "coconut palm", "date palm", "acacia", "eucalyptus", "rainforest tree", "scrub",
        };

        // real heights in metres (min, max)
        static readonly float[] HMin = { 14, 20, 12, 9, 10, 9, 9, 10, 9, 8, 5, 14, 22, 2.5f, };
        static readonly float[] HMax = { 32, 42, 27, 17, 24, 22, 19, 22, 22, 20, 11, 38, 46, 6.0f, };

        // leaf colour multiplier of each kind (0x80 = as drawn in the leaf texture, which already has the kind's
        // own green): nudges the hue per kind, then each tree gets its own shade on top
        static readonly uint[] Leaf =
        {
            0x7a8484, 0x748080, 0x848478, 0x80866e, 0x828a6c, 0x8a8e70, 0x8c9070, 0x74807a,
            0x888a74, 0x86846e, 0x868470, 0x80868a, 0x7c8a70, 0x848478,
        };

        static uint Mix(uint h)
        {
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return h;
        }

        static float F(uint h, int shift) { return ((h >> shift) & 1023) / 1023f; }

        struct W { public byte S; public float P; public W(byte s, float p) { S = s; P = p; } }

        static byte Roll(uint h, params W[] ws)
        {
            float sum = 0;
            foreach (W w in ws) sum += w.P;
            float r = F(h, 3) * sum;
            foreach (W w in ws) { if (r < w.P) return w.S; r -= w.P; }
            return ws[ws.Length - 1].S;
        }

        static bool In(double lat, double lon, double la0, double la1, double lo0, double lo1)
        {
            return lat >= la0 && lat <= la1 && lon >= lo0 && lon <= lo1;
        }

        /// <summary>
        /// Pick a tree for a spot. elev is real metres above the sea, kind the GroundKind there, street true for
        /// a single tree from the map (a street or garden tree). Returns 255 where no tree should grow.
        /// </summary>
        public static byte Pick(double lat, double lon, double elev, byte kind, bool street, uint h)
        {
            if (kind == GroundKind.Snow || kind == GroundKind.Paved && !street) return 255;
            double a = Math.Abs(lat);
            bool dry = kind == GroundKind.DryGrass || kind == GroundKind.Sand || kind == GroundKind.Dirt;
            // how close to the tree line (0 low down, 1 at it): the tree line falls from ~3800 m in the tropics
            // to sea level near 70 degrees
            double treeLine = a < 25 ? 3800 : a < 70 ? 3800 - (a - 25) * 84 : 0;
            double alpine = treeLine <= 0 ? 1 : elev / treeLine;
            if (kind == GroundKind.Rock) { if (F(h, 13) > 0.25f) return 255; alpine = Math.Max(alpine, 0.8); }
            if (alpine > 1.02) return 255;

            bool australia = In(lat, lon, -44, -10, 112, 155);
            bool africa = In(lat, lon, -35, 30, -18, 52) || In(lat, lon, 12, 30, 35, 60);
            bool medEurope = In(lat, lon, 30, 45.5, -10, 42) && !In(lat, lon, 43, 46, 5, 17.5);
            bool california = In(lat, lon, 31, 42.5, -124.6, -115);
            bool chile = In(lat, lon, -40, -29, -75, -69);
            bool cape = In(lat, lon, -35, -32, 17, 24);
            bool medit = medEurope || california || chile || cape || In(lat, lon, -36, -30, 114, 122);
            bool pacificNW = In(lat, lon, 42.5, 60, -136, -119);
            bool eastAsia = In(lat, lon, 22, 46, 100, 146);
            bool desertBelt = In(lat, lon, 15, 35, -18, 75) || In(lat, lon, 22, 37, -118, -100);
            bool tropics = a < 23.5;
            bool subtropics = a < 33;

            if (alpine > 0.7) return Roll(h, new W(Spruce, 5), new W(Fir, 3), new W(Pine, 2));
            if (australia)
            {
                if (dry) return Roll(h, new W(Eucalyptus, 4), new W(Acacia, 3), new W(Scrub, 3));
                if (tropics && (kind == GroundKind.Sand && elev < 60 || street)) return Roll(h, new W(Palm, 4), new W(Eucalyptus, 5));
                return Roll(h, new W(Eucalyptus, 9), new W(Acacia, 1));
            }
            if (tropics || (subtropics && !medit && !desertBelt && !eastAsia))
            {
                if (kind == GroundKind.Sand) return elev < 60 ? Roll(h, new W(Palm, 8), new W(Scrub, 1)) : Roll(h, new W(Scrub, 3), new W(Acacia, 2));
                if (africa || In(lat, lon, 5, 30, 68, 90))
                {
                    if (dry || kind == GroundKind.Grass || kind == GroundKind.Farm) return Roll(h, new W(Acacia, 7), new W(Scrub, 2), new W(Palm, 1));
                }
                if (street) return Roll(h, new W(Palm, 5), new W(Jungle, 3), new W(Maple, 2));
                if (dry) return Roll(h, new W(Acacia, 4), new W(Scrub, 4), new W(Palm, 1));
                if (alpine > 0.45) return Roll(h, new W(Jungle, 4), new W(Pine, 3), new W(Oak, 2));
                return Roll(h, new W(Jungle, 8), new W(Palm, 1), new W(Maple, 1));
            }
            if (desertBelt)
            {
                if (street || kind == GroundKind.Grass || kind == GroundKind.Farm) return Roll(h, new W(DatePalm, 6), new W(Scrub, 2), new W(Cypress, 1), new W(Acacia, 1));
                return Roll(h, new W(Scrub, 6), new W(Acacia, 2), new W(DatePalm, 2));
            }
            if (medit)
            {
                if (street) return california && lat < 35.5
                    ? Roll(h, new W(Palm, 4), new W(Maple, 3), new W(Eucalyptus, 2), new W(Cypress, 1))
                    : Roll(h, new W(Maple, 4), new W(Cypress, 2), new W(StonePine, 2), new W(DatePalm, 1), new W(Oak, 1));
                if (dry) return Roll(h, new W(Oak, 3), new W(Scrub, 4), new W(StonePine, 1));
                if (california && lat > 37 && alpine < 0.35) return Roll(h, new W(Fir, 4), new W(Oak, 4), new W(Eucalyptus, 1), new W(Pine, 1));
                if (alpine > 0.4) return Roll(h, new W(Pine, 5), new W(Fir, 3), new W(Oak, 2));
                return Roll(h, new W(Oak, 4), new W(StonePine, 3), new W(Pine, 2), new W(Cypress, 1));
            }
            if (pacificNW) return street ? Roll(h, new W(Maple, 6), new W(Oak, 2), new W(Fir, 2)) : Roll(h, new W(Fir, 7), new W(Spruce, 2), new W(Maple, 1));
            if (a > 58) return Roll(h, new W(Spruce, 5), new W(Pine, 3), new W(Birch, 3));
            if (a > 51 && !In(lat, lon, 45, 56, -11, 25))
                return street ? Roll(h, new W(Birch, 4), new W(Maple, 4), new W(Spruce, 2)) : Roll(h, new W(Spruce, 5), new W(Pine, 3), new W(Birch, 3), new W(Maple, 1));
            if (eastAsia)
            {
                if (street) return Roll(h, new W(Maple, 6), new W(Oak, 2), new W(Pine, 2));
                return Roll(h, new W(Oak, 3), new W(Maple, 3), new W(Pine, 3), new W(Fir, 1));
            }
            // temperate: broadleaf woods, more conifers up the hills
            if (street) return Roll(h, new W(Maple, 6), new W(Oak, 3), new W(Birch, 1));
            if (dry) return Roll(h, new W(Pine, 4), new W(Oak, 3), new W(Scrub, 2));
            if (alpine > 0.4) return Roll(h, new W(Spruce, 5), new W(Fir, 2), new W(Pine, 2), new W(Maple, 1));
            return Roll(h, new W(Oak, 4), new W(Maple, 3), new W(Birch, 2), new W(Pine, 1), new W(Spruce, 1));
        }

        public static byte KindAt(Raster ras, double u, double v)
        {
            int x = (int)(u * ras.N), y = (int)(v * ras.N);
            if (x < 0) x = 0; else if (x > ras.N - 1) x = ras.N - 1;
            if (y < 0) y = 0; else if (y > ras.N - 1) y = ras.N - 1;
            return ras.Kind[y * ras.N + x];
        }

        /// <summary>Soft clumps 0..1 over the ground (gx, gy in game metres), so trees gather in copses and groves.</summary>
        public static float Clump(double gx, double gy)
        {
            double x = gx / 28.0, y = gy / 28.0;
            long ix = (long)Math.Floor(x), iy = (long)Math.Floor(y);
            float fx = (float)(x - ix), fy = (float)(y - iy);
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Cell(ix, iy), b = Cell(ix + 1, iy), c = Cell(ix, iy + 1), d = Cell(ix + 1, iy + 1);
            float n = a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
            return n * n;
        }

        static float Cell(long x, long y) { return (Mix((uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ 0x5bd1e995u) & 0xffff) / 65535f; }

        /// <summary>
        /// Chance of a tree on one 7 m candidate spot: dense in woods, groves in parks, and away from them lone
        /// trees and copses in gardens, fields and savanna (gx, gy: ground position in game metres).
        /// </summary>
        public static float Chance(byte mask, byte kind, double gx, double gy)
        {
            if (mask == Raster.Forest) return kind == GroundKind.Rock ? 0.35f : 0.85f;
            if (mask == Raster.Ocean || mask == Raster.Water || mask == Raster.Paved || mask == Raster.Built) return 0;
            float c = Clump(gx, gy);
            if (mask == Raster.Green) return 0.03f + 0.55f * c;
            switch (kind)
            {
                case GroundKind.Grass: return 0.008f + 0.16f * c * c;        // gardens, meadows
                case GroundKind.DryGrass: return 0.01f + 0.12f * c * c;      // savanna, dry hills
                case GroundKind.Farm: return 0.003f + 0.05f * c * c * c;     // the odd tree and copse among fields
                case GroundKind.Forest: return 0.6f;
                case GroundKind.Dirt: case GroundKind.Sand: return 0.004f * c;
                default: return 0;
            }
        }

        /// <summary>A whole tree spot: species, size, turn and colour, from where it stands and one random number.</summary>
        public static bool Make(double lat, double lon, float x, float y, float z, byte kind, bool street, uint seed, out TreeSpot s)
        {
            s = default(TreeSpot);
            uint h = Mix(seed ^ 0x9e3779b9u), h2 = Mix(seed + 0x632be5abu);
            double elev = y * Geo.Scale;
            byte sp = Pick(lat, lon, elev, kind, street, h);
            if (sp == 255) return false;
            // smaller near the tree line and in town
            double treeLine = Math.Abs(lat) < 25 ? 3800 : 3800 - (Math.Abs(lat) - 25) * 84;
            float alpine = treeLine <= 0 ? 1f : (float)Math.Max(0, Math.Min(1, elev / treeLine));
            float t = F(h2, 0);
            t = t * t * (3 - 2 * t) * 0.6f + t * 0.4f;
            float real = HMin[sp] + (HMax[sp] - HMin[sp]) * t;
            if (street) real *= 0.6f + 0.25f * F(h2, 20);
            if (alpine > 0.75f) real *= 1f - (alpine - 0.75f) * 2.4f;
            if (kind == GroundKind.DryGrass || kind == GroundKind.Rock) real *= 0.8f;
            if (real < 2f) real = 2f;
            s.X = x; s.Y = y; s.Z = z;
            s.Height = Geo.Height(real) * HeightScale;
            s.Width = (0.82f + 0.36f * F(h2, 10)) * CrownScale;
            s.Yaw = F(h, 13) * 6.2831853f;
            s.Species = sp;
            s.Variant = (byte)((h >> 23) % Variants);
            s.Rand = F(h, 22);
            // leaf colour: the kind's own green, a little lighter or darker, warmer or cooler per tree, and drier
            // (more olive and yellow) on dry ground
            uint c = Leaf[sp];
            float r = (c >> 16 & 255) / 128f, g = (c >> 8 & 255) / 128f, b = (c & 255) / 128f;
            float bright = 0.80f + 0.36f * F(h2, 3);
            float warm = (F(h2, 13) - 0.5f) * 0.26f;
            r *= bright * (1 + warm); g *= bright; b *= bright * (1 - warm * 1.4f);
            if (kind == GroundKind.DryGrass || kind == GroundKind.Sand || kind == GroundKind.Dirt) { r *= 1.12f; b *= 0.8f; }
            s.Tint = Pack(r) | Pack(g) << 8 | Pack(b) << 16;
            return true;
        }

        static uint Pack(float v) { int i = (int)(v * 128f + 0.5f); return (uint)(i < 0 ? 0 : i > 255 ? 255 : i); }
    }
}
