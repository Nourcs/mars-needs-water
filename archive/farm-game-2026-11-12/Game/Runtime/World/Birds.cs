using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// A few flocks of birds circling and drifting in the sky around the player, flapping and gliding. Each flock
    /// lives at a spot on the planet (so it stays put when the planet is re-based) and is moved somewhere new once
    /// the player has walked away from it. Gulls over the sea, darker birds over land. Presentation only.
    /// </summary>
    public sealed class Birds : MonoBehaviour
    {
        const int FlockCount = 4;
        const float Respawn = 280f;

        sealed class Flock
        {
            public Vector3 CentreLocal;    // planet space
            public float Radius, Speed, Angle, Climb;
            public readonly List<Transform> Bird = new List<Transform>();
            public readonly List<Transform> WingL = new List<Transform>(), WingR = new List<Transform>();
            public readonly List<float> Phase = new List<float>(), Lag = new List<float>(), Side = new List<float>();
            public Material Body, Wing;
        }

        Planet _planet;
        Mesh _body, _wing;
        readonly List<Flock> _flocks = new List<Flock>();
        readonly System.Random _rng = new System.Random(4242);
        float R() => (float)_rng.NextDouble();

        static readonly Color[] Tropical = { new Color(0.9f, 0.22f, 0.2f), new Color(0.2f, 0.5f, 0.95f), new Color(0.25f, 0.75f, 0.3f), new Color(0.98f, 0.8f, 0.2f), new Color(0.98f, 0.55f, 0.65f) };
        static readonly Color Gull = new Color(0.95f, 0.95f, 0.93f), Crow = new Color(0.18f, 0.18f, 0.22f), Swallow = new Color(0.36f, 0.27f, 0.2f);

        public void Init(Planet planet)
        {
            _planet = planet;
            var mb = new MeshBuilder();
            mb.Box(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(0.22f, 0.2f, 0.75f)), Color.white, Color.white);
            mb.Blob(Matrix4x4.TRS(new Vector3(0f, 0.06f, 0.42f), Quaternion.identity, new Vector3(0.22f, 0.2f, 0.24f)), Color.white, 6);
            mb.Cone(Matrix4x4.TRS(new Vector3(0f, 0.05f, 0.52f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.08f, 0.14f, 0.08f)), 4, new Color(1f, 0.75f, 0.3f));
            mb.Tri(Matrix4x4.identity, new Vector3(-0.18f, 0f, -0.55f), new Vector3(0f, 0f, -0.3f), new Vector3(0.18f, 0f, -0.55f), Color.white); // tail
            var own = Kit.Get("animal-bird");               // our own bird body, when its model file is there
            if (own != null) { mb = new MeshBuilder(); mb.Model(Matrix4x4.identity, own); }
            _body = mb.ToMesh("BirdBody");
            var wb = new MeshBuilder();
            // One wing from the shoulder (x = 0) out to the tip, swept back.
            wb.Quad(Matrix4x4.identity, new Vector3(0f, 0f, 0.16f), new Vector3(0.55f, 0f, 0.1f), new Vector3(0.6f, 0f, -0.16f), new Vector3(0f, 0f, -0.18f), Color.white);
            wb.Tri(Matrix4x4.identity, new Vector3(0.55f, 0f, 0.1f), new Vector3(1.05f, 0f, -0.2f), new Vector3(0.6f, 0f, -0.16f), Color.white);
            _wing = wb.ToMesh("BirdWing");
            for (int f = 0; f < FlockCount; f++) _flocks.Add(MakeFlock(f));
        }

        Flock MakeFlock(int index)
        {
            var fl = new Flock();
            int n = 4 + (int)(R() * 6f);
            for (int i = 0; i < n; i++)
            {
                var b = new GameObject("Bird").transform;
                b.SetParent(transform, false);
                b.gameObject.AddComponent<MeshFilter>().sharedMesh = _body;
                var r = b.gameObject.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                fl.Bird.Add(b);
                for (int s = 0; s < 2; s++)
                {
                    var w = new GameObject(s == 0 ? "WingL" : "WingR").transform;
                    w.SetParent(b, false);
                    w.localPosition = new Vector3(s == 0 ? -0.1f : 0.1f, 0.06f, 0.05f);
                    w.localScale = new Vector3(s == 0 ? -1f : 1f, 1f, 1f);
                    w.gameObject.AddComponent<MeshFilter>().sharedMesh = _wing;
                    w.gameObject.AddComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    (s == 0 ? fl.WingL : fl.WingR).Add(w);
                }
                fl.Phase.Add(R() * 6.28f);
                // V formation: each bird a little behind and to one side of the one ahead.
                fl.Lag.Add(((i + 1) / 2) * 0.05f);
                fl.Side.Add(i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 1.6f);
            }
            Place(fl, true);
            return fl;
        }

        /// <summary>Puts a flock in the sky somewhere around the player, and colours it for what is below.</summary>
        void Place(Flock fl, bool anywhere)
        {
            var player = _planet.Player.position;
            var pl = _planet.transform.InverseTransformPoint(player);
            var up = pl.normalized;
            Planet.TangentAt(up, out var e, out var n);
            float a = R() * Mathf.PI * 2f, dist = anywhere ? 30f + R() * 150f : 120f + R() * 120f;
            var dir = (pl + (e * Mathf.Cos(a) + n * Mathf.Sin(a)) * dist).normalized;
            float wl = Planet.WaterLevel(dir), hd = _planet.Height(dir);
            float ground = Mathf.Max(wl, hd);
            fl.CentreLocal = dir * (Planet.Radius + ground + 22f + R() * 30f);
            fl.Radius = 18f + R() * 40f;
            fl.Speed = (8f + R() * 6f) * (R() < 0.5f ? 1f : -1f);
            fl.Angle = R() * 6.28f;
            fl.Climb = R() * 6.28f;
            bool sea = hd < wl;
            // Gulls over the sea, bright parrots over the tropics, white birds near the poles, dark birds elsewhere.
            EarthData.LatLon(dir, out float lat, out _);
            float alat = Mathf.Abs(lat);
            var col = sea || alat > 62f ? Gull : alat < 23f && R() < 0.7f ? Tropical[(int)(R() * Tropical.Length) % Tropical.Length] : R() < 0.5f ? Crow : Swallow;
            if (fl.Body != null) { Destroy(fl.Body); Destroy(fl.Wing); }
            fl.Body = new Material(Prims.Mat(col, 0.1f, 0f));
            fl.Wing = new Material(fl.Body);
            if (fl.Wing.HasProperty("_Cull")) fl.Wing.SetFloat("_Cull", 0f);
            foreach (var b in fl.Bird) b.GetComponent<MeshRenderer>().sharedMaterial = fl.Body;
            foreach (var w in fl.WingL) w.GetComponent<MeshRenderer>().sharedMaterial = fl.Wing;
            foreach (var w in fl.WingR) w.GetComponent<MeshRenderer>().sharedMaterial = fl.Wing;
        }

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            float dt = Time.deltaTime, t = Time.time;
            var centreOfPlanet = _planet.transform.position;
            foreach (var fl in _flocks)
            {
                var c = _planet.transform.TransformPoint(fl.CentreLocal);
                if ((c - _planet.Player.position).magnitude > Respawn) { Place(fl, false); c = _planet.transform.TransformPoint(fl.CentreLocal); }
                var up = (c - centreOfPlanet).normalized;
                var e = Vector3.Cross(up, Vector3.forward);
                if (e.sqrMagnitude < 1e-6f) e = Vector3.Cross(up, Vector3.right);
                e.Normalize();
                var n = Vector3.Cross(e, up);
                fl.Angle += fl.Speed / fl.Radius * dt;
                fl.Climb += dt * 0.3f;
                // The circle itself wanders slowly, so the flock does not trace a perfect ring.
                float wander = Mathf.Sin(t * 0.07f + fl.Climb) * fl.Radius * 0.4f;
                for (int i = 0; i < fl.Bird.Count; i++)
                {
                    float ang = fl.Angle - Mathf.Sign(fl.Speed) * fl.Lag[i] * 4f;
                    float rad = fl.Radius + fl.Side[i];
                    var radial = e * Mathf.Cos(ang) + n * Mathf.Sin(ang);
                    var tangent = (-e * Mathf.Sin(ang) + n * Mathf.Cos(ang)) * Mathf.Sign(fl.Speed);
                    var pos = c + radial * rad + e * wander + up * (Mathf.Sin(fl.Climb + i * 0.4f) * 3f - Mathf.Abs(fl.Side[i]) * 0.3f);
                    var b = fl.Bird[i];
                    b.position = pos;
                    // Bank into the turn.
                    var bank = Quaternion.AngleAxis(-18f * Mathf.Sign(fl.Speed), tangent);
                    b.rotation = Quaternion.LookRotation(tangent, bank * up);
                    // Flap in bursts, then glide with the wings held out.
                    float burst = Mathf.Sin(t * 0.6f + fl.Phase[i]);
                    float flap = burst > -0.2f ? Mathf.Sin(t * 11f + fl.Phase[i] * 3f) * 38f : 6f;
                    fl.WingL[i].localRotation = Quaternion.Euler(0f, 0f, -flap);
                    fl.WingR[i].localRotation = Quaternion.Euler(0f, 0f, flap);
                }
            }
        }

        void OnDestroy()
        {
            if (_body != null) Destroy(_body);
            if (_wing != null) Destroy(_wing);
            foreach (var fl in _flocks) { if (fl.Body != null) Destroy(fl.Body); if (fl.Wing != null) Destroy(fl.Wing); }
        }
    }
}
