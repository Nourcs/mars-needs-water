using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Real 3D grass, reeds and stones on the ground round the player, chosen from what the ground is made of there
    /// (GroundKind): thick lawn on grass, tall straw on dry grass, reeds in marsh, sparse tufts on bare earth,
    /// stones on rock. Blades sway in gusts of wind and bend away from the player. Everything is drawn in two GPU
    /// calls from buffers rebuilt only when the player crosses into new ground.
    /// </summary>
    public sealed class EarthGrass : MonoBehaviour
    {
        const float Cell = 8f;          // ground is filled in squares this wide (game metres)
        const float Range = 36f;        // out to here
        const float Step = 0.4f;        // a candidate spot every this many metres
        const int PerCell = (int)(Cell / Step);

        // per kind: chance of a clump at a candidate spot, blade height min / max (game metres), and chance of a stone
        static readonly float[] ClumpChance = { 1.0f, 0.75f, 0.12f, 0.45f, 0.03f, 0.04f, 0f, 0f, 0.08f, 0.55f };
        static readonly float[] HeightMin = { 0.16f, 0.30f, 0.22f, 0.32f, 0.35f, 0.12f, 0f, 0f, 0.12f, 0.6f };
        static readonly float[] HeightMax = { 0.40f, 0.70f, 0.48f, 0.62f, 0.65f, 0.25f, 0f, 0f, 0.30f, 1.15f };
        static readonly float[] StoneChance = { 0.003f, 0.006f, 0.012f, 0.002f, 0.004f, 0.12f, 0.004f, 0f, 0.04f, 0f };

        struct Inst                      // 32 bytes, matches EarthGrass.shader
        {
            public Vector3 Pos; public float Yaw; public float Height; public float Width; public uint Tint; public float Rand;
        }

        sealed class Chunk { public List<Inst> Grass = new List<Inst>(), Stones = new List<Inst>(); }

        EarthWorld world;
        readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
        readonly List<long> drop = new List<long>();
        Mesh clump, stone;
        Material grassMat, stoneMat;
        GraphicsBuffer grassBuf, stoneBuf;
        int grassCount, stoneCount;
        bool dirty;
        Inst[] scratch = new Inst[0];

        public void Init(EarthWorld w)
        {
            world = w;
            Shader sh = Shader.Find("MarsNeedsWater/EarthGrass");
            if (sh == null || !sh.isSupported || !SystemInfo.supportsInstancing) { Debug.LogWarning("EarthGrass shader missing: no 3D grass"); enabled = false; return; }
            grassMat = new Material(sh);
            grassMat.SetFloat("_Wind", 1f); grassMat.SetFloat("_UpBlend", 0.65f); grassMat.SetFloat("_Stone", 0f);
            stoneMat = new Material(sh);
            stoneMat.SetFloat("_Wind", 0f); stoneMat.SetFloat("_UpBlend", 0.1f); stoneMat.SetFloat("_Stone", 1f);
            stoneMat.SetFloat("_Cull", (float)CullMode.Back);
            clump = MakeClump();
            stone = MakeStone();
            EarthWorld.Shifted += OnShifted;
            EarthWorld.Jumped += OnJumped;
        }

        void OnDestroy()
        {
            EarthWorld.Shifted -= OnShifted;
            EarthWorld.Jumped -= OnJumped;
            if (grassBuf != null) grassBuf.Release();
            if (stoneBuf != null) stoneBuf.Release();
            if (clump != null) Destroy(clump);
            if (stone != null) Destroy(stone);
            if (grassMat != null) Destroy(grassMat);
            if (stoneMat != null) Destroy(stoneMat);
        }

        void OnShifted(Vector3 d)
        {
            foreach (Chunk c in chunks.Values)
            {
                Move(c.Grass, d);
                Move(c.Stones, d);
            }
            dirty = true;
        }

        static void Move(List<Inst> l, Vector3 d)
        {
            for (int i = 0; i < l.Count; i++) { Inst x = l[i]; x.Pos += d; l[i] = x; }
        }

        void OnJumped() { chunks.Clear(); dirty = true; }

        static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

        void Update()
        {
            Transform pt = world != null ? world.PlayerTransform : null;
            if (pt == null) return;
            Vector3 p = pt.position;
            Vector2 off = EarthGround.Offset;
            // squares are counted on the ground itself, so they stay the same when the world re-centres
            float ax = p.x + off.x, az = p.z + off.y;
            int cx = Mathf.FloorToInt(ax / Cell), cz = Mathf.FloorToInt(az / Cell);
            int r = Mathf.CeilToInt(Range / Cell);

            // flying high or driving fast: no grass to see
            float ground;
            bool near = world.Ground(p, out ground, true) && p.y - ground < 60f;

            drop.Clear();
            foreach (long k in chunks.Keys)
            {
                int kx = (int)(k >> 32), kz = (int)(uint)k;
                if (!near || Mathf.Abs(kx - cx) > r + 1 || Mathf.Abs(kz - cz) > r + 1) drop.Add(k);
            }
            foreach (long k in drop) chunks.Remove(k);
            if (drop.Count > 0) dirty = true;

            if (near)
            {
                float start = Time.realtimeSinceStartup;
                // nearest squares first
                for (int ring = 0; ring <= r && Time.realtimeSinceStartup - start < 0.003f; ring++)
                    for (int dz = -ring; dz <= ring; dz++)
                        for (int dx = -ring; dx <= ring; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                            long k = Key(cx + dx, cz + dz);
                            if (chunks.ContainsKey(k)) continue;
                            float ccx = (cx + dx + 0.5f) * Cell - ax, ccz = (cz + dz + 0.5f) * Cell - az;
                            if (ccx * ccx + ccz * ccz > (Range + Cell) * (Range + Cell)) continue;
                            if (Time.realtimeSinceStartup - start > 0.003f) break;
                            Chunk c = Build(cx + dx, cz + dz, off, p.y);
                            if (c != null) { chunks[k] = c; dirty = true; }
                        }
            }

            if (dirty) { dirty = false; Upload(); }

            Shader.SetGlobalVector("_GrassPlayer", p);
            Shader.SetGlobalFloat("_GrassRange", Range);
            var bounds = new Bounds(p, new Vector3(Range * 2.5f, 400f, Range * 2.5f));
            if (grassCount > 0) Graphics.DrawMeshInstancedProcedural(clump, 0, grassMat, bounds, grassCount, null, ShadowCastingMode.Off, true, 0);
            if (stoneCount > 0) Graphics.DrawMeshInstancedProcedural(stone, 0, stoneMat, bounds, stoneCount, null, ShadowCastingMode.On, true, 0);
        }

        static uint Hash(int a, int b, int c)
        {
            uint h = (uint)a * 0x8da6b343u ^ (uint)b * 0xd8163841u ^ (uint)c * 0xcb1ab31fu;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return h;
        }

        static float F(uint h, int shift) { return ((h >> shift) & 1023) / 1023f; }

        /// <summary>Fills one square, or returns null if the ground there is not loaded yet.</summary>
        Chunk Build(int cx, int cz, Vector2 off, float y0)
        {
            var c = new Chunk();
            int missing = 0;
            for (int j = 0; j < PerCell; j++)
                for (int i = 0; i < PerCell; i++)
                {
                    int gi = cx * PerCell + i, gj = cz * PerCell + j;
                    uint h = Hash(gi, gj, 1), h2 = Hash(gi, gj, 2);
                    float ax = (gi + F(h, 0)) * Step, az = (gj + F(h, 10)) * Step;
                    var at = new Vector3(ax - off.x, y0, az - off.y);
                    float y; byte kind; Color32 tint;
                    if (!world.GroundInfo(at, out y, out kind, out tint)) { missing++; continue; }
                    if (kind >= GroundKind.Count) continue;
                    float roll = F(h, 20);
                    if (roll < ClumpChance[kind])
                    {
                        float hgt = Mathf.Lerp(HeightMin[kind], HeightMax[kind], F(h2, 0));
                        // grass a little greener and brighter than the ground it grows from; straw paler
                        Color tc = tint;
                        if (kind == GroundKind.Grass || kind == GroundKind.Farm || kind == GroundKind.Forest) tc = new Color(tc.r * 0.95f, tc.g * 1.08f, tc.b * 0.85f);
                        else if (kind == GroundKind.DryGrass || kind == GroundKind.Sand || kind == GroundKind.Dirt) tc = Color.Lerp(tc, new Color(0.72f, 0.64f, 0.42f), 0.5f);
                        else if (kind == GroundKind.Mud) tc = Color.Lerp(tc, new Color(0.35f, 0.42f, 0.2f), 0.6f);
                        else if (kind == GroundKind.Rock) tc = Color.Lerp(tc, new Color(0.45f, 0.5f, 0.3f), 0.6f);
                        float v = 0.85f + 0.3f * F(h2, 10);
                        c.Grass.Add(new Inst
                        {
                            Pos = new Vector3(at.x, y - 0.02f, at.z), Yaw = F(h2, 20) * 6.2832f, Height = hgt,
                            Width = kind == GroundKind.Mud ? 0.8f : kind == GroundKind.Grass ? 1.1f : 1f,
                            Tint = Pack(tc * v, kind), Rand = F(h, 5)
                        });
                    }
                    if (F(h2, 5) < StoneChance[kind])
                    {
                        uint h3 = Hash(gi, gj, 3);
                        float size = 0.05f + 0.35f * Mathf.Pow(F(h3, 0), 3f);
                        if (kind == GroundKind.Rock) size *= 1.6f;
                        Color sc = Color.Lerp(new Color(0.48f, 0.46f, 0.42f), tint, 0.35f) * (0.8f + 0.35f * F(h3, 10));
                        c.Stones.Add(new Inst
                        {
                            Pos = new Vector3(at.x, y - size * 0.35f, at.z), Yaw = F(h3, 20) * 6.2832f, Height = size,
                            Width = 0.8f + 0.6f * F(h3, 5), Tint = Pack(sc, kind), Rand = F(h3, 15)
                        });
                    }
                }
            // wait for the ground to load rather than leave a bare square
            if (missing > PerCell * PerCell / 4) return null;
            return c;
        }

        static uint Pack(Color c, byte kind)
        {
            uint r = (uint)Mathf.Clamp(c.r * 255f, 0, 255), g = (uint)Mathf.Clamp(c.g * 255f, 0, 255), b = (uint)Mathf.Clamp(c.b * 255f, 0, 255);
            return r | (g << 8) | (b << 16) | ((uint)kind << 24);
        }

        void Upload()
        {
            grassCount = Fill(ref grassBuf, true, grassMat);
            stoneCount = Fill(ref stoneBuf, false, stoneMat);
        }

        int Fill(ref GraphicsBuffer buf, bool grass, Material mat)
        {
            int n = 0;
            foreach (Chunk c in chunks.Values) n += grass ? c.Grass.Count : c.Stones.Count;
            if (n == 0) return 0;
            if (scratch.Length < n) scratch = new Inst[Mathf.NextPowerOfTwo(n)];
            int o = 0;
            foreach (Chunk c in chunks.Values)
            {
                List<Inst> l = grass ? c.Grass : c.Stones;
                l.CopyTo(scratch, o);
                o += l.Count;
            }
            if (buf == null || buf.count < n)
            {
                if (buf != null) buf.Release();
                buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(n), 32);
                mat.SetBuffer("_Instances", buf);
            }
            buf.SetData(scratch, 0, 0, n);
            return n;
        }

        // ------------------------------------------------------------ meshes

        /// <summary>A clump of 14 tapered, curved blades, 1 unit tall (scaled per clump). uv: x across the blade, y up it.</summary>
        static Mesh MakeClump()
        {
            var v = new List<Vector3>(); var nn = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var rnd = new System.Random(7);
            const int Blades = 14, Segs = 4;
            for (int b = 0; b < Blades; b++)
            {
                float a = (float)(rnd.NextDouble() * Mathf.PI * 2), d = (float)Mathf.Sqrt((float)rnd.NextDouble()) * 0.16f;
                var root = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                float face = (float)(rnd.NextDouble() * Mathf.PI * 2);
                var side = new Vector3(Mathf.Cos(face), 0, Mathf.Sin(face));
                var normal = new Vector3(-side.z, 0, side.x);
                // lean outwards a bit, and curve over towards the tip
                Vector3 lean = (root.sqrMagnitude > 1e-6f ? root.normalized : normal) * (0.15f + 0.35f * (float)rnd.NextDouble());
                lean += normal * (float)(rnd.NextDouble() - 0.5) * 0.3f;
                float h = 0.7f + 0.3f * (float)rnd.NextDouble();
                float w = 0.022f + 0.016f * (float)rnd.NextDouble();
                int first = v.Count;
                for (int s = 0; s <= Segs; s++)
                {
                    float t = (float)s / Segs;
                    Vector3 c = root + Vector3.up * (t * h) + lean * (t * t) * h;
                    float ww = s == Segs ? 0f : w * (1f - t * 0.85f);
                    v.Add(c - side * ww); nn.Add(normal); uv.Add(new Vector2(-1, t));
                    if (s == Segs) break;
                    v.Add(c + side * ww); nn.Add(normal); uv.Add(new Vector2(1, t));
                }
                for (int s = 0; s < Segs - 1; s++)
                {
                    int a0 = first + s * 2;
                    tri.Add(a0); tri.Add(a0 + 2); tri.Add(a0 + 1);
                    tri.Add(a0 + 1); tri.Add(a0 + 2); tri.Add(a0 + 3);
                }
                int l = first + (Segs - 1) * 2;
                tri.Add(l); tri.Add(l + 2); tri.Add(l + 1);
            }
            var m = new Mesh { name = "Grass clump" };
            m.SetVertices(v); m.SetNormals(nn); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.bounds = new Bounds(Vector3.up * 0.5f, Vector3.one * 2f);
            return m;
        }

        /// <summary>A lumpy, flattish stone about 1 unit across.</summary>
        static Mesh MakeStone()
        {
            // an icosahedron split once, pushed in and out at random
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var p = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var mid = new Dictionary<long, int>();
            var tris = new List<int>();
            System.Func<int, int, int> Mid = (a, b) =>
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                int idx;
                if (mid.TryGetValue(k, out idx)) return idx;
                p.Add((p[a] + p[b]) * 0.5f);
                mid[k] = p.Count - 1;
                return p.Count - 1;
            };
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            var rnd = new System.Random(3);
            for (int i = 0; i < p.Count; i++)
            {
                Vector3 d = p[i].normalized;
                float r = 0.5f * (0.8f + 0.35f * (float)rnd.NextDouble());
                p[i] = new Vector3(d.x * r * 1.1f, d.y * r * 0.6f, d.z * r * 0.9f);
            }
            // flat shading: every face its own corners
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var ti = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = p[tris[i]], b = p[tris[i + 1]], c = p[tris[i + 2]];
                Vector3 nr = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(nr, a + b + c) < 0) { Vector3 tmp = b; b = c; c = tmp; nr = -nr; }
                for (int k = 0; k < 3; k++) { ti.Add(v.Count + k); n.Add(Vector3.Lerp(nr, (k == 0 ? a : k == 1 ? b : c).normalized, 0.4f).normalized); uv.Add(new Vector2(0, 0.5f)); }
                v.Add(a); v.Add(b); v.Add(c);
            }
            var m = new Mesh { name = "Stone" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(ti, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
