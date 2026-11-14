using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The player's car: an open-topped off-road 4x4, built in code. Boxy tub and short bonnet, flared arches,
    /// a tubular roll bar, a fold-flat windscreen, chunky treaded tyres on a raised chassis, a spare on the back
    /// and a snorkel up the right pillar. Every face takes its colour from a small palette texture with slight
    /// per-patch variations of each colour, so big panels look worn rather than flat, with nothing repeating.
    /// Local space: +z forward, y up, the origin at the player's centre, the ground 0.85 below it.
    /// </summary>
    public sealed class Jeep : MonoBehaviour
    {
        public const float Ground = -0.85f;      // the bottom of the tyres, where the character controller stands
        public const float TyreR = 0.43f, TyreW = 0.34f;
        public const float Track = 0.80f, FrontZ = 1.08f, RearZ = -1.02f;
        const float AxleY = Ground + TyreR;

        // palette rows
        const int Paint = 0, Flare = 1, Rubber = 2, Tread = 3, Steel = 4, DarkSteel = 5, Rim = 6, Seat = 7,
                  Glass = 8, Lamp = 9, Tail = 10, Amber = 11, Floor = 12, Tube = 13, Grille = 14, PaintLight = 15;
        const int Variants = 16;

        static readonly Color[] Palette =
        {
            new Color(0.42f, 0.41f, 0.27f),   // olive-khaki paint
            new Color(0.09f, 0.09f, 0.085f),  // black arch flares
            new Color(0.055f, 0.055f, 0.055f),// tyre rubber
            new Color(0.085f, 0.082f, 0.078f),// tread blocks
            new Color(0.50f, 0.51f, 0.52f),   // bright steel
            new Color(0.17f, 0.17f, 0.18f),   // dark steel: chassis, bumpers
            new Color(0.33f, 0.33f, 0.30f),   // wheel rims
            new Color(0.17f, 0.14f, 0.11f),   // canvas seats
            new Color(0.52f, 0.62f, 0.67f),   // windscreen glass
            new Color(0.96f, 0.95f, 0.86f),   // headlight lens
            new Color(0.62f, 0.07f, 0.05f),   // tail lights
            new Color(0.90f, 0.52f, 0.10f),   // indicators
            new Color(0.21f, 0.21f, 0.17f),   // floor mats
            new Color(0.11f, 0.11f, 0.11f),   // roll bar, snorkel
            new Color(0.045f, 0.045f, 0.045f),// grille backing
            new Color(0.47f, 0.46f, 0.31f),   // paint, sun-faded
        };

        Transform[] spin, steer;
        float roll;

        /// <summary>Builds a jeep under parent and returns it.</summary>
        public static Jeep Build(Transform parent)
        {
            var root = new GameObject("Jeep");
            root.transform.SetParent(parent, false);
            Jeep j = root.AddComponent<Jeep>();
            Material mat = EarthWorld.Mat(PaletteTexture(), 0.18f);

            Mesh body = BodyMesh(), wheel = WheelMesh();
            Part(root.transform, "Body", body, mat, Vector3.zero, Quaternion.identity);

            j.spin = new Transform[4];
            j.steer = new Transform[2];
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2, right = i % 2 == 1;
                var pivot = new GameObject(front ? "Steer" : "Axle").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(right ? Track : -Track, AxleY, front ? FrontZ : RearZ);
                if (front) j.steer[i] = pivot;
                // the right-hand wheels are turned round so the rims face outwards
                Transform w = Part(pivot, "Wheel", wheel, mat, Vector3.zero, Quaternion.identity).transform;
                w.localScale = new Vector3(right ? -1f : 1f, 1f, 1f);
                j.spin[i] = w;
            }
            Part(root.transform, "Spare", wheel, mat, new Vector3(0f, 0.16f, -1.71f), Quaternion.Euler(0f, -90f, 0f));
            return j;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Turns the wheels: speed in metres a second along the ground (negative backwards), steer -1..1.</summary>
        public void Roll(float speed, float steerInput, float dt)
        {
            roll = Mathf.Repeat(roll + speed / TyreR * Mathf.Rad2Deg * dt, 360f);
            Quaternion r = Quaternion.Euler(roll, 0f, 0f);
            for (int i = 0; i < spin.Length; i++) spin[i].localRotation = r;
            float want = steerInput * 28f;
            for (int i = 0; i < steer.Length; i++)
            {
                float now = steer[i].localEulerAngles.y;
                if (now > 180f) now -= 360f;
                steer[i].localRotation = Quaternion.Euler(0f, Mathf.Lerp(now, want, 1f - Mathf.Exp(-8f * dt)), 0f);
            }
        }

        // ------------------------------------------------------------ palette

        static Texture2D PaletteTexture()
        {
            var t = new Texture2D(Palette.Length, Variants, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "JeepPalette" };
            var px = new Color32[Palette.Length * Variants];
            var rng = new System.Random(4417);
            for (int v = 0; v < Variants; v++)
                for (int c = 0; c < Palette.Length; c++)
                {
                    // a little lighter or darker, a little warmer or cooler: dust, sun and wear
                    float k = 1f + ((float)rng.NextDouble() - 0.5f) * 0.09f;
                    float warm = ((float)rng.NextDouble() - 0.5f) * 0.025f;
                    Color b = Palette[c];
                    px[v * Palette.Length + c] = new Color(Mathf.Clamp01(b.r * k + warm), Mathf.Clamp01(b.g * k), Mathf.Clamp01(b.b * k - warm), 1f);
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // ------------------------------------------------------------ body

        static Mesh BodyMesh()
        {
            var k = new Kit();
            float top = 0.42f;   // the tub's top edge

            // chassis: frame rails, axles and differentials, side steps
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.43f, -0.22f, 0.02f), new Vector3(0.1f, 0.16f, 3.2f), DarkSteel);
                k.Box(new Vector3(s * 0.69f, -0.19f, -0.02f), new Vector3(0.18f, 0.06f, 1.0f), DarkSteel);
            }
            foreach (float z in new[] { FrontZ, RearZ })
            {
                k.Tube(new Vector3(-Track + 0.12f, AxleY, z), new Vector3(Track - 0.12f, AxleY, z), 0.055f, 8, DarkSteel);
                k.Box(new Vector3(0.08f, AxleY, z), new Vector3(0.24f, 0.2f, 0.22f), DarkSteel);
                for (int s = -1; s <= 1; s += 2)   // springs up to the frame
                    k.Tube(new Vector3(s * 0.43f, AxleY, z), new Vector3(s * 0.43f, -0.3f, z), 0.045f, 8, Steel);
            }

            // the tub: an open box with a floor, a dip in the sides for the doorless doorways
            k.Box(new Vector3(0f, -0.09f, -0.5f), new Vector3(1.24f, 0.06f, 2.0f), Floor);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.595f, (top - 0.12f) * 0.5f, -0.95f), new Vector3(0.05f, top + 0.12f, 1.1f), Paint);
                k.Box(new Vector3(s * 0.595f, (0.18f - 0.12f) * 0.5f, 0.05f), new Vector3(0.05f, 0.30f, 0.9f), Paint);
                k.Box(new Vector3(s * 0.605f, top + 0.012f, -0.95f), new Vector3(0.08f, 0.025f, 1.12f), PaintLight);   // rolled top edge
                k.Box(new Vector3(s * 0.605f, 0.192f, 0.05f), new Vector3(0.08f, 0.025f, 0.9f), PaintLight);
            }
            k.Box(new Vector3(0f, (top - 0.12f) * 0.5f, -1.475f), new Vector3(1.24f, top + 0.12f, 0.05f), Paint);
            k.Box(new Vector3(0f, (top - 0.12f) * 0.5f, 0.475f), new Vector3(1.24f, top + 0.12f, 0.05f), Paint);     // firewall
            k.Box(new Vector3(0f, top + 0.012f, -1.475f), new Vector3(1.26f, 0.025f, 0.08f), PaintLight);

            // bonnet: engine box, a slightly wider lid, latches
            k.Box(new Vector3(0f, 0.13f, 1.0f), new Vector3(1.16f, 0.5f, 1.0f), Paint);
            k.Box(new Vector3(0f, 0.40f, 0.99f), new Vector3(1.2f, 0.04f, 1.04f), PaintLight);
            k.Box(new Vector3(0f, 0.423f, 0.99f), new Vector3(0.5f, 0.012f, 0.9f), Paint);   // a low ridge down the middle
            for (int s = -1; s <= 1; s += 2)
                k.Box(new Vector3(s * 0.6f, 0.33f, 1.32f), new Vector3(0.03f, 0.09f, 0.05f), DarkSteel);

            // front: a dark panel with horizontal bars, round lamps either side, indicators below
            k.Box(new Vector3(0f, 0.14f, 1.505f), new Vector3(0.74f, 0.4f, 0.03f), Grille);
            for (int b = 0; b < 5; b++)
                k.Box(new Vector3(0f, -0.02f + b * 0.08f, 1.53f), new Vector3(0.74f, 0.025f, 0.025f), Paint);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.38f, 0.14f, 1.53f), new Vector3(0.03f, 0.4f, 0.03f), Paint);
                k.Tube(new Vector3(s * 0.48f, 0.22f, 1.49f), new Vector3(s * 0.48f, 0.22f, 1.57f), 0.088f, 16, Steel);
                k.Tube(new Vector3(s * 0.48f, 0.22f, 1.57f), new Vector3(s * 0.48f, 0.22f, 1.578f), 0.07f, 16, Lamp);
                k.Box(new Vector3(s * 0.48f, 0.05f, 1.52f), new Vector3(0.08f, 0.045f, 0.03f), Amber);
            }

            // bumpers, tow points
            k.Box(new Vector3(0f, -0.2f, 1.64f), new Vector3(1.86f, 0.16f, 0.16f), DarkSteel);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.43f, -0.2f, 1.555f), new Vector3(0.1f, 0.1f, 0.05f), DarkSteel);
                k.Box(new Vector3(s * 0.3f, -0.2f, 1.74f), new Vector3(0.04f, 0.08f, 0.06f), Tail);
            }
            k.Box(new Vector3(0f, -0.2f, -1.56f), new Vector3(1.5f, 0.16f, 0.14f), DarkSteel);

            // rear: tail lights and the spare wheel's bracket
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.48f, 0.27f, -1.505f), new Vector3(0.09f, 0.11f, 0.015f), Tail);
                k.Box(new Vector3(s * 0.48f, 0.18f, -1.505f), new Vector3(0.09f, 0.045f, 0.015f), Amber);
            }
            k.Box(new Vector3(0f, 0.16f, -1.53f), new Vector3(0.3f, 0.08f, 0.06f), DarkSteel);
            k.Tube(new Vector3(0f, 0.16f, -1.5f), new Vector3(0f, 0.16f, -1.56f), 0.05f, 10, DarkSteel);

            // flared arches over each wheel: painted arch, black lip on its outer edge
            foreach (float z in new[] { FrontZ, RearZ })
                for (int s = -1; s <= 1; s += 2)
                {
                    Arch(k, s, z, 0.53f, 0.585f, 1.0f, 0.035f, Paint);
                    Arch(k, s, z, 0.555f, 0.97f, 1.03f, 0.05f, Flare);
                }

            // windscreen: a flat frame that would fold forward onto the bonnet, leaning back a little
            k.Push(new Vector3(0f, top, 0.5f), Quaternion.Euler(-12f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.6f, 0.25f, 0f), new Vector3(0.045f, 0.5f, 0.045f), Paint);
                k.Box(new Vector3(s * 0.45f, 0.0f, 0.02f), new Vector3(0.06f, 0.04f, 0.05f), DarkSteel);   // hinges
            }
            k.Box(new Vector3(-0.70f, 0.08f, 0.03f), new Vector3(0.14f, 0.02f, 0.02f), DarkSteel);   // a mirror on the driver's side
            k.Box(new Vector3(-0.79f, 0.12f, 0.03f), new Vector3(0.05f, 0.12f, 0.1f), Tube);
            k.Box(new Vector3(0f, 0.025f, 0f), new Vector3(1.24f, 0.05f, 0.05f), Paint);
            k.Box(new Vector3(0f, 0.48f, 0f), new Vector3(1.24f, 0.045f, 0.05f), Paint);
            k.Box(new Vector3(0f, 0.25f, 0f), new Vector3(1.15f, 0.42f, 0.012f), Glass);
            k.Pop();

            // dash and steering wheel (driver on the left)
            k.Box(new Vector3(0f, 0.31f, 0.4f), new Vector3(1.14f, 0.16f, 0.12f), Seat);
            Vector3 col0 = new Vector3(-0.3f, 0.26f, 0.42f), col1 = new Vector3(-0.3f, 0.42f, 0.2f);
            k.Tube(col0, col1, 0.022f, 6, Tube);
            k.Push(col1, Quaternion.LookRotation(col1 - col0));
            for (int i = 0; i < 14; i++)
            {
                float a0 = i * Mathf.PI * 2f / 14f, a1 = (i + 1) * Mathf.PI * 2f / 14f;
                k.Tube(new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.15f, new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.15f, 0.018f, 6, Tube);
            }
            k.Tube(new Vector3(-0.15f, 0f, 0f), new Vector3(0.15f, 0f, 0f), 0.015f, 6, Tube);
            k.Pop();

            // seats: two buckets in front, a bench behind
            for (int s = -1; s <= 1; s += 2)
                Chair(k, new Vector3(s * 0.3f, -0.06f, -0.16f), 0.42f);
            Chair(k, new Vector3(0f, -0.06f, -1.12f), 1.05f);

            // roll bar: a hoop behind the front seats, stays back to the tail, bars forward to the windscreen
            const float r = 0.034f;
            float hz = -0.5f, hy = 1.12f, hx = 0.55f;
            Vector3 wsTop = new Vector3(0f, top + 0.48f * Mathf.Cos(12f * Mathf.Deg2Rad), 0.5f - 0.48f * Mathf.Sin(12f * Mathf.Deg2Rad));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 foot = new Vector3(s * hx, top, hz), knee = new Vector3(s * hx, hy - 0.09f, hz),
                        corner = new Vector3(s * (hx - 0.09f), hy, hz);
                k.Tube(foot, knee, r, 10, Tube);
                k.Tube(knee, corner, r, 10, Tube);
                k.Tube(new Vector3(s * hx, hy - 0.06f, hz), new Vector3(s * hx, top, -1.42f), r, 10, Tube);
                k.Tube(new Vector3(s * hx, hy - 0.05f, hz), new Vector3(s * 0.58f, wsTop.y, wsTop.z), r * 0.9f, 10, Tube);
                k.Box(new Vector3(s * hx, top + 0.01f, hz), new Vector3(0.1f, 0.02f, 0.1f), DarkSteel);      // base plates
                k.Box(new Vector3(s * hx, top + 0.01f, -1.42f), new Vector3(0.1f, 0.02f, 0.1f), DarkSteel);
            }
            k.Tube(new Vector3(-(hx - 0.09f), hy, hz), new Vector3(hx - 0.09f, hy, hz), r, 10, Tube);
            k.Tube(new Vector3(-hx, 0.78f, hz), new Vector3(hx, 0.78f, hz), r * 0.8f, 10, Tube);

            // snorkel: out of the right side of the bonnet, up the windscreen pillar, a forward-facing head on top
            Vector3 sn0 = new Vector3(0.56f, 0.26f, 0.74f), sn1 = new Vector3(0.68f, 0.26f, 0.62f), sn2 = new Vector3(0.68f, 1.0f, 0.36f);
            k.Tube(sn0, sn1, 0.045f, 10, Tube);
            k.Tube(sn1, sn2, 0.045f, 10, Tube);
            k.Push(sn2, Quaternion.Euler(-12f, 0f, 0f));
            k.Box(new Vector3(0f, 0.03f, 0.04f), new Vector3(0.11f, 0.1f, 0.2f), Tube);
            k.Box(new Vector3(0f, 0.03f, 0.145f), new Vector3(0.09f, 0.07f, 0.01f), Grille);
            k.Pop();

            return k.Finish("Jeep body");
        }

        /// <summary>An arch over a wheel, from front to back, x from x0 to x1 on side s.</summary>
        static void Arch(Kit k, int s, float z, float radius, float x0, float x1, float thick, int col)
        {
            const int n = 9;
            float a0 = 12f * Mathf.Deg2Rad, a1 = 168f * Mathf.Deg2Rad;
            float xc = s * (x0 + x1) * 0.5f, w = x1 - x0;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(a0, a1, (i + 0.5f) / n);
                float len = radius * (a1 - a0) / n + 0.01f;
                Vector3 c = new Vector3(xc, AxleY + Mathf.Sin(a) * (radius + thick * 0.5f), z + Mathf.Cos(a) * (radius + thick * 0.5f));
                k.Push(c, Quaternion.Euler(90f - a * Mathf.Rad2Deg, 0f, 0f));
                k.Box(Vector3.zero, new Vector3(w, thick, len), col);
                k.Pop();
            }
        }

        static void Chair(Kit k, Vector3 at, float width)
        {
            k.Box(at + new Vector3(0f, 0.04f, 0f), new Vector3(width * 0.8f, 0.08f, 0.3f), DarkSteel);   // frame
            k.Box(at + new Vector3(0f, 0.13f, 0.02f), new Vector3(width, 0.1f, 0.42f), Seat);            // cushion
            k.Push(at + new Vector3(0f, 0.16f, -0.18f), Quaternion.Euler(-12f, 0f, 0f));
            k.Box(new Vector3(0f, 0.25f, 0f), new Vector3(width, 0.5f, 0.1f), Seat);
            k.Pop();
        }

        // ------------------------------------------------------------ wheel

        /// <summary>One wheel round the origin, axle along x, the rim on the -x side (mirrored for the right-hand wheels).</summary>
        static Mesh WheelMesh()
        {
            var k = new Kit();
            float hw = TyreW * 0.5f, core = TyreR - 0.055f;
            k.Tube(new Vector3(hw, 0f, 0f), new Vector3(-hw, 0f, 0f), core, 28, Rubber);
            k.Tube(new Vector3(hw + 0.01f, 0f, 0f), new Vector3(-hw - 0.01f, 0f, 0f), core - 0.06f, 28, Rubber);   // bulging sidewalls

            // tread: two staggered rows of chunky blocks, the outer ends wrapping a little onto the sidewalls
            const int lugs = 18;
            for (int i = 0; i < lugs; i++)
                for (int row = 0; row < 2; row++)
                {
                    float a = (i + row * 0.5f) * 360f / lugs;
                    float side = row == 0 ? -1f : 1f;
                    k.Push(Vector3.zero, Quaternion.Euler(a, 0f, 0f));
                    k.Box(new Vector3(side * (hw * 0.5f + 0.008f), core + 0.03f, 0f), new Vector3(hw + 0.01f, 0.065f, 0.085f), Tread);
                    k.Box(new Vector3(side * (hw + 0.012f), core - 0.01f, 0f), new Vector3(0.025f, 0.07f, 0.07f), Tread);
                    k.Box(new Vector3(0f, core + 0.012f, 0.03f * side), new Vector3(0.05f, 0.03f, 0.05f), Tread);   // centre ribs
                    k.Pop();
                }

            // steel rim, set into the tyre, with a hub and five nuts
            float face = -hw - 0.012f;
            k.Tube(new Vector3(face + 0.03f, 0f, 0f), new Vector3(face, 0f, 0f), 0.24f, 24, Rim);
            k.Tube(new Vector3(face, 0f, 0f), new Vector3(face - 0.006f, 0f, 0f), 0.2f, 24, Steel);
            k.Tube(new Vector3(face - 0.006f, 0f, 0f), new Vector3(face - 0.05f, 0f, 0f), 0.075f, 12, DarkSteel);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                Vector3 c = new Vector3(face - 0.006f, Mathf.Cos(a) * 0.12f, Mathf.Sin(a) * 0.12f);
                k.Tube(c, c + new Vector3(-0.025f, 0f, 0f), 0.017f, 6, Steel);
            }
            return k.Finish("Jeep wheel");
        }

        // ------------------------------------------------------------ mesh kit

        /// <summary>
        /// Boxes and tubes into one mesh. Every face gets its own vertices and a palette cell picked by where it is,
        /// and long faces are cut into patches, each its own shade of the colour.
        /// </summary>
        sealed class Kit
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();
            readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
            Matrix4x4 m = Matrix4x4.identity;
            const float Patch = 0.28f;

            public void Push(Vector3 pos, Quaternion rot) { stack.Push(m); m = m * Matrix4x4.TRS(pos, rot, Vector3.one); }
            public void Pop() { m = stack.Pop(); }

            static Vector2 Cell(int col, Vector3 p)
            {
                int h = Mathf.FloorToInt(p.x * 73.1f) * 73856093 ^ Mathf.FloorToInt(p.y * 79.3f) * 19349663 ^ Mathf.FloorToInt(p.z * 83.7f) * 83492791;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                int variant = (h & 0x7fffffff) % Variants;
                return new Vector2((col + 0.5f) / Palette.Length, (variant + 0.5f) / Variants);
            }

            /// <summary>A flat quad a-b-c-d facing out along normal, cut into patches when it is big.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, int col)
            {
                a = m.MultiplyPoint3x4(a); b = m.MultiplyPoint3x4(b); c = m.MultiplyPoint3x4(c); d = m.MultiplyPoint3x4(d);
                normal = m.MultiplyVector(normal).normalized;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) { Vector3 x = b; b = d; d = x; }
                int nu = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / Patch)), nv = Mathf.Max(1, Mathf.CeilToInt((d - a).magnitude / Patch));
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        float u0 = (float)i / nu, u1 = (float)(i + 1) / nu, v0 = (float)j / nv, v1 = (float)(j + 1) / nv;
                        Vector3 p00 = Bi(a, b, c, d, u0, v0), p10 = Bi(a, b, c, d, u1, v0), p11 = Bi(a, b, c, d, u1, v1), p01 = Bi(a, b, c, d, u0, v1);
                        Vector2 cell = Cell(col, (p00 + p11) * 0.5f);
                        int k = v.Count;
                        v.Add(p00); v.Add(p10); v.Add(p11); v.Add(p01);
                        for (int q = 0; q < 4; q++) { n.Add(normal); uv.Add(cell); }
                        t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
                    }
            }

            static Vector3 Bi(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u, float w)
            {
                return Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), w);
            }

            public void Box(Vector3 c, Vector3 size, int col)
            {
                Vector3 h = size * 0.5f;
                Vector3 X = new Vector3(h.x, 0, 0), Y = new Vector3(0, h.y, 0), Z = new Vector3(0, 0, h.z);
                Face(c + X, Y, Z, Vector3.right, col); Face(c - X, Y, Z, Vector3.left, col);
                Face(c + Y, X, Z, Vector3.up, col); Face(c - Y, X, Z, Vector3.down, col);
                Face(c + Z, X, Y, Vector3.forward, col); Face(c - Z, X, Y, Vector3.back, col);
            }

            void Face(Vector3 c, Vector3 u, Vector3 w, Vector3 normal, int col)
            {
                Quad(c - u - w, c + u - w, c + u + w, c - u + w, normal, col);
            }

            /// <summary>A capped round bar from a to b; its sides are smooth, each strip its own shade.</summary>
            public void Tube(Vector3 a, Vector3 b, float radius, int sides, int col)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, e2 = Vector3.Cross(axis, e1);
                var ring = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides;
                    ring[i] = e1 * Mathf.Cos(ang) + e2 * Mathf.Sin(ang);
                }
                for (int i = 0; i < sides; i++)
                {
                    Vector3 r0 = ring[i], r1 = ring[(i + 1) % sides];
                    Vector3 p0 = m.MultiplyPoint3x4(a + r0 * radius), p1 = m.MultiplyPoint3x4(a + r1 * radius),
                            p2 = m.MultiplyPoint3x4(b + r1 * radius), p3 = m.MultiplyPoint3x4(b + r0 * radius);
                    Vector3 n0 = m.MultiplyVector(r0).normalized, n1 = m.MultiplyVector(r1).normalized;
                    Vector2 cell = Cell(col, (p0 + p2) * 0.5f);
                    int k = v.Count;
                    v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                    n.Add(n0); n.Add(n1); n.Add(n1); n.Add(n0);
                    for (int q = 0; q < 4; q++) uv.Add(cell);
                    if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n0 + n1) >= 0f) { t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3); }
                    else { t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2); }
                }
                Cap(a, ring, radius, -axis, col);
                Cap(b, ring, radius, axis, col);
            }

            void Cap(Vector3 c, Vector3[] ring, float radius, Vector3 normal, int col)
            {
                Vector3 wc = m.MultiplyPoint3x4(c), wn = m.MultiplyVector(normal).normalized;
                int k = v.Count;
                v.Add(wc); n.Add(wn); uv.Add(Cell(col, wc));
                Vector2 cell = Cell(col, wc + wn * 0.01f);
                for (int i = 0; i < ring.Length; i++) { v.Add(m.MultiplyPoint3x4(c + ring[i] * radius)); n.Add(wn); uv.Add(cell); }
                uv[k] = cell;
                for (int i = 0; i < ring.Length; i++)
                {
                    int i0 = k + 1 + i, i1 = k + 1 + (i + 1) % ring.Length;
                    if (Vector3.Dot(Vector3.Cross(v[i0] - wc, v[i1] - wc), wn) >= 0f) { t.Add(k); t.Add(i0); t.Add(i1); }
                    else { t.Add(k); t.Add(i1); t.Add(i0); }
                }
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
