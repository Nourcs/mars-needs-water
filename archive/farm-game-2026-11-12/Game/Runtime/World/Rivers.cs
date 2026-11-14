using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The world's real rivers (Natural Earth, baked by tools/earth/bake_rivers.py into Resources/Earth/earth_rivers).
    /// Every point of a river carries the height of its water. Near the player a river cuts its own channel into the
    /// ground (<see cref="Ground"/>, called from <see cref="Planet.Height"/>), with banks that always stand above the
    /// water, and gets a ribbon of water. Roads and railways cross on bridges (<see cref="Level"/>); trees and houses
    /// keep out (<see cref="Near"/>). The water is for looking at and wading through: it is not a source yet.
    /// </summary>
    public sealed class Rivers : MonoBehaviour
    {
        const float Step = 5f, Depth = 1.4f;
        const float ActiveRange = 1900f, BuildIn = 950f, DropOut = 1250f;
        const float Cell = 200f, Margin = 34f, Sample = 40f;

        internal sealed class Chunk
        {
            public int Index;
            public int First, N, RawN;                  // N grows while the chunk is active: its points are then at most 10 m apart
            public byte Cls, Flags;                     // class 0 (the great rivers) .. 4 (streams); flags 1/2 = the river really ends at the start/end
            public Vector3 Centre; public float Rad;
            public Vector3[] P; public float[] S;       // unit directions and water heights, worked out while the chunk is active
            public byte[] Off;                          // per point: 0 river, 1 its mouth (already under a lake or the sea), 2 no river (a rebuilt city, an airfield, the farm)
            public bool Skip(int s) => Off[s] == 2 || Off[s + 1] == 2 || (Off[s] == 1 && Off[s + 1] == 1);
            public bool Active;
            public GameObject Go; public Mesh Mesh;
            // Wide enough that the ground's mesh (a point every 5 m near the player) always has points on the river bed:
            // a narrower channel falls between them and the bed shows through the water.
            public float Half => Cls == 0 ? 10f : Cls == 1 ? 9f : Cls == 2 ? 8f : Cls == 3 ? 7.5f : 7f;
            public float Bank => 6f;
        }

        internal static Chunk[] _chunks;
        static byte[] _raw;
        static int _pts;
        static bool _loaded, _hasFocus;
        static Vector3 _focus;
        static readonly List<Chunk> _active = new List<Chunk>();
        static readonly Dictionary<long, List<Chunk>> _grid = new Dictionary<long, List<Chunk>>();

        Planet _planet;
        Material _mat;
        float _timer;
        readonly List<Chunk> _want = new List<Chunk>();

        static readonly Color Mid = new Color(0.1f, 0.4f, 0.62f, 0.8f), Edge = new Color(0.3f, 0.62f, 0.74f, 0.35f);

        public void Init(Planet planet)
        {
            _planet = planet;
            _mat = planet.SeaMaterial;
            Load();
        }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _chunks = new Chunk[0];
            var ta = Resources.Load<TextAsset>("Earth/earth_rivers");
            if (ta == null) { Debug.LogWarning("Earth/earth_rivers missing: no rivers."); return; }
            var b = ta.bytes;
            if (b.Length < 16 || b[0] != 'M' || b[1] != 'N' || b[2] != 'W' || b[3] != 'V') return;
            int n = System.BitConverter.ToInt32(b, 8);
            if (b.Length < 16 + n * 20) return;
            _raw = b;
            _pts = 16 + n * 20;
            _chunks = new Chunk[n];
            for (int i = 0, o = 16; i < n; i++, o += 20)
                _chunks[i] = new Chunk
                {
                    Index = i,
                    First = System.BitConverter.ToInt32(b, o), N = System.BitConverter.ToUInt16(b, o + 4), RawN = System.BitConverter.ToUInt16(b, o + 4), Cls = b[o + 6], Flags = b[o + 7],
                    Centre = EarthData.Dir(System.BitConverter.ToSingle(b, o + 8), System.BitConverter.ToSingle(b, o + 12)),
                    Rad = System.BitConverter.ToSingle(b, o + 16),
                };
        }

        static Vector3[] Points(Chunk c)
        {
            if (c.P != null) return c.P;
            var p = new Vector3[c.RawN];
            for (int k = 0, o = _pts + c.First * 12; k < c.RawN; k++, o += 12)
                p[k] = EarthData.Dir(System.BitConverter.ToSingle(_raw, o), System.BitConverter.ToSingle(_raw, o + 4));
            return p;
        }

        /// <summary>A chunk's points from the file, with points added so none are more than 10 m apart.</summary>
        static Vector3[] Dense(Chunk c)
        {
            var pts = new List<Vector3>();
            Vector3 prev = default;
            for (int k = 0, o = _pts + c.First * 12; k < c.RawN; k++, o += 12)
            {
                var q = EarthData.Dir(System.BitConverter.ToSingle(_raw, o), System.BitConverter.ToSingle(_raw, o + 4));
                if (k > 0)
                {
                    int n = Mathf.Max(1, Mathf.CeilToInt((q - prev).magnitude * Planet.Radius / 10f));
                    for (int j = 1; j < n; j++) pts.Add(Vector3.Lerp(prev, q, j / (float)n).normalized);
                }
                pts.Add(q); prev = q;
            }
            return pts.ToArray();
        }

        static Dictionary<long, List<int>> _index;

        static long IndexKey(Vector3 p)
        {
            long x = Mathf.FloorToInt(p.x / 1000f) + 512, y = Mathf.FloorToInt(p.y / 1000f) + 512, z = Mathf.FloorToInt(p.z / 1000f) + 512;
            return (x << 40) | (y << 20) | z;
        }

        /// <summary>
        /// Does any river come into a region? The region is given by a centre, a radius that contains it, and a test
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
                    if (false) continue;
                    if ((c.Centre - centre).magnitude * Planet.Radius - c.Rad > radius) continue;
                    var pts = Points(c);
                    for (int s = 0; s < pts.Length - 1; s++)
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

        /// <summary>Tells the rivers where the player is, so the ones within about 2 km cut their channels. Cheap to call often.</summary>
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
                if ((c.Centre - dir).magnitude * Planet.Radius - c.Rad > ActiveRange) continue;
                if (CityReplica.Covers(c.Centre) > 0.5f) continue;   // a rebuilt city has its own river
                c.Active = true;
                if (c.P == null)
                {
                    // Points at most 10 m apart, each with its own water height taken from the ground there, so the
                    // water follows the land between the river's own points instead of cutting straight across a dip.
                    c.P = Dense(c); c.N = c.P.Length; c.S = new float[c.N]; c.Off = new byte[c.N];
                    Settle(c);
                }
                keep.Add(c);
            }
            foreach (var c in _active) if (!c.Active) { Drop(c); c.P = null; c.S = null; c.Off = null; c.N = c.RawN; }
            _active.Clear();
            _active.AddRange(keep);
            _grid.Clear();
            foreach (var c in _active) Register(c);
        }

        /// <summary>
        /// The water's height at each point of a river comes from the ground the game really has there (the
        /// landscape with its small hills, a rebuilt city's ground), not from the map the river was drawn on: just
        /// below the lowest of the ground at its centre and at both edges. So water can never stand above the land
        /// beside it. Where the point is already under a lake or the sea, inside a rebuilt city or on the farm,
        /// there is no river.
        /// </summary>
        static void Settle(Chunk c)
        {
            var planet = Planet.Current;
            // The chunk's points with three more from the river before and after it, so both chunks work out the
            // same water height where they meet.
            var ext = new List<Vector3>(); int first = 0;
            if ((c.Flags & 1) == 0 && c.Index > 0)
            {
                var o = _chunks[c.Index - 1]; var q = o.P ?? Dense(o);
                if (q.Length > 1 && (q[q.Length - 1] - c.P[0]).magnitude * Planet.Radius < 1f)
                    for (int k = Mathf.Max(0, q.Length - 4); k < q.Length - 1; k++) ext.Add(q[k]);
            }
            first = ext.Count;
            ext.AddRange(c.P);
            if ((c.Flags & 2) == 0 && c.Index + 1 < _chunks.Length)
            {
                var o = _chunks[c.Index + 1]; var q = o.P ?? Dense(o);
                if (q.Length > 1 && (q[0] - c.P[c.N - 1]).magnitude * Planet.Radius < 1f)
                    for (int k = 1; k < Mathf.Min(q.Length, 4); k++) ext.Add(q[k]);
            }
            int n = ext.Count;
            var raw = new float[n]; var off = new byte[n];
            for (int k = 0; k < n; k++)
            {
                var d = ext[k];
                var f = ext[Mathf.Min(k + 1, n - 1)] - ext[Mathf.Max(k - 1, 0)];
                f = f * (1f / Mathf.Max(1e-9f, f.magnitude));
                var side = Vector3.Cross(d, f).normalized;
                float g = Planet.HeightBeforeRivers(d), low = g;
                // the lowest ground across the channel and its banks, so a river on a slope sits in a cut, not on a wall
                float reach = c.Half + c.Bank;
                for (int q = 1; q <= 4; q++)
                {
                    float at = reach * q / 4f;
                    low = Mathf.Min(low, Planet.HeightBeforeRivers((d * Planet.Radius - side * at).normalized));
                    low = Mathf.Min(low, Planet.HeightBeforeRivers((d * Planet.Radius + side * at).normalized));
                }
                float wl = Planet.WaterLevel(d);
                if (CityReplica.Covers(d) > 0.5f || Hubs.Near(d, 6f) || (planet != null && planet.FarmCheb(d) < Planet.FarmHalf + 110f)) { off[k] = 2; raw[k] = wl; continue; }
                if (g < wl + 0.05f) { off[k] = 1; raw[k] = wl; continue; }
                raw[k] = Mathf.Max(wl + 0.05f, low - 0.25f);
            }
            // Water never stands higher than the ground a little way up or down the river either (a bend, a steep
            // reach): each point takes the lowest of itself and its two neighbours on each side.
            for (int k = 0; k < c.N; k++)
            {
                int e = first + k; float v = raw[e];
                if (off[e] == 0)
                    for (int q = Mathf.Max(0, e - 2); q <= Mathf.Min(n - 1, e + 2); q++) if (off[q] == 0 && raw[q] < v) v = raw[q];
                c.S[k] = v; c.Off[k] = off[e];
            }
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

        /// <summary>The nearest river to a spot: metres from its centre line, its water height there, and its size. False if none is close.</summary>
        static bool Probe(Vector3 dir, out float dist, out float surface, out float half, out float bank)
        {
            dist = float.MaxValue; surface = 0f; half = 0f; bank = 0f;
            if (!_hasFocus || !_grid.TryGetValue(CellKey(dir * Planet.Radius), out var l)) return false;
            float best = float.MaxValue;                // how far outside the channel, so a big river wins over a stream beside it
            for (int i = 0; i < l.Count; i++)
            {
                var c = l[i];
                if (c.P == null) continue;
                float reach = c.Half + c.Bank * 2f;
                if ((c.Centre - dir).magnitude * Planet.Radius > c.Rad + reach) continue;
                for (int s = 0; s < c.N - 1; s++)
                {
                    if (c.Skip(s)) continue;                      // no river on this stretch
                    var a = c.P[s]; var ab = c.P[s + 1] - a;
                    float t = Mathf.Clamp01(Vector3.Dot(dir - a, ab) / Mathf.Max(1e-14f, ab.sqrMagnitude));
                    float d = (dir - (a + ab * t).normalized).magnitude * Planet.Radius;
                    if (d > reach || d - c.Half >= best) continue;
                    best = d - c.Half; dist = d; half = c.Half; bank = c.Bank;
                    surface = Mathf.Lerp(c.S[s], c.S[s + 1], t);
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>One river chunk at a spot: metres from its centre line and its water height there. False beyond its banks.</summary>
        static bool Reach(Chunk c, Vector3 dir, out float dist, out float surface)
        {
            dist = float.MaxValue; surface = 0f;
            if (c.P == null) return false;
            float reach = c.Half + c.Bank;
            if ((c.Centre - dir).magnitude * Planet.Radius > c.Rad + reach) return false;
            for (int s = 0; s < c.N - 1; s++)
            {
                if (c.Skip(s)) continue;
                var a = c.P[s]; var ab = c.P[s + 1] - a;
                float t = Mathf.Clamp01(Vector3.Dot(dir - a, ab) / Mathf.Max(1e-14f, ab.sqrMagnitude));
                float d = (dir - (a + ab * t).normalized).magnitude * Planet.Radius;
                if (d > reach || d >= dist) continue;
                dist = d; surface = Mathf.Lerp(c.S[s], c.S[s + 1], t);
            }
            return dist < float.MaxValue;
        }

        /// <summary>The lowest water of any river whose channel or bank reaches this spot.</summary>
        static bool Lowest(Vector3 dir, out float surface)
        {
            surface = float.MaxValue;
            if (!_hasFocus || !_grid.TryGetValue(CellKey(dir * Planet.Radius), out var l)) return false;
            for (int i = 0; i < l.Count; i++)
                if (Reach(l[i], dir, out _, out float s) && s < surface) surface = s;
            return surface < float.MaxValue;
        }

        /// <summary>
        /// Cuts the river's channel into the ground. It only ever cuts: the ground is never raised into a wall to
        /// hold water up, because the water takes its height from the ground in the first place.
        /// </summary>
        public static void Ground(Vector3 dir, ref float h)
        {
            if (!_hasFocus || !_grid.TryGetValue(CellKey(dir * Planet.Radius), out var l)) return;
            if (h < Planet.WaterLevel(dir) - 0.2f) return;          // already under a lake or the sea
            // Every river that reaches this spot cuts it (where two meet, the lower one wins).
            float h0 = h;
            for (int i = 0; i < l.Count; i++)
            {
                if (!Reach(l[i], dir, out float d, out float s)) continue;
                float half = l[i].Half, bank = l[i].Bank;
                // a flat bed the full width of the river, then the bank
                if (d <= half) { float u = d / half; u *= u; h = Mathf.Min(h, s - Depth * (1f - 0.3f * u * u)); }
                else if (d <= half + bank) h = Mathf.Min(h, Mathf.Lerp(s - Depth * 0.7f, h0, Mathf.SmoothStep(0f, 1f, (d - half) / bank)));
            }
        }

        /// <summary>Is this spot in a river or on its bank? Gives the water height, so roads and railways can bridge it.</summary>
        public static bool Level(Vector3 dir, out float surface)
        {
            if (Probe(dir, out float d, out surface, out float half, out float bank) && d <= half + bank) return true;
            surface = 0f;
            return false;
        }

        /// <summary>Is this spot on a river's bed (under its water)?</summary>
        public static bool Bed(Vector3 dir) => Probe(dir, out float d, out _, out float half, out _) && d <= half * 0.9f;

        /// <summary>Is this spot within <paramref name="metres"/> of the water's edge of a river?</summary>
        public static bool Near(Vector3 dir, float metres)
            => Probe(dir, out float d, out _, out float half, out float bank) && d <= half + Mathf.Min(metres, bank * 2f);

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            if (_want.Count > 0)
            {
                var c = _want[_want.Count - 1];
                _want.RemoveAt(_want.Count - 1);
                if (c.Go == null && c.Active) Build(c);
            }
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.4f;
            var pd = _planet.WorldToDir(_planet.Player.position);
            Focus(pd);
            _want.Clear();
            foreach (var c in _active)
            {
                float rough = (c.Centre - pd).magnitude * Planet.Radius - c.Rad;
                if (c.Go == null) { if (rough < BuildIn && c.Mesh == null) _want.Add(c); }
                else if (rough > DropOut) Drop(c);
            }
            _want.Sort((a, b) => ((b.Centre - pd).sqrMagnitude).CompareTo((a.Centre - pd).sqrMagnitude));
        }

        static void Drop(Chunk c)
        {
            if (c.Go != null) Destroy(c.Go);
            if (c.Mesh != null) Destroy(c.Mesh);
            c.Go = null; c.Mesh = null;
        }

        /// <summary>The water of one chunk as cross-sections: four points across (edge, inner, inner, edge), each with its own height.</summary>
        internal sealed class Ribbon
        {
            public int M;
            public Vector3[] Dir, Side;
            public float[] Lev;
            public float[] Off4 = new float[4];         // metres from the centre line of the four points
            public float[][] Y;                         // [cross-section][4] heights
            public bool[] Skip;                         // no water between this cross-section and the one before
        }

        /// <summary>
        /// The ribbon of water: level across, following the river's water heights along it. A point of it never
        /// hangs in the air: where the ground under it lies well below the water (the next reach of a steep
        /// river, another river's channel) that point comes down to just above the ground, as rapids do.
        /// </summary>
        internal static Ribbon Shape(Chunk c, Planet planet)
        {
            var dirs = new List<Vector3>(); var fwd = new List<Vector3>(); var lev = new List<float>(); var off = new List<bool>();
            for (int s = 0; s < c.N - 1; s++)
            {
                Vector3 a = c.P[s], b = c.P[s + 1];
                float len = (b - a).magnitude * Planet.Radius;
                if (len < 0.3f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / Step));
                var f = (b - a) * (1f / (b - a).magnitude);
                for (int q = dirs.Count == 0 ? 0 : 1; q <= n; q++)
                {
                    float t = q / (float)n;
                    dirs.Add(Vector3.Lerp(a, b, t).normalized); fwd.Add(f); lev.Add(Mathf.Lerp(c.S[s], c.S[s + 1], t));
                    off.Add(c.Skip(s));
                }
            }
            int m = dirs.Count - 1;
            var r = new Ribbon { M = m };
            if (m < 1) return r;
            float wide = c.Half + c.Bank * 0.8f, inner = c.Half * 0.6f;
            r.Off4[0] = -wide; r.Off4[1] = -inner; r.Off4[2] = inner; r.Off4[3] = wide;
            r.Dir = dirs.ToArray(); r.Lev = lev.ToArray(); r.Skip = off.ToArray();
            r.Side = new Vector3[m + 1]; r.Y = new float[m + 1][];
            for (int s = 0; s <= m; s++)
            {
                var t = s > 0 && s < m ? (fwd[s] + fwd[s - 1]).normalized : fwd[s];
                r.Side[s] = Vector3.Cross(dirs[s], t).normalized;
                // Level across, like a lake: the edges run into the banks and the ground hides them, so the shore
                // is wherever the ground meets the water. Only where an edge would hang in the air (the ground
                // falls away beside the river) the whole cross-section comes down to that ground.
                float y = lev[s];
                // where another river's channel meets this one, the water comes down to the lower of the two
                for (int k = 0; k < 4; k++)
                    if (Lowest((dirs[s] * Planet.Radius + r.Side[s] * r.Off4[k]).normalized, out float other) && other < y) y = other;
                for (int k = 0; k < 4; k += 3)
                {
                    var e = (dirs[s] * Planet.Radius + r.Side[s] * r.Off4[k]).normalized;
                    float g = planet.Height(e);
                    if (g < y && !(Lowest(e, out float there) && g <= there)) y = g - 0.05f;     // lower ground that is not another river's bed
                }
                y = Mathf.Max(y, lev[s] - 0.7f);                    // never under its own bed
                r.Y[s] = new float[] { y, y, y, y };
                if (Hubs.Near(dirs[s], 0f)) r.Skip[s] = true;          // an airfield or a quay has taken the ground
            }
            return r;
        }

        void Build(Chunk c)
        {
            var rb = Shape(c, _planet);
            int m = rb.M;
            if (m < 1) return;
            var origin = c.Centre * (Planet.Radius + _planet.Height(c.Centre));
            var mb = new MeshBuilder();
            var I = Matrix4x4.identity;
            Vector3 P(int s, int k) => (rb.Dir[s] * Planet.Radius + rb.Side[s] * rb.Off4[k]).normalized * (Planet.Radius + rb.Y[s][k]) - origin;
            for (int s = 0; s < m; s++)
            {
                if (rb.Skip[s + 1] || (s > 0 && rb.Skip[s] && Hubs.Near(rb.Dir[s], 0f))) continue;   // a lake, the sea, a city, the farm or a hub has taken over
                mb.Quad(I, P(s, 0), P(s + 1, 0), P(s + 1, 1), P(s, 1), Edge);
                mb.Quad(I, P(s, 1), P(s + 1, 1), P(s + 1, 2), P(s, 2), Mid);
                mb.Quad(I, P(s, 2), P(s + 1, 2), P(s + 1, 3), P(s, 3), Edge);
            }
            var mesh = mb.ToMesh("River");
            if (mesh == null) return;
            var go = new GameObject("River");
            go.transform.SetParent(_planet.transform, false);
            go.transform.localPosition = origin;
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rd = go.AddComponent<MeshRenderer>();
            rd.sharedMaterial = _mat;
            rd.shadowCastingMode = ShadowCastingMode.Off;
            rd.receiveShadows = false;
            c.Go = go; c.Mesh = mesh;
        }

        void OnDestroy()
        {
            foreach (var c in _active) Drop(c);
        }
    }
}
