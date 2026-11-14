using System;
using System.Collections.Generic;
using System.IO;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Reads single map tiles out of a PMTiles v3 archive over HTTP range requests, with a disk cache.
    /// The Overture archives are 20 to 190 GB each, so only the few kilobytes around the player are ever fetched.
    /// </summary>
    public sealed class Pmtiles
    {
        sealed class Dir { public long[] Ids; public long[] Runs; public long[] Lens; public long[] Offs; }

        public readonly string Name;
        public int MinZoom, MaxZoom;
        readonly string url, cacheDir;
        long rootOff, rootLen, leafOff, dataOff;
        bool ready;
        readonly object gate = new object();
        readonly Once<long, Dir> dirs = new Once<long, Dir>(4096);

        public Pmtiles(string baseUrl, string name, string cacheRoot)
        {
            Name = name;
            url = baseUrl + name + ".pmtiles";
            cacheDir = Path.Combine(cacheRoot, name);
        }

        void Open()
        {
            if (ready) return;
            lock (gate)
            {
                if (ready) return;
                string hp = Path.Combine(cacheDir, "header.bin");
                byte[] h = File.Exists(hp) ? File.ReadAllBytes(hp) : null;
                if (h == null || h.Length < 127)
                {
                    h = Net.Get(url, 0, 127);
                    if (h == null) throw new IOException("Map archive not found: " + url);
                    Net.WriteFile(hp, h);
                }
                if (h[0] != (byte)'P' || h[1] != (byte)'M' || h[7] != 3) throw new IOException("Not a PMTiles v3 archive: " + url);
                rootOff = (long)BitConverter.ToUInt64(h, 8);
                rootLen = (long)BitConverter.ToUInt64(h, 16);
                leafOff = (long)BitConverter.ToUInt64(h, 40);
                dataOff = (long)BitConverter.ToUInt64(h, 56);
                MinZoom = h[100]; MaxZoom = h[101];
                ready = true;
            }
        }

        public static long TileId(int z, long x, long y)
        {
            long acc = ((1L << (2 * z)) - 1) / 3;
            long n = 1L << z, d = 0;
            for (long s = n / 2; s > 0; s /= 2)
            {
                long rx = (x & s) > 0 ? 1 : 0, ry = (y & s) > 0 ? 1 : 0;
                d += s * s * ((3 * rx) ^ ry);
                if (ry == 0)
                {
                    if (rx == 1) { x = s - 1 - x; y = s - 1 - y; }
                    long t = x; x = y; y = t;
                }
            }
            return acc + d;
        }

        static long Varint(byte[] b, ref int p)
        {
            long r = 0; int s = 0;
            while (true)
            {
                byte c = b[p++];
                r |= (long)(c & 127) << s; s += 7;
                if (c < 128) return r;
            }
        }

        Dir LoadDir(long off, long len)
        {
            return dirs.Get(off, () =>
            {
                string path = Path.Combine(cacheDir, "dir", off + ".bin");
                byte[] raw = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if (raw == null || raw.Length != len)
                {
                    raw = Net.Get(url, off, len);
                    if (raw == null) throw new IOException("Map index not reachable");
                    Net.WriteFile(path, raw);
                }
                byte[] b = Net.Gunzip(raw);
                int p = 0;
                int n = (int)Varint(b, ref p);
                var d = new Dir { Ids = new long[n], Runs = new long[n], Lens = new long[n], Offs = new long[n] };
                long last = 0;
                for (int i = 0; i < n; i++) { last += Varint(b, ref p); d.Ids[i] = last; }
                for (int i = 0; i < n; i++) d.Runs[i] = Varint(b, ref p);
                for (int i = 0; i < n; i++) d.Lens[i] = Varint(b, ref p);
                for (int i = 0; i < n; i++)
                {
                    long v = Varint(b, ref p);
                    d.Offs[i] = (v == 0 && i > 0) ? d.Offs[i - 1] + d.Lens[i - 1] : v - 1;
                }
                return d;
            });
        }

        /// <summary>The uncompressed tile, or null when the archive has nothing there.</summary>
        public byte[] GetTile(int z, int x, int y)
        {
            string path = Path.Combine(cacheDir, z.ToString(), x + "_" + y + ".bin");
            if (File.Exists(path))
            {
                byte[] cached = File.ReadAllBytes(path);
                return cached.Length == 0 ? null : Net.Gunzip(cached);
            }
            Open();
            long id = TileId(z, x, y);
            long off = rootOff, len = rootLen;
            byte[] found = null;
            for (int depth = 0; depth < 4; depth++)
            {
                Dir d = LoadDir(off, len);
                int i = Array.BinarySearch(d.Ids, id);
                if (i < 0) i = ~i - 1;
                if (i < 0) break;
                if (d.Runs[i] == 0) { off = leafOff + d.Offs[i]; len = d.Lens[i]; continue; }
                if (id - d.Ids[i] < d.Runs[i]) found = Net.Get(url, dataOff + d.Offs[i], d.Lens[i]);
                break;
            }
            Net.WriteFile(path, found ?? new byte[0]);
            return found == null ? null : Net.Gunzip(found);
        }
    }
}
