using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Animals of the region you are in: a few small herds on the ground (or pods in the sea) around the player,
    /// each of a species that lives there (elephants and giraffes on the African savanna, kangaroos in Australia,
    /// penguins in Antarctica, dolphins in warm seas...). They graze, wander and keep off the water; a herd that
    /// is left far behind is moved somewhere new near the player. Our own models (own-animal-*). Presentation only.
    /// </summary>
    public sealed class Wildlife : MonoBehaviour
    {
        const int HerdCount = 5;
        const float Respawn = 330f;

        enum Gait { Walk, Hop, Waddle, Leap, Cruise }

        sealed class Species
        {
            public string Model; public int Min, Max; public float Speed, Spread; public Gait Gait; public float Size;
            public Species(string model, int min, int max, float speed, Gait gait = Gait.Walk, float spread = 14f, float size = 1f)
            { Model = model; Min = min; Max = max; Speed = speed; Gait = gait; Spread = spread; Size = size; }
        }

        static readonly Dictionary<string, Species> All = new Dictionary<string, Species>
        {
            { "elephant", new Species("elephant", 2, 5, 1.1f, Gait.Walk, 18f) }, { "giraffe", new Species("giraffe", 2, 4, 1.2f, Gait.Walk, 18f) },
            { "zebra", new Species("zebra", 4, 8, 1.4f) }, { "horse", new Species("horse", 3, 6, 1.5f) }, { "lion", new Species("lion", 2, 4, 1.0f) },
            { "tiger", new Species("tiger", 1, 2, 1.1f) }, { "kangaroo", new Species("kangaroo", 3, 6, 2.6f, Gait.Hop) }, { "camel", new Species("camel", 3, 6, 1.0f) },
            { "bear", new Species("bear", 1, 2, 0.9f) }, { "polarbear", new Species("polarbear", 1, 2, 0.9f) }, { "panda", new Species("panda", 1, 3, 0.5f) },
            { "deer", new Species("deer", 3, 6, 1.4f) }, { "cow", new Species("cow", 4, 8, 0.6f) }, { "bison", new Species("bison", 4, 8, 0.8f) },
            { "sheep", new Species("sheep", 5, 9, 0.6f, Gait.Walk, 10f) }, { "llama", new Species("llama", 3, 6, 0.9f) },
            { "penguin", new Species("penguin", 6, 10, 0.5f, Gait.Waddle, 7f) }, { "dolphin", new Species("dolphin", 3, 5, 5f, Gait.Leap, 10f) },
            { "whale", new Species("whale", 1, 2, 2.2f, Gait.Cruise, 30f) },
        };

        sealed class Animal
        {
            public Transform T; public Vector3 Dir; public float Heading, Phase, Pause, Walk, Turn;
        }

        sealed class Herd
        {
            public Species S; public Vector3 Centre; public float Retry;
            public readonly List<Animal> A = new List<Animal>();
        }

        Planet _planet;
        readonly List<Herd> _herds = new List<Herd>();
        readonly System.Random _rng = new System.Random(977);
        float R() => (float)_rng.NextDouble();

        public void Init(Planet planet)
        {
            _planet = planet;
            if (Kit.Get("animal-deer") == null) { enabled = false; return; }   // no models: no wildlife
            for (int i = 0; i < HerdCount; i++) { var h = new Herd(); _herds.Add(h); Place(h, true); }
        }

        static bool In(float v, float a, float b) => v >= a && v <= b;

        /// <summary>The species that lives at this spot, or null where nothing should roam (towns, bare ice, the farm).</summary>
        string Pick(Vector3 dir)
        {
            EarthData.LatLon(dir, out float lat, out float lon);
            float alat = Mathf.Abs(lat), h = _planet.Height(dir), r = R();
            if (h < Planet.WaterLevel(dir) - 1.5f)
            {
                if (EarthData.IceAt(lat, lon)) return null;
                return alat > 52f ? "whale" : (r < 0.8f ? "dolphin" : "whale");
            }
            if (h < Planet.WaterLevel(dir) + 0.6f) return null;
            if (_planet.FarmCheb(dir) < Planet.FarmHalf + 25f) return null;
            if (Places.Urban(dir, lat, lon) > 0.15f || CityReplica.Covers(dir) > 0.1f || Hubs.Near(dir, 10f)) return null;
            var biome = EarthData.BiomeAt(lat, lon);
            bool ice = biome == EarthData.Biome.Ice || EarthData.IceAt(lat, lon);
            if (lat < -58f || (ice && lat < 0f)) return "penguin";
            if (ice || lat > 68f) return "polarbear";
            bool forest = biome == EarthData.Biome.Forest, bare = biome == EarthData.Biome.Bare;
            if (In(lon, 112f, 155f) && In(lat, -44f, -10f)) return "kangaroo";
            if (In(lon, -18f, 52f) && In(lat, -35f, 16f))
            {
                if (bare) return lat > 5f ? "camel" : (r < 0.5f ? "zebra" : "lion");
                if (forest) return r < 0.7f ? "elephant" : "zebra";
                return r < 0.28f ? "elephant" : r < 0.5f ? "giraffe" : r < 0.8f ? "zebra" : "lion";
            }
            if (bare && ((In(lat, 12f, 38f) && In(lon, -18f, 75f)) || (In(lat, 35f, 50f) && In(lon, 50f, 112f)))) return "camel";
            if (In(lon, -82f, -62f) && In(lat, -50f, 8f) && h > 55f) return "llama";
            if (forest && In(lon, 100f, 120f) && In(lat, 24f, 35f) && r < 0.6f) return "panda";
            if (In(lon, 68f, 125f) && In(lat, -10f, 30f)) return forest ? (r < 0.45f ? "tiger" : r < 0.8f ? "elephant" : "deer") : (r < 0.6f ? "cow" : "elephant");
            if (alat > 50f) return bare ? (r < 0.6f ? "deer" : "bear") : (r < 0.5f ? "deer" : r < 0.8f ? "bear" : "horse");
            if (bare) return alat < 40f && In(lon, -125f, -95f) ? (r < 0.5f ? "horse" : null) : null;
            if (!forest && In(lon, -115f, -90f) && In(lat, 32f, 52f) && r < 0.6f) return "bison";
            if (forest) return r < 0.7f ? "deer" : "bear";
            return r < 0.4f ? "cow" : r < 0.7f ? "sheep" : r < 0.88f ? "horse" : "deer";
        }

        void Place(Herd herd, bool anywhere)
        {
            var pl = _planet.transform.InverseTransformPoint(_planet.Player.position);
            var up = pl.normalized;
            Planet.TangentAt(up, out var e, out var n);
            float a = R() * Mathf.PI * 2f, dist = anywhere ? 45f + R() * 170f : 130f + R() * 120f;
            var dir = (pl + (e * Mathf.Cos(a) + n * Mathf.Sin(a)) * dist).normalized;
            string name = Pick(dir);
            herd.Centre = dir;
            if (name == null || !All.TryGetValue(name, out var s)) { Clear(herd); herd.S = null; herd.Retry = 4f + R() * 6f; return; }
            if (herd.S != s || herd.A.Count == 0)
            {
                Clear(herd);
                herd.S = s;
                int count = s.Min + (int)(R() * (s.Max - s.Min + 1));
                for (int i = 0; i < count; i++)
                {
                    var g = Own.Make("own-animal-" + s.Model, s.Model, transform, Vector3.zero, 0.3f);
                    if (g == null) break;
                    g.transform.localScale = Vector3.one * s.Size * (0.85f + R() * 0.3f) * (i > 0 && R() < 0.25f ? 0.6f : 1f);   // the odd youngster
                    herd.A.Add(new Animal { T = g.transform });
                }
            }
            Planet.TangentAt(dir, out e, out n);
            foreach (var an in herd.A)
            {
                float b = R() * Mathf.PI * 2f, d = Mathf.Sqrt(R()) * s.Spread;
                an.Dir = (dir * Planet.Radius + (e * Mathf.Cos(b) + n * Mathf.Sin(b)) * d).normalized;
                an.Heading = R() * Mathf.PI * 2f;
                an.Phase = R() * 6.28f;
                an.Pause = R() * 6f;
                an.Walk = 0f;
            }
        }

        void Clear(Herd herd)
        {
            foreach (var an in herd.A) if (an.T != null) Destroy(an.T.gameObject);
            herd.A.Clear();
        }

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            float dt = Time.deltaTime, t = Time.time;
            var pt = _planet.transform;
            foreach (var herd in _herds)
            {
                if (herd.S == null) { herd.Retry -= dt; if (herd.Retry <= 0f) Place(herd, false); continue; }
                var centre = pt.TransformPoint(herd.Centre * Planet.Radius);
                var toPlayer = _planet.Player.position - centre;
                var upC = pt.TransformDirection(herd.Centre);
                toPlayer -= upC * Vector3.Dot(toPlayer, upC);
                if (toPlayer.magnitude > Respawn) { Place(herd, false); if (herd.S == null) continue; }
                var s = herd.S;
                bool sea = s.Gait == Gait.Leap || s.Gait == Gait.Cruise;
                foreach (var an in herd.A)
                {
                    Planet.TangentAt(an.Dir, out var e, out var n);
                    // Graze for a while, then walk for a while (sea animals never stop).
                    bool moving = sea || an.Pause <= 0f;
                    if (!sea)
                    {
                        if (an.Pause > 0f) { an.Pause -= dt; if (an.Pause <= 0f) { an.Walk = 3f + R() * 7f; an.Turn = (R() - 0.5f) * 0.5f; } }
                        else { an.Walk -= dt; if (an.Walk <= 0f) an.Pause = 2f + R() * 7f; }
                    }
                    if (moving)
                    {
                        an.Heading += (sea ? Mathf.Sin(t * 0.2f + an.Phase) * 0.25f : an.Turn) * dt;
                        // Stay with the herd.
                        var back = herd.Centre - an.Dir;
                        if (back.magnitude * Planet.Radius > s.Spread * 1.6f)
                        {
                            float want = Mathf.Atan2(Vector3.Dot(back, n), Vector3.Dot(back, e));
                            an.Heading = Mathf.LerpAngle(an.Heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg, dt * 1.5f) * Mathf.Deg2Rad;
                        }
                        var step = (e * Mathf.Cos(an.Heading) + n * Mathf.Sin(an.Heading)) * (s.Speed * dt / Planet.Radius);
                        var next = (an.Dir + step).normalized;
                        float nh = _planet.Height(next), nwl = Planet.WaterLevel(next);
                        bool ok = sea ? nh < nwl - 1.2f : nh > nwl + 0.5f;
                        if (ok) an.Dir = next; else an.Heading += Mathf.PI * (0.7f + R() * 0.6f);
                    }
                    float h = _planet.Height(an.Dir), wl = Planet.WaterLevel(an.Dir);
                    var fwdL = e * Mathf.Cos(an.Heading) + n * Mathf.Sin(an.Heading);
                    var up = pt.TransformDirection(an.Dir);
                    var fwd = pt.TransformDirection(fwdL);
                    float y, pitch = 0f, roll = 0f;
                    switch (s.Gait)
                    {
                        case Gait.Leap:
                        {
                            float ph = t * 1.5f + an.Phase;
                            y = wl - 0.5f + Mathf.Sin(ph) * 1.3f;
                            pitch = -Mathf.Cos(ph) * 38f;
                            break;
                        }
                        case Gait.Cruise:
                            y = wl - 1.5f + Mathf.Sin(t * 0.35f + an.Phase) * 0.9f;
                            pitch = -Mathf.Cos(t * 0.35f + an.Phase) * 5f;
                            break;
                        case Gait.Hop:
                            y = h + (moving ? Mathf.Abs(Mathf.Sin(t * 5f + an.Phase)) * 0.55f : 0f);
                            pitch = moving ? Mathf.Sin(t * 10f + an.Phase * 2f) * 6f : 0f;
                            break;
                        case Gait.Waddle:
                            y = h;
                            roll = moving ? Mathf.Sin(t * 9f + an.Phase) * 11f : 0f;
                            break;
                        default:
                            y = h + (moving ? Mathf.Abs(Mathf.Sin(t * 7f * Mathf.Sqrt(s.Speed) + an.Phase)) * 0.04f : 0f);
                            roll = moving ? Mathf.Sin(t * 7f * Mathf.Sqrt(s.Speed) + an.Phase) * 2.5f : 0f;
                            pitch = moving ? 0f : Mathf.Max(0f, Mathf.Sin(t * 0.5f + an.Phase)) * 6f;   // head down to graze
                            break;
                    }
                    an.T.position = pt.TransformPoint(an.Dir * (Planet.Radius + y));
                    an.T.rotation = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(pitch, 0f, roll);
                }
            }
        }
    }
}
