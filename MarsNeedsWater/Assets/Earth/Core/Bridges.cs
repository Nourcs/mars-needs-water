using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarsNeedsWater.Earth
{
    /// <summary>The centre line of one stretch of bridge deck in a tile, for "what am I standing on".</summary>
    public sealed class BridgeRun
    {
        public float HalfWidth;
        public float[] P;                       // x, y, z triples, tile-local
        public float MinX, MaxX, MinZ, MaxZ;
    }

    /// <summary>
    /// Raises every road and railway the map marks as a bridge on to a deck.
    ///
    /// The map gives no heights, so the deck is worked out from the bridge itself: it leaves the ground at each
    /// end of the bridge, climbs on a ramp and runs level in between, higher the longer the bridge is
    /// (a footbridge clears a few metres, a bridge over a strait about 60 real metres). A bridge usually crosses
    /// several map squares, so each square looks at the eight around it too, and the height at a spot depends
    /// only on how far along the bridge its two ends are. That way neighbouring squares agree where they meet.
    /// </summary>
    public static class Bridges
    {
        const double Slope = 0.06;              // how steep an approach ramp may be
        const double MaxClear = 60, LandClear = 14;   // real metres: the highest a deck goes above the sea, and above land or a river
        const double WaterClear = 0.8;           // game metres a deck keeps above water
        const float Thick = 0.3f;               // deck thickness, game metres
        const int Keep = 4;                      // how many ends each joint remembers
        const double Gap = 40, Beside = 30, SameEnd = 60; // real metres: bridges this close side by side move together; ends this close are one end

        // ------------------------------------------------------------ which part of a road is a bridge

        /// <summary>The stretches (as fractions of the whole map segment) flagged is_bridge; null if none.</summary>
        static List<double> Ranges(string flags, string what)
        {
            if (flags == null) return null;
            List<double> r = null;
            int at = 0;
            while ((at = flags.IndexOf(what, at, StringComparison.Ordinal)) >= 0)
            {
                int open = flags.LastIndexOf('{', at), close = flags.IndexOf('}', at);
                double a = 0, b = 1;
                if (open >= 0 && close > open)
                {
                    int bt = flags.IndexOf("\"between\"", open, close - open, StringComparison.Ordinal);
                    if (bt >= 0)
                    {
                        int l = flags.IndexOf('[', bt), c = flags.IndexOf(',', l + 1), e = flags.IndexOf(']', l + 1);
                        if (l >= 0 && c > l && e > c)
                        {
                            double.TryParse(flags.Substring(l + 1, c - l - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out a);
                            double.TryParse(flags.Substring(c + 1, e - c - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out b);
                        }
                    }
                }
                if (r == null) r = new List<double>();
                r.Add(a); r.Add(b);
                at += what.Length;
            }
            return r;
        }

        /// <summary>
        /// Split a road's lines (tile whole-number coordinates) into bridge and plain pieces, exactly where the
        /// map's flags say; stretches flagged as tunnel are dropped. A road that was cut off at the tile edge is
        /// measured against the whole segment, put back together from the neighbouring tiles, so every tile that
        /// holds a piece of it cuts it at the same spot. Only if that fails is it a bridge all along or not at all.
        /// </summary>
        /// <summary>For testing: how many cut-off segments were measured exactly, and how many had to be guessed.</summary>
        public static int Exact, Guessed;

        public static void Split(List<int[]> parts, string flags, int extent, List<int[]> plain, List<int[]> bridge,
            TileSources src = null, string id = null, int tx = 0, int ty = 0)
        {
            List<double> r = Ranges(flags, "is_bridge"), tn = flags != null && flags.IndexOf("between", StringComparison.Ordinal) >= 0 ? Ranges(flags, "is_tunnel") : null;
            if (r == null && tn == null) { plain.AddRange(parts); return; }
            if (flags.IndexOf("between", StringComparison.Ordinal) < 0) { bridge.AddRange(parts); return; }   // a bridge from end to end
            bool cut = parts.Count != 1;
            if (!cut)
                foreach (int c in parts[0]) if (c <= -32 || c >= extent + 32) { cut = true; break; }
            Segments.Whole whole = cut ? Segments.Assemble(src, id, tx, ty, extent, parts) : null;
            var starts = new double[parts.Count];
            if (cut && whole != null)
                for (int k = 0; k < parts.Count; k++)
                {
                    starts[k] = Segments.Locate(whole, (double)tx * extent + parts[k][0], (double)ty * extent + parts[k][1]);
                    if (double.IsNaN(starts[k])) { whole = null; break; }
                }
            if (cut) { if (whole != null) Exact++; else Guessed++; }
            if (cut && whole == null)
            {
                if (Cover(tn) >= 0.5) return;
                (Cover(r) >= 0.5 ? bridge : plain).AddRange(parts);
                return;
            }
            for (int q = 0; q < parts.Count; q++)
            {
                int[] p = parts[q];
                int n = p.Length / 2;
                var at = new double[n];
                at[0] = cut ? starts[q] : 0;
                for (int i = 1; i < n; i++)
                {
                    double dx = p[i * 2] - p[i * 2 - 2], dy = p[i * 2 + 1] - p[i * 2 - 1];
                    at[i] = at[i - 1] + Math.Sqrt(dx * dx + dy * dy);
                }
                double len = cut ? whole.Len : at[n - 1];
                if (len <= 0 || at[n - 1] - at[0] <= 0) continue;
                // cut points, in order
                var cuts = new List<double> { at[0], at[n - 1] };
                foreach (List<double> rr in new[] { r, tn })
                    if (rr != null)
                        for (int i = 0; i < rr.Count; i++) { double c = Math.Min(1, Math.Max(0, rr[i])) * len; if (c > at[0] + 0.5 && c < at[n - 1] - 0.5) cuts.Add(c); }
                cuts.Sort();
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    double s0 = cuts[k], s1 = cuts[k + 1];
                    if (s1 - s0 < 0.5) continue;
                    double mid = (s0 + s1) / 2 / len;
                    if (In(tn, mid)) continue;
                    var piece = new List<int>();
                    Add(piece, p, at, s0);
                    for (int i = 0; i < n; i++) if (at[i] > s0 && at[i] < s1) { piece.Add(p[i * 2]); piece.Add(p[i * 2 + 1]); }
                    Add(piece, p, at, s1);
                    if (piece.Count >= 4) (In(r, mid) ? bridge : plain).Add(piece.ToArray());
                }
            }
        }

        static bool In(List<double> r, double f)
        {
            if (r != null) for (int i = 0; i < r.Count; i += 2) if (f >= r[i] && f <= r[i + 1]) return true;
            return false;
        }

        static double Cover(List<double> r)
        {
            double cover = 0;
            if (r != null) for (int i = 0; i < r.Count; i += 2) cover += Math.Max(0, r[i + 1] - r[i]);
            return cover;
        }

        static void Add(List<int> piece, int[] p, double[] at, double s)
        {
            int n = at.Length, i = 1;
            while (i < n - 1 && at[i] < s) i++;
            double span = at[i] - at[i - 1], f = span > 0 ? (s - at[i - 1]) / span : 0;
            piece.Add((int)Math.Round(p[i * 2 - 2] + (p[i * 2] - p[i * 2 - 2]) * f));
            piece.Add((int)Math.Round(p[i * 2 - 1] + (p[i * 2 + 1] - p[i * 2 - 1]) * f));
        }

        // ------------------------------------------------------------ buildings in the way

        const int LineRow = 6;
        const float DeckMargin = 0.3f;          // game metres kept clear on either side of a deck

        /// <summary>
        /// The bridges of a tile, for Blocked: ax, ay, bx, by, half width (all as fractions of the tile) and
        /// which road it is, for every straight stretch. Null if the tile has no bridge.
        /// </summary>
        internal static float[] Lines(List<TileBuilder.RoadItem> roads, int extent, float tileSize)
        {
            List<float> list = null;
            for (int q = 0; q < roads.Count; q++)
            {
                TileBuilder.RoadItem r = roads[q];
                if (r.Bridge.Count == 0) continue;
                if (list == null) list = new List<float>();
                float hw = (r.Style.RealWidth * TileBuilder.RoadScale * 0.5f + 0.15f + DeckMargin) / tileSize;
                foreach (int[] p in r.Bridge)
                    for (int i = 0; i + 3 < p.Length; i += 2)
                    {
                        list.Add(p[i] / (float)extent); list.Add(p[i + 1] / (float)extent);
                        list.Add(p[i + 2] / (float)extent); list.Add(p[i + 3] / (float)extent);
                        list.Add(hw); list.Add(q);
                    }
            }
            return list != null ? list.ToArray() : null;
        }

        /// <summary>
        /// Would a building with this outline (tile whole-number coordinates) stand in the way on a bridge?
        /// The map draws bridge towers, piers and toll gates as buildings, and built like any other they are
        /// walls across the deck. A building is in the way if a bridge runs right through it (in one side and
        /// out another), or if it sits on the deck (its middle, or half its corners, within the deck's width).
        /// One that a bridge merely ends in, like a footbridge into a shopping centre, is left alone.
        /// </summary>
        internal static bool Blocked(float[] lines, int[] ring, double extent)
        {
            if (lines == null) return false;
            int n = ring.Length / 2;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                double x = ring[i * 2] / extent, y = ring[i * 2 + 1] / extent;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            double cx = (minX + maxX) * 0.5, cy = (minY + maxY) * 0.5;
            bool[] on = null;
            int onCount = 0, crossings = 0, road = -1;
            for (int s = 0; s < lines.Length; s += LineRow)
            {
                double ax = lines[s], ay = lines[s + 1], bx = lines[s + 2], by = lines[s + 3], hw = lines[s + 4];
                if (Math.Max(ax, bx) < minX - hw || Math.Min(ax, bx) > maxX + hw || Math.Max(ay, by) < minY - hw || Math.Min(ay, by) > maxY + hw) continue;
                if (Away(cx, cy, ax, ay, bx, by) < hw * hw) return true;
                if ((int)lines[s + 5] != road) { road = (int)lines[s + 5]; crossings = 0; }
                if (on == null) on = new bool[n];
                for (int i = 0, j = n - 1; i < n; j = i, i++)
                {
                    double px = ring[i * 2] / extent, py = ring[i * 2 + 1] / extent, qx = ring[j * 2] / extent, qy = ring[j * 2 + 1] / extent;
                    double d1 = (bx - ax) * (py - ay) - (by - ay) * (px - ax), d2 = (bx - ax) * (qy - ay) - (by - ay) * (qx - ax);
                    double d3 = (qx - px) * (ay - py) - (qy - py) * (ax - px), d4 = (qx - px) * (by - py) - (qy - py) * (bx - px);
                    if (d1 * d2 < 0 && d3 * d4 < 0 && ++crossings >= 2) return true;
                    if (!on[i] && Away(px, py, ax, ay, bx, by) < hw * hw) { on[i] = true; onCount++; }
                }
            }
            return onCount * 2 >= n && onCount > 0;
        }

        /// <summary>The square of how far x,y is from the stretch a to b.</summary>
        static double Away(double x, double y, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double f = len2 > 0 ? Math.Min(1, Math.Max(0, ((x - ax) * dx + (y - ay) * dy) / len2)) : 0;
            double ex = x - (ax + dx * f), ey = y - (ay + dy * f);
            return ex * ex + ey * ey;
        }

        // ------------------------------------------------------------ the bridges of a tile

        sealed class Piece
        {
            public double[] P;          // x (east), y (south), real metres from the centre tile's north-west corner
            public double[] At;         // distance along, per point
            public double Len;
            public int A, B;            // node at each end
            public int[] Stations;      // a joint every 25 m or so along it, ends included
            public bool Centre;
            public float Width; public int Pal;
            public TileBuilder.RoadItem Item;     // the road it carries (its look: lanes, lines)
        }

        sealed class Node
        {
            public double X, Y; public int Degree; public bool Edge, End;
            public double Tx, Ty;       // at an end: the way the bridge runs from here
            public int Group = -1;      // ends that belong together (the two carriageways of one road) share a group
            public double Own = double.MaxValue;   // to the nearest end along its own bridge only
            public float W;             // the widest road that meets here
            public bool Leans;          // part of a path that stops in mid-air beside a wider bridge, and is held up by it
            public List<int> Near;      // the ends (by group) that lie close by along its own bridge
            public List<(int other, double len, bool beside)> Links = new List<(int, double, bool)>();
            public double G;            // ground (or landmark deck) height at an end, game metres
            public double[] D = { double.MaxValue, double.MaxValue, double.MaxValue, double.MaxValue };   // the nearest ends (of different groups): how far
            public int[] L = { -1, -1, -1, -1 };                                                          // and which
            public List<int> Pieces = new List<int>();
        }

        /// <summary>Build the decks of tile t (zoom 14). roads are the tile's own roads, as CollectRoads gave them.</summary>
        internal static void Build(TileResult t, TileSources src, List<TileBuilder.RoadItem> own, int extent, Func<double, double, bool> water = null)
        {
            t.Bridges = new MeshData();
            t.BridgeRuns = new List<BridgeRun>();
            bool any = false;
            foreach (TileBuilder.RoadItem r in own) if (r.Bridge.Count > 0) { any = true; break; }
            if (!any) return;                                   // most tiles have no bridge: do not fetch the neighbours

            double tileReal = t.Size * Geo.Scale;
            double far = tileReal * 0.75;                        // an end further off than this has no say
            var pieces = new List<Piece>();
            var anchors = new Dictionary<long, List<(double x, double y, float w)>>();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    List<TileBuilder.RoadItem> roads = own; int ext = extent;
                    if (dx != 0 || dy != 0)
                    {
                        int n14 = 1 << 14, nx = ((t.X + dx) % n14 + n14) % n14, ny = t.Y + dy;
                        if (ny < 0 || ny >= n14) continue;
                        MvtLayer seg = Segments.Layer(src, nx, ny);   // a neighbour that cannot be read only costs accuracy
                        if (seg == null) continue;
                        roads = TileBuilder.CollectRoads(seg, 14, nx, ny, src);
                        ext = seg.Extent;
                    }
                    foreach (TileBuilder.RoadItem r in roads)
                        foreach (int[] part in r.Bridge)
                            Clip(part, ext, dx, dy, tileReal, r, dx == 0 && dy == 0, pieces);
                    // where a road on the ground ends: if that is on a bridge, the bridge is on the ground there too
                    foreach (TileBuilder.RoadItem r in roads)
                        foreach (int[] part in r.Plain)
                            foreach (int e in new[] { 0, part.Length - 2 })
                            {
                                int px = part[e], py = part[e + 1];
                                if (px <= -32 || py <= -32 || px >= ext + 32 || py >= ext + 32) continue;      // cut off by the tile, not a true end
                                double ax = px * tileReal / ext + dx * tileReal, ay = py * tileReal / ext + dy * tileReal;
                                long key = (long)Math.Floor(ax / 2.0) * 1000003L + (long)Math.Floor(ay / 2.0);
                                List<(double x, double y, float w)> cell;
                                if (!anchors.TryGetValue(key, out cell)) anchors[key] = cell = new List<(double, double, float)>();
                                cell.Add((ax, ay, r.Style.RealWidth * TileBuilder.RoadScale));
                            }
                }
            if (pieces.Count == 0) return;
            // a ground road can also end part-way along a bridge line: cut the line there, so the spot becomes a joint
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                Piece p = pieces[i];
                int np = p.P.Length / 2, from = 0;
                List<Piece> cutUp = null;
                for (int k = 1; k < np - 1; k++)
                {
                    if (!Anchored(anchors, p.P[k * 2], p.P[k * 2 + 1], p.Width)) continue;
                    if (cutUp == null) cutUp = new List<Piece>();
                    cutUp.Add(Part(p, from, k)); from = k;
                }
                if (cutUp == null) continue;
                cutUp.Add(Part(p, from, np - 1));
                pieces.RemoveAt(i);
                foreach (Piece c in cutUp) if (c.Len > 0.2) pieces.Add(c);
            }

            // ---- join the pieces up where their ends meet
            var nodes = new List<Node>();
            var stations = new List<(double x, double y, double tx, double ty, int node, int piece)>();
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                p.A = NodeAt(nodes, grid, p.P[0], p.P[1]);
                p.B = NodeAt(nodes, grid, p.P[p.P.Length - 2], p.P[p.P.Length - 1]);
                nodes[p.A].Degree++; nodes[p.B].Degree++;
                nodes[p.A].Pieces.Add(i); nodes[p.B].Pieces.Add(i);
                // joints along the piece, so that a bridge can also be tied to the one running beside it
                int m = Math.Max(1, (int)Math.Ceiling(p.Len / 25.0));
                p.Stations = new int[m + 1];
                p.Stations[0] = p.A; p.Stations[m] = p.B;
                int seg = 0, np = p.P.Length / 2;
                for (int k = 0; k <= m; k++)
                {
                    double sAt = p.Len * k / m;
                    while (seg < np - 2 && p.At[seg + 1] < sAt) seg++;
                    double sl = p.At[seg + 1] - p.At[seg], f = sl > 0 ? (sAt - p.At[seg]) / sl : 0;
                    double ddx = p.P[seg * 2 + 2] - p.P[seg * 2], ddy = p.P[seg * 2 + 3] - p.P[seg * 2 + 1], dl = Math.Sqrt(ddx * ddx + ddy * ddy);
                    if (dl < 1e-9) { ddx = 1; ddy = 0; dl = 1; }
                    double sx = p.P[seg * 2] + ddx * f, sy = p.P[seg * 2 + 1] + ddy * f;
                    if (k > 0 && k < m) { nodes.Add(new Node { X = sx, Y = sy }); p.Stations[k] = nodes.Count - 1; }
                    if (k == 0) { nodes[p.A].Tx = ddx / dl; nodes[p.A].Ty = ddy / dl; }
                    if (k == m) { nodes[p.B].Tx = -ddx / dl; nodes[p.B].Ty = -ddy / dl; }
                    stations.Add((sx, sy, ddx / dl, ddy / dl, p.Stations[k], i));
                    if (p.Width > nodes[p.Stations[k]].W) nodes[p.Stations[k]].W = p.Width;
                    if (k > 0)
                    {
                        int u = p.Stations[k - 1], v = p.Stations[k];
                        nodes[u].Links.Add((v, p.Len / m, false)); nodes[v].Links.Add((u, p.Len / m, false));
                    }
                }
            }
            // ---- tie bridges that run side by side (the two carriageways of a road, a footway along a deck),
            //      so that they rise and fall together instead of each going its own way
            var cells = new Dictionary<long, List<int>>();
            for (int i = 0; i < stations.Count; i++)
            {
                long key = (long)Math.Floor(stations[i].x / Beside) * 1000003L + (long)Math.Floor(stations[i].y / Beside);
                List<int> cell;
                if (!cells.TryGetValue(key, out cell)) cells[key] = cell = new List<int>();
                cell.Add(i);
            }
            for (int i = 0; i < stations.Count; i++)
            {
                var a = stations[i];
                long cx = (long)Math.Floor(a.x / Beside), cy = (long)Math.Floor(a.y / Beside);
                for (long j = cy - 1; j <= cy + 1; j++)
                    for (long c = cx - 1; c <= cx + 1; c++)
                    {
                        List<int> cell;
                        if (!cells.TryGetValue(c * 1000003L + j, out cell)) continue;
                        foreach (int o in cell)
                        {
                            var b = stations[o];
                            if (o <= i || b.piece == a.piece || b.node == a.node) continue;
                            double ex = b.x - a.x, ey = b.y - a.y, d = Math.Sqrt(ex * ex + ey * ey);
                            if (d > Beside || Math.Abs(a.tx * b.tx + a.ty * b.ty) < 0.9) continue;
                            d = Math.Max(d, 1);
                            nodes[a.node].Links.Add((b.node, d, true)); nodes[b.node].Links.Add((a.node, d, true));
                        }
                    }
            }
            // ---- the ends of bridges: where a deck meets the ground (or a landmark bridge's own deck)
            var queue = new SortedSet<(double d, int node, int label)>();
            var ends = new List<int>();
            var joined = new List<int>();
            Action<int> land = i =>
            {
                Node nd = nodes[i];
                ends.Add(i);
                double lat = Geo.TileLat(t.Y + nd.Y / tileReal, 14), lon = Geo.TileLon(t.X + nd.X / tileReal, 14);
                float top;
                if (Landmarks.Deck(lat, lon, 14, out top)) nd.G = top;
                else if (nd.X >= 0 && nd.X <= tileReal && nd.Y >= 0 && nd.Y <= tileReal)
                {
                    float x, y, z;
                    t.Surface(nd.X / tileReal, nd.Y / tileReal, out x, out y, out z);
                    nd.G = y;
                }
                // in a neighbour: read the terrain the way that tile's ground mesh is put together
                else nd.G = Math.Max(0, Raw(src, t, nd.X / tileReal, nd.Y / tileReal, true)) / Geo.Scale;
            };
            for (int i = 0; i < nodes.Count; i++)
            {
                Node nd = nodes[i];
                nd.Edge = nd.X < -tileReal + 3 || nd.X > 2 * tileReal - 3 || nd.Y < -tileReal + 3 || nd.Y > 2 * tileReal - 3;
                if (nd.Edge) continue;
                if (nd.Degree != 1)
                {
                    // a joint along a bridge is on the ground only where a ground road ends at it (but the run-up
                    // to a landmark bridge stays up, whatever paths and steps meet it)
                    if (!Anchored(anchors, nd.X, nd.Y, nd.W)) continue;
                    if (Landmarks.Landing(Geo.TileLat(t.Y + nd.Y / tileReal, 14), Geo.TileLon(t.X + nd.X / tileReal, 14), 220)) continue;
                    joined.Add(i);
                }
                nd.End = true; nd.Group = i;
                land(i);
            }
            // the other carriageway of the same road comes down with it
            foreach (int i in joined)
                foreach (var link in nodes[i].Links)
                {
                    Node o = nodes[link.other];
                    if (!link.beside || link.len > 22 || o.End || o.Edge || o.W > nodes[i].W * 1.3f) continue;
                    o.End = true; o.Group = nodes[i].Group;
                    land(link.other);
                }
            // ---- a footway or cycle path on a bridge is often mapped with short breaks in it. Where it stops beside
            //      a wider bridge that carries on, and starts again a little further, that is not an end at all:
            //      it stays up with the road beside it
            var reach = new SortedSet<(double d, int node)>();
            foreach (int e in ends) { nodes[e].Own = 0; reach.Add((0, e)); }
            while (reach.Count > 0)
            {
                var top = reach.Min; reach.Remove(top);
                Node nd = nodes[top.node];
                if (nd.Own != top.d) continue;
                foreach (var link in nd.Links)
                {
                    if (link.beside) continue;
                    double d = top.d + link.len;
                    if (d > far || d >= nodes[link.other].Own) continue;
                    nodes[link.other].Own = d; reach.Add((d, link.other));
                }
            }
            var walk = new SortedSet<(double d, int node)>();
            var gone = new Dictionary<int, double>();
            var all = ends.ToArray();
            for (int k = ends.Count - 1; k >= 0; k--)
            {
                Node nd = nodes[ends[k]];
                bool gap = false;
                foreach (int f in all)
                {
                    Node o = nodes[f];
                    double ex = o.X - nd.X, ey = o.Y - nd.Y, d = Math.Sqrt(ex * ex + ey * ey);
                    if (f == ends[k] || d > Gap || nd.Tx * o.Tx + nd.Ty * o.Ty > -0.7) continue;
                    if (d > 3 && -(ex * nd.Tx + ey * nd.Ty) / d < 0.7) continue;      // it must lie ahead, where this one stops
                    gap = true; break;
                }
                if (!gap) continue;
                foreach (var link in nd.Links)
                {
                    Node b = nodes[link.other];
                    if (!link.beside || b.End || b.Own < Beside || b.W * 0.75f <= nd.W) continue;
                    nd.End = false; nd.Group = -1;
                    // the stretch of it near here now leans on the road beside it
                    gone.Clear(); gone[ends[k]] = 0; walk.Add((0, ends[k]));
                    while (walk.Count > 0)
                    {
                        var top = walk.Min; walk.Remove(top);
                        if (gone[top.node] != top.d) continue;
                        nodes[top.node].Leans = true;
                        foreach (var hop in nodes[top.node].Links)
                        {
                            if (hop.beside) continue;
                            double d = top.d + hop.len, old;
                            if (d > far / 3 || (gone.TryGetValue(hop.other, out old) && old <= d)) continue;
                            gone[hop.other] = d; walk.Add((d, hop.other));
                        }
                    }
                    ends.RemoveAt(k);
                    break;
                }
            }
            foreach (int e in ends) { nodes[e].D[0] = 0; nodes[e].L[0] = e; queue.Add((0, e, e)); }
            // ---- ends that lie close together and face the same way are the same end of one crossing
            //      (two carriageways seldom stop at quite the same spot): they count as one
            for (int i = 0; i < ends.Count; i++)
                for (int j = i + 1; j < ends.Count; j++)
                {
                    Node a = nodes[ends[i]], b = nodes[ends[j]];
                    double ex = b.X - a.X, ey = b.Y - a.Y;
                    if (ex * ex + ey * ey > SameEnd * SameEnd || a.Tx * b.Tx + a.Ty * b.Ty < 0.7) continue;
                    int ga = a.Group, gb = b.Group;
                    if (ga == gb) continue;
                    int lo = Math.Min(ga, gb), hi = Math.Max(ga, gb);
                    foreach (int e in ends) if (nodes[e].Group == hi) nodes[e].Group = lo;
                }
            // ---- note at each joint which ends lie close by along its own bridge. Two bridges side by side are
            //      one crossing where they share such an end (it only looks at what is near, so every map square
            //      comes to the same answer)
            foreach (int e in ends)
            {
                int g = nodes[e].Group;
                gone.Clear(); gone[e] = 0; walk.Add((0, e));
                while (walk.Count > 0)
                {
                    var top = walk.Min; walk.Remove(top);
                    if (gone[top.node] != top.d) continue;
                    Node nd = nodes[top.node];
                    if (nd.Near == null) nd.Near = new List<int>();
                    if (!nd.Near.Contains(g)) nd.Near.Add(g);
                    foreach (var link in nd.Links)
                    {
                        if (link.beside) continue;
                        double d = top.d + link.len, old;
                        if (d > far / 3 || (gone.TryGetValue(link.other, out old) && old <= d)) continue;
                        gone[link.other] = d; walk.Add((d, link.other));
                    }
                }
            }
            // ---- for every joint: how far, along the bridge, to its nearest two different ends
            while (queue.Count > 0)
            {
                var top = queue.Min; queue.Remove(top);
                Node nd = nodes[top.node];
                bool live = false;
                for (int k = 0; k < Keep; k++) if (nd.L[k] == top.label && nd.D[k] == top.d) live = true;
                if (!live) continue;
                foreach (var link in nd.Links)
                {
                    double d = top.d + link.len;
                    if (d > far || link.other == top.node) continue;
                    // across to the bridge alongside only if the two are one crossing, and never from a narrow
                    // one to a wide one (so a small bridge next to a long viaduct does not pull it to the ground)
                    if (link.beside)
                    {
                        Node o = nodes[link.other];
                        if (nd.W * 1.3f < o.W) continue;
                        if (!(o.Leans && nd.W * 0.75f > o.W) && !Shares(nd, o)) continue;
                    }
                    if (Offer(nodes, nodes[link.other], d, top.label)) queue.Add((d, link.other, top.label));
                }
            }

            // ---- the decks of this tile
            float S = t.Size;
            var line = new List<float>();
            var sea = new Dictionary<long, double>();
            float deckTop;
            bool nearDeck = Landmarks.Deck(Geo.TileLat(t.Y + 0.5, 14), Geo.TileLon(t.X + 0.5, 14), tileReal, out deckTop);
            var cd = new double[2 * Keep + 1]; var cg = new double[2 * Keep + 1]; var cgrp = new int[2 * Keep + 1];
            foreach (Piece p in pieces)
            {
                if (!p.Centre || p.Len < 0.5) continue;
                line.Clear();
                int m = p.Stations.Length - 1;
                int n = p.P.Length / 2;
                for (int i = 0; i + 1 < n; i++)
                {
                    double seg = p.At[i + 1] - p.At[i];
                    int steps = Math.Max(1, (int)Math.Ceiling(seg / 10.0));
                    for (int k = i == 0 ? 0 : 1; k <= steps; k++)
                    {
                        double f = (double)k / steps, s = p.At[i] + seg * f;
                        double x = p.P[i * 2] + (p.P[i * 2 + 2] - p.P[i * 2]) * f, y = p.P[i * 2 + 1] + (p.P[i * 2 + 3] - p.P[i * 2 + 1]) * f;
                        // map roads are drawn wider than life and a landmark bridge is not: running up to one,
                        // the road draws in to the width of its deck
                        float narrow = 1;
                        if (nearDeck)
                        {
                            double la = Geo.TileLat(t.Y + y / tileReal, 14), lo = Geo.TileLon(t.X + x / tileReal, 14), la2, lo2;
                            if (Landmarks.DrawIn(la, lo, p.Width * Geo.Scale, out la2, out lo2, out narrow))
                            {
                                x = (Geo.LonToX(lo2, 14) - t.X) * tileReal; y = (Geo.LatToY(la2, 14) - t.Y) * tileReal;
                            }
                        }
                        // nearest end and the nearest different one, going either way along the piece
                        int st = Math.Min(m - 1, (int)(s / p.Len * m));
                        Node a = nodes[p.Stations[st]], b = nodes[p.Stations[st + 1]];
                        double sa = s - p.Len * st / m, sb = p.Len * (st + 1) / m - s;
                        float lx, ly, lz;
                        t.Surface(x / tileReal, y / tileReal, out lx, out ly, out lz);
                        double under = ly;
                        // the lie of the land around, not every dip and bump right below: a deck with no end near
                        // rides over that (and it reads the same from whichever map square)
                        double wide = Math.Max(0, Raw(src, t, x / tileReal, y / tileReal));
                        foreach (double radius in Around)
                            for (int e = 0; e < 16; e += 2)
                                wide += Math.Max(0, Raw(src, t, (x + Ring[e] * radius) / tileReal, (y + Ring[e + 1] * radius) / tileReal));
                        wide /= (1 + 8 * Around.Length) * Geo.Scale;
                        // the nearest ends (one of each group), going either way along the piece; the ground below
                        // counts as one more, half the reach away
                        int nc = 1;
                        cd[0] = far / 2; cg[0] = wide; cgrp[0] = -1;
                        for (int q = 0; q < 2 * Keep; q++)
                        {
                            Node nd = q < Keep ? a : b;
                            int lab = nd.L[q % Keep];
                            if (lab < 0) continue;
                            double d = nd.D[q % Keep] + (q < Keep ? sa : sb);
                            if (d >= far) continue;
                            int grp = nodes[lab].Group, at = -1;
                            for (int e = 1; e < nc; e++) if (cgrp[e] == grp) at = e;
                            if (at < 0) { at = nc++; cd[at] = double.MaxValue; cgrp[at] = grp; }
                            if (d < cd[at]) { cd[at] = d; cg[at] = nodes[lab].G; }
                        }
                        for (int e = 1; e < nc; e++)                 // nearest first
                            for (int h = e; h > 0 && cd[h] < cd[h - 1]; h--)
                            {
                                double sd = cd[h]; cd[h] = cd[h - 1]; cd[h - 1] = sd;
                                double sg = cg[h]; cg[h] = cg[h - 1]; cg[h - 1] = sg;
                                int si = cgrp[h]; cgrp[h] = cgrp[h - 1]; cgrp[h - 1] = si;
                            }
                        // each has a say that fades to nothing just as the next one further off would take its
                        // place, so the deck has no jumps
                        double cut = nc >= Keep ? cd[Keep - 1] : far, sum = 0, level = 0, c1 = far, c2 = far;
                        for (int e = 0, real = 0; e < nc; e++)
                        {
                            if (cgrp[e] >= 0) { if (real == 0) c1 = cd[e]; else if (real == 1) c2 = cd[e]; real++; }
                            if (e >= Keep - 1) continue;
                            double w = 1 / Math.Max(cd[e], 0.5) - 1 / Math.Max(cut, 0.5);
                            if (w <= 0) continue;
                            sum += w; level += w * cg[e];
                        }
                        level = sum > 0 ? level / sum : wide;
                        double span = c1 + c2;
                        double clear = Math.Min(2.5 + 0.045 * span, LandClear + (MaxClear - LandClear) * Sea(src, t, x / tileReal, y / tileReal, tileReal, sea));
                        double ramp = Math.Max(15, Math.Min(clear / Slope, 0.45 * span));
                        double u = Math.Min(1, Math.Min(c1, c2) / ramp);
                        // level from end to end where that already clears what is below (a valley, or the run-up to
                        // a landmark bridge high above the shore); humped only where the ground gives no room
                        double rise = u * u * (3 - 2 * u), open = Math.Min(1, Math.Max(0, (Math.Min(c1, c2) - 60) / 240));
                        double over = under + (wide - under) * open + clear * rise / Geo.Scale - level, soft = 0.6 * rise;
                        double deck = level + 0.5 * (over + Math.Sqrt(over * over + soft * soft));
                        if (deck < under + 0.08) deck = under + 0.08;
                        // over water a deck never lies on the surface, except right where it lands
                        double wetHere = 0;
                        if (water != null)
                        {
                            double uu = x / tileReal, vv = y / tileReal, o = 8 / tileReal;
                            wetHere = ((water(uu, vv) ? 2 : 0) + (water(uu + o, vv) ? 1 : 0) + (water(uu - o, vv) ? 1 : 0) + (water(uu, vv + o) ? 1 : 0) + (water(uu, vv - o) ? 1 : 0)) / 6.0;
                            double lift = WaterClear * wetHere * Math.Min(1, c1 / 8.0);
                            if (deck < under + lift) deck = under + lift;
                        }
                        line.Add(lx); line.Add((float)deck); line.Add(lz); line.Add(ly); line.Add(narrow); line.Add((float)wetHere);
                    }
                }
                Deck(t, line, p.Width, p.Pal, p.Item);
            }
        }

        /// <summary>Does a ground road, wide enough to count beside a bridge of this width, end at this spot?</summary>
        static bool Anchored(Dictionary<long, List<(double x, double y, float w)>> anchors, double x, double y, float width)
        {
            long cx = (long)Math.Floor(x / 2.0), cy = (long)Math.Floor(y / 2.0);
            for (long j = cy - 1; j <= cy + 1; j++)
                for (long i = cx - 1; i <= cx + 1; i++)
                {
                    List<(double x, double y, float w)> cell;
                    if (!anchors.TryGetValue(i * 1000003L + j, out cell)) continue;
                    foreach (var a in cell)
                        if (a.w * 1.3f >= width && (a.x - x) * (a.x - x) + (a.y - y) * (a.y - y) < 0.9 * 0.9) return true;
                }
            return false;
        }

        /// <summary>The stretch of a piece from point a to point b.</summary>
        static Piece Part(Piece p, int a, int b)
        {
            var q = new Piece { Centre = p.Centre, Width = p.Width, Pal = p.Pal, Item = p.Item, P = new double[(b - a + 1) * 2], At = new double[b - a + 1] };
            Array.Copy(p.P, a * 2, q.P, 0, q.P.Length);
            for (int i = 0; i < q.At.Length; i++) q.At[i] = p.At[a + i] - p.At[a];
            q.Len = q.At[q.At.Length - 1];
            return q;
        }

        static bool Shares(Node a, Node b)
        {
            if (a.Near == null || b.Near == null) return false;
            foreach (int g in a.Near) if (b.Near.Contains(g)) return true;
            return false;
        }

        static bool Offer(List<Node> nodes, Node v, double d, int label)
        {
            int g = nodes[label].Group;
            for (int i = 0; i < Keep; i++)
                if (v.L[i] >= 0 && nodes[v.L[i]].Group == g)
                {
                    if (d >= v.D[i]) return false;
                    for (int k = i; k < Keep - 1; k++) { v.D[k] = v.D[k + 1]; v.L[k] = v.L[k + 1]; }
                    v.D[Keep - 1] = double.MaxValue; v.L[Keep - 1] = -1;
                    break;
                }
            for (int i = 0; i < Keep; i++)
                if (d < v.D[i])
                {
                    for (int k = Keep - 1; k > i; k--) { v.D[k] = v.D[k - 1]; v.L[k] = v.L[k - 1]; }
                    v.D[i] = d; v.L[i] = label;
                    return true;
                }
            return false;
        }

        static int NodeAt(List<Node> nodes, Dictionary<long, List<int>> grid, double x, double y)
        {
            long cx = (long)Math.Floor(x / 2.0), cy = (long)Math.Floor(y / 2.0);
            for (long j = cy - 1; j <= cy + 1; j++)
                for (long i = cx - 1; i <= cx + 1; i++)
                {
                    List<int> cell;
                    if (!grid.TryGetValue(i * 1000003L + j, out cell)) continue;
                    foreach (int k in cell)
                    {
                        double dx = nodes[k].X - x, dy = nodes[k].Y - y;
                        if (dx * dx + dy * dy < 0.9 * 0.9) return k;
                    }
                }
            nodes.Add(new Node { X = x, Y = y });
            List<int> own;
            long key = cx * 1000003L + cy;
            if (!grid.TryGetValue(key, out own)) grid[key] = own = new List<int>();
            own.Add(nodes.Count - 1);
            return nodes.Count - 1;
        }

        /// <summary>Raw terrain height (real metres) at a spot given in centre-tile units, which may lie in a neighbour.</summary>
        static float Raw(TileSources src, TileResult t, double u, double v, bool asMesh = false)
        {
            const int G = TileResult.Grid;
            double gx = (t.X + u) * G, gy = (t.Y + v) * G;
            long i = (long)Math.Floor(gx), j = (long)Math.Floor(gy);
            float fx = (float)(gx - i), fy = (float)(gy - j);
            float a = src.Dem.Pixel(12, i, j), b = src.Dem.Pixel(12, i + 1, j), c = src.Dem.Pixel(12, i, j + 1), d = src.Dem.Pixel(12, i + 1, j + 1);
            if (asMesh)             // the way the ground mesh joins the same four readings: two flat triangles
                return fx + fy <= 1f ? a + fx * (b - a) + fy * (c - a) : d + (1f - fx) * (c - d) + (1f - fy) * (b - d);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        static readonly double[] Around = { 60, 120 };   // real metres
        static readonly double[] Ring = { 1, 0, 0.707, 0.707, 0, 1, -0.707, 0.707, -1, 0, -0.707, -0.707, 0, -1, 0.707, -0.707 };

        /// <summary>
        /// How much open sea surrounds a spot, 0 to 1 (the terrain data is at or below zero under the sea).
        /// Only bridges out over the sea climb high; over land and rivers they stay low. It is worked out on a
        /// fixed grid of spots about 240 m apart, the same for every map square, and blended in between, so it
        /// changes gently along a bridge and neighbouring squares agree.
        /// </summary>
        static double Sea(TileSources src, TileResult t, double u, double v, double tileReal, Dictionary<long, double> seen)
        {
            const int N = 8;
            double gx = (t.X + u) * N, gy = (t.Y + v) * N;
            long i = (long)Math.Floor(gx), j = (long)Math.Floor(gy);
            double fx = gx - i, fy = gy - j;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            double a = SeaAt(src, t, i, j, tileReal, seen), b = SeaAt(src, t, i + 1, j, tileReal, seen);
            double c = SeaAt(src, t, i, j + 1, tileReal, seen), d = SeaAt(src, t, i + 1, j + 1, tileReal, seen);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        static double SeaAt(TileSources src, TileResult t, long i, long j, double tileReal, Dictionary<long, double> seen)
        {
            long key = i * 4000037L + j;
            double f;
            if (seen.TryGetValue(key, out f)) return f;
            double u = i / 8.0 - t.X, v = j / 8.0 - t.Y;
            // "wet" fades in between 1.5 m and sea level
            double wet = 2 * Wet(Raw(src, t, u, v)), all = 2;
            foreach (double radius in new[] { 150.0, 300.0, 450.0, 600.0 })
                for (int k = 0; k < 16; k += 2)
                {
                    all++;
                    wet += Wet(Raw(src, t, u + Ring[k] * radius / tileReal, v + Ring[k + 1] * radius / tileReal));
                }
            f = Math.Min(1, Math.Max(0, (wet / all - 0.25) / 0.6));
            f = f * f * (3 - 2 * f);
            seen[key] = f;
            return f;
        }

        static double Wet(float h) { return Math.Min(1, Math.Max(0, 1 - h / 1.5)); }

        /// <summary>Keep the part of a line that lies inside its own tile, and note it in metres.</summary>
        static void Clip(int[] part, int extent, int dx, int dy, double tileReal, TileBuilder.RoadItem r, bool centre, List<Piece> pieces)
        {
            var run = new List<double>();
            double k = tileReal / extent;
            int n = part.Length / 2;
            for (int i = 0; i + 1 < n; i++)
            {
                double ax = part[i * 2], ay = part[i * 2 + 1], bx = part[i * 2 + 2], by = part[i * 2 + 3];
                double t0 = 0, t1 = 1;
                bool inside = TileBuilder.ClipT(-(bx - ax), ax, ref t0, ref t1) && TileBuilder.ClipT(bx - ax, extent - ax, ref t0, ref t1) &&
                              TileBuilder.ClipT(-(by - ay), ay, ref t0, ref t1) && TileBuilder.ClipT(by - ay, extent - ay, ref t0, ref t1);
                if (!inside || t1 - t0 <= 0) { Flush(run, r, centre, pieces); continue; }
                if (run.Count == 0 || t0 > 0)
                {
                    Flush(run, r, centre, pieces);
                    run.Add((ax + (bx - ax) * t0) * k + dx * tileReal); run.Add((ay + (by - ay) * t0) * k + dy * tileReal);
                }
                run.Add((ax + (bx - ax) * t1) * k + dx * tileReal); run.Add((ay + (by - ay) * t1) * k + dy * tileReal);
                if (t1 < 1) Flush(run, r, centre, pieces);
            }
            Flush(run, r, centre, pieces);
        }

        static void Flush(List<double> run, TileBuilder.RoadItem r, bool centre, List<Piece> pieces)
        {
            if (run.Count >= 4)
            {
                var p = new Piece { P = run.ToArray(), Centre = centre, Width = r.Style.RealWidth * TileBuilder.RoadScale, Pal = r.Style.Pal, Item = r };
                int n = p.P.Length / 2;
                p.At = new double[n];
                for (int i = 1; i < n; i++)
                {
                    double dx = p.P[i * 2] - p.P[i * 2 - 2], dy = p.P[i * 2 + 1] - p.P[i * 2 - 1];
                    p.At[i] = p.At[i - 1] + Math.Sqrt(dx * dx + dy * dy);
                }
                p.Len = p.At[n - 1];
                if (p.Len > 0.2) pieces.Add(p);
            }
            run.Clear();
        }

        // ------------------------------------------------------------ the deck itself

        /// <summary>line holds x, deck height, z, ground height for each point along the centre.</summary>
        const int Row = 6;

        /// <summary>line holds x, deck height, z, ground height and width factor for each point along the centre.</summary>
        static void Deck(TileResult t, List<float> line, float width, int pal, TileBuilder.RoadItem item = null)
        {
            int n = line.Count / Row;
            if (n < 2) return;
            MeshData m = t.Bridges;
            float cu = (pal % 16 + 0.5f) / 16f, cv = (pal / 16 + 0.5f) / 16f;
            float su = (Style.PalPave % 16 + 0.5f) / 16f, sv = (Style.PalPave / 16 + 0.5f) / 16f;
            float full = width * 0.5f + 0.15f;
            var run = new BridgeRun { HalfWidth = full, P = new float[n * 3], MinX = float.MaxValue, MinZ = float.MaxValue, MaxX = float.MinValue, MaxZ = float.MinValue };
            int pl = -1, pr = -1, plb = -1, prb = -1, pls = -1, prs = -1, plsb = -1, prsb = -1;
            var topL = new List<float>(n * 3); var topR = new List<float>(n * 3);     // the road surface laid on top (RoadNet)
            float sincePier = 6f;
            for (int i = 0; i < n; i++)
            {
                float x = line[i * Row], y = line[i * Row + 1], z = line[i * Row + 2], ground = line[i * Row + 3];
                float hw = width * 0.5f * line[i * Row + 4] + 0.15f;
                run.P[i * 3] = x; run.P[i * 3 + 1] = y; run.P[i * 3 + 2] = z;
                if (x < run.MinX) run.MinX = x; if (x > run.MaxX) run.MaxX = x;
                if (z < run.MinZ) run.MinZ = z; if (z > run.MaxZ) run.MaxZ = z;
                int i0 = Math.Max(0, i - 1), i1 = Math.Min(n - 1, i + 1);
                float dx = line[i1 * Row] - line[i0 * Row], dz = line[i1 * Row + 2] - line[i0 * Row + 2];
                float dl = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dl < 1e-5f) { dx = 1; dz = 0; dl = 1; }
                dx /= dl; dz /= dl;
                float ox = -dz * hw, oz = dx * hw;                    // to the left of the way we are going
                // where the deck comes down to the ground it lies on it like a road, tilted with the slope,
                // so that it runs straight into the road that carries on from its end; and while it is still
                // low its sides go down to the ground like the walls of a ramp, so nothing shows underneath
                float yl = y, yr = y, tl = Thick, tr = Thick;
                if (y - ground < 4f && line[i * Row + 5] < 0.4f)
                {
                    float gl = t.HeightAt(x + ox, z + oz), gr = t.HeightAt(x - ox, z - oz);
                    yl = Edge(gl, y, ground); yr = Edge(gr, y, ground);
                    tl = Skirt(yl - gl); tr = Skirt(yr - gr);
                }
                int l = m.Add(x + ox, yl, z + oz, 0, 1, 0, cu, cv), r = m.Add(x - ox, yr, z - oz, 0, 1, 0, cu, cv);
                float ih = (hw - 0.15f) / hw;                                         // the carriageway, inside the deck's lip
                topL.Add(x + ox * ih); topL.Add(yl); topL.Add(z + oz * ih); topR.Add(x - ox * ih); topR.Add(yr); topR.Add(z - oz * ih);
                int lb = m.Add(x + ox, yl - tl, z + oz, 0, -1, 0, su, sv), rb = m.Add(x - ox, yr - tr, z - oz, 0, -1, 0, su, sv);
                int ls = m.Add(x + ox, yl, z + oz, -dz, 0, dx, su, sv), lsb = m.Add(x + ox, yl - tl, z + oz, -dz, 0, dx, su, sv);
                int rs = m.Add(x - ox, yr, z - oz, dz, 0, -dx, su, sv), rsb = m.Add(x - ox, yr - tr, z - oz, dz, 0, -dx, su, sv);
                if (pl >= 0)
                {
                    m.Tri(pl, pr, l, 0, 1, 0); m.Tri(pr, r, l, 0, 1, 0);
                    m.Tri(plb, prb, lb, 0, -1, 0); m.Tri(prb, rb, lb, 0, -1, 0);
                    m.Tri(pls, plsb, ls, -dz, 0, dx); m.Tri(plsb, lsb, ls, -dz, 0, dx);
                    m.Tri(prs, prsb, rs, dz, 0, -dx); m.Tri(prsb, rsb, rs, dz, 0, -dx);
                    float step = (float)Math.Sqrt((x - line[i * Row - Row]) * (x - line[i * Row - Row]) + (z - line[i * Row - Row + 2]) * (z - line[i * Row - Row + 2]));
                    sincePier += step;
                }
                pl = l; pr = r; plb = lb; prb = rb; pls = ls; prs = rs; plsb = lsb; prsb = rsb;
                // a pier now and then, where the deck is well clear of what it crosses
                if (sincePier >= 12f && y - ground > 2.4f && i > 0 && i < n - 1)
                {
                    sincePier = 0;
                    float pw = Math.Max(0.35f, hw * 0.55f), pd = 0.3f;
                    Box(m, x, z, dx, dz, pw, pd, Math.Min(ground, t.BedAt(x, z)) - 0.4f, y - Thick, su, sv);   // down to the bed where it stands in water
                }
            }
            t.BridgeRuns.Add(run);
            if (item != null) RoadNet.DeckTop(t, item, topL, topR);
        }

        /// <summary>How far down the side of the deck goes: to the ground while it is low, a plain slab once it is up.</summary>
        static float Skirt(float gap)
        {
            if (gap <= 1.4f) return Math.Max(0.05f, gap + 0.15f);
            if (gap >= 2.4f) return Thick;
            float f = gap - 1.4f;
            return 1.55f + (Thick - 1.55f) * f * f * (3 - 2 * f);
        }

        /// <summary>Height of a deck edge: level when the deck is in the air, following the ground when it lies on it.</summary>
        static float Edge(float ge, float deck, float ground)
        {
            float gap = deck - ground;
            if (gap >= 2f) return deck;
            if (ge > ground)
            {
                // never let the uphill side dig into the slope while the deck is low
                float f = Math.Min(1f, Math.Max(0f, 2f - gap));
                f = f * f * (3 - 2 * f);
                return deck + Math.Max(0f, ge + 0.08f - deck) * f;
            }
            float u = Math.Min(1f, Math.Max(0f, gap));
            u = u * u * (3 - 2 * u);
            return deck + (ge - ground) * (1 - u);
        }

        /// <summary>An upright block: half-width pw across the road, half-depth pd along it.</summary>
        static void Box(MeshData m, float x, float z, float dx, float dz, float pw, float pd, float y0, float y1, float u, float v)
        {
            float ax = -dz * pw, az = dx * pw, bx = dx * pd, bz = dz * pd;
            float[] cx = { x + ax + bx, x + ax - bx, x - ax - bx, x - ax + bx }, cz = { z + az + bz, z + az - bz, z - az - bz, z - az + bz };
            for (int s = 0; s < 4; s++)
            {
                int e = (s + 1) % 4;
                float nx = (cx[s] + cx[e]) / 2 - x, nz = (cz[s] + cz[e]) / 2 - z, nl = (float)Math.Sqrt(nx * nx + nz * nz);
                nx /= nl; nz /= nl;
                int a = m.Add(cx[s], y0, cz[s], nx, 0, nz, u, v), b = m.Add(cx[e], y0, cz[e], nx, 0, nz, u, v);
                int c = m.Add(cx[e], y1, cz[e], nx, 0, nz, u, v), d = m.Add(cx[s], y1, cz[s], nx, 0, nz, u, v);
                m.Tri(a, b, c, nx, 0, nz); m.Tri(a, c, d, nx, 0, nz);
            }
        }
    }
}
