using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// A flat local frame on the planet's surface (x east, y up, z north, optionally turned by a yaw), for building
    /// towns and landmarks in ordinary metres. <see cref="Ground"/> finds the real ground under any point.
    /// </summary>
    public sealed class SurfaceSpot
    {
        readonly Planet _planet;
        public readonly Vector3 Dir, Origin, East, North;
        public readonly Quaternion Rot;
        public readonly float BaseH;

        public SurfaceSpot(Planet planet, Vector3 dir, float yaw, float baseH)
        {
            _planet = planet;
            Dir = dir;
            BaseH = baseH;
            Planet.TangentAt(dir, out var e, out var n);
            Rot = Quaternion.LookRotation(n, dir) * Quaternion.Euler(0f, yaw, 0f);
            East = Rot * Vector3.right;
            North = Rot * Vector3.forward;
            Origin = dir * (Planet.Radius + baseH);
        }

        public Vector3 DirAt(float x, float z) => (Dir * Planet.Radius + East * x + North * z).normalized;

        /// <summary>The ground under local (x, z), in local coordinates; h is its height above sea level.</summary>
        public Vector3 Ground(float x, float z, out float h)
        {
            var d = DirAt(x, z);
            h = _planet.Height(d);
            return Quaternion.Inverse(Rot) * (d * (Planet.Radius + h) - Origin);
        }

        public GameObject Make(string name, Mesh mesh, Material mat, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_planet.transform, false);
            go.transform.localPosition = Origin;
            go.transform.localRotation = Rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }

    /// <summary>
    /// Real towns and cities (from <see cref="Places"/>), built when the player comes within a few kilometres,
    /// bigger cities from farther away. Each town is cut into tiles of 4 × 4 blocks. Far tiles are simple
    /// coloured blocks (one cheap mesh each); tiles near the player swap to Kenney's modelled buildings (CC0, see
    /// <see cref="Kit"/>) with windows, doors and awnings, plus trucks on the streets and a fountain and statue on
    /// the main square. Glass towers still rise downtown in the biggest cities. Every block draws its random
    /// numbers in the same order for both versions, so near and far always agree. A collider only on tiles close
    /// to the player. Presentation only.
    /// </summary>
    public sealed class Cities : MonoBehaviour
    {
        const float Block = 15f, StreetW = 6f, Pitch = Block + StreetW;
        const int TileBlocks = 4;
        // Swap far blocks for modelled buildings well out in the haze, so the change is hard to spot; towns under
        // 150,000 people are small enough to be modelled whole from the start and never swap at all.
        const float DetailIn = 420f, DetailOut = 540f, ColliderIn = 140f;

        sealed class Tile
        {
            public int Tx, Tz;
            public bool Fixed;   // built detailed once, never swapped
            public Vector3 Dir;
            public GameObject Simple, Detail;
            public Mesh SimpleMesh, DetailMesh;
            public MeshCollider Col;
        }

        sealed class Town
        {
            public int Index;
            public Places.Place P;
            public SurfaceSpot Spot;
            public float Radius;
            public Style Look;
            public readonly List<Tile> Tiles = new List<Tile>();
        }

        Planet _planet;
        Material _mat;
        readonly Dictionary<int, Town> _live = new Dictionary<int, Town>();
        readonly List<int> _drop = new List<int>();
        readonly List<(Town town, Tile tile, float d)> _want = new List<(Town, Tile, float)>();
        float _timer;
        int _next = -1;

        static readonly Color[] Glass = { new Color(0.45f, 0.68f, 0.9f), new Color(0.38f, 0.58f, 0.75f), new Color(0.55f, 0.75f, 0.85f), new Color(0.3f, 0.45f, 0.62f) };
        static readonly Color Trim = new Color(0.92f, 0.93f, 0.95f), FlatRoof = new Color(0.55f, 0.56f, 0.6f), Pavement = new Color(0.7f, 0.7f, 0.72f);
        static readonly Color Shopfront = new Color(0.28f, 0.3f, 0.36f), RoofKit = new Color(0.72f, 0.73f, 0.76f);
        static readonly Color Plaza = new Color(0.82f, 0.78f, 0.7f);
        // Gentle tints so five Kenney buildings don't make a whole city the same purple and green.
        static readonly Color[] Tints =
        {
            Color.white, new Color(1f, 0.9f, 0.78f), new Color(0.95f, 0.78f, 0.7f), new Color(0.82f, 0.95f, 0.88f),
            new Color(0.82f, 0.9f, 1f), new Color(1f, 0.97f, 0.88f), new Color(0.9f, 0.88f, 0.86f),
        };

        // Kenney buildings: small towns get the low ones, city blocks the tall ones.
        static readonly string[] LowHouses = { "building-small-a", "building-small-d", "building-small-a", "building-small-d", "building-garage" };
        static readonly string[] CityHouses = { "building-small-b", "building-small-c", "building-small-a", "building-small-d" };
        /// <summary>How a town is built, from where in the world it is.</summary>
        public enum Style { West, Adobe, Asia, Hut, Nordic }

        // Our own regional houses (tools/art/towns.py). Walls are near-white, so the tints below colour only the walls.
        static readonly string[][] StyleLow =
        {
            LowHouses,
            new[] { "house-adobe-a", "house-adobe-b", "house-adobe-c", "house-adobe-a" },
            new[] { "house-asia-a", "house-asia-a", "house-asia-b", "house-asia-c" },
            new[] { "house-hut-a", "house-hut-b", "house-hut-c", "house-hut-a" },
            new[] { "house-nordic-a", "house-nordic-a", "house-nordic-b" },
        };
        static readonly string[][] StyleCity =
        {
            CityHouses,
            new[] { "house-adobe-b", "building-small-b", "house-adobe-a", "building-small-c" },
            new[] { "house-asia-b", "building-small-b", "house-asia-b", "building-small-c", "house-asia-c" },
            CityHouses,
            new[] { "house-nordic-a", "building-small-b", "building-small-c", "building-small-a" },
        };
        static readonly string[] StyleCentre = { "centre-church", "centre-mosque", "centre-pagoda", null, "centre-church" };
        static readonly Color[][] StyleTints =
        {
            Tints,
            new[] { Color.white, new Color(0.96f, 0.88f, 0.72f), new Color(0.9f, 0.76f, 0.58f), new Color(0.98f, 0.93f, 0.84f), new Color(0.93f, 0.8f, 0.72f), new Color(0.8f, 0.9f, 0.95f) },
            new[] { Color.white, new Color(0.93f, 0.92f, 0.88f), new Color(0.88f, 0.86f, 0.8f), new Color(0.97f, 0.9f, 0.8f) },
            new[] { Color.white, new Color(0.95f, 0.9f, 0.85f), new Color(0.9f, 0.82f, 0.75f) },
            new[] { new Color(0.72f, 0.24f, 0.2f), new Color(0.93f, 0.78f, 0.36f), Color.white, new Color(0.45f, 0.58f, 0.68f), new Color(0.72f, 0.24f, 0.2f), new Color(0.35f, 0.5f, 0.42f) },
        };

        /// <summary>The building style of a town at this latitude and longitude with this many people.</summary>
        public static Style StyleFor(float lat, float lon, float pop)
        {
            if (lat > 55f || lat < -50f) return Style.Nordic;
            if (lon > 95f && lon < 150f && lat > -10f && lat < 50f) return Style.Asia;
            bool northAfrica = lat > 12f && lon > -18f && lon < 35f && lat < (lon < 12f ? 35.9f : lon < 25f ? 34f : 35f);
            bool middleEast = lon >= 35f && lon < 63f && lat > 12f && lat < 38f;
            bool southAsia = lon >= 60f && lon <= 95f && lat > 5f && lat < 37f;
            if (northAfrica || middleEast || southAsia) return Style.Adobe;
            if (lat > -35f && lat <= 12f && lon > -18f && lon < 52f && pop < 150000f) return Style.Hut;
            return Style.West;
        }

        static readonly string[] Trucks = { "vehicle-truck-red", "vehicle-truck-yellow" };
        // Average wall colour of each model, for its far-away block (filled in on first use).
        static readonly Dictionary<string, Color> _avg = new Dictionary<string, Color>();

        public void Init(Planet planet)
        {
            _planet = planet;
            _mat = Prims.VertexColored(0.3f, false, 0.2f);
        }

        /// <summary>How far away a town is built: villages from 1 km, megacities from 2.6 km.</summary>
        static float Range(Places.Place p) => Mathf.Min(2600f, 700f + Mathf.Sqrt(p.Pop) * 1.2f);

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            var pd = _planet.WorldToDir(_planet.Player.position);
            // At most one new town, or one detailed tile, per frame.
            if (_next >= 0) { Build(_next); _next = -1; }
            else if (_want.Count > 0)
            {
                var w = _want[_want.Count - 1];
                _want.RemoveAt(_want.Count - 1);
                if (w.tile.Detail == null && _live.ContainsKey(w.town.Index)) BuildDetail(w.town, w.tile);
            }
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.3f;

            float best = float.MaxValue;
            var all = Places.All;
            for (int i = 0; i < all.Count; i++)
            {
                float d = (all[i].Dir - pd).magnitude * Planet.Radius;
                if (!_live.ContainsKey(i) && d < Range(all[i]) && d < best && all[i].Radius > 0f && !CityReplica.IsTown(i)) { best = d; _next = i; }
            }
            _drop.Clear();
            _want.Clear();
            foreach (var kv in _live)
            {
                var t = kv.Value;
                float dt = (t.P.Dir - pd).magnitude * Planet.Radius;
                if (dt > Range(t.P) + 400f) { _drop.Add(kv.Key); continue; }
                if (dt > t.Radius + DetailOut + 100f)
                {
                    foreach (var tile in t.Tiles) if (!tile.Fixed && (tile.Detail != null || tile.Col != null)) Simplify(tile);
                    else if (tile.Fixed && tile.Col != null) { Destroy(tile.Col); tile.Col = null; }
                    continue;
                }
                foreach (var tile in t.Tiles)
                {
                    float d = (tile.Dir - pd).magnitude * Planet.Radius;
                    if (!tile.Fixed && tile.Detail == null && d < DetailIn) _want.Add((t, tile, d));
                    else if (!tile.Fixed && tile.Detail != null && d > DetailOut) Simplify(tile);
                    // Collider on whichever version is showing, only close by.
                    var shown = tile.Detail != null ? tile.DetailMesh : tile.SimpleMesh;
                    var go = tile.Detail != null ? tile.Detail : tile.Simple;
                    if (d < ColliderIn && tile.Col == null && go != null && shown != null) { tile.Col = go.AddComponent<MeshCollider>(); tile.Col.sharedMesh = shown; }
                    else if (d >= ColliderIn && tile.Col != null) { Destroy(tile.Col); tile.Col = null; }
                }
            }
            _want.Sort((a, b) => b.d.CompareTo(a.d)); // nearest last (popped first)
            foreach (int i in _drop) { Clear(_live[i]); _live.Remove(i); }
        }

        void Simplify(Tile tile)
        {
            if (tile.Col != null) { Destroy(tile.Col); tile.Col = null; }
            if (tile.Detail != null) Destroy(tile.Detail);
            if (tile.DetailMesh != null) Destroy(tile.DetailMesh);
            tile.Detail = null; tile.DetailMesh = null;
            if (tile.Simple != null) tile.Simple.SetActive(true);
        }

        void Clear(Town t)
        {
            foreach (var tile in t.Tiles)
            {
                if (tile.Simple != null) Destroy(tile.Simple);
                if (tile.Detail != null) Destroy(tile.Detail);
                if (tile.SimpleMesh != null) Destroy(tile.SimpleMesh);
                if (tile.DetailMesh != null) Destroy(tile.DetailMesh);
            }
            t.Tiles.Clear();
        }

        bool Blocked(Vector3 d)
        {
            if (Vector3.Dot(d, _planet.FarmDir) > 0.9998f && _planet.FarmCheb(d) < Planet.FarmHalf + 25f) return true; // the farm
            float cos = Mathf.Cos(Landmarks.Clearing / Planet.Radius);
            foreach (var l in Landmarks.All) if (Vector3.Dot(d, l.Dir) > cos) return true;
            return false;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        void Build(int index)
        {
            var p = Places.All[index];
            var rng = new System.Random(index * 7919 + 17);
            float h0 = _planet.Height(p.Dir);
            var town = new Town { Index = index, P = p, Radius = p.Radius, Look = StyleFor(p.Lat, p.Lon, p.Pop), Spot = new SurfaceSpot(_planet, p.Dir, (float)rng.NextDouble() * 90f, h0) };
            int cells = Mathf.CeilToInt(p.Radius / Pitch);
            bool whole = p.Pop <= 150000f; // small town: modelled from the start, no swapping
            int t0 = FloorDiv(-cells, TileBlocks), t1 = FloorDiv(cells, TileBlocks);
            for (int tz = t0; tz <= t1; tz++)
            for (int tx = t0; tx <= t1; tx++)
            {
                var mb = new MeshBuilder();
                Emit(town, tx, tz, mb, whole);
                var mesh = mb.ToMesh($"Town_{p.Name}_{tx}_{tz}");
                if (mesh == null) continue;
                float cx = (tx * TileBlocks + (TileBlocks - 1) * 0.5f) * Pitch, cz = (tz * TileBlocks + (TileBlocks - 1) * 0.5f) * Pitch;
                var tile = new Tile { Tx = tx, Tz = tz, Fixed = whole, Dir = town.Spot.DirAt(cx, cz), SimpleMesh = mesh };
                tile.Simple = town.Spot.Make(mesh.name, mesh, _mat, true);
                town.Tiles.Add(tile);
            }
            _live[index] = town;
        }

        void BuildDetail(Town town, Tile tile)
        {
            var mb = new MeshBuilder();
            Emit(town, tile.Tx, tile.Tz, mb, true);
            var mesh = mb.ToMesh($"TownDetail_{town.P.Name}_{tile.Tx}_{tile.Tz}");
            if (mesh == null) return;
            tile.DetailMesh = mesh;
            tile.Detail = town.Spot.Make(mesh.name, mesh, _mat, true);
            if (tile.Col != null) { Destroy(tile.Col); tile.Col = null; }
            if (tile.Simple != null) tile.Simple.SetActive(false);
        }

        /// <summary>
        /// Every block of one tile, as simple blocks (far) or Kenney models (near). Both versions draw the same
        /// random numbers in the same order.
        /// </summary>
        void Emit(Town town, int tx, int tz, MeshBuilder mb, bool detail)
        {
            var p = town.P;
            var spot = town.Spot;
            float radius = p.Radius;
            bool big = p.Pop > 2.5e6f, city = p.Pop > 150000f;
            int cells = Mathf.CeilToInt(radius / Pitch);
            for (int gz = tz * TileBlocks; gz < (tz + 1) * TileBlocks; gz++)
            for (int gx = tx * TileBlocks; gx < (tx + 1) * TileBlocks; gx++)
            {
                if (gx < -cells || gx > cells || gz < -cells || gz > cells) continue;
                float bx = gx * Pitch, bz = gz * Pitch;
                float dist = new Vector2(bx, bz).magnitude;
                if (dist > radius) continue;
                var rng = new System.Random(town.Index * 7919 ^ (gx * 73856093) ^ (gz * 19349663));
                float R() => (float)rng.NextDouble();
                float core = 1f - dist / radius;                    // 1 downtown .. 0 at the edge
                if (R() > 0.45f + core * 0.6f && !(gx == 0 && gz == 0)) continue; // gaps on the outskirts
                var g0 = spot.Ground(bx, bz, out float hb);
                if (hb < Planet.WaterLevel(spot.DirAt(bx, bz)) + 0.4f || Blocked(spot.DirAt(bx, bz))) continue;
                // A road through the block: no pavement slab over it, and (below) no houses on it.
                bool roadHere = Roads.Near(spot.DirAt(bx, bz), 10f);
                if (!roadHere) mb.Box(Matrix4x4.TRS(g0 + new Vector3(0f, -0.9f, 0f), Quaternion.identity, new Vector3(Block + 1.5f, 2f, Block + 1.5f)), Pavement, Pavement);

                if (gx == 0 && gz == 0 && p.Pop > 20000f && !roadHere)
                {
                    // The main square: paving, a fountain and a statue.
                    mb.Box(Matrix4x4.TRS(g0 + new Vector3(0f, 0.12f, 0f), Quaternion.identity, new Vector3(Block, 0.1f, Block)), Plaza, Plaza);
                    // Half the squares get the region's own centre piece (church, mosque, pagoda) instead of a fountain.
                    string centreName = StyleCentre[(int)town.Look];
                    bool west = town.Look == Style.West || town.Look == Style.Nordic;
                    if (west && (p.Lon > 60f || town.Index % 2 == 1)) centreName = null;
                    var centre = centreName != null ? Kit.Get(centreName) : null;
                    if (centre != null)
                    {
                        if (detail) mb.Model(Matrix4x4.TRS(g0 + new Vector3(0f, 0.05f, 0f), Quaternion.identity, Vector3.one * (Block * 0.9f)), centre);
                        else Block3(mb, g0, 0f, 8f, centre.Size.y * Block * 0.55f, 8f, new Color(0.93f, 0.9f, 0.84f), RoofKit);
                        continue;
                    }
                    if (detail)
                    {
                        mb.Model(Matrix4x4.TRS(g0 + new Vector3(0f, 0.05f, 0f), Quaternion.identity, Vector3.one * 9f), Kit.Get("pavement-fountain"));
                        mb.Model(Matrix4x4.TRS(g0 + new Vector3(5.5f, 0.1f, 5.5f), Quaternion.Euler(0f, 225f, 0f), Vector3.one * 2.4f), Kit.Get("statue"));
                        mb.Model(Matrix4x4.TRS(g0 + new Vector3(-6f, 0.1f, 6f), Quaternion.identity, Vector3.one * 3f), Kit.Get("flag"));
                    }
                    else Block3(mb, g0, 0f, 3f, 2f, 3f, RoofKit, Glass[0]);
                    continue;
                }

                for (int lz = 0; lz < 2; lz++)
                for (int lx = 0; lx < 2; lx++)
                {
                    // All of this lot's random numbers first, so both versions stay in step.
                    float rSkip = R(), rKind = R(), rModel = R(), rFace = R(), rH = R(), rG = R();
                    if (rSkip < 0.12f) continue;
                    float x = bx + (lx - 0.5f) * Block * 0.5f, z = bz + (lz - 0.5f) * Block * 0.5f;
                    var d = spot.DirAt(x, z);
                    if (Blocked(d) || (roadHere && Roads.Near(d, 5f)) || Hubs.Near(d, 6f) || Rivers.Near(d, 3f)) continue;
                    var g = spot.Ground(x, z, out float hl);
                    if (hl < Planet.WaterLevel(d) + 0.4f) continue; // never in the sea or a lake
                    float lot = Block * 0.5f - 0.8f;
                    if (big && core > 0.55f && rKind < 0.75f)
                    {
                        Tower(mb, g, lot * (0.7f + rG * 0.3f), lot * (0.7f + rH * 0.3f), 22f + core * core * 70f * (0.5f + rH), rG, rModel);
                        continue;
                    }
                    int look = (int)town.Look;
                    var names = city && core > 0.25f ? StyleCity[look] : StyleLow[look];
                    string name = names[(int)(rModel * names.Length) % names.Length];
                    var k = Kit.Get(name);
                    bool own = name.StartsWith("house-");
                    if (k == null && own)
                    {
                        // The regional model is missing: fall back to the plain town houses.
                        names = city && core > 0.25f ? CityHouses : LowHouses;
                        name = names[(int)(rModel * names.Length) % names.Length];
                        k = Kit.Get(name); own = false; look = 0;
                    }
                    // Face the street: the lot's outer side along x or z.
                    float yaw = rFace < 0.5f ? (lx == 0 ? -90f : 90f) : (lz == 0 ? 180f : 0f);
                    float sc = Block * 0.5f;
                    float tall = city && core > 0.25f ? 1f + core * 0.5f * rH : 1f;   // a little taller downtown
                    var at = g + new Vector3(0f, -0.25f, 0f);
                    var tints = own ? StyleTints[look] : Tints;
                    var tint = tints[(int)(rG * tints.Length) % tints.Length];
                    if (own) tall = Mathf.Min(tall, 1.15f);
                    if (detail && k != null)
                        mb.Model(Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), new Vector3(sc, sc * tall, sc)), k, tint, own);
                    else
                    {
                        // Far away: one block of the building's own colour and height.
                        float hgt = (k != null ? k.Size.y : 1f) * sc * tall;
                        Block3(mb, at, -1.5f, sc * 0.86f, hgt + 1.3f, sc * 0.86f, AvgColour(name, k) * tint, FlatRoof);
                    }
                }

                // Now and then a parked truck on the street beside the block.
                float rTruck = R(), rTp = R(), rTc = R();
                if (detail && rTruck < (city ? 0.35f : 0.2f))
                {
                    bool alongZ = rTp < 0.5f;
                    float off = (rTp * 2f % 1f - 0.5f) * Block * 0.8f;
                    float sx = alongZ ? bx + Block / 2f + StreetW * 0.3f : bx + off, sz = alongZ ? bz + off : bz + Block / 2f + StreetW * 0.3f;
                    var gt = spot.Ground(sx, sz, out float ht);
                    if (ht >= Planet.WaterLevel(spot.DirAt(sx, sz)) + 0.4f && !(roadHere && Roads.Near(spot.DirAt(sx, sz), 2f)))
                        mb.Model(Matrix4x4.TRS(gt, Quaternion.Euler(0f, alongZ ? (rTc < 0.5f ? 0f : 180f) : (rTc < 0.5f ? 90f : -90f), 0f), Vector3.one * 1.7f),
                                 Kit.Get(Trucks[rTc < 0.5f ? 0 : 1]));
                }
            }
        }

        static Color AvgColour(string name, Kit.Model k)
        {
            if (_avg.TryGetValue(name, out var c)) return c;
            c = new Color(0.8f, 0.75f, 0.68f);
            if (k != null)
            {
                // Average over side-facing triangles above the ground tile: the walls.
                float r = 0, gr = 0, b = 0; int n = 0;
                for (int i = 0; i < k.V.Length; i++)
                    if (Mathf.Abs(k.N[i].y) < 0.3f && k.V[i].y > 0.1f) { r += k.C[i].r; gr += k.C[i].g; b += k.C[i].b; n++; }
                if (n > 0) c = new Color(r / n, gr / n, b / n);
            }
            _avg[name] = c;
            return c;
        }

        static void Block3(MeshBuilder mb, Vector3 g, float y0, float w, float h, float d, Color side, Color top)
            => mb.Box(Matrix4x4.TRS(g + new Vector3(0f, y0 + h / 2f, 0f), Quaternion.identity, new Vector3(w, h, d)), side, top);

        /// <summary>A glass tower: floor bands, a slimmer top, a lobby and roof plant.</summary>
        static void Tower(MeshBuilder mb, Vector3 g, float w, float d, float h, float rA, float rB)
        {
            var glass = Glass[(int)(rA * Glass.Length) % Glass.Length];
            float lower = h * (0.55f + rB * 0.2f);
            Block3(mb, g, -2f, w, lower + 2f, d, glass, FlatRoof);
            Block3(mb, g, -0.5f, w + 0.6f, 4.5f, d + 0.6f, Shopfront, Trim);                  // lobby
            for (float y = 4f; y < lower - 1f; y += 4f) Block3(mb, g, y, w + 0.25f, 0.35f, d + 0.25f, Trim, Trim);
            Block3(mb, g, lower, w * 0.7f, h - lower, d * 0.7f, glass * 1.08f, FlatRoof);
            for (float y = lower + 4f; y < h - 1f; y += 4f) Block3(mb, g, y, w * 0.7f + 0.25f, 0.35f, d * 0.7f + 0.25f, Trim, Trim);
            Block3(mb, g, h, w * 0.3f, 2.2f, d * 0.3f, RoofKit, RoofKit);                       // roof plant
            if (h > 60f) Block3(mb, g, h + 2.2f, 0.5f, h * 0.12f, 0.5f, Trim, Trim);
        }

        void OnDestroy()
        {
            foreach (var t in _live.Values) Clear(t);
            _live.Clear();
        }
    }
}
