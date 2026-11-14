using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The world's real airports and sea ports (Natural Earth, baked by tools/earth/bake_hubs.py into
    /// Resources/Earth/earth_hubs). An airport levels its own ground and lays out a runway, taxiway, apron, terminal,
    /// tower, hangars and parked airliners, and one airliner keeps taking off and landing. A port finds the nearest
    /// shore, levels the land behind it into a quay and builds piers, cranes, container stacks, a warehouse, a
    /// lighthouse and moored cargo ships. Only the ones near the player are built. Presentation only.
    /// </summary>
    public sealed class Hubs : MonoBehaviour
    {
        const float BuildIn = 1500f, DropOut = 1900f, Paint = 0.3f;

        internal sealed class Hub
        {
            public bool Port; public int Rank; public string Name;
            public Vector3 Dir, East, North;            // local frame: x = East, z = North (runway / out to sea)
            public float Level, Water;                  // ground height of the hub, water level beside it
            public float HalfX, HalfZ, OffX, OffZ, Blend; // the levelled rectangle and how far it fades into the landscape
            public float Length;                        // runway length
            public Quaternion Rot;
            public GameObject Go; public readonly List<Mesh> Meshes = new List<Mesh>();
            public Transform Plane; public int Mode; public float Z, V, Y, Hold;
            public bool Big => Rank <= 4;
        }

        internal static readonly List<Hub> _all = new List<Hub>();
        static readonly Dictionary<long, List<Hub>> _grid = new Dictionary<long, List<Hub>>();
        static bool _loaded;
        static Mesh _airliner;

        Planet _planet;
        Material _mat;
        float _timer;

        static readonly Color Tarmac = new Color(0.2f, 0.21f, 0.24f), Taxi = new Color(0.3f, 0.31f, 0.34f), Apron = new Color(0.62f, 0.62f, 0.63f);
        static readonly Color White = new Color(0.94f, 0.94f, 0.92f), Yellow = new Color(0.95f, 0.76f, 0.2f), Glass = new Color(0.35f, 0.55f, 0.68f);
        static readonly Color Wall = new Color(0.86f, 0.87f, 0.88f), RoofGrey = new Color(0.55f, 0.57f, 0.6f), Dark = new Color(0.17f, 0.17f, 0.19f);
        static readonly Color Concrete = new Color(0.7f, 0.7f, 0.72f), Rust = new Color(0.55f, 0.2f, 0.15f);
        static readonly Color[] Boxes =
        {
            new Color(0.75f, 0.2f, 0.16f), new Color(0.15f, 0.38f, 0.66f), new Color(0.9f, 0.55f, 0.12f), new Color(0.2f, 0.52f, 0.36f),
            new Color(0.82f, 0.8f, 0.76f), new Color(0.5f, 0.24f, 0.2f), new Color(0.24f, 0.26f, 0.3f), new Color(0.88f, 0.76f, 0.2f),
        };
        static readonly Color[] Liveries =
        {
            new Color(0.8f, 0.15f, 0.15f), new Color(0.1f, 0.3f, 0.65f), new Color(0.95f, 0.6f, 0.1f), new Color(0.1f, 0.5f, 0.45f), new Color(0.35f, 0.2f, 0.55f),
        };

        public static float RunwayFor(int rank) => rank <= 3 ? 420f : rank <= 5 ? 340f : rank <= 7 ? 280f : 230f;

        // ------------------------------------------------------------------ data

        /// <summary>Reads the file and settles every hub on the landscape. Call before any ground is built.</summary>
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var ta = Resources.Load<TextAsset>("Earth/earth_hubs");
            if (ta == null) { Debug.LogWarning("Earth/earth_hubs missing: no airports or ports."); return; }
            var b = ta.bytes;
            if (b.Length < 8 || b[0] != 'M' || b[1] != 'N' || b[2] != 'W' || b[3] != 'H') return;
            int n = System.BitConverter.ToInt32(b, 4), o = 8;
            var farm = EarthData.Dir(Planet.FarmLat, Planet.FarmLon);
            for (int i = 0; i < n && o + 15 <= b.Length; i++)
            {
                var h = new Hub { Port = b[o] == 1, Rank = b[o + 1] };
                float lat = System.BitConverter.ToSingle(b, o + 2), lon = System.BitConverter.ToSingle(b, o + 6), yaw = System.BitConverter.ToSingle(b, o + 10);
                int len = b[o + 14];
                h.Name = System.Text.Encoding.UTF8.GetString(b, o + 15, Mathf.Min(len, b.Length - o - 15));
                o += 15 + len;
                h.Dir = EarthData.Dir(lat, lon);
                if ((h.Dir - farm).magnitude * Planet.Radius < 320f || CityReplica.Covers(h.Dir) > 0.2f) continue;
                if (h.Port ? !SettlePort(h) : !SettleAirport(h, yaw)) continue;
                h.Rot = Quaternion.LookRotation(h.North, h.Dir);
                _all.Add(h);
                float reach = Mathf.Sqrt(Sq(h.HalfX + Mathf.Abs(h.OffX)) + Sq(h.HalfZ + Mathf.Abs(h.OffZ))) + h.Blend + 45f;
                var p = h.Dir * Planet.Radius;
                long last = -1;
                for (int k = 0; k < 8; k++)
                {
                    long key = CellKey(p + new Vector3((k & 1) == 0 ? -reach : reach, (k & 2) == 0 ? -reach : reach, (k & 4) == 0 ? -reach : reach));
                    if (key == last) continue;
                    last = key;
                    if (!_grid.TryGetValue(key, out var l)) _grid[key] = l = new List<Hub>();
                    if (!l.Contains(h)) l.Add(h);
                }
            }
        }

        static float Sq(float v) => v * v;

        static long CellKey(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / 1000f) + 512, y = Mathf.FloorToInt(p.y / 1000f) + 512, z = Mathf.FloorToInt(p.z / 1000f) + 512;
            return (x << 40) | (y << 20) | z;
        }

        static Vector3 Off(Vector3 dir, Vector3 a, float da, Vector3 b, float db) => (dir * Planet.Radius + a * da + b * db).normalized;

        /// <summary>An airport: the frame from its heading, the level from the ground it stands on. False if it is out at sea.</summary>
        static bool SettleAirport(Hub h, float yaw)
        {
            // An airfield needs a wide, fairly level piece of ground that nothing else uses: no road, railway or
            // river through it, no town, no other airfield. It tries its real spot and heading first, then other
            // headings, then spots a little further out each time. If nothing fits it is left off the map: an
            // airport that a motorway runs through is worse than no airport.
            var home = h.Dir;
            Planet.TangentAt(home, out var e0, out var n0);
            h.Length = RunwayFor(h.Rank);
            h.HalfX = 100f; h.OffX = 25f; h.HalfZ = h.Length * 0.5f + 25f; h.OffZ = 0f; h.Blend = 90f;
            bool found = false;
            for (int ring = 0; ring <= 6 && !found; ring++)
                for (int k = 0; k < (ring == 0 ? 1 : 12) && !found; k++)
                {
                    float b = (k * 30f + ring * 15f) * Mathf.Deg2Rad;
                    var at = ring == 0 ? home : Off(home, e0, Mathf.Cos(b) * ring * 130f, n0, Mathf.Sin(b) * ring * 130f);
                    if (Planet.NaturalHeight(at) < Planet.WaterLevel(at) - 8f) continue;   // deep water
                    Planet.TangentAt(at, out var e, out var n);
                    for (int t = 0; t < 4 && !found; t++)
                    {
                        float a = (yaw + t * 45f) * Mathf.Deg2Rad;
                        h.Dir = at;
                        h.North = (n * Mathf.Cos(a) + e * Mathf.Sin(a)).normalized;
                        h.East = Vector3.Cross(h.Dir, h.North).normalized;
                        found = Clear(h, 30f);
                    }
                }
            if (!found) return false;
            float sum = 0f, water = float.MinValue; int cnt = 0;
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = -2; iz <= 2; iz++)
                {
                    var d = Off(h.Dir, h.East, h.OffX + ix * h.HalfX * 0.8f, h.North, iz * h.HalfZ * 0.45f);
                    float wl = Planet.WaterLevel(d);
                    sum += Mathf.Max(Planet.NaturalHeight(d), wl); cnt++;
                    water = Mathf.Max(water, wl);
                }
            h.Water = water;
            h.Level = Mathf.Max(sum / cnt, water + 1.6f);
            return true;
        }

        /// <summary>
        /// Is the ground a hub would level free of everything else on the map: roads, railways, rivers, rebuilt
        /// cities, the farm, other hubs, and (for an airfield) towns and ground too hilly to level?
        /// </summary>
        static bool Clear(Hub h, float relief)
        {
            string why = Blocked(h, relief);
            if (why == null) return true;
            Why[why] = Why.TryGetValue(why, out int n) ? n + 1 : 1;
            return false;
        }

        /// <summary>For the world audit: how often each thing stood in the way of a hub.</summary>
        internal static readonly Dictionary<string, int> Why = new Dictionary<string, int>();

        static string Blocked(Hub h, float relief)
        {
            var farm = EarthData.Dir(Planet.FarmLat, Planet.FarmLon);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int ix = -2; ix <= 2; ix++)
                for (int iz = -4; iz <= 4; iz++)
                {
                    var d = Off(h.Dir, h.East, h.OffX + ix * (h.HalfX + 12f) * 0.5f, h.North, h.OffZ + iz * (h.HalfZ + 12f) * 0.25f);
                    if (CityReplica.Covers(d) > 0f) return "city";
                    if ((d - farm).magnitude * Planet.Radius < 330f) return "farm";
                    if (!h.Port)
                    {
                        EarthData.LatLon(d, out float lat, out float lon);
                        if (Places.Urban(d, lat, lon) > 0.5f) return "town";   // the edge of a town may reach an airfield, its middle may not
                        float g = Mathf.Max(Planet.NaturalHeight(d), Planet.WaterLevel(d));
                        lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
                    }
                    if (_grid.TryGetValue(CellKey(d * Planet.Radius), out var l))
                        for (int i = 0; i < l.Count; i++) if (l[i] != h && Outside(l[i], d) < l[i].Blend + h.Blend) return "hub";
                }
            if (hi - lo > relief) return "hilly";
            float rad = Mathf.Sqrt(Sq(h.HalfX + Mathf.Abs(h.OffX)) + Sq(h.HalfZ + Mathf.Abs(h.OffZ))) + 30f;
            if (h.Port)
            {
                // A road or a railway may come to a port, behind the quay; nothing may cross the quay itself.
                if (Roads.Crosses(h.Dir, rad, p => Outside(h, p) <= 4f && Vector3.Dot((p - h.Dir) * Planet.Radius, h.North) > -14f)) return "road";
                if (Rivers.Crosses(h.Dir, rad, p => Outside(h, p) <= 12f)) return "river";
                return null;
            }
            if (Roads.Crosses(h.Dir, rad, p => Outside(h, p) <= 14f)) return "road";
            if (Rivers.Crosses(h.Dir, rad, p => Outside(h, p) <= 22f)) return "river";
            return null;
        }

        static bool Wet(Vector3 d, float below) => Planet.NaturalHeight(d) < Planet.WaterLevel(d) - below;

        /// <summary>A port: looks for the nearest shore and faces the open water. False if there is none within reach.</summary>
        static bool SettlePort(Hub h)
        {
            Planet.TangentAt(h.Dir, out var e, out var n);
            string last = "no shore";
            for (int r = 0; r <= 160; r += 16)
            {
                int count = r == 0 ? 1 : 12;
                for (int k = 0; k < count; k++)
                {
                    float a = (k * 30f + r) * Mathf.Deg2Rad;
                    var c = Off(h.Dir, e, Mathf.Cos(a) * r, n, Mathf.Sin(a) * r);
                    if (Planet.NaturalHeight(c) < Planet.WaterLevel(c) + 0.8f) continue;
                    // Land. Which way is the most open water?
                    float best = float.MaxValue; Vector3 sea = Vector3.zero; bool found = false;
                    for (int j = 0; j < 8; j++)
                    {
                        float bb = j * 45f * Mathf.Deg2Rad;
                        var u = e * Mathf.Cos(bb) + n * Mathf.Sin(bb);
                        if (!Wet(Off(c, u, 30f, u, 0f), 1f)) continue;
                        var far = Off(c, u, 75f, u, 0f);
                        float depth = Planet.NaturalHeight(far) - Planet.WaterLevel(far);
                        if (depth < best) { best = depth; sea = u; found = true; }
                    }
                    if (!found) continue;
                    var shore = c;
                    for (int s = 2; s <= 30; s += 2)
                    {
                        shore = Off(c, sea, s, sea, 0f);
                        if (Planet.NaturalHeight(shore) < Planet.WaterLevel(shore) + 0.3f) break;
                    }
                    if (CityReplica.Covers(shore) > 0.2f) return false;
                    h.Dir = shore;
                    h.North = (sea - shore * Vector3.Dot(sea, shore)).normalized;
                    h.East = Vector3.Cross(h.Dir, h.North).normalized;
                    h.Water = Planet.WaterLevel(Off(shore, h.North, 30f, h.North, 0f));
                    h.Level = h.Water + 1.6f;
                    h.HalfX = h.Big ? 60f : 44f; h.HalfZ = 22f; h.OffX = 0f; h.OffZ = -20f; h.Blend = 35f;
                    { string why = Blocked(h, float.MaxValue); if (why != null) { last = why; continue; } }   // something is in the way here: try the next piece of shore
                    return true;
                }
            }
            Why["port: " + last] = Why.TryGetValue("port: " + last, out int cnt) ? cnt + 1 : 1;
            return false;
        }

        /// <summary>Where a spot is in a hub's own metres, measured from the edge of its levelled rectangle (0 inside).</summary>
        static float Outside(Hub h, Vector3 dir)
        {
            var v = (dir - h.Dir) * Planet.Radius;
            float dx = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(v, h.East) - h.OffX) - h.HalfX);
            float dz = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(v, h.North) - h.OffZ) - h.HalfZ);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Levels the ground under an airport, and the land behind a port's quay.</summary>
        public static void Ground(Vector3 dir, ref float h)
        {
            if (_grid.Count == 0 || !_grid.TryGetValue(CellKey(dir * Planet.Radius), out var l)) return;
            for (int i = 0; i < l.Count; i++)
            {
                var hub = l[i];
                float d = Outside(hub, dir);
                if (d >= hub.Blend) continue;
                if (hub.Port && h < hub.Water - 0.4f) continue;      // the harbour itself stays water
                h = Mathf.Lerp(h, hub.Level, Mathf.SmoothStep(0f, 1f, 1f - d / hub.Blend));
            }
        }

        /// <summary>Is this spot on an airport or a quay, or within <paramref name="metres"/> of one? Keeps trees, houses and animals off.</summary>
        public static bool Near(Vector3 dir, float metres)
        {
            if (_grid.Count == 0 || !_grid.TryGetValue(CellKey(dir * Planet.Radius), out var l)) return false;
            for (int i = 0; i < l.Count; i++) if (Outside(l[i], dir) <= metres) return true;
            return false;
        }

        /// <summary>Is this spot where no road, railway or river may be: on an airfield, or on the quay front of a port?</summary>
        internal static bool Forbidden(Vector3 dir)
        {
            if (_grid.Count == 0 || !_grid.TryGetValue(CellKey(dir * Planet.Radius), out var l)) return false;
            for (int i = 0; i < l.Count; i++)
            {
                var h = l[i];
                if (Outside(h, dir) > 0f) continue;
                if (!h.Port || Vector3.Dot((dir - h.Dir) * Planet.Radius, h.North) > -14f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ building near the player

        public void Init(Planet planet)
        {
            _planet = planet;
            _mat = Prims.VertexColored(0.25f, false, 0.1f);
            Load();
        }

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            foreach (var h in _all) if (h.Plane != null) Fly(h);
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.6f;
            var pd = _planet.WorldToDir(_planet.Player.position);
            Hub build = null; float best = BuildIn;
            foreach (var h in _all)
            {
                float d = (h.Dir - pd).magnitude * Planet.Radius;
                if (h.Go != null) { if (d > DropOut) Drop(h); continue; }
                if (d < best) { best = d; build = h; }
            }
            if (build != null) { if (build.Port) BuildPort(build); else BuildAirport(build); }
        }

        void Drop(Hub h)
        {
            if (h.Go != null) Destroy(h.Go);
            foreach (var m in h.Meshes) if (m != null) Destroy(m);
            h.Meshes.Clear();
            h.Go = null; h.Plane = null;
        }

        void OnDestroy()
        {
            foreach (var h in _all) Drop(h);
        }

        /// <summary>A point in the hub's own metres (x, z on the ground, y above its level), following the planet's curve.</summary>
        static Vector3 Pt(Hub h, float x, float z, float y)
            => Quaternion.Inverse(h.Rot) * ((h.Dir * Planet.Radius + h.East * x + h.North * z).normalized * (Planet.Radius + h.Level + y) - h.Dir * (Planet.Radius + h.Level));

        /// <summary>A flat patch between two corners, cut into pieces small enough to follow the planet's curve.</summary>
        static void Slab(MeshBuilder mb, Hub h, float x0, float z0, float x1, float z1, float y, Color col)
        {
            int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / 24f)), nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / 24f));
            var I = Matrix4x4.identity;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float a = Mathf.Lerp(x0, x1, i / (float)nx), b = Mathf.Lerp(x0, x1, (i + 1) / (float)nx);
                    float c = Mathf.Lerp(z0, z1, j / (float)nz), d = Mathf.Lerp(z0, z1, (j + 1) / (float)nz);
                    mb.Quad(I, Pt(h, a, c, y), Pt(h, a, d, y), Pt(h, b, d, y), Pt(h, b, c, y), col);
                }
        }

        /// <summary>A box standing at (x, z) with its base at height y, turned by a yaw about the up axis.</summary>
        static void Block(MeshBuilder mb, Hub h, float x, float z, float y, float sx, float sy, float sz, Color side, Color top, float yaw = 0f)
            => mb.Box(Matrix4x4.TRS(Pt(h, x, z, y + sy * 0.5f), Quaternion.Euler(0f, yaw, 0f), new Vector3(sx, sy, sz)), side, top);

        GameObject Make(Hub h, string name, MeshBuilder mb, bool collider, Transform parent)
        {
            var mesh = mb.ToMesh(name);
            if (mesh == null) return null;
            h.Meshes.Add(mesh);
            var go = new GameObject(name);
            if (parent == null)
            {
                go.transform.SetParent(_planet.transform, false);
                go.transform.localPosition = h.Dir * (Planet.Radius + h.Level);
                go.transform.localRotation = h.Rot;
            }
            else go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = collider ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        // ------------------------------------------------------------------ airports

        void BuildAirport(Hub h)
        {
            float L = h.Length, W = h.Big ? 20f : 16f, A = h.Big ? 62f : 42f;
            var rng = new System.Random(h.Name.Length * 31 + (int)(L + h.Dir.x * 9999f));
            var paint = new MeshBuilder();
            Slab(paint, h, -W / 2f, -L / 2f, W / 2f, L / 2f, Paint, Tarmac);
            for (float z = -L / 2f + 40f; z < L / 2f - 46f; z += 24f) Slab(paint, h, -0.35f, z, 0.35f, z + 12f, Paint + 0.04f, White);   // centre line
            for (int e = -1; e <= 1; e += 2)
            {
                Slab(paint, h, e * (W / 2f - 0.8f) - 0.25f, -L / 2f + 4f, e * (W / 2f - 0.8f) + 0.25f, L / 2f - 4f, Paint + 0.04f, White); // edge lines
                for (int k = 0; k < 6; k++)                                                                                               // threshold bars
                {
                    float x = (k - 2.5f) * (W - 4f) / 6f, z0 = e > 0 ? L / 2f - 22f : -L / 2f + 8f;
                    Slab(paint, h, x - 0.55f, z0, x + 0.55f, z0 + 14f, Paint + 0.04f, White);
                }
                Slab(paint, h, W / 2f, e * (L / 2f - 18f) - 4f, 30f, e * (L / 2f - 18f) + 4f, Paint, Taxi);                               // links to the taxiway
            }
            Slab(paint, h, W / 2f, -4f, 30f, 4f, Paint, Taxi);
            Slab(paint, h, 30f, -L / 2f + 10f, 38f, L / 2f - 10f, Paint, Taxi);                                                           // the taxiway
            Slab(paint, h, 33.85f, -L / 2f + 12f, 34.15f, L / 2f - 12f, Paint + 0.04f, Yellow);
            Slab(paint, h, 38f, -A, 80f, A, Paint, Apron);
            Slab(paint, h, 95f, -3.5f, 125f, 3.5f, Paint, Taxi);                                                                          // the road in from town
            h.Go = Make(h, "Airport " + h.Name, paint, false, null);
            if (h.Go == null) return;

            var mb = new MeshBuilder();
            // Terminal: a long hall with a band of glass, and a pier or two reaching onto the apron.
            Block(mb, h, 88f, 0f, 0f, 14f, 9f, A * 2f - 10f, Wall, RoofGrey);
            Block(mb, h, 88f, 0f, 3.2f, 14.3f, 3.4f, A * 2f - 16f, Glass, Glass);
            Block(mb, h, 88f, 0f, 9f, 9f, 1.2f, A * 2f - 26f, RoofGrey, White);
            for (int k = h.Big ? -1 : 0; k <= (h.Big ? 1 : 0); k += 2)
            {
                float z = k * A * 0.5f;
                Block(mb, h, 76f, z, 0f, 12f, 5f, 5f, Wall, RoofGrey);
                Block(mb, h, 76f, z, 2.2f, 12.2f, 1.6f, 5.2f, Glass, Glass);
                if (!h.Big) break;
            }
            // Control tower.
            mb.Cylinder(Matrix4x4.TRS(Pt(h, 92f, A + 12f, 0f), Quaternion.identity, new Vector3(4.2f, 20f, 4.2f)), 10, Wall, Wall);
            mb.Cylinder(Matrix4x4.TRS(Pt(h, 92f, A + 12f, 20f), Quaternion.identity, new Vector3(8f, 3.2f, 8f)), 10, Glass, RoofGrey);
            mb.Cylinder(Matrix4x4.TRS(Pt(h, 92f, A + 12f, 23.2f), Quaternion.identity, new Vector3(9f, 0.5f, 9f)), 10, RoofGrey, RoofGrey);
            Block(mb, h, 92f, A + 12f, 23.7f, 0.3f, 4f, 0.3f, Dark, Dark);
            // Hangars.
            for (int k = 0; k < (h.Big ? 2 : 1); k++)
            {
                float z = -(A + 22f + k * 30f);
                Block(mb, h, 64f, z, 0f, 28f, 9f, 22f, RoofGrey, Wall);
                Block(mb, h, 49.9f, z, 0f, 0.3f, 7.5f, 16f, Dark, Dark);
                mb.Roof(Matrix4x4.TRS(Pt(h, 64f, z, 9f), Quaternion.identity, new Vector3(28f, 3f, 22f)), Wall);
            }
            // Airliners parked nose-in at the terminal.
            int parked = h.Big ? 4 : 2;
            for (int k = 0; k < parked; k++)
            {
                float z = (k - (parked - 1) * 0.5f) * (A * 2f - 24f) / parked;
                if (h.Big && Mathf.Abs(Mathf.Abs(z) - A * 0.5f) < 13f) z += z > 0f ? 15f : -15f;
                if (!h.Big && Mathf.Abs(z) < 13f) z = z >= 0f ? 16f : -16f;
                Airliner(mb, Matrix4x4.TRS(Pt(h, 58f, z, Paint), Quaternion.Euler(0f, 90f, 0f), Vector3.one), Liveries[rng.Next(Liveries.Length)]);
            }
            Make(h, "Buildings", mb, true, h.Go.transform);

            // The airliner that flies.
            if (_airliner == null) { var am = new MeshBuilder(); Airliner(am, Matrix4x4.identity, Liveries[0]); _airliner = am.ToMesh("Airliner"); }
            var plane = new GameObject("Flight");
            plane.transform.SetParent(h.Go.transform, false);
            plane.AddComponent<MeshFilter>().sharedMesh = _airliner;
            plane.AddComponent<MeshRenderer>().sharedMaterial = _mat;
            h.Plane = plane.transform;
            h.Mode = 0; h.Hold = 2f + rng.Next(12);
            plane.SetActive(false);
        }

        static void Flat(MeshBuilder mb, Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            mb.Quad(m, a, b, c, d, col);
            mb.Quad(m, d, c, b, a, col);
        }

        /// <summary>An airliner 24 m long standing on its wheels at the origin, nose along +z.</summary>
        static void Airliner(MeshBuilder mb, Matrix4x4 m, Color livery)
        {
            var along = Quaternion.Euler(90f, 0f, 0f);       // turns the y axis of a cylinder to +z
            var grey = new Color(0.6f, 0.62f, 0.66f);
            mb.Cylinder(m * Matrix4x4.TRS(new Vector3(0f, 2.7f, -10f), along, new Vector3(2.7f, 19f, 2.7f)), 10, White, White);
            mb.Cone(m * Matrix4x4.TRS(new Vector3(0f, 2.7f, 9f), along, new Vector3(2.7f, 3.2f, 2.7f)), 10, White);
            mb.Cone(m * Matrix4x4.TRS(new Vector3(0f, 2.7f, -10f), Quaternion.Euler(-90f, 0f, 0f), new Vector3(2.7f, 4f, 2.7f)), 10, White);
            mb.Box(m * Matrix4x4.TRS(new Vector3(0f, 2.75f, -1f), Quaternion.identity, new Vector3(2.76f, 0.3f, 17.5f)), livery, livery);   // stripe
            mb.Box(m * Matrix4x4.TRS(new Vector3(0f, 3.45f, 8.3f), Quaternion.identity, new Vector3(2.1f, 0.5f, 1f)), Dark, Dark);           // cockpit glass
            for (int e = -1; e <= 1; e += 2)
            {
                Flat(mb, m, new Vector3(e * 1.2f, 2.1f, 3f), new Vector3(e * 1.2f, 2.1f, -2.6f), new Vector3(e * 11f, 2.7f, -5.2f), new Vector3(e * 11f, 2.7f, -3.4f), grey);
                Flat(mb, m, new Vector3(e * 0.8f, 3.1f, -10f), new Vector3(e * 0.8f, 3.1f, -12.4f), new Vector3(e * 4.6f, 3.3f, -13.6f), new Vector3(e * 4.6f, 3.3f, -12.4f), grey);
                mb.Cylinder(m * Matrix4x4.TRS(new Vector3(e * 4.3f, 1.3f, -1.2f), along, new Vector3(1.4f, 3.2f, 1.4f)), 8, grey, Dark);
                mb.Box(m * Matrix4x4.TRS(new Vector3(e * 1.9f, 0.6f, -1.2f), Quaternion.identity, new Vector3(0.5f, 1.2f, 0.9f)), Dark, Dark);
            }
            Flat(mb, m, new Vector3(0f, 3.7f, -9.2f), new Vector3(0f, 3.7f, -13f), new Vector3(0f, 8.4f, -14.4f), new Vector3(0f, 8.4f, -12.6f), livery);
            mb.Box(m * Matrix4x4.TRS(new Vector3(0f, 0.6f, 6.6f), Quaternion.identity, new Vector3(0.35f, 1.2f, 0.7f)), Dark, Dark);
        }

        /// <summary>One airliner per airport: it takes off, is gone a while, comes back in to land, and starts again.</summary>
        void Fly(Hub h)
        {
            float dt = Time.deltaTime, L = h.Length, pitch = 0f;
            switch (h.Mode)
            {
                case 0:                                         // waiting at the start of the runway
                    h.Hold -= dt;
                    h.Z = -L / 2f + 26f; h.V = 0f; h.Y = 0f;
                    if (h.Hold <= 0f) h.Mode = 1;
                    if (h.Hold < 6f && !h.Plane.gameObject.activeSelf) h.Plane.gameObject.SetActive(true);
                    break;
                case 1:                                         // take-off run and climb out
                    h.V = Mathf.Min(62f, h.V + 7.5f * dt);
                    h.Z += h.V * dt;
                    if (h.V > 40f) { pitch = Mathf.Min(11f, (h.V - 40f) * 2.5f + (h.Y > 0f ? 6f : 0f)); h.Y += h.V * Mathf.Sin(pitch * Mathf.Deg2Rad) * dt; }
                    if (h.Z > 1500f) { h.Mode = 2; h.Hold = 14f; h.Plane.gameObject.SetActive(false); }
                    break;
                case 2:                                         // away
                    h.Hold -= dt;
                    if (h.Hold <= 0f) { h.Mode = 3; h.Z = -1400f; h.V = 46f; h.Plane.gameObject.SetActive(true); }
                    break;
                case 3:                                         // final approach and landing roll
                    float touch = -L / 2f + 34f;
                    h.Z += h.V * dt;
                    if (h.Z < touch) { h.Y = (touch - h.Z) * 0.07f; pitch = 3f; }
                    else { h.Y = 0f; h.V = Mathf.Max(0f, h.V - 7f * dt); if (h.V <= 0.01f) { h.Mode = 4; h.Hold = 4f; } }
                    break;
                default:                                        // stopped: taken off the runway, then the next departure
                    h.Hold -= dt;
                    if (h.Hold <= 0f) { h.Mode = 0; h.Hold = 16f; h.Plane.gameObject.SetActive(false); }
                    break;
            }
            if (!h.Plane.gameObject.activeSelf) return;
            var up = (h.Dir * Planet.Radius + h.North * h.Z).normalized;
            var fwd = (h.North - up * Vector3.Dot(h.North, up)).normalized;
            h.Plane.localPosition = Pt(h, 0f, h.Z, Paint + h.Y);
            h.Plane.localRotation = Quaternion.Inverse(h.Rot) * Quaternion.LookRotation(fwd, up) * Quaternion.Euler(-pitch, 0f, 0f);
        }

        // ------------------------------------------------------------------ ports

        void BuildPort(Hub h)
        {
            float HX = h.HalfX, pierLen = h.Big ? 84f : 62f;
            var rng = new System.Random(h.Name.Length * 17 + (int)(h.Dir.z * 9999f));
            var mb = new MeshBuilder();
            var I = Matrix4x4.identity;
            // The quay: a concrete apron along the shore with walls down into the water.
            Slab(mb, h, -HX, -40f, HX, 3f, 0.15f, Concrete);
            Wall4(mb, h, -HX, -40f, HX, 3f, 0.15f, -5f, Concrete);
            Slab(mb, h, -HX, 1.6f, HX, 2f, 0.19f, Yellow);
            // Piers out into the harbour, each with a ship alongside if the water is deep enough.
            int piers = h.Big ? 2 : 1;
            for (int k = 0; k < piers; k++)
            {
                float x = piers == 1 ? 0f : (k == 0 ? -HX * 0.5f : HX * 0.5f);
                Slab(mb, h, x - 4f, 3f, x + 4f, 3f + pierLen, 0.15f, Concrete);
                Wall4(mb, h, x - 4f, 3f, x + 4f, 3f + pierLen, 0.15f, -1.1f, Concrete);
                for (float z = 9f; z < pierLen; z += 12f)
                    for (int e = -1; e <= 1; e += 2) Block(mb, h, x + e * 3f, 3f + z, -8f, 0.9f, 7.2f, 0.9f, Dark, Dark);
                float side = k == 0 ? 1f : -1f, sx = x + side * 10.5f, sz = 3f + pierLen * 0.55f;
                if (Planet.NaturalHeight(Off(h.Dir, h.East, sx, h.North, sz)) < h.Water - 1.5f)
                    Ship(mb, I * Matrix4x4.TRS(Pt(h, sx, sz, -1.6f), Quaternion.identity, Vector3.one), rng);
            }
            // Gantry cranes on the quay edge.
            int cranes = h.Big ? 3 : 1;
            for (int k = 0; k < cranes; k++)
            {
                float x = cranes == 1 ? HX * 0.45f : (k - 1) * HX * 0.42f + 10f;
                var col = k % 2 == 0 ? new Color(0.85f, 0.3f, 0.15f) : new Color(0.15f, 0.4f, 0.7f);
                for (int ex = -1; ex <= 1; ex += 2)
                    for (int ez = -1; ez <= 1; ez += 2) Block(mb, h, x + ex * 5f, -6f + ez * 5f, 0.15f, 1f, 17f, 1f, col, col);
                Block(mb, h, x, -6f, 17f, 11f, 1.4f, 11f, col, col);
                Block(mb, h, x, 4f, 18.4f, 2.2f, 1.8f, 36f, col, col);                   // the boom, out over the water
                Block(mb, h, x, 9f, 15.6f, 2.6f, 2.6f, 3.2f, White, col);                // the driver's cab
                Block(mb, h, x, -6f, 18.4f, 1.2f, 7f, 1.2f, col, col);
            }
            // Container stacks.
            for (float x = -HX + 5f; x < 4f; x += 7.4f)
                for (float z = -36f; z < -13f; z += 3.2f)
                {
                    int high = rng.Next(4);
                    for (int s = 0; s < high; s++)
                    {
                        var c = Boxes[rng.Next(Boxes.Length)];
                        Block(mb, h, x, z, 0.15f + s * 2.5f, 6.2f, 2.45f, 2.6f, c, Color.Lerp(c, Color.black, 0.2f));
                    }
                }
            // Warehouse and lighthouse.
            float wx = (12f + HX - 4f) * 0.5f, ww = HX - 20f;
            Block(mb, h, wx, -28f, 0.15f, ww, 7f, 16f, Wall, RoofGrey);
            mb.Roof(Matrix4x4.TRS(Pt(h, wx, -28f, 7.15f), Quaternion.identity, new Vector3(ww + 1f, 2.6f, 17f)), Rust);
            Block(mb, h, wx, -19.9f, 0.15f, ww * 0.3f, 5f, 0.3f, Dark, Dark);
            mb.Cylinder(Matrix4x4.TRS(Pt(h, HX - 4f, -2.5f, 0.15f), Quaternion.identity, new Vector3(3.4f, 6f, 3.4f)), 10, White, White);
            mb.Cylinder(Matrix4x4.TRS(Pt(h, HX - 4f, -2.5f, 6.15f), Quaternion.identity, new Vector3(3f, 4f, 3f)), 10, new Color(0.8f, 0.15f, 0.12f), White);
            mb.Cylinder(Matrix4x4.TRS(Pt(h, HX - 4f, -2.5f, 10.15f), Quaternion.identity, new Vector3(2.6f, 4f, 2.6f)), 10, White, White);
            mb.Cylinder(Matrix4x4.TRS(Pt(h, HX - 4f, -2.5f, 14.15f), Quaternion.identity, new Vector3(2.2f, 1.8f, 2.2f)), 10, new Color(1f, 0.93f, 0.6f), Dark);
            mb.Cone(Matrix4x4.TRS(Pt(h, HX - 4f, -2.5f, 15.95f), Quaternion.identity, new Vector3(2.8f, 1.6f, 2.8f)), 10, new Color(0.8f, 0.15f, 0.12f));
            h.Go = Make(h, "Port " + h.Name, mb, true, null);
        }

        /// <summary>Walls from the edges of a slab down to a depth.</summary>
        static void Wall4(MeshBuilder mb, Hub h, float x0, float z0, float x1, float z1, float top, float bottom, Color col)
        {
            var I = Matrix4x4.identity;
            void Run(float ax, float az, float bx, float bz)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(bz - az)) / 24f));
                for (int i = 0; i < n; i++)
                {
                    float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                    float px = Mathf.Lerp(ax, bx, t0), pz = Mathf.Lerp(az, bz, t0), qx = Mathf.Lerp(ax, bx, t1), qz = Mathf.Lerp(az, bz, t1);
                    Flat(mb, I, Pt(h, px, pz, top), Pt(h, qx, qz, top), Pt(h, qx, qz, bottom), Pt(h, px, pz, bottom), col * 0.92f);
                }
            }
            Run(x0, z0, x1, z0); Run(x1, z0, x1, z1); Run(x1, z1, x0, z1); Run(x0, z1, x0, z0);
        }

        /// <summary>A cargo ship 56 m long, bow towards +z, its waterline at y = 0 of the matrix.</summary>
        static void Ship(MeshBuilder mb, Matrix4x4 m, System.Random rng)
        {
            Color[] hulls = { new Color(0.12f, 0.2f, 0.36f), new Color(0.16f, 0.16f, 0.18f), new Color(0.5f, 0.14f, 0.12f), new Color(0.12f, 0.34f, 0.3f) };
            var hull = hulls[rng.Next(hulls.Length)]; var deck = new Color(0.45f, 0.3f, 0.24f);
            Matrix4x4 B(float x, float y, float z, float sx, float sy, float sz) => m * Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, new Vector3(sx, sy, sz));
            mb.Box(B(0f, 0.6f, -4f, 10f, 5.2f, 44f), hull, deck);
            mb.Box(B(0f, -1.7f, -4f, 10.05f, 0.9f, 44.05f), Rust, Rust);                                    // the red below the waterline
            // The bow: a wedge.
            Vector3 l = new Vector3(-5f, 3.2f, 18f), r = new Vector3(5f, 3.2f, 18f), tip = new Vector3(0f, 3.6f, 28f);
            Vector3 lb = new Vector3(-5f, -2f, 18f), rb = new Vector3(5f, -2f, 18f), tb = new Vector3(0f, -2f, 25f);
            mb.Tri(m, l, tip, r, deck);
            mb.Tri(m, r, tip, l, deck);
            Flat(mb, m, l, lb, tb, tip, hull);
            Flat(mb, m, r, tip, tb, rb, hull);
            // The bridge at the stern, with a funnel.
            mb.Box(B(0f, 7.2f, -21f, 9f, 8f, 6f), White, White);
            mb.Box(B(0f, 9.4f, -17.95f, 8f, 1.2f, 0.1f), Dark, Dark);
            mb.Box(B(0f, 12.4f, -22.5f, 2.4f, 3f, 2.6f), hull, Dark);
            // Containers on deck.
            for (int row = 0; row < 5; row++)
                for (int col = -1; col <= 1; col++)
                {
                    int high = 1 + rng.Next(3);
                    for (int s = 0; s < high; s++)
                    {
                        var c = Boxes[rng.Next(Boxes.Length)];
                        mb.Box(B(col * 2.9f, 4.45f + s * 2.5f, -14f + row * 6.6f, 2.7f, 2.45f, 6.2f), c, Color.Lerp(c, Color.black, 0.2f));
                    }
                }
        }
    }
}
