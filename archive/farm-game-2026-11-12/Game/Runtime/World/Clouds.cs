using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Big puffy cartoon clouds: clusters of low-poly blobs drifting with the wind, 200-420 m up, within about 2.2 km
    /// of the player. A cloud that drifts or is left too far behind is rebuilt upwind. They live on the planet, so
    /// they stay put when the planet is re-based. One draw call each, no shadows. Presentation only.
    /// </summary>
    public sealed class Clouds : MonoBehaviour
    {
        const int Count = 34;
        const float Range = 2200f, Drop = 2600f;

        Planet _planet;
        Material _mat;
        readonly List<Transform> _clouds = new List<Transform>();
        readonly List<Mesh> _meshes = new List<Mesh>();
        readonly System.Random _rng = new System.Random(77);
        Vector3 _windPlanet;   // planet-space drift direction near the player
        float R() => (float)_rng.NextDouble();

        public void Init(Planet planet)
        {
            _planet = planet;
            _mat = Prims.VertexColored(0f, false, 0.5f);
            // Bright tops, soft blue-grey undersides (the shader's ambient), no hard terminator.
            if (_mat.HasProperty("_EmissionColor")) _mat.SetColor("_EmissionColor", new Color(0.3f, 0.32f, 0.36f));
            if (_mat.HasProperty("_ShadeSoftness")) _mat.SetFloat("_ShadeSoftness", 1f);
            if (_mat.HasProperty("_RimStrength")) _mat.SetFloat("_RimStrength", 0.6f);
            for (int i = 0; i < 6; i++) _meshes.Add(BuildCloud(i));
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Cloud");
                go.transform.SetParent(planet.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _meshes[i % _meshes.Count];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                _clouds.Add(go.transform);
                Place(go.transform, true);
            }
        }

        Mesh BuildCloud(int seed)
        {
            var rng = new System.Random(seed * 31 + 5);
            float Rr() => (float)rng.NextDouble();
            var mb = new MeshBuilder();
            int puffs = 7 + (int)(Rr() * 6f);
            float len = 60f + Rr() * 60f;
            for (int i = 0; i < puffs; i++)
            {
                float t = puffs == 1 ? 0.5f : i / (puffs - 1f);
                float x = (t - 0.5f) * len, z = (Rr() - 0.5f) * len * 0.4f;
                float size = (22f + Rr() * 20f) * (1f - Mathf.Abs(t - 0.5f) * 0.9f);
                // Taller puffs in the middle make a cauliflower cumulus top.
                float lift = size * (0.15f + (1f - Mathf.Abs(t - 0.5f) * 2f) * 0.35f);
                mb.Blob(Matrix4x4.TRS(new Vector3(x, lift, z), Quaternion.Euler(0f, Rr() * 360f, 0f), new Vector3(size, size * 0.8f, size * 0.9f)), new Color(1f, 1f, 1f), 10);
            }
            // A flat-ish base so they read as cumulus.
            mb.Blob(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(len * 0.9f, 8f, len * 0.4f)), new Color(0.93f, 0.95f, 0.98f), 10);
            return mb.ToMesh("Cloud" + seed);
        }

        /// <summary>Puts a cloud somewhere around the player (anywhere at start, upwind afterwards).</summary>
        void Place(Transform c, bool anywhere)
        {
            var pl = _planet.transform.InverseTransformPoint(_planet.Player.position);
            var up = pl.normalized;
            Planet.TangentAt(up, out var e, out var n);
            if (_windPlanet.sqrMagnitude < 0.5f) _windPlanet = (e * 0.8f + n * 0.6f).normalized;
            Vector3 offset;
            if (anywhere) { float a = R() * Mathf.PI * 2f, d = Mathf.Sqrt(R()) * Range; offset = (e * Mathf.Cos(a) + n * Mathf.Sin(a)) * d; }
            else
            {
                var wind = (_windPlanet - up * Vector3.Dot(_windPlanet, up)).normalized;
                var side = Vector3.Cross(up, wind);
                offset = -wind * Range * (0.8f + R() * 0.2f) + side * (R() - 0.5f) * Range * 1.6f;
            }
            var dir = (pl + offset).normalized;
            float ground = Mathf.Max(Planet.WaterLevel(dir), _planet.Height(dir));
            c.localPosition = dir * (Planet.Radius + Mathf.Max(ground + 120f, 200f + R() * 220f));
            c.localRotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up).sqrMagnitude > 1e-6f ? Vector3.Cross(dir, Vector3.up) : Vector3.forward, dir)
                              * Quaternion.Euler(0f, R() * 360f, 0f);
            float s = 1f + R() * 1.2f;
            c.localScale = new Vector3(s, s * (0.8f + R() * 0.4f), s);
        }

        void Update()
        {
            if (_planet == null || _planet.Player == null) return;
            var pl = _planet.transform.InverseTransformPoint(_planet.Player.position);
            float step = 4f * Time.deltaTime; // 4 m/s breeze
            foreach (var c in _clouds)
            {
                var p = c.localPosition;
                var up = p.normalized;
                var wind = (_windPlanet - up * Vector3.Dot(_windPlanet, up)).normalized;
                c.localPosition = p + wind * step;
                // Measured along the ground, ignoring height.
                float d = (up - pl.normalized).magnitude * Planet.Radius;
                if (d > Drop) Place(c, false);
            }
        }

        void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
            if (_mat != null) Destroy(_mat);
        }
    }
}
