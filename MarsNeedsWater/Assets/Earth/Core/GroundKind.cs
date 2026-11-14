using System;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// What the ground is made of at each spot: grass, sand, rock, snow and so on. A square's ground picture keeps
    /// the kind in its alpha channel; the ground shader draws a detailed texture for it up close, and the colour
    /// picture (made here to look like that texture from afar) takes over in the distance. Grass and stones near
    /// the player (EarthGrass) are chosen from the same kinds.
    /// </summary>
    public static class GroundKind
    {
        // same order as the texture layers in Resources/Earth/Ground (see tools/ground/bake_ground.py)
        public const byte Grass = 0, DryGrass = 1, Forest = 2, Farm = 3, Sand = 4, Rock = 5, Snow = 6, Paved = 7, Dirt = 8, Mud = 9;
        public const int Count = 10;
        public const byte FromMask = 254;     // let the raster pick the kind from the mask it is given
        public static readonly string[] Names = { "grass", "drygrass", "forest", "farm", "sand", "rock", "snow", "paved", "dirt", "mud" };

        /// <summary>Average colour of each kind's texture, sRGB (written by bake_ground.py in means.txt).</summary>
        public static readonly uint[] Mean = { 0x5f7d33, 0x8b7b51, 0x574623, 0x585a2e, 0xbca882, 0x776f64, 0xd2d7de, 0x6f6e6c, 0x756047, 0x3a3722 };

        /// <summary>The same averages in linear light (the shader divides by these to carry the map colour into the texture).</summary>
        public static readonly float[] MeanLinear =
        {
            0.1139f, 0.2065f, 0.0332f,  0.2572f, 0.1997f, 0.0830f,  0.0949f, 0.0619f, 0.0165f,  0.0980f, 0.1029f, 0.0277f,
            0.5005f, 0.3938f, 0.2234f,  0.1834f, 0.1592f, 0.1274f,  0.6466f, 0.6789f, 0.7288f,  0.1597f, 0.1563f, 0.1496f,
            0.1768f, 0.1164f, 0.0624f,  0.0417f, 0.0384f, 0.0158f,
        };

        /// <summary>The kind a raster mask stands for when nothing more is known.</summary>
        public static byte OfMask(byte mask)
        {
            switch (mask)
            {
                case Raster.Forest: return Forest;
                case Raster.Paved: case Raster.Built: return Paved;
                case Raster.Ocean: return Sand;     // the sea floor near shore
                case Raster.Water: return Mud;      // lake and river beds
                default: return Grass;
            }
        }

        public static bool Natural(byte k) { return k != Paved; }

        // ------------------------------------------------------------ colours seen from afar

        // Real ground is not one colour: each kind has a few looks, picked per map feature (a field of wheat
        // next to a green one) and blended by large soft noise.
        static readonly uint[][] Looks =
        {
            new uint[] { 0x5f7d33, 0x6a8638, 0x56742f, 0x6f8a40, 0x627a36 },            // grass
            new uint[] { 0x8b7b51, 0x9a8656, 0x7d7650, 0xa08f60, 0x86804f },            // dry grass, scrub
            new uint[] { 0x3d4a26, 0x36442a, 0x44502a, 0x3a4524 },                      // forest floor as seen through the canopy
            new uint[] { 0x6b7a35, 0x8a8a45, 0xa8955a, 0x7a6448, 0x5d7a30, 0xb5a265, 0x85703f },  // fields: green crop, ripening, stubble, ploughed
            new uint[] { 0xbca882, 0xc8b38a, 0xb09d78, 0xd0bf98 },                      // sand
            new uint[] { 0x776f64, 0x847b6e, 0x6c675f, 0x8a8172 },                      // rock
            new uint[] { 0xe4e8ee, 0xdde3ea, 0xeef1f5 },                                // snow
            new uint[] { 0x8a8884, 0x7d7b77, 0x95928c },                                // concrete, paving
            new uint[] { 0x8a7050, 0x9a8060, 0x7a6448, 0xb09470 },                      // bare earth
            new uint[] { 0x4a4630, 0x3f3e2a, 0x545034 },                                // mud, marsh
        };

        public static uint Look(byte kind, uint hash)
        {
            uint[] l = Looks[kind < Count ? kind : 0];
            return l[(hash >> 7) % (uint)l.Length];
        }

        // ------------------------------------------------------------ noise, the same everywhere on the globe

        static uint Hash(int x, int y, int s)
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)s * 0xcb1ab31fu;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return h;
        }

        static float Value(double x, double y, int s)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = (float)(x - xi), fy = (float)(y - yi);
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = (Hash(xi, yi, s) & 0xffff) / 65535f, b = (Hash(xi + 1, yi, s) & 0xffff) / 65535f;
            float c = (Hash(xi, yi + 1, s) & 0xffff) / 65535f, d = (Hash(xi + 1, yi + 1, s) & 0xffff) / 65535f;
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        /// <summary>Soft noise 0..1 over global map units (zoom-14 tiles times 256), period about 'size' units.</summary>
        public static float Noise(double gx, double gy, double size, int seed)
        {
            double x = gx / size, y = gy / size;
            return Value(x, y, seed) * 0.55f + Value(x * 2.13 + 17.1, y * 2.13 + 3.7, seed + 1) * 0.3f + Value(x * 4.7 + 5.3, y * 4.7 + 11.9, seed + 2) * 0.15f;
        }

        // ------------------------------------------------------------ from Overture's base theme

        /// <summary>land_cover subtypes (ESA WorldCover).</summary>
        public static bool OfCover(string sub, out byte kind)
        {
            switch (sub)
            {
                case "forest": case "mangrove": kind = Forest; return true;
                case "grass": kind = Grass; return true;
                case "shrub": case "moss": kind = DryGrass; return true;
                case "crop": kind = Farm; return true;
                case "barren": kind = Dirt; return true;
                case "urban": kind = Paved; return true;
                case "wetland": kind = Mud; return true;
                case "snow": kind = Snow; return true;
            }
            kind = Grass; return false;
        }

        /// <summary>land subtypes (OpenStreetMap natural areas).</summary>
        public static bool OfLand(string sub, out byte kind)
        {
            switch (sub)
            {
                case "sand": case "desert": case "reef": kind = Sand; return true;
                case "rock": kind = Rock; return true;
                case "glacier": kind = Snow; return true;
                case "forest": kind = Forest; return true;
                case "grass": kind = Grass; return true;
                case "shrub": kind = DryGrass; return true;
                case "wetland": kind = Mud; return true;
            }
            kind = Grass; return false;
        }

        /// <summary>land_use subtypes and classes. Built-up land is paving; homes, schools and parks have lawns and gardens.</summary>
        public static byte OfUse(string sub, string cls, byte mask)
        {
            if (mask == Raster.Paved) return Paved;
            switch (sub)
            {
                case "park": case "recreation": case "golf": case "cemetery": case "managed": case "horticulture": case "campground":
                case "residential": case "education": case "medical": case "religious":
                    return Grass;
                case "agriculture": return cls == "vineyard" ? Dirt : cls == "orchard" ? Grass : cls == "meadow" ? Grass : Farm;
                case "construction": case "landfill": case "resource_extraction": return Dirt;
                case "airfield": case "military": return DryGrass;
            }
            return Paved;
        }

        public static uint UseLook(string sub, string cls, byte kind, uint hash)
        {
            if (sub == "golf") return 0x6e9a3c;                       // watered and mown
            if (sub == "residential") return Mix(Look(Grass, hash), 0x8a8478, 0.25f);   // lawns, drives and patios
            if (sub == "cemetery" || sub == "park") return Mix(Look(Grass, hash), 0x6e9440, 0.3f);
            return Look(kind, hash);
        }

        // ------------------------------------------------------------ rules from the terrain

        /// <summary>
        /// Real metres above which snow lies all year at this latitude (about 5,300 m in the tropics, 3,000 m in the
        /// Alps, 1,700 m in Norway and Alaska, sea level in the far north and south).
        /// </summary>
        public static double SnowLine(double lat)
        {
            double a = Math.Abs(lat);
            return Math.Max(-200.0, 5300.0 - 95.0 * Math.Max(0.0, a - 22.0) - (a > 62 ? (a - 62) * 40.0 : 0.0));
        }

        /// <summary>
        /// Finishes a square's ground picture: steep slopes turn to rock, high and cold ground to snow, low ground
        /// along the sea to beach sand; every natural pixel gets the soft colour variation real land has; the kind
        /// goes into the alpha channel. hs: ground heights in real metres on the (G+1) x (G+1) grid, north row first.
        /// </summary>
        public static void Finish(Raster ras, int Z, int X, int Y, float size, float[] hs, int G)
        {
            int N = ras.N, V = G + 1;
            byte[] kind = ras.Kind, mask = ras.Mask, rgba = ras.Rgba;
            double cellReal = size * Geo.Scale / G;

            // slope (degrees) at each grid point
            var slope = new float[V * V];
            for (int j = 0; j <= G; j++)
                for (int i = 0; i <= G; i++)
                {
                    int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, G), j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, G);
                    double gx = (hs[j * V + i1] - hs[j * V + i0]) / ((i1 - i0) * cellReal);
                    double gy = (hs[j1 * V + i] - hs[j0 * V + i]) / ((j1 - j0) * cellReal);
                    slope[j * V + i] = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) / Geo.Deg);
                }
            // coarse squares see a smoothed terrain, so their slopes read low: ask for less there
            float steep = 36f - 3.5f * Math.Max(0, 14 - Z);

            // distance (real metres) from the sea and from lakes and rivers
            float pxReal = (float)(size * Geo.Scale / N);
            float[] seaDist = Distance(mask, N, Raster.Ocean, 80f / pxReal);
            float[] lakeDist = Distance(mask, N, Raster.Water, 20f / pxReal);

            double lat = Geo.TileLat(Y + 0.5, Z);
            double snowLine = SnowLine(lat);
            double scale = Math.Pow(2, 14 - Z) * 256.0 / N;     // global units per pixel
            double gx0 = (double)X * Math.Pow(2, 14 - Z) * 256.0, gy0 = (double)Y * Math.Pow(2, 14 - Z) * 256.0;
            float gG = (float)G / N;

            for (int y = 0; y < N; y++)
            {
                float fy = (y + 0.5f) * gG;
                int j = Math.Min((int)fy, G - 1);
                float ty = fy - j;
                double gy = gy0 + (y + 0.5) * scale;
                for (int x = 0; x < N; x++)
                {
                    int p = y * N + x;
                    byte k = kind[p], m = mask[p];
                    if (m == Raster.Ocean || m == Raster.Water || k == Paved)
                    {
                        rgba[p * 4 + 3] = k;
                        continue;
                    }
                    float fx = (x + 0.5f) * gG;
                    int i = Math.Min((int)fx, G - 1);
                    float tx = fx - i;
                    int a = j * V + i;
                    float h = Bi(hs[a], hs[a + 1], hs[a + V], hs[a + V + 1], tx, ty);
                    float s = Bi(slope[a], slope[a + 1], slope[a + V], slope[a + V + 1], tx, ty);
                    double gx = gx0 + (x + 0.5) * scale;

                    // two noises: a broad one (hundreds of metres) and a finer one that roughens every border
                    float broad = Noise(gx, gy, 900, 11);
                    float fine = Noise(gx, gy, 40, 23);
                    byte was = k;

                    // beaches: low, gentle ground right beside the sea
                    float sd = seaDist[p] * pxReal;
                    float beach = 18f + 40f * broad + 12f * (fine - 0.5f);
                    if (sd < beach && h < 6f + 4f * fine && s < 16f && k != Forest) k = Sand;
                    else if (sd < beach * 0.4f && h < 4f && s < 20f) k = Sand;
                    // a strip of bare earth or sand round lakes and along rivers
                    else if (lakeDist[p] * pxReal < 3f + 5f * fine && s < 18f && k != Forest) k = fine > 0.55f ? Sand : Dirt;

                    // high mountains: bare rock above the tree line, snow above the snow line
                    double over = h - (snowLine + (broad - 0.5) * 500.0 + (fine - 0.5) * 140.0);
                    if (over > 0 && s < 48f) k = Snow;
                    else if (over > -700 && k != Snow && k != Paved)
                    {
                        float rocky = (float)((over + 700) / 700.0);      // 0 at the tree line, 1 at the snow line
                        if (fine < rocky * 0.9f) k = Rock;
                        else if (k == Forest || k == Grass || k == Farm) k = DryGrass;
                    }
                    if (k == Snow && s > 40f + 10f * fine) k = Rock;

                    // cliffs and steep slopes are bare rock, with scree and dry grass where it eases off
                    float st = steep + 10f * (fine - 0.5f) + 6f * (broad - 0.5f);
                    if (s > st && k != Snow) k = Rock;
                    else if (s > st - 7f && (k == Grass || k == Farm) && fine > 0.6f) k = DryGrass;

                    if (k != was)
                    {
                        uint c = Look(k, (uint)(broad * 997) * 7919u);
                        if (k == Snow) c = Mix(c, 0xffffff, 0.2f);
                        rgba[p * 4] = (byte)(c >> 16); rgba[p * 4 + 1] = (byte)(c >> 8); rgba[p * 4 + 2] = (byte)c;
                    }
                    kind[p] = k;
                    rgba[p * 4 + 3] = k;

                    // soft variation: brighter and darker, greener and drier patches, as real land has
                    float mid = Noise(gx, gy, 140, 41);
                    float v = 0.8f + 0.2f * broad + 0.2f * mid + 0.1f * (fine - 0.5f);
                    float r = rgba[p * 4] * v, g = rgba[p * 4 + 1] * v, b = rgba[p * 4 + 2] * v;
                    if (k == Grass || k == Farm)
                    {
                        float dry = Math.Max(0f, Noise(gx, gy, 2500, 31) * 0.7f + mid * 0.5f - 0.6f) * 1.6f;
                        r += (139 - r) * dry * 0.6f; g += (123 - g) * dry * 0.6f; b += (81 - b) * dry * 0.6f;
                    }
                    rgba[p * 4] = Clamp(r); rgba[p * 4 + 1] = Clamp(g); rgba[p * 4 + 2] = Clamp(b);
                }
            }
        }

        static float Bi(float a, float b, float c, float d, float tx, float ty)
        {
            return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
        }

        static byte Clamp(float v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }

        static uint Mix(uint a, uint b, float t)
        {
            uint r = (uint)(((a >> 16) & 255) + (((b >> 16) & 255) - (float)((a >> 16) & 255)) * t);
            uint g = (uint)(((a >> 8) & 255) + (((b >> 8) & 255) - (float)((a >> 8) & 255)) * t);
            uint bl = (uint)((a & 255) + ((b & 255) - (float)(a & 255)) * t);
            return (r << 16) | (g << 8) | bl;
        }

        /// <summary>Distance in pixels to the nearest pixel with this mask, up to 'limit' (two-pass chamfer).</summary>
        static float[] Distance(byte[] mask, int N, byte what, float limit)
        {
            var d = new float[N * N];
            bool any = false;
            for (int p = 0; p < N * N; p++) { if (mask[p] == what) { d[p] = 0; any = true; } else d[p] = 1e6f; }
            if (!any) return d;
            const float D = 1.41421f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int p = y * N + x;
                    float v = d[p];
                    if (v == 0) continue;
                    if (x > 0) v = Math.Min(v, d[p - 1] + 1);
                    if (y > 0)
                    {
                        v = Math.Min(v, d[p - N] + 1);
                        if (x > 0) v = Math.Min(v, d[p - N - 1] + D);
                        if (x < N - 1) v = Math.Min(v, d[p - N + 1] + D);
                    }
                    d[p] = v;
                }
            for (int y = N - 1; y >= 0; y--)
                for (int x = N - 1; x >= 0; x--)
                {
                    int p = y * N + x;
                    float v = d[p];
                    if (v == 0) continue;
                    if (x < N - 1) v = Math.Min(v, d[p + 1] + 1);
                    if (y < N - 1)
                    {
                        v = Math.Min(v, d[p + N] + 1);
                        if (x < N - 1) v = Math.Min(v, d[p + N + 1] + D);
                        if (x > 0) v = Math.Min(v, d[p + N - 1] + D);
                    }
                    d[p] = Math.Min(v, 1e6f);
                }
            return d;
        }

        /// <summary>A small copy of the colour picture (RGB, n x n), for tinting grass blades to the ground under them.</summary>
        public static byte[] Shrink(byte[] rgba, int N, int n)
        {
            var o = new byte[n * n * 3];
            int f = N / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int yy = 0; yy < f; yy++)
                        for (int xx = 0; xx < f; xx++)
                        {
                            int p = ((y * f + yy) * N + x * f + xx) * 4;
                            r += rgba[p]; g += rgba[p + 1]; b += rgba[p + 2];
                        }
                    int q = (y * n + x) * 3, c = f * f;
                    o[q] = (byte)(r / c); o[q + 1] = (byte)(g / c); o[q + 2] = (byte)(b / c);
                }
            return o;
        }
    }
}
