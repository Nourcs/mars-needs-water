using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// How roads look up close, and the meshes that show it. Each road gets a kind of surface (asphalt, paving,
    /// gravel, dirt, rail ...), a width and lane count from its class, one-way flag and the map's own width,
    /// and painted lines that follow the local rules (yellow centre lines in the Americas, white elsewhere).
    /// Lines stop short of junctions, minor roads get a stop line where they meet a bigger road, crossings drawn
    /// in the map become zebras, and town streets get kerbs and pavements on the sides where the map has them.
    /// The surface itself is drawn by the EarthRoad shader from what each vertex carries:
    ///   uv0  x: metres across from the centre line (left of the way it was drawn is positive), y: metres along
    ///   uv1  x: half-width of the carriageway, y: metres clear of the nearest junction, z: kind, w: line flags
    ///   uv2  x: lanes, y: lane width, z: random seed, w: side with a stop line (+1 left, -1 right, 0 none)
    /// Everything is worked out from the map data of the tile itself, so each tile agrees with its neighbours.
    /// </summary>
    public static class RoadLook
    {
        // surface kinds (EarthRoad.shader has the same numbers)
        public const int Asphalt = 0, Pavers = 1, Concrete = 2, Dirt = 3, Gravel = 4, Steps = 5, Cycle = 6, Rail = 7, Zebra = 8, Sidewalk = 9, KerbFace = 10;

        // line flags
        public const int EdgeLines = 1, CentreDashed = 2, CentreSolid = 4, CentreDouble = 8, Yellow = 16, OneWay = 32, YellowLeft = 64, Ruts = 128, LongDash = 256, Lanes = 512, Red = 1024;

        const float KerbHeight = 0.075f;     // 15 cm real, at the height scale of buildings (Geo.Height)
        const float SidewalkReal = 2.2f;     // real metres of pavement beside a street
        const float StepAlong = 3.0f;        // game metres between cross-sections (the ground has a point every 6 m or so)

        public sealed class Look
        {
            public int Kind, Flags, Lanes = 2, Rank, Layer;
            public float LaneReal = 3.5f;
            public bool Car, Crosswalk, Sidewalk, SideL, SideR;
            public uint Seed;
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
        public static bool YellowCentre(double lat, double lon) { return lon < -30 && lon > -170; }

        /// <summary>Countries that drive on the left (coarse boxes; good enough for which half gets the stop line).</summary>
        public static bool LeftHand(double lat, double lon)
        {
            if (lat > 49.8 && lat < 61 && lon > -11 && lon < 2) return true;                // Britain and Ireland
            if (lat > 24 && lat < 46 && lon > 128 && lon < 146) return true;                // Japan
            if (lat > -45 && lat < -10 && lon > 112 && lon < 154) return true;              // Australia
            if (lat > -48 && lat < -34 && lon > 165 && lon < 179) return true;              // New Zealand
            if (lat > 6 && lat < 31 && lon > 68 && lon < 89) return true;                   // India, Sri Lanka, Bangladesh, Nepal
            if (lat > -35 && lat < -15 && lon > 12 && lon < 41) return true;                // southern Africa
            if (lat > -11 && lat < 7 && lon > 95 && lon < 141) return true;                 // Indonesia, Malaysia, Singapore
            if (lat > 5 && lat < 21 && lon > 97 && lon < 106) return true;                  // Thailand
            if (lat > 21.9 && lat < 22.6 && lon > 113.8 && lon < 114.5) return true;        // Hong Kong, Macau
            if (lat > -5 && lat < 5 && lon > 33 && lon < 42) return true;                   // Kenya, Uganda
            return false;
        }

        static string Value(string rules)
        {
            // [{"value":"paved"}] -> paved (the first rule; ones that only cover part of the road count too)
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

        /// <summary>
        /// Called by TileBuilder.CollectRoads for every tile (all zooms, and the neighbours Bridges reads), so the
        /// width a road gets here is the same everywhere it is used. Drops the map's separate pavement lines (each
        /// street gets its own pavement instead) and stretches crossings to span the widened streets.
        /// </summary>
        internal static void Prepare(List<TileBuilder.RoadItem> list, MvtLayer L, int tz, int tx, int ty)
        {
            double lat = Geo.TileLat(ty + 0.5, tz), lon = Geo.TileLon(tx + 0.5, tz);
            bool yellow = YellowCentre(lat, lon);
            int kSub = L.Key("subtype"), kCls = L.Key("class"), kSc = L.Key("subclass"), kSurf = L.Key("road_surface"),
                kAcc = L.Key("access_restrictions"), kWid = L.Key("width_rules"), kFlags = L.Key("road_flags"), kId = L.Key("id");
            double unit = Geo.TileSize(tz, lat) * Geo.Scale / L.Extent;      // real metres per tile unit

            // the map's pavements, to know which side of which street has one
            var walks = new Dictionary<long, List<int[]>>();
            const double Cell = 40.0;           // real metres
            int walkCount = 0;
            foreach (TileBuilder.RoadItem r in list)
            {
                string sc = L.Str(r.F, kSc);
                if (sc != "sidewalk") continue;
                walkCount++;
                foreach (int[] p in r.F.Parts)
                    for (int i = 0; i + 3 < p.Length; i += 2)
                    {
                        long cx = (long)Math.Floor((p[i] + p[i + 2]) * 0.5 * unit / Cell), cy = (long)Math.Floor((p[i + 1] + p[i + 3]) * 0.5 * unit / Cell);
                        long key = cx * 100003L + cy;
                        List<int[]> c;
                        if (!walks.TryGetValue(key, out c)) walks[key] = c = new List<int[]>();
                        c.Add(new[] { p[i], p[i + 1], p[i + 2], p[i + 3] });
                    }
            }

            for (int n = list.Count - 1; n >= 0; n--)
            {
                TileBuilder.RoadItem r = list[n];
                string sub = L.Str(r.F, kSub), cls = L.Str(r.F, kCls) ?? "unknown", sc = L.Str(r.F, kSc);
                var look = new Look { Rank = Rank(cls), Layer = r.Style.Layer };
                string id = L.Str(r.F, kId);
                uint h = 2166136261; if (id != null) foreach (char ch in id) { h ^= ch; h *= 16777619; }
                look.Seed = h;
                r.Look = look;
                if (sub == "rail") { look.Kind = Rail; continue; }
                if (sc == "sidewalk") { list.RemoveAt(n); continue; }       // drawn as each street's own pavement
                string surf = Value(L.Str(r.F, kSurf));
                bool unpaved = surf == "unpaved" || surf == "dirt" || surf == "ground" || surf == "earth" || surf == "grass" || surf == "mud" || surf == "sand";
                bool gravel = surf == "gravel" || surf == "fine_gravel" || surf == "compacted" || surf == "pebblestone";
                bool stones = surf == "paving_stones" || surf == "sett" || surf == "cobblestone" || surf == "unhewn_cobblestone" || surf == "bricks";
                string flags = L.Str(r.F, kFlags);
                bool link = sc == "link" || (flags != null && flags.Contains("is_link"));
                string acc = L.Str(r.F, kAcc);
                bool oneway = acc != null && acc.Contains("\"heading\":\"backward\"") && acc.Contains("denied");

                if (sc == "crosswalk")
                {
                    look.Kind = Zebra; look.Crosswalk = true; look.Layer = 9;
                    // a crossing runs kerb to kerb; streets are drawn 1.5 times their true width, so it must grow too
                    for (int k = 0; k < r.Plain.Count; k++) r.Plain[k] = Stretch(r.Plain[k], 1.6);
                    continue;
                }
                switch (cls)
                {
                    case "footway": case "pedestrian": case "living_street" when stones:
                        look.Kind = stones ? Pavers : unpaved ? Dirt : gravel ? Gravel : Concrete; break;
                    case "steps": look.Kind = Steps; break;
                    case "cycleway":
                        look.Kind = unpaved ? Dirt : gravel ? Gravel : Cycle;
                        if (lat > 35 && lat < 72 && lon > -11 && lon < 32) look.Flags |= Red;     // Europe paints its cycle paths red
                        break;
                    case "path": case "bridleway":
                        look.Kind = surf == "paved" || surf == "asphalt" || surf == "concrete" ? Concrete : stones ? Pavers : gravel ? Gravel : Dirt; break;
                    case "track":
                        look.Kind = surf == "paved" || surf == "asphalt" || surf == "concrete" ? Asphalt : gravel ? Gravel : Dirt;
                        if (look.Kind != Asphalt) look.Flags |= Ruts;
                        break;
                    default:
                        look.Car = true;
                        look.Kind = unpaved ? Dirt : gravel ? Gravel : stones ? Pavers : Asphalt;
                        if (look.Kind == Dirt || look.Kind == Gravel) look.Flags |= Ruts;
                        break;
                }
                if (!look.Car) continue;

                // width and lanes: the map's width when it gives one, else from the class
                float real = r.Style.RealWidth;
                int lanes = 2;
                switch (cls)
                {
                    case "motorway": lanes = oneway ? 3 : 4; real = oneway ? 13f : 22f; look.Flags |= EdgeLines | LongDash; break;
                    case "trunk": lanes = 2; real = oneway ? 11.5f : 12f; look.Flags |= EdgeLines | LongDash; break;
                    case "primary": lanes = oneway ? 2 : 4; real = oneway ? 8.5f : 14f; look.Flags |= EdgeLines; break;
                    case "secondary": lanes = 2; real = oneway ? 8f : 10.5f; look.Flags |= EdgeLines; break;
                    case "tertiary": lanes = 2; real = oneway ? 7.5f : 9f; break;
                    case "residential": case "unclassified": lanes = oneway ? 1 : 2; real = oneway ? 5.5f : 7.5f; break;
                    case "living_street": lanes = 1; real = 6f; break;
                    case "service": lanes = 1; real = sc == "parking_aisle" ? 6f : sc == "driveway" || sc == "alley" ? 3.5f : 4.5f; break;
                    default: lanes = 1; break;
                }
                if (link) { lanes = 1; real = 6f; look.Flags |= EdgeLines; oneway = true; }
                float given;
                string ws = Value(L.Str(r.F, kWid));
                if (ws != null && float.TryParse(ws, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out given) && given >= 2.5f && given <= 40f)
                {
                    real = given;
                    if (look.Rank >= 3) lanes = Math.Max(oneway ? 1 : 2, (int)Math.Floor(given / 3.3));
                    if (!oneway && lanes % 2 == 1) lanes = Math.Max(2, lanes - 1);
                }
                look.Lanes = Math.Max(1, lanes);
                look.LaneReal = Math.Min(3.6f, (real - 0.8f) / look.Lanes);
                if (oneway) look.Flags |= OneWay;
                if (look.Kind == Asphalt)
                {
                    if (oneway)
                    {
                        if (look.Lanes > 1) look.Flags |= Lanes;
                        if (yellow && (look.Flags & EdgeLines) != 0 && look.Rank >= 5) look.Flags |= YellowLeft;
                    }
                    else if (look.Rank >= 3 || (look.Rank == 2 && real >= 7f && cls == "unclassified"))
                    {
                        if (look.Rank >= 4 && yellow) look.Flags |= CentreDouble | CentreSolid;
                        else if (look.Rank >= 6) look.Flags |= CentreSolid;
                        else look.Flags |= CentreDashed;
                        if (yellow) look.Flags |= Yellow;
                        if (look.Lanes > 2) look.Flags |= Lanes;
                    }
                }
                r.Style.RealWidth = real;

                // a pavement on each side the map draws one along (when the map has pavements at all here)
                if (walkCount >= 8 && look.Rank >= 1 && look.Rank <= 5 && !link)
                {
                    double reach = real * 0.5 + 9.0;
                    int left = 0, right = 0, tests = 0;
                    foreach (int[] p in r.F.Parts)
                        for (int i = 0; i + 3 < p.Length; i += 2)
                        {
                            double ax = p[i], ay = p[i + 1], bx = p[i + 2], by = p[i + 3];
                            double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) * unit;
                            if (len < 0.5) continue;
                            int steps = Math.Max(1, (int)(len / 10));
                            for (int s = 0; s < steps; s++)
                            {
                                double f = (s + 0.5) / steps, px = ax + (bx - ax) * f, py = ay + (by - ay) * f;
                                double dx = (bx - ax) * unit / len, dy = (by - ay) * unit / len;     // unit direction (east, south)
                                tests++;
                                int side = SideOfWalk(walks, px * unit, py * unit, dx, dy, real * 0.5, reach, unit, Cell);
                                if ((side & 1) != 0) left++;
                                if ((side & 2) != 0) right++;
                            }
                        }
                    if (tests > 0)
                    {
                        look.SideL = left >= tests * 0.35;
                        look.SideR = right >= tests * 0.35;
                    }
                }
            }
        }

        /// <summary>Real width a road takes up on the ground picture: the carriageway and the pavements beside it.</summary>
        internal static float PaintWidth(TileBuilder.RoadItem r)
        {
            float w = r.Style.RealWidth;
            if (r.Look != null) w += ((r.Look.SideL ? 1 : 0) + (r.Look.SideR ? 1 : 0)) * SidewalkReal;
            return w;
        }

        /// <summary>Which sides of a street point (real metres) have a mapped pavement running alongside: bit 1 left, bit 2 right.</summary>
        static int SideOfWalk(Dictionary<long, List<int[]>> walks, double x, double y, double dx, double dy, double inner, double reach, double unit, double cell)
        {
            int res = 0;
            long cx = (long)Math.Floor(x / cell), cy = (long)Math.Floor(y / cell);
            for (long j = cy - 1; j <= cy + 1; j++)
                for (long i = cx - 1; i <= cx + 1; i++)
                {
                    List<int[]> c;
                    if (!walks.TryGetValue(i * 100003L + j, out c)) continue;
                    foreach (int[] s in c)
                    {
                        double ax = s[0] * unit, ay = s[1] * unit, bx = s[2] * unit, by = s[3] * unit;
                        double ex = bx - ax, ey = by - ay, l2 = ex * ex + ey * ey;
                        if (l2 < 1e-6) continue;
                        double f = Math.Max(0, Math.Min(1, ((x - ax) * ex + (y - ay) * ey) / l2));
                        double qx = ax + ex * f - x, qy = ay + ey * f - y;
                        double d = Math.Sqrt(qx * qx + qy * qy);
                        if (d < inner - 1.0 || d > reach) continue;
                        // running alongside, not across
                        double l = Math.Sqrt(l2), along = Math.Abs((ex * dx + ey * dy) / l);
                        if (along < 0.8) continue;
                        // left of the way (east, south coordinates): (dy, -dx)
                        double side = qx * dy - qy * dx;
                        res |= side > 0 ? 1 : 2;
                    }
                }
            return res;
        }

        static int[] Stretch(int[] p, double k)
        {
            int n = p.Length / 2;
            if (n < 2) return p;
            double cx = 0, cy = 0;
            for (int i = 0; i < n; i++) { cx += p[i * 2]; cy += p[i * 2 + 1]; }
            cx /= n; cy /= n;
            var q = new int[p.Length];
            for (int i = 0; i < n; i++)
            {
                q[i * 2] = (int)Math.Round(cx + (p[i * 2] - cx) * k);
                q[i * 2 + 1] = (int)Math.Round(cy + (p[i * 2 + 1] - cy) * k);
            }
            return q;
        }

        // ------------------------------------------------------------ meshes

        sealed class Junction { public double X, Y; public float R; public int MaxRank; }

        sealed class Seg { public double Ax, Ay, Bx, By; public float Hw; public TileBuilder.RoadItem R; }

        sealed class Ctx
        {
            public TileResult T;
            public double S;                                   // tile size, game metres
            public Dictionary<long, List<Junction>> Junctions = new Dictionary<long, List<Junction>>();
            public Dictionary<long, List<Seg>> Segs = new Dictionary<long, List<Seg>>();
            public bool Urban;                                 // the tile has no mapped pavements: build them where it is built up
            public int[] Density;                              // roof points per 16 x 16 cell
            public bool LeftHand;
            public const double JCell = 8.0, SCell = 8.0;
        }

        static long Key(long i, long j) { return i * 1000003L + j; }

        /// <summary>Builds t.Roads: every road, rail line, pavement and crossing of a zoom-14 tile.</summary>
        internal static void Build(TileResult t, List<TileBuilder.RoadItem> roads, MvtLayer L)
        {
            t.Roads = new MeshData();
            if (L == null) return;
            double e = L.Extent, S = t.Size;
            var c = new Ctx { T = t, S = S, LeftHand = LeftHand(t.Lat, t.Lon) };

            // junctions: where three or more road ends meet at one map point (a line passing through counts twice)
            var ends = new Dictionary<long, (int count, float hw, int maxRank, int x, int y)>();
            bool anyWalk = false;
            foreach (TileBuilder.RoadItem r in roads)
            {
                if (r.Look == null || !r.Look.Car) continue;
                if (r.Look.SideL || r.Look.SideR) anyWalk = true;
                float hw = r.Style.RealWidth * TileBuilder.RoadScale * 0.5f;
                foreach (List<int[]> set in new[] { r.Plain, r.Bridge })
                    foreach (int[] p in set)
                    {
                        int n = p.Length / 2;
                        for (int i = 0; i < n; i++)
                        {
                            long k = ((long)p[i * 2] << 32) ^ (uint)p[i * 2 + 1];
                            (int count, float hw, int maxRank, int x, int y) v;
                            ends.TryGetValue(k, out v);
                            v.count += (i == 0 || i == n - 1) ? 1 : 2;
                            v.hw = Math.Max(v.hw, hw); v.maxRank = Math.Max(v.maxRank, r.Look.Rank); v.x = p[i * 2]; v.y = p[i * 2 + 1];
                            ends[k] = v;
                        }
                        // centre lines in a grid, to keep pavements off other streets
                        for (int i = 0; i + 1 < n; i++)
                        {
                            var sg = new Seg { Ax = p[i * 2] / e * S, Ay = p[i * 2 + 1] / e * S, Bx = p[i * 2 + 2] / e * S, By = p[i * 2 + 3] / e * S, Hw = hw, R = r };
                            long i0 = (long)Math.Floor(Math.Min(sg.Ax, sg.Bx) / Ctx.SCell), i1 = (long)Math.Floor(Math.Max(sg.Ax, sg.Bx) / Ctx.SCell);
                            long j0 = (long)Math.Floor(Math.Min(sg.Ay, sg.By) / Ctx.SCell), j1 = (long)Math.Floor(Math.Max(sg.Ay, sg.By) / Ctx.SCell);
                            if ((i1 - i0 + 1) * (j1 - j0 + 1) > 400) continue;
                            for (long jj = j0; jj <= j1; jj++)
                                for (long ii = i0; ii <= i1; ii++)
                                {
                                    List<Seg> cell;
                                    if (!c.Segs.TryGetValue(Key(ii, jj), out cell)) c.Segs[Key(ii, jj)] = cell = new List<Seg>();
                                    cell.Add(sg);
                                }
                        }
                    }
            }
            foreach (var kv in ends)
            {
                if (kv.Value.count < 3) continue;
                var j = new Junction { X = kv.Value.x / e * S, Y = kv.Value.y / e * S, R = kv.Value.hw * 1.05f, MaxRank = kv.Value.maxRank };
                long ci = (long)Math.Floor(j.X / Ctx.JCell), cj = (long)Math.Floor(j.Y / Ctx.JCell);
                List<Junction> cell;
                if (!c.Junctions.TryGetValue(Key(ci, cj), out cell)) c.Junctions[Key(ci, cj)] = cell = new List<Junction>();
                cell.Add(j);
            }

            // no pavements in the map here: give streets one where the tile is built up (by the roofs already made)
            if (!anyWalk && t.Roofs != null && t.Roofs.Count > 0)
            {
                c.Urban = true;
                c.Density = new int[16 * 16];
                var P = t.Roofs.P;
                for (int i = 0; i < P.Count; i += 3)
                {
                    int gx = (int)((P[i] / S + 0.5) * 16), gy = (int)((0.5 - P[i + 2] / S) * 16);
                    if (gx < 0 || gy < 0 || gx > 15 || gy > 15) continue;
                    c.Density[gy * 16 + gx]++;
                }
            }

            var piece = new List<double>();
            foreach (TileBuilder.RoadItem r in roads)
            {
                if (r.Look == null) continue;
                foreach (int[] part in r.Plain)
                {
                    int n = part.Length / 2;
                    piece.Clear();
                    bool clipStart = false;
                    for (int i = 0; i + 1 < n; i++)
                    {
                        double ax = part[i * 2] / e, ay = part[i * 2 + 1] / e, bx = part[i * 2 + 2] / e, by = part[i * 2 + 3] / e;
                        double t0 = 0, t1 = 1;
                        if (!TileBuilder.ClipT(-(bx - ax), ax, ref t0, ref t1) || !TileBuilder.ClipT(bx - ax, 1 - ax, ref t0, ref t1) ||
                            !TileBuilder.ClipT(-(by - ay), ay, ref t0, ref t1) || !TileBuilder.ClipT(by - ay, 1 - ay, ref t0, ref t1))
                        {
                            if (piece.Count >= 4) Strip(c, r, piece, clipStart, true);
                            piece.Clear();
                            continue;
                        }
                        double sx = ax + (bx - ax) * t0, sy = ay + (by - ay) * t0, ex = ax + (bx - ax) * t1, ey = ay + (by - ay) * t1;
                        if (piece.Count == 0 || t0 > 0)
                        {
                            if (piece.Count >= 4) Strip(c, r, piece, clipStart, true);
                            piece.Clear();
                            piece.Add(sx); piece.Add(sy);
                            clipStart = t0 > 0;
                        }
                        piece.Add(ex); piece.Add(ey);
                        if (t1 < 1) { if (piece.Count >= 4) Strip(c, r, piece, clipStart, true); piece.Clear(); }
                    }
                    if (piece.Count >= 4) Strip(c, r, piece, clipStart, false);
                }
                t.RoadCount++;
            }
        }

        static Junction NearestJunction(Ctx c, double x, double y, out double dist)
        {
            dist = double.MaxValue;
            Junction best = null;
            long ci = (long)Math.Floor(x / Ctx.JCell), cj = (long)Math.Floor(y / Ctx.JCell);
            for (long j = cj - 1; j <= cj + 1; j++)
                for (long i = ci - 1; i <= ci + 1; i++)
                {
                    List<Junction> cell;
                    if (!c.Junctions.TryGetValue(Key(i, j), out cell)) continue;
                    foreach (Junction q in cell)
                    {
                        double d = Math.Sqrt((q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y)) - q.R;
                        if (d < dist) { dist = d; best = q; }
                    }
                }
            return best;
        }

        /// <summary>Is this spot (tile metres) on the carriageway of a street other than 'self'?</summary>
        static bool OnOtherRoad(Ctx c, double x, double y, TileBuilder.RoadItem self, float margin)
        {
            List<Seg> cell;
            if (!c.Segs.TryGetValue(Key((long)Math.Floor(x / Ctx.SCell), (long)Math.Floor(y / Ctx.SCell)), out cell)) return false;
            foreach (Seg s in cell)
            {
                if (s.R == self) continue;
                double ex = s.Bx - s.Ax, ey = s.By - s.Ay, l2 = ex * ex + ey * ey;
                double f = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((x - s.Ax) * ex + (y - s.Ay) * ey) / l2));
                double qx = s.Ax + ex * f - x, qy = s.Ay + ey * f - y;
                double r = s.Hw + margin;
                if (qx * qx + qy * qy < r * r) return true;
            }
            return false;
        }

        static bool OnAnyRoad(Ctx c, double x, double y, float margin) { return OnOtherRoad(c, x, y, null, margin); }

        static bool UrbanAt(Ctx c, double x, double y)
        {
            if (c.Density == null) return false;
            int gx = (int)(x / c.S * 16), gy = (int)(y / c.S * 16);
            int sum = 0;
            for (int j = gy - 1; j <= gy + 1; j++)
                for (int i = gx - 1; i <= gx + 1; i++)
                    if (i >= 0 && j >= 0 && i < 16 && j < 16) sum += c.Density[j * 16 + i];
            return sum >= 260;      // about 25 buildings in a block of 3 x 3 cells (some 70 m across)
        }

        /// <summary>Stable world position for the dash pattern where a road leaves the tile, so it carries on in the next one.</summary>
        static double Phase(Ctx c, double x, double y, double dx, double dy)
        {
            double wx = ((c.T.X & 63) * c.S) + x, wy = ((c.T.Y & 63) * c.S) + y;
            return wx * dx + wy * dy;
        }

        static void Normal(TileResult t, double u, double v, double S, out float nx, out float ny, out float nz)
        {
            float ax, ay, az, bx, by, bz, cx, cy, cz, dx, dy, dz;
            double o = 1.5 / S;
            t.Surface(u - o, v, out ax, out ay, out az); t.Surface(u + o, v, out bx, out by, out bz);
            t.Surface(u, v - o, out cx, out cy, out cz); t.Surface(u, v + o, out dx, out dy, out dz);
            float ex = bx - ax, ey = by - ay, ez = bz - az;      // east
            float sx = cx - dx, sy = cy - dy, sz = cz - dz;      // north
            nx = sy * ez - sz * ey; ny = sz * ex - sx * ez; nz = sx * ey - sy * ex;
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (l < 1e-9f || ny <= 0) { nx = 0; ny = 1; nz = 0; return; }
            nx /= l; ny /= l; nz /= l;
        }

        static void Vert(MeshData m, float x, float y, float z, float nx, float ny, float nz, float s, float v, float hw, float dj, int kind, int flags, float lanes, float laneW, float seed, float stop)
        {
            m.Add(x, y, z, nx, ny, nz, s, v);
            m.X.Add(hw); m.X.Add(dj); m.X.Add(kind); m.X.Add(flags);
            m.Y.Add(lanes); m.Y.Add(laneW); m.Y.Add(seed); m.Y.Add(stop);
        }

        static void Extra(MeshData m)
        {
            if (m.X == null) { m.X = new List<float>(); m.Y = new List<float>(); }
        }

        /// <summary>One clipped piece of a road (tile u, v pairs): the carriageway, and its pavements and kerbs.</summary>
        static void Strip(Ctx c, TileBuilder.RoadItem r, List<double> uv, bool clipStart, bool clipEnd)
        {
            TileResult t = c.T;
            double S = c.S;
            Look k = r.Look;
            // to metres, adding points every couple of metres so the strip follows the ground
            var pts = new List<double>();
            for (int i = 0; i < uv.Count; i += 2)
            {
                double x = uv[i] * S, y = uv[i + 1] * S;
                if (pts.Count >= 2)
                {
                    double lx = pts[pts.Count - 2], ly = pts[pts.Count - 1];
                    double d = Math.Sqrt((x - lx) * (x - lx) + (y - ly) * (y - ly));
                    if (d < 0.01) continue;
                    int steps = (int)Math.Ceiling(d / StepAlong);
                    for (int s = 1; s < steps; s++) { pts.Add(lx + (x - lx) * s / steps); pts.Add(ly + (y - ly) * s / steps); }
                }
                pts.Add(x); pts.Add(y);
            }
            int n = pts.Count / 2;
            if (n < 2) return;

            MeshData m = t.Roads;
            Extra(m);
            float width = r.Style.RealWidth * TileBuilder.RoadScale;
            if (k.Kind == Rail) width = Math.Max(width, 1.6f);
            float hw = width * 0.5f;
            float lift = 0.05f + Math.Min(k.Layer, 9) * 0.012f;
            float laneW = k.LaneReal * TileBuilder.RoadScale;
            float seed = (k.Seed & 0xffff) / 65535f;

            // directions, distance along and the dash phase
            var dirs = new double[n * 2];
            var along = new double[n];
            for (int i = 0; i < n; i++)
            {
                double d0x = 0, d0y = 0, d1x = 0, d1y = 0;
                if (i > 0) { d0x = pts[i * 2] - pts[i * 2 - 2]; d0y = pts[i * 2 + 1] - pts[i * 2 - 1]; double l = Math.Sqrt(d0x * d0x + d0y * d0y); d0x /= l; d0y /= l; along[i] = along[i - 1] + l; }
                if (i < n - 1) { d1x = pts[i * 2 + 2] - pts[i * 2]; d1y = pts[i * 2 + 3] - pts[i * 2 + 1]; double l = Math.Sqrt(d1x * d1x + d1y * d1y); d1x /= l; d1y /= l; }
                if (i == 0) { d0x = d1x; d0y = d1y; }
                if (i == n - 1) { d1x = d0x; d1y = d0y; }
                double dx = d0x + d1x, dy = d0y + d1y, dl = Math.Sqrt(dx * dx + dy * dy);
                if (dl < 1e-6) { dx = d1x; dy = d1y; dl = 1; }
                dx /= dl; dy /= dl;
                double cos = Math.Max(0.5, dx * d0x + dy * d0y);
                dirs[i * 2] = dx / cos; dirs[i * 2 + 1] = dy / cos;   // mitred: longer on bends so the width stays even
            }
            double v0 = 0;
            if (clipStart) v0 = Phase(c, pts[0], pts[1], dirs[0], dirs[1]);
            else if (clipEnd) v0 = Phase(c, pts[n * 2 - 2], pts[n * 2 - 1], dirs[n * 2 - 2], dirs[n * 2 - 1]) - along[n - 1];
            v0 = v0 - Math.Floor(v0 / 64.0) * 64.0;

            // junction clearance and stop lines
            var dj = new float[n];
            var stop = new float[n];
            for (int i = 0; i < n; i++)
            {
                double d;
                Junction j = NearestJunction(c, pts[i * 2], pts[i * 2 + 1], out d);
                dj[i] = (float)Math.Max(-20, Math.Min(50, d));
                if (j != null && k.Car && j.MaxRank > k.Rank && k.Rank >= 1 && d < 3)
                {
                    // the junction lies ahead (end of the piece) or behind (start): traffic comes in on the near-side half
                    bool ahead = i > n / 2;
                    bool right = !c.LeftHand;
                    float side = (k.Flags & OneWay) != 0 ? 2f : (ahead == right ? -1f : 1f);
                    stop[i] = side;
                }
            }

            int kind = k.Kind, flags = k.Flags;
            float lanes = k.Lanes;
            if (k.Crosswalk) { Crossing(c, r, pts, dirs, n, lift, seed); return; }

            // the carriageway: several points across so it never cuts into a rounded road crown or a hill
            int across = Math.Max(2, (int)Math.Ceiling(width / 1.6) + 1);
            int prevRow = -1;
            for (int i = 0; i < n; i++)
            {
                double x = pts[i * 2], y = pts[i * 2 + 1];
                double lx = dirs[i * 2 + 1], ly = -dirs[i * 2];         // to the left of the way the line runs (east, south)
                float nx, ny, nz;
                Normal(t, x / S, y / S, S, out nx, out ny, out nz);
                int row = m.Count;
                float v = (float)(v0 + along[i]);
                for (int a = 0; a < across; a++)
                {
                    double f = (double)a / (across - 1) * 2 - 1;       // -1 right .. +1 left
                    double ox = lx * hw * f, oy = ly * hw * f;
                    float px, py, pz;
                    t.Surface((x + ox) / S, (y + oy) / S, out px, out py, out pz);
                    Vert(m, px, py + lift, pz, nx, ny, nz, (float)(hw * f), v, hw, dj[i], kind, flags, lanes, laneW, seed, stop[i]);
                }
                if (prevRow >= 0)
                    for (int a = 0; a + 1 < across; a++)
                    {
                        int p0 = prevRow + a, p1 = prevRow + a + 1, q0 = row + a, q1 = row + a + 1;
                        m.Tri(p0, p1, q0, 0, 1, 0); m.Tri(p1, q1, q0, 0, 1, 0);
                    }
                prevRow = row;
            }
            // round ends (not where the tile cuts the road), so streets meeting at a corner close up
            for (int end = 0; end < 2; end++)
            {
                if (end == 0 ? clipStart : clipEnd) continue;
                int i = end == 0 ? 0 : n - 1;
                double x = pts[i * 2], y = pts[i * 2 + 1];
                double dx = dirs[i * 2], dy = dirs[i * 2 + 1], dl = Math.Sqrt(dx * dx + dy * dy);
                dx /= dl; dy /= dl;
                if (end == 0) { dx = -dx; dy = -dy; }
                double lx = dy, ly = -dx;
                float nx, ny, nz;
                Normal(t, x / S, y / S, S, out nx, out ny, out nz);
                float cx, cy, cz;
                t.Surface(x / S, y / S, out cx, out cy, out cz);
                float v = (float)(v0 + along[i]);
                int centre = m.Count;
                Vert(m, cx, cy + lift, cz, nx, ny, nz, 0, v, hw, dj[i], kind, flags, lanes, laneW, seed, 0);
                int prev = -1;
                for (int s = 0; s <= 8; s++)
                {
                    double ang = s * Math.PI / 8;                         // from the right side round the front to the left
                    double fx = -Math.Cos(ang), fa = Math.Sin(ang);
                    double ox = lx * hw * fx + dx * hw * fa, oy = ly * hw * fx + dy * hw * fa;
                    float qx, qy, qz;
                    t.Surface((x + ox) / S, (y + oy) / S, out qx, out qy, out qz);
                    float sAcross = (float)(hw * fx * (end == 0 ? -1 : 1));
                    int q = m.Count;
                    Vert(m, qx, qy + lift, qz, nx, ny, nz, sAcross, v + (float)(hw * fa * (end == 0 ? -1 : 1)), hw, Math.Min(dj[i], 0f), kind, flags & ~(CentreDashed | CentreSolid | CentreDouble | Lanes | EdgeLines), lanes, laneW, seed, 0);
                    if (prev >= 0) m.Tri(centre, prev, q, 0, 1, 0);
                    prev = q;
                }
            }

            // pavements and kerbs
            if (!k.Car || k.Kind == Dirt || k.Kind == Gravel) return;
            bool wantL = k.SideL, wantR = k.SideR;
            if (c.Urban && k.Rank >= 1 && k.Rank <= 5) wantL = wantR = true;     // judged spot by spot below
            if (!wantL && !wantR) return;
            float sw = SidewalkReal * TileBuilder.RoadScale;
            for (int side = 0; side < 2; side++)
            {
                if (side == 0 ? !wantL : !wantR) continue;
                float sg = side == 0 ? 1f : -1f;
                var keep = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    double x = pts[i * 2], y = pts[i * 2 + 1];
                    double lx = dirs[i * 2 + 1] * sg, ly = -dirs[i * 2] * sg;
                    double mx = x + lx * (hw + sw * 0.5), my = y + ly * (hw + sw * 0.5);
                    double ix = x + lx * (hw + 0.05), iy = y + ly * (hw + 0.05);
                    bool ok = dj[i] > sw * 0.6;
                    if (ok && c.Urban && !(side == 0 ? k.SideL : k.SideR)) ok = UrbanAt(c, mx, my);
                    if (ok) ok = !OnOtherRoad(c, mx, my, r, 0.1f) && !OnOtherRoad(c, ix, iy, r, 0.05f);
                    if (ok) { byte mk = t.WMask != null ? WaterMask(t, mx / S, my / S) : (byte)0; ok = mk < 128; }
                    keep[i] = ok;
                }
                // drop short bits, they only look like litter
                int runStart = -1;
                for (int i = 0; i <= n; i++)
                {
                    bool on = i < n && keep[i];
                    if (on && runStart < 0) runStart = i;
                    if (!on && runStart >= 0)
                    {
                        if (along[i - 1] - along[runStart] >= 3.0) Pavement(c, pts, dirs, along, runStart, i - 1, sg, hw, sw, lift, v0, seed);
                        runStart = -1;
                    }
                }
            }
        }

        static byte WaterMask(TileResult t, double u, double v)
        {
            int px = (int)(u * t.MaskN), py = (int)(v * t.MaskN);
            if (px < 0 || py < 0 || px >= t.MaskN || py >= t.MaskN) return 0;
            return t.WMask[py * t.MaskN + px];
        }

        /// <summary>A raised pavement along one side (sg +1 left, -1 right) from point a to point b, with its kerb.</summary>
        static void Pavement(Ctx c, List<double> pts, double[] dirs, double[] along, int a, int b, float sg, float hw, float sw, float lift, double v0, float seed)
        {
            TileResult t = c.T;
            double S = c.S;
            MeshData m = t.Roads;
            float top = lift + KerbHeight;
            int prev = -1;
            for (int i = a; i <= b; i++)
            {
                double x = pts[i * 2], y = pts[i * 2 + 1];
                double lx = dirs[i * 2 + 1] * sg, ly = -dirs[i * 2] * sg;
                float nx, ny, nz;
                Normal(t, x / S, y / S, S, out nx, out ny, out nz);
                float v = (float)(v0 + along[i]);
                float ix, iy, iz, mx, my, mz, ox, oy, oz;
                t.Surface((x + lx * hw) / S, (y + ly * hw) / S, out ix, out iy, out iz);
                t.Surface((x + lx * (hw + sw * 0.5)) / S, (y + ly * (hw + sw * 0.5)) / S, out mx, out my, out mz);
                t.Surface((x + lx * (hw + sw)) / S, (y + ly * (hw + sw)) / S, out ox, out oy, out oz);
                // stamp the ground under it as paving, so no grass grows through
                Stamp(t, (x + lx * (hw + sw * 0.5)) / S, (y + ly * (hw + sw * 0.5)) / S, sw * 0.6 / S);
                int row = m.Count;
                // kerb face (lit as facing the road), the top, and the outer edge down to the ground
                float fnx = (float)(-lx), fnz = (float)(ly);    // tile z is north: south offset flips
                Vert(m, ix, iy + lift - 0.03f, iz, fnx, 0.3f, fnz, 0, v, hw, 50, KerbFace, 0, 0, 0, seed, 0);
                Vert(m, ix, iy + top, iz, fnx, 0.3f, fnz, 0.01f, v, hw, 50, KerbFace, 0, 0, 0, seed, 0);
                Vert(m, ix, iy + top, iz, nx, ny, nz, 0, v, sw, 50, Sidewalk, 0, 0, 0, seed, 0);
                Vert(m, mx, my + top, mz, nx, ny, nz, sw * 0.5f, v, sw, 50, Sidewalk, 0, 0, 0, seed, 0);
                Vert(m, ox, oy + top, oz, nx, ny, nz, sw, v, sw, 50, Sidewalk, 0, 0, 0, seed, 0);
                Vert(m, ox, oy + top, oz, -fnx, 0.3f, -fnz, 0.01f, v, hw, 50, KerbFace, 0, 0, 0, seed, 0);
                Vert(m, ox, oy - 0.1f, oz, -fnx, 0.3f, -fnz, 0, v, hw, 50, KerbFace, 0, 0, 0, seed, 0);
                if (prev >= 0)
                {
                    float upx = nx, upy = ny, upz = nz;
                    Quad(m, prev, prev + 1, row, row + 1, fnx, 0, fnz);
                    Quad(m, prev + 2, prev + 3, row + 2, row + 3, upx, upy, upz);
                    Quad(m, prev + 3, prev + 4, row + 3, row + 4, upx, upy, upz);
                    Quad(m, prev + 5, prev + 6, row + 5, row + 6, -fnx, 0, -fnz);
                }
                prev = row;
            }
        }

        static void Quad(MeshData m, int p0, int p1, int q0, int q1, float nx, float ny, float nz)
        {
            m.Tri(p0, p1, q0, nx, ny, nz); m.Tri(p1, q1, q0, nx, ny, nz);
        }

        static void Stamp(TileResult t, double u, double v, double r)
        {
            if (t.Kinds == null) return;
            int N = t.TexSize;
            int rp = Math.Max(1, (int)Math.Ceiling(r * N));
            int cx = (int)(u * N), cy = (int)(v * N);
            for (int j = cy - rp; j <= cy + rp; j++)
                for (int i = cx - rp; i <= cx + rp; i++)
                    if (i >= 0 && j >= 0 && i < N && j < N) t.Kinds[j * N + i] = GroundKind.Paved;
        }

        /// <summary>A zebra crossing: only the stretch that lies on a street.</summary>
        static void Crossing(Ctx c, TileBuilder.RoadItem r, List<double> pts, double[] dirs, int n, float lift, float seed)
        {
            TileResult t = c.T;
            double S = c.S;
            MeshData m = t.Roads;
            float hw = 3.2f * TileBuilder.RoadScale * 0.5f;
            lift += 0.035f;
            // finer steps, so the bars stop right at the kerb
            var fp = new List<double>();
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    double ax = pts[i * 2 - 2], ay = pts[i * 2 - 1], bx = pts[i * 2], by = pts[i * 2 + 1];
                    int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / 0.2));
                    for (int k = 1; k < steps; k++) { fp.Add(ax + (bx - ax) * k / steps); fp.Add(ay + (by - ay) * k / steps); }
                }
                fp.Add(pts[i * 2]); fp.Add(pts[i * 2 + 1]);
            }
            int fn = fp.Count / 2;
            var fd = new double[fn * 2];
            for (int i = 0; i < fn; i++)
            {
                int i0 = Math.Max(0, i - 1), i1 = Math.Min(fn - 1, i + 1);
                double dx = fp[i1 * 2] - fp[i0 * 2], dy = fp[i1 * 2 + 1] - fp[i0 * 2 + 1], dl = Math.Sqrt(dx * dx + dy * dy);
                if (dl < 1e-9) { dx = 1; dy = 0; dl = 1; }
                fd[i * 2] = dx / dl; fd[i * 2 + 1] = dy / dl;
            }
            pts = fp; dirs = fd; n = fn;
            var on = new bool[n];
            for (int i = 0; i < n; i++) on[i] = OnAnyRoad(c, pts[i * 2], pts[i * 2 + 1], -0.05f);
            int prev = -1;
            double along = 0, lastRow = -9;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) along += Math.Sqrt((pts[i * 2] - pts[i * 2 - 2]) * (pts[i * 2] - pts[i * 2 - 2]) + (pts[i * 2 + 1] - pts[i * 2 - 1]) * (pts[i * 2 + 1] - pts[i * 2 - 1]));
                if (!on[i] && !(i + 1 < n && on[i + 1]) && !(i > 0 && on[i - 1])) { prev = -1; continue; }
                // a row where it steps on or off the street, and otherwise every couple of metres
                bool edge = i == 0 || i == n - 1 || on[i] != on[i - 1] || on[i] != on[i + 1];
                if (!edge && prev >= 0 && along - lastRow < 2.0) continue;
                lastRow = along;
                double x = pts[i * 2], y = pts[i * 2 + 1];
                double lx = dirs[i * 2 + 1], ly = -dirs[i * 2];
                float nx, ny, nz;
                Normal(t, x / S, y / S, S, out nx, out ny, out nz);
                int row = m.Count;
                for (int a = 0; a < 3; a++)
                {
                    double f = a - 1;
                    float px, py, pz;
                    t.Surface((x + lx * hw * f) / S, (y + ly * hw * f) / S, out px, out py, out pz);
                    // fades out where it leaves the street (dj below zero hides it)
                    Vert(m, px, py + lift, pz, nx, ny, nz, (float)(hw * f), (float)along, hw, on[i] ? 1f : -1f, Zebra, 0, 0, 0, seed, 0);
                }
                if (prev >= 0) { Quad(m, prev, prev + 1, row, row + 1, 0, 1, 0); Quad(m, prev + 1, prev + 2, row + 1, row + 2, 0, 1, 0); }
                prev = row;
            }
        }

        // ------------------------------------------------------------ bridge decks

        /// <summary>
        /// The road surface on a bridge deck (Bridges.Deck gives the deck's left and right top edges per point),
        /// laid a hair above the deck so it carries the same lines as the road leading on to it.
        /// </summary>
        internal static void DeckTop(TileResult t, TileBuilder.RoadItem r, List<float> left, List<float> right)
        {
            if (r == null || r.Look == null || t.Roads == null) return;
            int n = left.Count / 3;
            if (n < 2) return;
            MeshData m = t.Roads;
            Extra(m);
            Look k = r.Look;
            float hw = r.Style.RealWidth * TileBuilder.RoadScale * 0.5f;
            float laneW = k.LaneReal * TileBuilder.RoadScale, seed = (k.Seed & 0xffff) / 65535f;
            int flags = k.Flags & ~Ruts, kind = k.Kind == Dirt || k.Kind == Gravel ? Asphalt : k.Kind;
            if (k.Crosswalk) return;
            float along = 0;
            int prev = -1;
            for (int i = 0; i < n; i++)
            {
                float ax = left[i * 3], ay = left[i * 3 + 1], az = left[i * 3 + 2], bx = right[i * 3], by = right[i * 3 + 1], bz = right[i * 3 + 2];
                if (i > 0)
                {
                    float cx = (ax + bx) * 0.5f - (left[i * 3 - 3] + right[i * 3 - 3]) * 0.5f, cz = (az + bz) * 0.5f - (left[i * 3 - 1] + right[i * 3 - 1]) * 0.5f;
                    along += (float)Math.Sqrt(cx * cx + cz * cz);
                }
                float half = (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)) * 0.5f;
                int row = m.Count;
                Vert(m, ax, ay + 0.02f, az, 0, 1, 0, half, along, hw, 50, kind, flags, k.Lanes, laneW, seed, 0);
                Vert(m, (ax + bx) * 0.5f, (ay + by) * 0.5f + 0.02f, (az + bz) * 0.5f, 0, 1, 0, 0, along, hw, 50, kind, flags, k.Lanes, laneW, seed, 0);
                Vert(m, bx, by + 0.02f, bz, 0, 1, 0, -half, along, hw, 50, kind, flags, k.Lanes, laneW, seed, 0);
                if (prev >= 0) { Quad(m, prev, prev + 1, row, row + 1, 0, 1, 0); Quad(m, prev + 1, prev + 2, row + 1, row + 2, 0, 1, 0); }
                prev = row;
            }
        }
    }
}
