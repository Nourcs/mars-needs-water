using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The look and feel of seas, lakes and rivers: the water material, the swell (the same sums the shader uses,
    /// so floating things bob with what you see), rings and splashes when something disturbs the water, and the
    /// murk you see with your head under. The surfaces themselves come with each square (WaterMesh).
    /// </summary>
    public sealed class EarthWater : MonoBehaviour
    {
        public static EarthWater I;

        public Material Material { get; private set; }
        /// <summary>The camera is under water.</summary>
        public bool Under { get { return under; } }

        const int MaxRipples = 32;

        struct Wave { public float Kx, Kz, A, W; }
        static readonly Wave[] Waves = MakeWaves();

        EarthWorld world;
        Vector2 offset;
        readonly Vector4[] ripples = new Vector4[MaxRipples];
        readonly Vector4[] waveData = new Vector4[4];
        int nextRipple;
        ParticleSystem drops;
        Color fogAbove;
        bool under;

        static readonly int IdTime = Shader.PropertyToID("_WaterTime"), IdOffset = Shader.PropertyToID("_WaterOffset"),
            IdRipples = Shader.PropertyToID("_WaterRipples"), IdWaves = Shader.PropertyToID("_WaterWaves"), IdUnder = Shader.PropertyToID("_WaterUnder");

#if UNITY_EDITOR
        /// <summary>
        /// New code often lands on disk while the game is playing. Unity's default is to recompile on the spot,
        /// which wipes every running script's lists and floods the Console with errors. Ask it to wait until Play stops.
        /// (Unity > Settings > General > Script Changes While Playing.)
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RecompileAfterPlaying()
        {
            const string Key = "ScriptCompilationDuringPlay";
            if (UnityEditor.EditorPrefs.GetInt(Key, 0) == 0) UnityEditor.EditorPrefs.SetInt(Key, 1);
        }
#endif

        static Wave[] MakeWaves()
        {
            // four long swells at different angles; game metres, so a 23 m swell is a 115 m one in the real sea
            float[] angle = { 20f, 68f, -32f, 115f }, length = { 23f, 14f, 9f, 6f }, height = { 0.10f, 0.06f, 0.04f, 0.025f };
            var w = new Wave[4];
            for (int i = 0; i < 4; i++)
            {
                float k = 2f * Mathf.PI / length[i];
                // deep-water speed of the real wave, shrunk to the map scale
                float speed = Mathf.Sqrt(9.81f * length[i] * (float)Geo.Scale / (2f * Mathf.PI)) / (float)Geo.Scale;
                float a = angle[i] * Mathf.Deg2Rad;
                w[i] = new Wave { Kx = Mathf.Sin(a) * k, Kz = Mathf.Cos(a) * k, A = height[i], W = speed * k };
            }
            return w;
        }

        public void Init(EarthWorld w)
        {
            I = this;
            world = w;
            fogAbove = RenderSettings.fogColor;
            WantDepthAndColour();

            Shader s = Resources.Load<Shader>("Earth/Shaders/EarthWater");
            if (s == null || !s.isSupported) s = Shader.Find("Universal Render Pipeline/Unlit");
            Material = new Material(s) { name = "Earth water" };
            Material.SetTexture("_WaveTex", MakeWaveTexture());
            Material.SetColor("_SkyHorizon", fogAbove);
            if (s.name != "MarsNeedsWater/EarthWater") Material.SetColor("_BaseColor", new Color(0.12f, 0.32f, 0.45f));

            for (int i = 0; i < 4; i++) waveData[i] = new Vector4(Waves[i].Kx, Waves[i].Kz, Waves[i].A, Waves[i].W);
            for (int i = 0; i < MaxRipples; i++) ripples[i] = new Vector4(0, 0, 0, -100f);
            Shader.SetGlobalVectorArray(IdWaves, waveData);
            Shader.SetGlobalVectorArray(IdRipples, ripples);

            drops = MakeDrops();
            gameObject.AddComponent<WaterCan>();
            gameObject.AddComponent<WaterReflection>().Init(w);
            gameObject.AddComponent<WaterSounds>().Init(w);
        }

        void OnDestroy() { if (I == this) I = null; }

        /// <summary>The water shader reads the depth and colour of what is behind the water; make sure the pipeline keeps them.</summary>
        static void WantDepthAndColour()
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;
            foreach (string name in new[] { "supportsCameraDepthTexture", "supportsCameraOpaqueTexture" })
            {
                var prop = rp.GetType().GetProperty(name);
                try { if (prop != null && prop.CanWrite) prop.SetValue(rp, true); } catch { }
            }
        }

        // ------------------------------------------------------------ the swell, as the shader has it

        /// <summary>How far the surface is lifted at a world spot right now. lake: lakes and rivers heave less.</summary>
        public float Swell(Vector3 at, bool lake, float depth = 10f)
        {
            float x = at.x + offset.x, z = at.z + offset.y, t = Time.time, h = 0;
            Warp(x, z, out float qx, out float qz);
            for (int i = 0; i < 4; i++)
            {
                Wave w = Waves[i];
                h += w.A * Amp(x, z, t, i) * Mathf.Sin(w.Kx * qx + w.Kz * qz - w.W * t + i * 1.7f);
            }
            return h * (lake ? 0.3f : 1f) * Mathf.Clamp01(depth / 1.5f);
        }

        // the shader's Hash, Noise and Patchy, so floating things heave as much as the water they sit on
        static float Frac(float v) { return v - Mathf.Floor(v); }

        static float Hash(float cx, float cy)
        {
            float qx = Frac(cx * 0.1031f), qy = Frac(cy * 0.1031f), qz = Frac(cx * 0.1031f);
            float d = qx * (qy + 33.33f) + qy * (qz + 33.33f) + qz * (qx + 33.33f);
            qx += d; qy += d; qz += d;
            return Frac((qx + qy) * qz);
        }

        static float Noise(float px, float py)
        {
            float cx = Mathf.Floor(px), cy = Mathf.Floor(py), fx = px - cx, fy = py - cy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(cx, cy), Hash(cx + 1f, cy), fx), b = Mathf.Lerp(Hash(cx, cy + 1f), Hash(cx + 1f, cy + 1f), fx);
            return Mathf.Lerp(a, b, fy);
        }

        static void Warp(float x, float z, out float qx, out float qz)
        {
            qx = x + Noise(x / 37f + 3.1f, z / 37f + 3.1f) * 9f - 4.5f;
            qz = z + Noise(x / 37f - 8.7f, z / 37f - 8.7f) * 9f - 4.5f;
        }

        static float Amp(float x, float z, float t, int i)
        {
            float s = 53f + i * 19f, o = i * 5.3f + t * 0.004f;
            return 0.2f + 1.5f * Noise(x / s + o, z / s + o);
        }

        /// <summary>Slope of the swell (rise per metre east, rise per metre north), for tilting floating things.</summary>
        public Vector2 SwellSlope(Vector3 at, bool lake, float depth = 10f)
        {
            float x = at.x + offset.x, z = at.z + offset.y, t = Time.time;
            Warp(x, z, out float qx, out float qz);
            Vector2 g = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                Wave w = Waves[i];
                float c = w.A * Amp(x, z, t, i) * Mathf.Cos(w.Kx * qx + w.Kz * qz - w.W * t + i * 1.7f);
                g.x += c * w.Kx; g.y += c * w.Kz;
            }
            return g * (lake ? 0.3f : 1f) * Mathf.Clamp01(depth / 1.5f);
        }

        // ------------------------------------------------------------ disturbing the water

        /// <summary>A ring spreading out on the surface. strength about 0.2 (a hand) to 1.5 (a big splash).</summary>
        public void Ripple(Vector3 at, float strength)
        {
            ripples[nextRipple] = new Vector4(at.x, Mathf.Clamp(strength, 0.05f, 2f), at.z, Time.time);
            nextRipple = (nextRipple + 1) % MaxRipples;
        }

        static readonly Color Drop = new Color(0.86f, 0.93f, 0.97f, 0.85f);

        /// <summary>Water thrown up in a crown, plus a ring. power about 1 (a step) to 10 (a jump from high up).</summary>
        public void Splash(Vector3 at, float power)
        {
            Ripple(at, Mathf.Clamp(power * 0.18f, 0.2f, 2f));
            if (WaterSounds.I != null) WaterSounds.I.Splash(power);
            int n = Mathf.Clamp((int)(power * 7f), 4, 90);
            float up = 1.2f + power * 0.55f;
            for (int i = 0; i < n; i++)
            {
                Vector2 r = Random.insideUnitCircle;
                var e = new ParticleSystem.EmitParams
                {
                    position = at + new Vector3(r.x, 0.05f, r.y) * 0.3f,
                    velocity = new Vector3(r.x * up * 0.45f, up * Random.Range(0.55f, 1.15f), r.y * up * 0.45f),
                    startSize = Random.Range(0.08f, 0.22f) * (0.8f + power * 0.06f),
                    startLifetime = Random.Range(0.6f, 1.2f),
                    startColor = Drop
                };
                drops.Emit(e, 1);
            }
        }

        /// <summary>A spray of drops thrown with a velocity (a boat's bow, a pour, a skimming plane).</summary>
        public void Spray(Vector3 at, Vector3 velocity, int count, float spread = 0.4f, float size = 0.14f)
        {
            for (int i = 0; i < count; i++)
            {
                var e = new ParticleSystem.EmitParams
                {
                    position = at + Random.insideUnitSphere * 0.15f,
                    velocity = velocity + Random.insideUnitSphere * (velocity.magnitude * spread + 0.3f),
                    startSize = size * Random.Range(0.6f, 1.4f),
                    startLifetime = Random.Range(0.4f, 0.9f),
                    startColor = Drop
                };
                drops.Emit(e, 1);
            }
        }

        ParticleSystem MakeDrops()
        {
            var go = new GameObject("Water drops") { layer = WaterReflection.WaterLayer };
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.maxParticles = 3000;
            main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.5f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            Shader s = Resources.Load<Shader>("Earth/Shaders/WaterSpray");
            if (s == null || !s.isSupported) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            r.sharedMaterial = new Material(s) { name = "Water drops" };
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------ the world moving under us

        /// <summary>The world re-centred: everything moved by -shift (x, z). The waves and rings stay where they were.</summary>
        public void Shifted(Vector3 shift)
        {
            offset.x = Mathf.Repeat(offset.x + shift.x, 92160f);    // kept small so the sums stay precise
            offset.y = Mathf.Repeat(offset.y + shift.z, 92160f);
            for (int i = 0; i < MaxRipples; i++) { ripples[i].x -= shift.x; ripples[i].z -= shift.z; }
            if (drops != null) drops.Clear();
        }

        /// <summary>Jumped somewhere else: forget the rings and drops.</summary>
        public void Clear()
        {
            for (int i = 0; i < MaxRipples; i++) ripples[i].w = -100f;
            if (drops != null) drops.Clear();
        }

        void LateUpdate()
        {
            Shader.SetGlobalFloat(IdTime, Time.time);
            Shader.SetGlobalVector(IdOffset, new Vector4(offset.x, offset.y, 0, 0));
            Shader.SetGlobalVectorArray(IdRipples, ripples);
            Shader.SetGlobalVectorArray(IdWaves, waveData);

            // head under water: close, blue-green murk instead of the sky's haze
            Camera cam = Camera.main;
            bool nowUnder = false, lake = false;
            if (cam != null && world != null)
            {
                Vector3 c = cam.transform.position;
                float level, bed;
                if (world.WaterAt(c, out level, out bed, out lake) && level > bed && c.y < level + Swell(c, lake, level - bed) - 0.05f) nowUnder = true;
            }
            if (nowUnder)
            {
                if (!under) fogAbove = RenderSettings.fogColor;
                RenderSettings.fogColor = lake ? new Color(0.10f, 0.24f, 0.20f) : new Color(0.06f, 0.24f, 0.30f);
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = lake ? 9f : 16f;
            }
            else if (under) RenderSettings.fogColor = fogAbove;
            under = nowUnder;
            Shader.SetGlobalFloat(IdUnder, under ? 1f : 0f);
        }

        // ------------------------------------------------------------ a tiling picture of small waves

        /// <summary>
        /// 512 x 512, repeating. Red and green: the slope of a sea of small waves, built like a real wave spectrum
        /// (many waves of every length, longer ones taller, mostly running one way with some across), each fitting the
        /// square a whole number of times so it tiles. Blue: lacy foam (the edges of bubble cells). Alpha: the bright
        /// net of light that waves focus on a shallow bed (caustics).
        /// </summary>
        static Texture2D MakeWaveTexture()
        {
            const int N = 512;
            var rnd = new System.Random(2026);

            // ---- the waves
            var kx = new System.Collections.Generic.List<float>(); var ky = new System.Collections.Generic.List<float>();
            var amp = new System.Collections.Generic.List<float>(); var ph = new System.Collections.Generic.List<float>();
            for (int a = -32; a <= 32; a++)
                for (int b = -32; b <= 32; b++)
                {
                    float k = Mathf.Sqrt(a * a + b * b);
                    if (k < 2f || k > 32f) continue;
                    float along = (a * 0.80f + b * 0.60f) / k;                 // the wind's way
                    float spread = Mathf.Pow(Mathf.Abs(along), 2f) * (along < 0 ? 0.25f : 1f) + 0.08f;
                    float A = spread / (k * k) * Mathf.Exp(-(4f / k) * (4f / k));
                    // keep a random share of the small ones so the sum stays quick to make
                    if (k > 10f && rnd.NextDouble() > 0.08) continue;
                    if (k > 10f) A *= 12f;
                    kx.Add(2f * Mathf.PI * a / N); ky.Add(2f * Mathf.PI * b / N);
                    amp.Add(A * (0.5f + (float)rnd.NextDouble())); ph.Add((float)rnd.NextDouble() * 6.2831f);
                }
            int W = kx.Count;
            var sx = new float[N * N]; var sy = new float[N * N];
            // separable: cos(ax + by + p) = cos(ax)cos(by + p) - sin(ax)sin(by + p)
            var cx = new float[N]; var snx = new float[N];
            for (int w = 0; w < W; w++)
            {
                float A = amp[w];
                if (A < 1e-7f) continue;
                for (int x = 0; x < N; x++) { cx[x] = Mathf.Cos(kx[w] * x); snx[x] = Mathf.Sin(kx[w] * x); }
                for (int y = 0; y < N; y++)
                {
                    float cy = Mathf.Cos(ky[w] * y + ph[w]), sy_ = Mathf.Sin(ky[w] * y + ph[w]);
                    int row = y * N;
                    float gx = A * kx[w] * N, gy = A * ky[w] * N;
                    for (int x = 0; x < N; x++)
                    {
                        float c = cx[x] * cy - snx[x] * sy_;
                        sx[row + x] += c * gx; sy[row + x] += c * gy;
                    }
                }
            }
            float maxS = 1e-6f;
            for (int o = 0; o < N * N; o++) maxS = Mathf.Max(maxS, Mathf.Abs(sx[o]), Mathf.Abs(sy[o]));

            // ---- bubble cells (foam) and light nets (caustics): distance to the nearest cell edges, repeating
            float[] foam = Cells(N, 22, rnd, 10f), caustic = Cells(N, 9, rnd, 6f);

            var px = new Color32[N * N];
            for (int o = 0; o < N * N; o++)
            {
                float f = foam[o], c = caustic[o];
                px[o] = new Color32((byte)Mathf.RoundToInt(127.5f + 127f * sx[o] / maxS), (byte)Mathf.RoundToInt(127.5f + 127f * sy[o] / maxS),
                                    (byte)Mathf.RoundToInt(255f * f), (byte)Mathf.RoundToInt(255f * c));
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "Water waves", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// A repeating pattern of rounded cells (cells x cells of them over the square), bright along the borders where
        /// two cells meet: 1 on a border, falling to 0 inside. sharp: how thin the bright lines are.
        /// </summary>
        static float[] Cells(int N, int cells, System.Random rnd, float sharp)
        {
            var pxs = new float[cells * cells]; var pys = new float[cells * cells];
            for (int i = 0; i < cells * cells; i++) { pxs[i] = (float)rnd.NextDouble(); pys[i] = (float)rnd.NextDouble(); }
            var o = new float[N * N];
            float size = N / (float)cells;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float gx = x / size, gy = y / size;
                    int cx = Mathf.FloorToInt(gx), cy = Mathf.FloorToInt(gy);
                    float d1 = 9f, d2 = 9f;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int ci = cx + i, cj = cy + j;
                            int wi = ((ci % cells) + cells) % cells, wj = ((cj % cells) + cells) % cells;
                            float dx = ci + pxs[wj * cells + wi] - gx, dy = cj + pys[wj * cells + wi] - gy;
                            float d = dx * dx + dy * dy;
                            if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                        }
                    float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);                 // 0 on a border
                    o[y * N + x] = Mathf.Exp(-edge * sharp);
                }
            return o;
        }
    }
}
