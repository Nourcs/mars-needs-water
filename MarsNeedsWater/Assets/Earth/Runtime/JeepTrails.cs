using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// What the ground does to the jeep and what the jeep does to the ground. Under each wheel it looks up what the
    /// ground is made of (grass, sand, rock, snow, mud, road...) and
    ///   - tells the jeep how much grip and drag that gives (Jeep.SurfGrip / SurfLoose / SurfDrag);
    ///   - lays a tyre track that suits it: dark pressed tracks in grass and mud, deep ruts with raised edges in sand
    ///     and snow, tread prints in earth, nothing on rock or road except skid marks, wet tracks after water and
    ///     the mud the tyres carry onto the road;
    ///   - throws up dust, powder snow, clods of mud, tyre smoke or spray.
    /// The tracks are one mesh of four ribbons (EarthTrail.shader); the dust is two particle systems (EarthDust.shader).
    /// Everything is varied by noise and chance, so nothing repeats.
    /// </summary>
    public sealed class JeepTrails : MonoBehaviour
    {
        [Tooltip("Tyre tracks on the ground.")] public bool Tracks = true;
        [Tooltip("Dust, snow, mud and smoke from the wheels.")] public bool Dust = true;
        [Tooltip("The ground changes the grip and drag of the tyres.")] public bool GroundGrip = true;
        [Tooltip("How long tracks last, times the normal time for each kind of ground.")] public float TrackLife = 1f;
        [Tooltip("How much dust, times normal.")] public float DustAmount = 1f;

        // ---- what each kind of ground is like (same order as GroundKind)
        struct Ground
        {
            public float Grip, Loose, Drag;                 // for the tyres
            public float Mark, Dark, Rut, Tread, Life, Wide; // the track: strength, how much darker, rut 0-1, tread 0-1, seconds, width x
            public float Puffs, Bits, Carry;                // dust, clods, how much of it sticks to the tyres
            public Ground(float grip, float loose, float drag, float mark, float dark, float rut, float tread, float life, float wide, float puffs, float bits, float carry)
            { Grip = grip; Loose = loose; Drag = drag; Mark = mark; Dark = dark; Rut = rut; Tread = tread; Life = life; Wide = wide; Puffs = puffs; Bits = bits; Carry = carry; }
        }

        static readonly Ground[] Kinds =
        {
            //          grip  loose drag   mark  dark  rut   tread life  wide  puffs bits  carry
            new Ground(0.92f, 0.35f, 0.25f, 0.55f, 0.36f, 0.20f, 0.35f, 45f, 1.00f, 0.06f, 0.30f, 0.15f),   // grass: dark flattened tracks
            new Ground(0.90f, 0.45f, 0.20f, 0.48f, 0.28f, 0.25f, 0.35f, 45f, 1.00f, 0.60f, 0.20f, 0.20f),   // dry grass: tracks and dust
            new Ground(0.86f, 0.45f, 0.35f, 0.55f, 0.34f, 0.35f, 0.55f, 55f, 1.05f, 0.20f, 0.40f, 0.40f),   // forest floor
            new Ground(0.82f, 0.55f, 0.50f, 0.62f, 0.30f, 0.65f, 0.70f, 65f, 1.15f, 0.55f, 0.50f, 0.60f),   // farmland: soft, ruts
            new Ground(0.72f, 0.80f, 0.85f, 0.78f, 0.20f, 1.00f, 0.55f, 75f, 1.35f, 0.90f, 0.40f, 0.45f),   // sand: deep ruts, lots of dust
            new Ground(1.00f, 0.12f, 0.12f, 0.00f, 0.25f, 0.00f, 0.00f, 30f, 1.00f, 0.12f, 0.00f, 0.00f),   // rock: nothing but a little dust
            new Ground(0.52f, 0.90f, 0.60f, 0.88f, 0.00f, 1.00f, 0.80f, 95f, 1.35f, 0.90f, 0.50f, 0.80f),   // snow: deep blue-shadowed ruts, powder
            new Ground(1.00f, 0.00f, 0.00f, 0.00f, 0.50f, 0.00f, 0.00f, 45f, 1.00f, 0.00f, 0.00f, 0.00f),   // paved: skid marks only
            new Ground(0.86f, 0.60f, 0.20f, 0.58f, 0.30f, 0.50f, 0.85f, 65f, 1.10f, 1.00f, 0.50f, 0.60f),   // bare earth: tread prints, dust
            new Ground(0.62f, 0.85f, 1.10f, 0.85f, 0.45f, 0.85f, 1.00f, 85f, 1.30f, 0.00f, 1.00f, 1.00f),   // mud: dark deep tracks, flying clods
        };

        static readonly Color SnowShade = new Color(0.50f, 0.62f, 0.84f), Earth = new Color(0.34f, 0.25f, 0.16f), Rubber = new Color(0.05f, 0.05f, 0.055f);

        static readonly float Small = Mathf.Lerp(1f, Jeep.Size, 0.6f);     // dust and clods shrink with the jeep, though not all the way
        const int N = 448;                              // cross-sections kept per wheel
        const float RoadLift = 0.085f, GroundLift = 0.03f;

        [StructLayout(LayoutKind.Sequential)]
        struct Vtx
        {
            public Vector3 Pos, Nrm;
            public Vector4 Uv0, Uv1, Col;
        }

        sealed class Wheel
        {
            public int Head;
            public bool Live, Known, WasDown, InWater;
            public Vector3 Last, Side;
            public float U, HeadU, Wet, Dirt, Seed, SampleIn, PuffAcc, BitAcc, Air, SplashIn, SkidSeen;
            public Color DirtCol, Tint = Color.grey;
            public byte Kind = GroundKind.Paved;
        }

        Jeep jeep;
        EarthWorld world;
        Transform anchor;
        Mesh mesh;
        Material trailMat, puffMat, bitMat;
        NativeArray<Vtx> verts;
        bool dirty, made;
        float clock;
        readonly Wheel[] wheels = { new Wheel(), new Wheel(), new Wheel(), new Wheel() };
        ParticleSystem puffs, bits;
        ParticleSystem.Particle[] scratch;
        static readonly int TrailNow = Shader.PropertyToID("_TrailNow");

        public void Init(Jeep j)
        {
            jeep = j;
            anchor = new GameObject("Jeep tracks").transform;

            Shader ts = Shader.Find("MarsNeedsWater/EarthTrail");
            if (ts != null && ts.isSupported)
            {
                trailMat = new Material(ts);
                verts = new NativeArray<Vtx>(4 * N * 2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                mesh = new Mesh { name = "Jeep tracks" };
                mesh.MarkDynamic();
                mesh.SetVertexBufferParams(verts.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4));
                var idx = new ushort[4 * N * 6];
                int k = 0;
                for (int w = 0; w < 4; w++)
                    for (int s = 0; s < N; s++)
                    {
                        int a = (w * N + s) * 2, b = (w * N + (s + 1) % N) * 2;
                        idx[k++] = (ushort)a; idx[k++] = (ushort)b; idx[k++] = (ushort)(a + 1);
                        idx[k++] = (ushort)(a + 1); idx[k++] = (ushort)b; idx[k++] = (ushort)(b + 1);
                    }
                mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt16);
                mesh.SetIndexBufferData(idx, 0, 0, idx.Length);
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx.Length), MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetVertexBufferData(verts, 0, 0, verts.Length);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
                anchor.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = anchor.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = trailMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                made = true;
            }
            else Debug.LogWarning("EarthTrail shader missing: no tyre tracks");

            Shader ds = Shader.Find("MarsNeedsWater/EarthDust");
            if (ds != null && ds.isSupported)
            {
                puffMat = new Material(ds); puffMat.SetFloat("_Soft", 1f);
                bitMat = new Material(ds); bitMat.SetFloat("_Soft", 0.1f);
                puffs = MakeParticles("Jeep dust", puffMat, 700, -0.015f, true);
                bits = MakeParticles("Jeep clods", bitMat, 500, 1.5f, false);
                scratch = new ParticleSystem.Particle[700];
            }
            else Debug.LogWarning("EarthDust shader missing: no dust from the wheels");

            foreach (Wheel w in wheels) w.Seed = Random.value;
            EarthWorld.Shifted += OnShifted;
            EarthWorld.Jumped += OnJumped;
        }

        void OnDestroy()
        {
            EarthWorld.Shifted -= OnShifted;
            EarthWorld.Jumped -= OnJumped;
            if (verts.IsCreated) verts.Dispose();
            if (mesh != null) Destroy(mesh);
            if (trailMat != null) Destroy(trailMat);
            if (puffMat != null) Destroy(puffMat);
            if (bitMat != null) Destroy(bitMat);
            if (anchor != null) Destroy(anchor.gameObject);
        }

        ParticleSystem MakeParticles(string name, Material mat, int max, float gravity, bool puff)
        {
            var go = new GameObject(name);
            go.transform.SetParent(anchor, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;

            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      puff ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) }
                           : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            if (puff)
            {
                var size = ps.sizeOverLifetime; size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f, 0f, 2.6f), new Keyframe(0.3f, 0.85f, 0.8f, 0.8f), new Keyframe(1f, 1.3f, 0.4f, 0f)));
                var lim = ps.limitVelocityOverLifetime; lim.enabled = true;
                lim.drag = 1.8f; lim.multiplyDragByParticleSize = false; lim.multiplyDragByParticleVelocity = false;
                lim.limit = 1000f; lim.dampen = 0f;
            }
            var rot = ps.rotationOverLifetime; rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(puff ? -0.5f : -5f, puff ? 0.5f : 5f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>What each wheel stands on, for tests: "sand sand grass(wet 0.4) -" (a dash = off the ground or unknown).</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Wheel w in wheels)
            {
                if (!w.WasDown || !w.Known) { sb.Append("- "); continue; }
                sb.Append(GroundKind.Names[w.Kind]);
                if (w.InWater) sb.Append("(in water)");
                else if (w.Wet > 0.01f) sb.Append("(wet ").Append(w.Wet.ToString("F1")).Append(')');
                if (w.Dirt > 0.01f) sb.Append("(dirt ").Append(w.Dirt.ToString("F1")).Append(')');
                sb.Append(w.Live ? "* " : " ");
            }
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------ the world moving under us

        void OnShifted(Vector3 d)
        {
            if (anchor != null) anchor.position += d;
            foreach (Wheel w in wheels) w.Last += d;
            ShiftParticles(puffs, d);
            ShiftParticles(bits, d);
        }

        void ShiftParticles(ParticleSystem ps, Vector3 d)
        {
            if (ps == null) return;
            int n = ps.GetParticles(scratch);
            for (int i = 0; i < n; i++) scratch[i].position += d;
            ps.SetParticles(scratch, n);
        }

        void OnJumped()
        {
            if (made)
            {
                for (int i = 0; i < verts.Length; i++) { Vtx v = verts[i]; v.Col.w = 0f; verts[i] = v; }
                dirty = true;
            }
            foreach (Wheel w in wheels) { w.Live = false; w.Known = false; w.Wet = w.Dirt = 0f; }
            if (puffs != null) puffs.Clear();
            if (bits != null) bits.Clear();
        }

        // ------------------------------------------------------------ every frame

        void LateUpdate()
        {
            if (jeep == null || jeep.Body == null) return;
            if (world == null) world = FindFirstObjectByType<EarthWorld>();
            float dt = Time.deltaTime;
            clock += dt;
            Shader.SetGlobalFloat(TrailNow, clock);

            // far from where the tracks' numbers started: start them again here, so they stay exact
            Vector3 home = jeep.transform.position - anchor.position;
            if (made && home.sqrMagnitude > 3000f * 3000f)
            {
                for (int i = 0; i < verts.Length; i++) { Vtx v = verts[i]; v.Pos -= home; verts[i] = v; }
                anchor.position += home;
                home = Vector3.zero;
                dirty = true;
            }

            Vector3 vel = jeep.Body.isKinematic ? Vector3.zero : jeep.Body.linearVelocity;
            float speed = vel.magnitude;
            Vector3 fwd = jeep.transform.forward;
            for (int i = 0; i < 4; i++) StepWheel(i, wheels[i], dt, vel, speed, fwd);

            if (made && dirty)
            {
                mesh.SetVertexBufferData(verts, 0, 0, verts.Length, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers);
                mesh.bounds = new Bounds(home, Vector3.one * 3000f);
                dirty = false;
            }
        }

        void StepWheel(int i, Wheel w, float dt, Vector3 vel, float speed, Vector3 fwd)
        {
            bool down = jeep.WheelDown(i);
            if (!down)
            {
                w.Air += dt;
                w.WasDown = false;
                End(i, w);
                if (GroundGrip) { jeep.SurfGrip[i] = 1f; jeep.SurfLoose[i] = 0f; jeep.SurfDrag[i] = 0f; }
                return;
            }

            // where the tyre meets the ground, as drawn (the physics runs a step ahead of the picture)
            bool right = i % 2 == 1, front = i < 2;
            Vector3 centre = jeep.transform.TransformPoint(new Vector3(right ? Jeep.Track : -Jeep.Track, Jeep.Ground + Jeep.TyreR, front ? Jeep.FrontZ : Jeep.RearZ));
            Vector3 pos, nrm;
            RaycastHit h;
            bool terrain = jeep.WheelOnTerrain[i];
            if (Physics.Raycast(centre + Vector3.up * 0.9f, Vector3.down, out h, 2.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            { pos = h.point; nrm = h.normal; terrain = h.collider.name == "Ground"; }
            else { pos = jeep.WheelPoint[i]; nrm = jeep.WheelNormal[i]; }
            if (nrm.y < 0.2f) { End(i, w); return; }

            // what is the ground here? (looked up a few times a second, and at once after landing)
            w.SampleIn -= dt;
            if (w.SampleIn <= 0f || !w.WasDown)
            {
                w.SampleIn = Random.Range(0.06f, 0.11f);
                float gy; byte kind; Color32 tint;
                if (!terrain) { w.Known = true; w.Kind = GroundKind.Paved; w.Tint = new Color(0.45f, 0.45f, 0.45f); }
                else if (world != null && world.GroundInfo(pos, out gy, out kind, out tint)) { w.Known = true; w.Kind = kind < GroundKind.Count ? kind : GroundKind.Grass; w.Tint = tint; }
                else w.Known = false;
                float surface;
                w.InWater = jeep.WaterFn != null && jeep.WaterFn(pos, out surface) && surface > pos.y + 0.03f;
            }
            Ground g = Kinds[w.Known ? w.Kind : GroundKind.Paved];
            bool hard = g.Mark <= 0f;

            float moved = w.WasDown ? Vector3.Distance(pos, w.Last) : 0f;
            float slide = Mathf.Abs(jeep.WheelSlide[i]), roll = Mathf.Abs(jeep.WheelRoll[i]);
            float skid = Mathf.Clamp01(Mathf.Max((slide - 2.5f) / 5f, jeep.WheelSpin[i] * Mathf.Clamp01((roll + slide) / 4f + 0.4f)));
            w.SkidSeen = Mathf.Lerp(w.SkidSeen, skid, 1f - Mathf.Exp(-12f * dt));

            // tyres get wet in water and dry again over the next stretch; they pick up mud, snow and sand and shed them on hard ground
            if (w.InWater) { w.Wet = 1f; w.Dirt = 0f; }
            else w.Wet = Mathf.Max(0f, w.Wet - moved / 55f);
            if (!hard && w.Known && !w.InWater)
            {
                if (g.Carry > w.Dirt) { w.Dirt = Mathf.MoveTowards(w.Dirt, g.Carry, moved / 4f); w.DirtCol = Pressed(w, g, 0f) * 1.15f; }
            }
            else w.Dirt = Mathf.Max(0f, w.Dirt - moved / (28f + 20f * w.Dirt));

            if (GroundGrip)
            {
                float wetRoad = hard ? 1f - 0.15f * w.Wet : 1f;
                jeep.SurfGrip[i] = w.Known ? g.Grip * wetRoad : 1f;
                jeep.SurfLoose[i] = w.Known ? g.Loose : 0f;
                jeep.SurfDrag[i] = w.Known ? g.Drag : 0f;
            }

            if (Dust) Kick(i, w, g, pos, nrm, vel, speed, fwd, skid, dt);
            w.Air = 0f;

            if (!made || !Tracks || w.InWater) { End(i, w); w.Last = pos; w.WasDown = true; return; }

            // ---- the track this wheel leaves here
            float strength, rut, tread, life, dark = g.Dark;
            Color col;
            if (!hard && w.Known)
            {
                // soft ground: pressed, darker, rutted; a sliding tyre scrubs a wider, smoother swathe
                strength = Mathf.Clamp01(g.Mark + 0.2f * w.SkidSeen);
                rut = g.Rut; tread = g.Tread * (1f - 0.8f * w.SkidSeen); life = g.Life;
                col = Pressed(w, g, w.Wet);
            }
            else
            {
                // hard ground: nothing, unless the tyres are wet, dirty or sliding
                float wet = 0.55f * w.Wet, dirt = 0.62f * w.Dirt, rubber = 0.75f * Mathf.Clamp01(w.SkidSeen * 1.3f);
                strength = Mathf.Max(wet, Mathf.Max(dirt, rubber));
                rut = 0f;
                if (rubber >= wet && rubber >= dirt) { col = Rubber; tread = 0.1f; life = 50f; }
                else if (dirt >= wet) { col = w.DirtCol; tread = 1f; life = 45f; }
                else { col = w.Tint.linear * 0.45f; tread = 0.5f; life = 14f; }       // water dries
            }
            life *= Mathf.Max(TrackLife, 0.05f);
            if (strength < 0.03f) { End(i, w); w.Last = pos; w.WasDown = true; return; }

            // across the way it is going, as wide as the tyre is seen from that way (a sideways tyre draws a broad band)
            Vector3 dir = w.Live ? pos - CommittedPos(i, w) : vel;
            dir -= nrm * Vector3.Dot(dir, nrm);
            if (dir.sqrMagnitude < 0.0025f) { dir = Vector3.ProjectOnPlane(vel.sqrMagnitude > 0.04f ? vel : fwd, nrm); }
            if (dir.sqrMagnitude < 1e-6f) { w.Last = pos; w.WasDown = true; return; }
            dir.Normalize();
            Vector3 side = Vector3.Cross(nrm, dir);
            float cos = Mathf.Abs(Vector3.Dot(dir, Vector3.ProjectOnPlane(fwd, nrm).normalized));
            float half = 0.5f * (Jeep.TyreW * cos + 0.5f * Jeep.Size * Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos))) * g.Wide * (rut > 0.5f ? 1.15f : 1f);
            Vector3 at = pos + nrm * (terrain && !(w.Known && w.Kind == GroundKind.Paved) ? GroundLift : RoadLift);

            if (!w.Live)
            {
                // a new stroke: an invisible start, then the live end that follows the tyre
                w.Head = (w.Head + 1) % N;
                Put(i, w.Head, at, side, nrm, half, w.U, life, rut, tread, w.Seed, col, 0f);
                w.Head = (w.Head + 1) % N;
                w.Live = true;
                w.Side = side;
                w.Seed = Random.value;
            }
            else
            {
                Vector3 from = CommittedPos(i, w);
                float gone = Vector3.Distance(at, from);
                float step = Mathf.Clamp(0.22f + speed * 0.03f, 0.22f, 1.0f);
                bool turned = gone > 0.12f && Vector3.Dot(side, w.Side) < 0.994f;
                if (gone > 30f) { End(i, w); w.Last = pos; w.WasDown = true; return; }      // it was moved, not driven
                if (gone >= step || turned)
                {
                    w.U = w.HeadU;
                    w.Side = side;
                    w.Head = (w.Head + 1) % N;
                }
            }
            if (w.Side == Vector3.zero) w.Side = side;
            float uNow = w.U + Vector3.Distance(at, CommittedPos(i, w));
            w.HeadU = uNow;
            Put(i, w.Head, at, side, nrm, half, uNow, life, rut, tread, w.Seed, col, strength);
            // the ring closes behind the live end: the next slot sits on it, unseen, and the one after fades in
            int a = (w.Head + 1) % N, b = (w.Head + 2) % N;
            Put(i, a, at, side, nrm, half, uNow, life, rut, tread, w.Seed, col, 0f);
            Fade(i, b);
            dirty = true;
            w.Last = pos; w.WasDown = true;
        }

        /// <summary>The colour of this ground once a tyre has pressed it (linear).</summary>
        static Color Pressed(Wheel w, Ground g, float wet)
        {
            Color t = w.Tint.linear;
            Color c;
            if (w.Kind == GroundKind.Snow) c = Color.Lerp(t, SnowShade.linear, 0.6f) * 0.92f;                 // packed snow, blue in its own shade
            else if (w.Kind == GroundKind.Sand) c = t * (1f - g.Dark);
            else c = Color.Lerp(t, Earth.linear, 0.3f) * (1f - g.Dark);                                       // the earth shows through
            c *= 1f - 0.3f * wet;
            c.a = 1f;
            return c;
        }

        /// <summary>The middle of the last cross-section laid down for good (the one before the live end), in the world.</summary>
        Vector3 CommittedPos(int wheel, Wheel w)
        {
            int s = (w.Head + N - 1) % N, b = (wheel * N + s) * 2;
            return (verts[b].Pos + verts[b + 1].Pos) * 0.5f + anchor.position;
        }

        void Put(int wheel, int s, Vector3 at, Vector3 side, Vector3 nrm, float half, float u, float life, float rut, float tread, float seed, Color col, float strength)
        {
            int b = (wheel * N + s) * 2;
            Vector3 local = at - anchor.position;
            var uv1 = new Vector4(rut, tread, seed, half);
            var c = new Vector4(col.r, col.g, col.b, strength);
            verts[b] = new Vtx { Pos = local - side * half, Nrm = nrm, Uv0 = new Vector4(u / Jeep.Size, -1f, clock, life), Uv1 = uv1, Col = c };
            verts[b + 1] = new Vtx { Pos = local + side * half, Nrm = nrm, Uv0 = new Vector4(u / Jeep.Size, 1f, clock, life), Uv1 = uv1, Col = c };
        }

        void Fade(int wheel, int s)
        {
            int b = (wheel * N + s) * 2;
            Vtx v = verts[b]; v.Col.w = 0f; verts[b] = v;
            v = verts[b + 1]; v.Col.w = 0f; verts[b + 1] = v;
        }

        /// <summary>The wheel left the ground (or the ground stopped taking marks): the track fades out over its last stretch.</summary>
        void End(int wheel, Wheel w)
        {
            if (!w.Live) return;
            w.Live = false;
            Fade(wheel, w.Head);
            dirty = true;
        }

        // ------------------------------------------------------------ dust, snow, mud, smoke, spray

        void Kick(int i, Wheel w, Ground g, Vector3 pos, Vector3 nrm, Vector3 vel, float speed, Vector3 fwd, float skid, float dt)
        {
            if (puffs == null) return;
            bool rear = i >= 2;
            Vector3 side = Vector3.Cross(nrm, fwd).normalized * (i % 2 == 1 ? 1f : -1f);      // outwards from the jeep
            float spinning = jeep.WheelSpin[i], slide = Mathf.Abs(jeep.WheelSlide[i]);
            float dry = 1f - 0.85f * w.Wet;

            // through shallow water: spray thrown up and out
            if (w.InWater)
            {
                w.SplashIn -= dt;
                if (speed > 1.5f && w.SplashIn <= 0f && EarthWater.I != null)
                {
                    float surface;
                    if (jeep.WaterFn(pos, out surface) && surface - pos.y < 1.1f)
                    {
                        Vector3 at = new Vector3(pos.x, surface + 0.05f, pos.z);
                        EarthWater.I.Spray(at, vel * 0.35f + Vector3.up * (1.6f + speed * 0.14f) + side * (1f + speed * 0.12f), Mathf.Clamp(1 + (int)(speed * 0.25f), 1, 5), 0.5f, 0.13f);
                    }
                    w.SplashIn = Random.Range(0.05f, 0.11f);
                }
                return;
            }
            if (!w.Known) return;

            Color tint = w.Tint;
            Color dust = w.Kind == GroundKind.Snow ? new Color(0.96f, 0.97f, 1f)
                       : w.Kind == GroundKind.Paved ? new Color(0.84f, 0.84f, 0.86f)
                       : Color.Lerp(tint, new Color(0.88f, 0.82f, 0.70f), 0.45f);
            float amount = Mathf.Max(DustAmount, 0f);

            // landing: a burst from under the wheel
            if (!w.WasDown && w.Air > 0.25f && g.Puffs + g.Bits > 0.05f)
            {
                int n = Mathf.RoundToInt(Random.Range(3f, 6f) * Mathf.Clamp01(g.Puffs + 0.3f) * amount);
                for (int k = 0; k < n; k++)
                {
                    Vector3 out2 = Quaternion.AngleAxis(Random.Range(0f, 360f), nrm) * side;
                    Puff(pos + nrm * 0.15f, vel * 0.25f + out2 * Random.Range(1.5f, 4f) + nrm * Random.Range(0.3f, 1.2f), Random.Range(0.8f, 1.5f), Random.Range(0.6f, 1.2f), dust, 0.6f * dry);
                }
            }

            // puffs: more with speed, a lot more when the tyre slides or spins. On roads only tyre smoke, when sliding hard.
            float puffRate = w.Kind == GroundKind.Paved
                ? (skid > 0.35f ? 16f * (skid - 0.2f) : 0f)
                : g.Puffs * dry * (Mathf.Clamp01(speed / 22f) * (rear ? 9f : 4f) + skid * 22f + spinning * 14f) * Mathf.Clamp01(speed / 2.5f + spinning);
            w.PuffAcc += puffRate * amount * dt;
            while (w.PuffAcc >= 1f)
            {
                w.PuffAcc -= Random.Range(0.7f, 1.3f);
                float big = Mathf.Lerp(0.45f, 1.25f, Mathf.Clamp01(speed / 28f)) * Random.Range(0.7f, 1.35f) * (w.Kind == GroundKind.Paved ? 0.8f : 1f);
                Vector3 v = vel * Random.Range(0.08f, 0.25f) + nrm * Random.Range(0.3f, 1.3f) + side * Random.Range(-0.4f, 1.2f) - fwd * Random.Range(0f, 1.5f) * Mathf.Sign(Vector3.Dot(vel, fwd));
                float alpha = w.Kind == GroundKind.Paved ? 0.35f : Mathf.Lerp(0.35f, 0.62f, g.Puffs) * Random.Range(0.75f, 1f);
                Puff(pos + nrm * Random.Range(0.05f, 0.3f) + side * Random.Range(-0.15f, 0.15f) - fwd * Random.Range(0f, 0.4f), v, big, Random.Range(0.7f, 1.6f), dust, alpha);
            }

            // clods and tufts flung back off the tread: when spinning, sliding or driving hard on soft ground
            if (bits == null || g.Bits <= 0f) return;
            float bitRate = g.Bits * (spinning * 30f + skid * 18f + (rear ? 1f : 0.4f) * Mathf.Clamp01(speed / 15f) * 7f * g.Loose) * Mathf.Clamp01(speed / 2f + spinning);
            w.BitAcc += bitRate * amount * dt;
            float sign = Vector3.Dot(vel, fwd) >= 0f ? -1f : 1f;                 // thrown out behind the way it is going
            Color clod = w.Kind == GroundKind.Snow ? new Color(0.95f, 0.96f, 1f) : Color.Lerp(tint, Earth, w.Kind == GroundKind.Sand ? 0.1f : 0.45f) * (w.Kind == GroundKind.Mud ? 0.75f : 0.9f);
            clod.a = 1f;
            while (w.BitAcc >= 1f)
            {
                w.BitAcc -= Random.Range(0.7f, 1.3f);
                float throwV = Mathf.Clamp(speed * 0.25f + spinning * 5f + slide * 0.3f, 1.5f, 9f);
                Vector3 v = vel * 0.55f + fwd * sign * throwV * Random.Range(0.4f, 1f) + nrm * throwV * Random.Range(0.35f, 0.9f) + side * Random.Range(-1.2f, 1.6f);
                var e = new ParticleSystem.EmitParams
                {
                    position = pos + nrm * 0.1f + fwd * sign * 0.25f + side * Random.Range(-0.15f, 0.15f),
                    velocity = v,
                    startSize = Random.Range(0.05f, 0.17f) * (w.Kind == GroundKind.Mud ? 1.25f : 1f) * Small,
                    startLifetime = Random.Range(0.45f, 1.0f),
                    startColor = clod * Random.Range(0.8f, 1.1f),
                    rotation = Random.Range(0f, 360f),
                };
                bits.Emit(e, 1);
            }
        }

        void Puff(Vector3 at, Vector3 velocity, float size, float life, Color c, float alpha)
        {
            c *= Random.Range(0.92f, 1.06f);
            c.a = alpha;
            var e = new ParticleSystem.EmitParams
            {
                position = at, velocity = velocity, startSize = size * Small, startLifetime = life, startColor = c, rotation = Random.Range(0f, 360f),
            };
            puffs.Emit(e, 1);
        }
    }
}
