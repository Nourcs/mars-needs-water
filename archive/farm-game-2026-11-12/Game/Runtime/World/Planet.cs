using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MarsNeedsWater.Game
{
    /// <summary>The farm's local frame. Simulation poses are in this frame; PoseX converts to and from world space.</summary>
    public static class FarmSpace
    {
        public static Transform Frame;
    }

    /// <summary>
    /// A walkable, round Earth with real continents, seas, mountains and ice (1 : 255 radius, heights × 6).
    ///
    /// How it stays cheap on a laptop:
    /// - Cube-sphere quadtree: six faces split into smaller patches near the player and stay coarse far away.
    ///   Patches behind the horizon are never built. Each patch is a 16 × 16 grid with a skirt to hide LOD seams.
    /// - Only the finest patches next to the player get colliders and trees (trees merged into one mesh per patch),
    ///   and only the ground within ~50 m gets grass (merged per 19 m cell, no shadows).
    /// - Everything is vertex-coloured with one stylised material, so a patch is one or two draw calls.
    /// - A time budget per frame for building patches; old patches are dropped once their replacements are ready.
    /// - Moving origin: the player always stays near the world origin with "up" = +Y. When they walk far enough,
    ///   the whole planet is turned and shifted under them. That keeps float precision and lets the ordinary
    ///   upright CharacterController, physics and lighting work anywhere on the globe.
    /// Presentation only: the simulation lives in the farm's flat local frame (<see cref="FarmSpace"/>).
    /// </summary>
    public sealed class Planet : MonoBehaviour
    {
        public const float Radius = 25000f;         // 1 : 255 of Earth's 6,371 km
        public const float LandStep = 1.09f;        // metres per land level (Everest ≈ 208 m: heights exaggerated × 6)
        public const float MaxHeight = 210f;
        public const float FarmHalf = 70f;          // the farm's flat ground slabs cover ±70 m
        public const float FarmLat = 36.75f, FarmLon = -119.77f; // a farm pond in California's Central Valley

        const int Res = 16;
        const int MaxLevel = 9;                     // finest patch ≈ 77 m across
        const float SplitK = 2.2f;
        const float ColliderDist = 160f;
        const float RebaseDist = 96f;
        const float BuildBudgetMs = 4f;
        const float GrassDist = 48f;                // grass is built inside this and dropped past GrassDist + 14
        const int GrassCells = 4;                   // a finest patch has 4 × 4 grass cells (≈ 19 m each)

        public Transform Farm { get; private set; }
        public Vector3 FarmDir { get; private set; }
        public Transform Player => _player;
        /// <summary>The material of the sea and the lakes (rivers share it).</summary>
        public Material SeaMaterial => _sea;
        /// <summary>The planet in play (there is only ever one).</summary>
        public static Planet Current { get; private set; }
        float _farmBase;
        Vector3 _farmEast, _farmNorth, _farmP;

        Transform _player;
        CharacterController _cc;
        Material _ground, _sea, _deco, _grass;

        static readonly Vector3[] FaceUp = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        readonly struct Key : System.IEquatable<Key>
        {
            public readonly int Face, Level, X, Y;
            public Key(int f, int l, int x, int y) { Face = f; Level = l; X = x; Y = y; }
            public bool Equals(Key o) => Face == o.Face && Level == o.Level && X == o.X && Y == o.Y;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode() => ((Face * 31 + Level) * 4099 + X) * 4099 + Y;
        }

        sealed class Patch
        {
            public GameObject Go;
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public MeshCollider Collider;
            public Mesh Terrain;
            public Vector3 Centre;
            // Finest patches keep their grid so grass can be grown on it later without resampling the Earth.
            public Vector3[] Grid;      // (Res + 3)² positions relative to Centre, with a one-sample border
            public Color[] Colour;      // (Res + 1)² ground colours (sRGB)
            public float[] Grassy;      // (Res + 1)² 0..1 how much grass grows there
            public Dictionary<int, Mesh> Grass;
        }

        readonly Dictionary<Key, Patch> _patches = new Dictionary<Key, Patch>();
        readonly HashSet<Key> _wanted = new HashSet<Key>();
        readonly List<(Key key, float dist)> _queue = new List<(Key, float)>();
        readonly List<Key> _drop = new List<Key>();
        readonly List<int> _dropGrass = new List<int>();
        readonly Stopwatch _watch = new Stopwatch();
        Vector3 _lastPlan = new Vector3(1e9f, 0, 0);
        float _planTimer, _grassTimer;

        // ------------------------------------------------------------------ setup

        /// <summary>Everything the height of the ground depends on, with nothing drawn.</summary>
        bool LoadGround()
        {
            if (!EarthData.Load()) return false;
            Places.Load();
            CityReplica.Load(); // before any ground is built: rebuilt cities bring their own ground
            WorldWaters.Load();
            Hubs.Load(); // airports and ports level their own ground, so they come before any ground is built too
            FarmDir = EarthData.Dir(FarmLat, FarmLon);
            TangentAt(FarmDir, out _farmEast, out _farmNorth);
            _farmBase = Mathf.Max(4f, NaturalHeight(FarmDir));
            _farmP = FarmDir * Radius;

            return true;
        }

        /// <summary>
        /// For the world audit (tools/audit): the ground, road and river rules exactly as the game applies them, with
        /// no scene. Not used by the game.
        /// </summary>
        public bool Headless()
        {
            if (!LoadGround()) return false;
            Current = this;
            return true;
        }

        /// <summary>
        /// The ground as it is before roads and railways shape it: the landscape, cities, airports and ports, and
        /// the rivers' channels. Roads work out their heights from this, then cut and fill the ground to fit.
        /// </summary>
        public static float HeightBeforeRoads(Vector3 dir)
        {
            float h = HeightBeforeRivers(dir);
            Rivers.Ground(dir, ref h);
            if (Current != null) Current.FarmGround(dir, ref h);
            return h;
        }

        /// <summary>
        /// The ground as it is before rivers cut their channels: the landscape, any rebuilt city, and the levelled
        /// ground of airports and ports. Rivers take their water height from this, so they always sit in it.
        /// </summary>
        public static float HeightBeforeRivers(Vector3 dir)
        {
            float h = NaturalHeight(dir);
            CityReplica.Ground(dir, ref h);
            Hubs.Ground(dir, ref h);
            return h;
        }

        /// <summary>Creates the planet and the farm frame, and turns the planet so the farm frame is the world origin.</summary>
        public bool Create()
        {
            if (!LoadGround()) return false;
            Farm = new GameObject("Farm").transform;
            Farm.SetParent(transform, false);
            Farm.localPosition = FarmDir * (Radius + _farmBase);
            Farm.localRotation = Quaternion.LookRotation(_farmNorth, FarmDir);
            // Planet pose that puts the farm frame exactly at the world origin, unrotated.
            var inv = Quaternion.Inverse(Farm.localRotation);
            transform.rotation = inv;
            transform.position = -(inv * Farm.localPosition);
            FarmSpace.Frame = Farm;

            _ground = Prims.VertexColored(0.06f, false, 0.1f);
            if (_ground.HasProperty("_Detail")) _ground.SetFloat("_Detail", 0.17f);
            var water = Shader.Find("MarsNeedsWater/StylizedWater");
            _sea = water != null && water.isSupported ? new Material(water) : Prims.VertexColored(0.85f, false, 0.35f);
            if (_sea.HasProperty("_Clarity")) _sea.SetFloat("_Clarity", 0.55f);
            if (_sea.HasProperty("_SkyColor")) _sea.SetColor("_SkyColor", new Color(0.45f, 0.66f, 0.95f));
            _deco = Prims.VertexColored(0.1f, false, 0.25f);
            if (_deco.HasProperty("_Wind")) _deco.SetFloat("_Wind", 0.18f);
            _grass = Prims.VertexColored(0f, true, 0.05f);
            if (_grass.HasProperty("_Wind")) _grass.SetFloat("_Wind", 0.14f);
            return true;
        }

        public void Init(Transform player)
        {
            Current = this;
            _player = player;
            _cc = player.GetComponent<CharacterController>();
            Roads.Focus(WorldToDir(player.position)); // so the first ground already knows where the roads are
            Rivers.Focus(WorldToDir(player.position)); // and where the rivers cut their channels
            Plan();
            // Build everything near the player before the first frame so there is ground to stand on.
            while (_queue.Count > 0) BuildNext();
            DropStale();
            UpdateColliders();
            UpdateGrass(64);
            // The things that live on the planet's surface.
            gameObject.AddComponent<Cities>().Init(this);
            gameObject.AddComponent<Roads>().Init(this);
            gameObject.AddComponent<Hubs>().Init(this);
            gameObject.AddComponent<Rivers>().Init(this);
            gameObject.AddComponent<CityReplica>().Init(this);
            gameObject.AddComponent<Transit>().Init(this);
            gameObject.AddComponent<LandmarkView>().Init(this);
            gameObject.AddComponent<Birds>().Init(this);
            gameObject.AddComponent<Wildlife>().Init(this);
            gameObject.AddComponent<Clouds>().Init(this);
            // One flat trigger at the water's surface (sea level, or the lake's own height), kept under the player,
            // is the water surface the scoop ray hits (land and the farm stand above it, so they are hit first).
            _seaSurface = new GameObject("SeaSurface").transform;
            var box = _seaSurface.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(80f, 0.2f, 80f);
            box.center = new Vector3(0f, -0.1f, 0f);
            SimTarget.Add(_seaSurface.gameObject, TargetKind.Water, -1);
            UpdateSeaSurface();
        }

        Transform _seaSurface;

        void UpdateSeaSurface()
        {
            if (_seaSurface == null || _player == null) return;
            var dir = WorldToDir(_player.position);
            var at = transform.TransformPoint(dir * (Radius + WaterLevel(dir)));
            var up = (at - transform.position).normalized;
            _seaSurface.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, up));
        }

        // ------------------------------------------------------------------ height

        /// <summary>Real-Earth height in metres above sea level at a unit direction, before the farm is flattened.</summary>
        public static float NaturalHeight(Vector3 dir)
        {
            EarthData.LatLon(dir, out float lat, out float lon);
            float h;
            if (EarthData.Measured) h = EarthData.Metres(lat, lon);
            else
            {
                float lv = EarthData.Level(lat, lon) - EarthData.SeaLevel;
                h = lv >= 0f ? lv * LandStep : -EarthData.SeaDepth(-lv);
            }
            float wl = EarthData.LakeLevel(lat, lon) * LandStep;
            if (h > wl)
            {
                // Small rolling detail on land so the ground is not smooth between data samples (38 m apart). None
                // under water or right at the shore, so lake beds never poke through their lake.
                var p = dir * Radius;
                float n = Noise3(p * 0.012f) * 3f + Noise3(p * 0.07f) * 0.8f;
                h += n * Mathf.Clamp01((h - wl) / 3f);
            }
            return h;
        }

        /// <summary>Height of the water surface in metres at a direction: the lake's own height, or 0 for the sea.</summary>
        public static float WaterLevel(Vector3 dir)
        {
            float city = 0f;
            if (CityReplica.Water(dir, ref city)) return city; // a rebuilt city brings its own bay, river or lake
            EarthData.LatLon(dir, out float lat, out float lon);
            return EarthData.LakeLevel(lat, lon) * LandStep;
        }

        public static float Noise3(Vector3 p)
            => (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 17.3f, p.z) + Mathf.PerlinNoise(p.z + 41.7f, p.x)) / 3f - 0.5f;

        /// <summary>Surface height in metres above sea level, with the farm's plateau cut in.</summary>
        public float Height(Vector3 dir)
        {
            float h = HeightBeforeRoads(dir);
            // Roads shape the ground last, so they also fit the rise around the farm; the farm itself is left alone.
            if (Vector3.Dot(dir, FarmDir) < 0.999f || FarmCheb(dir) > FarmHalf + 8f) Roads.Ground(dir, ref h);
            return h;
        }

        void FarmGround(Vector3 dir, ref float h)
        {
            if (Vector3.Dot(dir, FarmDir) < 0.999f) return; // farther than ~1 km from the farm: untouched
            float cheb = FarmCheb(dir);
            // Under the farm's ground slabs the terrain sinks 3 m (so the pond pit stays open), then meets the slab
            // edge at exactly the farm height, then blends into the real landscape.
            if (cheb < FarmHalf - 5f) h = _farmBase - 3f;
            else if (cheb < FarmHalf - 1f) h = _farmBase - 3f * (1f - (cheb - (FarmHalf - 5f)) / 4f);
            else if (cheb < FarmHalf + 5f) h = _farmBase;
            else h = Mathf.Lerp(_farmBase, h, Mathf.SmoothStep(0f, 1f, (cheb - FarmHalf - 5f) / 90f));
        }

        /// <summary>Distance in metres from the farm centre, measured as the larger of the east and north offsets.</summary>
        public float FarmCheb(Vector3 dir)
        {
            var d = dir * Radius - _farmP;
            return Mathf.Max(Mathf.Abs(Vector3.Dot(d, _farmEast)), Mathf.Abs(Vector3.Dot(d, _farmNorth)));
        }

        /// <summary>World position to a unit direction from the planet centre (planet space).</summary>
        public Vector3 WorldToDir(Vector3 world) => transform.InverseTransformPoint(world).normalized;

        /// <summary>Planet-space east and north at a unit direction (left-handed: east = dir × up).</summary>
        public static void TangentAt(Vector3 dir, out Vector3 east, out Vector3 north)
        {
            east = Vector3.Cross(dir, Vector3.up);
            if (east.sqrMagnitude < 1e-8f) east = Vector3.left;
            east.Normalize();
            north = Vector3.Cross(east, dir).normalized;
        }

        // ------------------------------------------------------------------ ground colours

        static readonly Color Lush = new Color(0.42f, 0.72f, 0.27f), LushDark = new Color(0.3f, 0.6f, 0.2f);
        static readonly Color DryGrass = new Color(0.76f, 0.72f, 0.36f), Tundra = new Color(0.56f, 0.6f, 0.4f);
        static readonly Color ForestFloor = new Color(0.23f, 0.5f, 0.2f), Jungle = new Color(0.16f, 0.48f, 0.18f);
        static readonly Color Sand = new Color(0.94f, 0.74f, 0.46f), RedSand = new Color(0.84f, 0.5f, 0.3f), DuneShade = new Color(0.78f, 0.56f, 0.36f);
        static readonly Color Beach = new Color(0.97f, 0.89f, 0.63f), Rock = new Color(0.6f, 0.56f, 0.53f);
        static readonly Color DarkRock = new Color(0.44f, 0.42f, 0.43f), Snow = new Color(0.95f, 0.97f, 1f);
        static readonly Color Seabed = new Color(0.4f, 0.55f, 0.55f), Street = new Color(0.6f, 0.6f, 0.63f), RoadSide = new Color(0.5f, 0.45f, 0.38f), CityPaving = new Color(0.74f, 0.73f, 0.71f);
        static readonly Color SeaShallow = new Color(0.22f, 0.82f, 0.8f), SeaMid = new Color(0.1f, 0.55f, 0.75f);
        static readonly Color SeaDeep = new Color(0.04f, 0.22f, 0.52f), SeaIce = new Color(0.86f, 0.93f, 0.98f);

        /// <summary>
        /// Hand-painted ground colour from the real biome, height, slope and latitude, with noise so it never looks
        /// like the 77 m data cells it comes from. <paramref name="up"/> is how upright the ground is (1 = flat).
        /// </summary>
        public Color Paint(Vector3 dir, float h, float up, out float grassy)
        {
            EarthData.LatLon(dir, out float lat, out float lon);
            var p = dir * Radius;
            float big = Noise3(p * 0.006f), mid = Noise3(p * 0.03f), fine = Noise3(p * 0.2f);
            // Wobble the lookup so biome borders are organic, not square.
            float wl = Noise3(p * 0.012f + new Vector3(5.1f, 0f, 0f)) * 0.5f, wo = Noise3(p * 0.012f + new Vector3(0f, 9.7f, 0f)) * 0.5f;
            float blat = lat + wl, blon = lon + wo;
            float alat = Mathf.Abs(lat);
            grassy = 0f;

            Color c;
            float water = EarthData.LakeLevel(lat, lon) * LandStep;
            bool cityHere = CityReplica.All.Count > 0 && CityReplica.Covers(dir) > 0.5f;
            if (cityHere) CityReplica.Water(dir, ref water);
            if (h < water + 0.05f) return Shade(SeabedColour(water - h, alat, big, mid, fine), fine);
            var biome = EarthData.BiomeAt(blat, blon);
            float snowLine = 118f * Mathf.Pow(Mathf.Cos(lat * Mathf.Deg2Rad), 1.5f) + big * 18f;
            if (biome == EarthData.Biome.Ice || EarthData.IceAt(blat, blon) || h > snowLine)
                c = Snow;
            else
            {
                switch (biome)
                {
                    case EarthData.Biome.Forest:
                        c = alat < 18f ? Jungle : Color.Lerp(ForestFloor, LushDark, mid + 0.5f);
                        grassy = 0.85f;
                        break;
                    case EarthData.Biome.Bare:
                        if (alat > 52f) { c = Color.Lerp(Tundra, Rock, mid + 0.5f); grassy = 0.2f; }
                        else
                        {
                            // Desert: golden to red sand, darker in the dune troughs, with gravel and scrub patches.
                            c = Color.Lerp(Sand, RedSand, Mathf.Clamp01(big * 2.2f + 0.5f));
                            c = Color.Lerp(c, DuneShade, Mathf.Clamp01(-mid * 2.5f));
                            float scrub = Mathf.Clamp01((fine + mid * 0.5f - 0.18f) * 4f);
                            c = Color.Lerp(c, new Color(0.62f, 0.56f, 0.36f), scrub * 0.45f);
                            grassy = scrub * 0.15f;
                        }
                        break;
                    default:
                    {
                        // Grassland: lush or straw-dry from a broad noise, drier near the 25° desert belts.
                        float belt = Mathf.Clamp01(1f - Mathf.Abs(alat - 25f) / 14f) * 0.45f;
                        float dry = Mathf.Clamp01(0.35f + big * 2.2f + belt);
                        c = alat > 58f ? Tundra : Color.Lerp(Color.Lerp(Lush, LushDark, mid + 0.5f), DryGrass, dry);
                        grassy = 1f - dry * 0.45f;
                        break;
                    }
                }
                // Sandy beaches along the coast: the strip where the height data steps from the last sea level to
                // the first land level (lowland plains a little inland stay green).
                float beach = 1f - Mathf.SmoothStep(0f, 1f, (h - water - 0.5f) / 0.55f);
                if (beach > 0f) { c = Color.Lerp(c, Beach, beach); grassy *= 1f - beach; }
                // Mountains go rocky, then snowy just under the snow line.
                float high = Mathf.SmoothStep(0f, 1f, (h - snowLine * 0.55f) / (snowLine * 0.3f));
                c = Color.Lerp(c, Rock, high * 0.8f);
                float nearSnow = Mathf.SmoothStep(0f, 1f, (h - snowLine * 0.9f) / (snowLine * 0.1f + 1f));
                c = Color.Lerp(c, Snow, nearSnow);
                grassy *= 1f - high;
            }
            // Steep ground shows bare rock (snow sticks only to gentler slopes).
            float steep = Mathf.SmoothStep(0f, 1f, (0.9f - up) / 0.2f);
            if (steep > 0f) { c = Color.Lerp(c, Color.Lerp(Rock, DarkRock, mid + 0.5f), steep); grassy *= 1f - steep; }
            // Towns and cities pave the ground.
            float town = Places.Urban(dir, lat, lon);
            if (town > 0f) { c = Color.Lerp(c, Street, town * 0.9f); grassy *= 1f - town; }
            // Bare earth under and beside the roads, and no grass poking through them.
            else if (h > water && Roads.Near(dir, 2.5f)) { c = Color.Lerp(c, RoadSide, 0.7f); grassy = 0f; }
            if (grassy > 0f && Hubs.Near(dir, 1f)) grassy = 0f; // mown short on airfields and quays
            // River beds are water-coloured, so a river still reads as one where the far, coarse ground hides its water;
            // a strip of damp earth along the edges.
            if (h > water && Rivers.Bed(dir)) { c = Color.Lerp(c, RiverBed, 0.85f); grassy = 0f; }
            else if (h > water && Rivers.Near(dir, 1.5f)) { c = Color.Lerp(c, DampEarth, 0.55f); grassy = 0f; }
            // A rebuilt city paves all of its land.
            if (cityHere && h > water + 1.2f && CityReplica.Paved(dir)) { c = CityPaving; grassy = 0f; }
            return Shade(c, fine);
        }

        static Color Shade(Color c, float n)
        {
            float k = 1f + n * 0.16f;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        /// <summary>Paint, then the farm's own dry ground near the farm.</summary>
        Color GroundColour(Vector3 dir, float h, float up, out float grassy)
        {
            var c = Paint(dir, h, up, out grassy);
            if (Vector3.Dot(dir, FarmDir) < 0.9999f) return c;
            float cheb = FarmCheb(dir);
            if (cheb < FarmHalf + 1.5f) grassy = 0f; // nothing may poke up through the farm's ground slabs
            float farm = 1f - Mathf.SmoothStep(0f, 1f, (cheb - FarmHalf - 3f) / 90f);
            if (farm <= 0f) return c;
            grassy *= 1f - farm * 0.6f;
            return Color.Lerp(c, Palette.Ground, farm);
        }

        static Color SeaColour(float depth, bool ice)
        {
            if (ice) return SeaIce;
            float t = Mathf.Clamp01(depth / 3f);
            return t < 1f ? Color.Lerp(SeaShallow, SeaMid, t) : Color.Lerp(SeaMid, SeaDeep, Mathf.Clamp01((depth - 3f) / 30f));
        }

        static readonly Color RiverBed = new Color(0.16f, 0.42f, 0.58f), DampEarth = new Color(0.62f, 0.58f, 0.42f);
        static readonly Color WetSand = new Color(0.86f, 0.78f, 0.56f), WhiteSand = new Color(0.97f, 0.93f, 0.8f);
        static readonly Color Seagrass = new Color(0.3f, 0.52f, 0.33f), Silt = new Color(0.5f, 0.55f, 0.5f);
        static readonly Color DeepFloor = new Color(0.25f, 0.32f, 0.38f), Abyss = new Color(0.14f, 0.17f, 0.24f);

        /// <summary>
        /// Sea and lake floors by depth (metres in game): sand at the shore (white in the tropics), patchy seagrass
        /// and silt on the shallow shelf, rock further down, dark ooze in the deep ocean.
        /// </summary>
        static Color SeabedColour(float depth, float alat, float big, float mid, float fine)
        {
            var sand = alat < 28f ? Color.Lerp(WhiteSand, WetSand, Mathf.Clamp01(big + 0.5f)) : Color.Lerp(WetSand, Beach, 0.3f);
            var shelf = Color.Lerp(Silt, Seagrass, Mathf.SmoothStep(0f, 1f, (mid + fine * 0.5f) * 3f + 0.5f));
            var rock = Color.Lerp(Silt, Seabed, mid + 0.5f);
            if (depth < 0.8f) return Color.Lerp(Beach, sand, Mathf.Clamp01(depth / 0.8f));
            if (depth < 2f) return Color.Lerp(sand, shelf, (depth - 0.8f) / 1.2f);
            if (depth < 6f) return Color.Lerp(shelf, rock, (depth - 2f) / 4f);
            if (depth < 40f) return Color.Lerp(rock, DeepFloor, (depth - 6f) / 34f);
            return Color.Lerp(DeepFloor, Abyss, Mathf.Clamp01((depth - 40f) / 60f));
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (_player == null) return;
            var p = _player.position;
            if (new Vector2(p.x, p.z).magnitude > RebaseDist) Rebase();

            _planTimer -= Time.unscaledDeltaTime;
            if (_planTimer <= 0f && (_player.position - _lastPlan).sqrMagnitude > 16f) { Plan(); _planTimer = 0.25f; }

            _watch.Restart();
            int built = 0;
            while (_queue.Count > 0 && (built == 0 || _watch.Elapsed.TotalMilliseconds < BuildBudgetMs)) { BuildNext(); built++; }
            // Old patches go once nothing near the player is still waiting to be built (no holes underfoot),
            // even while walking keeps adding far-away work to the queue.
            if (_queue.Count == 0 || _queue[_queue.Count - 1].dist > 600f * 600f) DropStale();
            UpdateColliders();
            _grassTimer -= Time.unscaledDeltaTime;
            if (_grassTimer <= 0f) { _grassTimer = 0.2f; UpdateGrass(2); }
            UpdateSeaSurface();
        }

        void LateUpdate()
        {
            // Shaders read planet-space positions (wind, ripples, ground detail) so patterns stay glued to the
            // ground when the planet is re-based.
            Shader.SetGlobalMatrix(WorldToPlanetId, transform.worldToLocalMatrix);
            UnderwaterFog();
        }

        // Under water the world turns into a short teal fog; the normal sky haze comes back on surfacing.
        bool _underwater;
        Color _airFog;
        float _airStart, _airEnd;
        FogMode _airMode;
        static readonly Color WaterFog = new Color(0.12f, 0.42f, 0.5f);

        void UnderwaterFog()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var p = cam.transform.position;
            bool under = p.y < WaterY(p) - 0.05f && p.y > SurfaceY(p, false) - 2f;
            if (under == _underwater) return;
            _underwater = under;
            if (under)
            {
                _airFog = RenderSettings.fogColor; _airStart = RenderSettings.fogStartDistance;
                _airEnd = RenderSettings.fogEndDistance; _airMode = RenderSettings.fogMode;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = WaterFog;
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = 55f;
            }
            else
            {
                RenderSettings.fogMode = _airMode;
                RenderSettings.fogColor = _airFog;
                RenderSettings.fogStartDistance = _airStart;
                RenderSettings.fogEndDistance = _airEnd;
            }
        }

        static readonly int WorldToPlanetId = Shader.PropertyToID("_WorldToPlanet");

        /// <summary>Turns and shifts the planet so the ground under the player is at the origin with up = +Y.</summary>
        void Rebase()
        {
            var pw = _player.position;
            var pp = transform.InverseTransformPoint(pw);
            var dir = pp.normalized;
            float surf = Radius + Height(dir);
            float above = pp.magnitude - surf;
            var worldUp = transform.rotation * dir;
            var delta = Quaternion.FromToRotation(worldUp, Vector3.up);
            var rot = delta * transform.rotation;
            transform.rotation = rot;
            transform.position = -(rot * (dir * surf));

            var fwd = delta * _player.forward;
            fwd.y = 0f;
            if (_cc != null) _cc.enabled = false;
            _player.position = new Vector3(0f, above, 0f);
            if (fwd.sqrMagnitude > 1e-6f) _player.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            if (_cc != null) _cc.enabled = true;
            Physics.SyncTransforms();
            _lastPlan = new Vector3(1e9f, 0, 0); // replan from the new frame
        }

        /// <summary>
        /// Puts the player down at a place on the planet, facing north (a shortcut for visiting far cities; see the
        /// map). The planet is turned so that the place is at the origin, exactly as <see cref="Rebase"/> does.
        /// </summary>
        public void Travel(Vector3 dir)
        {
            if (_player == null) return;
            dir.Normalize();
            float surf = Radius + Mathf.Max(Height(dir), WaterLevel(dir));
            TangentAt(dir, out _, out var north);
            var rot = Quaternion.Inverse(Quaternion.LookRotation(north, dir));
            transform.rotation = rot;
            transform.position = -(rot * (dir * surf));
            if (_cc != null) _cc.enabled = false;
            _player.position = new Vector3(0f, 2.5f, 0f);
            _player.rotation = Quaternion.identity;
            if (_cc != null) _cc.enabled = true;
            Physics.SyncTransforms();
            _lastPlan = new Vector3(1e9f, 0, 0);
        }

        // ------------------------------------------------------------------ quadtree planning

        void Plan()
        {
            _lastPlan = _player.position;
            _wanted.Clear();
            var pp = transform.InverseTransformPoint(_player.position);
            float alt = Mathf.Max(1.7f, pp.magnitude - Radius);
            float horizon = Mathf.Sqrt(2f * Radius * alt + alt * alt) + Mathf.Sqrt(2f * Radius * MaxHeight);
            for (int f = 0; f < 6; f++) Visit(new Key(f, 0, 0, 0), pp, horizon);

            _queue.Clear();
            foreach (var k in _wanted)
                if (!_patches.ContainsKey(k)) _queue.Add((k, (PatchCentre(k) * Radius - pp).sqrMagnitude));
            _queue.Sort((a, b) => b.dist.CompareTo(a.dist)); // nearest last, so BuildNext pops from the end
        }

        void Visit(Key k, Vector3 pp, float horizon)
        {
            float edge = Radius * Mathf.PI * 0.5f / (1 << k.Level);
            var c = PatchCentre(k) * Radius;
            float d = (c - pp).magnitude;
            if (k.Level > 0 && d - edge * 0.8f > horizon) return; // behind the horizon
            if (k.Level < MaxLevel && d < SplitK * edge)
            {
                int l = k.Level + 1, x = k.X * 2, y = k.Y * 2;
                Visit(new Key(k.Face, l, x, y), pp, horizon);
                Visit(new Key(k.Face, l, x + 1, y), pp, horizon);
                Visit(new Key(k.Face, l, x, y + 1), pp, horizon);
                Visit(new Key(k.Face, l, x + 1, y + 1), pp, horizon);
                return;
            }
            _wanted.Add(k);
        }

        static Vector3 PatchCentre(Key k)
        {
            float s = 2f / (1 << k.Level);
            return CubeToSphere(k.Face, -1f + (k.X + 0.5f) * s, -1f + (k.Y + 0.5f) * s);
        }

        static Vector3 CubeToSphere(int face, float a, float b)
        {
            var up = FaceUp[face];
            var axisA = new Vector3(up.y, up.z, up.x);
            var axisB = Vector3.Cross(up, axisA);
            var p = up + axisA * a + axisB * b;
            float x2 = p.x * p.x, y2 = p.y * p.y, z2 = p.z * p.z;
            return new Vector3(
                p.x * Mathf.Sqrt(1f - y2 / 2f - z2 / 2f + y2 * z2 / 3f),
                p.y * Mathf.Sqrt(1f - z2 / 2f - x2 / 2f + z2 * x2 / 3f),
                p.z * Mathf.Sqrt(1f - x2 / 2f - y2 / 2f + x2 * y2 / 3f)).normalized;
        }

        void DropStale()
        {
            _drop.Clear();
            foreach (var k in _patches.Keys) if (!_wanted.Contains(k)) _drop.Add(k);
            foreach (var k in _drop) Free(k);
        }

        void Free(Key k)
        {
            if (!_patches.TryGetValue(k, out var p)) return;
            foreach (var m in p.Meshes) if (m != null) Destroy(m);
            if (p.Grass != null) foreach (var m in p.Grass.Values) if (m != null) Destroy(m);
            Destroy(p.Go);
            _patches.Remove(k);
        }

        void UpdateColliders()
        {
            var pp = transform.InverseTransformPoint(_player.position);
            foreach (var kv in _patches)
            {
                if (kv.Key.Level != MaxLevel) continue;
                bool near = (PatchCentre(kv.Key) * Radius - pp).magnitude < ColliderDist;
                var p = kv.Value;
                if (near && p.Collider == null) { p.Collider = p.Go.AddComponent<MeshCollider>(); p.Collider.sharedMesh = p.Terrain; }
                else if (!near && p.Collider != null) { Destroy(p.Collider); p.Collider = null; }
            }
        }

        // ------------------------------------------------------------------ patch building

        void BuildNext()
        {
            var (k, _) = _queue[_queue.Count - 1];
            _queue.RemoveAt(_queue.Count - 1);
            if (_patches.ContainsKey(k) || !_wanted.Contains(k)) return;
            _patches[k] = Build(k);
        }

        Patch Build(Key k)
        {
            var patch = new Patch { Go = new GameObject($"Patch_{k.Face}_{k.Level}_{k.X}_{k.Y}") };
            var centreDir = PatchCentre(k);
            var centre = centreDir * Radius;
            patch.Centre = centre;
            patch.Go.transform.SetParent(transform, false);
            patch.Go.transform.localPosition = centre;

            float s = 2f / (1 << k.Level);
            float a0 = -1f + k.X * s, b0 = -1f + k.Y * s, step = s / Res;
            int n = Res + 1, g = Res + 3; // g: grid with a one-sample border for seamless normals
            var pos = new Vector3[g * g];
            var dirs = new Vector3[g * g];
            var hs = new float[g * g];
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int j = 0; j < g; j++)
            for (int i = 0; i < g; i++)
            {
                var d = CubeToSphere(k.Face, a0 + (i - 1) * step, b0 + (j - 1) * step);
                float h = Height(d);
                if (i >= 1 && i <= n && j >= 1 && j <= n) { minH = Mathf.Min(minH, h); maxH = Mathf.Max(maxH, h); }
                dirs[j * g + i] = d;
                hs[j * g + i] = h;
                pos[j * g + i] = d * (Radius + h) - centre;
            }

            // Terrain mesh: inner grid + skirt, coloured per vertex.
            var v = new List<Vector3>(n * n + 4 * n);
            var nm = new List<Vector3>(n * n + 4 * n);
            var col = new List<Color>(n * n + 4 * n);
            var t = new List<int>(Res * Res * 6 + Res * 24);
            bool fine = k.Level == MaxLevel;
            var paint = fine ? new Color[n * n] : null;
            var grassy = fine ? new float[n * n] : null;
            for (int j = 1; j <= n; j++)
            for (int i = 1; i <= n; i++)
            {
                int gi = j * g + i;
                v.Add(pos[gi]);
                var nrm = Vector3.Cross(pos[gi + g] - pos[gi - g], pos[gi + 1] - pos[gi - 1]).normalized;
                if (Vector3.Dot(nrm, dirs[gi]) < 0f) nrm = -nrm;
                nm.Add(nrm);
                var c = GroundColour(dirs[gi], hs[gi], Vector3.Dot(nrm, dirs[gi]), out float gr);
                col.Add(Prims.VC(c));
                if (fine) { int q = (j - 1) * n + (i - 1); paint[q] = c; grassy[q] = gr; }
            }

            for (int j = 0; j < Res; j++)
            for (int i = 0; i < Res; i++)
            {
                int q = j * n + i;
                AddQuad(t, v, q, q + 1, q + n, q + n + 1, centreDir);
            }
            float edge = Radius * Mathf.PI * 0.5f / (1 << k.Level);
            float skirt = Mathf.Max(2f, edge * 0.03f);
            Skirt(v, nm, col, t, 0, 1, n, skirt, centreDir);
            Skirt(v, nm, col, t, Res * n, 1, n, skirt, centreDir);
            Skirt(v, nm, col, t, 0, n, n, skirt, centreDir);
            Skirt(v, nm, col, t, Res, n, n, skirt, centreDir);

            var terrain = new Mesh { name = "Terrain" };
            terrain.SetVertices(v);
            terrain.SetNormals(nm);
            terrain.SetColors(col);
            terrain.SetTriangles(t, 0);
            terrain.RecalculateBounds();
            patch.Terrain = terrain;
            patch.Meshes.Add(terrain);
            patch.Go.AddComponent<MeshFilter>().sharedMesh = terrain;
            var r = patch.Go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _ground;
            r.shadowCastingMode = k.Level >= MaxLevel - 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (fine) { patch.Grid = pos; patch.Colour = paint; patch.Grassy = grassy; }

            // Water surface (sea level, or a lake's own height), only where some of the patch is under water:
            // turquoise shallows to deep blue. Vertex alpha is the depth, for the shader's see-through and foam.
            float maxWl = 0f;
            var wls = new float[n * n];
            for (int j = 1; j <= n; j++)
            for (int i = 1; i <= n; i++)
            {
                float w = WaterLevel(dirs[j * g + i]);
                wls[(j - 1) * n + i - 1] = w;
                if (w > maxWl) maxWl = w;
            }
            if (minH < maxWl + 0.5f)
            {
                var sv = new List<Vector3>(n * n);
                var sn = new List<Vector3>(n * n);
                var sc = new List<Color>(n * n);
                var st = new List<int>(Res * Res * 6);
                for (int j = 1; j <= n; j++)
                for (int i = 1; i <= n; i++)
                {
                    var d = dirs[j * g + i];
                    float w = wls[(j - 1) * n + i - 1];
                    sv.Add(d * (Radius + w) - centre);
                    sn.Add(d);
                    EarthData.LatLon(d, out float lat, out float lon);
                    bool ice = Mathf.Abs(lat) > 60f && EarthData.IceAt(lat, lon);
                    float depth = w - hs[j * g + i];
                    var wc = Prims.VC(SeaColour(depth, ice));
                    wc.a = ice ? 1f : Mathf.Clamp01(depth / 8f); // depth: see-through shallows and shore foam
                    sc.Add(wc);
                }
                for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    int q = j * n + i;
                    // Skip quads that are dry at all four corners (so a lake's level never covers the valley below it).
                    // Also skip quads whose corners belong to waters at different heights (a lake's edge
                    // cell beside the sea): they would stand up as a wall of water.
                    bool dry = true;
                    float wMin = float.MaxValue, wMax = float.MinValue;
                    for (int c = 0; c < 4; c++)
                    {
                        int qi = q + (c & 1) + (c >> 1) * n;
                        int gi = (qi / n + 1) * g + qi % n + 1;
                        if (hs[gi] < wls[qi] + 0.3f) dry = false;
                        if (wls[qi] < wMin) wMin = wls[qi];
                        if (wls[qi] > wMax) wMax = wls[qi];
                    }
                    if (dry || wMax - wMin > 0.6f) continue;
                    AddQuad(st, sv, q, q + 1, q + n, q + n + 1, centreDir);
                }
                var sea = new Mesh { name = "Sea" };
                sea.SetVertices(sv);
                sea.SetNormals(sn);
                sea.SetColors(sc);
                sea.SetTriangles(st, 0);
                sea.RecalculateBounds();
                patch.Meshes.Add(sea);
                var so = new GameObject("Sea");
                so.transform.SetParent(patch.Go.transform, false);
                so.AddComponent<MeshFilter>().sharedMesh = sea;
                var sr = so.AddComponent<MeshRenderer>();
                sr.sharedMaterial = _sea;
                sr.shadowCastingMode = ShadowCastingMode.Off;
            }

            // Trees, bushes and rocks on the two finest levels, from the real biome map.
            if (k.Level >= MaxLevel - 1 && (maxH > 0.5f || k.Level == MaxLevel)) AddDeco(patch, k, a0, b0, s, centre);
            return patch;
        }

        /// <summary>Adds two triangles, wound so they face away from the planet centre.</summary>
        static void AddQuad(List<int> t, List<Vector3> v, int a, int b, int c, int d, Vector3 outward)
        {
            var n = Vector3.Cross(v[c] - v[a], v[b] - v[a]);
            if (Vector3.Dot(n, outward) >= 0f) { t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d); }
            else { t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c); }
        }

        static void Skirt(List<Vector3> v, List<Vector3> nm, List<Color> col, List<int> t, int start, int stride, int n, float depth, Vector3 inward)
        {
            int baseIdx = v.Count;
            for (int j = 0; j < n; j++)
            {
                int src = start + j * stride;
                v.Add(v[src] - inward * depth);
                nm.Add(nm[src]);
                col.Add(col[src]);
            }
            for (int j = 0; j < n - 1; j++)
            {
                int a = start + j * stride, b = start + (j + 1) * stride, a2 = baseIdx + j, b2 = baseIdx + j + 1;
                // Both windings: a skirt only ever shows through a crack, from either side.
                t.Add(a); t.Add(b); t.Add(a2); t.Add(b); t.Add(b2); t.Add(a2);
                t.Add(a); t.Add(a2); t.Add(b); t.Add(b); t.Add(a2); t.Add(b2);
            }
        }

        // ------------------------------------------------------------------ trees, bushes, rocks

        static readonly Color BarkCol = new Color(0.45f, 0.3f, 0.18f);
        static readonly Color[] PineCols = { new Color(0.13f, 0.42f, 0.25f), new Color(0.18f, 0.48f, 0.26f), new Color(0.1f, 0.36f, 0.22f) };
        static readonly Color[] LeafCols = { new Color(0.36f, 0.64f, 0.2f), new Color(0.46f, 0.72f, 0.24f), new Color(0.28f, 0.56f, 0.18f), new Color(0.55f, 0.74f, 0.22f) };
        static readonly Color[] Autumn = { new Color(0.92f, 0.55f, 0.15f), new Color(0.85f, 0.3f, 0.15f), new Color(0.95f, 0.78f, 0.2f) };
        static readonly Color BushCol = new Color(0.3f, 0.56f, 0.2f), RockCol = new Color(0.62f, 0.6f, 0.58f), Cactus = new Color(0.3f, 0.55f, 0.28f);

        bool NearLandmark(Vector3 dir, float metres)
        {
            float cos = Mathf.Cos(metres / Radius);
            foreach (var l in Landmarks.All) if (Vector3.Dot(dir, l.Dir) > cos) return true;
            return false;
        }

        void AddDeco(Patch patch, Key k, float a0, float b0, float s, Vector3 centre)
        {
            var rng = new System.Random((k.Face * 7919) ^ (k.X * 73856093) ^ (k.Y * 19349663) ^ k.Level);
            float R() => (float)rng.NextDouble();
            bool fine = k.Level == MaxLevel;
            var centreDir = centre.normalized;
            bool landmarkNear = NearLandmark(centreDir, 400f);
            bool farmNear = Vector3.Dot(centreDir, FarmDir) > 0.99995f;
            var mb = new MeshBuilder();
            int tries = fine ? 90 : 70;
            int seg = fine ? 7 : 5;
            for (int i = 0; i < tries; i++)
            {
                var d = CubeToSphere(k.Face, a0 + R() * s, b0 + R() * s);
                float h = Height(d);
                float wl = WaterLevel(d);
                if (h < wl - 0.6f) { if (fine) Underwater(mb, d, h, wl - h, centre, R); continue; }
                if (h < wl + 0.8f) continue;
                if (farmNear && FarmCheb(d) < FarmHalf + 12f) continue; // keep the farm clear
                if (landmarkNear && NearLandmark(d, Landmarks.Clearing)) continue;
                EarthData.LatLon(d, out float lat, out float lon);
                if (Places.Urban(d, lat, lon) > 0.25f || Roads.Near(d, 3f) || Hubs.Near(d, 4f) || Rivers.Near(d, 2f) || (CityReplica.Covers(d) > 0.3f && !CityReplica.Park(d))) continue;
                var biome = EarthData.BiomeAt(lat, lon);
                if (biome == EarthData.Biome.Ice || EarthData.IceAt(lat, lon)) continue;
                float alat = Mathf.Abs(lat);
                float snowLine = 118f * Mathf.Pow(Mathf.Cos(lat * Mathf.Deg2Rad), 1.5f);
                if (h > snowLine * 0.85f) { if (R() < 0.3f) RockAt(mb, d, h, centre, R, 1f); continue; }

                float roll = R();
                var basePos = d * (Radius + h) - centre;
                var up = Quaternion.FromToRotation(Vector3.up, d) * Quaternion.Euler(0f, R() * 360f, 0f);
                bool cold = alat > 50f || h > snowLine * 0.5f;
                switch (biome)
                {
                    case EarthData.Biome.Forest:
                        if (roll < 0.06f) RockAt(mb, d, h, centre, R, 1f);
                        else if (roll < 0.18f) BushAt(mb, basePos, up, R, seg, fine);
                        else if (Flora.Tree(mb, basePos, up, R, true, lat, lon, h, snowLine)) { }
                        else if (cold || roll < 0.32f) PineAt(mb, basePos, up, R, seg, fine);
                        else Puffy(mb, basePos, up, R, seg, alat);
                        break;
                    case EarthData.Biome.Grass:
                    case EarthData.Biome.Water:
                        if (roll < 0.05f) RockAt(mb, d, h, centre, R, 0.7f);
                        else if (roll < 0.2f) BushAt(mb, basePos, up, R, seg, fine);
                        else if (roll < 0.36f)
                        {
                            if (Flora.Tree(mb, basePos, up, R, false, lat, lon, h, snowLine)) { }
                            else if (cold) PineAt(mb, basePos, up, R, seg, fine);
                            else Puffy(mb, basePos, up, R, seg, alat);
                        }
                        break;
                    default: // bare: desert or tundra
                        if (roll < 0.16f) { RockAt(mb, d, h, centre, R, 1.3f); if (R() < 0.5f) RockAt(mb, d, h, centre, R, 0.6f); }
                        else if (roll < 0.23f && Flora.Desert(mb, basePos, up, R, lat, lon)) { }
                        else if (roll < 0.23f && alat < 35f) CactusAt(mb, basePos, up, R);
                        else if (roll < 0.36f) DryBush(mb, basePos, up, R);
                        break;
                }
            }
            var m = mb.ToMesh("Deco");
            if (m == null) return;
            patch.Meshes.Add(m);
            var go = new GameObject("Deco");
            go.transform.SetParent(patch.Go.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _deco;
            r.shadowCastingMode = fine ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        static Color Vary(Color c, float r) { float k = 0.9f + r * 0.2f; return new Color(c.r * k, c.g * k, c.b * k, 1f); }

        /// <summary>Foliage colour: alpha below 1 tells the shader to sway it gently in the wind.</summary>
        static Color Leafy(Color c) { c.a = 0.55f; return c; }

        static void Pine(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R, int seg)
        {
            float hgt = 6f + R() * 6f, w = hgt * 0.42f;
            var leaf = Leafy(Vary(PineCols[(int)(R() * PineCols.Length) % PineCols.Length], R()));
            mb.Cylinder(Matrix4x4.TRS(at, up, new Vector3(0.45f, hgt * 0.3f, 0.45f)), 5, BarkCol, BarkCol);
            for (int t = 0; t < 3; t++)
            {
                float y = hgt * (0.2f + t * 0.24f), sc = w * (1f - t * 0.27f);
                mb.Cone(Matrix4x4.TRS(at + up * new Vector3(0f, y, 0f), up, new Vector3(sc, hgt * 0.42f, sc)), seg, leaf * (1f + t * 0.06f));
            }
        }

        static void Puffy(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R, int seg, float alat)
        {
            float hgt = 4f + R() * 4f, c = 3f + R() * 2.4f;
            var pick = alat > 35f && alat < 60f && R() < 0.12f ? Autumn[(int)(R() * Autumn.Length) % Autumn.Length] : LeafCols[(int)(R() * LeafCols.Length) % LeafCols.Length];
            var leaf = Leafy(Vary(pick, R()));
            mb.Cylinder(Matrix4x4.TRS(at, up, new Vector3(0.5f, hgt * 0.65f, 0.5f)), 5, BarkCol, BarkCol);
            mb.Blob(Matrix4x4.TRS(at + up * new Vector3(0f, hgt * 0.62f + c * 0.3f, 0f), up, new Vector3(c, c * 0.85f, c)), leaf, seg);
            if (R() < 0.6f)
            {
                float c2 = c * (0.55f + R() * 0.2f);
                var off = new Vector3((R() - 0.5f) * c * 0.9f, hgt * 0.62f + c * 0.15f, (R() - 0.5f) * c * 0.9f);
                mb.Blob(Matrix4x4.TRS(at + up * off, up, new Vector3(c2, c2 * 0.85f, c2)), leaf * 1.08f, seg);
            }
        }

        /// <summary>Near the player a Kenney pine (CC0 model, sways), farther out the cheap code-built one.</summary>
        static void PineAt(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R, int seg, bool fine)
        {
            var k = Kit.Get("tree", 0.45f); // both deco levels, so trees never change shape as patches refine
            if (k == null) { Pine(mb, at, up, R, seg); return; }
            float sc = 4f + R() * 2.6f;
            float tint = 0.9f + R() * 0.2f;
            mb.Model(Matrix4x4.TRS(at + up * new Vector3(0f, -0.2f, 0f), up, new Vector3(sc, sc * (0.9f + R() * 0.25f), sc)), k, new Color(tint, tint, tint * 0.95f));
        }

        /// <summary>Near the player a Kenney bush (CC0 model, sways), farther out the code-built one.</summary>
        static void BushAt(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R, int seg, bool fine)
        {
            var k = Kit.Get("grass", 0.4f);
            if (k == null) { Bush(mb, at, up, R, seg); return; }
            float sc = 4.5f + R() * 3.5f;
            float tint = 0.85f + R() * 0.25f;
            mb.Model(Matrix4x4.TRS(at, up, new Vector3(sc, sc * (0.8f + R() * 0.4f), sc)), k, new Color(tint, tint, tint));
        }

        static void Bush(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R, int seg)
        {
            float b = 0.9f + R() * 1.1f;
            mb.Blob(Matrix4x4.TRS(at + up * new Vector3(0f, b * 0.3f, 0f), up, new Vector3(b * 1.3f, b, b * 1.2f)), Leafy(Vary(BushCol, R())), seg);
        }

        void RockAt(MeshBuilder mb, Vector3 d, float h, Vector3 centre, System.Func<float> R, float size)
        {
            float sz = (0.5f + R() * (R() < 0.12f ? 3.5f : 1f)) * size;
            var at = d * (Radius + h) - centre;
            var rot = Quaternion.FromToRotation(Vector3.up, d) * Quaternion.Euler(R() * 20f, R() * 360f, R() * 20f);
            mb.Blob(Matrix4x4.TRS(at, rot, new Vector3(sz * 1.3f, sz * 0.7f, sz)), Vary(RockCol, R()), 5);
        }

        static readonly Color[] CoralCols =
        {
            new Color(1f, 0.45f, 0.42f), new Color(1f, 0.62f, 0.28f), new Color(0.95f, 0.45f, 0.75f),
            new Color(0.62f, 0.45f, 0.95f), new Color(1f, 0.85f, 0.35f), new Color(0.35f, 0.85f, 0.75f),
        };
        static readonly Color Kelp = new Color(0.42f, 0.5f, 0.18f), KelpDark = new Color(0.28f, 0.38f, 0.14f);

        /// <summary>
        /// Life on sea and lake floors: coral in tropical shallows, swaying kelp in cooler water, seagrass tufts on
        /// the shelf, boulders everywhere. Plants never reach the surface; deep water has only the odd rock.
        /// </summary>
        void Underwater(MeshBuilder mb, Vector3 d, float h, float depth, Vector3 centre, System.Func<float> R)
        {
            EarthData.LatLon(d, out float lat, out _);
            float alat = Mathf.Abs(lat);
            if (alat > 66f) { if (R() < 0.15f) RockAt(mb, d, h, centre, R, 1.2f); return; } // icy seas: bare
            var at = d * (Radius + h) - centre;
            var up = Quaternion.FromToRotation(Vector3.up, d) * Quaternion.Euler(0f, R() * 360f, 0f);
            float roll = R();
            if (depth > 10f) { if (roll < 0.12f) RockAt(mb, d, h, centre, R, 1.5f); return; }
            if (roll < 0.1f) { RockAt(mb, d, h, centre, R, 1.1f); return; }
            if (alat < 28f && depth < 5f)
            {
                if (roll > 0.75f) return;
                // Coral: a cluster of bright knobs and branches.
                var col = Vary(CoralCols[(int)(R() * CoralCols.Length) % CoralCols.Length], R());
                int parts = 2 + (int)(R() * 4f);
                for (int i = 0; i < parts; i++)
                {
                    var off = up * new Vector3((R() - 0.5f) * 1.6f, 0f, (R() - 0.5f) * 1.6f);
                    float sz = 0.5f + R() * 0.9f;
                    if (R() < 0.5f) mb.Blob(Matrix4x4.TRS(at + off, up, new Vector3(sz, sz * 0.7f, sz)), col, 6);
                    else mb.Cone(Matrix4x4.TRS(at + off, up * Quaternion.Euler((R() - 0.5f) * 40f, 0f, (R() - 0.5f) * 40f),
                                 new Vector3(sz * 0.35f, Mathf.Min(sz * 2.2f, depth - 0.3f), sz * 0.35f)), 5, col * 1.08f);
                }
                return;
            }
            if (roll > 0.6f) return;
            float room = depth - 0.4f;
            if (room < 0.4f) return;
            if (alat > 30f && depth > 1.5f && roll < 0.4f)
            {
                // Kelp: tall wavy strands, swaying in the current (vertex alpha below 1).
                int strands = 2 + (int)(R() * 3f);
                for (int i = 0; i < strands; i++)
                {
                    float hgt = Mathf.Min(room, 2.5f + R() * 6f);
                    var col = Leafy(Vary(Color.Lerp(Kelp, KelpDark, R()), R()));
                    var off = up * new Vector3((R() - 0.5f) * 1.2f, 0f, (R() - 0.5f) * 1.2f);
                    mb.Cone(Matrix4x4.TRS(at + off, up * Quaternion.Euler((R() - 0.5f) * 14f, 0f, (R() - 0.5f) * 14f),
                            new Vector3(0.35f, hgt, 0.35f)), 4, col);
                }
                return;
            }
            // Seagrass: a low tuft of blades.
            var grass = Leafy(Vary(Seagrass * 0.85f, R()));
            for (int i = 0; i < 5; i++)
            {
                var off = up * new Vector3((R() - 0.5f) * 0.8f, 0f, (R() - 0.5f) * 0.8f);
                mb.Cone(Matrix4x4.TRS(at + off, up * Quaternion.Euler((R() - 0.5f) * 30f, R() * 360f, (R() - 0.5f) * 30f),
                        new Vector3(0.12f, Mathf.Min(room, 0.5f + R() * 0.7f), 0.05f)), 3, grass);
            }
        }

        static readonly Color[] DryCols = { new Color(0.62f, 0.6f, 0.34f), new Color(0.55f, 0.5f, 0.3f), new Color(0.7f, 0.62f, 0.4f) };

        /// <summary>Desert scrub: a low, wide, olive-straw tuft (sways a little).</summary>
        static void DryBush(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R)
        {
            var col = Leafy(Vary(DryCols[(int)(R() * DryCols.Length) % DryCols.Length], R()));
            float sz = 0.8f + R() * 1.2f;
            mb.Blob(Matrix4x4.TRS(at + up * new Vector3(0f, sz * 0.2f, 0f), up, new Vector3(sz * 1.4f, sz * 0.7f, sz * 1.2f)), col, 6);
            if (R() < 0.5f) mb.Blob(Matrix4x4.TRS(at + up * new Vector3(sz * 0.7f, sz * 0.12f, 0.2f), up, new Vector3(sz * 0.8f, sz * 0.45f, sz * 0.8f)), col * 0.92f, 5);
        }

        static void CactusAt(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R)
        {
            float hgt = 2.2f + R() * 1.8f;
            mb.Cylinder(Matrix4x4.TRS(at, up, new Vector3(0.45f, hgt, 0.45f)), 6, Cactus, Cactus * 1.1f);
            mb.Cylinder(Matrix4x4.TRS(at + up * new Vector3(0.45f, hgt * 0.45f, 0f), up, new Vector3(0.3f, hgt * 0.35f, 0.3f)), 5, Cactus, Cactus * 1.1f);
            mb.Cylinder(Matrix4x4.TRS(at + up * new Vector3(-0.42f, hgt * 0.3f, 0f), up, new Vector3(0.28f, hgt * 0.3f, 0.28f)), 5, Cactus, Cactus * 1.1f);
        }

        // ------------------------------------------------------------------ grass

        /// <summary>Grows thick grass on the finest patches around the player and drops it behind them.</summary>
        void UpdateGrass(int maxBuilds)
        {
            var pp = transform.InverseTransformPoint(_player.position);
            int built = 0;
            int n = Res + 1, g = Res + 3, cell = Res / GrassCells;
            foreach (var kv in _patches)
            {
                var p = kv.Value;
                if (p.Grid == null) continue;
                bool patchNear = (p.Centre - pp).magnitude < GrassDist + 80f;
                if (!patchNear && p.Grass == null) continue;
                for (int cj = 0; cj < GrassCells; cj++)
                for (int ci = 0; ci < GrassCells; ci++)
                {
                    int id = cj * GrassCells + ci;
                    int mi = ci * cell + cell / 2 + 1, mj = cj * cell + cell / 2 + 1;
                    float dist = (p.Centre + p.Grid[mj * g + mi] - pp).magnitude;
                    bool have = p.Grass != null && p.Grass.ContainsKey(id);
                    if (!have && dist < GrassDist && built < maxBuilds)
                    {
                        if (p.Grass == null) p.Grass = new Dictionary<int, Mesh>();
                        p.Grass[id] = BuildGrass(p, kv.Key, ci, cj, n, g, cell);
                        built++;
                    }
                    else if (have && dist > GrassDist + 14f)
                    {
                        var m = p.Grass[id];
                        p.Grass.Remove(id);
                        if (m != null)
                        {
                            var holder = p.Go.transform.Find("Grass" + id);
                            if (holder != null) Destroy(holder.gameObject);
                            Destroy(m);
                        }
                    }
                }
            }
        }

        Mesh BuildGrass(Patch p, Key k, int ci, int cj, int n, int g, int cell)
        {
            var rng = new System.Random(((k.Face * 7919) ^ (k.X * 73856093) ^ (k.Y * 19349663)) + ci * 131 + cj * 977);
            float R() => (float)rng.NextDouble();
            var mb = new MeshBuilder();
            for (int j = cj * cell; j < (cj + 1) * cell; j++)
            for (int i = ci * cell; i < (ci + 1) * cell; i++)
            {
                int q00 = j * n + i, q10 = q00 + 1, q01 = q00 + n, q11 = q01 + 1;
                float gmax = Mathf.Max(Mathf.Max(p.Grassy[q00], p.Grassy[q10]), Mathf.Max(p.Grassy[q01], p.Grassy[q11]));
                if (gmax < 0.05f) continue;
                Vector3 P(int ii, int jj) => p.Grid[(jj + 1) * g + (ii + 1)];
                Vector3 a = P(i, j), b = P(i + 1, j), c = P(i, j + 1), d = P(i + 1, j + 1);
                for (int t = 0; t < 18; t++)
                {
                    float u = R(), w = R();
                    float gr = Mathf.Lerp(Mathf.Lerp(p.Grassy[q00], p.Grassy[q10], u), Mathf.Lerp(p.Grassy[q01], p.Grassy[q11], u), w);
                    if (R() > gr) continue;
                    var col = Color.Lerp(Color.Lerp(p.Colour[q00], p.Colour[q10], u), Color.Lerp(p.Colour[q01], p.Colour[q11], u), w);
                    var at = Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(c, d, u), w);
                    var upDir = (p.Centre + at).normalized;
                    var basis = Quaternion.FromToRotation(Vector3.up, upDir);
                    float k1 = 0.9f + R() * 0.25f;
                    var root = new Color(col.r * 0.72f * k1, col.g * 0.78f * k1, col.b * 0.7f * k1, 1f);
                    // Alpha is how much a vertex sways in the wind: roots stay put, tips move.
                    var tip = new Color(Mathf.Min(1f, col.r * 1.18f * k1 + 0.05f), Mathf.Min(1f, col.g * 1.22f * k1 + 0.06f), col.b * 1.05f * k1, 0f);
                    float tall = 0.35f + R() * 0.4f;
                    for (int bl = 0; bl < 7; bl++)
                    {
                        var rot = basis * Quaternion.Euler((R() - 0.5f) * 40f, R() * 360f, (R() - 0.5f) * 40f);
                        var off = basis * new Vector3((R() - 0.5f) * 0.35f, -0.04f, (R() - 0.5f) * 0.35f);
                        mb.Blade(Matrix4x4.TRS(at + off, rot, new Vector3(0.07f + R() * 0.05f, tall * (0.7f + R() * 0.5f), 1f)), root, tip);
                    }
                }
            }
            var mesh = mb.ToMesh("Grass");
            if (mesh == null) return null;
            var go = new GameObject("Grass" + (cj * GrassCells + ci));
            go.transform.SetParent(p.Go.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _grass;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return mesh;
        }

        void OnDestroy()
        {
            foreach (var p in _patches.Values)
            {
                foreach (var m in p.Meshes) if (m != null) Destroy(m);
                if (p.Grass != null) foreach (var m in p.Grass.Values) if (m != null) Destroy(m);
            }
            _patches.Clear();
            if (_underwater) { _underwater = false; RenderSettings.fogMode = _airMode; RenderSettings.fogColor = _airFog; RenderSettings.fogStartDistance = _airStart; RenderSettings.fogEndDistance = _airEnd; }
            if (FarmSpace.Frame == Farm) FarmSpace.Frame = null;
            if (Current == this) Current = null;
        }

        /// <summary>
        /// World Y of the ground (or sea surface) straight below a world point near the player, where "up" is +Y.
        /// </summary>
        public float SurfaceY(Vector3 world, bool onWater = true)
        {
            var dir = WorldToDir(world);
            float h = Height(dir);
            if (onWater) h = Mathf.Max(WaterLevel(dir), h);
            return transform.TransformPoint(dir * (Radius + h)).y;
        }

        /// <summary>
        /// World Y of the water surface (sea or lake) straight below or above a world point near the player, or
        /// negative infinity where the ground there is dry.
        /// </summary>
        public float WaterY(Vector3 world)
        {
            var dir = WorldToDir(world);
            float wl = WaterLevel(dir);
            if (Height(dir) >= wl) return float.NegativeInfinity;
            return transform.TransformPoint(dir * (Radius + wl)).y;
        }
    }
}
