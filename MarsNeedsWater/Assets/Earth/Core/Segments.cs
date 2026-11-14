using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Overture describes where a road is a bridge or a tunnel as fractions of the WHOLE map segment
    /// ("between": [0.25, 0.75]). A tile only holds the part of the segment that crosses it, so to know
    /// where those fractions fall this puts the segment back together from the neighbouring tiles.
    /// </summary>
    internal static class Segments
    {
        sealed class Entry { public MvtLayer L; public Dictionary<string, List<MvtFeature>> Partial; }

        const int Max = 72;
        static readonly Dictionary<long, Entry> cache = new Dictionary<long, Entry>();
        static readonly Queue<long> order = new Queue<long>();

        /// <summary>A whole segment as one line, in zoom-14 tile units counted from the world's corner.</summary>
        internal sealed class Whole { public double[] P; public double[] At; public double Len; }

        static Entry Get(TileSources src, int x, int y)
        {
            int n = 1 << 14;
            if (y < 0 || y >= n) return null;
            x = (x % n + n) % n;
            long key = (long)x * n + y;
            lock (cache) { Entry hit; if (cache.TryGetValue(key, out hit)) return hit; }
            var e = new Entry();
            try
            {
                var tl = Mvt.Decode(src.Transport.GetTile(14, x, y), "segment");
                tl.TryGetValue("segment", out e.L);
            }
            catch (Exception) { return null; }                    // not readable now: do not remember that
            if (e.L != null)
            {
                e.Partial = new Dictionary<string, List<MvtFeature>>();
                int kId = e.L.Key("id"), kRoad = e.L.Key("road_flags"), kRail = e.L.Key("rail_flags");
                foreach (MvtFeature f in e.L.Features)
                {
                    if (f.Type != 2) continue;
                    string fl = e.L.Str(f, kRoad) ?? e.L.Str(f, kRail);
                    if (fl == null || fl.IndexOf("between", StringComparison.Ordinal) < 0) continue;
                    string id = e.L.Str(f, kId);
                    if (id == null) continue;
                    List<MvtFeature> list;
                    if (!e.Partial.TryGetValue(id, out list)) e.Partial[id] = list = new List<MvtFeature>();
                    list.Add(f);
                }
            }
            lock (cache)
            {
                if (!cache.ContainsKey(key))
                {
                    cache[key] = e; order.Enqueue(key);
                    while (order.Count > Max) cache.Remove(order.Dequeue());
                }
            }
            return e;
        }

        /// <summary>The road and rail lines of a zoom-14 tile (shared, so never change what it returns); null if there are none.</summary>
        internal static MvtLayer Layer(TileSources src, int x, int y)
        {
            Entry e = Get(src, x, y);
            return e == null ? null : e.L;
        }

        /// <summary>Put segment id back together, starting from tile tx, ty. Null when it cannot be done for certain.</summary>
        internal static int[] Why = new int[8];
        internal static Whole Assemble(TileSources src, string id, int tx, int ty, int extent, List<int[]> parts = null)
        {
            if (src == null || id == null) { Why[0]++; return null; }
            var pieces = new List<double[]>();
            var seen = new HashSet<long>();
            var todo = new Queue<(int x, int y)>();
            todo.Enqueue((tx, ty));
            // the piece in hand may lie only in this tile's margin, which belongs to a neighbour
            if (parts != null)
                foreach (int[] p in parts)
                    for (int i = 0; i + 1 < p.Length; i += 2)
                    {
                        int ox = p[i] < 0 ? -1 : p[i] > extent ? 1 : 0, oy = p[i + 1] < 0 ? -1 : p[i + 1] > extent ? 1 : 0;
                        if (ox != 0 || oy != 0) todo.Enqueue((tx + ox, ty + oy));
                    }
            while (todo.Count > 0)
            {
                var at = todo.Dequeue();
                if (!seen.Add(at.x * 100003L + at.y)) continue;
                if (seen.Count > 40) { Why[1]++; return null; }                 // a very long segment: give up rather than read half the country
                Entry e = Get(src, at.x, at.y);
                if (e == null) { Why[2]++; return null; }
                List<MvtFeature> fs;
                if (e.L == null || !e.Partial.TryGetValue(id, out fs)) continue;
                double E = e.L.Extent, k = extent / E;
                foreach (MvtFeature f in fs)
                    foreach (int[] p in f.Parts)
                    {
                        // the stretches of this line inside the tile proper (its margin overlaps the neighbours)
                        var run = new List<double>();
                        Action flush = () =>
                        {
                            if (run.Count >= 4)
                            {
                                var g = new double[run.Count];
                                for (int i = 0; i < run.Count; i += 2) { g[i] = (at.x * E + run[i]) * k; g[i + 1] = (at.y * E + run[i + 1]) * k; }
                                pieces.Add(g);
                                foreach (int end in new[] { 0, run.Count - 2 })
                                {
                                    int ox = run[end] <= 1e-6 ? -1 : run[end] >= E - 1e-6 ? 1 : 0, oy = run[end + 1] <= 1e-6 ? -1 : run[end + 1] >= E - 1e-6 ? 1 : 0;
                                    if (ox != 0) todo.Enqueue((at.x + ox, at.y));
                                    if (oy != 0) todo.Enqueue((at.x, at.y + oy));
                                    if (ox != 0 && oy != 0) todo.Enqueue((at.x + ox, at.y + oy));
                                }
                            }
                            run.Clear();
                        };
                        for (int i = 0; i + 3 < p.Length; i += 2)
                        {
                            double ax = p[i], ay = p[i + 1], bx = p[i + 2], by = p[i + 3], t0 = 0, t1 = 1;
                            if (!TileBuilder.ClipT(-(bx - ax), ax, ref t0, ref t1) || !TileBuilder.ClipT(bx - ax, E - ax, ref t0, ref t1) ||
                                !TileBuilder.ClipT(-(by - ay), ay, ref t0, ref t1) || !TileBuilder.ClipT(by - ay, E - ay, ref t0, ref t1)) { flush(); continue; }
                            if (run.Count == 0 || t0 > 0) { flush(); run.Add(ax + (bx - ax) * t0); run.Add(ay + (by - ay) * t0); }
                            if (t1 > t0) { run.Add(ax + (bx - ax) * t1); run.Add(ay + (by - ay) * t1); }
                            if (t1 < 1) flush();
                        }
                        flush();
                    }
            }
            // drop stretches of no length, then chain them: each one starts where the one before it ended
            pieces.RemoveAll(g => Length(g) < 1e-6);
            int n = pieces.Count;
            if (n == 0) { Why[3]++; return null; }
            const double tol = 3;
            int first = -1;
            for (int i = 0; i < n; i++)
            {
                bool fed = false;
                for (int j = 0; j < n && !fed; j++)
                    if (j != i && Near(pieces[j], pieces[j].Length - 2, pieces[i], 0, tol)) fed = true;
                if (fed) continue;
                if (first >= 0) { Why[4]++; return null; }                    // two loose starts: pieces are missing in between
                first = i;
            }
            if (first < 0) { Why[5]++; return null; }
            var used = new bool[n];
            var pts = new List<double>();
            int cur = first, count = 0;
            while (cur >= 0)
            {
                used[cur] = true; count++;
                double[] g = pieces[cur];
                for (int i = pts.Count == 0 ? 0 : 2; i < g.Length; i++) pts.Add(g[i]);
                int next = -1;
                for (int j = 0; j < n; j++) if (!used[j] && Near(g, g.Length - 2, pieces[j], 0, tol)) { next = j; break; }
                cur = next;
            }
            if (count != n) { Why[6]++; return null; }
            var w = new Whole { P = pts.ToArray() };
            w.At = new double[w.P.Length / 2];
            for (int i = 1; i < w.At.Length; i++)
            {
                double dx = w.P[i * 2] - w.P[i * 2 - 2], dy = w.P[i * 2 + 1] - w.P[i * 2 - 1];
                w.At[i] = w.At[i - 1] + Math.Sqrt(dx * dx + dy * dy);
            }
            w.Len = w.At[w.At.Length - 1];
            return w.Len > 0 ? w : null;
        }

        static double Length(double[] g)
        {
            double s = 0;
            for (int i = 2; i < g.Length; i += 2) s += Math.Sqrt((g[i] - g[i - 2]) * (g[i] - g[i - 2]) + (g[i + 1] - g[i - 1]) * (g[i + 1] - g[i - 1]));
            return s;
        }

        static bool Near(double[] a, int i, double[] b, int j, double tol)
        {
            return Math.Abs(a[i] - b[j]) <= tol && Math.Abs(a[i + 1] - b[j + 1]) <= tol;
        }

        /// <summary>How far along the whole segment the point lies; NaN if it is not on it.</summary>
        internal static double Locate(Whole w, double gx, double gy)
        {
            double best = double.MaxValue, at = double.NaN;
            for (int i = 0; i + 1 < w.At.Length; i++)
            {
                double ax = w.P[i * 2], ay = w.P[i * 2 + 1], dx = w.P[i * 2 + 2] - ax, dy = w.P[i * 2 + 3] - ay, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? ((gx - ax) * dx + (gy - ay) * dy) / l2 : 0;
                // a line's margin can reach a little past the true end of what was put together
                double tc = Math.Max(0, Math.Min(1, t)), ex = ax + dx * tc - gx, ey = ay + dy * tc - gy, d = ex * ex + ey * ey;
                if (d < best) { best = d; at = w.At[i] + Math.Sqrt(l2) * tc; }
            }
            return best <= 4 ? at : double.NaN;
        }
    }
}
