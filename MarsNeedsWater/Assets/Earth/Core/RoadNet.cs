using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The roads, streets, paths and railways of a tile, drawn as clean flat shapes in the game's cartoon style.
    ///
    /// The map gives every road as loose lines. Here they are first joined into a network: lines are cut where
    /// three or more meet, and pieces that carry on into each other (the same street past a side road) are strung
    /// together into one long run. Each run is then rounded off at its bends and painted on the ground with
    /// RoadPaint, in layers: pavements and verges at the bottom, kerbs, then the road surfaces from footpaths up to
    /// motorways (each with its own lines and crossings straight on top of it), then rails. Because all the kerbs
    /// are painted before any road surface, streets flow into each other at junctions with no seams; and because
    /// a road's lines belong to its surface, a bigger road passing over a smaller one covers its lines too.
    ///
    /// Lines stop short of junctions (but carry straight on along a main road past a side street), side streets
    /// get a stop line, dashes are spaced to fit evenly between junctions and to meet across tile borders, and
    /// crossings drawn in the map become zebras. Streets among buildings get a pavement and kerb; out of town they
    /// get a soft verge instead.
    /// </summary>
    public static class RoadNet
    {
        // what a road is made of
        public const int Asphalt = 0, Paving = 1, Concrete = 2, Dirt = 3, Gravel = 4, Steps = 5, Cycle = 6, Rail = 7;

        // centre line
        public const int None = 0, Dashed = 1, Solid = 2, Twin = 3;

        // layers, bottom to top
        const int LUnder = 0, LKerb = 1, LFill = 2, LSteel = 10, LayerCount = 11;

        // sizes, game metres
        const float KerbW = 0.12f, WalkW = 0.85f, VergeW = 0.16f;
        const float LineHalf = 0.08f, ThinHalf = 0.06f;
        const double DashPeriod = 3.2, DashOn = 1.35, LongPeriod = 4.8, LongOn = 2.0;

        // colours
        const uint White = 0xf6f3e8, Yellow = 0xf7c843;
        const uint AsphaltMajor = 0x4f535e, AsphaltMinor = 0x595d68, AsphaltService = 0x666a74;
        const uint KerbColour = 0x8e8a82, WalkColour = 0xc4c2bc, VergeColour = 0xb3a98f;
        const uint PavingColour = 0xd8c9ab, ConcreteColour = 0xd4cec0, DirtColour = 0xbc9f6e, GravelColour = 0xb9b1a0;
        const uint CyclePlain = 0x7f848e, CycleRed = 0xc46c5c, StepsColour = 0xc2bbad, BallastColour = 0x938b7d, SteelColour = 0xd0d5db;

        public sealed class Spec
        {
            public int Surface, Rank, Lanes = 2, Centre, Sig;
            public bool Car, OneWay, Crosswalk, Tram, EdgeLines, LaneLines, YellowCentre, LongDash, Ruts, Link;
            public uint Colour;
            public float Seed;
        }

        // ------------------------------------------------------------ from the map

        static int Rank(string cls)
        {
            switch (cls)
            {
                case "motorway": return 7;
                case "trunk": return 6;
                case "primary": return 5;
                case "secondary": return 4;
                case "tertiary": return 3;
                case "residential": case "unclassified": return 2;
                case "living_street": case "service": case "unknown": return 1;
            }
            return 0;
        }

        /// <summary>The Americas paint the line between opposing traffic yellow.</summary>
        static bool YellowLand(double lat, double lon) { return lon < -30 && lon > -170; }

        /// <summary>Countries that drive on the left (coarse boxes; it only decides which half gets the stop line).</summary>
        static bool LeftHand(double lat, double lon)
        {
            if (lat > 49.8 && lat < 61 && lon > -11 && lon < 2) return true;                // Britain and Ireland
            if (lat > 24 && lat < 46 && lon > 128 && lon < 146) return true;                // Japan
            if (lat > -45 && lat < -10 && lon > 112 && lon < 154) return true;              // Australia
            if (lat > -48 && lat < -34 && lon > 165 && lon < 179) return true;              // New Zealand
            if (lat > 6 && lat < 31 && lon > 68 && lon < 89) return true;                   // India and its neighbours
            if (lat > -35 && lat < -15 && lon > 12 && lon < 41) return true;                // southern Africa
            if (lat > -11 && lat < 7 && lon > 95 && lon < 141) return true;                 // Indonesia, Malaysia, Singapore
            if (lat > 5 && lat < 21 && lon > 97 && lon < 106) return true;                  // Thailand
            if (lat > 21.9 && lat < 22.6 && lon > 113.8 && lon < 114.5) return true;        // Hong Kong, Macau
            if (lat > -5 && lat < 5 && lon > 33 && lon < 42) return true;                   // Kenya, Uganda
            return false;
        }

        /// <summary>[{"value":"paved"}] gives paved: the first rule of a map property written as a list of rules.</summary>
        static string First(string rules)
        {
            if (rules == null) return null;
            int i = rules.IndexOf("\"value\":", StringComparison.Ordinal);
            if (i < 0) return null;
            i += 8;
            if (i < rules.Length && rules[i] == '"')
            {
                int e = rules.IndexOf('"', i + 1);
                return e > i ? rules.Substring(i + 1, e - i - 1) : null;
            }
            int j = i;
            while (j < rules.Length && (char.IsDigit(rules[j]) || rules[j] == '.' || rules[j] == '-')) j++;
            return rules.Substring(i, j - i);
        }

        static uint Hash(string s)
        {
            uint h = 2166136261;
            if (s != null) foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return h;
        }

        /// <summary>
        /// Called by TileBuilder.CollectRoads for every tile at every zoom (and for the neighbours Bridges reads),
        /// so a road is the same kind and width wherever it is used. Works out what each road is made of, how wide
        /// it is and which lines it carries; drops the map's separate pavement lines (streets get their own).
        /// </summary>
        internal static void Prepare(List<TileBuilder.RoadItem> list, MvtLayer L, int tz, int tx, int ty)
        {
            double lat = Geo.TileLat(ty + 0.5, tz), lon = Geo.TileLon(tx + 0.5, tz);
            bool yellow = YellowLand(lat, lon);
            bool redCycle = lat > 35 && lat < 72 && lon > -11 && lon < 32;      // Europe paints its cycle paths red
            int kSub = L.Key("subtype"), kCls = L.Key("class"), kSc = L.Key("subclass"), kSurf = L.Key("road_surface"),
                kAcc = L.Key("access_restrictions"), kWid = L.Key("width_rules"), kFlags = L.Key("road_flags"), kId = L.Key("id");

            for (int n = list.Count - 1; n >= 0; n--)
            {
                TileBuilder.RoadItem r = list[n];
                string sub = L.Str(r.F, kSub), cls = L.Str(r.F, kCls) ?? "unknown", sc = L.Str(r.F, kSc);
                if (sub != "rail" && sc == "sidewalk") { list.RemoveAt(n); continue; }
                uint h = Hash(L.Str(r.F, kId));
                var k = new Spec { Rank = Rank(cls), Seed = (h & 0xffff) / 65535f };
                r.Spec = k;
                if (sub == "rail")
                {
                    k.Surface = Rail; k.Rank = 0; k.Tram = cls == "tram";
                    k.Colour = BallastColour;
                    k.Sig = (int)(Hash(cls) & 0xffff) | 0x10000;
                    continue;
                }
                if (sc == "crosswalk") { k.Crosswalk = true; k.Rank = 0; continue; }

                string surf = First(L.Str(r.F, kSurf));
                bool unpaved = surf == "unpaved" || surf == "dirt" || surf == "ground" || surf == "earth" || surf == "grass" || surf == "mud" || surf == "sand";
                bool gravel = surf == "gravel" || surf == "fine_gravel" || surf == "compacted" || surf == "pebblestone";
                bool stones = surf == "paving_stones" || surf == "sett" || surf == "cobblestone" || surf == "unhewn_cobblestone" || surf == "bricks";
                bool sealedTop = surf == "paved" || surf == "asphalt" || surf == "concrete";
                string flags = L.Str(r.F, kFlags);
                bool link = sc == "link" || (flags != null && flags.Contains("is_link"));
                string acc = L.Str(r.F, kAcc);
                bool oneway = acc != null && acc.Contains("\"heading\":\"backward\"") && acc.Contains("denied");

                switch (cls)
                {
                    case "pedestrian":
                        k.Surface = unpaved ? Dirt : gravel ? Gravel : Paving; break;
                    case "footway":
                        k.Surface = stones ? Paving : unpaved ? Dirt : gravel ? Gravel : Concrete; break;
                    case "steps": k.Surface = Steps; break;
                    case "cycleway":
                        k.Surface = unpaved ? Dirt : gravel ? Gravel : Cycle; break;
                    case "path": case "bridleway":
                        k.Surface = sealedTop ? Concrete : stones ? Paving : gravel ? Gravel : Dirt; break;
                    case "track":
                        k.Surface = sealedTop ? Asphalt : gravel ? Gravel : Dirt;
                        k.Ruts = k.Surface != Asphalt;
                        break;
                    default:
                        k.Car = true;
                        k.Surface = unpaved ? Dirt : gravel ? Gravel : stones ? Paving : Asphalt;
                        k.Ruts = k.Surface == Dirt || k.Surface == Gravel;
                        break;
                }
                switch (k.Surface)
                {
                    case Paving: k.Colour = PavingColour; break;
                    case Concrete: k.Colour = ConcreteColour; break;
                    case Dirt: k.Colour = DirtColour; break;
                    case Gravel: k.Colour = GravelColour; break;
                    case Steps: k.Colour = StepsColour; break;
                    case Cycle: k.Colour = redCycle ? CycleRed : CyclePlain; break;
                    default: k.Colour = k.Rank >= 3 ? AsphaltMajor : k.Rank == 2 ? AsphaltMinor : AsphaltService; break;
                }
                float real = r.Style.RealWidth;
                if (k.Car)
                {
                    // width and lanes: the map's own width when it gives one, else from the class of road
                    int lanes = 2;
                    switch (cls)
                    {
                        case "motorway": lanes = oneway ? 3 : 4; real = oneway ? 13f : 22f; k.EdgeLines = true; k.LongDash = true; break;
                        case "trunk": lanes = 2; real = oneway ? 11.5f : 12f; k.EdgeLines = true; k.LongDash = true; break;
                        case "primary": lanes = oneway ? 2 : 4; real = oneway ? 8.5f : 14f; break;
                        case "secondary": lanes = 2; real = oneway ? 8f : 10.5f; break;
                        case "tertiary": lanes = 2; real = oneway ? 7.5f : 9f; break;
                        case "residential": case "unclassified": lanes = oneway ? 1 : 2; real = oneway ? 5.5f : 7.5f; break;
                        case "living_street": lanes = 1; real = 6f; break;
                        case "service": lanes = 1; real = sc == "parking_aisle" ? 6f : sc == "driveway" || sc == "alley" ? 3.5f : 4.5f; break;
                        default: lanes = 1; break;
                    }
                    if (link) { lanes = 1; real = 6f; k.EdgeLines = true; oneway = true; k.Link = true; }
                    float given;
                    string ws = First(L.Str(r.F, kWid));
                    if (ws != null && float.TryParse(ws, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out given) && given >= 2.5f && given <= 40f)
                    {
                        real = given;
                        if (k.Rank >= 3) lanes = Math.Max(oneway ? 1 : 2, (int)Math.Floor(given / 3.3));
                        if (!oneway && lanes % 2 == 1) lanes = Math.Max(2, lanes - 1);
                    }
                    k.Lanes = Math.Max(1, lanes);
                    k.OneWay = oneway;
                    if (k.Surface == Asphalt)
                    {
                        if (oneway) k.LaneLines = k.Lanes > 1;
                        else if (k.Rank >= 3)
                        {
                            k.Centre = k.Rank >= 4 && yellow ? Twin : k.Rank >= 6 ? Solid : Dashed;
                            k.YellowCentre = yellow;
                            k.LaneLines = k.Lanes > 2;
                        }
                    }
                    else k.EdgeLines = false;
                    r.Style.RealWidth = real;
                }
                k.Sig = (int)((Hash(cls) & 0xfff) | (uint)(k.Lanes << 12) | (uint)(k.Surface << 16) | (oneway ? 1u << 20 : 0u) | (link ? 1u << 21 : 0u) | ((uint)(real * 2f) << 22));
            }
        }

        /// <summary>Real width a road takes up on the ground picture.</summary>
        internal static float PaintWidth(TileBuilder.RoadItem r) { return r.Style.RealWidth; }

        // ------------------------------------------------------------ the network

        sealed class Edge { public double[] P; public long A, B; public TileBuilder.RoadItem R; public bool Used; public Leg LegA, LegB; }

        sealed class Leg
        {
            public Edge E; public bool Start, Bridge, Car;
            public double Dx, Dy;           // the way it leaves the junction
            public float Hw; public int Rank, Sig;
            public Leg Pair; public Node At;
            public TileBuilder.RoadItem R; public double Len;
            public bool Bent;               // it turns within its first few metres: no clean corner to round off
        }

        sealed class Node { public double X, Y; public readonly List<Leg> Legs = new List<Leg>(); }

        struct End { public double Trim; public bool Stop, Butt; }

        sealed class Chain
        {
            public TileBuilder.RoadItem R; public Spec K; public float Hw;
            public double[] X, Y, S, Nx, Ny, M;
            public int N; public double Len;
            public End Head, Tail;
            public List<double> Gaps = new List<double>();       // s, half-length, 1 if it also breaks the centre line
            public List<double> Zebras;
            public long Order;
        }

        sealed class Job
        {
            public TileResult T; public double S; public RoadPaint Paint; public bool Left;
            public readonly Dictionary<long, Node> Nodes = new Dictionary<long, Node>();
            public readonly List<Chain> Chains = new List<Chain>();
            public int[] Built; public int BuiltN; public const double BuiltCell = 4.0;
        }

        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }

        /// <summary>Builds t.Roads: every road, path, railway, pavement and crossing of a zoom-14 tile.</summary>
        internal static void Build(TileResult t, List<TileBuilder.RoadItem> roads, MvtLayer L)
        {
            t.Roads = new MeshData();
            if (L == null) return;
            double e = L.Extent, S = t.Size;
            var job = new Job { T = t, S = S, Paint = new RoadPaint(t, LayerCount), Left = LeftHand(t.Lat, t.Lon) };

            // ---- how many line ends meet at each map point (a line passing through counts as two)
            var meet = new Dictionary<long, int>();
            foreach (TileBuilder.RoadItem r in roads)
            {
                if (r.Spec == null || r.Spec.Crosswalk) continue;
                foreach (List<int[]> set in new[] { r.Plain, r.Bridge })
                    foreach (int[] p in set)
                    {
                        int n = p.Length / 2;
                        for (int i = 0; i < n; i++)
                        {
                            long k = Key(p[i * 2], p[i * 2 + 1]);
                            int c; meet.TryGetValue(k, out c);
                            meet[k] = c + (i == 0 || i == n - 1 ? 1 : 2);
                        }
                    }
                t.RoadCount++;
            }

            // ---- cut the lines at the junctions
            var edges = new List<Edge>();
            foreach (TileBuilder.RoadItem r in roads)
            {
                if (r.Spec == null || r.Spec.Crosswalk) continue;
                float hw = r.Style.RealWidth * TileBuilder.RoadScale * 0.5f;
                foreach (int[] p in r.Plain)
                {
                    int n = p.Length / 2, from = 0;
                    for (int i = 1; i < n; i++)
                    {
                        if (i < n - 1 && meet[Key(p[i * 2], p[i * 2 + 1])] < 3) continue;
                        var pts = new List<double>();
                        for (int q = from; q <= i; q++)
                        {
                            double x = p[q * 2] / e * S, y = p[q * 2 + 1] / e * S;
                            if (pts.Count >= 2 && Math.Abs(pts[pts.Count - 2] - x) < 1e-6 && Math.Abs(pts[pts.Count - 1] - y) < 1e-6) continue;
                            pts.Add(x); pts.Add(y);
                        }
                        if (pts.Count >= 4)
                        {
                            var ed = new Edge { P = pts.ToArray(), A = Key(p[from * 2], p[from * 2 + 1]), B = Key(p[i * 2], p[i * 2 + 1]), R = r };
                            ed.LegA = AddLeg(job, ed.A, ed, true, ed.P, false, hw, r);
                            ed.LegB = AddLeg(job, ed.B, ed, false, ed.P, true, hw, r);
                            edges.Add(ed);
                        }
                        from = i;
                    }
                }
                // a bridge is drawn by Bridges, but its ends count at the junctions they touch
                foreach (int[] p in r.Bridge)
                {
                    int n = p.Length / 2;
                    if (n < 2) continue;
                    var q = new double[p.Length];
                    for (int i = 0; i < p.Length; i++) q[i] = p[i] / e * S;
                    AddLeg(job, Key(p[0], p[1]), null, true, q, false, hw, r);
                    AddLeg(job, Key(p[p.Length - 2], p[p.Length - 1]), null, false, q, true, hw, r);
                }
            }

            // ---- which pieces carry on into each other
            foreach (Node nd in job.Nodes.Values)
            {
                List<Leg> g = nd.Legs;
                if (g.Count == 2)
                {
                    Leg a = g[0], b = g[1];
                    if (!a.Bridge && !b.Bridge && a.Sig == b.Sig && a.E != b.E) { a.Pair = b; b.Pair = a; }
                    continue;
                }
                if (g.Count < 3) continue;
                // the straightest pairs of the same kind of road, most important roads first
                while (true)
                {
                    Leg ba = null, bb = null; double best = double.MaxValue;
                    for (int i = 0; i < g.Count; i++)
                        for (int j = i + 1; j < g.Count; j++)
                        {
                            Leg a = g[i], b = g[j];
                            if (a.Bridge || b.Bridge || a.Pair != null || b.Pair != null || a.Sig != b.Sig || a.E == b.E) continue;
                            double dot = a.Dx * b.Dx + a.Dy * b.Dy;
                            if (dot > -0.8) continue;
                            double score = dot - a.Rank * 10;
                            if (score < best) { best = score; ba = a; bb = b; }
                        }
                    if (ba == null) break;
                    ba.Pair = bb; bb.Pair = ba;
                }
            }

            // ---- string them together
            foreach (Edge first in edges)
            {
                if (first.Used) continue;
                Edge cur = first; bool fwd = true;
                for (int guard = 0; guard < 100000; guard++)
                {
                    Leg back = fwd ? cur.LegA : cur.LegB;
                    if (back.Pair == null || back.Pair.E == first || back.Pair.E.Used) break;
                    Leg nx = back.Pair;
                    cur = nx.E; fwd = !nx.Start;
                }
                var run = new List<Edge>(); var dirs = new List<bool>();
                while (true)
                {
                    cur.Used = true; run.Add(cur); dirs.Add(fwd);
                    Leg end = fwd ? cur.LegB : cur.LegA;
                    if (end.Pair == null || end.Pair.E.Used) break;
                    cur = end.Pair.E; fwd = end.Pair.Start;
                }
                Chain c = MakeChain(job, run, dirs);
                if (c != null) job.Chains.Add(c);
            }

            Buildings(job);
            Zebras(job, roads, e);
            // lesser roads first, slip roads before the roads they join: whatever comes later lies on top
            for (int i = 0; i < job.Chains.Count; i++) job.Chains[i].Order = (job.Chains[i].R.Style.Layer * 100 + job.Chains[i].K.Rank * 2 + (job.Chains[i].K.Link ? 0 : 1)) * 100000L + i;
            job.Chains.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (Chain c in job.Chains) Draw(job, c);
            foreach (Node nd in job.Nodes.Values) Corners(job, nd);
            job.Paint.Flush(t.Roads);
        }

        static Leg AddLeg(Job job, long key, Edge e, bool start, double[] p, bool fromEnd, float hw, TileBuilder.RoadItem r)
        {
            Node nd;
            int n = p.Length / 2;
            double ox = fromEnd ? p[n * 2 - 2] : p[0], oy = fromEnd ? p[n * 2 - 1] : p[1], dx = 0, dy = 0;
            if (!job.Nodes.TryGetValue(key, out nd)) job.Nodes[key] = nd = new Node { X = ox, Y = oy };
            double len = 0;
            for (int i = 1; i < n; i++) len += Math.Sqrt((p[i * 2] - p[i * 2 - 2]) * (p[i * 2] - p[i * 2 - 2]) + (p[i * 2 + 1] - p[i * 2 - 1]) * (p[i * 2 + 1] - p[i * 2 - 1]));
            // the direction it sets off in, read a little way along so a tiny first step does not mislead
            for (int i = 1; i < n; i++)
            {
                int q = fromEnd ? n - 1 - i : i;
                dx = p[q * 2] - ox; dy = p[q * 2 + 1] - oy;
                if (dx * dx + dy * dy > 0.5 * 0.5) break;
            }
            double l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 1e-9) { dx = 1; dy = 0; l = 1; }
            // does it still head the same way three metres on?
            bool bent = false;
            for (int i = 1; i < n; i++)
            {
                int q = fromEnd ? n - 1 - i : i;
                double fx = p[q * 2] - ox, fy = p[q * 2 + 1] - oy, fl = Math.Sqrt(fx * fx + fy * fy);
                if (fl < 0.5) continue;
                if ((fx * dx + fy * dy) / (fl * l) < 0.96) bent = true;
                if (fl > 3.0) break;
            }
            var leg = new Leg { Bent = bent, E = e, Start = start, Bridge = e == null, Car = r.Spec.Car, Dx = dx / l, Dy = dy / l, Hw = hw, Rank = r.Spec.Rank, Sig = r.Spec.Sig, At = nd, R = r, Len = len };
            nd.Legs.Add(leg);
            return leg;
        }

        /// <summary>What a road must keep clear of at a junction: how far back its lines stop, and whether it gives way.</summary>
        static void Others(Leg me, Leg partner, out double clear, out bool equal, out bool giveWay)
        {
            clear = 0; equal = false; giveWay = false;
            foreach (Leg o in me.At.Legs)
            {
                if (o == me || o == partner || !o.Car) continue;
                double sin = Math.Abs(me.Dx * o.Dy - me.Dy * o.Dx);
                // how far along this road the other one's surface reaches: a long way where they merge at a slant
                double cos = Math.Abs(me.Dx * o.Dx + me.Dy * o.Dy);
                clear = Math.Max(clear, Math.Min((o.Hw + me.Hw * 0.5 * cos) / Math.Max(sin, 0.12), 14.0) + (sin > 0.5 ? 0.45 : 0));
                if (o.Rank >= me.Rank) equal = true;
                if (o.Rank > me.Rank || (o.Pair != null && partner == null && o.Rank == me.Rank)) giveWay = true;
            }
        }

        static Chain MakeChain(Job job, List<Edge> run, List<bool> dirs)
        {
            TileBuilder.RoadItem r = run[0].R;
            Spec k = r.Spec;
            var c = new Chain { R = r, K = k, Hw = r.Style.RealWidth * TileBuilder.RoadScale * 0.5f };
            if (k.Surface == Rail) c.Hw = Math.Max(c.Hw, 0.8f);

            // the points of the whole run, and which of them are junctions (those stay where they are)
            var pts = new List<double>(); var pin = new List<bool>(); var joint = new List<Leg>();
            for (int q = 0; q < run.Count; q++)
            {
                double[] p = run[q].P;
                int n = p.Length / 2;
                for (int i = q == 0 ? 0 : 1; i < n; i++)
                {
                    int s = dirs[q] ? i : n - 1 - i;
                    pts.Add(p[s * 2]); pts.Add(p[s * 2 + 1]);
                    bool last = i == n - 1 && q < run.Count - 1;
                    Leg into = last ? (dirs[q] ? run[q].LegB : run[q].LegA) : null;
                    pin.Add(into != null && into.At.Legs.Count >= 3);
                    joint.Add(into);
                }
            }
            int count = pts.Count / 2;
            if (count < 2) return null;
            // off the tile altogether: nothing to draw
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                minX = Math.Min(minX, pts[i * 2]); maxX = Math.Max(maxX, pts[i * 2]);
                minY = Math.Min(minY, pts[i * 2 + 1]); maxY = Math.Max(maxY, pts[i * 2 + 1]);
            }
            double pad = c.Hw + KerbW + WalkW + 0.5;
            if (maxX < -pad || maxY < -pad || minX > job.S + pad || minY > job.S + pad) return null;

            // ---- round the bends
            var X = new List<double>(count * 2); var Y = new List<double>(count * 2);
            var at = new int[count];
            double reach = Math.Min(3.5, Math.Max(1.2, c.Hw * 1.6));
            for (int i = 0; i < count; i++)
            {
                double x = pts[i * 2], y = pts[i * 2 + 1];
                if (i == 0 || i == count - 1 || pin[i]) { at[i] = X.Count; X.Add(x); Y.Add(y); continue; }
                double ax = pts[i * 2 - 2] - x, ay = pts[i * 2 - 1] - y, bx = pts[i * 2 + 2] - x, by = pts[i * 2 + 3] - y;
                double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                if (la < 1e-6 || lb < 1e-6) { at[i] = X.Count; X.Add(x); Y.Add(y); continue; }
                ax /= la; ay /= la; bx /= lb; by /= lb;
                double turn = Math.Acos(Math.Max(-1, Math.Min(1, -(ax * bx + ay * by))));      // 0 = straight on
                if (turn < 0.06) { at[i] = X.Count; X.Add(x); Y.Add(y); continue; }
                double cut = Math.Min(reach, Math.Min(la, lb) * 0.45);
                int steps = Math.Max(2, (int)Math.Ceiling(turn / 0.11));
                at[i] = X.Count + steps / 2;
                for (int s = 0; s <= steps; s++)
                {
                    double f = (double)s / steps, u = 1 - f;
                    // a curve from a little before the corner to a little after, pulled towards the corner
                    X.Add(u * u * (x + ax * cut) + 2 * u * f * x + f * f * (x + bx * cut));
                    Y.Add(u * u * (y + ay * cut) + 2 * u * f * y + f * f * (y + by * cut));
                }
            }
            // drop points that fell on top of each other
            int m = 0;
            var keepAt = new int[X.Count];
            for (int i = 0; i < X.Count; i++)
            {
                if (m > 0 && Math.Abs(X[i] - X[m - 1]) < 1e-4 && Math.Abs(Y[i] - Y[m - 1]) < 1e-4) { keepAt[i] = m - 1; continue; }
                X[m] = X[i]; Y[m] = Y[i]; keepAt[i] = m; m++;
            }
            if (m < 2) return null;
            c.N = m;
            c.X = new double[m]; c.Y = new double[m]; c.S = new double[m]; c.Nx = new double[m]; c.Ny = new double[m]; c.M = new double[m];
            for (int i = 0; i < m; i++) { c.X[i] = X[i]; c.Y[i] = Y[i]; }
            for (int i = 1; i < m; i++) c.S[i] = c.S[i - 1] + Math.Sqrt((c.X[i] - c.X[i - 1]) * (c.X[i] - c.X[i - 1]) + (c.Y[i] - c.Y[i - 1]) * (c.Y[i] - c.Y[i - 1]));
            c.Len = c.S[m - 1];
            for (int i = 0; i < m; i++)
            {
                double d0x = 0, d0y = 0, d1x = 0, d1y = 0;
                if (i > 0) { d0x = c.X[i] - c.X[i - 1]; d0y = c.Y[i] - c.Y[i - 1]; double l = Math.Sqrt(d0x * d0x + d0y * d0y); d0x /= l; d0y /= l; }
                if (i < m - 1) { d1x = c.X[i + 1] - c.X[i]; d1y = c.Y[i + 1] - c.Y[i]; double l = Math.Sqrt(d1x * d1x + d1y * d1y); d1x /= l; d1y /= l; }
                if (i == 0) { d0x = d1x; d0y = d1y; }
                if (i == m - 1) { d1x = d0x; d1y = d0y; }
                double tx = d0x + d1x, ty = d0y + d1y, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-6) { tx = d1x; ty = d1y; tl = 1; }
                tx /= tl; ty /= tl;
                c.Nx[i] = ty; c.Ny[i] = -tx;                          // to the left of the way it runs (x east, y south)
                c.M[i] = 1 / Math.Max(0.55, tx * d0x + ty * d0y);     // longer on a bend, so the width stays even
            }

            // ---- where it must leave room: junctions along the way, and its two ends
            for (int i = 1; i < count - 1; i++)
            {
                Leg into = joint[i];
                if (into == null || !k.Car) continue;
                double clear; bool equal, give;
                Others(into, into.Pair, out clear, out equal, out give);
                if (clear <= 0) continue;
                c.Gaps.Add(c.S[keepAt[at[i]]]); c.Gaps.Add(clear + 0.15); c.Gaps.Add(equal ? 1 : 0);
            }
            c.Head = EndOf(k, c.Hw, dirs[0] ? run[0].LegA : run[0].LegB);
            int z = run.Count - 1;
            c.Tail = EndOf(k, c.Hw, dirs[z] ? run[z].LegB : run[z].LegA);
            return c;
        }

        static End EndOf(Spec k, float hw, Leg leg)
        {
            var e = new End();
            // where it runs straight on into one other road the two surfaces simply butt together
            if (leg.At.Legs.Count == 2)
            {
                Leg o = leg.At.Legs[0] == leg ? leg.At.Legs[1] : leg.At.Legs[0];
                e.Butt = !o.Bridge && o.Dx * leg.Dx + o.Dy * leg.Dy < -0.97 && Math.Abs(o.Hw - leg.Hw) < 0.4f;
            }
            if (!k.Car) return e;
            if (leg.At.Legs.Count == 1) { e.Trim = hw + 0.2; return e; }       // a dead end
            double clear; bool equal, give;
            Others(leg, null, out clear, out equal, out give);
            if (clear <= 0) return e;                                           // it simply carries on as another kind of road
            e.Trim = clear + 0.12;
            e.Stop = give && k.Rank >= 2 && k.Surface == Asphalt && !k.Link;
            return e;
        }

        // ------------------------------------------------------------ built-up ground

        /// <summary>Where the tile's buildings stand, on a coarse grid, as running totals (for quick sums over any box).</summary>
        static void Buildings(Job job)
        {
            TileResult t = job.T;
            if (t.Roofs == null || t.Roofs.Count == 0) return;
            int n = (int)Math.Ceiling(job.S / Job.BuiltCell);
            var occ = new bool[n * n];
            List<float> P = t.Roofs.P;
            for (int i = 0; i < P.Count; i += 3)
            {
                int gx = (int)((P[i] + job.S * 0.5) / Job.BuiltCell), gy = (int)((job.S * 0.5 - P[i + 2]) / Job.BuiltCell);
                if (gx >= 0 && gy >= 0 && gx < n && gy < n) occ[gy * n + gx] = true;
            }
            var sum = new int[(n + 1) * (n + 1)];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    sum[(y + 1) * (n + 1) + x + 1] = (occ[y * n + x] ? 1 : 0) + sum[y * (n + 1) + x + 1] + sum[(y + 1) * (n + 1) + x] - sum[y * (n + 1) + x];
            job.Built = sum; job.BuiltN = n;
        }

        static int BuiltIn(Job job, int cx, int cy, int r)
        {
            int n = job.BuiltN, w = n + 1;
            int x0 = Math.Max(0, cx - r), y0 = Math.Max(0, cy - r), x1 = Math.Min(n, cx + r + 1), y1 = Math.Min(n, cy + r + 1);
            if (x1 <= x0 || y1 <= y0) return 0;
            return job.Built[y1 * w + x1] - job.Built[y0 * w + x1] - job.Built[y1 * w + x0] + job.Built[y0 * w + x0];
        }

        /// <summary>Is this spot among buildings: some right here, and more round about (a lone farm does not count)?</summary>
        static bool Town(Job job, double x, double y)
        {
            if (job.Built == null) return false;
            int cx = (int)Math.Floor(x / Job.BuiltCell), cy = (int)Math.Floor(y / Job.BuiltCell);
            return BuiltIn(job, cx, cy, 2) >= 2 && BuiltIn(job, cx, cy, 8) >= 14;
        }

        // ------------------------------------------------------------ crossings

        /// <summary>Find where the map's pedestrian crossings cross a street: each becomes a zebra on that street.</summary>
        static void Zebras(Job job, List<TileBuilder.RoadItem> roads, double e)
        {
            bool any = false;
            foreach (TileBuilder.RoadItem r in roads) if (r.Spec != null && r.Spec.Crosswalk) { any = true; break; }
            if (!any) return;
            const double Cell = 10.0;
            var grid = new Dictionary<long, List<int>>();
            for (int q = 0; q < job.Chains.Count; q++)
            {
                Chain c = job.Chains[q];
                if (!c.K.Car || c.K.Surface != Asphalt) continue;
                for (int i = 0; i + 1 < c.N; i++)
                {
                    long i0 = (long)Math.Floor(Math.Min(c.X[i], c.X[i + 1]) / Cell), i1 = (long)Math.Floor(Math.Max(c.X[i], c.X[i + 1]) / Cell);
                    long j0 = (long)Math.Floor(Math.Min(c.Y[i], c.Y[i + 1]) / Cell), j1 = (long)Math.Floor(Math.Max(c.Y[i], c.Y[i + 1]) / Cell);
                    if ((i1 - i0 + 1) * (j1 - j0 + 1) > 64) continue;
                    for (long j = j0; j <= j1; j++)
                        for (long i2 = i0; i2 <= i1; i2++)
                        {
                            long key = i2 * 1000003L + j;
                            List<int> cell;
                            if (!grid.TryGetValue(key, out cell)) grid[key] = cell = new List<int>();
                            cell.Add(q); cell.Add(i);
                        }
                }
            }
            foreach (TileBuilder.RoadItem r in roads)
            {
                if (r.Spec == null || !r.Spec.Crosswalk) continue;
                foreach (int[] p in r.Plain)
                    for (int i = 0; i + 3 < p.Length; i += 2)
                    {
                        double ax = p[i] / e * job.S, ay = p[i + 1] / e * job.S, bx = p[i + 2] / e * job.S, by = p[i + 3] / e * job.S;
                        long i0 = (long)Math.Floor(Math.Min(ax, bx) / Cell), i1 = (long)Math.Floor(Math.Max(ax, bx) / Cell);
                        long j0 = (long)Math.Floor(Math.Min(ay, by) / Cell), j1 = (long)Math.Floor(Math.Max(ay, by) / Cell);
                        if ((i1 - i0 + 1) * (j1 - j0 + 1) > 64) continue;
                        for (long j = j0; j <= j1; j++)
                            for (long i2 = i0; i2 <= i1; i2++)
                            {
                                List<int> cell;
                                if (!grid.TryGetValue(i2 * 1000003L + j, out cell)) continue;
                                for (int q = 0; q < cell.Count; q += 2)
                                {
                                    Chain c = job.Chains[cell[q]];
                                    int s = cell[q + 1];
                                    double cx = c.X[s], cy = c.Y[s], dx = c.X[s + 1] - cx, dy = c.Y[s + 1] - cy;
                                    double ex = bx - ax, ey = by - ay, den = dx * ey - dy * ex;
                                    if (Math.Abs(den) < 1e-9) continue;
                                    double f = ((ax - cx) * ey - (ay - cy) * ex) / den, g = ((ax - cx) * dy - (ay - cy) * dx) / den;
                                    if (f < 0 || f > 1 || g < -0.05 || g > 1.05) continue;
                                    // square across the street only: a path wandering along it is not a crossing
                                    double sl = Math.Sqrt(dx * dx + dy * dy), el = Math.Sqrt(ex * ex + ey * ey);
                                    if (Math.Abs(den) / (sl * el) < 0.6) continue;
                                    double at = c.S[s] + (c.S[s + 1] - c.S[s]) * f;
                                    if (at < 0.6 || at > c.Len - 0.6) continue;
                                    if (c.Zebras == null) c.Zebras = new List<double>();
                                    bool dup = false;
                                    foreach (double z in c.Zebras) if (Math.Abs(z - at) < 1.6) dup = true;
                                    if (!dup) c.Zebras.Add(at);
                                }
                            }
                    }
            }
        }

        // ------------------------------------------------------------ drawing

        /// <summary>The spot a distance s along a run: where it is, which way is left, and how much wider a bend makes it.</summary>
        static void Spot(Chain c, double s, ref int seg, out double x, out double y, out double nx, out double ny, out double m)
        {
            if (seg > c.N - 2) seg = c.N - 2;
            while (seg > 0 && c.S[seg] > s) seg--;
            while (seg < c.N - 2 && c.S[seg + 1] < s) seg++;
            double l = c.S[seg + 1] - c.S[seg], f = l > 0 ? (s - c.S[seg]) / l : 0;
            if (f <= 1e-6) { x = c.X[seg]; y = c.Y[seg]; nx = c.Nx[seg]; ny = c.Ny[seg]; m = c.M[seg]; return; }
            if (f >= 1 - 1e-6) { x = c.X[seg + 1]; y = c.Y[seg + 1]; nx = c.Nx[seg + 1]; ny = c.Ny[seg + 1]; m = c.M[seg + 1]; return; }
            x = c.X[seg] + (c.X[seg + 1] - c.X[seg]) * f; y = c.Y[seg] + (c.Y[seg + 1] - c.Y[seg]) * f;
            double dx = (c.X[seg + 1] - c.X[seg]) / l, dy = (c.Y[seg + 1] - c.Y[seg]) / l;
            nx = dy; ny = -dx; m = 1;
        }

        /// <summary>
        /// A band along a run from s0 to s1, its middle 'off' metres left of the centre line. With 'own' the shape
        /// is measured from its own middle (a dash with round ends); otherwise along the whole run (a surface).
        /// </summary>
        static void Strip(Job job, int layer, RoadPaint.Ink ink, Chain c, double s0, double s1, double off, bool own)
        {
            if (s1 - s0 < 1e-4) return;
            double mid = (s0 + s1) * 0.5;
            if (own) ink.Hy = (float)((s1 - s0) * 0.5);
            float hx = ink.Hx;
            int seg = 0;
            double px = 0, py = 0, qx = 0, qy = 0, ps = 0;
            bool have = false;
            double s = s0;
            while (true)
            {
                double x, y, nx, ny, m;
                Spot(c, s, ref seg, out x, out y, out nx, out ny, out m);
                double lx = x + nx * (off + hx) * m, ly = y + ny * (off + hx) * m, rx = x + nx * (off - hx) * m, ry = y + ny * (off - hx) * m;
                if (have)
                {
                    float b0 = (float)(own ? ps - mid : ps), b1 = (float)(own ? s - mid : s);
                    job.Paint.Quad(layer, ink, px, py, hx, b0, lx, ly, hx, b1, rx, ry, -hx, b1, qx, qy, -hx, b0);
                }
                px = lx; py = ly; qx = rx; qy = ry; ps = s; have = true;
                if (s >= s1) break;
                // on to the next point of the run, in steps of 10 m at most so long straights still follow the ground
                int nextV = seg + 1;
                while (nextV < c.N - 1 && c.S[nextV] <= s + 1e-9) nextV++;
                double next = Math.Min(s1, Math.Min(c.S[nextV], s + 10.0));
                if (next <= s + 1e-9) next = s1;
                s = next;
            }
        }

        static void Disc(Job job, int layer, RoadPaint.Ink ink, Chain c, bool tail)
        {
            int i = tail ? c.N - 1 : 0;
            job.Paint.Disc(layer, ink, c.X[i], c.Y[i]);
        }

        static bool Inside(Job job, double x, double y) { return x >= 0 && y >= 0 && x <= job.S && y <= job.S; }

        /// <summary>Take [g0, g1] out of a list of stretches (pairs of from, to).</summary>
        static void Remove(List<double> spans, double g0, double g1)
        {
            for (int i = spans.Count - 2; i >= 0; i -= 2)
            {
                double a = spans[i], b = spans[i + 1];
                if (g1 <= a || g0 >= b) continue;
                spans.RemoveRange(i, 2);
                if (g1 < b) { spans.Insert(i, g1); spans.Insert(i + 1, b); }
                if (g0 > a) { spans.Insert(i, a); spans.Insert(i + 1, g0); }
            }
        }

        /// <summary>Cut stretches where the run crosses the tile's edge, and keep the parts inside.</summary>
        static List<double> InTile(Job job, Chain c, List<double> spans, List<double> edges)
        {
            var res = new List<double>();
            for (int i = 0; i < spans.Count; i += 2)
            {
                double a = spans[i], b = spans[i + 1], from = a;
                for (int q = 0; q <= edges.Count; q++)
                {
                    double to = q < edges.Count ? edges[q] : b;
                    if (to <= from) continue;
                    if (to > b) to = b;
                    if (to - from > 0.05)
                    {
                        int seg = 0; double x, y, nx, ny, m;
                        Spot(c, (from + to) * 0.5, ref seg, out x, out y, out nx, out ny, out m);
                        if (Inside(job, x, y)) { res.Add(from); res.Add(to); }
                    }
                    from = to;
                    if (from >= b) break;
                }
            }
            return res;
        }

        static void Dashes(Job job, int layer, Chain c, List<double> spans, double off, float half, uint colour, double period, double on)
        {
            for (int i = 0; i < spans.Count; i += 2)
            {
                double a = spans[i], b = spans[i + 1], len = b - a;
                if (len < on * 0.8) continue;
                int n = Math.Max(1, (int)Math.Round(len / period));
                double p = len / n, d = Math.Min(on * p / period, p * 0.6);
                var ink = RoadPaint.Ink.Box(half, 0, half * 0.7f, colour, RoadPaint.Line, c.K.Seed);
                for (int k = 0; k < n; k++)
                {
                    double mid = a + (k + 0.5) * p;
                    Strip(job, layer, ink, c, mid - d * 0.5, mid + d * 0.5, off, true);
                }
            }
        }

        static void FullLine(Job job, int layer, Chain c, List<double> spans, double off, float half, uint colour)
        {
            var ink = RoadPaint.Ink.Box(half, 0, half * 0.7f, colour, RoadPaint.Line, c.K.Seed);
            for (int i = 0; i < spans.Count; i += 2)
                if (spans[i + 1] - spans[i] > 0.4) Strip(job, layer, ink, c, spans[i], spans[i + 1], off, true);
        }

        static void Draw(Job job, Chain c)
        {
            Spec k = c.K;
            float hw = c.Hw;
            RoadPaint paint = job.Paint;
            int fill = LFill + Math.Min(7, Math.Max(0, c.R.Style.Layer));

            // ---- railways: a bed of stones with sleepers, and two rails; a tram only has its rails, set in the street
            if (k.Surface == Rail)
            {
                if (!k.Tram)
                {
                    Strip(job, fill, RoadPaint.Ink.Band(hw, BallastColour, RoadPaint.Sleepers, k.Seed, 0.1f), c, 0, c.Len, 0, false);
                }
                float gauge = k.Tram ? 0.24f : 0.3f;
                var steel = RoadPaint.Ink.Band(0.04f, SteelColour, RoadPaint.Steel, k.Seed);
                Strip(job, LSteel, steel, c, 0, c.Len, gauge, false);
                Strip(job, LSteel, steel, c, 0, c.Len, -gauge, false);
                return;
            }

            // ---- under the road: a pavement and kerb in town, a soft verge out of it
            if (k.Car)
            {
                bool walks = k.Rank >= 2 && k.Rank <= 5 && !k.Link && k.Surface != Dirt && k.Surface != Gravel;
                var spots = new List<double>(); var town = new List<bool>();
                int seg = 0;
                int steps = Math.Max(1, (int)Math.Ceiling(c.Len / 2.0));
                for (int i = 0; i <= steps; i++)
                {
                    double s = c.Len * i / steps, x, y, nx, ny, m;
                    Spot(c, s, ref seg, out x, out y, out nx, out ny, out m);
                    spots.Add(s);
                    town.Add(walks && Town(job, x, y));
                }
                Runs(town, spots, 7.0, true);        // close short gaps
                Runs(town, spots, 5.0, false);       // drop short bits
                float outer = hw + KerbW + WalkW;
                var walk = RoadPaint.Ink.Band(outer, WalkColour, RoadPaint.Slabs, k.Seed, 0, hw + KerbW);
                var kerb = RoadPaint.Ink.Band(hw + KerbW, KerbColour, RoadPaint.Kerb, k.Seed);
                var verge = RoadPaint.Ink.Band(hw + VergeW, VergeColour, RoadPaint.Verge, k.Seed, 0.07f);
                for (int i = 0; i < spots.Count; )
                {
                    int j = i;
                    while (j + 1 < spots.Count && town[j + 1] == town[i]) j++;
                    double s0 = spots[i], s1 = j + 1 < spots.Count ? spots[j + 1] : spots[j];
                    if (s1 > s0)
                    {
                        if (town[i])
                        {
                            Strip(job, LUnder, walk, c, s0, s1, 0, false);
                            Strip(job, LKerb, kerb, c, s0, s1, 0, false);
                            StampWalk(job, c, s0, s1, hw + KerbW + WalkW * 0.5);
                        }
                        else Strip(job, LUnder, verge, c, s0, s1, 0, false);
                    }
                    i = j + 1;
                }
                for (int end = 0; end < 2; end++)
                {
                    bool tw = end == 0 ? town[0] : town[town.Count - 1];
                    if (tw)
                    {
                        Disc(job, LUnder, RoadPaint.Ink.Disc(outer, WalkColour, RoadPaint.Slabs, k.Seed, 0, hw + KerbW), c, end == 1);
                        Disc(job, LKerb, RoadPaint.Ink.Disc(hw + KerbW, KerbColour, RoadPaint.Kerb, k.Seed), c, end == 1);
                    }
                    else Disc(job, LUnder, RoadPaint.Ink.Disc(hw + VergeW, VergeColour, RoadPaint.Verge, k.Seed, 0.07f), c, end == 1);
                }
            }

            // ---- the surface itself
            int kind; float soft = 0, extra = 0;
            switch (k.Surface)
            {
                case Paving: kind = RoadPaint.Cobbles; break;
                case Concrete: kind = RoadPaint.Flat; break;
                case Dirt: kind = k.Ruts ? RoadPaint.Track : RoadPaint.Earth; soft = Math.Min(0.25f, hw * 0.4f); break;
                case Gravel: kind = k.Ruts ? RoadPaint.Track : RoadPaint.Gravel; soft = Math.Min(0.18f, hw * 0.3f); extra = 1; break;
                case Steps: kind = RoadPaint.Steps; break;
                default: kind = RoadPaint.Asphalt; break;
            }
            Strip(job, fill, RoadPaint.Ink.Band(hw, k.Colour, kind, k.Seed, soft, extra), c, 0, c.Len, 0, false);
            var cap = RoadPaint.Ink.Disc(hw, k.Colour, kind == RoadPaint.Steps || kind == RoadPaint.Track ? RoadPaint.Flat : kind, k.Seed, soft, extra);
            if (!c.Head.Butt) Disc(job, fill, cap, c, false);
            if (!c.Tail.Butt) Disc(job, fill, cap, c, true);
            if (!k.Car || k.Surface != Asphalt) return;

            // ---- stop lines and zebras
            double headTrim = c.Head.Trim, tailTrim = c.Tail.Trim;
            for (int end = 0; end < 2; end++)
            {
                End e = end == 0 ? c.Head : c.Tail;
                if (!e.Stop || c.Len < e.Trim * 2 + 3) continue;
                // a zebra right at the junction already marks where to stop
                bool zebraHere = false;
                if (c.Zebras != null) foreach (double z in c.Zebras) if ((end == 0 ? z : c.Len - z) < e.Trim + 2.0) zebraHere = true;
                if (zebraHere) continue;
                double s = end == 0 ? e.Trim + 0.11 : c.Len - e.Trim - 0.11, x, y, nx, ny, m;
                int seg = 0;
                Spot(c, s, ref seg, out x, out y, out nx, out ny, out m);
                // traffic coming in to the junction keeps to its own side
                double side = k.OneWay ? 0 : (end == 1) != job.Left ? -1 : 1;
                double half = k.OneWay ? hw - 0.14 : hw * 0.5 - 0.1, o = side * hw * 0.5;
                if (half > 0.2 && Inside(job, x, y))
                    paint.Bar(fill, RoadPaint.Ink.Box(0.11f, (float)half, 0.04f, White, RoadPaint.Line, k.Seed), x + nx * o, y + ny * o, nx, ny);
                if (end == 0) headTrim += 0.4; else tailTrim += 0.4;
            }
            var centre = new List<double> { headTrim, c.Len - tailTrim };
            if (centre[1] <= centre[0]) centre.Clear();
            var sides = new List<double>(centre);
            for (int i = 0; i < c.Gaps.Count; i += 3)
            {
                Remove(sides, c.Gaps[i] - c.Gaps[i + 1], c.Gaps[i] + c.Gaps[i + 1]);
                if (c.Gaps[i + 2] > 0) Remove(centre, c.Gaps[i] - c.Gaps[i + 1], c.Gaps[i] + c.Gaps[i + 1]);
            }
            if (c.Zebras != null)
                foreach (double z in c.Zebras)
                {
                    int seg = 0; double x, y, nx, ny, m;
                    Spot(c, z, ref seg, out x, out y, out nx, out ny, out m);
                    Remove(sides, z - 0.75, z + 0.75); Remove(centre, z - 0.75, z + 0.75);
                    if (!Inside(job, x, y)) continue;
                    double dx = -ny, dy = nx;                       // along the street
                    int bars = Math.Max(2, (int)Math.Floor((hw * 2 - 0.2) / 0.46));
                    var bar = RoadPaint.Ink.Box(0.12f, 0.5f, 0.05f, White, RoadPaint.Line, k.Seed);
                    for (int b = 0; b < bars; b++)
                    {
                        double o = (b - (bars - 1) * 0.5) * 0.46;
                        paint.Bar(fill, bar, x + nx * o, y + ny * o, dx, dy);
                    }
                }

            // ---- painted lines
            if (k.Centre == None && !k.LaneLines && !k.EdgeLines) return;
            var edges = new List<double>();
            for (int i = 0; i + 1 < c.N; i++)
            {
                bool a = Inside(job, c.X[i], c.Y[i]), b = Inside(job, c.X[i + 1], c.Y[i + 1]);
                if (a == b) continue;
                double t0 = 0, t1 = 1, dx = c.X[i + 1] - c.X[i], dy = c.Y[i + 1] - c.Y[i];
                TileBuilder.ClipT(-dx, c.X[i], ref t0, ref t1); TileBuilder.ClipT(dx, job.S - c.X[i], ref t0, ref t1);
                TileBuilder.ClipT(-dy, c.Y[i], ref t0, ref t1); TileBuilder.ClipT(dy, job.S - c.Y[i], ref t0, ref t1);
                edges.Add(c.S[i] + (c.S[i + 1] - c.S[i]) * (a ? t1 : t0));
            }
            centre = InTile(job, c, centre, edges);
            sides = InTile(job, c, sides, edges);
            double period = k.LongDash ? LongPeriod : DashPeriod, on = k.LongDash ? LongOn : DashOn;
            uint mid = k.YellowCentre ? Yellow : White;
            if (k.Centre == Dashed) Dashes(job, fill, c, centre, 0, LineHalf, mid, period, on);
            else if (k.Centre == Solid) FullLine(job, fill, c, centre, 0, LineHalf, mid);
            else if (k.Centre == Twin) { FullLine(job, fill, c, centre, 0.13, ThinHalf, mid); FullLine(job, fill, c, centre, -0.13, ThinHalf, mid); }
            double inner = hw - (k.EdgeLines ? 0.2 : 0.05);
            if (k.LaneLines && k.Lanes > 1)
            {
                double lane = inner * 2 / k.Lanes;
                for (int q = 1; q < k.Lanes; q++)
                {
                    double o = -inner + q * lane;
                    if (!k.OneWay && Math.Abs(o) < 0.05) continue;      // the middle is the centre line
                    Dashes(job, fill, c, centre, o, ThinHalf, White, period, on);
                }
            }
            if (k.EdgeLines)
            {
                FullLine(job, fill, c, sides, hw - 0.16, ThinHalf, White);
                FullLine(job, fill, c, sides, -(hw - 0.16), ThinHalf, White);
            }
        }

        /// <summary>
        /// Round off the inside corners where streets meet, as real kerbs are: a wedge of asphalt fills the corner
        /// out to a curve, and in town the kerb follows the same curve.
        /// </summary>
        static void Corners(Job job, Node nd)
        {
            List<Leg> g = nd.Legs;
            if (g.Count < 2 || !Inside(job, nd.X, nd.Y)) return;
            int cars = 0;
            foreach (Leg l in g) if (l.Car && !l.Bridge) cars++;
            if (cars < 2) return;
            g.Sort((a, b) => Math.Atan2(a.Dy, a.Dx).CompareTo(Math.Atan2(b.Dy, b.Dx)));
            bool town = Town(job, nd.X, nd.Y);
            for (int i = 0; i < g.Count; i++)
            {
                Leg a = g[i], b = g[(i + 1) % g.Count];
                if (a == b || !a.Car || !b.Car || a.Bridge || b.Bridge || a.Bent || b.Bent || a.Len < 3 || b.Len < 3) continue;
                Spec ka = a.R.Spec, kb = b.R.Spec;
                if (ka.Surface == Dirt || ka.Surface == Gravel || kb.Surface == Dirt || kb.Surface == Gravel) continue;
                double cross = a.Dx * b.Dy - a.Dy * b.Dx, dot = a.Dx * b.Dx + a.Dy * b.Dy;
                double th = Math.Atan2(cross, dot);                    // from a round to b, the way they were sorted
                if (th < 0.95 || th > 2.5) continue;                    // a real corner, not a fork or a straight run
                double tan = Math.Tan(th * 0.5);
                double radius = Math.Min(1.7, Math.Max(0.7, 0.6 * Math.Min(a.Hw, b.Hw) + 0.55));
                double room = Math.Min(a.Len, b.Len) * 0.45;
                // each street's edge on the side that faces the other street
                double nax = -a.Dy, nay = a.Dx, nbx = b.Dy, nby = -b.Dx;
                Leg top = a.Rank <= b.Rank ? a : b;         // the side street is the one that widens out to meet the other
                int fill = LFill + Math.Min(7, Math.Max(0, Math.Max(a.R.Style.Layer, b.R.Style.Layer)));
                int kind = top.R.Spec.Surface == Paving ? RoadPaint.Cobbles : RoadPaint.Asphalt;
                // the curve is sized once, on the asphalt; the kerb follows the same curve a little further out
                {
                    double t0 = ((nbx * b.Hw - nax * a.Hw) * b.Dy - (nby * b.Hw - nay * a.Hw) * b.Dx) / cross;
                    if (radius / tan > room - t0) radius = (room - t0) * tan;
                }
                if (radius < 0.2) continue;
                double bx = a.Dx + b.Dx, by = a.Dy + b.Dy, bl = Math.Sqrt(bx * bx + by * by);
                for (int pass = 0; pass < 2; pass++)
                {
                    // pass 0: the asphalt; pass 1: the kerb round it
                    if (pass == 1 && (!town || ka.Rank < 2 || kb.Rank < 2 || ka.Rank > 5 || kb.Rank > 5 || ka.Link || kb.Link)) break;
                    double grow = pass == 0 ? 0 : KerbW, r = radius - grow;
                    if (r < 0.08) break;
                    double ha = a.Hw + grow, hb = b.Hw + grow;
                    double t = ((nbx * hb - nax * ha) * b.Dy - (nby * hb - nay * ha) * b.Dx) / cross;
                    double cx = nd.X + nax * ha + a.Dx * t, cy = nd.Y + nay * ha + a.Dy * t;
                    double reach = r / tan;
                    double k = r / Math.Sin(th * 0.5) / bl;
                    double ox = cx + bx * k, oy = cy + by * k;        // the middle of the curve
                    var ink = new RoadPaint.Ink { Hx = (float)r, Hy = (float)r, Round = -(float)r, Colour = pass == 0 ? top.R.Spec.Colour : KerbColour, Kind = pass == 0 ? kind : RoadPaint.Kerb, Seed = top.R.Spec.Seed };
                    double p1x = cx + a.Dx * reach, p1y = cy + a.Dy * reach, p2x = cx + b.Dx * reach, p2y = cy + b.Dy * reach;
                    // start a little inside the streets, so the wedge laps under their edges
                    double sx = cx - bx / bl * 0.1, sy = cy - by / bl * 0.1;
                    job.Paint.Tri(pass == 0 ? fill : LKerb, ink, sx, sy, (float)(sx - ox), (float)(sy - oy), p1x, p1y, (float)(p1x - ox), (float)(p1y - oy), p2x, p2y, (float)(p2x - ox), (float)(p2y - oy));
                }
            }
        }

        /// <summary>Tidy a row of yes/no along a run: fill in (or with fill false, wipe out) stretches shorter than min metres.</summary>
        static void Runs(List<bool> on, List<double> at, double min, bool fill)
        {
            int n = on.Count;
            for (int i = 0; i < n; )
            {
                int j = i;
                while (j + 1 < n && on[j + 1] == on[i]) j++;
                bool target = !fill;                 // filling looks at the gaps (false), wiping at the bits (true)
                if (on[i] == target && i > 0 && j < n - 1 && at[j] - at[i] < min)
                    for (int q = i; q <= j; q++) on[q] = !target;
                // a short bit at either end of the run goes too
                else if (!fill && on[i] && (i == 0 || j == n - 1) && at[j] - at[i] < min * 0.5 && !(i == 0 && j == n - 1))
                    for (int q = i; q <= j; q++) on[q] = false;
                i = j + 1;
            }
        }

        /// <summary>Mark the ground under a pavement as paved, so no grass grows through it.</summary>
        static void StampWalk(Job job, Chain c, double s0, double s1, double off)
        {
            TileResult t = job.T;
            if (t.Kinds == null) return;
            int N = t.TexSize, seg = 0;
            int rp = Math.Max(1, (int)Math.Ceiling(WalkW * 0.6 / job.S * N));
            for (double s = s0; s <= s1; s += 0.6)
            {
                double x, y, nx, ny, m;
                Spot(c, s, ref seg, out x, out y, out nx, out ny, out m);
                for (int side = -1; side <= 1; side += 2)
                {
                    int cx = (int)((x + nx * off * side) / job.S * N), cy = (int)((y + ny * off * side) / job.S * N);
                    for (int j = cy - rp; j <= cy + rp; j++)
                        for (int i = cx - rp; i <= cx + rp; i++)
                            if (i >= 0 && j >= 0 && i < N && j < N) t.Kinds[j * N + i] = GroundKind.Paved;
                }
            }
        }

        // ------------------------------------------------------------ bridge decks

        /// <summary>
        /// The road on top of a bridge deck (Bridges gives the deck's left and right top edges, point by point):
        /// the same surface and lines as the road that leads on to it, laid a hair above the deck.
        /// </summary>
        internal static void DeckTop(TileResult t, TileBuilder.RoadItem r, List<float> left, List<float> right)
        {
            if (r == null || r.Spec == null || r.Spec.Crosswalk || t.Roads == null) return;
            int n = left.Count / 3;
            if (n < 2) return;
            Spec k = r.Spec;
            float hw = r.Style.RealWidth * TileBuilder.RoadScale * 0.5f;
            var d = new Deck { L = left, R = right, N = n, S = new double[n], Paint = new RoadPaint(t, 3), Hw = hw };
            for (int i = 1; i < n; i++)
            {
                double cx = (left[i * 3] + right[i * 3]) * 0.5 - (left[i * 3 - 3] + right[i * 3 - 3]) * 0.5, cz = (left[i * 3 + 2] + right[i * 3 + 2]) * 0.5 - (left[i * 3 - 1] + right[i * 3 - 1]) * 0.5;
                d.S[i] = d.S[i - 1] + Math.Sqrt(cx * cx + cz * cz);
            }
            double len = d.S[n - 1];
            if (k.Surface == Rail)
            {
                if (!k.Tram) d.Strip(0, RoadPaint.Ink.Band(hw, BallastColour, RoadPaint.Sleepers, k.Seed), 0, len, 0, false);
                float gauge = k.Tram ? 0.24f : 0.3f;
                var steel = RoadPaint.Ink.Band(0.04f, SteelColour, RoadPaint.Steel, k.Seed);
                d.Strip(1, steel, 0, len, gauge, false); d.Strip(1, steel, 0, len, -gauge, false);
                d.Paint.Flush(t.Roads);
                return;
            }
            // a deck is always a made surface, whatever the track leading to it is
            bool rough = k.Surface == Dirt || k.Surface == Gravel || k.Surface == Steps;
            int kind = k.Surface == Paving ? RoadPaint.Cobbles : rough || k.Surface == Concrete ? RoadPaint.Flat : RoadPaint.Asphalt;
            d.Strip(0, RoadPaint.Ink.Band(hw, rough ? ConcreteColour : k.Colour, kind, k.Seed), 0, len, 0, false);
            if (k.Car && k.Surface == Asphalt && len > 2)
            {
                double period = k.LongDash ? LongPeriod : DashPeriod, on = k.LongDash ? LongOn : DashOn;
                uint mid = k.YellowCentre ? Yellow : White;
                if (k.Centre == Dashed) d.Dashes(0, LineHalf, mid, period, on);
                else if (k.Centre == Solid) d.Strip(2, RoadPaint.Ink.Band(LineHalf, mid, RoadPaint.Line, k.Seed), 0, len, 0, false);
                else if (k.Centre == Twin)
                {
                    var line = RoadPaint.Ink.Band(ThinHalf, mid, RoadPaint.Line, k.Seed);
                    d.Strip(2, line, 0, len, 0.13, false); d.Strip(2, line, 0, len, -0.13, false);
                }
                double inner = hw - (k.EdgeLines ? 0.2 : 0.05);
                if (k.LaneLines && k.Lanes > 1)
                {
                    double lane = inner * 2 / k.Lanes;
                    for (int q = 1; q < k.Lanes; q++)
                    {
                        double o = -inner + q * lane;
                        if (!k.OneWay && Math.Abs(o) < 0.05) continue;
                        d.Dashes(o, ThinHalf, White, period, on);
                    }
                }
                if (k.EdgeLines)
                {
                    var line = RoadPaint.Ink.Band(ThinHalf, White, RoadPaint.Line, k.Seed);
                    d.Strip(2, line, 0, len, hw - 0.16, false); d.Strip(2, line, 0, len, -(hw - 0.16), false);
                }
            }
            d.Paint.Flush(t.Roads);
        }

        sealed class Deck
        {
            public List<float> L, R; public int N; public double[] S; public RoadPaint Paint; public float Hw;
            const float Up = 0.025f;

            /// <summary>The point a distance s along the deck and 'off' metres left of its middle (never past its edge).</summary>
            float[] At(double s, double off, ref int seg)
            {
                while (seg > 0 && S[seg] > s) seg--;
                while (seg < N - 2 && S[seg + 1] < s) seg++;
                double l = S[seg + 1] - S[seg], f = l > 0 ? Math.Max(0, Math.Min(1, (s - S[seg]) / l)) : 0;
                int a = seg * 3, b = a + 3;
                double lx = L[a] + (L[b] - L[a]) * f, ly = L[a + 1] + (L[b + 1] - L[a + 1]) * f, lz = L[a + 2] + (L[b + 2] - L[a + 2]) * f;
                double rx = R[a] + (R[b] - R[a]) * f, ry = R[a + 1] + (R[b + 1] - R[a + 1]) * f, rz = R[a + 2] + (R[b + 2] - R[a + 2]) * f;
                double half = Math.Sqrt((lx - rx) * (lx - rx) + (lz - rz) * (lz - rz)) * 0.5;
                // the deck may be narrower than the road (drawn in towards a landmark bridge): squeeze, never overhang
                double g = half > 1e-6 ? 0.5 + 0.5 * Math.Max(-1, Math.Min(1, off / Math.Max(Hw, 1e-3))) : 0.5;      // 1 = left edge
                return new[] { (float)(rx + (lx - rx) * g), (float)(ry + (ly - ry) * g) + Up, (float)(rz + (lz - rz) * g) };
            }

            public void Strip(int layer, RoadPaint.Ink ink, double s0, double s1, double off, bool own)
            {
                if (s1 - s0 < 1e-4) return;
                double mid = (s0 + s1) * 0.5;
                if (own) ink.Hy = (float)((s1 - s0) * 0.5);
                float hx = ink.Hx;
                int seg = 0;
                float[] pl = null, pr = null; double ps = 0;
                double s = s0;
                while (true)
                {
                    float[] l = At(s, off + hx, ref seg), r = At(s, off - hx, ref seg);
                    if (pl != null)
                    {
                        float b0 = (float)(own ? ps - mid : ps), b1 = (float)(own ? s - mid : s);
                        Paint.QuadAt(layer, ink, pl, hx, b0, l, hx, b1, r, -hx, b1, pr, -hx, b0);
                    }
                    pl = l; pr = r; ps = s;
                    if (s >= s1) break;
                    int nextV = seg + 1;
                    while (nextV < N - 1 && S[nextV] <= s + 1e-9) nextV++;
                    double next = Math.Min(s1, S[nextV]);
                    if (next <= s + 1e-9) next = s1;
                    s = next;
                }
            }

            public void Dashes(double off, float half, uint colour, double period, double on)
            {
                double len = S[N - 1];
                int n = Math.Max(1, (int)Math.Round(len / period));
                double p = len / n, d = Math.Min(on * p / period, p * 0.6);
                var ink = RoadPaint.Ink.Box(half, 0, half * 0.7f, colour, RoadPaint.Line);
                for (int k = 0; k < n; k++)
                {
                    double mid = (k + 0.5) * p;
                    Strip(2, ink, mid - d * 0.5, mid + d * 0.5, off, true);
                }
            }
        }
    }
}
