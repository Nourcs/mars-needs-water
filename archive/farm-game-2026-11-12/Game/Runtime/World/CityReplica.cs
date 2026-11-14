using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Real city cores rebuilt from real map data (Overture Maps: © OpenStreetMap contributors, Overture Maps
    /// Foundation; baked by tools/earth/bake_city.py into Resources/Cities/&lt;name&gt;.bytes): the real street
    /// network, every large building and tower as its real footprint at its real relative height, and houses
    /// lining the streets where the real city has low buildings. The planet is 255 times smaller than Earth, so
    /// a walkable city is enlarged; it brings its own flat ground and its own shoreline (see <see cref="Ground"/>),
    /// and the ordinary towns, world roads and trees stay out of it (<see cref="Covers"/>). Built in tiles near
    /// the player. Presentation only.
    /// </summary>
    public sealed class CityReplica : MonoBehaviour
    {
        const float Tile = 48f, BuildIn = 430f, DropOut = 540f, DetailIn = 190f, DetailOut = 250f, ColliderIn = 110f;
        const float Plateau = 3f, BayDepth = 9f; // the city's ground above its water, and its water's bed below the ground

        internal struct Street { public byte Cls; public bool Elevated; public Vector2[] P; public bool CapA, CapB; }
        internal struct Big { public float H; public Color Col; public byte Flag; public Vector2[] P; public ushort[] Tri; public Vector2 C; }
        internal struct Hill { public float X, Z, R0, R1, H; }
        internal struct Lot { public float X, Z, Yaw, YScale; public byte Model, Tint; }

        internal sealed class TileData
        {
            public int Tx, Tz;
            public readonly List<int> Streets = new List<int>(), Bigs = new List<int>(), Lots = new List<int>();
            public GameObject Base, LotsGo;
            public Mesh BaseMesh, LotsMesh;
            public bool Detail;
            public MeshCollider Col, LotsCol;
        }

        internal sealed class City
        {
            public string Name;
            public Vector3 Dir, East, North;
            public float HalfW, HalfD, Cell, Gx0, Gz0, CosReach;
            public float SiteLat, SiteLon, Lon0, Lat0, Lon1, Lat1, Scale, Base, Water;
            public bool Sea;
            public bool Flat; // a town: real streets and buildings laid on the game's own landscape, no ground of its own
            public Color[] Tints;
            public int Gw, Gd;
            public byte[] Land, Weight, Park;
            internal Street[] Streets; internal Big[] Bigs; internal Lot[] Lots;
            internal Hill[] Hills = new Hill[0];
            internal readonly Dictionary<int, TileData> Tiles = new Dictionary<int, TileData>();
            internal SurfaceSpot Spot;

            /// <summary>Local metres (x east, z north) of a direction, on the city's flat plan.</summary>
            public Vector2 Local(Vector3 dir)
            {
                var p = dir * (Planet.Radius / Vector3.Dot(dir, Dir)) - Dir * Planet.Radius;
                return new Vector2(Vector3.Dot(p, East), Vector3.Dot(p, North));
            }

            /// <summary>Bilinear sample of one of the ground rasters, 0..1; false outside it.</summary>
            public bool Sample(byte[] map, Vector2 l, out float v)
            {
                v = 0f;
                float u = (l.x - Gx0) / Cell, w = (l.y - Gz0) / Cell;
                if (u < 0f || w < 0f || u >= Gw - 1 || w >= Gd - 1) return false;
                int i = (int)u, j = (int)w; float fu = u - i, fw = w - j;
                int o = j * Gw + i;
                v = Mathf.Lerp(Mathf.Lerp(map[o], map[o + 1], fu), Mathf.Lerp(map[o + Gw], map[o + Gw + 1], fu), fw) / 255f;
                return true;
            }
        }

        internal static readonly List<City> All = new List<City>();
        static bool _loaded;

        static readonly float[] Width = { 5f, 5f, 4.6f, 4.2f, 3.8f, 3.4f, 3.4f, 2.8f, 2.8f };
        static readonly string[] Houses = { "building-small-a", "building-small-b", "building-small-c", "building-small-d" };
        static readonly Color Asphalt = new Color(0.3f, 0.31f, 0.34f), Line = new Color(0.92f, 0.9f, 0.82f), Concrete = new Color(0.68f, 0.68f, 0.7f);
        static readonly Color RoofGrey = new Color(0.56f, 0.57f, 0.6f);

        Planet _planet;
        Material _mat;
        float _timer;
        readonly List<(City c, TileData t, int op, float d)> _todo = new List<(City, TileData, int, float)>();

        // ------------------------------------------------------------------ data and the ground

        /// <summary>Reads every baked city. Must run before any terrain is built (the ground depends on it).</summary>
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var files = Resources.LoadAll<TextAsset>("Cities");
            System.Array.Sort(files, (x, y) => string.CompareOrdinal(x.name, y.name));
            foreach (var ta in files)
            {
                var c = Parse(ta.bytes);
                if (c == null) { Debug.LogWarning($"Cities/{ta.name} is not a city file (or an old one): skipped."); continue; }
                c.Name = ta.name;
                c.Dir = EarthData.Dir(c.SiteLat, c.SiteLon);
                Planet.TangentAt(c.Dir, out _, out var n);
                var rot = Quaternion.LookRotation(n, c.Dir);
                c.East = rot * Vector3.right; c.North = rot * Vector3.forward;
                // A city by the sea stands just above it. Inland it stands on the landscape it finds, and its rivers
                // and lakes lie a little below its streets.
                float lake = EarthData.LakeLevel(c.SiteLat, c.SiteLon) * Planet.LandStep;
                c.Water = c.Sea ? 0f : Mathf.Max(0f, Mathf.Max(lake, Planet.NaturalHeight(c.Dir) - Plateau));
                c.Base = c.Water + Plateau;
                float far = 0f;
                foreach (var hl in c.Hills) far = Mathf.Max(far, new Vector2(hl.X, hl.Z).magnitude + hl.R1);
                float reach = Mathf.Max(far, new Vector2(Mathf.Max(-c.Gx0, c.Gx0 + c.Gw * c.Cell), Mathf.Max(-c.Gz0, c.Gz0 + c.Gd * c.Cell)).magnitude) + 20f;
                c.CosReach = Mathf.Cos(reach / Planet.Radius);
                All.Add(c);
            }
            MoveLandmarks();
            // The ordinary towns inside a rebuilt city (and its bay) give way to it.
            Places.Load();
            for (int i = 0; i < Places.All.Count; i++)
            {
                var p = Places.All[i];
                if (Covers(p.Dir) < 0.2f && !Crowds(p)) continue;
                p.Radius = 0f;
                Places.All[i] = p;
            }
            LoadTowns();
        }

        /// <summary>
        /// A wonder that really stands inside a rebuilt city core goes to its real spot among the real streets.
        /// One that really stands outside the core, but would now be swallowed by the enlarged city, moves out
        /// past the city's edge on the side where it really lies.
        /// </summary>
        static void MoveLandmarks()
        {
            for (int i = 0; i < Landmarks.All.Count; i++)
            {
                var l = Landmarks.All[i];
                if (l.Kind == Landmarks.Kind.GoldenGate) continue; // placed by hand at the city's tip
                foreach (var c in All)
                {
                    if (Vector3.Dot(l.Dir, c.Dir) < 0.99f) continue;
                    float midLat = (c.Lat0 + c.Lat1) * 0.5f, midLon = (c.Lon0 + c.Lon1) * 0.5f;
                    var real = new Vector2((l.Lon - midLon) * 111320f * Mathf.Cos(midLat * Mathf.Deg2Rad) / c.Scale, (l.Lat - midLat) * 110574f / c.Scale);
                    if (Mathf.Abs(real.x) > c.HalfW - 8f || Mathf.Abs(real.y) > c.HalfD - 8f)
                    {
                        var g = c.Local(l.Dir);
                        if (Mathf.Abs(g.x) > c.HalfW + 110f || Mathf.Abs(g.y) > c.HalfD + 110f) continue; // not in the way
                        real = real * Mathf.Min((c.HalfW + 95f) / Mathf.Max(0.001f, Mathf.Abs(real.x)), (c.HalfD + 95f) / Mathf.Max(0.001f, Mathf.Abs(real.y)));
                    }
                    l.Dir = (c.Dir * Planet.Radius + c.East * real.x + c.North * real.y).normalized;
                    EarthData.LatLon(l.Dir, out float la, out float lo);
                    l.Lat = la; l.Lon = lo;
                    Landmarks.All[i] = l;
                    break;
                }
            }
        }

        /// <summary>Would this ordinary town's blocks reach into a rebuilt city (the old San Francisco beside the new one)?</summary>
        static bool Crowds(Places.Place p)
        {
            foreach (var c in All)
            {
                if (Vector3.Dot(p.Dir, c.Dir) < 0.9f) continue;
                var l = c.Local(p.Dir);
                float m = p.Radius + 25f;
                if (Mathf.Abs(l.x) < c.HalfW + m && Mathf.Abs(l.y) < c.HalfD + m) return true;
            }
            return false;
        }

        static City Parse(byte[] b)
        {
            if (b.Length < 80 || b[0] != 'M' || b[1] != 'N' || b[2] != 'W' || b[3] != 'C' || System.BitConverter.ToInt32(b, 4) != 2) return null;
            int o = 8;
            float F() { float v = System.BitConverter.ToSingle(b, o); o += 4; return v; }
            int I() { int v = System.BitConverter.ToInt32(b, o); o += 4; return v; }
            int U16() { int v = System.BitConverter.ToUInt16(b, o); o += 2; return v; }
            var c = new City { SiteLat = F(), SiteLon = F(), Lon0 = F(), Lat0 = F(), Lon1 = F(), Lat1 = F() };
            c.Sea = I() != 0;
            c.HalfW = F(); c.HalfD = F(); c.Scale = F();
            c.Cell = F(); c.Gw = I(); c.Gd = I(); c.Gx0 = F(); c.Gz0 = F();
            int cells = c.Gw * c.Gd;
            c.Land = new byte[cells]; c.Weight = new byte[cells]; c.Park = new byte[cells];
            System.Buffer.BlockCopy(b, o, c.Land, 0, cells); o += cells;
            System.Buffer.BlockCopy(b, o, c.Weight, 0, cells); o += cells;
            System.Buffer.BlockCopy(b, o, c.Park, 0, cells); o += cells;
            // Streets, cut into pieces of about 40 m so each lands in one tile.
            int ns = I();
            var streets = new List<Street>();
            for (int s = 0; s < ns; s++)
            {
                byte cls = b[o], fl = b[o + 1]; o += 2;
                int n = U16();
                var pts = new Vector2[n];
                for (int k = 0; k < n; k++) pts[k] = new Vector2(F(), F());
                Cut(streets, cls, fl != 0, pts);
            }
            c.Streets = streets.ToArray();
            int nb = I();
            c.Bigs = new Big[nb];
            for (int i = 0; i < nb; i++)
            {
                var g = new Big { H = F(), Col = new Color(b[o] / 255f, b[o + 1] / 255f, b[o + 2] / 255f), Flag = b[o + 3] };
                o += 4;
                int n = U16();
                g.P = new Vector2[n];
                var sum = Vector2.zero;
                for (int k = 0; k < n; k++) { g.P[k] = new Vector2(F(), F()); sum += g.P[k]; }
                g.C = sum / Mathf.Max(1, n);
                int nt = U16();
                g.Tri = new ushort[nt * 3];
                for (int k = 0; k < nt * 3; k++) g.Tri[k] = (ushort)U16();
                c.Bigs[i] = g;
            }
            int nl = I();
            c.Lots = new Lot[nl];
            for (int i = 0; i < nl; i++)
            {
                var l = new Lot { X = F(), Z = F(), Yaw = F(), Model = b[o], Tint = b[o + 1] };
                o += 2;
                l.YScale = F();
                c.Lots[i] = l;
            }
            // Hills the city adds to its ground (the bluffs a bridge lands on), heights above the city's ground.
            int nh = I();
            c.Hills = new Hill[nh];
            for (int i = 0; i < nh; i++) c.Hills[i] = new Hill { X = F(), Z = F(), R0 = F(), R1 = F(), H = F() };
            // The colours of its house fronts.
            int nc = I();
            c.Tints = new Color[Mathf.Max(1, nc)];
            c.Tints[0] = Color.white;
            for (int i = 0; i < nc; i++) { c.Tints[i] = new Color(b[o] / 255f, b[o + 1] / 255f, b[o + 2] / 255f); o += 3; }
            Sort(c);
            return c;
        }

        /// <summary>Cuts a street into pieces of about 40 m, so each lands in one tile.</summary>
        static void Cut(List<Street> into, byte cls, bool elevated, Vector2[] pts)
        {
            int n = pts.Length;
            if (n < 2) return;
            var piece = new List<Vector2> { pts[0] };
            float len = 0f; bool first = true;
            for (int k = 1; k < n; k++)
            {
                len += (pts[k] - pts[k - 1]).magnitude;
                piece.Add(pts[k]);
                if (len > 40f || k == n - 1)
                {
                    into.Add(new Street { Cls = cls, Elevated = elevated, P = piece.ToArray(), CapA = first, CapB = k == n - 1 });
                    piece = new List<Vector2> { pts[k] }; len = 0f; first = false;
                }
            }
        }

        /// <summary>Sorts a city's streets, buildings and house lots into its tiles.</summary>
        static void Sort(City c)
        {
            TileData T(Vector2 p)
            {
                int tx = Mathf.FloorToInt(p.x / Tile), tz = Mathf.FloorToInt(p.y / Tile), key = (tz + 512) * 4096 + tx + 512;
                if (!c.Tiles.TryGetValue(key, out var t)) c.Tiles[key] = t = new TileData { Tx = tx, Tz = tz };
                return t;
            }
            for (int i = 0; i < c.Streets.Length; i++) T(c.Streets[i].P[c.Streets[i].P.Length / 2]).Streets.Add(i);
            for (int i = 0; i < c.Bigs.Length; i++) T(c.Bigs[i].C).Bigs.Add(i);
            for (int i = 0; i < c.Lots.Length; i++) T(new Vector2(c.Lots[i].X, c.Lots[i].Z)).Lots.Add(i);
        }

        // ------------------------------------------------------------------ towns

        // Every other town: the real streets and buildings of its real centre (tools/earth/bake_towns.py, in a few
        // files, Resources/Towns/towns*.bytes), read town by town as the player comes near. A town keeps the size and
        // the ground it has in the game; it only swaps the made-up blocks of Cities for the real ones.
        struct TownRef { public int Place, Offset; public byte[] Data; public Vector3 Dir; public float Radius, CosIn, CosOut; public City Live; }
        static TownRef[] _towns = new TownRef[0];
        static bool[] _isTown;

        /// <summary>Does this place (index into Places.All) have a real town centre to build?</summary>
        public static bool IsTown(int place) => _isTown != null && place >= 0 && place < _isTown.Length && _isTown[place];

        static void LoadTowns()
        {
            var list = new List<TownRef>();
            _isTown = new bool[Places.All.Count];
            var farm = EarthData.Dir(Planet.FarmLat, Planet.FarmLon);
            Planet.TangentAt(farm, out var farmEast, out var farmNorth);
            foreach (var ta in Resources.LoadAll<TextAsset>("Towns"))
            {
            var b = ta.bytes;
            if (b.Length < 12 || b[0] != 'M' || b[1] != 'N' || b[2] != 'W' || b[3] != 'T') { Debug.LogWarning("Towns/" + ta.name + " is not a towns file: skipped."); continue; }
            int n = System.BitConverter.ToInt32(b, 4);
            for (int i = 0; i < n; i++)
            {
                int o = 8 + 20 * i;
                int place = System.BitConverter.ToInt32(b, o);
                float lat = System.BitConverter.ToSingle(b, o + 4), lon = System.BitConverter.ToSingle(b, o + 8), radius = System.BitConverter.ToSingle(b, o + 12);
                if (place < 0 || place >= Places.All.Count || _isTown[place]) continue;
                var p = Places.All[place];
                // Skipped if the list of places has changed since the bake, or the town gave way to a rebuilt city core.
                if (Mathf.Abs(p.Lat - lat) > 0.01f || Mathf.Abs(p.Lon - lon) > 0.01f || p.Radius <= 0f) continue;
                // The town is as big as it was baked (towns that would overlap were shrunk until they just touch), and
                // a town that stands on the farm moves aside, clear of it.
                p.Radius = radius;
                float need = Planet.FarmHalf + 45f + radius, off = (p.Dir - farm).magnitude * Planet.Radius;
                if (off < need)
                {
                    var away = p.Dir - farm; away -= farm * Vector3.Dot(away, farm);
                    away = off > 30f ? away.normalized : (farmEast - farmNorth).normalized; // on top of the farm: to the south-east
                    p.Dir = (farm * Planet.Radius + away * need).normalized;
                    EarthData.LatLon(p.Dir, out float la, out float lo);
                    p.Lat = la; p.Lon = lo;
                }
                Places.All[place] = p;
                list.Add(new TownRef
                {
                    Place = place, Offset = System.BitConverter.ToInt32(b, o + 16), Data = b, Dir = p.Dir, Radius = p.Radius,
                    CosIn = Mathf.Cos((p.Radius + BuildIn) / Planet.Radius), CosOut = Mathf.Cos((p.Radius + DropOut + 60f) / Planet.Radius),
                });
                _isTown[place] = true;
            }
            }
            _towns = list.ToArray();
        }

        static City ParseTown(TownRef t)
        {
            var b = t.Data; int o = t.Offset;
            const float q = 0.02f; // file units: 2 cm
            Vector2 Pt() { var v = new Vector2(System.BitConverter.ToInt16(b, o) * q, System.BitConverter.ToInt16(b, o + 2) * q); o += 4; return v; }
            var c = new City { Name = "town" + t.Place, Flat = true, Dir = t.Dir, HalfW = t.Radius, HalfD = t.Radius, Base = -1e6f, Lots = new Lot[0], Tints = new[] { Color.white } };
            Planet.TangentAt(c.Dir, out _, out var north);
            var rot = Quaternion.LookRotation(north, c.Dir);
            c.East = rot * Vector3.right; c.North = rot * Vector3.forward;
            int ns = System.BitConverter.ToUInt16(b, o); o += 2;
            var streets = new List<Street>();
            for (int s = 0; s < ns; s++)
            {
                byte cls = b[o]; int n = b[o + 1]; o += 2;
                var pts = new Vector2[n];
                for (int k = 0; k < n; k++) pts[k] = Pt();
                Cut(streets, cls, false, pts);
            }
            c.Streets = streets.ToArray();
            int nb = System.BitConverter.ToUInt16(b, o); o += 2;
            c.Bigs = new Big[nb];
            for (int i = 0; i < nb; i++)
            {
                var g = new Big { H = b[o] * 0.5f, Col = new Color(b[o + 1] / 255f, b[o + 2] / 255f, b[o + 3] / 255f) };
                int n = b[o + 4]; o += 5;
                g.P = new Vector2[n];
                var sum = Vector2.zero;
                for (int k = 0; k < n; k++) { g.P[k] = Pt(); sum += g.P[k]; }
                g.C = sum / Mathf.Max(1, n);
                int nt = b[o]; o++;
                g.Tri = new ushort[nt * 3];
                for (int k = 0; k < nt * 3; k++) g.Tri[k] = b[o + k];
                o += nt * 3;
                c.Bigs[i] = g;
            }
            Sort(c);
            return c;
        }

        /// <summary>Buildings keep off the farm and the wonders.</summary>
        bool Blocked(Vector3 d)
        {
            if (Vector3.Dot(d, _planet.FarmDir) > 0.9998f && _planet.FarmCheb(d) < Planet.FarmHalf + 25f) return true;
            float cos = Mathf.Cos(Landmarks.Clearing / Planet.Radius);
            foreach (var l in Landmarks.All) if (Vector3.Dot(d, l.Dir) > cos) return true;
            return false;
        }

        /// <summary>
        /// The city's own ground: a flat plateau where the real city has land, a shallow bay where it has water,
        /// blended into the natural landscape at the edges. Called for every height the planet works out.
        /// </summary>
        public static void Ground(Vector3 dir, ref float h)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (Vector3.Dot(dir, c.Dir) < c.CosReach) continue;
                var l = c.Local(dir);
                if (c.Sample(c.Weight, l, out float w) && w > 0f)
                {
                    c.Sample(c.Land, l, out float land);
                    float ch = Mathf.Lerp(c.Base - BayDepth, c.Base, Mathf.SmoothStep(0f, 1f, (land - 0.3f) / 0.35f));
                    h = Mathf.Lerp(h, ch, Mathf.SmoothStep(0f, 1f, w));
                }
                for (int k = 0; k < c.Hills.Length; k++)
                {
                    var hl = c.Hills[k];
                    float d = (l - new Vector2(hl.X, hl.Z)).magnitude;
                    if (d >= hl.R1) continue;
                    h = Mathf.Max(h, c.Base + hl.H * Mathf.SmoothStep(0f, 1f, 1f - (d - hl.R0) / (hl.R1 - hl.R0)));
                }
                return;
            }
        }

        /// <summary>How much a spot belongs to a rebuilt city: 0 outside, 1 well inside (its land or its bay).</summary>
        public static float Covers(Vector3 dir)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (Vector3.Dot(dir, c.Dir) < c.CosReach) continue;
                if (c.Sample(c.Weight, c.Local(dir), out float w)) return w;
            }
            return 0f;
        }

        /// <summary>How many cities are rebuilt.</summary>
        public static int Count => All.Count;

        static readonly Dictionary<string, string> Titles = new Dictionary<string, string>
        {
            { "sanfrancisco", "San Francisco" }, { "newyork", "New York" }, { "losangeles", "Los Angeles" }, { "hongkong", "Hong Kong" }, { "rio", "Rio de Janeiro" },
        };

        /// <summary>The middle of a rebuilt city.</summary>
        public static Vector3 Centre(int i) => All[i].Dir;

        /// <summary>A city's name for people.</summary>
        public static string Title(int i)
        {
            string n = All[i].Name;
            return Titles.TryGetValue(n, out var t) ? t : char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        /// <summary>A good place to arrive in a city: on the street nearest its middle.</summary>
        public static Vector3 Arrival(int i)
        {
            var c = All[i];
            var best = Vector2.zero; float bd = float.MaxValue;
            foreach (var s in c.Streets)
            {
                if (s.Elevated) continue;
                var p = s.P[0];
                float d = p.sqrMagnitude;
                if (d < bd && c.Sample(c.Land, p, out float land) && land > 0.9f) { bd = d; best = p; }
            }
            return (c.Dir * Planet.Radius + c.East * best.x + c.North * best.y).normalized;
        }

        /// <summary>The water level inside a rebuilt city (its bay, river or lake); false outside one.</summary>
        public static bool Water(Vector3 dir, ref float level)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (Vector3.Dot(dir, c.Dir) < c.CosReach) continue;
                if (c.Sample(c.Weight, c.Local(dir), out float w) && w > 0.5f) { level = c.Water; return true; }
            }
            return false;
        }

        /// <summary>Is this spot in one of a rebuilt city's parks (grass and trees, not paving)?</summary>
        public static bool Park(Vector3 dir)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (Vector3.Dot(dir, c.Dir) < c.CosReach) continue;
                return c.Sample(c.Park, c.Local(dir), out float p) && p > 0.5f;
            }
            return false;
        }

        /// <summary>Is this spot on the paved land of a rebuilt city?</summary>
        public static bool Paved(Vector3 dir)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (Vector3.Dot(dir, c.Dir) < c.CosReach) continue;
                var l = c.Local(dir);
                if (Mathf.Abs(l.x) > c.HalfW + 4f || Mathf.Abs(l.y) > c.HalfD + 4f) return false;
                return c.Sample(c.Land, l, out float land) && land > 0.5f && !(c.Sample(c.Park, l, out float park) && park > 0.5f);
            }
            return false;
        }

        // ------------------------------------------------------------------ building tiles near the player

        public void Init(Planet planet)
        {
            _planet = planet;
            _mat = Prims.VertexColored(0.3f, false, 0.2f);
            if (_mat.HasProperty("_Windows")) _mat.SetFloat("_Windows", 1f);
            Load();
            foreach (var c in All) c.Spot = new SurfaceSpot(planet, c.Dir, 0f, c.Base);
        }

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            if (_todo.Count > 0)
            {
                var j = _todo[_todo.Count - 1];
                _todo.RemoveAt(_todo.Count - 1);
                if (j.op == 0 && j.t.Base == null) BuildBase(j.c, j.t);
                else if (j.op == 1 && j.t.Base != null) BuildLots(j.c, j.t, true);
                else if (j.op == 2 && j.t.Base != null) BuildLots(j.c, j.t, false);
            }
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.35f;
            var pd = _planet.WorldToDir(_planet.Player.position);
            _todo.Clear();
            foreach (var c in All) Tick(c, pd);
            for (int i = 0; i < _towns.Length; i++)
            {
                float cos = Vector3.Dot(pd, _towns[i].Dir);
                var live = _towns[i].Live;
                if (live == null)
                {
                    if (cos < _towns[i].CosIn) continue;
                    live = _towns[i].Live = ParseTown(_towns[i]);
                    live.Spot = new SurfaceSpot(_planet, live.Dir, 0f, _planet.Height(live.Dir));
                }
                Tick(live, pd);
                if (cos < _towns[i].CosOut)
                {
                    foreach (var t in live.Tiles.Values) Clear(t);
                    _towns[i].Live = null;
                }
            }
            _todo.Sort((a, b) => b.d.CompareTo(a.d)); // nearest last (popped first)
        }

        /// <summary>Builds, refines and drops one city's tiles for where the player is.</summary>
        void Tick(City c, Vector3 pd)
        {
            bool near = Vector3.Dot(pd, c.Dir) > Mathf.Cos((c.HalfW + c.HalfD + DropOut) / Planet.Radius);
            var pl = near ? c.Local(pd) : Vector2.zero;
            foreach (var t in c.Tiles.Values)
            {
                float d = near ? (new Vector2((t.Tx + 0.5f) * Tile, (t.Tz + 0.5f) * Tile) - pl).magnitude : float.MaxValue;
                if (t.Base == null) { if (d < BuildIn) _todo.Add((c, t, 0, d)); continue; }
                if (d > DropOut) { Clear(t); continue; }
                if (!t.Detail && d < DetailIn && t.Lots.Count > 0) _todo.Add((c, t, 1, d));
                else if (t.Detail && d > DetailOut) _todo.Add((c, t, 2, d));
                bool close = d < ColliderIn;
                if (close && t.Col == null && t.BaseMesh != null) { t.Col = t.Base.AddComponent<MeshCollider>(); t.Col.sharedMesh = t.BaseMesh; }
                else if (!close && t.Col != null) { Destroy(t.Col); t.Col = null; }
                if (close && t.LotsCol == null && t.LotsGo != null && t.LotsMesh != null) { t.LotsCol = t.LotsGo.AddComponent<MeshCollider>(); t.LotsCol.sharedMesh = t.LotsMesh; }
                else if (!close && t.LotsCol != null) { Destroy(t.LotsCol); t.LotsCol = null; }
            }
        }

        static void Clear(TileData t)
        {
            if (t.Base != null) Destroy(t.Base);
            if (t.LotsGo != null) Destroy(t.LotsGo);
            if (t.BaseMesh != null) Destroy(t.BaseMesh);
            if (t.LotsMesh != null) Destroy(t.LotsMesh);
            t.Base = null; t.LotsGo = null; t.BaseMesh = null; t.LotsMesh = null; t.Col = null; t.LotsCol = null; t.Detail = false;
        }

        void BuildBase(City c, TileData t)
        {
            var mb = new MeshBuilder();
            foreach (int i in t.Streets) AddStreet(mb, c, c.Streets[i]);
            foreach (int i in t.Bigs) AddBig(mb, c, c.Bigs[i]);
            var mesh = mb.ToMesh($"City_{c.Name}_{t.Tx}_{t.Tz}");
            // A tile with nothing in it still gets an (empty) object, so it is not tried again every frame.
            t.BaseMesh = mesh;
            t.Base = c.Spot.Make($"City_{c.Name}_{t.Tx}_{t.Tz}", mesh, _mat, true);
            BuildLots(c, t, false);
        }

        void BuildLots(City c, TileData t, bool detail)
        {
            if (t.LotsGo != null) Destroy(t.LotsGo);
            if (t.LotsMesh != null) Destroy(t.LotsMesh);
            t.LotsGo = null; t.LotsMesh = null; t.LotsCol = null;
            t.Detail = detail;
            if (t.Lots.Count == 0) return;
            var mb = new MeshBuilder();
            foreach (int i in t.Lots)
            {
                var l = c.Lots[i];
                var g = c.Spot.Ground(l.X, l.Z, out float h);
                if (h < c.Base - 1.5f) continue; // never in the water
                var k = Kit.Get(Houses[l.Model % Houses.Length]);
                var tint = c.Tints[l.Tint % c.Tints.Length];
                const float size = 5.2f;
                var rot = Quaternion.Euler(0f, l.Yaw, 0f);
                if (detail && k != null)
                    mb.Model(Matrix4x4.TRS(g + new Vector3(0f, -0.15f, 0f), rot, new Vector3(size, size * l.YScale, size)), k, tint);
                else
                {
                    float hgt = (k != null ? k.Size.y : 1f) * size * l.YScale;
                    var wall = new Color(0.86f * tint.r, 0.82f * tint.g, 0.76f * tint.b, 0.9f);
                    mb.Box(Matrix4x4.TRS(g + new Vector3(0f, hgt * 0.5f - 0.5f, 0f), rot, new Vector3(size * 0.88f, hgt + 1f, size * 0.88f)), wall, RoofGrey);
                }
            }
            var mesh = mb.ToMesh($"CityHouses_{c.Name}_{t.Tx}_{t.Tz}");
            if (mesh == null) return;
            t.LotsMesh = mesh;
            t.LotsGo = c.Spot.Make(mesh.name, mesh, _mat, true);
        }

        void AddStreet(MeshBuilder mb, City c, Street s)
        {
            var I = Matrix4x4.identity;
            float half = Width[Mathf.Min(s.Cls, Width.Length - 1)] * 0.5f;
            float lift = s.Elevated ? 6.5f : 0.18f;
            // Cross-sections every 6 m or so along the piece.
            var pts = new List<Vector2>(); var dirs = new List<Vector2>();
            for (int k = 0; k < s.P.Length - 1; k++)
            {
                var a = s.P[k]; var b = s.P[k + 1];
                float len = (b - a).magnitude;
                if (len < 0.01f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 6f));
                for (int q = pts.Count == 0 ? 0 : 1; q <= n; q++) { pts.Add(Vector2.Lerp(a, b, q / (float)n)); dirs.Add((b - a) / len); }
            }
            if (pts.Count < 2) return;
            var L = new Vector3[pts.Count]; var R = new Vector3[pts.Count]; var up = new Vector3[pts.Count];
            var wet = new bool[pts.Count]; // a town's street stops at the water's edge, the farm and the wonders
            for (int k = 0; k < pts.Count; k++)
            {
                var f = k > 0 && k < pts.Count - 1 ? (dirs[k] + dirs[k - 1]).normalized : dirs[k];
                var side = new Vector2(f.y, -f.x); // to the right of the way we are going
                var pl = pts[k] - side * half; var pr = pts[k] + side * half;
                var gl = c.Spot.Ground(pl.x, pl.y, out float hl); var gr = c.Spot.Ground(pr.x, pr.y, out float hr);
                // On the ground a street lies on the city's plateau; over the bay it carries on level, as a pier or bridge.
                float y = Mathf.Max(Mathf.Max(hl, hr), c.Base);
                up[k] = c.Spot.DirAt(pts[k].x, pts[k].y);
                if (c.Flat) wet[k] = Mathf.Min(hl, hr) < Planet.WaterLevel(up[k]) + 0.3f || Blocked(up[k]);
                var upL = Quaternion.Inverse(c.Spot.Rot) * up[k];
                L[k] = gl + upL * (y - hl + lift); R[k] = gr + upL * (y - hr + lift);
                up[k] = upL;
            }
            for (int k = 0; k < pts.Count - 1; k++)
            {
                if (wet[k] || wet[k + 1]) continue;
                mb.Quad(I, L[k], L[k + 1], R[k + 1], R[k], Asphalt);
                if (s.Elevated)
                {
                    // A viaduct: slab sides and underside, a parapet, and a pier now and then.
                    Vector3 d0 = up[k] * 0.8f, d1 = up[k + 1] * 0.8f;
                    mb.Quad(I, L[k], L[k] - d0, L[k + 1] - d1, L[k + 1], Concrete);
                    mb.Quad(I, R[k], R[k + 1], R[k + 1] - d1, R[k] - d0, Concrete);
                    mb.Quad(I, L[k] - d0, R[k] - d0, R[k + 1] - d1, L[k + 1] - d1, Concrete);
                    if (k % 2 == 0)
                    {
                        var mid = (L[k] + R[k]) * 0.5f;
                        mb.Box(Matrix4x4.TRS(mid - up[k] * (0.8f + 5f), Quaternion.identity, new Vector3(1.1f, 10f, 1.1f)), Concrete, Concrete);
                    }
                }
                else if (s.Cls <= 3 && k % 2 == 0)
                {
                    Vector3 m0 = (L[k] + R[k]) * 0.5f + up[k] * 0.03f, m1 = (L[k + 1] + R[k + 1]) * 0.5f + up[k + 1] * 0.03f;
                    var sd = (R[k] - L[k]).normalized * 0.08f;
                    var a = Vector3.Lerp(m0, m1, 0.2f); var b = Vector3.Lerp(m0, m1, 0.8f);
                    mb.Quad(I, a - sd, b - sd, b + sd, a + sd, Line);
                }
            }
            // Round ends where the real street ends, so streets that meet at a corner close up.
            for (int e = 0; e < 2; e++)
            {
                if (!(e == 0 ? s.CapA : s.CapB)) continue;
                int k = e == 0 ? 0 : pts.Count - 1;
                if (wet[k]) continue;
                var ctr = (L[k] + R[k]) * 0.5f + up[k] * 0.005f;
                var x = (R[k] - L[k]) * 0.5f; var z = Vector3.Cross(x, up[k]);
                for (int q = 0; q < 10; q++)
                {
                    float a0 = q * Mathf.PI * 0.2f, a1 = (q + 1) * Mathf.PI * 0.2f;
                    mb.Tri(I, ctr, ctr + x * Mathf.Cos(a1) + z * Mathf.Sin(a1), ctr + x * Mathf.Cos(a0) + z * Mathf.Sin(a0), Asphalt);
                }
            }
        }

        void AddBig(MeshBuilder mb, City c, Big g)
        {
            var I = Matrix4x4.identity;
            var b0 = c.Spot.Ground(g.C.x, g.C.y, out float h0);
            float baseY = Mathf.Max(h0, c.Base) - h0; // piers and sheds over the bay stand at street level
            var up = Quaternion.Inverse(c.Spot.Rot) * c.Spot.DirAt(g.C.x, g.C.y);
            int n = g.P.Length;
            float area = 0f;
            for (int k = 0; k < n; k++) { var p = g.P[k]; var q = g.P[(k + 1) % n]; area += p.x * q.y - q.x * p.y; }
            bool ccw = area > 0f;
            float bot = -2f - baseY; // walls reach below the ground, and down into the water under a pier
            if (c.Flat)
            {
                // A town stands on the landscape as it is: nothing in the water, on a world road, the farm or a wonder,
                // and walls deep enough for the lowest corner on a slope.
                var dc = c.Spot.DirAt(g.C.x, g.C.y);
                if (h0 < Planet.WaterLevel(dc) + 0.4f || Roads.Near(dc, 4f) || Hubs.Near(dc, 6f) || Rivers.Near(dc, 3f) || Blocked(dc)) return;
                float low = h0;
                for (int k = 0; k < n; k += Mathf.Max(1, n / 4)) { c.Spot.Ground(g.P[k].x, g.P[k].y, out float hk); low = Mathf.Min(low, hk); }
                bot = low - h0 - 1.5f;
            }
            // Local position of a footprint corner at a height, pulled towards the middle by a factor (tapers).
            Vector3 V(int k, float y, float shrink)
            {
                var p = Vector2.Lerp(g.C, g.P[k], shrink) - g.C;
                return b0 + new Vector3(p.x, 0f, p.y) + up * (baseY + y);
            }
            var wall = new Color(g.Col.r, g.Col.g, g.Col.b, g.H >= 3.2f ? 0.9f : 1f); // alpha 0.9 = windows
            var roof = Color.Lerp(g.Col, RoofGrey, 0.6f);
            void Ring(float y0, float s0, float y1, float s1)
            {
                for (int k = 0; k < n; k++)
                {
                    int a = ccw ? k : (k + 1) % n, b = ccw ? (k + 1) % n : k;
                    mb.Quad(I, V(a, y0, s0), V(a, y1, s1), V(b, y1, s1), V(b, y0, s0), wall);
                }
            }
            void Roof(float y, float s)
            {
                for (int k = 0; k < g.Tri.Length; k += 3) mb.Tri(I, V(g.Tri[k], y, s), V(g.Tri[k + 2], y, s), V(g.Tri[k + 1], y, s), roof);
            }
            if (g.Flag == 1)
            {
                // A pyramid: straight up a little, then all the way to a point.
                Ring(bot, 1f, g.H * 0.06f, 1f);
                Ring(g.H * 0.06f, 1f, g.H, 0.02f);
            }
            else if (g.Flag == 2)
            {
                // A tower that slims towards a rounded crown.
                Ring(bot, 1f, g.H * 0.6f, 1f);
                Ring(g.H * 0.6f, 1f, g.H * 0.86f, 0.86f);
                Ring(g.H * 0.86f, 0.86f, g.H, 0.62f);
                Roof(g.H, 0.62f);
            }
            else if (g.H > 28f)
            {
                // Tall towers step in near the top.
                Ring(bot, 1f, g.H * 0.88f, 1f);
                Roof(g.H * 0.88f, 1f);
                Ring(g.H * 0.88f, 0.72f, g.H, 0.72f);
                Roof(g.H, 0.72f);
            }
            else
            {
                Ring(bot, 1f, g.H, 1f);
                Roof(g.H, 1f);
            }
        }

        void OnDestroy()
        {
            foreach (var c in All) foreach (var t in c.Tiles.Values) Clear(t);
            for (int i = 0; i < _towns.Length; i++)
            {
                if (_towns[i].Live == null) continue;
                foreach (var t in _towns[i].Live.Tiles.Values) Clear(t);
                _towns[i].Live = null;
            }
        }
    }
}
