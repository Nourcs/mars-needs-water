using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The world's real roads (Natural Earth, baked by tools/earth/bake_roads.py into Resources/Earth/earth_roads):
    /// highways, ordinary roads, and short link roads to towns and landmarks that no real road reaches. The file is
    /// cut into chunks of a few hundred metres; only chunks near the player are decoded, registered for
    /// <see cref="Near"/> (which keeps trees, grass and houses off the roads) and given a mesh. Roads run through
    /// towns as their main streets, meet in round junctions, and cross water on bridges. The same file carries the
    /// real railways (class 3: ballast, sleepers and two rails, with a few trains running on the lines near the
    /// player) and a station (class 4) where a line passes a town; trains call at them. Presentation only.
    /// </summary>
    public sealed class Roads : MonoBehaviour
    {
        const float Step = 5f;                          // metres between cross-sections
        const float ActiveRange = 1900f, BuildIn = 950f, DropOut = 1250f, ColliderIn = 120f;
        const float Cell = 200f, Margin = 62f, Sample = 40f; // lookup grid: one cell finds every road within 40 m
        const float Lift = 0.28f, RailLift = 0.36f, RailTop = 0.16f, Gauge = 0.72f;
        const int MaxTrains = 3, TrainCars = 5;
        const float CarGap = 7.7f, TrainSpeed = 14f;

        internal sealed class Chunk
        {
            public int First, N;
            public int Index;
            public int Line;                            // chunks of one line share this
            public byte Cls, Flags;                     // class 0 highway, 1 road, 2 link, 3 railway, 4 station; flags 1/2 = the line really ends at the start/end
            public float[] Cum;                         // railways: metres along the chunk at each point
            public Vector3 Centre; public float Rad;
            public Vector3[] P;                         // unit directions, decoded while the chunk is active
            public int State;                           // 0 unknown, 1 good, 2 dropped (a link that would run through water)
            public bool Active;
            public GameObject Go; public Mesh Mesh; public MeshCollider Col;
            public Section Raw, Prof;                   // what the ground asks of the road here, and the road's final shape
            public float Half => Cls == 0 ? 3.6f : Cls == 1 ? 2.7f : Cls == 2 ? 2.1f : Cls == 3 ? 1.9f : 7f;
        }

        internal static Chunk[] _chunks;
        static byte[] _raw;
        static int _pts;
        static bool _loaded, _hasFocus;
        static Vector3 _focus;
        static readonly List<Chunk> _active = new List<Chunk>();
        static readonly List<Chunk> _stations = new List<Chunk>();
        static readonly Dictionary<long, List<Chunk>> _grid = new Dictionary<long, List<Chunk>>();

        Planet _planet;
        Material _mat;
        float _timer;
        readonly List<Chunk> _want = new List<Chunk>();
        readonly List<Train> _trains = new List<Train>();
        readonly System.Random _rng = new System.Random(7);
        float _trainTimer = 3f;

        static readonly Color Asphalt = new Color(0.27f, 0.28f, 0.31f), Line = new Color(0.93f, 0.91f, 0.82f), Yellow = new Color(0.95f, 0.76f, 0.2f);
        static readonly Color Verge = new Color(0.47f, 0.44f, 0.4f), Concrete = new Color(0.7f, 0.7f, 0.72f);
        static readonly Color Ballast = new Color(0.5f, 0.48f, 0.45f), Sleeper = new Color(0.31f, 0.24f, 0.19f), Steel = new Color(0.6f, 0.61f, 0.65f);

        public void Init(Planet planet)
        {
            _planet = planet;
            _mat = Prims.VertexColored(0.25f, false, 0.1f);
            Load();
        }

        // ------------------------------------------------------------------ data

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _chunks = new Chunk[0];
            var ta = Resources.Load<TextAsset>("Earth/earth_roads");
            if (ta == null) { Debug.LogWarning("Earth/earth_roads missing: no roads."); return; }
            var b = ta.bytes;
            if (b.Length < 16 || b[0] != 'M' || b[1] != 'N' || b[2] != 'W' || b[3] != 'R') { Debug.LogWarning("Earth/earth_roads is not a roads file."); return; }
            int n = System.BitConverter.ToInt32(b, 8);
            if (b.Length < 16 + n * 20) return;
            _raw = b;
            _pts = 16 + n * 20;
            _chunks = new Chunk[n];
            for (int i = 0, o = 16; i < n; i++, o += 20)
            {
                _chunks[i] = new Chunk
                {
                    Index = i, Line = i == 0 || (b[o + 7] & 1) != 0 || b[o + 6] != _chunks[i - 1].Cls ? i : _chunks[i - 1].Line,
                    First = System.BitConverter.ToInt32(b, o), N = System.BitConverter.ToUInt16(b, o + 4), Cls = b[o + 6], Flags = b[o + 7],
                    Centre = EarthData.Dir(System.BitConverter.ToSingle(b, o + 8), System.BitConverter.ToSingle(b, o + 12)),
                    Rad = System.BitConverter.ToSingle(b, o + 16),
                };
            }
        }

        static Dictionary<long, List<int>> _index;

        static long IndexKey(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / 1000f) + 512, y = Mathf.FloorToInt(p.y / 1000f) + 512, z = Mathf.FloorToInt(p.z / 1000f) + 512;
            return (x << 40) | (y << 20) | z;
        }

        /// <summary>
        /// Does any road or railway come into a region? The region is given by a centre, a radius that contains it, and a test
        /// for one spot. Works from the whole file, wherever the player is, so airports and ports can be placed
        /// clear of every line before any ground is built.
        /// </summary>
        public static bool Crosses(Vector3 centre, float radius, System.Func<Vector3, bool> inside)
        {
            Load();
            if (_chunks == null || _chunks.Length == 0) return false;
            if (_index == null)
            {
                _index = new Dictionary<long, List<int>>();
                for (int i = 0; i < _chunks.Length; i++)
                {
                    long key = IndexKey(_chunks[i].Centre * Planet.Radius);
                    if (!_index.TryGetValue(key, out var l)) _index[key] = l = new List<int>();
                    l.Add(i);
                }
            }
            var p = centre * Planet.Radius;
            int span = Mathf.CeilToInt((radius + 400f) / 1000f);
            for (int dx = -span; dx <= span; dx++)
            for (int dy = -span; dy <= span; dy++)
            for (int dz = -span; dz <= span; dz++)
            {
                if (!_index.TryGetValue(IndexKey(p + new Vector3(dx * 1000f, dy * 1000f, dz * 1000f)), out var l)) continue;
                foreach (int i in l)
                {
                    var c = _chunks[i];
                    if (c.Cls > 3) continue;
                    if ((c.Centre - centre).magnitude * Planet.Radius - c.Rad > radius) continue;
                    var pts = Points(c);
                    for (int s = 0; s < c.N - 1; s++)
                    {
                        float len = (pts[s + 1] - pts[s]).magnitude * Planet.Radius;
                        int n = Mathf.Max(1, Mathf.CeilToInt(len / 6f));
                        for (int q = 0; q <= n; q++) if (inside(Vector3.Lerp(pts[s], pts[s + 1], q / (float)n).normalized)) return true;
                    }
                }
            }
            return false;
        }

        static long CellKey(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / Cell) + 1024, y = Mathf.FloorToInt(p.y / Cell) + 1024, z = Mathf.FloorToInt(p.z / Cell) + 1024;
            return (x << 40) | (y << 20) | z;
        }

        /// <summary>
        /// Tells the roads where the player is. Chunks within about 2 km are decoded and registered for
        /// <see cref="Near"/>; the rest are forgotten. Cheap to call often: it only works when the player has moved.
        /// </summary>
        public static void Focus(Vector3 dir)
        {
            Load();
            if (_hasFocus && (dir - _focus).magnitude * Planet.Radius < 200f) return;
            _hasFocus = true;
            _focus = dir;
            foreach (var c in _active) c.Active = false;
            var keep = new List<Chunk>();
            for (int i = 0; i < _chunks.Length; i++)
            {
                var c = _chunks[i];
                float d = (c.Centre - dir).magnitude * Planet.Radius - c.Rad;
                if (d > ActiveRange) continue;
                if (CityReplica.Covers(c.Centre) > 0.5f) continue; // a rebuilt city has its own streets
                c.Active = true;
                if (c.P == null)
                {
                    c.P = new Vector3[c.N];
                    for (int k = 0, o = _pts + c.First * 8; k < c.N; k++, o += 8)
                        c.P[k] = EarthData.Dir(System.BitConverter.ToSingle(_raw, o), System.BitConverter.ToSingle(_raw, o + 4));
                    if (c.Cls == 3)
                    {
                        c.Cum = new float[c.N];
                        for (int k = 1; k < c.N; k++) c.Cum[k] = c.Cum[k - 1] + (c.P[k] - c.P[k - 1]).magnitude * Planet.Radius;
                    }
                }
                keep.Add(c);
            }
            foreach (var c in _active) if (!c.Active) { Drop(c); c.P = null; c.Cum = null; c.Raw = null; c.Prof = null; }
            _active.Clear();
            _active.AddRange(keep);
            _grid.Clear();
            _stations.Clear();
            foreach (var c in _active) { Register(c); if (c.Cls == 4) _stations.Add(c); }
        }

        static void Register(Chunk c)
        {
            long last = -1;
            for (int s = 0; s < c.N - 1; s++)
            {
                float len = (c.P[s + 1] - c.P[s]).magnitude * Planet.Radius;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / Sample));
                for (int q = 0; q <= n; q++)
                {
                    var p = Vector3.Lerp(c.P[s], c.P[s + 1], q / (float)n).normalized * Planet.Radius;
                    for (int k = 0; k < 8; k++)
                    {
                        long key = CellKey(p + new Vector3((k & 1) == 0 ? -Margin : Margin, (k & 2) == 0 ? -Margin : Margin, (k & 4) == 0 ? -Margin : Margin));
                        if (key == last) continue;
                        last = key;
                        if (!_grid.TryGetValue(key, out var l)) _grid[key] = l = new List<Chunk>();
                        if (!l.Contains(c)) l.Add(c);
                    }
                }
            }
        }

        /// <summary>Metres from a spot to the centre line of a chunk.</summary>
        static float Distance(Chunk c, Vector3 dir)
        {
            float best = float.MaxValue;
            for (int s = 0; s < c.N - 1; s++)
            {
                var a = c.P[s]; var ab = c.P[s + 1] - a;
                float t = Mathf.Clamp01(Vector3.Dot(dir - a, ab) / Mathf.Max(1e-14f, ab.sqrMagnitude));
                float d = (dir - (a + ab * t).normalized).sqrMagnitude;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best) * Planet.Radius;
        }

        /// <summary>Is this spot within <paramref name="metres"/> (at most 40) of the edge of a road? Cheap enough per tree.</summary>
        public static bool Near(Vector3 dir, float metres)
        {
            if (!_hasFocus) return false;
            if (!_grid.TryGetValue(CellKey(dir * Planet.Radius), out var l)) return false;
            for (int i = 0; i < l.Count; i++)
            {
                var c = l[i];
                if (c.State == 2 || c.P == null) continue;
                if ((c.Centre - dir).magnitude * Planet.Radius > c.Rad + 45f) continue;
                if (Distance(c, dir) > c.Half + metres) continue;
                if (c.State == 0 && !Check(c)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Real roads are always kept; a link road is dropped if it would run mostly through water.</summary>
        static bool Check(Chunk c)
        {
            if (c.Cls != 2) { c.State = 1; return true; }
            var planet = Planet.Current;
            if (planet == null) return false;
            int wet = 0, n = 0;
            for (int s = 0; s < c.N; s++)
            {
                n++;
                if (planet.Height(c.P[s]) < Planet.WaterLevel(c.P[s]) + 0.3f) wet++;
            }
            c.State = wet > n * 0.35f ? 2 : 1;
            return c.State == 1;
        }

        // ------------------------------------------------------------------ meshes near the player

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            if (_want.Count > 0)
            {
                var c = _want[_want.Count - 1];
                _want.RemoveAt(_want.Count - 1);
                if (c.Go == null && c.Active && c.State != 2) { if (c.Cls == 4) BuildStation(c); else Build(c); }
            }
            RunTrains();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.4f;
            var pd = _planet.WorldToDir(_planet.Player.position);
            Focus(pd);
            _want.Clear();
            foreach (var c in _active)
            {
                if (c.State == 2) continue;
                float rough = (c.Centre - pd).magnitude * Planet.Radius - c.Rad;
                if (c.Go == null)
                {
                    if (rough < BuildIn && (c.State == 1 || Check(c))) _want.Add(c);
                    continue;
                }
                if (rough > DropOut) { Drop(c); continue; }
                if (c.Cls == 4) continue;                       // a station keeps the collider it was built with
                bool close = rough < ColliderIn && Distance(c, pd) < ColliderIn;
                if (close && c.Col == null) { c.Col = c.Go.AddComponent<MeshCollider>(); c.Col.sharedMesh = c.Mesh; }
                else if (!close && c.Col != null) { Destroy(c.Col); c.Col = null; }
            }
            // Nearest first (the list is popped from its end).
            _want.Sort((a, b) => ((b.Centre - pd).sqrMagnitude).CompareTo((a.Centre - pd).sqrMagnitude));
        }

        static void Drop(Chunk c)
        {
            if (c.Go != null) Destroy(c.Go);
            if (c.Mesh != null) Destroy(c.Mesh);
            c.Go = null; c.Mesh = null; c.Col = null;
        }

        // ------------------------------------------------------------------ the road's shape

        /// <summary>
        /// Cross-sections along a stretch of road: where they are, the ground under the centre and both edges, and
        /// the height of the road surface there.
        /// </summary>
        internal sealed class Section
        {
            public int M;                               // index of the last cross-section
            public int From, To;                        // the part that is built (a line that would end out in the water stops at the shore)
            public float[] Pre;                         // heights before other lines are taken into account
            public bool Final;                          // worked out with everything around it loaded
            public Vector3[] Dir, Fwd, Side;
            public float[] Along, Need, Gl, Gc, Gr, Yl, Yc, Yr;
            public bool[] Bank;                         // on a river's bank, where the ground may not be filled
            public bool[] Wet, Raised;                  // over water; carried above the ground (bridge or viaduct)
        }

        const float Reach = 300f;                       // how far along the line a rise ahead is prepared for
        /// <summary>
        /// The steepest a line may climb. Heights on this planet are stretched six times against distances, so real
        /// mountain roads are steep here; these limits only stop a line following a cliff or a bank straight up.
        /// </summary>
        internal static float Grade(byte cls) => cls == 3 ? 0.3f : cls == 0 ? 0.4f : 0.5f;

        static Vector3[] Points(Chunk c)
        {
            if (c.P != null) return c.P;
            var p = new Vector3[c.N];
            for (int k = 0, o = _pts + c.First * 8; k < c.N; k++, o += 8)
                p[k] = EarthData.Dir(System.BitConverter.ToSingle(_raw, o), System.BitConverter.ToSingle(_raw, o + 4));
            return p;
        }

        /// <summary>
        /// What the ground asks of the road at each cross-section of a chunk: on the ground under its centre line,
        /// and well clear of any water. Heights are of the ground before any road has shaped it.
        /// </summary>
        static Section Raw(Chunk c, Planet planet)
        {
            if (c.Raw != null) return c.Raw;
            var pts = Points(c);
            var dirs = new List<Vector3>();
            var fwd = new List<Vector3>();
            var along = new List<float>();
            float run = 0f;
            for (int s = 0; s < c.N - 1; s++)
            {
                Vector3 a = pts[s], b = pts[s + 1];
                float len = (b - a).magnitude * Planet.Radius;
                if (len < 0.3f && s < c.N - 2) continue;        // too short to have a direction
                int n = Mathf.Max(1, Mathf.CeilToInt(len / Step));
                var f = (b - a) * (1f / Mathf.Max(1e-9f, (b - a).magnitude));
                if (len < 0.3f && fwd.Count > 0) f = fwd[fwd.Count - 1];
                for (int q = dirs.Count == 0 ? 0 : 1; q <= n; q++) { dirs.Add(Vector3.Lerp(a, b, q / (float)n).normalized); fwd.Add(f); along.Add(run + len * q / n); }
                run += len;
            }
            int m = dirs.Count - 1;
            var r = new Section { M = m };
            if (m < 1) return r;
            float half = c.Half, lift = c.Cls == 3 ? RailLift : Lift;
            r.Dir = dirs.ToArray(); r.Fwd = fwd.ToArray(); r.Along = along.ToArray();
            r.Side = new Vector3[m + 1];
            r.Need = new float[m + 1]; r.Gl = new float[m + 1]; r.Gc = new float[m + 1]; r.Gr = new float[m + 1];
            r.Wet = new bool[m + 1]; r.Bank = new bool[m + 1];
            for (int s = 0; s <= m; s++)
            {
                // The tangent at a corner is the average of the two segments, so the edges stay parallel round bends.
                var t = s > 0 && s < m ? (fwd[s] + fwd[s - 1]).normalized : fwd[s];
                // At the joint with the next piece of the line both pieces use the same tangent, so they meet edge to edge.
                if (s == 0 && Joined(c, -1)) { var q = Points(_chunks[c.Index - 1]); int e = q.Length - 1; var v = q[e] - q[e - 1]; if (v.magnitude * Planet.Radius > 0.3f) t = (fwd[0] + v * (1f / v.magnitude)).normalized; }
                if (s == m && Joined(c, 1)) { var q = Points(_chunks[c.Index + 1]); var v = q[1] - q[0]; if (v.magnitude * Planet.Radius > 0.3f) t = (fwd[m] + v * (1f / v.magnitude)).normalized; }
                var d = dirs[s];
                r.Side[s] = Vector3.Cross(d, t).normalized;
                float wl = Planet.WaterLevel(d);
                r.Bank[s] = Rivers.Level(d, out float river);
                if (r.Bank[s] && river > wl) wl = river;   // a river is crossed on a bridge too
                r.Gc[s] = Planet.HeightBeforeRoads(d);
                r.Gl[s] = Planet.HeightBeforeRoads((d * Planet.Radius - r.Side[s] * half).normalized);
                r.Gr[s] = Planet.HeightBeforeRoads((d * Planet.Radius + r.Side[s] * half).normalized);
                r.Wet[s] = r.Gc[s] < wl + 0.5f;
                // The road follows the ground under its centre line; the ground beside it is cut or filled to meet it.
                r.Need[s] = r.Wet[s] ? Mathf.Max(r.Gc[s] + lift, wl + 1.5f) : r.Gc[s] + lift;
            }
            if (c.Active) c.Raw = r;
            return r;
        }

        const float Viaduct = 5f, Bench = 2f, Shoulder = 14f;

        /// <summary>Is everything that could matter to this chunk's shape loaded (the lines it crosses, the line either side of it)?</summary>
        static bool Settled(Chunk c) => c.Active && _hasFocus && (c.Centre - _focus).magnitude * Planet.Radius + c.Rad < 1250f;

        /// <summary>
        /// Earthworks. The ground beside a road or a railway is shaped to it, as on any real line: where the ground
        /// stands higher than the road it is cut away (a road along a hillside sits on a ledge, not in the air on
        /// its low side or buried on its high side), and where it lies a little lower it is banked up under the
        /// road. Water is left alone (that is what bridges are for), and so are cities, airfields and quays.
        /// </summary>
        static readonly (float key, float floor, float roof)[] _ew = new (float, float, float)[12];

        public static void Ground(Vector3 dir, ref float h)
        {
            if (!_hasFocus || !_grid.TryGetValue(CellKey(dir * Planet.Radius), out var l)) return;
            var planet = Planet.Current;
            if (planet == null) return;
            bool looked = false, wet = false;
            int n = 0;                                                 // every line here has its say; the nearest has the last word
            for (int i = 0; i < l.Count; i++)
            {
                var c = l[i];
                if (c.Cls > 3 || c.P == null || c.State == 2 || !Settled(c)) continue;
                float reach = c.Half + Shoulder;
                if ((c.Centre - dir).magnitude * Planet.Radius > c.Rad + reach) continue;
                if (Distance(c, dir) > reach) continue;
                if (!looked)
                {
                    looked = true;
                    if (CityReplica.Covers(dir) > 0.5f || Hubs.Forbidden(dir)) return;
                    wet = h < Planet.WaterLevel(dir) + 0.3f || Rivers.Level(dir, out _);    // never fill water in; banks above a bridge are still cut back
                }
                var p = Profile(c, planet);
                if (p.M < 1 || p.To - p.From < 1) continue;
                // The nearest spot on the built part of the line, and the road's height there.
                float best = float.MaxValue, deck = 0f; bool bridge = false;
                for (int s = p.From; s < p.To; s++)
                {
                    var a = p.Dir[s]; var ab = p.Dir[s + 1] - a;
                    float t = Mathf.Clamp01(Vector3.Dot(dir - a, ab) / Mathf.Max(1e-14f, ab.sqrMagnitude));
                    float q = (dir - (a + ab * t)).sqrMagnitude;
                    if (q >= best) continue;
                    best = q; deck = Mathf.Lerp(p.Yc[s], p.Yc[s + 1], t); bridge = p.Raised[t < 0.5f ? s : s + 1];
                }
                float d = Mathf.Sqrt(best) * Planet.Radius;
                if (d > reach) continue;
                float bed = deck - (c.Cls == 3 ? RailLift : Lift), outside = Mathf.Max(0f, d - c.Half - Bench);
                if (n == _ew.Length) continue;
                // sorted farthest first
                int at = n++; float key = d - c.Half;
                while (at > 0 && _ew[at - 1].key < key) { _ew[at] = _ew[at - 1]; at--; }
                _ew[at] = (key, bridge || wet ? float.MinValue : bed - 0.2f - outside * 0.8f, bed - 0.2f + outside * 1.3f);   // 0.2 spare, so a bend on a slope never shows through
            }
            for (int i = 0; i < n; i++)
            {
                if (h < _ew[i].floor) h = _ew[i].floor;      // an embankment
                if (h > _ew[i].roof) h = _ew[i].roof;        // a cutting
            }
            // Where two surfaces overlap (a junction on a slope) the ground stays under the lower one.
            for (int i = 0; i < n; i++) if (_ew[i].key <= 0.05f && h > _ew[i].roof) h = _ew[i].roof;
        }

        /// <summary>Does the line carry on into the next (sense 1) or previous (sense -1) chunk?</summary>
        static bool Joined(Chunk c, int sense)
        {
            if ((c.Flags & (sense > 0 ? 2 : 1)) != 0) return false;
            int j = c.Index + sense;
            return j >= 0 && j < _chunks.Length && _chunks[j].Cls == c.Cls && _chunks[j].N >= 2;
        }

        /// <summary>Does the line stop at this end of the chunk: its real end, or the edge of a rebuilt city (which has its own streets)?</summary>
        static bool Terminal(Chunk c, int sense) => !Joined(c, sense) || CityReplica.Covers(_chunks[c.Index + sense].Centre) > 0.5f;

        /// <summary>
        /// The cross-sections of a chunk and of the line up to Reach before and after it, in order along the line.
        /// Neighbouring chunks see each other's cross-sections exactly, so they agree where they meet.
        /// </summary>
        sealed class Stretch
        {
            public readonly List<float> Need = new List<float>(), At = new List<float>();
            public readonly List<bool> Wet = new List<bool>();
            public float[] Bare;        // the need at each cross-section before bumps are filled
            public readonly List<Vector3> Dir = new List<Vector3>(), Fwd = new List<Vector3>();
            public int First;
            public bool EndA, EndB;     // the stretch starts / stops at the line's own end
        }

        static Stretch Window(Chunk c, Planet planet, Section own)
        {
            var w = new Stretch();
            int m = own.M;
            var before = new List<(float need, float at, Vector3 dir, Vector3 fwd, bool wet)>();
            {
                float pos = 0f; var k = c;
                while (-pos < Reach && !Terminal(k, -1))
                {
                    k = _chunks[k.Index - 1];
                    var r = Raw(k, planet);
                    if (r.M < 1) break;
                    for (int s = r.M - 1; s >= 0 && -pos < Reach; s--) { pos -= r.Along[s + 1] - r.Along[s]; before.Add((r.Need[s], pos, r.Dir[s], r.Fwd[s], r.Wet[s])); }
                }
                w.EndA = -pos < Reach && Terminal(k, -1);
            }
            for (int i = before.Count - 1; i >= 0; i--) { w.Need.Add(before[i].need); w.Wet.Add(before[i].wet); w.At.Add(before[i].at); w.Dir.Add(before[i].dir); w.Fwd.Add(before[i].fwd); }
            w.First = w.Need.Count;
            for (int s = 0; s <= m; s++) { w.Need.Add(own.Need[s]); w.Wet.Add(own.Wet[s]); w.At.Add(own.Along[s]); w.Dir.Add(own.Dir[s]); w.Fwd.Add(own.Fwd[s]); }
            {
                float pos = own.Along[m]; var k = c;
                while (pos - own.Along[m] < Reach && !Terminal(k, 1))
                {
                    k = _chunks[k.Index + 1];
                    var r = Raw(k, planet);
                    if (r.M < 1) break;
                    for (int s = 1; s <= r.M && pos - own.Along[m] < Reach; s++) { pos += r.Along[s] - r.Along[s - 1]; w.Need.Add(r.Need[s]); w.Wet.Add(r.Wet[s]); w.At.Add(pos); w.Dir.Add(r.Dir[s]); w.Fwd.Add(r.Fwd[s]); }
                }
                w.EndB = pos - own.Along[m] < Reach && Terminal(k, 1);
            }
            // Small bumps are filled, never cut.
            int n = w.Need.Count;
            var y0 = new float[n];
            w.Bare = w.Need.ToArray();
            for (int i = 0; i < n; i++) y0[i] = i == 0 || i == n - 1 ? w.Need[i] : Mathf.Max(w.Need[i], (w.Need[i - 1] + w.Need[i] * 2f + w.Need[i + 1]) * 0.25f);
            for (int i = 0; i < n; i++) w.Need[i] = y0[i];
            return w;
        }

        /// <summary>
        /// Each cross-section of the chunk looks Reach along the line both ways, and no further (so two chunks
        /// always agree about the cross-section they share), and stands high enough that the line reaches every
        /// rise in that stretch at no more than its grade.
        /// </summary>
        static float[] Graded(Chunk c, Stretch w, int m)
        {
            float grade = Grade(c.Cls);
            int n = w.Need.Count;
            var y = new float[m + 1];
            for (int s = 0; s <= m; s++)
            {
                int i = w.First + s; float v = w.Need[i];
                for (int q = i - 1; q >= 0 && w.At[i] - w.At[q] <= Reach; q--) v = Mathf.Max(v, w.Need[q] - grade * (w.At[i] - w.At[q]));
                for (int q = i + 1; q < n && w.At[q] - w.At[i] <= Reach; q++) v = Mathf.Max(v, w.Need[q] - grade * (w.At[q] - w.At[i]));
                y[s] = v;
            }
            return y;
        }

        /// <summary>The chunk's heights from its own line alone, before the lines it crosses are taken into account.</summary>
        static Section Pre(Chunk c, Planet planet)
        {
            var own = Raw(c, planet);
            if (own.M < 1 || own.Pre != null) return own;
            var pre = Graded(c, Window(c, planet, own), own.M);
            if (c.Active) own.Pre = pre;
            else return new Section { M = own.M, Dir = own.Dir, Fwd = own.Fwd, Pre = pre };
            return own;
        }

        /// <summary>
        /// Another line crossing at this spot, and the height it has there. Two lines that cross meet at one height
        /// (a level crossing, a junction) unless one passes more than 5 m above the other, which is a flyover.
        /// </summary>
        static bool CrossingAt(Chunk c, Vector3 d, Vector3 fwd, Planet planet, out float height)
        {
            height = 0f;
            if (!_hasFocus || !_grid.TryGetValue(CellKey(d * Planet.Radius), out var l)) return false;
            bool any = false;
            for (int i = 0; i < l.Count; i++)
            {
                var o = l[i];
                if (o.Line == c.Line || o.Cls > 3 || o.P == null || o.State == 2) continue;
                if ((o.Centre - d).magnitude * Planet.Radius > o.Rad + 4f) continue;
                if (Distance(o, d) > 2.6f) continue;
                var p = Raw(o, planet);
                if (p.M < 1) continue;
                int at = 0; float near = float.MaxValue;
                for (int s = 0; s <= p.M; s++) { float q = (p.Dir[s] - d).sqrMagnitude; if (q < near) { near = q; at = s; } }
                if (Mathf.Abs(Vector3.Dot(p.Fwd[at], fwd)) > 0.85f) continue;     // alongside, not across
                if (!any || p.Need[at] > height) height = p.Need[at];
                any = true;
            }
            return any;
        }

        /// <summary>The height a line needs at a spot where lines meet: the same for every line, whatever its kind.</summary>
        static float NeedAt(Vector3 x)
        {
            float g = Planet.HeightBeforeRoads(x), wl = Planet.WaterLevel(x);
            if (Rivers.Level(x, out float river) && river > wl) wl = river;
            return g < wl + 0.5f ? Mathf.Max(g + RailLift, wl + 1.5f) : g + RailLift;
        }

        static Vector3 Closest(Vector3 a, Vector3 b, Vector3 p, out float t)
        {
            var ab = b - a;
            t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-14f, ab.sqrMagnitude));
            return a + ab * t;
        }

        /// <summary>
        /// Does another line cross or join this one right at cross-section i of the stretch? Gives the height of
        /// the meeting point, taken from the ground at the exact spot where the two centre lines meet, so both
        /// lines work out the same number.
        /// </summary>
        static bool Meet(Chunk c, Stretch w, int i, Planet planet, out float height)
        {
            height = 0f;
            var d = w.Dir[i];
            if (!_hasFocus || !_grid.TryGetValue(CellKey(d * Planet.Radius), out var l)) return false;
            int n = w.Dir.Count, lo = Mathf.Max(0, i - 1), hi = Mathf.Min(n - 1, i + 1);
            if (hi <= lo) return false;
            bool any = false;
            float touch = 4f / Planet.Radius; touch *= touch;
            Planet.TangentAt(d, out var east, out var north);
            for (int k = 0; k < l.Count; k++)
            {
                var o = l[k];
                if (o.Line == c.Line || o.Cls > 3 || o.P == null || o.State == 2 || o.N < 2) continue;
                if ((o.Centre - d).magnitude * Planet.Radius > o.Rad + 8f) continue;
                // the nearest piece of the other line, and its direction there
                float best = float.MaxValue; var dirO = Vector3.zero;
                for (int s = 0; s < o.N - 1; s++)
                {
                    float q = (d - Closest(o.P[s], o.P[s + 1], d, out _)).sqrMagnitude;
                    if (q < best) { best = q; dirO = o.P[s + 1] - o.P[s]; }
                }
                if (Mathf.Sqrt(best) * Planet.Radius > 8f) continue;
                float len = dirO.magnitude;
                if (len < 1e-9f) continue;
                bool across = Mathf.Abs(Vector3.Dot(dirO * (1f / len), w.Fwd[i])) <= 0.85f;     // not running alongside
                bool found = false; var x = Vector3.zero;
                // 1. the centre lines cross
                for (int j = lo; j < hi && !found && across; j++)
                {
                    float ax = Vector3.Dot(w.Dir[j] - d, east), ay = Vector3.Dot(w.Dir[j] - d, north);
                    float bx = Vector3.Dot(w.Dir[j + 1] - d, east) - ax, by = Vector3.Dot(w.Dir[j + 1] - d, north) - ay;
                    for (int s = 0; s < o.N - 1 && !found; s++)
                    {
                        float cx = Vector3.Dot(o.P[s] - d, east), cy = Vector3.Dot(o.P[s] - d, north);
                        float dx = Vector3.Dot(o.P[s + 1] - d, east) - cx, dy = Vector3.Dot(o.P[s + 1] - d, north) - cy;
                        float den = bx * dy - by * dx;
                        if (Mathf.Abs(den) < 1e-18f) continue;
                        float t = ((cx - ax) * dy - (cy - ay) * dx) / den, u = ((cx - ax) * by - (cy - ay) * bx) / den;
                        if (t < 0f || t > 1f || u < 0f || u > 1f) continue;
                        x = (o.P[s] + (o.P[s + 1] - o.P[s]) * u).normalized; found = true;
                    }
                }
                // 2. the other line ends beside this one
                for (int sense = -1; sense <= 1 && !found; sense += 2)
                {
                    var e = sense < 0 ? o.P[0] : o.P[o.N - 1];
                    if ((e - d).sqrMagnitude * Planet.Radius * Planet.Radius > 64f || !Terminal(o, sense)) continue;
                    for (int j = lo; j < hi && !found; j++)
                    {
                        var q = Closest(w.Dir[j], w.Dir[j + 1], e, out float t);
                        if ((q - e).sqrMagnitude > touch) continue;
                        if (j == lo && t <= 0f && !(lo == 0 && w.EndA) && lo != i) continue;       // nearer to an earlier piece
                        if (j == hi - 1 && t >= 1f && !(hi == n - 1 && w.EndB) && hi != i) continue;
                        x = q.normalized; found = true;
                    }
                }
                // 3. this line ends beside the other
                if (!found && ((i == 0 && w.EndA) || (i == n - 1 && w.EndB)) && best <= touch)
                {
                    float bq = float.MaxValue;
                    for (int s = 0; s < o.N - 1; s++)
                    {
                        var q = Closest(o.P[s], o.P[s + 1], d, out _);
                        float qq = (q - d).sqrMagnitude;
                        if (qq < bq) { bq = qq; x = q.normalized; }
                    }
                    found = true;
                }
                if (!found) continue;
                float h = NeedAt(x);
                if (!any || h > height) height = h;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// How many cross-sections at one end of a chunk lie in a stretch of water that the line never comes out
        /// of: it reaches its end, or the edge of a rebuilt city, still over the water. That stretch is not built,
        /// so a line stops at the shore instead of on a bridge to nowhere.
        /// </summary>
        static int DeadEnd(Chunk c, int sense, Planet planet, Section own)
        {
            int m = own.M, wet = 0;
            for (int s = sense > 0 ? m : 0; s >= 0 && s <= m && own.Wet[s]; s -= sense) wet++;
            if (wet == 0) return 0;
            float gone = 0f; var k = c;
            while (gone < 600f)
            {
                if (Terminal(k, sense)) return wet;
                k = _chunks[k.Index + sense];
                var r = Raw(k, planet);
                if (r.M < 1) return wet;
                for (int s = sense > 0 ? 0 : r.M; s >= 0 && s <= r.M; s += sense) if (!r.Wet[s]) return 0;
                gone += r.Along[r.M];
            }
            return 0;                                   // a very long bridge: it stays
        }

        /// <summary>
        /// The road's final shape over a chunk. A road never follows a cliff or a river bank straight up or down:
        /// it looks along its own line, into the chunks before and after, and climbs at no more than its grade,
        /// on an embankment or a viaduct where the ground falls away. Never below the ground, never in the water,
        /// level with the lines it crosses, and it comes down to the ground where it ends.
        /// </summary>
        internal static Section Profile(Chunk c, Planet planet)
        {
            if (c.Prof != null && (c.Prof.Final || !Settled(c))) return c.Prof;
            var own = Raw(c, planet);
            int m = own.M;
            if (m < 1) return own;
            var w = Window(c, planet, own);
            // Fixed points: where another line crosses or joins, both stand on the ground there, at one height;
            // and where the line itself ends it stands on the ground.
            int n = w.Need.Count;
            var pin = new float[n];
            for (int i = 0; i < n; i++)
            {
                pin[i] = float.NaN;
                if (Meet(c, w, i, planet, out float other)) pin[i] = other;
            }
            for (int e = 0; e < 2; e++)
            {
                if (!(e == 0 ? w.EndA : w.EndB)) continue;
                int i = e == 0 ? 0 : n - 1;
                if (float.IsNaN(pin[i])) pin[i] = w.Bare[i];
            }
            for (int i = 0; i < n; i++) if (!float.IsNaN(pin[i]) && pin[i] > w.Need[i]) w.Need[i] = pin[i];
            var y = Graded(c, w, m);
            // Between fixed points the line may not stand higher than its grade lets it climb from them, so it
            // runs in a cutting through a rise instead of leaping off the ground at a junction.
            float grade = Grade(c.Cls);
            for (int s = 0; s <= m; s++)
            {
                int i = w.First + s, a = -1, b = -1;
                for (int q = i; q >= 0 && w.At[i] - w.At[q] <= Reach; q--) if (!float.IsNaN(pin[q])) { a = q; break; }
                for (int q = i; q < n && w.At[q] - w.At[i] <= Reach; q++) if (!float.IsNaN(pin[q])) { b = q; break; }
                if (a < 0 && b < 0) continue;
                float graded = y[s], cap = float.MaxValue;
                if (a >= 0) cap = Mathf.Min(cap, pin[a] + grade * (w.At[i] - w.At[a]));
                if (b >= 0) cap = Mathf.Min(cap, pin[b] + grade * (w.At[b] - w.At[i]));
                if (a >= 0 && b >= 0 && b > a) cap = Mathf.Max(cap, Mathf.Lerp(pin[a], pin[b], (w.At[i] - w.At[a]) / Mathf.Max(1e-3f, w.At[b] - w.At[a])));
                if (y[s] > cap) y[s] = cap;
                if (y[s] >= graded) continue;
                // never down into the water: it still clears every stretch of water within reach, at its grade
                for (int q = i; q >= 0 && w.At[i] - w.At[q] <= Reach; q--) if (w.Wet[q]) y[s] = Mathf.Max(y[s], w.Bare[q] - grade * (w.At[i] - w.At[q]));
                for (int q = i + 1; q < n && w.At[q] - w.At[i] <= Reach; q++) if (w.Wet[q]) y[s] = Mathf.Max(y[s], w.Bare[q] - grade * (w.At[q] - w.At[i]));
                if (y[s] > graded) y[s] = graded;
            }

            var p = new Section { M = m, Dir = own.Dir, Fwd = own.Fwd, Side = own.Side, Along = own.Along, Need = own.Need, Gl = own.Gl, Gc = own.Gc, Gr = own.Gr, Wet = own.Wet, Bank = own.Bank };
            p.From = DeadEnd(c, -1, planet, own); p.To = m - DeadEnd(c, 1, planet, own);
            // A line cut short at a shore comes down to the ground there.
            const float Ramp = 1f;
            bool endA = p.From > 0, endB = p.To < m;
            if (p.From <= p.To)
            {
                if (endA) { int a = p.From; y[a] = own.Need[a]; for (int s = a + 1; s <= m; s++) { float cap = Mathf.Max(own.Need[s], y[s - 1] + Ramp * (own.Along[s] - own.Along[s - 1])); if (y[s] <= cap) break; y[s] = cap; } }
                if (endB) { int b = p.To; y[b] = own.Need[b]; for (int s = b - 1; s >= 0; s--) { float cap = Mathf.Max(own.Need[s], y[s + 1] + Ramp * (own.Along[s + 1] - own.Along[s])); if (y[s] <= cap) break; y[s] = cap; } }
            }
            p.Yl = new float[m + 1]; p.Yc = new float[m + 1]; p.Yr = new float[m + 1]; p.Raised = new bool[m + 1];
            float lift = c.Cls == 3 ? RailLift : Lift;
            for (int s = 0; s <= m; s++)
            {
                float yc = y[s];
                p.Yc[s] = yc;
                // Over water, or more than Viaduct above the ground, it is carried on piers; lower than that the
                // ground is banked up under it; where the ground is higher it is cut away (see Ground).
                p.Raised[s] = own.Wet[s] || yc - lift - own.Gc[s] > (own.Bank[s] ? 0.6f : Viaduct);
                // It could not come down to a line it crosses here (a bridge approach, say): it passes over on piers.
                for (int q = Mathf.Max(0, w.First + s - 1); q <= Mathf.Min(n - 1, w.First + s + 1); q++)
                    if (!float.IsNaN(pin[q]) && yc > pin[q] + 0.8f) p.Raised[s] = true;
                p.Yl[s] = yc; p.Yr[s] = yc;
            }
            p.Final = Settled(c);
            if (c.Active) c.Prof = p;
            return p;
        }

        /// <summary>The height of the rail bed a given distance along a railway chunk.</summary>
        static float BedAlong(Chunk c, float d, Planet planet)
        {
            var p = Profile(c, planet);
            if (p.M < 1) return 0f;
            int s = 0;
            while (s < p.M - 1 && p.Along[s + 1] < d) s++;
            return Mathf.Lerp(p.Yc[s], p.Yc[s + 1], Mathf.Clamp01((d - p.Along[s]) / Mathf.Max(1e-3f, p.Along[s + 1] - p.Along[s])));
        }

        void Build(Chunk c)
        {
            var sec = Profile(c, _planet);
            if (sec.M < 1 || sec.To - sec.From < 1) { c.State = 2; return; }
            // A little past each end where the line carries on, so chunks overlap, not gap.
            bool pre = sec.From == 0 && !Terminal(c, -1), post = sec.To == sec.M && !Terminal(c, 1);
            int m = sec.To - sec.From + (pre ? 1 : 0) + (post ? 1 : 0);
            int Src(int s) => Mathf.Clamp(s - (pre ? 1 : 0) + sec.From, sec.From, sec.To);
            var dirs = new Vector3[m + 1]; var side = new Vector3[m + 1];
            float[] yl = new float[m + 1], yc = new float[m + 1], yr = new float[m + 1], gl = new float[m + 1], gr = new float[m + 1];
            var wet = new bool[m + 1];
            const float ext = 0.3f / Planet.Radius;
            for (int s = 0; s <= m; s++)
            {
                int q = Src(s);
                dirs[s] = sec.Dir[q]; side[s] = sec.Side[q];
                yl[s] = sec.Yl[q]; yc[s] = sec.Yc[q]; yr[s] = sec.Yr[q]; gl[s] = sec.Gl[q]; gr[s] = sec.Gr[q];
                wet[s] = sec.Raised[q];
            }
            if (pre) dirs[0] = (dirs[0] - sec.Fwd[0] * ext).normalized;
            if (post) dirs[m] = (dirs[m] + sec.Fwd[sec.M] * ext).normalized;
            bool endA = sec.From > 0 || Terminal(c, -1), endB = sec.To < sec.M || Terminal(c, 1);
            float half = c.Half;
            bool rail = c.Cls == 3;
            var origin = c.Centre * (Planet.Radius + _planet.Height(c.Centre));

            var mb = new MeshBuilder();
            var I = Matrix4x4.identity;
            Vector3 P(int s, float off, float y) => (dirs[s] * Planet.Radius + side[s] * off).normalized * (Planet.Radius + y) - origin;
            // u runs 0 (left edge) .. 1 (right edge); the height bends through the centre line.
            Vector3 Top(int s, float u) => P(s, Mathf.Lerp(-half, half, u), u < 0.5f ? Mathf.Lerp(yl[s], yc[s], u * 2f) : Mathf.Lerp(yc[s], yr[s], u * 2f - 1f));
            for (int s = 0; s < m; s++)
            {
                bool bridge = wet[s] || wet[s + 1];
                var edge = bridge ? Concrete : rail ? Ballast : Verge;
                const float v = 0.07f;
                if (rail)
                {
                    // A bed of ballast, sleepers across it and two steel rails.
                    mb.Quad(I, Top(s, 0f), Top(s + 1, 0f), Top(s + 1, 0.5f), Top(s, 0.5f), edge);
                    mb.Quad(I, Top(s, 0.5f), Top(s + 1, 0.5f), Top(s + 1, 1f), Top(s, 1f), edge);
                    Vector3 On(float off, float t, float up)
                    {
                        float u = 0.5f + off / (half * 2f);
                        return Vector3.Lerp(Top(s, u) + dirs[s] * up, Top(s + 1, u) + dirs[s + 1] * up, t);
                    }
                    for (int q = 0; q < 4; q++)
                    {
                        float t0 = (q + 0.5f) / 4f - 0.035f, t1 = t0 + 0.07f;
                        mb.Quad(I, On(-1.25f, t0, 0.05f), On(-1.25f, t1, 0.05f), On(1.25f, t1, 0.05f), On(1.25f, t0, 0.05f), Sleeper);
                    }
                    for (int k = -1; k <= 1; k += 2)
                    {
                        float a = k * Gauge - 0.07f, b = k * Gauge + 0.07f;
                        mb.Quad(I, On(a, 0f, RailTop), On(a, 1f, RailTop), On(b, 1f, RailTop), On(b, 0f, RailTop), Steel);
                        mb.Quad(I, On(a, 0f, RailTop), On(a - 0.03f, 0f, 0f), On(a - 0.03f, 1f, 0f), On(a, 1f, RailTop), Steel);
                        mb.Quad(I, On(b, 0f, RailTop), On(b, 1f, RailTop), On(b + 0.03f, 1f, 0f), On(b + 0.03f, 0f, 0f), Steel);
                    }
                }
                else
                {
                    mb.Quad(I, Top(s, 0f), Top(s + 1, 0f), Top(s + 1, v), Top(s, v), edge);
                    mb.Quad(I, Top(s, v), Top(s + 1, v), Top(s + 1, 0.5f), Top(s, 0.5f), Asphalt);
                    mb.Quad(I, Top(s, 0.5f), Top(s + 1, 0.5f), Top(s + 1, 1f - v), Top(s, 1f - v), Asphalt);
                    mb.Quad(I, Top(s, 1f - v), Top(s + 1, 1f - v), Top(s + 1, 1f), Top(s, 1f), edge);
                }
                // Shoulders: a gentle bank down into the ground on land, a slab edge on a bridge.
                float outw = bridge ? 0.3f : 1.3f, deep = bridge ? 0.6f : 1.2f;
                Vector3 l0 = Top(s, 0f), l1 = Top(s + 1, 0f), r0 = Top(s, 1f), r1 = Top(s + 1, 1f);
                Vector3 l0b = P(s, -half - outw, yl[s] - deep), l1b = P(s + 1, -half - outw, yl[s + 1] - deep);
                Vector3 r0b = P(s, half + outw, yr[s] - deep), r1b = P(s + 1, half + outw, yr[s + 1] - deep);
                mb.Quad(I, l0, l0b, l1b, l1, edge);
                mb.Quad(I, r0, r1, r1b, r0b, edge);
                if (bridge) mb.Quad(I, l0b, r0b, r1b, l1b, edge);
                // Markings: a solid yellow centre line on highways, white dashes on roads, none on link roads.
                if (c.Cls == 0 || (c.Cls == 1 && s % 2 == 0))
                {
                    float a = 0.5f - 0.11f / (half * 2f), b = 0.5f + 0.11f / (half * 2f);
                    float t0 = c.Cls == 0 ? 0f : 0.2f, t1 = c.Cls == 0 ? 1f : 0.8f;
                    Vector3 Up(int q, float u) => Top(q, u) + dirs[q] * 0.035f;
                    mb.Quad(I, Vector3.Lerp(Up(s, a), Up(s + 1, a), t0), Vector3.Lerp(Up(s, a), Up(s + 1, a), t1),
                            Vector3.Lerp(Up(s, b), Up(s + 1, b), t1), Vector3.Lerp(Up(s, b), Up(s + 1, b), t0), c.Cls == 0 ? Yellow : Line);
                }
                if (bridge)
                {
                    if (s % 4 == 0 && wet[s])
                    {
                        float bottom = Mathf.Min(gl[s], gr[s]) - 1f, hgt = yc[s] - 0.5f - bottom;
                        var rot = Quaternion.LookRotation(Vector3.Cross(side[s], dirs[s]), dirs[s]);
                        mb.Box(Matrix4x4.TRS(P(s, 0f, bottom + hgt * 0.5f), rot, new Vector3(half * 1.4f, hgt, 1.1f)), Concrete, Concrete);
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        float off = k == 0 ? -half + 0.15f : half - 0.15f;
                        var p0 = P(s, off, (k == 0 ? yl[s] : yr[s]) + 0.35f); var p1 = P(s + 1, off, (k == 0 ? yl[s + 1] : yr[s + 1]) + 0.35f);
                        if ((p1 - p0).sqrMagnitude < 1e-4f) continue;
                        mb.Box(Matrix4x4.TRS((p0 + p1) * 0.5f, Quaternion.LookRotation(p1 - p0, dirs[s]), new Vector3(0.3f, 0.7f, (p1 - p0).magnitude)), Concrete, Concrete);
                    }
                }
            }
            // A round junction where the road really ends, so roads that meet there join up.
            for (int e = 0; e < 2; e++)
            {
                if (rail || !(e == 0 ? endA : endB)) continue;
                int s = e == 0 ? 0 : m;
                float r = half + 0.25f, y = yc[s] + 0.015f;
                Vector3 f = Vector3.Cross(side[s], dirs[s]).normalized;
                Vector3 Rim(int k)
                {
                    float a = k * Mathf.PI * 2f / 14f;
                    return (dirs[s] * Planet.Radius + side[s] * (Mathf.Cos(a) * r) + f * (Mathf.Sin(a) * r)).normalized * (Planet.Radius + y) - origin;
                }
                var ctr = P(s, 0f, y);
                for (int k = 0; k < 14; k++)
                {
                    mb.Tri(I, ctr, Rim(k + 1), Rim(k), Asphalt);
                    mb.Quad(I, Rim(k), Rim(k + 1), Rim(k + 1) * 1f - dirs[s] * 1.2f + (Rim(k + 1) - ctr) * 0.3f, Rim(k) - dirs[s] * 1.2f + (Rim(k) - ctr) * 0.3f, Verge);
                }
            }
            var mesh = mb.ToMesh(rail ? "Rail" : "Road");
            if (mesh == null) { c.State = 2; return; }
            var go = new GameObject(rail ? "Rail" : "Road");
            go.transform.SetParent(_planet.transform, false);
            go.transform.localPosition = origin;
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rd = go.AddComponent<MeshRenderer>();
            rd.sharedMaterial = _mat;
            rd.shadowCastingMode = ShadowCastingMode.Off;
            c.Go = go; c.Mesh = mesh;
        }

        // ------------------------------------------------------------------ stations

        static readonly Color Platform = new Color(0.74f, 0.73f, 0.7f), Wall = new Color(0.93f, 0.87f, 0.74f), Tile = new Color(0.66f, 0.3f, 0.22f);
        static readonly Color Canopy = new Color(0.25f, 0.42f, 0.4f), Post = new Color(0.3f, 0.31f, 0.34f);

        /// <summary>Height of the rail bed beside a spot: the track's own height there, so platforms meet the rails.</summary>
        float BedAt(Vector3 d)
        {
            Chunk best = null; float bd = 30f;
            if (_grid.TryGetValue(CellKey(d * Planet.Radius), out var l))
                foreach (var c in l)
                {
                    if (c.Cls != 3 || c.P == null) continue;
                    float dist = Distance(c, d);
                    if (dist < bd) { bd = dist; best = c; }
                }
            if (best != null)
            {
                var p = Profile(best, _planet);
                int at = -1; float near = float.MaxValue;
                for (int s = 0; s <= p.M; s++) { float q = (p.Dir[s] - d).sqrMagnitude; if (q < near) { near = q; at = s; } }
                if (at >= 0) return p.Yc[at];
            }
            float wl = Planet.WaterLevel(d);
            if (Rivers.Level(d, out float river) && river > wl) wl = river;
            return Mathf.Max(_planet.Height(d) + RailLift, wl + 1.5f);
        }

        /// <summary>A platform with a canopy and a small station house beside the track, on the town's side of it.</summary>
        void BuildStation(Chunk c)
        {
            if (c.N < 3) { c.State = 2; return; }
            var d = c.P[0];
            var f = (c.P[1] - c.P[0]).normalized;
            var sd = Vector3.Cross(d, f).normalized;
            f = Vector3.Cross(sd, d).normalized;
            float k = Vector3.Dot(c.P[2] - c.P[0], sd) < 0f ? -1f : 1f;
            if (_planet.Height(d) < Planet.WaterLevel(d) + 0.3f) { c.State = 2; return; }
            float y = BedAt(d);
            var origin = d * (Planet.Radius + y);
            var rot = Quaternion.LookRotation(f, d);          // x = across the track, y = up, z = along it
            var mb = new MeshBuilder();
            Matrix4x4 M(float x, float yy, float z, float sx, float sy, float sz) => Matrix4x4.TRS(rot * new Vector3(x * k, yy, z), rot, new Vector3(sx, sy, sz));
            mb.Box(M(4.0f, -0.6f, 0f, 3.6f, 2.4f, 36f), Platform, Platform);
            mb.Box(M(2.45f, 0.61f, 0f, 0.35f, 0.02f, 36f), Yellow, Yellow);                       // the line not to cross
            for (int i = -2; i <= 2; i++) mb.Box(M(4.9f, 2.05f, i * 4.2f, 0.18f, 2.9f, 0.18f), Post, Post);
            mb.Box(M(4.0f, 3.55f, 0f, 3.9f, 0.16f, 19f), Canopy, Canopy);
            mb.Box(M(8.9f, 0.4f, 0f, 5.4f, 4.4f, 11f), Wall, Wall);                               // the station house
            mb.Roof(Matrix4x4.TRS(rot * new Vector3(8.9f * k, 2.6f, 0f), rot * Quaternion.Euler(0f, 90f, 0f), new Vector3(11.8f, 1.7f, 6.2f)), Tile);
            mb.Box(M(6.15f, 1.3f, 0f, 0.12f, 1.9f, 1.3f), Post, Post);                            // its door
            for (int i = -1; i <= 1; i += 2) mb.Box(M(6.15f, 1.75f, i * 3.2f, 0.12f, 0.9f, 1.5f), new Color(0.45f, 0.62f, 0.72f), Post);
            for (int i = -1; i <= 1; i += 2) mb.Box(M(3.4f, 1.0f, i * 12f, 0.5f, 0.45f, 1.8f), Sleeper, Sleeper); // benches
            var mesh = mb.ToMesh("Station");
            if (mesh == null) { c.State = 2; return; }
            var go = new GameObject("Station");
            go.transform.SetParent(_planet.transform, false);
            go.transform.localPosition = origin;
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _mat;
            c.Col = go.AddComponent<MeshCollider>();
            c.Col.sharedMesh = mesh;
            c.Go = go; c.Mesh = mesh;
        }

        // ------------------------------------------------------------------ trains

        sealed class Train
        {
            public int Ci; public float D;              // where the front of the train is: chunk and metres along it
            public int Sense;                           // +1 runs towards the end of the chunk, -1 towards its start
            public float Speed, Wait;
            public Chunk Called;                        // the station it last stopped at
            public GameObject Root; public Transform[] Cars; public Mesh[] Meshes;
        }

        /// <summary>The chunk that carries the line on from this one, if it is loaded.</summary>
        static bool Onward(int ci, int sense, out int next)
        {
            next = ci + sense;
            var c = _chunks[ci];
            if ((c.Flags & (sense > 0 ? 2 : 1)) != 0 || next < 0 || next >= _chunks.Length) return false;
            var n = _chunks[next];
            return n.Cls == 3 && n.P != null && n.Cum != null;
        }

        /// <summary>Walks to a place on the line, crossing into neighbouring chunks. False if the line ends first.</summary>
        static bool Locate(ref int ci, ref float d, out Vector3 dir, out Vector3 fwd)
        {
            bool ok = true;
            var c = _chunks[ci];
            for (int guard = 0; guard < 40; guard++)
            {
                float len = c.Cum[c.N - 1];
                if (d < 0f)
                {
                    if (!Onward(ci, -1, out int j)) { d = 0f; ok = false; break; }
                    ci = j; c = _chunks[ci]; d += c.Cum[c.N - 1];
                }
                else if (d > len)
                {
                    if (!Onward(ci, 1, out int j)) { d = len; ok = false; break; }
                    d -= len; ci = j; c = _chunks[ci];
                }
                else break;
            }
            int s = 0;
            while (s < c.N - 2 && c.Cum[s + 1] < d) s++;
            float seg = Mathf.Max(1e-4f, c.Cum[s + 1] - c.Cum[s]);
            dir = Vector3.Lerp(c.P[s], c.P[s + 1], Mathf.Clamp01((d - c.Cum[s]) / seg)).normalized;
            fwd = (c.P[s + 1] - c.P[s]).normalized;
            return ok;
        }

        void RunTrains()
        {
            var pd = _planet.WorldToDir(_planet.Player.position);
            _trainTimer -= Time.deltaTime;
            if (_trainTimer <= 0f)
            {
                _trainTimer = 2.5f;
                if (_trains.Count < MaxTrains) Spawn(pd);
            }
            for (int i = _trains.Count - 1; i >= 0; i--)
                if (!Move(_trains[i], pd)) { Scrap(_trains[i]); _trains.RemoveAt(i); }
        }

        void Spawn(Vector3 pd)
        {
            Chunk pick = null; int seen = 0;
            foreach (var c in _active)
            {
                if (c.Cls != 3 || c.P == null || c.Cum == null || c.Cum[c.N - 1] < 20f) continue;
                float far = (c.Centre - pd).magnitude * Planet.Radius;
                if (far < 320f || far > 1300f) continue;
                bool taken = false;
                foreach (var t in _trains) if ((_chunks[t.Ci].Centre - c.Centre).magnitude * Planet.Radius < 500f) taken = true;
                if (taken) continue;
                if (_rng.Next(++seen) == 0) pick = c;
            }
            if (pick == null) return;
            var tr = new Train { Ci = pick.Index, D = pick.Cum[pick.N - 1] * 0.5f, Sense = _rng.Next(2) == 0 ? 1 : -1, Speed = TrainSpeed };
            tr.Root = new GameObject("Train");
            tr.Root.transform.SetParent(_planet.transform, false);
            tr.Cars = new Transform[TrainCars]; tr.Meshes = new Mesh[TrainCars];
            bool freight = _rng.Next(3) == 0;
            Color[] liveries = { new Color(0.8f, 0.16f, 0.14f), new Color(0.13f, 0.36f, 0.66f), new Color(0.95f, 0.72f, 0.12f), new Color(0.16f, 0.5f, 0.32f), new Color(0.9f, 0.9f, 0.88f), new Color(0.93f, 0.45f, 0.1f) };
            Color[] wagons = { new Color(0.55f, 0.25f, 0.18f), new Color(0.3f, 0.4f, 0.5f), new Color(0.42f, 0.44f, 0.3f), new Color(0.6f, 0.52f, 0.36f), new Color(0.35f, 0.35f, 0.38f) };
            var livery = liveries[_rng.Next(liveries.Length)];
            for (int k = 0; k < TrainCars; k++)
            {
                var mesh = CarMesh(k == 0 ? 0 : freight ? 2 : 1, k == 0 || !freight ? livery : wagons[_rng.Next(wagons.Length)]);
                var go = new GameObject(k == 0 ? "Loco" : "Car");
                go.transform.SetParent(tr.Root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _mat;
                tr.Cars[k] = go.transform; tr.Meshes[k] = mesh;
            }
            _trains.Add(tr);
            Move(tr, pd);
        }

        /// <summary>A vehicle 7 m long standing on the rails at its origin, nose along +z. Kind 0 engine, 1 coach, 2 wagon.</summary>
        static Mesh CarMesh(int kind, Color col)
        {
            var mb = new MeshBuilder();
            var dark = new Color(0.16f, 0.16f, 0.18f); var glass = new Color(0.2f, 0.27f, 0.33f); var roof = new Color(0.72f, 0.73f, 0.75f);
            Matrix4x4 B(float x, float y, float z, float sx, float sy, float sz) => Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, new Vector3(sx, sy, sz));
            for (int e = -1; e <= 1; e += 2) mb.Box(B(0f, 0.27f, e * 2.3f, 1.9f, 0.5f, 1.7f), dark, dark);   // bogies
            mb.Box(B(0f, 0.62f, 0f, 2.1f, 0.22f, 7.2f), dark, dark);                                          // frame and couplers
            if (kind == 0)
            {
                mb.Box(B(0f, 1.6f, -0.4f, 2.3f, 1.75f, 6.0f), col, roof);
                mb.Box(B(0f, 1.25f, 2.95f, 2.2f, 1.05f, 0.9f), col, col);                                     // nose
                mb.Box(B(0f, 2.0f, 2.3f, 2.34f, 0.6f, 0.9f), glass, roof);                                    // cab windows
                mb.Box(B(0f, 2.0f, 2.62f, 1.9f, 0.6f, 0.06f), glass, glass);
                mb.Box(B(0f, 1.05f, -0.4f, 2.34f, 0.22f, 6.0f), Color.white, Color.white);                    // stripe
                mb.Box(B(0f, 1.5f, 3.41f, 0.5f, 0.25f, 0.04f), new Color(1f, 0.95f, 0.7f), Color.white);      // headlight
                mb.Box(B(0f, 2.6f, -1.6f, 1.2f, 0.25f, 2.2f), dark, dark);                                    // roof gear
            }
            else if (kind == 1)
            {
                mb.Box(B(0f, 1.65f, 0f, 2.3f, 1.85f, 6.9f), col, roof);
                mb.Box(B(0f, 1.95f, 0f, 2.34f, 0.62f, 5.9f), glass, glass);                                   // the band of windows
                mb.Box(B(0f, 2.62f, 0f, 1.9f, 0.14f, 6.5f), roof, roof);
            }
            else
            {
                mb.Box(B(0f, 1.6f, 0f, 2.3f, 1.75f, 6.7f), col, Color.Lerp(col, Color.black, 0.25f));
                for (int e = -1; e <= 1; e++) mb.Box(B(0f, 1.6f, e * 2.2f, 2.36f, 1.75f, 0.12f), Color.Lerp(col, Color.black, 0.3f), col); // ribs
            }
            return mb.ToMesh("TrainCar");
        }

        /// <summary>Runs one train for a frame. False when it should be taken away.</summary>
        bool Move(Train t, Vector3 pd)
        {
            var c = _chunks[t.Ci];
            if (c.P == null || c.Cum == null) return false;
            float want = TrainSpeed;
            if (t.Wait > 0f) { t.Wait -= Time.deltaTime; want = 0f; }
            t.Speed = Mathf.MoveTowards(t.Speed, want, 5f * Time.deltaTime);
            t.D += t.Sense * t.Speed * Time.deltaTime;
            if (!Locate(ref t.Ci, ref t.D, out var head, out _))
            {
                // The end of the line (or of what is loaded): out of sight the train goes away, in sight it waits and heads back.
                if ((head - pd).magnitude * Planet.Radius > 900f) return false;
                int ci = t.Ci; float d = t.D - t.Sense * CarGap * (TrainCars - 1);
                Locate(ref ci, ref d, out _, out _);
                t.Ci = ci; t.D = d; t.Sense = -t.Sense; t.Speed = 0f; t.Wait = 5f; t.Called = null;
                System.Array.Reverse(t.Cars);
                head = Vector3.zero;
            }
            else if ((head - pd).magnitude * Planet.Radius > 1750f) return false;
            Vector3 mid = Vector3.zero;
            for (int k = 0; k < TrainCars; k++)
            {
                int ca = t.Ci, cb = t.Ci;
                float da = t.D - t.Sense * (k * CarGap + 1.2f), db = t.D - t.Sense * (k * CarGap + 5.8f);
                Locate(ref ca, ref da, out var a, out _);
                Locate(ref cb, ref db, out var b, out _);
                var pa = a * (Planet.Radius + BedAlong(_chunks[ca], da, _planet) + RailTop);
                var pb = b * (Planet.Radius + BedAlong(_chunks[cb], db, _planet) + RailTop);
                var along = pa - pb;
                if (along.sqrMagnitude < 1e-6f) continue;
                var car = t.Cars[k];
                // After turning back the engine pushes from the rear, so every vehicle keeps facing the way it was built.
                bool engineLeads = car.name == "Loco" ? k == 0 : t.Cars[0].name == "Loco";
                car.localPosition = (pa + pb) * 0.5f;
                car.localRotation = Quaternion.LookRotation(engineLeads ? along : -along, (a + b).normalized);
                if (k == TrainCars / 2) mid = ((a + b) * 0.5f).normalized;
            }
            // Call at a station: brake so the middle of the train comes to rest by the platform.
            if (t.Wait <= 0f && mid.sqrMagnitude > 0.5f)
                foreach (var st in _stations)
                {
                    if (st == t.Called || st.P == null || st.Go == null) continue;
                    if ((st.P[0] - mid).magnitude * Planet.Radius > 21f) continue;
                    t.Called = st; t.Wait = 7f;
                    break;
                }
            return true;
        }

        void Scrap(Train t)
        {
            if (t.Meshes != null) foreach (var m in t.Meshes) if (m != null) Destroy(m);
            if (t.Root != null) Destroy(t.Root);
        }

        void OnDestroy()
        {
            foreach (var t in _trains) Scrap(t);
            _trains.Clear();
            foreach (var c in _active) Drop(c);
        }

    }
}
