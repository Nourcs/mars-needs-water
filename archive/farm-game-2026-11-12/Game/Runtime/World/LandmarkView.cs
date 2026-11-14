using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The world's famous places (<see cref="Landmarks"/>), built from chunky shapes at their real latitude and
    /// longitude when the player is within 3 km, at a size you can walk around (not to the planet's 1 : 255 scale,
    /// or the Eiffel Tower would be knee-high). A collider when close, and a "you found it" banner the first time.
    /// Presentation only.
    /// </summary>
    public sealed class LandmarkView : MonoBehaviour
    {
        const float BuildRange = 3000f, DropRange = 3400f, ColliderRange = 400f, FoundRange = 160f;

        sealed class Live { public GameObject Go; public Mesh Mesh; public MeshCollider Col; }

        Planet _planet;
        Material _mat;
        readonly Dictionary<int, Live> _live = new Dictionary<int, Live>();
        float _timer, _bannerUntil;
        string _banner, _bannerSub;
        GUIStyle _big, _small;

        /// <summary>Landmarks found this session (indexes into <see cref="Landmarks.All"/>), for the map.</summary>
        public static readonly HashSet<int> Found = new HashSet<int>();

        public void Init(Planet planet)
        {
            _planet = planet;
            _mat = Prims.VertexColored(0.3f, false, 0.25f);
            Found.Clear();
            SettleHollywood();
        }

        /// <summary>
        /// The Hollywood Sign belongs on a hillside above Los Angeles, read from the city. The rebuilt city is far
        /// larger than the map around it, so the sign's real spot is no use: look along the city's north-west side
        /// for the slope that faces downtown, stands highest above it and has nothing in between, and turn the
        /// letters to face the city.
        /// </summary>
        void SettleHollywood()
        {
            int li = Landmarks.All.FindIndex(x => x.Kind == Landmarks.Kind.Hollywood);
            var c = CityReplica.All.Find(x => x.Name == "losangeles");
            if (li < 0 || c == null) return;
            float best = 0f; Vector3 bestDir = Vector3.zero; float bestYaw = 0f; bool found = false;
            // First a proper hillside; failing that, any dry spot in plain sight of the city.
            for (int pass = 0; pass < 2 && !found; pass++)
            for (float ang = -80f; ang <= 20f; ang += 4f)
            {
                float sn = Mathf.Sin(ang * Mathf.Deg2Rad), cs = Mathf.Cos(ang * Mathf.Deg2Rad);
                // Where this bearing leaves the city, and the eye of someone standing there.
                float edge = Mathf.Min(c.HalfW / Mathf.Max(0.001f, Mathf.Abs(sn)), c.HalfD / Mathf.Max(0.001f, Mathf.Abs(cs)));
                Vector3 At(float r) => (c.Dir * Planet.Radius + c.East * (sn * r) + c.North * (cs * r)).normalized;
                float eye = c.Base + 4f;
                for (float r = edge + 45f; r <= edge + 520f; r += 12f)
                {
                    var d = At(r);
                    float h = _planet.Height(d);
                    if (Planet.WaterLevel(d) > h) continue;
                    if (pass == 0 && (h < c.Base + 8f || _planet.Height(At(r - 14f)) > h - 2f)) continue; // a slope that falls towards the city
                    if (Roads.Near(d, 20f) || Hubs.Near(d, 40f)) continue;
                    bool seen = true;
                    for (int k = 1; k < 16 && seen; k++)
                    {
                        float t = k / 16f, rr = Mathf.Lerp(edge * 0.6f, r, t);
                        if (rr > edge && _planet.Height(At(rr)) > Mathf.Lerp(eye, h + 5f, t) - 0.5f) seen = false;
                    }
                    if (!seen) continue;
                    float score = (h - c.Base) / (r - edge * 0.6f) * 100f - Mathf.Abs(ang + 35f) * 0.03f - (r - edge) * 0.004f;
                    if (found && score <= best) continue;
                    best = score; bestDir = d; found = true;
                    // Letters read from the side their back is turned away from: turn that side to the city.
                    Planet.TangentAt(d, out var e, out var n);
                    var away = d * Planet.Radius - c.Dir * Planet.Radius;
                    bestYaw = Mathf.Atan2(Vector3.Dot(away, e), Vector3.Dot(away, n)) * Mathf.Rad2Deg;
                }
            }
            if (!found) return;
            var l = Landmarks.All[li];
            l.Dir = bestDir; l.Yaw = bestYaw;
            EarthData.LatLon(bestDir, out float la, out float lo);
            l.Lat = la; l.Lon = lo;
            Landmarks.All[li] = l;
        }

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.5f;
            var pd = _planet.WorldToDir(_planet.Player.position);
            bool builtOne = false;
            for (int i = 0; i < Landmarks.All.Count; i++)
            {
                var l = Landmarks.All[i];
                float d = (l.Dir - pd).magnitude * Planet.Radius;
                _live.TryGetValue(i, out var live);
                if (live == null && d < BuildRange && !builtOne) { _live[i] = live = Build(i); builtOne = true; }
                else if (live != null && d > DropRange)
                {
                    if (live.Go != null) Destroy(live.Go);
                    if (live.Mesh != null) Destroy(live.Mesh);
                    _live.Remove(i);
                    continue;
                }
                if (live == null) continue;
                bool near = d < ColliderRange;
                if (near && live.Col == null && live.Mesh != null) { live.Col = live.Go.AddComponent<MeshCollider>(); live.Col.sharedMesh = live.Mesh; }
                else if (!near && live.Col != null) { Destroy(live.Col); live.Col = null; }
                if (d < FoundRange && Found.Add(i))
                {
                    _banner = "You found the " + l.Name + "!";
                    if (l.Name.StartsWith("Moai") || l.Name.StartsWith("Stonehenge") || l.Name.StartsWith("Machu") || l.Name.StartsWith("Pyramids")
                        || l.Name.StartsWith("Angkor") || l.Name.StartsWith("Chich") || l.Name.StartsWith("Big Ben") || l.Name.StartsWith("St "))
                        _banner = "You found " + l.Name + "!";
                    _bannerSub = $"{l.Where}   ·   {Found.Count} of {Landmarks.All.Count} wonders found";
                    _bannerUntil = Time.unscaledTime + 6f;
                }
            }
        }

        void OnGUI()
        {
            if (_banner == null || Time.unscaledTime > _bannerUntil) return;
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _big.normal.textColor = new Color(1f, 0.92f, 0.5f);
                _small = new GUIStyle(_big) { fontSize = 16, fontStyle = FontStyle.Normal };
                _small.normal.textColor = Color.white;
            }
            float a = Mathf.Clamp01((_bannerUntil - Time.unscaledTime) / 1f);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            var r = new Rect(0f, Screen.height * 0.2f, Screen.width, 44f);
            GUI.Box(new Rect(Screen.width / 2f - 300f, r.y - 8f, 600f, 86f), GUIContent.none);
            GUI.Label(r, _banner, _big);
            GUI.Label(new Rect(0f, r.yMax, Screen.width, 26f), _bannerSub, _small);
            GUI.color = old;
        }

        Live Build(int i)
        {
            var l = Landmarks.All[i];
            float h = _planet.Height(l.Dir);
            // Stand on the ground, or on a little island if the spot is just offshore at this map resolution.
            float baseH = Mathf.Max(h, 0.8f);
            var spot = new SurfaceSpot(_planet, l.Dir, l.Yaw, baseH);
            var b = new LB(new MeshBuilder(), spot);
            // Our own modelled wonder if there is one (tools/art/wonders.py: Resources/Kit/own-wonder-<kind>), built in
            // this same frame; otherwise the older shapes drawn in code below.
            var own = l.Kind == Landmarks.Kind.GreatWall || l.Kind == Landmarks.Kind.Hollywood ? null : Kit.Get("wonder-" + l.Kind.ToString().ToLowerInvariant());
            if (own != null) b.Mb.Model(Matrix4x4.identity, own);
            else switch (l.Kind)
            {
                case Landmarks.Kind.Eiffel: b.Plinth(26f, 26f, LB.Paving); b.Lattice(new Color(0.55f, 0.42f, 0.3f), new Color(0.48f, 0.36f, 0.26f), 1f); break;
                case Landmarks.Kind.TokyoTower: b.Plinth(24f, 24f, LB.Paving); b.Lattice(new Color(0.92f, 0.3f, 0.15f), new Color(0.96f, 0.96f, 0.94f), 0.85f); break;
                case Landmarks.Kind.Liberty: b.Liberty(); break;
                case Landmarks.Kind.Pyramids: b.Pyramids(); break;
                case Landmarks.Kind.TajMahal: b.Taj(); break;
                case Landmarks.Kind.Colosseum: b.Colosseum(); break;
                case Landmarks.Kind.BigBen: b.BigBen(); break;
                case Landmarks.Kind.OperaHouse: b.Opera(); break;
                case Landmarks.Kind.Redeemer: b.Redeemer(); break;
                case Landmarks.Kind.GoldenGate: b.GoldenGate(); break;
                case Landmarks.Kind.Burj: b.Burj(); break;
                case Landmarks.Kind.Pisa: b.Pisa(); break;
                case Landmarks.Kind.GreatWall: b.GreatWall(); break;
                case Landmarks.Kind.MachuPicchu: b.MachuPicchu(); break;
                case Landmarks.Kind.Stonehenge: b.Stonehenge(); break;
                case Landmarks.Kind.Moai: b.Moai(); break;
                case Landmarks.Kind.SpaceNeedle: b.SpaceNeedle(); break;
                case Landmarks.Kind.CNTower: b.CNTower(); break;
                case Landmarks.Kind.StBasil: b.StBasil(); break;
                case Landmarks.Kind.Sagrada: b.Sagrada(); break;
                case Landmarks.Kind.AngkorWat: b.Angkor(); break;
                case Landmarks.Kind.ChichenItza: b.Chichen(); break;
                case Landmarks.Kind.Parthenon: b.Parthenon(); break;
                case Landmarks.Kind.Brandenburg: b.Brandenburg(); break;
                case Landmarks.Kind.Hollywood: b.Hollywood(); break;
            }
            var mesh = b.Mb.ToMesh(l.Name);
            var live = new Live { Mesh = mesh };
            if (mesh != null) live.Go = spot.Make("Landmark_" + l.Name, mesh, _mat, true);
            return live;
        }

        void OnDestroy()
        {
            foreach (var l in _live.Values) if (l.Mesh != null) Destroy(l.Mesh);
            _live.Clear();
        }

        /// <summary>Landmark builder: shapes in the landmark's local frame (x east, y up, z north, metres).</summary>
        sealed class LB
        {
            public readonly MeshBuilder Mb;
            readonly SurfaceSpot _spot;
            public Matrix4x4 F = Matrix4x4.identity; // extra transform (e.g. Pisa's lean)

            public static readonly Color Paving = new Color(0.78f, 0.76f, 0.72f);
            static readonly Color Stone = new Color(0.82f, 0.74f, 0.56f), Marble = new Color(0.95f, 0.93f, 0.88f);
            static readonly Color Gold = new Color(1f, 0.8f, 0.25f), Lawn = new Color(0.4f, 0.7f, 0.28f), Pool = new Color(0.2f, 0.6f, 0.8f);
            static readonly Color DarkStone = new Color(0.45f, 0.42f, 0.4f), Concrete = new Color(0.82f, 0.8f, 0.76f);

            public LB(MeshBuilder mb, SurfaceSpot spot) { Mb = mb; _spot = spot; }

            // ---- primitives (y0 = bottom)
            void Box(float x, float y0, float z, float w, float h, float d, Color c) => Box(x, y0, z, w, h, d, c, c);
            void Box(float x, float y0, float z, float w, float h, float d, Color c, Color top)
                => Mb.Box(F * Matrix4x4.TRS(new Vector3(x, y0 + h / 2f, z), Quaternion.identity, new Vector3(w, h, d)), c, top);
            void BoxR(Vector3 centre, Quaternion rot, Vector3 size, Color c) => Mb.Box(F * Matrix4x4.TRS(centre, rot, size), c, c);
            void Cyl(float x, float y0, float z, float diam, float h, int sides, Color c) => Cyl(x, y0, z, diam, h, sides, c, c);
            void Cyl(float x, float y0, float z, float diam, float h, int sides, Color c, Color cap)
                => Mb.Cylinder(F * Matrix4x4.TRS(new Vector3(x, y0, z), Quaternion.identity, new Vector3(diam, h, diam)), sides, c, cap);
            void Cone(float x, float y0, float z, float diam, float h, int sides, Color c)
                => Mb.Cone(F * Matrix4x4.TRS(new Vector3(x, y0, z), Quaternion.identity, new Vector3(diam, h, diam)), sides, c);
            void Blob(float x, float y, float z, float w, float h, Color c, int seg = 8)
                => Mb.Blob(F * Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, new Vector3(w, h, w)), c, seg);
            void Beam(Vector3 a, Vector3 b, float w, Color c)
            {
                var d = b - a;
                float len = d.magnitude;
                if (len < 1e-4f) return;
                Mb.Box(F * Matrix4x4.TRS((a + b) / 2f, Quaternion.FromToRotation(Vector3.up, d / len), new Vector3(w, len, w)), c, c);
            }
            static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

            /// <summary>A plaza or platform that reaches well down into the ground, so it never floats on a slope.</summary>
            public void Plinth(float w, float d, Color c, float top = 0.3f) => Box(0f, -30f, 0f, w, 30f + top, d, DarkStone, c);

            // ---- landmarks

            /// <summary>Eiffel-style lattice tower (also Tokyo Tower, with its colours).</summary>
            public void Lattice(Color a, Color b, float k)
            {
                float[] y = { 0f, 22f * k, 42f * k, 72f * k };
                float[] r = { 13f, 7f, 4f, 0.6f };
                for (int s = 0; s < 3; s++)
                {
                    var c = (s % 2 == 0) ? a : b;
                    for (int q = 0; q < 4; q++)
                    {
                        float sx = q == 0 || q == 3 ? 1f : -1f, sz = q < 2 ? 1f : -1f;
                        float w = s == 2 ? 1.1f : 2.2f;
                        Beam(V(sx * r[s], y[s], sz * r[s]), V(sx * r[s + 1], y[s + 1], sz * r[s + 1]), w, c);
                    }
                    // Cross-bracing on each face.
                    for (int f = 0; f < 4; f++)
                    {
                        Vector3 P(float rr, float yy, int corner)
                        {
                            float sx = corner == 0 || corner == 3 ? 1f : -1f, sz = corner < 2 ? 1f : -1f;
                            return V(sx * rr, yy, sz * rr);
                        }
                        int c0 = f, c1 = (f + 1) % 4;
                        Beam(P(r[s], y[s], c0), P(r[s + 1], y[s + 1], c1), 0.5f, c);
                        Beam(P(r[s], y[s], c1), P(r[s + 1], y[s + 1], c0), 0.5f, c);
                    }
                    if (s < 2) Box(0f, y[s + 1] - 1f, 0f, r[s + 1] * 2.4f, 2f, r[s + 1] * 2.4f, b, a);
                }
                // The arches between the legs at ground level.
                for (int f = 0; f < 4; f++)
                {
                    var rot = Quaternion.Euler(0f, f * 90f, 0f);
                    var p0 = rot * V(-9f, 0f, 11f); var p1 = rot * V(0f, 12f * k, 9f); var p2 = rot * V(9f, 0f, 11f);
                    Beam(p0 + Vector3.up * 6f * k, p1, 1.2f, a);
                    Beam(p1, p2 + Vector3.up * 6f * k, 1.2f, a);
                }
                Box(0f, y[3], 0f, 2.2f, 3f, 2.2f, b);
                Cyl(0f, y[3] + 3f, 0f, 0.4f, 8f * k, 4, Gold);
            }

            public void Liberty()
            {
                var green = new Color(0.45f, 0.75f, 0.64f);
                Plinth(40f, 40f, Lawn);
                // A star-shaped fort, then the pedestal.
                for (int s = 0; s < 11; s++)
                {
                    var rot = Quaternion.Euler(0f, s * 360f / 11f, 0f);
                    BoxR(rot * V(0f, 1.5f, 13f), rot, V(7f, 3f, 7f), Stone);
                }
                Box(0f, 0f, 0f, 16f, 3f, 16f, Stone);
                Box(0f, 3f, 0f, 11f, 10f, 11f, Stone, Stone * 0.95f);
                Box(0f, 13f, 0f, 9f, 3f, 9f, Stone);
                // The figure: a robe, a head with the crown, the torch raised high and the tablet.
                Cyl(0f, 16f, 0f, 6.5f, 4f, 10, green);
                Cone(0f, 16f, 0f, 6f, 22f, 10, green);
                Cyl(0f, 18f, 0f, 4.4f, 11f, 10, green);
                Blob(0f, 31.5f, 0f, 3f, 3.6f, green);
                for (int s = 0; s < 7; s++)
                {
                    var rot = Quaternion.Euler(0f, -60f + s * 20f, 0f);
                    Beam(V(0f, 32.5f, 0f) + rot * V(0f, 0f, -1.2f), V(0f, 32.5f, 0f) + rot * V(0f, 1.2f, -3.4f), 0.35f, green);
                }
                Beam(V(1.8f, 26f, 0f), V(3.6f, 38f, 0f), 1.1f, green);    // raised right arm
                Cyl(3.6f, 38f, 0f, 1.2f, 1.6f, 6, green);
                Blob(3.6f, 40.4f, 0f, 1.6f, 2.4f, Gold);                  // the flame
                BoxR(V(-2.6f, 25f, -0.6f), Quaternion.Euler(0f, 0f, 10f), V(1f, 4f, 2.6f), green); // tablet
            }

            public void Pyramids()
            {
                var sand = new Color(0.92f, 0.8f, 0.52f);
                void Pyr(float x, float z, float side, float h) =>
                    Mb.Cone(F * Matrix4x4.TRS(V(x, -2f, z), Quaternion.Euler(0f, 45f, 0f), V(side * 1.414f, h + 2f, side * 1.414f)), 4, sand);
                Pyr(0f, 0f, 64f, 42f);
                Pyr(-60f, -55f, 58f, 40f);
                Pyr(-105f, -105f, 30f, 20f);
                // The Sphinx, guarding the way in.
                Box(55f, -1f, 30f, 6f, 6f, 20f, sand * 0.95f);
                Box(55f, 4f, 37f, 5f, 7f, 5f, sand);
                Box(55f, 9f, 38.5f, 6.5f, 2f, 3f, sand * 0.9f);
                Box(55f, 0f, 43f, 5f, 3f, 6f, sand * 0.92f);
            }

            public void Taj()
            {
                Plinth(70f, 90f, Lawn);
                Box(0f, 0.3f, -32f, 6f, 0.4f, 44f, Pool);                 // the long reflecting pool
                Box(0f, 0f, 14f, 44f, 4f, 44f, Marble);
                Box(0f, 4f, 14f, 24f, 16f, 24f, Marble);
                Box(0f, 15f, 2f, 10f, 9f, 0.6f, Marble * 0.9f);           // the great arch
                Cyl(0f, 20f, 14f, 14f, 4f, 12, Marble);
                Blob(0f, 30f, 14f, 17f, 18f, Marble, 12);
                Cone(0f, 37f, 14f, 3f, 6f, 8, Marble);
                Cyl(0f, 42f, 14f, 0.3f, 3f, 4, Gold);
                foreach (var (x, z) in new[] { (-8f, 6f), (8f, 6f), (-8f, 22f), (8f, 22f) })
                {
                    Cyl(x, 20f, z, 4f, 3f, 8, Marble);
                    Blob(x, 24f, z, 5f, 5f, Marble);
                }
                foreach (var (x, z) in new[] { (-20f, -6f), (20f, -6f), (-20f, 34f), (20f, 34f) })
                {
                    Cyl(x, 4f, z, 2.6f, 28f, 8, Marble);
                    Cyl(x, 32f, z, 3.4f, 2f, 8, Marble);
                    Blob(x, 35f, z, 3f, 3f, Marble);
                }
            }

            public void Colosseum()
            {
                Plinth(70f, 60f, Paving);
                Cyl(0f, 0f, 0f, 36f, 1f, 16, new Color(0.88f, 0.78f, 0.56f));   // the arena floor
                var travertine = new Color(0.86f, 0.76f, 0.58f);
                var arch = new Color(0.4f, 0.34f, 0.28f);
                int n = 36;
                for (int s = 0; s < n; s++)
                {
                    float a = s * Mathf.PI * 2f / n;
                    var p = V(Mathf.Cos(a) * 30f, 0f, Mathf.Sin(a) * 24f);
                    var rot = Quaternion.LookRotation(V(Mathf.Cos(a) * 24f, 0f, Mathf.Sin(a) * 30f).normalized);
                    bool ruined = s > 4 && s < 14;
                    float h = ruined ? 11f - (s % 3) * 2f : 19f;
                    BoxR(p + Vector3.up * h / 2f, rot, V(5.4f, h, 4f), travertine);
                    // Arches on the outside: dark openings in three tiers.
                    for (int t = 0; t < 3; t++)
                    {
                        float y = 2.4f + t * 5.5f;
                        if (y + 3f > h) break;
                        BoxR(p + rot * V(0f, 0f, 2.05f) + Vector3.up * y, rot, V(2.6f, 3.6f, 0.15f), arch);
                    }
                    // Seating stepping down to the arena.
                    BoxR(p * 0.82f + Vector3.up * 5f, rot, V(4.4f, 10f, 4f), travertine * 0.9f);
                    BoxR(p * 0.68f + Vector3.up * 2.5f, rot, V(3.6f, 5f, 4f), travertine * 0.85f);
                }
            }

            public void BigBen()
            {
                var sandst = new Color(0.86f, 0.74f, 0.5f);
                Plinth(90f, 30f, Paving);
                Box(0f, 0f, 0f, 8f, 40f, 8f, sandst);
                foreach (var rot in new[] { 0f, 90f, 180f, 270f })
                {
                    var q = Quaternion.Euler(0f, rot, 0f);
                    BoxR(V(0f, 33f, 0f) + q * V(0f, 0f, -4.1f), q, V(6f, 6f, 0.3f), new Color(0.97f, 0.95f, 0.85f));
                    BoxR(V(0f, 33f, 0f) + q * V(0f, 0f, -4.3f), q, V(1.2f, 1.2f, 0.2f), Gold);
                }
                Box(0f, 40f, 0f, 9f, 2f, 9f, sandst);
                Mb.Cone(F * Matrix4x4.TRS(V(0f, 42f, 0f), Quaternion.Euler(0f, 45f, 0f), V(11f, 12f, 11f)), 4, new Color(0.3f, 0.36f, 0.4f));
                Cyl(0f, 54f, 0f, 0.4f, 4f, 4, Gold);
                // The Houses of Parliament alongside, with little turrets.
                Box(42f, 0f, 0f, 70f, 16f, 16f, sandst);
                Mb.Roof(F * Matrix4x4.TRS(V(42f, 16f, 0f), Quaternion.identity, V(70f, 5f, 16f)), new Color(0.3f, 0.36f, 0.4f));
                for (int t = 0; t < 7; t++) { Box(12f + t * 10f, 16f, 8f, 2f, 6f, 2f, sandst); Cone(12f + t * 10f, 22f, 8f, 2.4f, 3f, 4, new Color(0.3f, 0.36f, 0.4f)); }
                Box(78f, 0f, 0f, 12f, 34f, 12f, sandst);
            }

            public void Opera()
            {
                var white = new Color(0.98f, 0.97f, 0.94f);
                Plinth(46f, 80f, new Color(0.82f, 0.62f, 0.5f), 4f);
                void Sail(float x, float z, float size, float tilt)
                {
                    // A triangular shell leaning forward (local -Z towards the harbour).
                    var m = F * Matrix4x4.TRS(V(x, 4f, z), Quaternion.Euler(tilt, 0f, 0f), V(size, size * 1.2f, size));
                    Vector3 a = V(-0.5f, 0f, 0f), b = V(0.5f, 0f, 0f), top = V(0f, 1f, -0.25f), back = V(0f, 0f, 0.55f);
                    Mb.Tri(m, a, top, b, white);
                    Mb.Tri(m, b, top, back, white * 0.94f);
                    Mb.Tri(m, back, top, a, white * 0.94f);
                }
                foreach (float x in new[] { -10f, 11f })
                {
                    Sail(x, -22f, 20f, -12f);
                    Sail(x, -6f, 17f, -10f);
                    Sail(x, 8f, 13f, -8f);
                    Sail(x, 20f, 9f, -6f);
                }
                Sail(0f, 33f, 9f, -6f);
            }

            public void Redeemer()
            {
                var soap = new Color(0.93f, 0.92f, 0.87f);
                Plinth(20f, 20f, Paving);
                Box(0f, 0f, 0f, 7f, 8f, 7f, soap * 0.95f);
                Cone(0f, 8f, 0f, 6f, 26f, 8, soap);
                Cyl(0f, 8f, 0f, 3.6f, 20f, 8, soap);
                Box(0f, 25f, 0f, 30f, 2.6f, 2.4f, soap);                  // arms wide open
                Box(0f, 21f, 0f, 5f, 6f, 2.6f, soap);
                Blob(0f, 30f, 0f, 3f, 3.6f, soap);
            }

            public void GoldenGate()
            {
                var orange = new Color(0.86f, 0.3f, 0.17f);
                const float half = 140f, tower = 55f, deckY = 16f, span = 70f;
                // Deck along local z, with towers at ±span.
                Box(0f, deckY, 0f, 12f, 1.6f, half * 2f, orange, new Color(0.35f, 0.35f, 0.38f));
                Box(5.6f, deckY + 1.6f, 0f, 0.3f, 1f, half * 2f, orange);
                Box(-5.6f, deckY + 1.6f, 0f, 0.3f, 1f, half * 2f, orange);
                foreach (float tz in new[] { -span, span })
                {
                    Box(0f, -30f, tz, 16f, 30f + 3f, 8f, Concrete);
                    foreach (float tx in new[] { -6.5f, 6.5f }) Box(tx, 3f, tz, 3f, tower - 3f, 3f, orange);
                    foreach (float y in new[] { deckY + 12f, deckY + 25f, tower - 3f }) Box(0f, y, tz, 13f, 2.2f, 2.4f, orange);
                }
                // Approach piers at the deck ends: two slim steel legs and a cross beam (a solid block read as a crate).
                foreach (float ez in new[] { -half, half })
                {
                    foreach (float tx in new[] { -5f, 5f }) Box(tx, -30f, ez, 2.2f, 30f + deckY, 2.2f, orange);
                    Box(0f, deckY - 2.5f, ez, 12f, 1.6f, 2f, orange);
                }
                // Main cables: a sag between the towers, and down to the deck ends; suspenders hanging from them.
                foreach (float cx in new[] { -6.5f, 6.5f })
                {
                    Vector3 prev = default;
                    for (int s = 0; s <= 28; s++)
                    {
                        float z = -half + s * (half * 2f / 28f), y;
                        if (Mathf.Abs(z) <= span) { float t = z / span; y = deckY + 3f + (tower - deckY - 3f) * t * t; }
                        else { float t = (Mathf.Abs(z) - span) / (half - span); y = Mathf.Lerp(tower, deckY + 2f, t); }
                        var p = V(cx, y, z);
                        if (s > 0) Beam(prev, p, 0.7f, orange);
                        if (y > deckY + 3f) Beam(V(cx, deckY + 1.6f, z), p, 0.18f, orange);
                        prev = p;
                    }
                }
            }

            public void Burj()
            {
                var steel = new Color(0.78f, 0.84f, 0.9f);
                Plinth(50f, 50f, Paving);
                float[] top = { 45f, 80f, 108f, 128f, 142f };
                float[] wing = { 14f, 11f, 8f, 5.5f, 3.5f };
                float y0 = 0f;
                for (int t = 0; t < top.Length; t++)
                {
                    Cyl(0f, y0, 0f, wing[t] * 0.9f, top[t] - y0, 6, steel, steel * 0.9f);
                    for (int w = 0; w < 3; w++)
                    {
                        var q = Quaternion.Euler(0f, w * 120f, 0f);
                        float h = top[t] - y0 - (w + t) % 3 * 4f;
                        BoxR(q * V(0f, 0f, wing[t] * 0.55f) + Vector3.up * (y0 + h / 2f), q, V(wing[t] * 0.5f, h, wing[t] * 0.9f), steel * (1f - w * 0.04f));
                    }
                    y0 = top[t];
                }
                Cone(0f, y0, 0f, 2.6f, 30f, 6, steel);
            }

            public void Pisa()
            {
                Plinth(70f, 40f, Lawn);
                // The cathedral beside it.
                Box(-28f, 0f, 0f, 34f, 14f, 14f, Marble);
                Mb.Roof(F * Matrix4x4.TRS(V(-28f, 14f, 0f), Quaternion.identity, V(34f, 5f, 14f)), new Color(0.75f, 0.35f, 0.25f));
                Cyl(-28f, 14f, 0f, 8f, 3f, 10, Marble);
                Blob(-28f, 18f, 0f, 8f, 6f, new Color(0.75f, 0.35f, 0.25f));
                // The tower, leaning 4°.
                var keep = F;
                F = F * Matrix4x4.TRS(V(10f, 0f, 0f), Quaternion.Euler(0f, 0f, -4f), Vector3.one);
                for (int t = 0; t < 7; t++)
                {
                    Cyl(0f, t * 4.2f, 0f, 8f, 4.2f, 14, Marble, Marble * 0.95f);
                    Cyl(0f, t * 4.2f + 3.8f, 0f, 9f, 0.5f, 14, Marble * 0.9f);
                }
                Cyl(0f, 29.4f, 0f, 6f, 4f, 12, Marble);
                Cyl(0f, 33.4f, 0f, 6.6f, 0.4f, 12, Marble * 0.9f);
                F = keep;
            }

            public void GreatWall()
            {
                var stone = new Color(0.72f, 0.66f, 0.55f);
                // A 480 m stretch that snakes over the real ground with watchtowers.
                Vector3 prev = default;
                int segs = 60;
                for (int s = 0; s <= segs; s++)
                {
                    float x = -240f + s * 8f, z = Mathf.Sin(x / 45f) * 28f + Mathf.Sin(x / 17f) * 6f;
                    var g = _spot.Ground(x, z, out _);
                    var p = g + Vector3.up * 4.5f;
                    if (s > 0)
                    {
                        var d = p - prev;
                        var rot = Quaternion.LookRotation(V(d.x, 0f, d.z).normalized);
                        var mid = (p + prev) / 2f;
                        float len = new Vector2(d.x, d.z).magnitude + 0.6f;
                        BoxR(mid - Vector3.up * 3.5f, rot, V(4.5f, 9f, len), stone);
                        for (int c = 0; c < 3; c++)
                        {
                            var off = rot * V(0f, 0f, (c - 1) * len / 3f);
                            BoxR(mid + off + rot * V(2f, 1.4f, 0f), rot, V(0.5f, 1.4f, 1.2f), stone * 0.95f);
                            BoxR(mid + off + rot * V(-2f, 1.4f, 0f), rot, V(0.5f, 1.4f, 1.2f), stone * 0.95f);
                        }
                    }
                    if (s % 8 == 0)
                    {
                        Box(g.x, g.y - 3f, g.z, 9f, 14f, 9f, stone * 0.92f, stone);
                        for (int c = 0; c < 4; c++)
                        {
                            var q = Quaternion.Euler(0f, c * 90f, 0f);
                            BoxR(V(g.x, g.y + 11.7f, g.z) + q * V(0f, 0f, 4.3f), q, V(9f, 1.4f, 0.5f), stone * 0.95f);
                        }
                    }
                    prev = p;
                }
            }

            public void MachuPicchu()
            {
                Color rock = new Color(0.62f, 0.6f, 0.56f), grass = new Color(0.42f, 0.72f, 0.3f), thatch = new Color(0.72f, 0.6f, 0.32f);
                // Terraces stepping down the mountainside.
                for (int t = 0; t < 7; t++) Box(0f, -30f, -10f - t * 7f, 80f - t * 4f, 30f - t * 2.5f, 7.5f, rock, grass);
                Box(0f, -30f, 20f, 80f, 30.3f, 50f, rock, grass);
                // Stone houses with thatched roofs on the top terrace.
                var rng = new System.Random(5);
                for (int hx = 0; hx < 5; hx++)
                for (int hz = 0; hz < 3; hz++)
                {
                    float x = -28f + hx * 14f + (float)rng.NextDouble() * 3f, z = 6f + hz * 13f;
                    Box(x, 0f, z, 7f, 3.2f, 5f, rock);
                    Mb.Roof(F * Matrix4x4.TRS(V(x, 3.2f, z), Quaternion.identity, V(7.6f, 3.4f, 5.6f)), thatch);
                }
                // Huayna Picchu, the steep green peak behind.
                Cone(0f, -10f, 85f, 60f, 75f, 9, new Color(0.3f, 0.55f, 0.25f));
                Cone(0f, 50f, 85f, 16f, 18f, 7, rock);
            }

            public void Stonehenge()
            {
                var sarsen = new Color(0.62f, 0.62f, 0.58f);
                for (int s = 0; s < 30; s++)
                {
                    float a = s * Mathf.PI * 2f / 30f;
                    var q = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                    var p = V(Mathf.Cos(a) * 16f, 0f, Mathf.Sin(a) * 16f);
                    if (s == 7 || s == 19) { BoxR(p + Vector3.up * 0.4f, q * Quaternion.Euler(85f, 0f, 0f), V(1f, 4.6f, 1.6f), sarsen); continue; } // fallen
                    BoxR(p + Vector3.up * 1.8f, q, V(1f, 5.6f, 1.6f), sarsen);
                    if (s % 2 == 0 && s != 18)
                    {
                        float a2 = (s + 1) * Mathf.PI * 2f / 30f, am = (s + 0.5f) * Mathf.PI * 2f / 30f;
                        BoxR(V(Mathf.Cos(am) * 16f, 5.1f, Mathf.Sin(am) * 16f), Quaternion.Euler(0f, -am * Mathf.Rad2Deg + 90f, 0f), V(4.2f, 1f, 1.1f), sarsen * 0.95f);
                    }
                }
                // The horseshoe of great trilithons.
                for (int t = 0; t < 5; t++)
                {
                    float a = Mathf.PI * (0.2f + t * 0.4f) + Mathf.PI * 0.5f;
                    var c = V(Mathf.Cos(a) * 8f, 0f, Mathf.Sin(a) * 8f);
                    var q = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                    float h = t == 2 ? 8f : 6.5f;
                    BoxR(c + q * V(-1.3f, h / 2f - 1f, 0f), q, V(1.8f, h, 1.2f), sarsen);
                    BoxR(c + q * V(1.3f, h / 2f - 1f, 0f), q, V(1.8f, h, 1.2f), sarsen);
                    BoxR(c + V(0f, h - 0.5f, 0f), q, V(4.8f, 1.1f, 1.3f), sarsen * 0.95f);
                }
            }

            public void Moai()
            {
                var tuff = new Color(0.45f, 0.4f, 0.36f);
                Plinth(40f, 14f, new Color(0.4f, 0.65f, 0.3f), 0.3f);
                Box(0f, 0f, 0f, 34f, 1.6f, 6f, DarkStone);
                for (int s = 0; s < 7; s++)
                {
                    float x = -15f + s * 5f, sc = 0.9f + (s % 3) * 0.1f;
                    Box(x, 1.6f, 0f, 2.6f * sc, 3.6f * sc, 1.9f * sc, tuff);                 // torso
                    Box(x, 1.6f + 3.6f * sc, 0f, 2.3f * sc, 4.4f * sc, 2.2f * sc, tuff);      // the long head
                    Box(x, 1.6f + 6.2f * sc, -1.2f * sc, 2.4f * sc, 0.6f * sc, 0.6f * sc, tuff * 0.9f); // brow
                    Box(x, 1.6f + 4.7f * sc, -1.3f * sc, 0.6f * sc, 1.7f * sc, 0.7f * sc, tuff * 0.95f); // nose
                    if (s == 3) Cyl(x, 1.6f + 8f * sc, 0f, 2.2f, 1.2f, 8, new Color(0.62f, 0.3f, 0.2f)); // red topknot
                }
            }

            public void SpaceNeedle()
            {
                Color white = new Color(0.94f, 0.94f, 0.92f), orange = new Color(0.95f, 0.55f, 0.2f);
                Plinth(30f, 30f, Paving);
                for (int s = 0; s < 3; s++)
                {
                    var q = Quaternion.Euler(0f, s * 120f, 0f);
                    Vector3 b0 = q * V(-2.2f, 0f, 9f), b1 = q * V(2.2f, 0f, 9f), waist = q * V(0f, 26f, 1.6f), top = q * V(0f, 46f, 3.4f);
                    Beam(b0, waist, 1f, white); Beam(b1, waist, 1f, white); Beam(waist, top, 1.1f, white);
                }
                Cyl(0f, 0f, 0f, 2.6f, 50f, 6, white);
                Mb.Cone(F * Matrix4x4.TRS(V(0f, 48f, 0f), Quaternion.Euler(180f, 0f, 0f), V(20f, 4f, 20f)), 14, white);
                Cyl(0f, 48f, 0f, 22f, 2.6f, 14, orange, orange);
                Cone(0f, 50.6f, 0f, 20f, 3.4f, 14, white);
                Cyl(0f, 54f, 0f, 0.6f, 12f, 4, white);
            }

            public void CNTower()
            {
                Plinth(30f, 30f, Paving);
                for (int w = 0; w < 3; w++)
                {
                    var q = Quaternion.Euler(0f, w * 120f, 0f);
                    BoxR(q * V(0f, 0f, 2.6f) + Vector3.up * 40f, q, V(3f, 80f, 5f), Concrete);
                }
                Cyl(0f, 0f, 0f, 5f, 112f, 6, Concrete);
                Cyl(0f, 84f, 0f, 15f, 3f, 12, Concrete);
                Blob(0f, 89f, 0f, 16f, 8f, new Color(0.35f, 0.42f, 0.5f), 12);
                Cyl(0f, 93f, 0f, 13f, 2f, 12, Concrete);
                Cyl(0f, 108f, 0f, 6f, 3f, 8, Concrete);
                Cyl(0f, 112f, 0f, 1.4f, 34f, 6, new Color(0.85f, 0.2f, 0.18f), Concrete);
            }

            public void StBasil()
            {
                Color brick = new Color(0.72f, 0.26f, 0.2f), cream = new Color(0.96f, 0.9f, 0.76f);
                Plinth(40f, 40f, Paving);
                Box(0f, 0f, 0f, 26f, 8f, 26f, brick, cream);
                Cyl(0f, 8f, 0f, 9f, 16f, 8, brick, cream);
                Cone(0f, 24f, 0f, 8f, 16f, 8, new Color(0.3f, 0.6f, 0.35f));
                Blob(0f, 41f, 0f, 2.2f, 2.8f, Gold);
                Color[] domes = { new Color(0.2f, 0.62f, 0.38f), new Color(0.9f, 0.75f, 0.2f), new Color(0.2f, 0.5f, 0.82f), new Color(0.9f, 0.32f, 0.25f),
                                  new Color(0.25f, 0.7f, 0.65f), new Color(0.95f, 0.6f, 0.15f), new Color(0.55f, 0.35f, 0.75f), new Color(0.85f, 0.2f, 0.35f) };
                for (int d = 0; d < 8; d++)
                {
                    float a = d * Mathf.PI / 4f, r = d % 2 == 0 ? 10f : 8f, hgt = d % 2 == 0 ? 16f : 11f, w = d % 2 == 0 ? 5f : 3.6f;
                    float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                    Cyl(x, 8f, z, w, hgt - 8f + 8f, 8, cream, cream);
                    Blob(x, 8f + hgt + w * 0.4f, z, w * 1.25f, w * 1.3f, domes[d]);
                    Cone(x, 8f + hgt + w * 0.9f, z, w * 0.5f, w * 0.9f, 6, domes[d]);
                    Cyl(x, 8f + hgt + w * 1.7f, z, 0.25f, 1.5f, 4, Gold);
                }
            }

            public void Sagrada()
            {
                var stone = new Color(0.8f, 0.7f, 0.52f);
                Plinth(70f, 50f, Paving);
                Box(0f, 0f, 0f, 50f, 24f, 26f, stone);
                Mb.Roof(F * Matrix4x4.TRS(V(0f, 24f, 0f), Quaternion.identity, V(50f, 8f, 26f)), stone * 0.9f);
                Color[] fruit = { new Color(0.95f, 0.4f, 0.2f), new Color(0.95f, 0.8f, 0.2f), new Color(0.3f, 0.7f, 0.4f), new Color(0.85f, 0.25f, 0.45f) };
                int f = 0;
                foreach (float z in new[] { -15f, 15f })
                for (int s = 0; s < 4; s++)
                {
                    float x = -15f + s * 10f, h = s == 1 || s == 2 ? 58f : 50f;
                    Cyl(x, 0f, z, 4.5f, h * 0.7f, 8, stone, stone);
                    Cone(x, h * 0.7f, z, 4.5f, h * 0.3f, 8, stone * 1.05f);
                    Blob(x, h + 1f, z, 2.4f, 2.4f, fruit[f++ % fruit.Length]);
                }
                Cyl(0f, 0f, 0f, 9f, 60f, 10, stone, stone);
                Cone(0f, 60f, 0f, 9f, 24f, 10, stone * 1.05f);
                Blob(0f, 86f, 0f, 3.4f, 3.4f, new Color(0.95f, 0.95f, 0.98f));
            }

            public void Angkor()
            {
                var sand = new Color(0.62f, 0.58f, 0.48f);
                // The moat around it, then three terraces and five lotus towers.
                foreach (var (x, z, w, d) in new[] { (0f, -62f, 132f, 14f), (0f, 62f, 132f, 14f), (-62f, 0f, 14f, 110f), (62f, 0f, 14f, 110f) })
                    Box(x, -1.5f, z, w, 1.6f, d, Pool);
                Box(0f, -1.5f, -62f, 8f, 1.9f, 14f, sand);                       // causeway over the moat
                Plinth(100f, 100f, Lawn);
                Box(0f, 0f, 0f, 70f, 5f, 70f, sand);
                Box(0f, 5f, 0f, 52f, 5f, 52f, sand * 0.97f);
                Box(0f, 10f, 0f, 36f, 6f, 36f, sand * 0.94f);
                void Lotus(float x, float z, float y0, float h, float w)
                {
                    for (int t = 0; t < 5; t++) Box(x, y0 + t * h * 0.14f, z, w * (1f - t * 0.14f), h * 0.14f, w * (1f - t * 0.14f), sand);
                    Mb.Cone(F * Matrix4x4.TRS(V(x, y0 + h * 0.7f, z), Quaternion.Euler(0f, 45f, 0f), V(w * 0.62f, h * 0.36f, w * 0.62f)), 8, sand * 1.04f);
                }
                Lotus(0f, 0f, 16f, 30f, 12f);
                foreach (var (x, z) in new[] { (-15f, -15f), (15f, -15f), (-15f, 15f), (15f, 15f) }) Lotus(x, z, 10f, 20f, 8f);
            }

            public void Chichen()
            {
                var lime = new Color(0.86f, 0.82f, 0.7f);
                Plinth(70f, 70f, Lawn);
                for (int t = 0; t < 9; t++)
                {
                    float w = 52f - t * 4.2f;
                    Box(0f, t * 2.8f, 0f, w, 2.8f, w, lime * (t % 2 == 0 ? 1f : 0.94f));
                }
                // Staircases up all four sides, and the temple on top.
                for (int s = 0; s < 4; s++)
                {
                    var q = Quaternion.Euler(0f, s * 90f, 0f);
                    for (int step = 0; step < 12; step++)
                        BoxR(q * V(0f, step * 2.1f + 1f, 26f - step * 1.75f - 1f), q, V(8f, 2.1f, 3.6f), lime * 1.03f);
                }
                Box(0f, 25.2f, 0f, 14f, 7f, 14f, lime, lime * 0.9f);
                Box(0f, 25.2f, -7.1f, 4f, 4.5f, 0.3f, new Color(0.25f, 0.2f, 0.16f));
            }

            public void Parthenon()
            {
                Plinth(50f, 30f, new Color(0.76f, 0.7f, 0.56f));
                for (int s = 0; s < 3; s++) Box(0f, s * 0.8f, 0f, 40f - s * 1.4f, 0.8f, 20f - s * 1.4f, Marble);
                float y0 = 2.4f, h = 11f;
                for (int i = 0; i < 14; i++)
                {
                    float x = -17f + i * (34f / 13f);
                    Cyl(x, y0, -7.6f, 1.7f, h, 8, Marble);
                    Cyl(x, y0, 7.6f, 1.7f, h, 8, Marble);
                }
                for (int i = 1; i < 7; i++)
                {
                    float z = -7.6f + i * (15.2f / 7f);
                    Cyl(-17f, y0, z, 1.7f, h, 8, Marble);
                    Cyl(17f, y0, z, 1.7f, h, 8, Marble);
                }
                Box(0f, y0 + 3f, 0f, 26f, 8f, 10f, Marble * 0.85f);              // the inner hall
                Box(0f, y0 + h, 0f, 37f, 2.4f, 18f, Marble);
                Mb.Roof(F * Matrix4x4.TRS(V(0f, y0 + h + 2.4f, 0f), Quaternion.identity, V(37f, 3f, 18f)), Marble * 0.95f);
            }

            public void Brandenburg()
            {
                var sandst = new Color(0.88f, 0.82f, 0.68f);
                Plinth(60f, 30f, Paving);
                for (int i = 0; i < 6; i++)
                {
                    float x = -15f + i * 6f;
                    Cyl(x, 0f, -4f, 1.8f, 14f, 8, sandst);
                    Cyl(x, 0f, 4f, 1.8f, 14f, 8, sandst);
                    if (i == 0 || i == 5) Box(x + (i == 0 ? -2.5f : 2.5f), 0f, 0f, 3f, 14f, 10f, sandst);
                }
                Box(0f, 14f, 0f, 38f, 3f, 11f, sandst);
                Box(0f, 17f, 0f, 16f, 3.6f, 8f, sandst);
                // The Quadriga: a chariot with four horses, in green bronze.
                var bronze = new Color(0.35f, 0.62f, 0.5f);
                Box(0f, 20.6f, 0f, 3f, 2.6f, 3f, bronze);
                for (int hz = 0; hz < 4; hz++)
                {
                    float x = -3f + hz * 2f;
                    Blob(x, 22f, -2.2f, 1.4f, 1.6f, bronze);
                    Box(x, 20.6f, -2.2f, 0.4f, 1.2f, 1.6f, bronze);
                }
                Blob(0f, 24f, 0f, 1.2f, 2.4f, bronze);
            }

            static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
            {
                { 'H', new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" } },
                { 'O', new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" } },
                { 'L', new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" } },
                { 'Y', new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" } },
                { 'W', new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" } },
                { 'D', new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" } },
            };

            public void Hollywood()
            {
                const string word = "HOLLYWOOD";
                const float px = 2.4f, gap = 4.5f;
                float letterW = 5f * px, total = word.Length * letterW + (word.Length - 1) * gap;
                for (int li = 0; li < word.Length; li++)
                {
                    float x0 = -total / 2f + li * (letterW + gap);
                    var g = _spot.Ground(x0 + letterW / 2f, 0f, out _);
                    float yb = g.y + 2f;
                    // Posts down into the hillside.
                    Box(x0 + px, g.y - 6f, 0.8f, 0.5f, 6f + 8f, 0.5f, new Color(0.4f, 0.4f, 0.42f));
                    Box(x0 + letterW - px, g.y - 6f, 0.8f, 0.5f, 6f + 8f, 0.5f, new Color(0.4f, 0.4f, 0.42f));
                    var rows = Font[word[li]];
                    for (int r = 0; r < 7; r++)
                    for (int c = 0; c < 5; c++)
                        if (rows[r][c] == '1') Box(x0 + (c + 0.5f) * px, yb + (6 - r) * px, 0f, px * 1.02f, px * 1.02f, 0.5f, new Color(0.98f, 0.98f, 0.96f));
                }
            }
        }
    }
}
