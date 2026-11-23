using System;
using System.Collections.Generic;
using System.IO;

namespace MarsNeedsWater.Earth
{
    public sealed class MeshData
    {
        public readonly List<float> P = new List<float>(), N = new List<float>(), U = new List<float>();
        public readonly List<int> I = new List<int>();
        /// <summary>Two extra sets of four numbers per vertex (texture coordinates 1 and 2), when a mesh needs them (roads).</summary>
        public List<float> X, Y;
        public int Count { get { return P.Count / 3; } }

        public int Add(float x, float y, float z, float nx, float ny, float nz, float u, float v)
        {
            P.Add(x); P.Add(y); P.Add(z); N.Add(nx); N.Add(ny); N.Add(nz); U.Add(u); U.Add(v);
            return P.Count / 3 - 1;
        }

        /// <summary>Add a triangle wound so that it faces the given direction (Unity: clockwise is the front).</summary>
        public void Tri(int a, int b, int c, float nx, float ny, float nz)
        {
            float ax = P[a * 3], ay = P[a * 3 + 1], az = P[a * 3 + 2];
            float ux = P[b * 3] - ax, uy = P[b * 3 + 1] - ay, uz = P[b * 3 + 2] - az;
            float vx = P[c * 3] - ax, vy = P[c * 3 + 1] - ay, vz = P[c * 3 + 2] - az;
            float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            if (cx * nx + cy * ny + cz * nz < 0) { int t = b; b = c; c = t; }
            I.Add(a); I.Add(b); I.Add(c);
        }
    }

    /// <summary>The three Overture archives the world is built from, plus terrain heights.</summary>
    public sealed class TileSources
    {
        public const string Release = "2026-10-21.0";
        public const string BaseUrl = "https://overturemaps-extras-us-west-2.s3.amazonaws.com/tiles/" + Release + "/";
        public const int BuildingsMax = 14, TransportMax = 14, BaseMax = 13;
        public readonly Pmtiles Buildings, Transport, Base;
        public readonly Dem Dem;

        public TileSources(string cacheRoot)
        {
            string root = Path.Combine(cacheRoot, "overture-" + Release);
            Buildings = new Pmtiles(BaseUrl, "buildings", root);
            Transport = new Pmtiles(BaseUrl, "transportation", root);
            Base = new Pmtiles(BaseUrl, "base", root);
            Dem = new Dem(cacheRoot);
        }
    }

    /// <summary>Everything needed to show one square of the world, built off the main thread.</summary>
    public sealed class TileResult
    {
        public int Z, X, Y;
        public double Lat, Lon;
        public float Size;
        public const int Grid = 64;
        public float[] Pos;
        public int Patches;
        public MeshData[] Ground;
        /// <summary>The ground picture, RGBA, north row first; alpha is the GroundKind of each pixel.</summary>
        public byte[] Texture;
        public int TexSize;
        /// <summary>Closest level only: GroundKind per pixel (TexSize squared), and a small RGB copy of the picture (TintN squared).</summary>
        public byte[] Kinds, Tint;
        public const int TintN = 128;
        public MeshData[] Walls;
        public MeshData Roofs, Roads, Trees, Bridges;
        /// <summary>Closest level only: every tree in the square (drawn by EarthTrees).</summary>
        public List<TreeSpot> TreeSpots;
        public List<BridgeRun> BridgeRuns;
        public int BuildingCount, RoadCount, TreeCount;

        /// <summary>Water surface at each grid vertex (tile-local x, y, z), or null when the square has no water.</summary>
        public float[] WPos;
        /// <summary>Per grid cell: 1 where the water surface covers it (a seabed below).</summary>
        public byte[] WCell;
        public MeshData[] Water;
        internal byte[] WKind;      // per grid vertex: 0 dry, 1 sea, 2 lake or river (also set on the dry rim round them)
        /// <summary>The map's water, one byte a pixel (255 = water), MaskN x MaskN, north row first; null when there is none.</summary>
        public byte[] WMask;
        public int MaskN;

        /// <summary>
        /// The top of whatever is at tile coordinates u (east) and v (south), each 0 to 1: the water surface over
        /// seas, lakes and rivers, otherwise the ground. Bridges and landmarks stand on this.
        /// </summary>
        public void Surface(double u, double v, out float x, out float y, out float z)
        {
            Bed(u, v, out x, out y, out z);
            float wy;
            if (WaterAt(u, v, out wy) && wy > y) y = wy;
        }

        /// <summary>Point on the ground mesh itself (the sea floor or lake bed under water).</summary>
        public void Bed(double u, double v, out float x, out float y, out float z) { Interp(Pos, u, v, out x, out y, out z); }

        /// <summary>Which way the ground faces at each grid point (the ground mesh's own normals), three numbers each.</summary>
        internal float[] Nrm;

        /// <summary>Which way the ground faces at u, v, shaded as the ground mesh is.</summary>
        public void Slope(double u, double v, out float nx, out float ny, out float nz)
        {
            if (Nrm == null) { nx = 0; ny = 1; nz = 0; return; }
            Interp(Nrm, u, v, out nx, out ny, out nz);
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (l < 1e-6f) { nx = 0; ny = 1; nz = 0; return; }
            nx /= l; ny /= l; nz /= l;
        }

        /// <summary>Tile-local height of the water surface at u, v, if there is water there.</summary>
        public bool WaterAt(double u, double v, out float y)
        {
            y = 0;
            if (WPos == null) return false;
            int i = (int)Math.Floor(u * Grid), j = (int)Math.Floor(v * Grid);
            if (i < 0) i = 0; else if (i > Grid - 1) i = Grid - 1;
            if (j < 0) j = 0; else if (j > Grid - 1) j = Grid - 1;
            byte cell = WCell[j * Grid + i];
            if (cell == 0) return false;
            float x, z;
            if (cell == WaterMesh.StreamCell)
            {
                // a stream or pond narrower than the grid: water only on the map's own water pixels, lying on the ground
                int px = (int)(u * MaskN), py = (int)(v * MaskN);
                if (px < 0) px = 0; else if (px > MaskN - 1) px = MaskN - 1;
                if (py < 0) py = 0; else if (py > MaskN - 1) py = MaskN - 1;
                if (WMask[py * MaskN + px] == 0) return false;
                Interp(Pos, u, v, out x, out y, out z);
                y += WaterMesh.StreamDepth;
                return true;
            }
            Interp(WPos, u, v, out x, out y, out z);
            return true;
        }

        static void Interp(float[] Pos, double u, double v, out float x, out float y, out float z)
        {
            double gx = u * Grid, gy = v * Grid;
            int i = (int)Math.Floor(gx), j = (int)Math.Floor(gy);
            if (i < 0) i = 0; else if (i > Grid - 1) i = Grid - 1;
            if (j < 0) j = 0; else if (j > Grid - 1) j = Grid - 1;
            float fx = (float)(gx - i), fy = (float)(gy - j);
            int a = (j * (Grid + 1) + i) * 3, b = a + 3, c = a + (Grid + 1) * 3, d = c + 3;
            if (fx + fy <= 1f)
            {
                x = Pos[a] + fx * (Pos[b] - Pos[a]) + fy * (Pos[c] - Pos[a]);
                y = Pos[a + 1] + fx * (Pos[b + 1] - Pos[a + 1]) + fy * (Pos[c + 1] - Pos[a + 1]);
                z = Pos[a + 2] + fx * (Pos[b + 2] - Pos[a + 2]) + fy * (Pos[c + 2] - Pos[a + 2]);
            }
            else
            {
                float gx1 = 1f - fx, gy1 = 1f - fy;
                x = Pos[d] + gx1 * (Pos[c] - Pos[d]) + gy1 * (Pos[b] - Pos[d]);
                y = Pos[d + 1] + gx1 * (Pos[c + 1] - Pos[d + 1]) + gy1 * (Pos[b + 1] - Pos[d + 1]);
                z = Pos[d + 2] + gx1 * (Pos[c + 2] - Pos[d + 2]) + gy1 * (Pos[b + 2] - Pos[d + 2]);
            }
        }

        /// <summary>
        /// The bridge deck over a tile-local x,z, if there is one that someone at height refY could be standing on
        /// (decks well above them do not count, so you can pass underneath).
        /// </summary>
        public bool DeckAt(float lx, float lz, float refY, out float y)
        {
            y = float.MinValue;
            if (BridgeRuns == null) return false;
            bool found = false;
            foreach (BridgeRun r in BridgeRuns)
            {
                float hw = r.HalfWidth + 0.2f;
                if (lx < r.MinX - hw || lx > r.MaxX + hw || lz < r.MinZ - hw || lz > r.MaxZ + hw) continue;
                float[] p = r.P;
                for (int i = 0; i + 5 < p.Length; i += 3)
                {
                    float dx = p[i + 3] - p[i], dz = p[i + 5] - p[i + 2], len2 = dx * dx + dz * dz;
                    if (len2 < 1e-8f) continue;
                    float f = ((lx - p[i]) * dx + (lz - p[i + 2]) * dz) / len2;
                    if (f < -0.02f || f > 1.02f) continue;
                    float ex = lx - (p[i] + dx * f), ez = lz - (p[i + 2] + dz * f);
                    if (ex * ex + ez * ez > hw * hw) continue;
                    float h = p[i + 1] + (p[i + 4] - p[i + 1]) * f;
                    if (refY > h - 1.0f && h > y) { y = h; found = true; }
                }
            }
            return found;
        }

        /// <summary>Height (tile-local y) of the top under a tile-local x,z: the water surface where there is water.</summary>
        public float HeightAt(float lx, float lz)
        {
            float x, y, z;
            Surface(lx / Size + 0.5, 0.5 - lz / Size, out x, out y, out z);
            return y;
        }

        /// <summary>Ground height (tile-local y) under a tile-local x,z, the sea floor or lake bed under water.</summary>
        public float BedAt(float lx, float lz)
        {
            float x, y, z;
            Bed(lx / Size + 0.5, 0.5 - lz / Size, out x, out y, out z);
            return y;
        }

        /// <summary>Closest level only: the GroundKind under a tile-local x,z, and the ground colour there (0..255).</summary>
        public byte KindAt(float lx, float lz)
        {
            if (Kinds == null) return GroundKind.Grass;
            int px = (int)((lx / Size + 0.5) * TexSize), py = (int)((0.5 - lz / Size) * TexSize);
            if (px < 0) px = 0; else if (px > TexSize - 1) px = TexSize - 1;
            if (py < 0) py = 0; else if (py > TexSize - 1) py = TexSize - 1;
            return Kinds[py * TexSize + px];
        }

        public void TintAt(float lx, float lz, out byte r, out byte g, out byte b)
        {
            r = g = b = 128;
            if (Tint == null) return;
            int px = (int)((lx / Size + 0.5) * TintN), py = (int)((0.5 - lz / Size) * TintN);
            if (px < 0) px = 0; else if (px > TintN - 1) px = TintN - 1;
            if (py < 0) py = 0; else if (py > TintN - 1) py = TintN - 1;
            int o = (py * TintN + px) * 3;
            r = Tint[o]; g = Tint[o + 1]; b = Tint[o + 2];
        }

        /// <summary>True when the water nearest a tile-local x,z is a lake or river rather than the sea.</summary>
        public bool LakeAt(float lx, float lz)
        {
            if (WKind == null) return false;
            int i = (int)Math.Round((lx / Size + 0.5) * Grid), j = (int)Math.Round((0.5 - lz / Size) * Grid);
            if (i < 0) i = 0; else if (i > Grid) i = Grid;
            if (j < 0) j = 0; else if (j > Grid) j = Grid;
            return WKind[j * (Grid + 1) + i] != WaterMesh.Sea;
        }

        /// <summary>Water surface height (tile-local y) at a tile-local x,z, if there is water there.</summary>
        public bool WaterHeightAt(float lx, float lz, out float y) { return WaterAt(lx / Size + 0.5, 0.5 - lz / Size, out y); }
    }

    public static class TileBuilder
    {
        public const float RoadScale = 0.3f;     // road width: real metres to game metres (1.5 times true 1:5, so they are walkable)
        const int G = TileResult.Grid;

        struct Map
        {
            public double K, Ox, Oy;
            public Map(int z, int x, int y, int zs, int extent)
            {
                int shift = z - zs;
                K = (double)(1 << shift) / extent;
                Ox = x - ((x >> shift) << shift);
                Oy = y - ((y >> shift) << shift);
            }
            public double U(int c) { return c * K - Ox; }
            public double V(int c) { return c * K - Oy; }
        }

        static uint Hash(string s)
        {
            uint h = 2166136261;
            if (s != null) for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            return h;
        }

        static uint Hash(int a, int b, int c)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        public static TileResult Build(TileSources src, int Z, int X, int Y, int texSize)
        {
            var t = new TileResult { Z = Z, X = X, Y = Y, TexSize = texSize };
            t.Lat = Geo.TileLat(Y + 0.5, Z);
            t.Lon = Geo.TileLon(X + 0.5, Z);
            t.Size = (float)Geo.TileSize(Z, t.Lat);
            var ras = new Raster(texSize, GroundKind.Look(GroundKind.Grass, 0));

            // ---- ground picture ----
            int zb = Math.Min(Z, TileSources.BaseMax);
            var baseLayers = Mvt.Decode(src.Base.GetTile(zb, X >> (Z - zb), Y >> (Z - zb)), "land_cover", "land", "land_use", "water", "infrastructure");
            PaintBase(ras, baseLayers, Z, X, Y, zb, t.Size);

            if (Z >= 12 && Z < 14)
            {
                var bl = Mvt.Decode(src.Buildings.GetTile(Z, X, Y), "building");
                MvtLayer b;
                if (bl.TryGetValue("building", out b))
                {
                    var map = new Map(Z, X, Y, Z, b.Extent);
                    foreach (MvtFeature f in b.Features)
                        if (f.Type == 3) { var rings = Rings(f, map, texSize); if (rings != null) ras.Fill(rings, 0x9a968f, Raster.Built); }
                }
            }

            int zt = Math.Min(Z, TileSources.TransportMax);
            var tl = Mvt.Decode(src.Transport.GetTile(zt, X >> (Z - zt), Y >> (Z - zt)), "segment");
            MvtLayer seg;
            tl.TryGetValue("segment", out seg);
            List<RoadItem> roads = seg != null ? CollectRoads(seg, zt, X >> (Z - zt), Y >> (Z - zt), src) : new List<RoadItem>();
            if (seg != null)
            {
                var map = new Map(Z, X, Y, zt, seg.Extent);
                float px = t.Size / texSize;
                foreach (RoadItem r in roads)
                {
                    if (r.Spec != null && r.Spec.Crosswalk) continue;      // drawn on the street up close only
                    float w = Math.Max(r.Style.MinPx, RoadNet.PaintWidth(r) * RoadScale / px);
                    // close up the road itself is drawn crisply on top (RoadNet): the picture only marks the ground
                    // as paved there, and keeps its colour well inside the road so no blurred edge shows beside it
                    float inset = Z == 14 ? 1.6f : 0f;
                    foreach (int[] part in r.Plain) { var pts = Line(part, map, texSize); if (pts != null) ras.Line(pts, w + (Z == 14 ? 1f : 0f), r.Style.Paint, Raster.Paved, GroundKind.FromMask, inset); }
                    // a bridge is not ground. Close up it is a deck of its own; from afar it is drawn on,
                    // without changing what the ground under it is (so the sea below stays level)
                    if (Z < 14) foreach (int[] part in r.Bridge) { var pts = Line(part, map, texSize); if (pts != null) ras.Line(pts, w, r.Style.Paint, Raster.Keep); }
                }
            }

            // ---- heights ----
            int zd = Z - 2;
            var frame = new Frame(t.Lat, t.Lon);
            t.Pos = new float[(G + 1) * (G + 1) * 3];
            // sample two extra rings so bad readings in the source (spikes of hundreds of metres) can be spotted
            int W = G + 5;
            var raw = new float[W * W];
            for (int j = 0; j < W; j++)
                for (int i = 0; i < W; i++)
                    raw[j * W + i] = src.Dem.Pixel(zd, (long)X * G + i - 2, (long)Y * G + j - 2);
            var win = new float[25];
            const int V = G + 1;
            var hs = new float[V * V];       // real metres: ground, then the sea floor or lake bed
            var lv = new float[V * V];       // real metres: water surface (lakes and rivers)
            var mk = new byte[V * V];
            for (int j = 0; j <= G; j++)
                for (int i = 0; i <= G; i++)
                {
                    int c = (j + 2) * W + i + 2;
                    float h = raw[c];
                    int q = 0;
                    for (int dj = -2; dj <= 2; dj++)
                        for (int di = -2; di <= 2; di++) win[q++] = raw[c + dj * W + di];
                    Array.Sort(win);
                    if (Math.Abs(h - win[12]) > 50f + 3f * (win[18] - win[6])) h = win[12];
                    int k = j * V + i;
                    hs[k] = h; lv[k] = win[12];
                    mk[k] = ras.MaskAt((i + 0.001) / G - 0.0005, (j + 0.001) / G - 0.0005);
                }
            WaterMesh.Shape(t, hs, lv, mk, ras);
            GroundKind.Finish(ras, Z, X, Y, t.Size, hs, G);
            if (Z == 14) { t.Kinds = ras.Kind; t.Tint = GroundKind.Shrink(ras.Rgba, texSize, TileResult.TintN); }
            for (int j = 0; j <= G; j++)
            {
                double lat = Geo.TileLat(Y + (double)j / G, Z);
                for (int i = 0; i <= G; i++)
                {
                    double lon = Geo.TileLon(X + (double)i / G, Z);
                    int k = j * V + i;
                    double x, y, z;
                    frame.ToLocal(lat, lon, Geo.Elevation(hs[k]), out x, out y, out z);
                    int o = k * 3;
                    t.Pos[o] = (float)x; t.Pos[o + 1] = (float)y; t.Pos[o + 2] = (float)z;
                    if (t.WPos != null)
                    {
                        frame.ToLocal(lat, lon, Geo.Elevation(lv[k]), out x, out y, out z);
                        t.WPos[o] = (float)x; t.WPos[o + 1] = (float)y; t.WPos[o + 2] = (float)z;
                    }
                }
            }
            t.Texture = ras.Rgba;
            BuildGround(t);
            WaterMesh.Build(t);

            // ---- things that stand on the ground: only on the closest level ----
            if (Z == 14)
            {
                Buildings.Build(t, src);
                RoadNet.Build(t, roads, seg);
                if (seg != null) Bridges.Build(t, src, roads, seg.Extent, (u, v) => { byte mk = ras.MaskAt(Math.Min(0.9995, Math.Max(0.0005, u)), Math.Min(0.9995, Math.Max(0.0005, v))); return mk == Raster.Ocean || mk == Raster.Water; });
                BuildTrees(t, ras, baseLayers, zb);
            }
            return t;
        }

        // ------------------------------------------------------------ painting

        /// <summary>The same number for a map feature at every zoom level (so its look does not change as finer squares load).</summary>
        static uint FeatureHash(MvtLayer L, MvtFeature f, int kId)
        {
            object id = kId >= 0 ? L.Get(f, kId) : null;
            if (id != null) return Hash(id.ToString());
            return f.Parts.Count > 0 && f.Parts[0].Length >= 2 ? Hash(f.Parts[0].Length, f.Type, 9) : 0u;
        }

        static List<float[]> Rings(MvtFeature f, Map map, int n)
        {
            double minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
            var rings = new List<float[]>(f.Parts.Count);
            foreach (int[] part in f.Parts)
            {
                var r = new float[part.Length];
                for (int i = 0; i < part.Length; i += 2)
                {
                    double x = map.U(part[i]) * n, y = map.V(part[i + 1]) * n;
                    r[i] = (float)x; r[i + 1] = (float)y;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
                rings.Add(r);
            }
            if (maxX < 0 || maxY < 0 || minX > n || minY > n) return null;
            return rings;
        }

        static float[] Line(int[] part, Map map, int n)
        {
            double minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
            var r = new float[part.Length];
            for (int i = 0; i < part.Length; i += 2)
            {
                double x = map.U(part[i]) * n, y = map.V(part[i + 1]) * n;
                r[i] = (float)x; r[i + 1] = (float)y;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            if (maxX < -8 || maxY < -8 || minX > n + 8 || minY > n + 8) return null;
            return r;
        }

        static void PaintBase(Raster ras, Dictionary<string, MvtLayer> layers, int Z, int X, int Y, int zb, float size)
        {
            int n = ras.N;
            float px = size / n;
            MvtLayer L;
            if (layers.TryGetValue("land_cover", out L))
            {
                var map = new Map(Z, X, Y, zb, L.Extent);
                int kSub = L.Key("subtype"), kId = L.Key("id");
                foreach (MvtFeature f in L.Features)
                {
                    if (f.Type != 3) continue;
                    string sub = L.Str(f, kSub);
                    byte kind;
                    if (sub == null || !GroundKind.OfCover(sub, out kind)) continue;
                    uint col = kind == GroundKind.Paved ? Style.LandCover[sub] : GroundKind.Look(kind, FeatureHash(L, f, kId));
                    var rings = Rings(f, map, n);
                    if (rings != null) ras.Fill(rings, col, sub == "forest" || sub == "mangrove" ? Raster.Forest : Raster.Land, kind);
                }
            }
            if (layers.TryGetValue("land", out L))
            {
                var map = new Map(Z, X, Y, zb, L.Extent);
                int kSub = L.Key("subtype"), kId = L.Key("id");
                foreach (MvtFeature f in L.Features)
                {
                    if (f.Type != 3) continue;
                    string sub = L.Str(f, kSub);
                    byte kind;
                    if (sub == null || !GroundKind.OfLand(sub, out kind)) continue;
                    var rings = Rings(f, map, n);
                    if (rings != null) ras.Fill(rings, GroundKind.Look(kind, FeatureHash(L, f, kId)), sub == "forest" ? Raster.Forest : Raster.Land, kind);
                }
            }
            if (layers.TryGetValue("land_use", out L))
            {
                var map = new Map(Z, X, Y, zb, L.Extent);
                int kSub = L.Key("subtype"), kCls = L.Key("class"), kId = L.Key("id");
                var items = new List<KeyValuePair<double, MvtFeature>>();
                foreach (MvtFeature f in L.Features)
                {
                    if (f.Type != 3 || f.Parts.Count == 0) continue;
                    int[] p = f.Parts[0];
                    int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                    for (int i = 0; i < p.Length; i += 2)
                    {
                        if (p[i] < minX) minX = p[i]; if (p[i] > maxX) maxX = p[i];
                        if (p[i + 1] < minY) minY = p[i + 1]; if (p[i + 1] > maxY) maxY = p[i + 1];
                    }
                    items.Add(new KeyValuePair<double, MvtFeature>(-(double)(maxX - minX) * (maxY - minY), f));
                }
                items.Sort((a, b) => a.Key.CompareTo(b.Key));
                foreach (var it in items)
                {
                    MvtFeature f = it.Value;
                    string sub = L.Str(f, kSub), cls = L.Str(f, kCls);
                    uint col;
                    if (sub == null || !Style.LandUse.TryGetValue(sub, out col)) continue;
                    byte mask = Raster.Land;
                    if (sub == "park" || sub == "golf" || sub == "horticulture" || sub == "managed" || sub == "cemetery") mask = Raster.Green;
                    if (sub == "recreation") { if (cls == "pitch" || cls == "track" || cls == "playground") mask = Raster.Paved; else mask = Raster.Green; }
                    if (sub == "agriculture" && (cls == "orchard" || cls == "vineyard")) mask = Raster.Green;
                    byte kind = GroundKind.OfUse(sub, cls, mask);
                    if (kind != GroundKind.Paved) col = GroundKind.UseLook(sub, cls, kind, FeatureHash(L, f, kId));
                    var rings = Rings(f, map, n);
                    if (rings != null) ras.Fill(rings, col, mask, kind);
                }
            }
            if (layers.TryGetValue("water", out L))
            {
                var map = new Map(Z, X, Y, zb, L.Extent);
                int kSub = L.Key("subtype"), kCls = L.Key("class");
                foreach (MvtFeature f in L.Features)
                {
                    string sub = L.Str(f, kSub);
                    if (sub == "physical") continue;
                    if (f.Type == 3)
                    {
                        var rings = Rings(f, map, n);
                        if (rings != null) ras.Fill(rings, WaterMesh.BedColour, sub == "ocean" ? Raster.Ocean : Raster.Water);
                    }
                    else if (f.Type == 2)
                    {
                        float w = Math.Max(1f, Style.WaterLineWidth(L.Str(f, kCls)) / (float)Geo.Scale / px);
                        foreach (int[] part in f.Parts) { var pts = Line(part, map, n); if (pts != null) ras.Line(pts, w, WaterMesh.BedColour, Raster.Water); }
                    }
                }
            }
            if (layers.TryGetValue("infrastructure", out L))
            {
                var map = new Map(Z, X, Y, zb, L.Extent);
                int kSub = L.Key("subtype"), kCls = L.Key("class");
                foreach (MvtFeature f in L.Features)
                {
                    if (f.Type == 1) continue;
                    string sub = L.Str(f, kSub), cls = L.Str(f, kCls);
                    uint col; float real;
                    if (sub == "pier") { col = 0xb8a07a; real = 5f; }
                    else if (sub == "bridge") continue;      // not ground: painting it stopped the sea under it being levelled
                    else if (sub == "airport" && cls == "runway") { col = 0x5f6166; real = 45f; }
                    else if (sub == "airport" && cls == "taxiway") { col = 0x777a80; real = 18f; }
                    else if (sub == "airport" && (cls == "apron" || cls == "helipad")) { col = 0xa3a5a8; real = 10f; }
                    else continue;
                    if (f.Type == 3) { var rings = Rings(f, map, n); if (rings != null) ras.Fill(rings, col, Raster.Paved); }
                    else
                    {
                        float w = Math.Max(1f, real / (float)Geo.Scale / px);
                        foreach (int[] part in f.Parts) { var pts = Line(part, map, n); if (pts != null) ras.Line(pts, w, col, Raster.Paved); }
                    }
                }
            }
        }

        // ------------------------------------------------------------ ground

        static void BuildGround(TileResult t)
        {
            int P = t.Z == 14 ? 1 : 4;
            t.Patches = P;
            t.Ground = new MeshData[P * P];
            int c = G / P;
            float skirt = t.Size * 0.03f;
            var nrm = new float[(G + 1) * (G + 1) * 3];
            for (int j = 0; j <= G; j++)
                for (int i = 0; i <= G; i++)
                {
                    int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, G), j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, G);
                    int a = (j * (G + 1) + i0) * 3, b = (j * (G + 1) + i1) * 3, cc = (j0 * (G + 1) + i) * 3, d = (j1 * (G + 1) + i) * 3;
                    float ex = t.Pos[b] - t.Pos[a], ey = t.Pos[b + 1] - t.Pos[a + 1], ez = t.Pos[b + 2] - t.Pos[a + 2];      // east
                    float sx = t.Pos[cc] - t.Pos[d], sy = t.Pos[cc + 1] - t.Pos[d + 1], sz = t.Pos[cc + 2] - t.Pos[d + 2];  // north
                    float nx = sy * ez - sz * ey, ny = sz * ex - sx * ez, nz = sx * ey - sy * ex;
                    float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len < 1e-9f) { nx = 0; ny = 1; nz = 0; len = 1; }
                    if (ny < 0) { nx = -nx; ny = -ny; nz = -nz; }
                    int o = (j * (G + 1) + i) * 3;
                    nrm[o] = nx / len; nrm[o + 1] = ny / len; nrm[o + 2] = nz / len;
                }
            t.Nrm = nrm;
            for (int pj = 0; pj < P; pj++)
                for (int pi = 0; pi < P; pi++)
                {
                    var m = new MeshData();
                    int w = c + 1;
                    for (int j = 0; j <= c; j++)
                        for (int i = 0; i <= c; i++)
                        {
                            int gi = pi * c + i, gj = pj * c + j, o = (gj * (G + 1) + gi) * 3;
                            m.Add(t.Pos[o], t.Pos[o + 1], t.Pos[o + 2], nrm[o], nrm[o + 1], nrm[o + 2], (float)gi / G, (float)gj / G);
                        }
                    for (int j = 0; j < c; j++)
                        for (int i = 0; i < c; i++)
                        {
                            int a = j * w + i, b = a + 1, cc = a + w, d = cc + 1;
                            m.I.Add(a); m.I.Add(b); m.I.Add(cc);
                            m.I.Add(b); m.I.Add(d); m.I.Add(cc);
                        }
                    // a skirt round the edge hides hairline gaps between neighbours and between detail levels
                    for (int side = 0; side < 4; side++)
                    {
                        int prev = -1, prevLow = -1;
                        for (int k = 0; k <= c; k++)
                        {
                            int i = side == 0 ? k : side == 1 ? c : side == 2 ? c - k : 0;
                            int j = side == 0 ? 0 : side == 1 ? k : side == 2 ? c : c - k;
                            int top = j * w + i;
                            int low = m.Add(m.P[top * 3], m.P[top * 3 + 1] - skirt, m.P[top * 3 + 2], m.N[top * 3], m.N[top * 3 + 1], m.N[top * 3 + 2], m.U[top * 2], m.U[top * 2 + 1]);
                            if (prev >= 0)
                            {
                                m.I.Add(prev); m.I.Add(top); m.I.Add(prevLow);
                                m.I.Add(top); m.I.Add(low); m.I.Add(prevLow);
                                m.I.Add(top); m.I.Add(prev); m.I.Add(prevLow);
                                m.I.Add(low); m.I.Add(top); m.I.Add(prevLow);
                            }
                            prev = top; prevLow = low;
                        }
                    }
                    t.Ground[pj * P + pi] = m;
                }
        }

        // ------------------------------------------------------------ roads and rail

        internal sealed class RoadItem { public MvtFeature F; public Style.Road Style; public RoadNet.Spec Spec; public List<int[]> Plain = new List<int[]>(), Bridge = new List<int[]>(); }

        /// <summary>
        /// Cut lines (tile whole-number coordinates) where they enter and leave a zone on the globe.
        /// Returns the pieces outside it; the pieces inside go to "inside" if that is given.
        /// </summary>
        static List<int[]> CutBy(List<int[]> parts, double extent, int tz, int tx, int ty, Func<double, double, bool> zone, List<int[]> inside)
        {
            var result = new List<int[]>();
            var run = new List<int>();
            double metres = Geo.TileSize(tz, Geo.TileLat(ty + 0.5, tz)) * Geo.Scale / extent;   // real metres per unit
            foreach (int[] part in parts)
            {
                run.Clear();
                bool runIn = false;
                for (int i = 0; i + 3 < part.Length; i += 2)
                {
                    double ax = part[i], ay = part[i + 1], bx = part[i + 2], by = part[i + 3];
                    int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) * metres / 12.0));
                    for (int k = 0; k < steps; k++)
                    {
                        double u0 = (double)k / steps, u1 = (double)(k + 1) / steps, um = (u0 + u1) / 2;
                        bool on = zone(Geo.TileLat(ty + (ay + (by - ay) * um) / extent, tz), Geo.TileLon(tx + (ax + (bx - ax) * um) / extent, tz));
                        if (on != runIn && run.Count > 0)
                        {
                            if (run.Count >= 4) { if (!runIn) result.Add(run.ToArray()); else if (inside != null) inside.Add(run.ToArray()); }
                            run.Clear();
                        }
                        runIn = on;
                        if (run.Count == 0) { run.Add((int)Math.Round(ax + (bx - ax) * u0)); run.Add((int)Math.Round(ay + (by - ay) * u0)); }
                        run.Add((int)Math.Round(ax + (bx - ax) * u1)); run.Add((int)Math.Round(ay + (by - ay) * u1));
                    }
                }
                if (run.Count >= 4) { if (!runIn) result.Add(run.ToArray()); else if (inside != null) inside.Add(run.ToArray()); }
            }
            return result;
        }

        internal static List<RoadItem> CollectRoads(MvtLayer L, int tz, int tx, int ty, TileSources src = null)
        {
            var list = new List<RoadItem>();
            float deckTop;
            bool nearDeck = Landmarks.Deck(Geo.TileLat(ty + 0.5, tz), Geo.TileLon(tx + 0.5, tz), Geo.TileSize(tz, Geo.TileLat(ty + 0.5, tz)) * Geo.Scale, out deckTop);
            int kSub = L.Key("subtype"), kCls = L.Key("class"), kRoad = L.Key("road_flags"), kRail = L.Key("rail_flags"), kId = L.Key("id");
            foreach (MvtFeature f in L.Features)
            {
                if (f.Type != 2) continue;
                string sub = L.Str(f, kSub), cls = L.Str(f, kCls) ?? "unknown";
                Style.Road st;
                if (sub == "road") { if (!Style.Roads.TryGetValue(cls, out st)) st = Style.Roads["unknown"]; }
                else if (sub == "rail") { if (cls == "subway") continue; if (!Style.Rails.TryGetValue(cls, out st)) st = Style.Rails["unknown"]; }
                else continue;
                string flags = L.Str(f, sub == "road" ? kRoad : kRail);
                if (flags != null && flags.Contains("is_tunnel") && !flags.Contains("between")) continue;
                // a landmark bridge carries its own road, so the flat map road is cut out under its deck
                List<int[]> parts = f.Parts;
                if (nearDeck) { float top; parts = CutBy(parts, L.Extent, tz, tx, ty, (la, lo) => Landmarks.Deck(la, lo, 6, out top), null); }
                var item = new RoadItem { F = f, Style = st };
                Bridges.Split(parts, flags, L.Extent, item.Plain, item.Bridge, tz == 14 ? src : null, L.Str(f, kId), tx, ty);
                // and the road running up to it is raised to meet the deck, like any other bridge
                if (nearDeck && sub == "road" && st.RealWidth >= 5f) item.Plain = CutBy(item.Plain, L.Extent, tz, tx, ty, (la, lo) => Landmarks.Landing(la, lo, 220), item.Bridge);
                list.Add(item);
            }
            RoadNet.Prepare(list, L, tz, tx, ty);     // what each road is made of, its width and its lines
            list.Sort((a, b) => a.Style.Layer.CompareTo(b.Style.Layer));
            return list;
        }

        internal static bool ClipT(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0) return q >= 0;
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }

        // ------------------------------------------------------------ trees

        static void BuildTrees(TileResult t, Raster ras, Dictionary<string, MvtLayer> baseLayers, int zb)
        {
            t.Trees = new MeshData();
            t.TreeSpots = new List<TreeSpot>();
            const int Max = 7000;
            MvtLayer L;
            if (baseLayers.TryGetValue("land", out L))
            {
                var map = new Map(t.Z, t.X, t.Y, zb, L.Extent);
                int kCls = L.Key("class");
                foreach (MvtFeature f in L.Features)
                {
                    if (f.Type != 1 || t.TreeCount >= Max) continue;
                    if (L.Str(f, kCls) != "tree") continue;
                    foreach (int[] p in f.Parts)
                    {
                        double u = map.U(p[0]), v = map.V(p[1]);
                        if (u < 0 || v < 0 || u >= 1 || v >= 1) continue;
                        byte mk = ras.MaskAt(u, v);
                        if (mk == Raster.Ocean || mk == Raster.Water) continue;
                        AddTree(t, u, v, Hash(p[0], p[1], 7), true);
                    }
                }
            }
            double step = 7.0 / t.Size;
            int cells = (int)(1.0 / step);
            for (int j = 0; j < cells && t.TreeCount < Max; j++)
                for (int i = 0; i < cells && t.TreeCount < Max; i++)
                {
                    uint h = Hash(t.X * 131 + i, t.Y * 137 + j, 3);
                    double u = (i + (h & 255) / 255.0) * step, v = (j + ((h >> 8) & 255) / 255.0) * step;
                    byte mk = ras.MaskAt(u, v);
                    // woods; clumps of trees in parks; lone trees and copses in fields, gardens and savanna
                    float chance = TreeSpecies.Chance(mk, TreeSpecies.KindAt(ras, u, v), (t.X + u) * t.Size, (t.Y + v) * t.Size);
                    if (chance <= 0 || ((h >> 16) & 0xffff) / 65536f >= chance) continue;
                    if (mk != Raster.Forest && !Clear(ras, u, v, 2.5 / t.Size)) continue;
                    AddTree(t, u, v, h, false);
                }
        }

        /// <summary>Nothing built or paved within r (tile units) of u, v.</summary>
        static bool Clear(Raster ras, double u, double v, double r)
        {
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                double uu = Math.Min(0.9995, Math.Max(0.0005, u + Math.Cos(a) * r)), vv = Math.Min(0.9995, Math.Max(0.0005, v + Math.Sin(a) * r));
                byte m = ras.MaskAt(uu, vv);
                if (m == Raster.Built || m == Raster.Paved || m == Raster.Water || m == Raster.Ocean) return false;
            }
            return true;
        }

        /// <summary>A tree at u, v: its kind, size and colour come from where it stands (TreeSpecies).</summary>
        static void AddTree(TileResult t, double u, double v, uint h, bool street)
        {
            float x, y, z;
            t.Surface(u, v, out x, out y, out z);
            TreeSpot s;
            if (!TreeSpecies.Make(t.Lat, t.Lon, x, y, z, t.KindAt(x, z), street, h, out s)) return;
            t.TreeSpots.Add(s);
            t.TreeCount++;
        }
    }
}
