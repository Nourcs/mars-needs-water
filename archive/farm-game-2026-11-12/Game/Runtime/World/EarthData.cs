using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The real Earth, baked into byte grids in Resources/Earth (equirectangular, row 0 = 90°N, column 0 = 180°W):
    /// earth_height (4096 × 2048: the ground everywhere, sea and lake floors included. 64 = sea level, 65-255 land
    /// elevation from NASA Blue Marble topography; 0-63 below sea level, stored as a step s = 64 - level that the
    /// game turns into metres with <see cref="SeaDepth"/>: real Natural Earth ocean depths and real lake depths),
    /// earth_lakes (2048 × 1024: each lake's surface height in land levels, 0 = sea level), earth_ice and earth_biome
    /// (2048 × 1024, Natural Earth glaciers plus Blue Marble colour: 0 water, 1 ice, 2 bare, 3 grass, 4 forest).
    /// Read-only after loading, so it is safe as static data.
    /// </summary>
    public static class EarthData
    {
        public const int W = 4096, H = 2048, LW = 2048, LH = 1024;
        public const int SeaLevel = 64;
        public enum Biome : byte { Water = 0, Ice = 1, Bare = 2, Grass = 3, Forest = 4 }

        static byte[] _height, _ice, _biome, _lakes;
        static short[] _terrain;                    // measured heights in steps of _tstep metres (earth_terrain); null = the old picture heights
        static float _tstep = 0.04f;
        static int _tw, _th, _iw = LW, _ih = LH, _kw = LW, _kh = LH;

        /// <summary>True when the measured terrain is loaded and <see cref="Metres"/> can be used.</summary>
        public static bool Measured => _terrain != null;

        public static bool Load()
        {
            if (_height != null) return true;
            var i = Resources.Load<TextAsset>("Earth/earth_ice");
            var b = Resources.Load<TextAsset>("Earth/earth_biome");
            if (i == null || b == null) { Debug.LogError("Earth data missing from Resources/Earth."); return false; }
            _ice = i.bytes;
            _biome = b.bytes;
            LoadTerrain();
            if (_terrain == null)
            {
                var h = Resources.Load<TextAsset>("Earth/earth_height");
                if (h == null) { Debug.LogError("Earth heights missing from Resources/Earth."); return false; }
                _height = h.bytes;
                if (_height.Length != W * H) return false;
            }
            else _height = new byte[0];
            var lk = Resources.Load<TextAsset>("Earth/earth_lakes");
            _lakes = lk != null ? lk.bytes : null;
            if (_lakes != null && _lakes.Length == 4 * LW * LH) { _kw = LW * 2; _kh = LH * 2; }
            else if (_lakes != null && _lakes.Length != LW * LH) _lakes = null;
            if (_lakes == null) Debug.LogWarning("earth_lakes.bytes missing: every lake will sit at sea level.");
            if (_ice.Length == 4 * LW * LH) { _iw = LW * 2; _ih = LH * 2; }
            return _ice.Length == _iw * _ih && _biome.Length == LW * LH;
        }

        /// <summary>
        /// The measured terrain (tools/earth/bake_terrain.py): "MNWF", width, height, metres per step, then a gzip of
        /// every cell stored as what is left after guessing it from its west, north and north-west neighbours, all
        /// the low bytes first and then all the high bytes.
        /// </summary>
        static void LoadTerrain()
        {
            // The file is big, so it comes in parts (earth_terrain_a, _b, ...) that are simply joined end to end.
            var parts = new System.Collections.Generic.List<byte[]>();
            int total = 0;
            for (char c = 'a'; c <= 'h'; c++)
            {
                var ta = Resources.Load<TextAsset>("Earth/earth_terrain_" + c);
                if (ta == null) break;
                parts.Add(ta.bytes); total += parts[parts.Count - 1].Length;
            }
            if (parts.Count == 0) return;
            try
            {
                var b = parts[0];
                if (parts.Count > 1)
                {
                    b = new byte[total];
                    int at = 0;
                    foreach (var p in parts) { System.Buffer.BlockCopy(p, 0, b, at, p.Length); at += p.Length; }
                }
                if (b.Length < 16 || b[0] != 'M' || b[1] != 'N' || b[2] != 'W' || b[3] != 'F') return;
                int w = System.BitConverter.ToInt32(b, 4), h = System.BitConverter.ToInt32(b, 8), n = w * h;
                if (w < 64 || h < 32 || n > 1 << 27) return;
                var raw = new byte[n * 2];
                using (var ms = new System.IO.MemoryStream(b, 16, b.Length - 16))
                using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
                {
                    int got = 0;
                    while (got < raw.Length)
                    {
                        int r = gz.Read(raw, got, raw.Length - got);
                        if (r <= 0) break;
                        got += r;
                    }
                    if (got != raw.Length) { Debug.LogWarning("earth_terrain.bytes is cut short: using the old heights."); return; }
                }
                var t = new short[n];
                for (int y = 0, k = 0; y < h; y++)
                {
                    int run = 0;
                    for (int x = 0; x < w; x++, k++)
                    {
                        run += (short)(raw[k] | (raw[n + k] << 8));
                        t[k] = (short)(run + (y > 0 ? t[k - w] : 0));
                    }
                }
                _terrain = t; _tw = w; _th = h; _tstep = System.BitConverter.ToSingle(b, 12);
            }
            catch (System.Exception e) { Debug.LogWarning("earth_terrain.bytes could not be read (" + e.Message + "): using the old heights."); }
        }

        /// <summary>
        /// Measured height in game metres above sea level (negative under the sea), smooth between samples. Real
        /// metres × 0.0235: the planet's 1 : 255 scale with heights exaggerated × 6. Only when <see cref="Measured"/>.
        /// </summary>
        public static float Metres(float lat, float lon)
        {
            float fx = (lon + 180f) / 360f * _tw - 0.5f, fy = (90f - lat) / 180f * _th - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            int xa = ((x0 % _tw) + _tw) % _tw, xb = xa + 1 == _tw ? 0 : xa + 1;
            int ya = Mathf.Clamp(y0, 0, _th - 1), yb = Mathf.Clamp(y0 + 1, 0, _th - 1);
            float a = _terrain[ya * _tw + xa], b = _terrain[ya * _tw + xb], c = _terrain[yb * _tw + xa], d = _terrain[yb * _tw + xb];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty) * _tstep;
        }

        /// <summary>
        /// Unit direction from the planet centre: +Y = north pole, +Z = 0° longitude, −X = 90°E. (Unity is
        /// left-handed, so east has to be −X for the continents to read the right way round from outside.)
        /// </summary>
        public static Vector3 Dir(float latDeg, float lonDeg)
        {
            float la = latDeg * Mathf.Deg2Rad, lo = lonDeg * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Cos(la) * Mathf.Sin(lo), Mathf.Sin(la), Mathf.Cos(la) * Mathf.Cos(lo));
        }

        public static void LatLon(Vector3 dir, out float latDeg, out float lonDeg)
        {
            latDeg = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            lonDeg = Mathf.Atan2(-dir.x, dir.z) * Mathf.Rad2Deg;
        }

        /// <summary>Bilinear height level (0..255, 64 = sea level) at a latitude and longitude.</summary>
        public static float Level(float lat, float lon)
        {
            float fx = (lon + 180f) / 360f * W - 0.5f, fy = (90f - lat) / 180f * H - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            float a = Px(x0, y0), b = Px(x0 + 1, y0), c = Px(x0, y0 + 1), d = Px(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static float Px(int x, int y)
        {
            x = ((x % W) + W) % W;          // longitude wraps
            y = Mathf.Clamp(y, 0, H - 1);   // latitude clamps at the poles
            return _height[y * W + x];
        }

        /// <summary>
        /// Metres below sea level for a sea step s (1..64): 0.65 s + 0.055 s² runs from 0.7 m to 267 m, which is the
        /// real depth × 0.0235 (the same 1 : 255 scale, exaggerated × 6, as the land). Fractional s blends smoothly.
        /// </summary>
        public static float SeaDepth(float s) => s <= 0f ? 0f : 0.65f * s + 0.055f * s * s;

        /// <summary>Height of the water surface in land levels at a point: a lake's own height, or 0 (sea level).</summary>
        public static int LakeLevel(float lat, float lon) => _lakes == null ? 0 : _lakes[Index(lat, lon, _kw, _kh)];

        public static Biome BiomeAt(float lat, float lon) => (Biome)_biome[LowIndex(lat, lon)];
        public static bool IceAt(float lat, float lon) => _ice[Index(lat, lon, _iw, _ih)] > 0;

        static int Index(float lat, float lon, int w, int h)
        {
            int x = Mathf.FloorToInt((lon + 180f) / 360f * w), y = Mathf.FloorToInt((90f - lat) / 180f * h);
            x = ((x % w) + w) % w;
            y = Mathf.Clamp(y, 0, h - 1);
            return y * w + x;
        }

        static int LowIndex(float lat, float lon)
        {
            int x = Mathf.FloorToInt((lon + 180f) / 360f * LW), y = Mathf.FloorToInt((90f - lat) / 180f * LH);
            x = ((x % LW) + LW) % LW;
            y = Mathf.Clamp(y, 0, LH - 1);
            return y * LW + x;
        }
    }
}
