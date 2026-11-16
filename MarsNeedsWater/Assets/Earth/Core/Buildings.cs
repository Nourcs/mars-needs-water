using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The buildings of one square: walls, real roof shapes (flat with a parapet, gabled, hipped, mansard, pyramid)
    /// and the odd things on top (chimneys, water tanks, plant rooms). The look of each comes from BuildingStyle;
    /// windows, balconies, brickwork and roof tiles are drawn by EarthBuilding.shader from numbers on each vertex:
    ///   uv0  metres along the wall and metres above the building's foot (roofs: metres along the eave and up the slope)
    ///   uv1  x: width of one window bay (0 = blank wall), y: height of one floor, z: height of the wall, w: accent colour
    ///   uv2  x: wall or roof colour, y: trim colour, z and w: packed choices (see Look.BitsA, Look.BitsB)
    /// </summary>
    public static class Buildings
    {
        /// <summary>Walls are spread over this many meshes so that no single collider is slow to make.</summary>
        public const int Chunks = 6;

        struct P3
        {
            public float X, Y, Z;
            public P3(float x, float y, float z) { X = x; Y = y; Z = z; }
            public static P3 operator +(P3 a, P3 b) { return new P3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
            public static P3 operator -(P3 a, P3 b) { return new P3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
            public static P3 operator *(P3 a, float k) { return new P3(a.X * k, a.Y * k, a.Z * k); }
            public float Len { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
            public static float Dot(P3 a, P3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
            public static P3 Cross(P3 a, P3 b) { return new P3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
            public P3 Up(float y) { return new P3(X, y, Z); }
        }

        sealed class Ring
        {
            public int N;
            public float[] X, Z;          // tile-local metres
            public int[] IX, IY;          // the map's own whole numbers, to spot edges cut by the tile border
            public float[] Len, NX, NZ;   // each edge (from corner i to i + 1): length, and the direction out of the building
        }

        sealed class Info { public string Cls, Sub, FCol, FMat, RMat, RCol; public double Height = double.NaN, Floors = double.NaN, Area; }

        /// <summary>Where vertices go, and what each says to the shader.</summary>
        sealed class Pen
        {
            public MeshData M;
            public float A0, A1, A2, A3, B0, B1, B2, B3;

            public int V(float x, float y, float z, float nx, float ny, float nz, float u, float v)
            {
                int i = M.Add(x, y, z, nx, ny, nz, u, v);
                M.X.Add(A0); M.X.Add(A1); M.X.Add(A2); M.X.Add(A3);
                M.Y.Add(B0); M.Y.Add(B1); M.Y.Add(B2); M.Y.Add(B3);
                return i;
            }

            /// <summary>A flat face of three or four corners, facing the side 'hint' is on; a to b is its lower edge.</summary>
            public void Face(P3 a, P3 b, P3 c, P3 hint) { Face(a, b, c, c, false, hint); }
            public void Face(P3 a, P3 b, P3 c, P3 d, P3 hint) { Face(a, b, c, d, true, hint); }

            void Face(P3 a, P3 b, P3 c, P3 d, bool quad, P3 hint)
            {
                P3 n = P3.Cross(b - a, c - a);
                float l = n.Len;
                if (l < 1e-7f) { if (!quad) return; n = P3.Cross(c - a, d - a); l = n.Len; if (l < 1e-7f) return; }
                n = n * (1f / l);
                if (P3.Dot(n, hint) < 0) n = n * -1f;
                P3 t = b - a;
                float tl = t.Len;
                if (tl < 1e-6f) { t = c - a; tl = t.Len; if (tl < 1e-6f) return; }
                t = t * (1f / tl);
                P3 up = P3.Cross(n, t);
                if (up.Y < 0 || (Math.Abs(up.Y) < 1e-4f && up.Z < 0)) up = up * -1f;
                int i0 = V(a.X, a.Y, a.Z, n.X, n.Y, n.Z, 0, 0);
                int i1 = V(b.X, b.Y, b.Z, n.X, n.Y, n.Z, P3.Dot(b - a, t), P3.Dot(b - a, up));
                int i2 = V(c.X, c.Y, c.Z, n.X, n.Y, n.Z, P3.Dot(c - a, t), P3.Dot(c - a, up));
                M.Tri(i0, i1, i2, n.X, n.Y, n.Z);
                if (!quad) return;
                int i3 = V(d.X, d.Y, d.Z, n.X, n.Y, n.Z, P3.Dot(d - a, t), P3.Dot(d - a, up));
                M.Tri(i0, i2, i3, n.X, n.Y, n.Z);
            }
        }

        static uint Hash(string s)
        {
            uint h = 2166136261;
            if (s != null) for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            return h;
        }

        static float Rnd(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xffffff) / 16777216f; }

        static double RingArea(int[] r)
        {
            double a = 0;
            int n = r.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i, i++) a += (double)r[j * 2] * r[i * 2 + 1] - (double)r[i * 2] * r[j * 2 + 1];
            return a * 0.5;
        }

        static MeshData NewMesh() { return new MeshData { X = new List<float>(), Y = new List<float>() }; }

        public static void Build(TileResult t, TileSources src)
        {
            t.Walls = new MeshData[Chunks];
            for (int i = 0; i < Chunks; i++) t.Walls[i] = NewMesh();
            t.Roofs = NewMesh();
            var layers = Mvt.Decode(src.Buildings.GetTile(14, t.X, t.Y), "building", "building_part");
            // plain map buildings are left out where a landmark has its own model
            var clear = new List<double>();
            var models = new List<Landmark>();
            double midLat = Geo.TileLat(t.Y + 0.5, 14), midLon = Geo.TileLon(t.X + 0.5, 14);
            foreach (Landmark lm in Landmarks.All)
            {
                if (lm.Build != null && Math.Abs(lm.Lat - midLat) < 0.04 && Math.Abs(lm.Lon - midLon) * Math.Cos(midLat * Geo.Deg) < 0.04) models.Add(lm);
                if (lm.ClearRadius <= 0) continue;
                double u = Geo.LonToX(lm.ClearLon, 14) - t.X, v = Geo.LatToY(lm.ClearLat, 14) - t.Y, r = lm.ClearRadius / Geo.Scale / t.Size;
                if (u < -r - 0.1 || v < -r - 0.1 || u > 1 + r + 0.1 || v > 1 + r + 0.1) continue;
                clear.Add(u); clear.Add(v); clear.Add(r * r);
            }
            MvtLayer parts;
            layers.TryGetValue("building_part", out parts);
            bool haveParts = parts != null && parts.Features.Count > 0;
            var parents = new Dictionary<string, Info>();
            foreach (string name in new[] { "building", "building_part" })
            {
                MvtLayer L;
                if (!layers.TryGetValue(name, out L)) continue;
                bool isPart = name == "building_part";
                int kH = L.Key("height"), kF = L.Key("num_floors"), kMin = L.Key("min_height"), kParts = L.Key("has_parts");
                int kCls = L.Key("class"), kSub = L.Key("subtype"), kId = L.Key("id"), kOf = L.Key("building_id"), kUnder = L.Key("is_underground");
                int kFCol = L.Key("facade_color"), kFMat = L.Key("facade_material"), kRShape = L.Key("roof_shape"), kRMat = L.Key("roof_material"), kRCol = L.Key("roof_color");
                double e = L.Extent;
                double unit = t.Size * Geo.Scale / e;       // real metres per map unit
                foreach (MvtFeature f in L.Features)
                {
                    if (f.Type != 3 || f.Parts.Count == 0) continue;
                    if (L.Flag(f, kUnder)) continue;
                    var me = new Info
                    {
                        Cls = L.Str(f, kCls), Sub = L.Str(f, kSub), FCol = L.Str(f, kFCol), FMat = L.Str(f, kFMat), RMat = L.Str(f, kRMat), RCol = L.Str(f, kRCol),
                        Height = L.Num(f, kH, double.NaN), Floors = L.Num(f, kF, double.NaN), Area = Math.Abs(RingArea(f.Parts[0])) * unit * unit,
                    };
                    if (me.Height <= 0) me.Height = double.NaN;
                    if (me.Floors <= 0) me.Floors = double.NaN;
                    string id = L.Str(f, kId);
                    if (!isPart && haveParts && L.Flag(f, kParts)) { if (id != null) parents[id] = me; continue; }

                    // a part looks like the building it belongs to, except where it says otherwise itself
                    Info of = me;
                    string styleId = id;
                    if (isPart)
                    {
                        string pid = L.Str(f, kOf);
                        if (pid != null) { styleId = pid; Info p; if (parents.TryGetValue(pid, out p)) of = p; }
                    }
                    uint hash = Hash(styleId);
                    int[] first = f.Parts[0];
                    double sx = 0, sy = 0;
                    for (int i = 0; i < first.Length; i += 2) { sx += first[i]; sy += first[i + 1]; }
                    double lat = Geo.TileLat(t.Y + sy / (first.Length / 2) / e, 14), lon = Geo.TileLon(t.X + sx / (first.Length / 2) / e, 14);
                    Look look = BuildingStyle.Pick(lat, lon, hash, of.Cls, of.Sub, double.IsNaN(of.Height) ? me.Height : of.Height, double.IsNaN(of.Floors) ? me.Floors : of.Floors,
                        Math.Max(of.Area, me.Area), me.FCol ?? of.FCol, me.FMat ?? of.FMat, L.Str(f, kRShape), me.RMat ?? of.RMat, me.RCol ?? of.RCol);
                    if (isPart && L.Str(f, kRShape) == null) look.RoofShape = BK.ShFlat;       // something may well stand on it

                    double real = me.Height, floors = me.Floors;
                    if (double.IsNaN(real))
                    {
                        if (!double.IsNaN(floors)) real = Math.Max(3.0, floors * look.FloorReal + (look.RoofShape != BK.ShFlat ? 1.5 : 0.5));
                        else if (isPart && !double.IsNaN(of.Height)) real = of.Height;
                        else if (look.Cat == BK.CatShed) real = Style.DefaultHeight(of.Cls, of.Sub, hash);
                        else { floors = look.DefFloors; real = floors * look.FloorReal + (look.RoofShape != BK.ShFlat ? 1.5 : 0.5); }
                    }
                    if (double.IsNaN(floors)) floors = Math.Max(1.0, Math.Round((real - (look.RoofShape != BK.ShFlat ? 1.0 : 0.3)) / look.FloorReal));
                    double realMin = L.Num(f, kMin, 0);
                    if (realMin < 0 || realMin >= real) realMin = 0;

                    // split the feature into polygons: an outer ring followed by its holes
                    double sign = 0;
                    var poly = new List<int[]>();
                    foreach (int[] ring in f.Parts)
                    {
                        if (ring.Length < 6) continue;
                        double a = RingArea(ring);
                        if (a == 0) continue;
                        if (sign == 0) sign = Math.Sign(a);
                        if (Math.Sign(a) == sign)
                        {
                            if (poly.Count > 0) Add(t, poly, e, real, realMin, floors, hash, clear, models, look, isPart);
                            poly = new List<int[]>();
                        }
                        else if (poly.Count == 0) continue;
                        poly.Add(ring);
                    }
                    if (poly.Count > 0) Add(t, poly, e, real, realMin, floors, hash, clear, models, look, isPart);
                }
            }
        }

        static void Add(TileResult t, List<int[]> raw, double extent, double real, double realMin, double floors, uint hash, List<double> clear, List<Landmark> models, Look k, bool isPart)
        {
            int[] outer = raw[0];
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, total = 0;
            for (int i = 0; i < outer.Length; i += 2)
            {
                if (outer[i] < minX) minX = outer[i]; if (outer[i] > maxX) maxX = outer[i];
                if (outer[i + 1] < minY) minY = outer[i + 1]; if (outer[i + 1] > maxY) maxY = outer[i + 1];
            }
            // a building wholly inside the neighbour's part of the buffer is drawn by the neighbour
            double cx = (minX + maxX) * 0.5, cy = (minY + maxY) * 0.5;
            if (cx < 0 || cy < 0 || cx >= extent || cy >= extent) return;
            for (int c = 0; c < clear.Count; c += 3)
            {
                double du = cx / extent - clear[c], dv = cy / extent - clear[c + 1];
                if (du * du + dv * dv < clear[c + 2]) return;
            }
            // nor where a landmark's model itself stands (tested at the middle and half way to each corner)
            foreach (Landmark lm in models)
                for (int q = 0; q < 5; q++)
                {
                    double qx = q == 0 ? cx : cx + (q % 2 == 1 ? minX - cx : maxX - cx) * 0.5, qy = q == 0 ? cy : cy + (q <= 2 ? minY - cy : maxY - cy) * 0.5;
                    double qla = Geo.TileLat(t.Y + qy / extent, 14), qlo = Geo.TileLon(t.X + qx / extent, 14);
                    double east = (qlo - lm.Lon) * 111195.0 * Math.Cos(lm.Lat * Geo.Deg) / Geo.Scale, north = (qla - lm.Lat) * 111195.0 / Geo.Scale;
                    if (Math.Abs(east) > 600 || Math.Abs(north) > 600) break;
                    if (Landmarks.Covers(lm, east, north)) return;
                }
            foreach (int[] r in raw) total += r.Length / 2;
            if (total > 6000) return;

            // ---- corners on the ground
            var rings = new List<Ring>(raw.Count);
            float gMin = float.MaxValue, gMax = float.MinValue, gSum = 0;
            int count = 0;
            for (int ri = 0; ri < raw.Count; ri++)
            {
                int[] r = raw[ri];
                int n = r.Length / 2;
                var ring = new Ring { X = new float[n], Z = new float[n], IX = new int[n], IY = new int[n] };
                int m = 0;
                for (int i = 0; i < n; i++)
                {
                    float x, y, z;
                    t.Surface(r[i * 2] / extent, r[i * 2 + 1] / extent, out x, out y, out z);
                    if (m > 0 && Math.Abs(x - ring.X[m - 1]) + Math.Abs(z - ring.Z[m - 1]) < 0.012f) continue;
                    ring.X[m] = x; ring.Z[m] = z; ring.IX[m] = r[i * 2]; ring.IY[m] = r[i * 2 + 1]; m++;
                    if (y < gMin) gMin = y; if (y > gMax) gMax = y;
                    gSum += y; count++;
                }
                if (m > 1 && Math.Abs(ring.X[0] - ring.X[m - 1]) + Math.Abs(ring.Z[0] - ring.Z[m - 1]) < 0.012f) m--;
                if (m < 3) { if (ri == 0) return; continue; }
                ring.N = m;
                rings.Add(ring);
            }
            if (count == 0) return;
            double area2 = 0;
            {
                Ring o = rings[0];
                for (int i = 0, j = o.N - 1; i < o.N; j = i, i++) area2 += (double)o.X[j] * o.Z[i] - (double)o.X[i] * o.Z[j];
            }
            if (Math.Abs(area2) < 0.02) return;
            float sg = area2 > 0 ? 1f : -1f;
            float areaGame = (float)Math.Abs(area2) * 0.5f;
            int verts = 0;
            foreach (Ring r in rings)
            {
                r.Len = new float[r.N]; r.NX = new float[r.N]; r.NZ = new float[r.N];
                for (int i = 0; i < r.N; i++)
                {
                    int j = (i + 1) % r.N;
                    float dx = r.X[j] - r.X[i], dz = r.Z[j] - r.Z[i], l = (float)Math.Sqrt(dx * dx + dz * dz);
                    r.Len[i] = l;
                    if (l > 1e-6f) { r.NX[i] = sg * dz / l; r.NZ[i] = -sg * dx / l; }
                }
                verts += r.N;
            }

            float gAvg = gSum / count;
            float top = Math.Max(gAvg + Geo.Height(real), gMax + 0.6f);
            float bottom = realMin > 0 ? gAvg + Geo.Height(realMin) : gMin - 0.4f;
            if (bottom >= top) bottom = gMin - 0.4f;
            float foot = Math.Max(bottom, gAvg);
            float body = top - foot;

            uint s = hash * 747796405u + 2891336453u;
            if (s == 0) s = 1;
            Rnd(ref s);
            var wall = new Pen { M = t.Walls[(int)((hash >> 8) % Chunks)] };
            var roof = new Pen { M = t.Roofs };
            float accent = k.Accent, wallCol = k.Wall, trim = k.Trim, roofCol = k.RoofCol, bitsB = k.BitsB;
            float bay0 = Math.Max(0.5f, k.BayReal / (float)Geo.Scale);

            // ---- the roof's shape decides how high the walls go
            int shape = k.RoofShape;
            if (areaGame < 0.5f || body < 0.5f) shape = BK.ShFlat;
            P3[] quad = shape == BK.ShFlat || shape == BK.ShMansard || rings.Count > 1 ? null : AsQuad(rings[0]);
            float[][] inX = null, inZ = null;
            float rise = 0, inset = 0;
            if (shape == BK.ShMansard)
            {
                rise = Math.Min(body * 0.3f, Math.Max(0.5f, (float)(body / Math.Max(1.0, floors + 1))));
                inset = rise / Math.Max(0.8f, k.Pitch);
                if (!Inset(rings, inset, verts, out inX, out inZ)) { inset *= 0.5f; rise *= 0.5f; if (!Inset(rings, inset, verts, out inX, out inZ)) shape = BK.ShFlat; }
            }
            else if (shape != BK.ShFlat && quad == null)
            {
                // any other outline: slopes all round, as deep as the outline allows, and a flat middle
                float want = Math.Min(1.8f, (float)Math.Sqrt(areaGame) * 0.34f);
                float pitch = Math.Max(0.3f, k.Pitch);
                bool ok = false;
                for (int tries = 0; tries < 4 && !ok; tries++, want *= 0.62f)
                {
                    inset = want; rise = Math.Min(inset * pitch, body * 0.42f);
                    ok = Inset(rings, inset, verts, out inX, out inZ);
                }
                if (!ok) shape = BK.ShFlat;
                else shape = BK.ShHip;
            }
            else if (shape != BK.ShFlat)
            {
                // a four-cornered outline gets the real thing; the ridge runs the long way unless told otherwise
                float l0 = (quad[1] - quad[0]).Len + (quad[3] - quad[2]).Len, l1 = (quad[2] - quad[1]).Len + (quad[0] - quad[3]).Len;
                bool along = l0 >= l1;
                if (shape == BK.ShGableAcross && Math.Min(l0, l1) > Math.Max(l0, l1) * 0.35f) along = !along;
                if (!along) { P3 q0 = quad[0]; quad[0] = quad[1]; quad[1] = quad[2]; quad[2] = quad[3]; quad[3] = q0; }
                float half = ((quad[2] - quad[1]).Len + (quad[0] - quad[3]).Len) * 0.25f;
                if (shape == BK.ShPyramid) half = Math.Min(half, ((quad[1] - quad[0]).Len + (quad[3] - quad[2]).Len) * 0.25f);
                rise = Math.Min(half * Math.Max(0.25f, k.Pitch), body * (shape == BK.ShPyramid ? 0.6f : 0.45f));
                if (rise < 0.08f) shape = BK.ShFlat;
            }
            float wallTop = top - (shape == BK.ShFlat ? 0f : rise);
            if (wallTop < bottom + 0.2f) { wallTop = top; shape = BK.ShFlat; }

            // ---- walls
            float H = wallTop - gAvg;
            int nF = (int)Math.Max(1.0, Math.Round(floors * H / Math.Max(0.1f, body)));
            if (H / nF < 0.62f) nF = Math.Max(1, (int)(H / 0.62f));
            float floorH = H / nF;
            int lo = (int)(-extent / 64) + 2, hi = (int)(extent + extent / 64) - 2;
            wall.A1 = floorH; wall.A2 = H; wall.A3 = accent;
            wall.B0 = wallCol; wall.B1 = trim; wall.B2 = k.BitsA(BK.Wall, k.Mat); wall.B3 = bitsB;
            float vB = bottom - gAvg, vT = wallTop - gAvg;
            foreach (Ring r in rings)
            {
                int n = r.N, start = 0;
                // a run of wall goes from one real corner to the next; windows are spaced evenly along each run
                var corner = new bool[n];
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    int p = (i + n - 1) % n;
                    corner[i] = r.NX[p] * r.NX[i] + r.NZ[p] * r.NZ[i] < 0.9f;
                    if (corner[i] && !any) { any = true; start = i; }
                }
                int e = 0;
                while (e < n)
                {
                    int e1 = e;
                    float L = 0;
                    do { L += r.Len[(start + e1) % n]; e1++; } while (e1 < n && !corner[(start + e1) % n]);
                    int nb = L < bay0 * 0.6f ? 0 : Math.Max(1, (int)Math.Round(L / bay0));
                    wall.A0 = nb == 0 ? 0f : L / nb;
                    float u = 0;
                    for (; e < e1; e++)
                    {
                        int i = (start + e) % n, j = (i + 1) % n;
                        float len = r.Len[i];
                        float u0 = u; u += len;
                        if (len < 0.02f) continue;
                        // an edge that runs along the tile buffer is a cut, not a real wall
                        int ax = r.IX[i], ay = r.IY[i], bx = r.IX[j], by = r.IY[j];
                        if ((ax <= lo && bx <= lo) || (ax >= hi && bx >= hi) || (ay <= lo && by <= lo) || (ay >= hi && by >= hi)) continue;
                        float nx = r.NX[i], nz = r.NZ[i];
                        int v0 = wall.V(r.X[i], bottom, r.Z[i], nx, 0, nz, u0, vB);
                        int v1 = wall.V(r.X[j], bottom, r.Z[j], nx, 0, nz, u, vB);
                        int v2 = wall.V(r.X[j], wallTop, r.Z[j], nx, 0, nz, u, vT);
                        int v3 = wall.V(r.X[i], wallTop, r.Z[i], nx, 0, nz, u0, vT);
                        wall.M.Tri(v0, v1, v2, nx, 0, nz);
                        wall.M.Tri(v0, v2, v3, nx, 0, nz);
                    }
                }
            }

            // ---- roof
            P3 up = new P3(0, 1, 0);
            float roofBits = k.BitsA(BK.Roof, k.RoofMat), plainBits = k.BitsA(BK.Plain, k.Mat), plainStucco = k.BitsA(BK.Plain, BK.Stucco);
            float bitsNoDormer = (int)bitsB & ~BK.FDormer;
            roof.A0 = 0; roof.A1 = 0; roof.A2 = 0; roof.A3 = accent;
            roof.B0 = roofCol; roof.B1 = trim; roof.B2 = roofBits; roof.B3 = bitsNoDormer;
            wall.A0 = 0; wall.A1 = 1; wall.A2 = 1;
            wall.B3 = bitsNoDormer;

            if (shape == BK.ShFlat)
            {
                // the walls stand a little above a flat roof: a parapet (the shader shows their backs)
                bool parapet = k.Cat != BK.CatShed && areaGame > 3f && body > 1.2f && k.Mat != BK.Metal;
                float y = parapet ? top - (0.1f + 0.22f * Rnd(ref s)) - (k.Cat == BK.CatTower ? 0.15f : 0f) : top;
                List<int> tris;
                double[] flat = Lid(rings, null, null, y, roof, verts, out tris);
                if (!isPart && tris != null) Clutter(wall, flat, tris, rings, null, null, y, k, ref s, areaGame, body);
            }
            else if (inX != null)
            {
                // mansard or hip all round: a sloping skirt up to a flat lid
                bool mansard = shape == BK.ShMansard;
                float slope = (float)Math.Sqrt(inset * inset + rise * rise);
                if (mansard && (k.Flags & BK.FDormer) != 0 && rise > 0.45f) roof.B3 = bitsNoDormer + BK.FDormer;
                for (int ri = 0; ri < rings.Count; ri++)
                {
                    Ring r = rings[ri];
                    for (int i = 0; i < r.N; i++)
                    {
                        int j = (i + 1) % r.N;
                        float len = r.Len[i];
                        if (len < 0.02f) continue;
                        roof.A0 = len < bay0 * 0.8f ? 0f : len / Math.Max(1, (int)Math.Round(len / bay0));
                        roof.A1 = slope;
                        roof.Face(new P3(r.X[i], wallTop, r.Z[i]), new P3(r.X[j], wallTop, r.Z[j]), new P3(inX[ri][j], top, inZ[ri][j]), new P3(inX[ri][i], top, inZ[ri][i]), up);
                    }
                }
                roof.A0 = 0; roof.A1 = 0; roof.B3 = bitsNoDormer;
                if (mansard) roof.B2 = k.BitsA(BK.Roof, BK.RSeam);
                List<int> tris;
                double[] flat = Lid(rings, inX, inZ, top, roof, verts, out tris);
                if (mansard && !isPart && tris != null && (k.Clutter & BK.ClChimney) != 0) Clutter(wall, flat, tris, rings, inX, inZ, top, k, ref s, areaGame, body);
            }
            else
            {
                P3 c0 = quad[0].Up(wallTop), c1 = quad[1].Up(wallTop), c2 = quad[2].Up(wallTop), c3 = quad[3].Up(wallTop);
                P3 mid = (c0 + c1 + c2 + c3) * 0.25f;
                if (shape == BK.ShPyramid)
                {
                    P3 apex = mid.Up(top);
                    roof.Face(c0, c1, apex, up); roof.Face(c1, c2, apex, up); roof.Face(c2, c3, apex, up); roof.Face(c3, c0, apex, up);
                }
                else
                {
                    P3 rA = ((c3 + c0) * 0.5f).Up(top), rB = ((c1 + c2) * 0.5f).Up(top);
                    P3 dir = rB - rA;
                    float ridge = dir.Len;
                    dir = dir * (1f / Math.Max(1e-4f, ridge));
                    bool hip = shape == BK.ShHip;
                    if (hip)
                    {
                        float pull = Math.Min(((c2 - c1).Len + (c0 - c3).Len) * 0.25f, ridge * 0.49f);
                        rA = rA + dir * pull; rB = rB - dir * pull;
                    }
                    roof.Face(c0, c1, rB, rA, up);
                    roof.Face(c2, c3, rA, rB, up);
                    if (hip) { roof.Face(c1, c2, rB, up); roof.Face(c3, c0, rA, up); }
                    else
                    {
                        // gable ends are wall
                        wall.B0 = wallCol; wall.B2 = plainBits;
                        wall.Face(c1, c2, rB, dir);
                        wall.Face(c3, c0, rA, dir * -1f);
                    }
                    if ((k.Clutter & BK.ClChimney) != 0 && rise > 0.25f)
                    {
                        int chimneys = Rnd(ref s) < 0.7f ? 1 : 0;
                        if (Rnd(ref s) < 0.3f) chimneys++;
                        P3 across = new P3(-dir.Z, 0, dir.X);
                        bool brick = k.Mat == BK.Brick || k.Mat == BK.Stone;
                        wall.B0 = brick ? wallCol : (Rnd(ref s) < 0.5f ? 0x8a5a48 : wallCol);
                        wall.B2 = k.BitsA(BK.Plain, brick ? k.Mat : BK.Brick);
                        for (int c = 0; c < chimneys; c++)
                        {
                            float f = c == 0 ? 0.12f + 0.3f * Rnd(ref s) : 0.6f + 0.28f * Rnd(ref s);
                            float w = 0.09f + 0.05f * Rnd(ref s), off = (Rnd(ref s) - 0.5f) * 0.5f * w;
                            P3 at = rA + (rB - rA) * f + across * off;
                            Box(wall, at.X, at.Z, top - rise * 0.5f, w * (1f + Rnd(ref s)), w, rise * 0.5f + 0.18f + 0.2f * Rnd(ref s), dir.X, dir.Z, true);
                        }
                    }
                }
            }
            t.BuildingCount++;
        }

        // ------------------------------------------------------------ shapes

        /// <summary>The four corners of an outline that is, give or take a few points along its sides, four-sided.</summary>
        static P3[] AsQuad(Ring r)
        {
            int n = r.N;
            if (n < 4 || n > 12) return null;
            var keep = new List<int>(4);
            float turnSign = 0;
            for (int i = 0; i < n; i++)
            {
                int p = (i + n - 1) % n;
                // edge normals turn with the walls: the cross product is the sine of the corner's angle
                float sin = r.NX[p] * r.NZ[i] - r.NZ[p] * r.NX[i];
                if (Math.Abs(sin) < 0.2f && r.NX[p] * r.NX[i] + r.NZ[p] * r.NZ[i] > 0) continue;
                if (turnSign == 0) turnSign = Math.Sign(sin);
                else if (Math.Sign(sin) != turnSign) return null;      // not convex
                keep.Add(i);
                if (keep.Count > 4) return null;
            }
            if (keep.Count != 4) return null;
            // points dropped must lie on the straight sides
            for (int q = 0; q < 4; q++)
            {
                int a = keep[q], b = keep[(q + 1) % 4];
                float dx = r.X[b] - r.X[a], dz = r.Z[b] - r.Z[a], l = (float)Math.Sqrt(dx * dx + dz * dz);
                if (l < 0.05f) return null;
                for (int i = (a + 1) % n; i != b; i = (i + 1) % n)
                    if (Math.Abs((r.X[i] - r.X[a]) * dz - (r.Z[i] - r.Z[a]) * dx) / l > 0.06f) return null;
            }
            var c = new P3[4];
            for (int q = 0; q < 4; q++) c[q] = new P3(r.X[keep[q]], 0, r.Z[keep[q]]);
            return c;
        }

        /// <summary>
        /// Every corner moved d into the building. False if the outline is too thin or too busy for that (the
        /// caller then makes something simpler).
        /// </summary>
        static bool Inset(List<Ring> rings, float d, int verts, out float[][] ox, out float[][] oz)
        {
            ox = null; oz = null;
            if (verts > 56 || d <= 0) return false;
            var rx = new float[rings.Count][]; var rz = new float[rings.Count][];
            for (int ri = 0; ri < rings.Count; ri++)
            {
                Ring r = rings[ri];
                int n = r.N;
                rx[ri] = new float[n]; rz[ri] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int p = (i + n - 1) % n;
                    float mx = r.NX[p] + r.NX[i], mz = r.NZ[p] + r.NZ[i];
                    float den = 1f + r.NX[p] * r.NX[i] + r.NZ[p] * r.NZ[i];
                    if (den < 0.35f) return false;                    // a spike
                    float x = r.X[i] - mx / den * d, z = r.Z[i] - mz / den * d;
                    rx[ri][i] = x; rz[ri][i] = z;
                }
            }
            // each moved corner must still be inside, and no nearer than d to any wall
            float need = d * 0.86f;
            need *= need;
            for (int ri = 0; ri < rings.Count; ri++)
                for (int i = 0; i < rings[ri].N; i++)
                {
                    float x = rx[ri][i], z = rz[ri][i];
                    bool inside = false;
                    foreach (Ring r in rings)
                        for (int a = 0, b = r.N - 1; a < r.N; b = a, a++)
                        {
                            float ax = r.X[b], az = r.Z[b], bx = r.X[a], bz = r.Z[a];
                            if ((az > z) != (bz > z) && x < (bx - ax) * (z - az) / (bz - az) + ax) inside = !inside;
                            float ex = bx - ax, ez = bz - az, l2 = ex * ex + ez * ez;
                            float f = l2 > 1e-10f ? ((x - ax) * ex + (z - az) * ez) / l2 : 0f;
                            if (f < 0) f = 0; else if (f > 1) f = 1;
                            float qx = x - (ax + ex * f), qz = z - (az + ez * f);
                            if (qx * qx + qz * qz < need) return false;
                        }
                    if (!inside) return false;
                }
            ox = rx; oz = rz;
            return true;
        }

        /// <summary>A flat lid over the outline (or over its inset) at height y. Returns the corners as x,z pairs.</summary>
        static double[] Lid(List<Ring> rings, float[][] inX, float[][] inZ, float y, Pen pen, int verts, out List<int> tris)
        {
            var data = new double[verts * 2];
            var holes = rings.Count > 1 ? new int[rings.Count - 1] : null;
            int k = 0, first = pen.M.Count;
            for (int ri = 0; ri < rings.Count; ri++)
            {
                if (ri > 0) holes[ri - 1] = k;
                Ring r = rings[ri];
                for (int i = 0; i < r.N; i++, k++)
                {
                    float x = inX != null ? inX[ri][i] : r.X[i], z = inZ != null ? inZ[ri][i] : r.Z[i];
                    data[k * 2] = x; data[k * 2 + 1] = z;
                    pen.V(x, y, z, 0, 1, 0, x, z);
                }
            }
            tris = Earcut.Triangulate(data, holes);
            for (int i = 0; i + 2 < tris.Count; i += 3) pen.M.Tri(first + tris[i], first + tris[i + 1], first + tris[i + 2], 0, 1, 0);
            return data;
        }

        /// <summary>A box standing at height y; dx,dz is the way its long side runs.</summary>
        static void Box(Pen pen, float x, float z, float y, float halfLong, float halfWide, float height, float dx, float dz, bool lid)
        {
            P3 a = new P3(dx, 0, dz) * halfLong, b = new P3(-dz, 0, dx) * halfWide, c = new P3(x, y, z), h = new P3(0, height, 0);
            P3 p0 = c - a - b, p1 = c + a - b, p2 = c + a + b, p3 = c - a + b;
            pen.Face(p0, p1, p1 + h, p0 + h, b * -1f);
            pen.Face(p1, p2, p2 + h, p1 + h, a);
            pen.Face(p2, p3, p3 + h, p2 + h, b);
            pen.Face(p3, p0, p0 + h, p3 + h, a * -1f);
            if (lid) pen.Face(p0 + h, p1 + h, p2 + h, p3 + h, new P3(0, 1, 0));
        }

        /// <summary>An upright drum of eight sides with a flat or pointed top.</summary>
        static void Drum(Pen pen, float x, float z, float y, float radius, float height, float cone)
        {
            P3 top = new P3(x, y + height + cone, z);
            for (int i = 0; i < 8; i++)
            {
                double a0 = i * Math.PI / 4, a1 = (i + 1) * Math.PI / 4;
                P3 p0 = new P3(x + (float)Math.Cos(a0) * radius, y, z + (float)Math.Sin(a0) * radius), p1 = new P3(x + (float)Math.Cos(a1) * radius, y, z + (float)Math.Sin(a1) * radius);
                P3 h = new P3(0, height, 0), outward = new P3((float)Math.Cos((a0 + a1) / 2), 0.2f, (float)Math.Sin((a0 + a1) / 2));
                pen.Face(p0, p1, p1 + h, p0 + h, outward);
                pen.Face(p0 + h, p1 + h, top, new P3(outward.X, 1, outward.Z));
            }
        }

        /// <summary>Things that stand on a flat roof: plant rooms, stair heads, water tanks, solar heaters, chimney stacks.</summary>
        static void Clutter(Pen pen, double[] flat, List<int> tris, List<Ring> rings, float[][] inX, float[][] inZ, float y, Look k, ref uint s, float area, float body)
        {
            if (tris.Count < 3 || area < 2.5f) return;
            int want = 0;
            if ((k.Clutter & BK.ClBox) != 0) want += k.Cat == BK.CatTower ? 1 + (int)(Rnd(ref s) * 2f) : Rnd(ref s) < 0.55f ? 1 : 0;
            if ((k.Clutter & BK.ClNyTank) != 0 && Rnd(ref s) < 0.45f) want++;
            if ((k.Clutter & BK.ClTank) != 0) want += (int)(Rnd(ref s) * 2.6f);
            if ((k.Clutter & BK.ClSolar) != 0) want += (int)(Rnd(ref s) * 2.4f);
            if ((k.Clutter & BK.ClChimney) != 0) want += 1 + (int)(Rnd(ref s) * 2.5f);
            if (area > 60f) want++;
            if (want == 0) return;
            if (want > 5) want = 5;

            // the long way of the building
            Ring o = rings[0];
            int longest = 0;
            for (int i = 1; i < o.N; i++) if (o.Len[i] > o.Len[longest]) longest = i;
            float dx = -o.NZ[longest], dz = o.NX[longest];

            int nt = tris.Count / 3;
            int boxes = (k.Clutter & BK.ClBox) != 0 ? 1 : 0;
            bool nyTank = (k.Clutter & BK.ClNyTank) != 0, chim = (k.Clutter & BK.ClChimney) != 0 && (k.Clutter & BK.ClBox) == 0 || k.RoofShape == BK.ShMansard;
            for (int q = 0; q < want; q++)
            {
                // somewhere in one of the larger triangles of the roof
                int best = -1; double bestA = 0;
                for (int tr = 0; tr < 3; tr++)
                {
                    int ti = (int)(Rnd(ref s) * nt) % nt * 3;
                    double ax = flat[tris[ti] * 2], az = flat[tris[ti] * 2 + 1], bx = flat[tris[ti + 1] * 2], bz = flat[tris[ti + 1] * 2 + 1], cx2 = flat[tris[ti + 2] * 2], cz2 = flat[tris[ti + 2] * 2 + 1];
                    double a = Math.Abs((bx - ax) * (cz2 - az) - (cx2 - ax) * (bz - az));
                    if (a > bestA) { bestA = a; best = ti; }
                }
                if (best < 0) continue;
                float w0 = 0.2f + 0.6f * Rnd(ref s), w1 = 0.2f + 0.6f * Rnd(ref s), w2 = 0.2f + 0.6f * Rnd(ref s), ws = w0 + w1 + w2;
                float x = (float)(flat[tris[best] * 2] * w0 + flat[tris[best + 1] * 2] * w1 + flat[tris[best + 2] * 2] * w2) / ws;
                float z = (float)(flat[tris[best] * 2 + 1] * w0 + flat[tris[best + 1] * 2 + 1] * w1 + flat[tris[best + 2] * 2 + 1] * w2) / ws;
                // how much room there is round it
                float room = float.MaxValue;
                for (int ri = 0; ri < rings.Count; ri++)
                {
                    Ring r = rings[ri];
                    for (int a = 0, b = r.N - 1; a < r.N; b = a, a++)
                    {
                        float ax = inX != null ? inX[ri][b] : r.X[b], az = inZ != null ? inZ[ri][b] : r.Z[b], bx = inX != null ? inX[ri][a] : r.X[a], bz = inZ != null ? inZ[ri][a] : r.Z[a];
                        float ex = bx - ax, ez = bz - az, l2 = ex * ex + ez * ez;
                        float f = l2 > 1e-10f ? ((x - ax) * ex + (z - az) * ez) / l2 : 0f;
                        if (f < 0) f = 0; else if (f > 1) f = 1;
                        float qx = x - (ax + ex * f), qz = z - (az + ez * f);
                        room = Math.Min(room, qx * qx + qz * qz);
                    }
                }
                room = (float)Math.Sqrt(room) * 0.62f;
                if (room < 0.09f) continue;

                pen.A0 = 0; pen.A1 = 1; pen.A2 = 1;
                if (chim && k.RoofShape == BK.ShMansard)
                {
                    // a Paris chimney stack: a thin wall of stone with a row of pots
                    float hl = Math.Min(room, 0.25f + 0.3f * Rnd(ref s));
                    pen.B0 = k.Wall; pen.B2 = k.BitsA(BK.Plain, BK.Stucco);
                    bool turn = Rnd(ref s) < 0.5f;
                    Box(pen, x, z, y, hl, Math.Min(room, 0.05f), 0.22f + 0.2f * Rnd(ref s), turn ? -dz : dx, turn ? dx : dz, true);
                }
                else if (nyTank && room > 0.2f)
                {
                    nyTank = false;
                    float r = Math.Min(room, 0.26f + 0.1f * Rnd(ref s)), legs = 0.25f + 0.2f * Rnd(ref s);
                    pen.B0 = 0x3a3632; pen.B2 = k.BitsA(BK.Plain, BK.Metal);
                    Box(pen, x, z, y, r * 0.6f, r * 0.6f, legs, dx, dz, false);
                    pen.B0 = 0x6e4f3a + (uint)(Rnd(ref s) * 6f) * 0x030201; pen.B2 = k.BitsA(BK.Plain, BK.Boards);
                    Drum(pen, x, z, y + legs, r, r * (1.5f + 0.5f * Rnd(ref s)), r * 0.55f);
                }
                else if (boxes > 0 || (q == want - 1 && area > 60f && (k.Clutter & (BK.ClTank | BK.ClSolar)) == 0))
                {
                    boxes--;
                    // a stair head or plant room, in the wall's own stuff or grey metal
                    bool plant = Rnd(ref s) < 0.45f;
                    float hl = Math.Min(room, (plant ? 0.25f : 0.35f) + 0.5f * Rnd(ref s)), hw = Math.Min(room, 0.2f + 0.35f * Rnd(ref s));
                    float h = (plant ? 0.2f : 0.45f) + 0.35f * Rnd(ref s);
                    if (k.Cat == BK.CatTower) { hl = Math.Min(room, hl * 1.8f); hw = Math.Min(room, hw * 1.8f); h *= 1.6f; }
                    pen.B0 = plant ? 0x9a9e9f : k.Wall; pen.B2 = k.BitsA(BK.Plain, plant ? BK.Metal : (k.Mat == BK.Glass ? BK.Concrete : k.Mat));
                    Box(pen, x, z, y, hl, hw, h, dx, dz, true);
                }
                else if ((k.Clutter & BK.ClSolar) != 0 && Rnd(ref s) < 0.6f)
                {
                    // a solar water heater: a sloping dark panel with a white drum at its head
                    float w = Math.Min(room, 0.16f + 0.05f * Rnd(ref s)), l = Math.Min(room * 1.4f, 0.3f);
                    P3 a = new P3(dx, 0, dz) * w, f = new P3(-dz, 0, dx) * l, c = new P3(x, y + 0.03f, z), lift = new P3(0, l * 0.9f, 0);
                    pen.B0 = 0x1e2c44; pen.B2 = k.BitsA(BK.Plain, BK.Glass);
                    pen.Face(c - a - f, c + a - f, c + a + f + lift, c - a + f + lift, new P3(0, 1, 0));
                    pen.B0 = 0xe4e4e0; pen.B2 = k.BitsA(BK.Plain, BK.Metal);
                    Box(pen, x - dz * l, z + dx * l, y + l * 0.9f, w, 0.045f, 0.09f, dx, dz, true);
                }
                else if ((k.Clutter & BK.ClTank) != 0)
                {
                    // a plastic water tank
                    float r = Math.Min(room, 0.09f + 0.06f * Rnd(ref s));
                    float pick = Rnd(ref s);
                    pen.B0 = pick < 0.5f ? 0x24262a : pick < 0.72f ? 0x2f5fa8 : pick < 0.86f ? 0xe6e6e2 : 0x8a8e90; pen.B2 = k.BitsA(BK.Plain, BK.Metal);
                    Drum(pen, x, z, y, r, r * (1.6f + 0.6f * Rnd(ref s)), r * 0.25f);
                }
                else if (chim)
                {
                    float w = Math.Min(room, 0.08f + 0.05f * Rnd(ref s));
                    bool brick = k.Mat == BK.Brick || k.Mat == BK.Stone;
                    pen.B0 = k.Wall; pen.B2 = k.BitsA(BK.Plain, brick ? k.Mat : BK.Stucco);
                    Box(pen, x, z, y, Math.Min(room, w * (1f + 1.5f * Rnd(ref s))), w, 0.25f + 0.25f * Rnd(ref s), dx, dz, true);
                }
            }
        }
    }
}
