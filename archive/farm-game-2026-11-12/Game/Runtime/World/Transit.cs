using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Public transport inside the rebuilt city cores (tools/earth/bake_transit.py, Resources/Transit/&lt;city&gt;):
    /// shelters at the real bus stops, buses running up and down the real main streets, tram rails along the real
    /// tram and light-rail lines with trams that call at their stops, and an entrance with a sign at every real metro
    /// station. Only the city the player is in is alive. Presentation only.
    /// </summary>
    public sealed class Transit : MonoBehaviour
    {
        const float StreetLift = 0.18f;

        sealed class Route
        {
            public bool Tram;
            public Vector2[] P; public float[] Cum;     // the line in the city's own metres, and metres along it
            public float[] Calls;                       // metres along the line where it stops
        }

        sealed class Vehicle
        {
            public Route R; public float D, Speed, Wait; public int Sense, NextCall = -1;
            public Transform[] Parts;                   // a bus is one part, a tram two
        }

        Planet _planet;
        Material _mat;
        CityReplica.City _city;
        GameObject _root;
        readonly List<Mesh> _meshes = new List<Mesh>();
        readonly List<Vehicle> _vehicles = new List<Vehicle>();
        float _timer;

        static readonly Color Steel = new Color(0.62f, 0.63f, 0.66f), Dark = new Color(0.16f, 0.16f, 0.18f), Glass = new Color(0.45f, 0.62f, 0.72f);
        static readonly Color Pole = new Color(0.3f, 0.31f, 0.34f), White = new Color(0.94f, 0.94f, 0.92f);

        public void Init(Planet planet)
        {
            _planet = planet;
            _mat = Prims.VertexColored(0.3f, false, 0.12f);
        }

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            if (_city != null) foreach (var v in _vehicles) Run(v);
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.7f;
            var pd = _planet.WorldToDir(_planet.Player.position);
            CityReplica.City here = null;
            foreach (var c in CityReplica.All)
            {
                if (c.Flat || c.Spot == null) continue;
                var l = c.Local(pd);
                if (Vector3.Dot(pd, c.Dir) > 0.9f && Mathf.Abs(l.x) < c.HalfW + 260f && Mathf.Abs(l.y) < c.HalfD + 260f) { here = c; break; }
            }
            if (here == _city) return;
            Clear();
            _city = here;
            if (here != null) Build(here);
        }

        void Clear()
        {
            if (_root != null) Destroy(_root);
            foreach (var m in _meshes) if (m != null) Destroy(m);
            _meshes.Clear(); _vehicles.Clear();
            _root = null; _city = null;
        }

        void OnDestroy() => Clear();

        /// <summary>A point of the city's plan at street height (plus a little), in the root's own coordinates.</summary>
        Vector3 At(Vector2 p, float above, out Vector3 up)
        {
            var g = _city.Spot.Ground(p.x, p.y, out float h);
            up = Quaternion.Inverse(_city.Spot.Rot) * _city.Spot.DirAt(p.x, p.y);
            return g + up * (Mathf.Max(h, _city.Base) - h + StreetLift + above);
        }

        Color Livery(bool tram)
        {
            switch (_city.Name)
            {
                case "london": return new Color(0.82f, 0.1f, 0.1f);
                case "newyork": return tram ? new Color(0.2f, 0.3f, 0.6f) : new Color(0.16f, 0.35f, 0.7f);
                case "paris": return new Color(0.2f, 0.62f, 0.52f);
                case "berlin": return new Color(0.96f, 0.8f, 0.1f);
                case "hongkong": return tram ? new Color(0.1f, 0.42f, 0.25f) : new Color(0.85f, 0.2f, 0.2f);
                case "sanfrancisco": return tram ? new Color(0.72f, 0.16f, 0.14f) : new Color(0.9f, 0.45f, 0.12f);
                case "toronto": return new Color(0.78f, 0.12f, 0.14f);
                case "tokyo": return new Color(0.35f, 0.7f, 0.3f);
                case "moscow": return new Color(0.15f, 0.4f, 0.75f);
                case "istanbul": return tram ? new Color(0.8f, 0.15f, 0.15f) : new Color(0.9f, 0.75f, 0.1f);
                case "singapore": return new Color(0.75f, 0.15f, 0.3f);
                default: return tram ? new Color(0.9f, 0.78f, 0.15f) : new Color(0.92f, 0.5f, 0.12f);
            }
        }

        void Build(CityReplica.City c)
        {
            var ta = Resources.Load<TextAsset>("Transit/" + c.Name);
            if (ta == null) return;
            var b = ta.bytes;
            if (b.Length < 12 || b[0] != 'M' || b[1] != 'N' || b[2] != 'W' || b[3] != 'P') return;
            int o = 8;
            int nr = System.BitConverter.ToUInt16(b, o); o += 2;
            var routes = new List<Route>();
            for (int i = 0; i < nr; i++)
            {
                var r = new Route { Tram = b[o] == 1 }; int n = System.BitConverter.ToUInt16(b, o + 1); o += 3;
                r.P = new Vector2[n]; r.Cum = new float[n];
                for (int k = 0; k < n; k++, o += 8)
                {
                    r.P[k] = new Vector2(System.BitConverter.ToSingle(b, o), System.BitConverter.ToSingle(b, o + 4));
                    if (k > 0) r.Cum[k] = r.Cum[k - 1] + (r.P[k] - r.P[k - 1]).magnitude;
                }
                if (n >= 2 && r.Cum[n - 1] > 30f) routes.Add(r);
            }
            int ns = System.BitConverter.ToUInt16(b, o); o += 2;
            var kinds = new byte[ns]; var pos = new Vector2[ns]; var yaw = new float[ns];
            for (int i = 0; i < ns; i++, o += 13)
            {
                kinds[i] = b[o]; pos[i] = new Vector2(System.BitConverter.ToSingle(b, o + 1), System.BitConverter.ToSingle(b, o + 5)); yaw[i] = System.BitConverter.ToSingle(b, o + 9);
            }

            _root = new GameObject("Transit " + c.Name);
            _root.transform.SetParent(_planet.transform, false);
            _root.transform.localPosition = c.Spot.Origin;
            _root.transform.localRotation = c.Spot.Rot;

            var mb = new MeshBuilder();
            var I = Matrix4x4.identity;
            var livery = Livery(false); var tramLivery = Livery(true);
            // Stops and stations.
            for (int i = 0; i < ns; i++)
            {
                var p = At(pos[i], 0f, out var up);
                if (kinds[i] == 0)
                {
                    // A shelter: two posts, a roof, a glass back, and the stop's sign. It faces the street.
                    var rot = Quaternion.LookRotation(Quaternion.Euler(0f, yaw[i], 0f) * Vector3.forward, up);
                    Matrix4x4 M(float x, float y, float z, float sx, float sy, float sz) => Matrix4x4.TRS(p + rot * new Vector3(x, y, z), rot, new Vector3(sx, sy, sz));
                    mb.Box(M(-1.1f, 1.1f, -0.45f, 0.08f, 2.2f, 0.08f), Pole, Pole);
                    mb.Box(M(1.1f, 1.1f, -0.45f, 0.08f, 2.2f, 0.08f), Pole, Pole);
                    mb.Box(M(0f, 2.25f, -0.1f, 2.6f, 0.08f, 1.1f), Pole, livery);
                    mb.Box(M(0f, 1.15f, -0.47f, 2.2f, 1.5f, 0.04f), Glass, Glass);
                    mb.Box(M(0f, 0.45f, -0.3f, 1.6f, 0.08f, 0.35f), Steel, Steel);                    // the bench
                    mb.Box(M(1.6f, 1.3f, 0f, 0.07f, 2.6f, 0.07f), Pole, Pole);
                    mb.Box(M(1.6f, 2.4f, 0f, 0.5f, 0.5f, 0.06f), livery, livery);
                }
                else if (kinds[i] == 2)
                {
                    // A metro entrance: a railed stairwell and a tall sign.
                    var rot = Quaternion.LookRotation(Vector3.forward - up * Vector3.Dot(Vector3.forward, up), up);
                    Matrix4x4 M(float x, float y, float z, float sx, float sy, float sz) => Matrix4x4.TRS(p + rot * new Vector3(x, y, z), rot, new Vector3(sx, sy, sz));
                    var sign = c.Name == "london" ? new Color(0.85f, 0.1f, 0.1f) : c.Name == "paris" ? new Color(0.15f, 0.5f, 0.3f) : c.Name == "newyork" ? new Color(0.1f, 0.45f, 0.25f) : new Color(0.15f, 0.35f, 0.75f);
                    mb.Box(M(0f, 0.03f, 0f, 1.5f, 0.04f, 3f), Dark, Dark);                              // the stairs going down
                    mb.Box(M(-0.8f, 0.5f, 0f, 0.07f, 1f, 3.1f), sign, sign);
                    mb.Box(M(0.8f, 0.5f, 0f, 0.07f, 1f, 3.1f), sign, sign);
                    mb.Box(M(0f, 0.5f, -1.55f, 1.67f, 1f, 0.07f), sign, sign);
                    mb.Box(M(0f, 1.9f, 1.9f, 0.1f, 3.8f, 0.1f), Pole, Pole);
                    mb.Box(M(0f, 3.5f, 1.9f, 1.1f, 1.1f, 0.12f), sign, sign);
                    mb.Box(M(0f, 3.5f, 1.9f, 0.7f, 0.22f, 0.16f), White, White);
                }
            }
            // Tram rails, set into the street.
            foreach (var r in routes)
            {
                if (!r.Tram) continue;
                for (int k = 0; k < r.P.Length - 1; k++)
                {
                    Vector2 a = r.P[k], bb = r.P[k + 1]; float len = (bb - a).magnitude;
                    if (len < 0.05f) continue;
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / 6f));
                    var f = (bb - a) / len; var side = new Vector2(f.y, -f.x);
                    for (int q = 0; q < n; q++)
                    {
                        Vector2 p0 = Vector2.Lerp(a, bb, q / (float)n), p1 = Vector2.Lerp(a, bb, (q + 1) / (float)n);
                        for (int e = -1; e <= 1; e += 2)
                        {
                            Vector2 l0 = p0 + side * (e * 0.45f - 0.05f), r0 = p0 + side * (e * 0.45f + 0.05f), l1 = p1 + side * (e * 0.45f - 0.05f), r1 = p1 + side * (e * 0.45f + 0.05f);
                            mb.Quad(I, At(l0, 0.03f, out _), At(l1, 0.03f, out _), At(r1, 0.03f, out _), At(r0, 0.03f, out _), Steel);
                        }
                    }
                }
            }
            var mesh = mb.ToMesh("Transit");
            if (mesh != null)
            {
                _meshes.Add(mesh);
                var go = new GameObject("Stops");
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rd = go.AddComponent<MeshRenderer>();
                rd.sharedMaterial = _mat;
                rd.shadowCastingMode = ShadowCastingMode.Off;
            }

            // Where each line calls, and a vehicle for it.
            Mesh busMesh = null, tramMesh = null;
            int seed = 0;
            foreach (var r in routes)
            {
                var calls = new List<float>();
                for (int i = 0; i < ns; i++)
                {
                    if (kinds[i] != (r.Tram ? 1 : 0)) continue;
                    float best = r.Tram ? 6f : 9f, at = -1f;
                    for (int k = 0; k < r.P.Length - 1; k++)
                    {
                        var ab = r.P[k + 1] - r.P[k];
                        float t = Mathf.Clamp01(Vector2.Dot(pos[i] - r.P[k], ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                        float d = (pos[i] - (r.P[k] + ab * t)).magnitude;
                        if (d < best) { best = d; at = Mathf.Lerp(r.Cum[k], r.Cum[k + 1], t); }
                    }
                    if (at >= 0f) calls.Add(at);
                }
                calls.Sort();
                r.Calls = calls.ToArray();
                if (r.Tram) { if (tramMesh == null) { tramMesh = CarMesh(true, tramLivery); _meshes.Add(tramMesh); } }
                else if (busMesh == null) { busMesh = CarMesh(false, livery); _meshes.Add(busMesh); }
                float total = r.Cum[r.P.Length - 1];
                int count = total > 500f ? 2 : 1;
                for (int v = 0; v < count; v++)
                {
                    var veh = new Vehicle { R = r, D = total * ((seed * 37 % 100) / 100f * 0.5f + v * 0.5f), Sense = (seed + v) % 2 == 0 ? 1 : -1, Speed = r.Tram ? 8f : 7f };
                    seed++;
                    veh.Parts = new Transform[r.Tram ? 2 : 1];
                    for (int k = 0; k < veh.Parts.Length; k++)
                    {
                        var go = new GameObject(r.Tram ? "Tram" : "Bus");
                        go.transform.SetParent(_root.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = r.Tram ? tramMesh : busMesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial = _mat;
                        veh.Parts[k] = go.transform;
                    }
                    _vehicles.Add(veh);
                }
            }
        }

        /// <summary>A bus (5.6 m) or one half of a tram (5.2 m), standing at the origin with its nose along +z.</summary>
        static Mesh CarMesh(bool tram, Color col)
        {
            var mb = new MeshBuilder();
            float len = tram ? 5.2f : 5.6f, wide = tram ? 1.45f : 1.5f;
            Matrix4x4 B(float x, float y, float z, float sx, float sy, float sz) => Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, new Vector3(sx, sy, sz));
            mb.Box(B(0f, 0.2f, 0f, wide * 0.9f, 0.3f, len * 0.92f), Dark, Dark);                    // wheels and underside
            mb.Box(B(0f, 1.2f, 0f, wide, 1.75f, len), col, White);
            mb.Box(B(0f, 1.5f, 0f, wide + 0.03f, 0.65f, len * 0.9f), Dark, Dark);                    // side windows
            mb.Box(B(0f, 1.45f, len * 0.5f, wide * 0.88f, 0.8f, 0.04f), Dark, Dark);                 // windscreen
            mb.Box(B(0f, 1.45f, -len * 0.5f, wide * 0.88f, 0.8f, 0.04f), Dark, Dark);
            mb.Box(B(0f, 0.65f, len * 0.5f + 0.01f, wide * 0.8f, 0.14f, 0.04f), new Color(1f, 0.95f, 0.7f), White);
            if (tram) mb.Box(B(0f, 2.35f, 0f, 0.08f, 0.55f, 1.4f), Dark, Dark);                      // the pantograph
            return mb.ToMesh(tram ? "Tram" : "Bus");
        }

        static Vector2 Along(Route r, float d, out Vector2 fwd)
        {
            int n = r.P.Length; d = Mathf.Clamp(d, 0f, r.Cum[n - 1]);
            int s = 0;
            while (s < n - 2 && r.Cum[s + 1] < d) s++;
            float seg = Mathf.Max(1e-4f, r.Cum[s + 1] - r.Cum[s]);
            fwd = (r.P[s + 1] - r.P[s]) / seg;
            return Vector2.Lerp(r.P[s], r.P[s + 1], (d - r.Cum[s]) / seg);
        }

        /// <summary>Runs a bus or a tram up and down its line, calling at its stops and turning back at each end.</summary>
        void Run(Vehicle v)
        {
            var r = v.R; float total = r.Cum[r.P.Length - 1], dt = Time.deltaTime;
            float want = r.Tram ? 8f : 7f;
            if (v.Wait > 0f) { v.Wait -= dt; want = 0f; }
            v.Speed = Mathf.MoveTowards(v.Speed, want, 4f * dt);
            float before = v.D;
            v.D += v.Sense * v.Speed * dt;
            if (v.Wait <= 0f)
                for (int i = 0; i < r.Calls.Length; i++)
                {
                    float brake = r.Calls[i] - v.Sense * 6f;                       // start braking 6 m before the stop
                    if ((before - brake) * (v.D - brake) <= 0f && before != v.D && i != v.NextCall) { v.Wait = 4f; v.NextCall = i; break; }
                }
            if (v.D <= 0f || v.D >= total) { v.D = Mathf.Clamp(v.D, 0f, total); v.Sense = -v.Sense; v.Speed = 0f; v.Wait = 3f; v.NextCall = -1; }
            for (int k = 0; k < v.Parts.Length; k++)
            {
                float d = v.D - v.Sense * k * 5.5f;
                var p = Along(r, d, out var f);
                if (v.Sense < 0) f = -f;
                // A bus keeps to the right of its street; a tram runs on its rails.
                if (!r.Tram) p += new Vector2(f.y, -f.x) * 0.95f;
                var at = At(p, 0.02f, out var up);
                var ahead = At(p + f * 2f, 0.02f, out _);
                v.Parts[k].localPosition = at;
                if ((ahead - at).sqrMagnitude > 1e-4f) v.Parts[k].localRotation = Quaternion.LookRotation(ahead - at, up);
            }
        }
    }
}
