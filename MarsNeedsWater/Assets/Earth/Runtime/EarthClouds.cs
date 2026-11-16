using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Clouds: solid, chunky puffs that drift on the wind above the player and throw moving shadows on land and
    /// water. A few dozen shapes are modelled at random when the game starts (clusters of lumpy balls on a flat
    /// base); each cloud in the sky is one of them turned, stretched and sized differently, so no two look alike.
    /// The same layout is drawn into a picture seen from above, which every Earth shader reads to shade the
    /// ground under a cloud (EarthToon.hlsl, CloudShade).
    /// </summary>
    public sealed class EarthClouds : MonoBehaviour
    {
        public static float Field = 6000f;        // the layout repeats after this far (game metres); nothing nearer does
        public static int Count = 95;
        public static float Height = 340f;        // above the ground under the player
        public static Vector2 Wind = new Vector2(7.5f, 3.2f);   // game metres a second: fast enough to watch them go
        public static float ShadowStrength = 0.78f;

        const int Shapes = 14, MapSize = 512;

        struct Ball { public Vector3 C; public float R; }
        struct Cloud { public Vector2 At; public float Up, Yaw; public Vector3 Size; public int Shape; }

        EarthWorld world;
        Mesh[] meshes;
        List<Ball>[] balls;
        Cloud[] clouds;
        Material material;
        Texture2D map;
        List<Matrix4x4>[] batches;
        Matrix4x4[] buffer = new Matrix4x4[1023];
        float baseY = float.NaN;
        Light sun;

        static readonly int IdOrigin = Shader.PropertyToID("_CloudOrigin"), IdSun = Shader.PropertyToID("_CloudSun"), IdMap = Shader.PropertyToID("_CloudMap");

        public void Init(EarthWorld w)
        {
            world = w;
            Shader sh = Resources.Load<Shader>("Earth/Shaders/EarthCloud");
            if (sh == null || !sh.isSupported) { enabled = false; return; }
            material = new Material(sh) { name = "Earth cloud", enableInstancing = true };

            var rng = new System.Random(20261115);
            meshes = new Mesh[Shapes]; balls = new List<Ball>[Shapes]; batches = new List<Matrix4x4>[Shapes];
            for (int i = 0; i < Shapes; i++) { balls[i] = MakeShape(rng, i); meshes[i] = MakeMesh(balls[i], rng); batches[i] = new List<Matrix4x4>(); }

            clouds = new Cloud[Count];
            for (int i = 0; i < Count; i++)
            {
                // sizes from small wisps to a few great banks
                float r = (float)rng.NextDouble();
                float size = Mathf.Lerp(110f, 400f, r * r) * (rng.NextDouble() < 0.12 ? 1.5f : 1f);
                clouds[i] = new Cloud
                {
                    At = new Vector2((float)rng.NextDouble() * Field, (float)rng.NextDouble() * Field),
                    Up = ((float)rng.NextDouble() - 0.5f) * 90f,
                    Yaw = (float)rng.NextDouble() * 360f,
                    Size = new Vector3(size * Mathf.Lerp(0.8f, 1.5f, (float)rng.NextDouble()), size * Mathf.Lerp(0.7f, 1.15f, (float)rng.NextDouble()), size * Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble())),
                    Shape = rng.Next(Shapes),
                };
            }
            MakeMap();
        }

        void OnDestroy()
        {
            Shader.SetGlobalVector(IdSun, Vector4.zero);
            if (meshes != null) foreach (Mesh m in meshes) if (m != null) Destroy(m);
            if (map != null) Destroy(map);
            if (material != null) Destroy(material);
        }

        // ------------------------------------------------------------ shapes

        // A cloud, one unit across: a row of balls, big in the middle, with a few heaped on top and odd ones out.
        static List<Ball> MakeShape(System.Random rng, int kind)
        {
            var list = new List<Ball>();
            System.Func<float> R = () => (float)rng.NextDouble();
            int row = 2 + rng.Next(4);                      // 2 to 5 along the base
            float span = 0.13f * row;            // balls overlap well: one body, not a string of beads
            for (int i = 0; i < row; i++)
            {
                float t = row == 1 ? 0f : i / (row - 1f) * 2f - 1f;
                float r = Mathf.Lerp(0.34f, 0.2f, t * t) * Mathf.Lerp(0.8f, 1.25f, R());
                list.Add(new Ball { C = new Vector3(t * span + (R() - 0.5f) * 0.08f, r * 0.55f, (R() - 0.5f) * 0.22f), R = r });
            }
            int top = kind % 3 == 0 ? 0 : 1 + rng.Next(3);      // some stay low and flat, others tower
            for (int i = 0; i < top; i++)
            {
                float r = Mathf.Lerp(0.16f, 0.3f, R());
                list.Add(new Ball { C = new Vector3((R() - 0.5f) * span * 1.1f, 0.24f + R() * 0.2f + (kind % 4 == 1 ? 0.15f : 0f), (R() - 0.5f) * 0.2f), R = r });
            }
            int side = rng.Next(3);
            for (int i = 0; i < side; i++)
            {
                float r = Mathf.Lerp(0.1f, 0.18f, R());
                float a = R() * Mathf.PI * 2f;
                list.Add(new Ball { C = new Vector3(Mathf.Cos(a) * span * 1.05f, r * 0.6f, Mathf.Sin(a) * 0.3f), R = r });
            }
            return list;
        }

        static Mesh MakeMesh(List<Ball> list, System.Random rng)
        {
            const int Around = 18, Rings = 11;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var idx = new List<int>();
            Vector3 mid = Vector3.zero;
            foreach (Ball b in list) mid += b.C;
            mid /= list.Count;
            foreach (Ball b in list)
            {
                int start = v.Count;
                float seed = (float)rng.NextDouble() * 50f;
                for (int j = 0; j <= Rings; j++)
                {
                    float lat = Mathf.PI * j / Rings - Mathf.PI * 0.5f;
                    for (int i = 0; i <= Around; i++)
                    {
                        float lon = 2f * Mathf.PI * i / Around;
                        var d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                        // lumps: the ball swells and dips a little, the same at both ends of the seam
                        float lump = 1f + 0.16f * (Mathf.PerlinNoise(seed + d.x * 1.9f + d.y * 1.3f, seed * 0.7f + d.z * 1.9f - d.y * 1.1f) - 0.5f) * 2f;
                        Vector3 p = b.C + d * (b.R * lump);
                        // the flat base all clouds share
                        if (p.y < 0.03f) p.y = 0.03f + (p.y - 0.03f) * 0.12f;
                        v.Add(p);
                        n.Add(Vector3.Normalize(d * 0.7f + (p - mid).normalized * 0.3f + (p.y < 0.04f ? Vector3.down * 0.8f : Vector3.zero)));
                    }
                }
                for (int j = 0; j < Rings; j++)
                    for (int i = 0; i < Around; i++)
                    {
                        int a = start + j * (Around + 1) + i, c = a + Around + 1;
                        idx.Add(a); idx.Add(c); idx.Add(a + 1);
                        idx.Add(a + 1); idx.Add(c); idx.Add(c + 1);
                    }
            }
            var m = new Mesh { name = "Cloud" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(idx, 0);
            m.bounds = new Bounds(new Vector3(0f, 0.3f, 0f), new Vector3(3f, 2f, 3f));
            return m;
        }

        // The layout seen from above, as how much shade each spot gets: read by every Earth shader.
        void MakeMap()
        {
            var a = new float[MapSize * MapSize];
            float perMetre = MapSize / Field;
            foreach (Cloud c in clouds)
            {
                Quaternion q = Quaternion.Euler(0f, c.Yaw, 0f);
                foreach (Ball b in balls[c.Shape])
                {
                    Vector3 p = q * Vector3.Scale(b.C, c.Size);
                    float r = b.R * (c.Size.x + c.Size.z) * 0.5f * 1.05f;
                    float cx = (c.At.x + p.x) * perMetre, cy = (c.At.y + p.z) * perMetre, rr = r * perMetre;
                    int x0 = Mathf.FloorToInt(cx - rr * 1.15f - 1), x1 = Mathf.CeilToInt(cx + rr * 1.15f + 1), y0 = Mathf.FloorToInt(cy - rr * 1.15f - 1), y1 = Mathf.CeilToInt(cy + rr * 1.15f + 1);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / rr;
                            float s = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.8f, 1.12f, d));
                            int k = ((y % MapSize + MapSize) % MapSize) * MapSize + (x % MapSize + MapSize) % MapSize;
                            if (s > a[k]) a[k] = s;
                        }
                }
            }
            var px = new Color32[a.Length];
            for (int i = 0; i < a.Length; i++) { byte s = (byte)(a[i] * 255f); px[i] = new Color32(s, s, s, 255); }
            map = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false, true) { name = "Cloud shadows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            map.SetPixels32(px); map.Apply(false, true);
            Shader.SetGlobalTexture(IdMap, map);
        }

        // ------------------------------------------------------------ every frame

        void LateUpdate()
        {
            if (clouds == null || world == null) return;
            Transform player = world.PlayerTransform;
            Camera cam = Camera.main;
            if (player == null || cam == null) return;

            // the cloud base keeps its height above the land the player is over, rising slowly over mountains
            float ground;
            if (!world.Ground(player.position, out ground)) ground = 0f;
            float want = Mathf.Max(ground, 0f) + Height;
            baseY = float.IsNaN(baseY) || Mathf.Abs(want - baseY) > 400f ? want : Mathf.Lerp(baseY, want, 1f - Mathf.Exp(-0.25f * Time.deltaTime));

            Vector2 off = EarthGround.Offset;
            Vector2 drift = Wind * Time.time;
            Vector3 eye = cam.transform.position;
            float reach = Field * 1.45f;
            for (int i = 0; i < Shapes; i++) batches[i].Clear();
            foreach (Cloud c in clouds)
            {
                // where this cloud is now, in the copy of the layout nearest the camera
                float x = c.At.x + drift.x - off.x, z = c.At.y + drift.y - off.y;
                x = eye.x + Mathf.Repeat(x - eye.x + Field * 0.5f, Field) - Field * 0.5f;
                z = eye.z + Mathf.Repeat(z - eye.z + Field * 0.5f, Field) - Field * 0.5f;
                Quaternion q = Quaternion.Euler(0f, c.Yaw, 0f);
                for (int kx = -1; kx <= 1; kx++)
                    for (int kz = -1; kz <= 1; kz++)
                    {
                        float px = x + kx * Field, pz = z + kz * Field;
                        float dx = px - eye.x, dz = pz - eye.z;
                        if (dx * dx + dz * dz > reach * reach) continue;
                        batches[c.Shape].Add(Matrix4x4.TRS(new Vector3(px, baseY + c.Up, pz), q, c.Size));
                    }
            }
            var rp = new RenderParams(material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(eye, Vector3.one * (reach * 4f)) };
            for (int i = 0; i < Shapes; i++)
            {
                List<Matrix4x4> b = batches[i];
                for (int s = 0; s < b.Count; s += 1023)
                {
                    int nn = Mathf.Min(1023, b.Count - s);
                    b.CopyTo(s, buffer, 0, nn);
                    Graphics.RenderMeshInstanced(rp, meshes[i], 0, buffer, nn);
                }
            }

            // for the shadows: where the layout is, how high, and which way the sun slants
            if (sun == null) foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            float sy = Mathf.Max(toSun.y, 0.2f);
            Shader.SetGlobalTexture(IdMap, map);
            Shader.SetGlobalVector(IdOrigin, new Vector4(off.x - drift.x, off.y - drift.y, 0f, baseY + 20f));
            Shader.SetGlobalVector(IdSun, new Vector4(toSun.x / sy, toSun.z / sy, ShadowStrength, 1f / Field));
        }
    }
}
