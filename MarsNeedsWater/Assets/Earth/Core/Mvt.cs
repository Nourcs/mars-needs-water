using System;
using System.Collections.Generic;
using System.Text;

namespace MarsNeedsWater.Earth
{
    public sealed class MvtFeature
    {
        public int Type;              // 1 point, 2 line, 3 polygon
        public int[] Tags;            // key index, value index pairs
        public List<int[]> Parts = new List<int[]>();   // x,y pairs in tile units
    }

    public sealed class MvtLayer
    {
        public string Name;
        public int Extent = 4096;
        public readonly List<MvtFeature> Features = new List<MvtFeature>();
        readonly List<string> keys = new List<string>();
        readonly List<int> valOff = new List<int>(), valLen = new List<int>();
        readonly Dictionary<string, int> keyIndex = new Dictionary<string, int>();
        object[] cache;
        byte[] buf;

        public int Key(string name) { int i; return keyIndex.TryGetValue(name, out i) ? i : -1; }

        public object Get(MvtFeature f, int key)
        {
            if (key < 0 || f.Tags == null) return null;
            for (int i = 0; i + 1 < f.Tags.Length; i += 2)
                if (f.Tags[i] == key) return Value(f.Tags[i + 1]);
            return null;
        }

        public string Str(MvtFeature f, int key) { return Get(f, key) as string; }

        public double Num(MvtFeature f, int key, double fallback)
        {
            object o = Get(f, key);
            if (o is double) return (double)o;
            if (o is long) return (long)o;
            if (o is string) { double d; if (double.TryParse((string)o, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d; }
            return fallback;
        }

        public bool Flag(MvtFeature f, int key) { object o = Get(f, key); return o is bool && (bool)o; }

        object Value(int i)
        {
            if (i < 0 || i >= valOff.Count) return null;
            if (cache == null) cache = new object[valOff.Count];
            object o = cache[i];
            if (o != null) return o;
            int p = valOff[i], end = p + valLen[i];
            o = "";
            while (p < end)
            {
                long k = Mvt.Varint(buf, ref p);
                int field = (int)(k >> 3), wire = (int)(k & 7);
                if (wire == 2) { int l = (int)Mvt.Varint(buf, ref p); if (field == 1) o = Encoding.UTF8.GetString(buf, p, l); p += l; }
                else if (wire == 5) { if (field == 2) o = (double)BitConverter.ToSingle(buf, p); p += 4; }
                else if (wire == 1) { if (field == 3) o = BitConverter.ToDouble(buf, p); p += 8; }
                else
                {
                    long v = Mvt.Varint(buf, ref p);
                    if (field == 4 || field == 5) o = v;
                    else if (field == 6) o = (v >> 1) ^ -(v & 1);
                    else if (field == 7) o = v != 0;
                }
            }
            cache[i] = o;
            return o;
        }

        internal void Read(byte[] b, int p, int end)
        {
            buf = b;
            var featSpans = new List<int>();
            while (p < end)
            {
                long k = Mvt.Varint(b, ref p);
                int field = (int)(k >> 3), wire = (int)(k & 7);
                if (wire == 2)
                {
                    int l = (int)Mvt.Varint(b, ref p);
                    if (field == 1) Name = Encoding.UTF8.GetString(b, p, l);
                    else if (field == 2) { featSpans.Add(p); featSpans.Add(l); }
                    else if (field == 3) { string s = Encoding.UTF8.GetString(b, p, l); keyIndex[s] = keys.Count; keys.Add(s); }
                    else if (field == 4) { valOff.Add(p); valLen.Add(l); }
                    p += l;
                }
                else if (wire == 0) { long v = Mvt.Varint(b, ref p); if (field == 5) Extent = (int)v; }
                else if (wire == 5) p += 4;
                else if (wire == 1) p += 8;
            }
            for (int i = 0; i < featSpans.Count; i += 2) Features.Add(ReadFeature(b, featSpans[i], featSpans[i] + featSpans[i + 1]));
        }

        static MvtFeature ReadFeature(byte[] b, int p, int end)
        {
            var f = new MvtFeature();
            int gp = 0, gend = 0;
            while (p < end)
            {
                long k = Mvt.Varint(b, ref p);
                int field = (int)(k >> 3), wire = (int)(k & 7);
                if (wire == 2)
                {
                    int l = (int)Mvt.Varint(b, ref p);
                    if (field == 2)
                    {
                        var tags = new List<int>();
                        int q = p, qe = p + l;
                        while (q < qe) tags.Add((int)Mvt.Varint(b, ref q));
                        f.Tags = tags.ToArray();
                    }
                    else if (field == 4) { gp = p; gend = p + l; }
                    p += l;
                }
                else if (wire == 0) { long v = Mvt.Varint(b, ref p); if (field == 3) f.Type = (int)v; }
                else if (wire == 5) p += 4;
                else if (wire == 1) p += 8;
            }
            int x = 0, y = 0;
            List<int> cur = null;
            while (gp < gend)
            {
                long c = Mvt.Varint(b, ref gp);
                int cmd = (int)(c & 7), n = (int)(c >> 3);
                if (cmd == 1 || cmd == 2)
                {
                    for (int i = 0; i < n && gp < gend; i++)
                    {
                        long dx = Mvt.Varint(b, ref gp), dy = Mvt.Varint(b, ref gp);
                        x += (int)((dx >> 1) ^ -(dx & 1));
                        y += (int)((dy >> 1) ^ -(dy & 1));
                        if (cmd == 1)
                        {
                            if (cur != null && cur.Count > 0) f.Parts.Add(cur.ToArray());
                            cur = new List<int>();
                        }
                        if (cur == null) cur = new List<int>();
                        cur.Add(x); cur.Add(y);
                    }
                }
            }
            if (cur != null && cur.Count > 0) f.Parts.Add(cur.ToArray());
            return f;
        }
    }

    /// <summary>Decoder for Mapbox Vector Tiles, the format inside the Overture archives.</summary>
    public static class Mvt
    {
        internal static long Varint(byte[] b, ref int p)
        {
            long r = 0; int s = 0;
            while (true)
            {
                byte c = b[p++];
                r |= (long)(c & 127) << s; s += 7;
                if (c < 128 || s > 63) return r;
            }
        }

        /// <summary>Decode the named layers of a tile. Missing layers are simply absent from the result.</summary>
        public static Dictionary<string, MvtLayer> Decode(byte[] b, params string[] wanted)
        {
            var layers = new Dictionary<string, MvtLayer>();
            if (b == null) return layers;
            int p = 0;
            while (p < b.Length)
            {
                long k = Varint(b, ref p);
                int field = (int)(k >> 3), wire = (int)(k & 7);
                if (wire == 2)
                {
                    int l = (int)Varint(b, ref p);
                    if (field == 3)
                    {
                        string name = PeekName(b, p, p + l);
                        if (wanted.Length == 0 || Array.IndexOf(wanted, name) >= 0)
                        {
                            var layer = new MvtLayer();
                            layer.Read(b, p, p + l);
                            layers[layer.Name] = layer;
                        }
                    }
                    p += l;
                }
                else if (wire == 0) Varint(b, ref p);
                else if (wire == 5) p += 4;
                else if (wire == 1) p += 8;
            }
            return layers;
        }

        static string PeekName(byte[] b, int p, int end)
        {
            while (p < end)
            {
                long k = Varint(b, ref p);
                int field = (int)(k >> 3), wire = (int)(k & 7);
                if (wire == 2)
                {
                    int l = (int)Varint(b, ref p);
                    if (field == 1) return Encoding.UTF8.GetString(b, p, l);
                    p += l;
                }
                else if (wire == 0) Varint(b, ref p);
                else if (wire == 5) p += 4;
                else if (wire == 1) p += 8;
            }
            return "";
        }
    }
}
