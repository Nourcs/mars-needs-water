using System;
using System.IO;
using System.IO.Compression;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Real ground heights and sea depths from the open Terrarium terrain tiles (Overture has no elevation).
    /// Values are real metres above sea level.
    /// </summary>
    public sealed class Dem
    {
        public const int Px = 256;
        const string Url = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/";
        readonly string cacheDir;
        readonly Once<long, float[]> tiles = new Once<long, float[]>(160);
        static readonly float[] Flat = new float[Px * Px];

        public Dem(string cacheRoot) { cacheDir = Path.Combine(cacheRoot, "terrain"); }

        /// <summary>Height at a global pixel (zoom z has 256 * 2^z pixels across).</summary>
        public float Pixel(int z, long gx, long gy)
        {
            long n = 1L << z;
            long span = n * Px;
            gx = ((gx % span) + span) % span;
            if (gy < 0) gy = 0;
            if (gy >= span) gy = span - 1;
            int tx = (int)(gx / Px), ty = (int)(gy / Px);
            float[] t = Tile(z, tx, ty);
            return t[(int)(gy - (long)ty * Px) * Px + (int)(gx - (long)tx * Px)];
        }

        float[] Tile(int z, int x, int y)
        {
            long key = ((long)z << 56) | ((long)x << 28) | (long)y;
            return tiles.Get(key, () =>
            {
                string path = Path.Combine(cacheDir, z.ToString(), x + "_" + y + ".png");
                byte[] png = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if (png == null)
                {
                    png = Net.Get(Url + z + "/" + x + "/" + y + ".png");
                    Net.WriteFile(path, png ?? new byte[0]);
                }
                if (png == null || png.Length == 0) return Flat;
                int w, h, bpp;
                byte[] px = Png.Decode(png, out w, out h, out bpp);
                if (px == null || w != Px || h != Px) return Flat;
                var t = new float[Px * Px];
                for (int i = 0; i < Px * Px; i++)
                {
                    int o = i * bpp;
                    t[i] = px[o] * 256f + px[o + 1] + px[o + 2] / 256f - 32768f;
                }
                return t;
            });
        }
    }

    /// <summary>Minimal PNG reader: 8-bit RGB or RGBA, not interlaced. Enough for terrain tiles.</summary>
    public static class Png
    {
        public static byte[] Decode(byte[] d, out int w, out int h, out int bpp)
        {
            w = h = bpp = 0;
            if (d.Length < 33 || d[0] != 0x89 || d[1] != 0x50) return null;
            int p = 8, colour = 0, depth = 0, interlace = 0;
            var idat = new MemoryStream();
            while (p + 8 <= d.Length)
            {
                int len = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
                string type = "" + (char)d[p + 4] + (char)d[p + 5] + (char)d[p + 6] + (char)d[p + 7];
                int body = p + 8;
                if (type == "IHDR")
                {
                    w = (d[body] << 24) | (d[body + 1] << 16) | (d[body + 2] << 8) | d[body + 3];
                    h = (d[body + 4] << 24) | (d[body + 5] << 16) | (d[body + 6] << 8) | d[body + 7];
                    depth = d[body + 8]; colour = d[body + 9]; interlace = d[body + 12];
                }
                else if (type == "IDAT") idat.Write(d, body, len);
                else if (type == "IEND") break;
                p = body + len + 4;
            }
            if (depth != 8 || interlace != 0 || (colour != 2 && colour != 6)) return null;
            bpp = colour == 2 ? 3 : 4;
            int stride = w * bpp;
            byte[] raw = new byte[(stride + 1) * h];
            byte[] z = idat.ToArray();
            using (var inf = new DeflateStream(new MemoryStream(z, 2, z.Length - 2), CompressionMode.Decompress))
            {
                int got = 0;
                while (got < raw.Length)
                {
                    int r = inf.Read(raw, got, raw.Length - got);
                    if (r <= 0) break;
                    got += r;
                }
                if (got < raw.Length) return null;
            }
            byte[] o = new byte[stride * h];
            for (int y = 0; y < h; y++)
            {
                int f = raw[y * (stride + 1)];
                int src = y * (stride + 1) + 1, dst = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? o[dst + x - bpp] : 0;
                    int b = y > 0 ? o[dst + x - stride] : 0;
                    int c = (x >= bpp && y > 0) ? o[dst + x - stride - bpp] : 0;
                    int v = raw[src + x];
                    switch (f)
                    {
                        case 1: v += a; break;
                        case 2: v += b; break;
                        case 3: v += (a + b) >> 1; break;
                        case 4:
                            int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
                            v += (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                            break;
                    }
                    o[dst + x] = (byte)v;
                }
            }
            return o;
        }
    }
}
