using System.Collections.Generic;
using MarsNeedsWater.Simulation;
using Pose = MarsNeedsWater.Simulation.Pose;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>Greybox helpers: primitive shapes with cached URP materials, a noise ground texture and 3D labels.</summary>
    public static class Prims
    {
        static readonly Dictionary<(Color, float, float, Color), Material> _mats = new Dictionary<(Color, float, float, Color), Material>();
        static Texture2D _noise;
        static Font _font;
        static Material _textMat;

        static Shader Lit()
        {
            // The stylised shader (Art/Resources/StylizedLit.shader); URP Lit if it is missing or failed to compile.
            var sh = Shader.Find("MarsNeedsWater/StylizedLit");
            if (sh != null && sh.isSupported) return sh;
            sh = Shader.Find("Universal Render Pipeline/Lit");
            return sh != null ? sh : Shader.Find("Standard");
        }

        /// <summary>A stylised material that takes its colours from mesh vertex colours (terrain, trees, cities).</summary>
        public static Material VertexColored(float smoothness, bool twoSided = false, float rim = 0.2f)
        {
            var m = new Material(Lit());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 1f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", rim);
            if (twoSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            return m;
        }

        /// <summary>
        /// A colour for mesh vertex colours. Material colours are converted from sRGB for us, vertex colours are not,
        /// so in a linear-colour project they are converted here to look the same as a material of that colour.
        /// </summary>
        public static Color VC(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        /// <summary>A flat-colour Lit material (cached per colour and finish).</summary>
        public static Material Mat(Color c) => Mat(c, 0.2f, 0f);

        public static Material Mat(Color c, float smoothness, float metallic) => Mat(c, smoothness, metallic, Color.black);

        /// <summary>A Lit material with an optional emission colour (screens, lamps).</summary>
        public static Material Mat(Color c, float smoothness, float metallic, Color emission)
        {
            var key = (c, smoothness, metallic, emission);
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Lit());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emission.r + emission.g + emission.b > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
            }
            _mats[key] = m;
            return m;
        }

        /// <summary>A Lit material tinted by a shared Perlin-noise texture, tiled every <paramref name="metresPerTile"/> m.</summary>
        public static Material Noisy(Color c, Vector3 size, float metresPerTile)
        {
            var m = new Material(Lit());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
            var tex = NoiseTexture();
            var tiling = new Vector2(Mathf.Max(1f, size.x / metresPerTile), Mathf.Max(1f, size.z / metresPerTile));
            foreach (var prop in new[] { "_BaseMap", "_MainTex" })
            {
                if (!m.HasProperty(prop)) continue;
                m.SetTexture(prop, tex);
                m.SetTextureScale(prop, tiling);
            }
            return m;
        }

        static Texture2D NoiseTexture()
        {
            if (_noise != null) return _noise;
            const int n = 128;
            _noise = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "GroundNoise", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Tileable two-octave noise: sample a torus-wrapped domain.
                float u = x / (float)n, v = y / (float)n;
                float a = Wrapped(u, v, 4f, 0f) * 0.65f + Wrapped(u, v, 16f, 37f) * 0.35f;
                float k = Mathf.Lerp(0.78f, 1.12f, a);
                px[y * n + x] = new Color(k, k * 0.99f, k * 0.96f, 1f);
            }
            _noise.SetPixels(px);
            _noise.Apply(true);
            return _noise;
        }

        static float Wrapped(float u, float v, float freq, float seed)
        {
            // Bilinear blend of four offsets makes the texture repeat seamlessly.
            float f(float a, float b) => Mathf.PerlinNoise(seed + a * freq, seed + b * freq);
            return f(u, v) * (1 - u) * (1 - v) + f(u - 1, v) * u * (1 - v) + f(u, v - 1) * (1 - u) * v + f(u - 1, v - 1) * u * v;
        }

        /// <summary>Clears the material cache (each Play builds the level fresh).</summary>
        public static void ClearCache()
        {
            _mats.Clear();
            _noise = null;
            _textMat = null;
        }

        public static GameObject Shape(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color c, Transform parent = null, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = Mat(c);
            if (!collider) Object.Destroy(g.GetComponent<Collider>());
            return g;
        }

        public static GameObject Box(string name, Vector3 pos, Vector3 scale, Color c, Transform parent = null, bool collider = true)
            => Shape(PrimitiveType.Cube, name, pos, scale, c, parent, collider);

        public static GameObject Cyl(string name, Vector3 pos, Vector3 scale, Color c, Transform parent = null, bool collider = true)
            => Shape(PrimitiveType.Cylinder, name, pos, scale, c, parent, collider);

        public static GameObject Ball(string name, Vector3 pos, float d, Color c, Transform parent = null, bool collider = true)
            => Shape(PrimitiveType.Sphere, name, pos, Vector3.one * d, c, parent, collider);

        /// <summary>Decoration: no collider, optional finish.</summary>
        public static GameObject Deco(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color c, Transform parent, float smoothness = 0.2f, float metallic = 0f)
        {
            var g = Shape(type, name, pos, scale, c, parent, false);
            g.GetComponent<Renderer>().sharedMaterial = Mat(c, smoothness, metallic);
            return g;
        }

        public static void SetColor(GameObject g, Color c)
        {
            var r = g.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = Mat(c);
        }

        public static void SetFinish(GameObject g, Color c, float smoothness, float metallic = 0f)
        {
            var r = g.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = Mat(c, smoothness, metallic);
        }

        /// <summary>
        /// A 3D text label (signs, stencils). Readable from the side its local -Z faces, like a Quad; the height is
        /// the cap height in metres and maxWidth caps long lines. Depth-tested, so walls hide it.
        /// </summary>
        public static GameObject Label(string text, Vector3 pos, float yaw, float height, Color c, Transform parent, float maxWidth = 0f, bool bold = true)
        {
            var g = new GameObject("Label_" + text.Split('\n')[0]);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tm = g.AddComponent<TextMesh>();
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = _font;
            tm.fontSize = 96;
            tm.characterSize = 0.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            tm.color = c;
            tm.text = text;
            var r = g.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = TextMaterial();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            g.AddComponent<FitText>().Configure(height, maxWidth);
            return g;
        }

        static Material TextMaterial()
        {
            if (_textMat != null) return _textMat;
            var sh = Shader.Find("MarsNeedsWater/Text3D");
            _textMat = sh != null ? new Material(sh) : new Material(_font.material);
            if (_font != null) _textMat.mainTexture = _font.material.mainTexture;
            return _textMat;
        }
    }

    /// <summary>Scales a TextMesh once its mesh exists so its glyphs are a set height (and fit a width).</summary>
    public sealed class FitText : MonoBehaviour
    {
        float _height, _maxWidth;
        int _tries;

        public void Configure(float height, float maxWidth)
        {
            _height = height;
            _maxWidth = maxWidth;
        }

        void LateUpdate()
        {
            var r = GetComponent<MeshRenderer>();
            if (r == null || ++_tries > 30) { enabled = false; return; }
            // World bounds of a flat, axis-aligned label: the smallest extent is its thickness, the middle one
            // its height and the largest its width (works for wall signs and floor stencils alike).
            var b = r.bounds.size;
            float lo = Mathf.Min(b.x, Mathf.Min(b.y, b.z)), hi = Mathf.Max(b.x, Mathf.Max(b.y, b.z));
            float mid = b.x + b.y + b.z - lo - hi;
            if (mid < 1e-5f) return;
            int lines = 1;
            var tm = GetComponent<TextMesh>();
            if (tm != null) foreach (char ch in tm.text) if (ch == '\n') lines++;
            float k = _height * (lines + (lines - 1) * 0.35f) / mid;
            float w = hi * k;
            if (_maxWidth > 0f && w > _maxWidth) k *= _maxWidth / w;
            transform.localScale = transform.localScale * k;
            enabled = false;
        }
    }

    /// <summary>
    /// Converts between the simulation's plain-data Pose (in the farm's local frame) and Unity world space.
    /// The farm frame moves when the planet is re-based under the player, so every conversion goes through it.
    /// </summary>
    public static class PoseX
    {
        public static Vector3 ToLocal(this Pose p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);

        public static Vector3 ToV3(this Pose p)
        {
            var f = FarmSpace.Frame;
            return f == null ? p.ToLocal() : f.TransformPoint(p.ToLocal());
        }

        public static Quaternion ToRot(this Pose p)
        {
            var r = Quaternion.Euler(0f, (float)p.Yaw, 0f);
            var f = FarmSpace.Frame;
            return f == null ? r : f.rotation * r;
        }

        /// <summary>World position (and world yaw) to a farm-frame pose.</summary>
        public static Pose ToPose(this Vector3 world, float yaw = 0f)
        {
            var f = FarmSpace.Frame;
            if (f == null) return new Pose(world.x, world.y, world.z, yaw);
            var l = f.InverseTransformPoint(world);
            var fwd = f.InverseTransformDirection(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
            return new Pose(l.x, l.y, l.z, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
        }

        public static Pose ToPose(this Transform t) => t.position.ToPose(t.eulerAngles.y);
    }

    /// <summary>
    /// Greybox palette: a sun-bleached late-summer farm. Safety orange marks player machinery, corporate teal marks
    /// the employer, and water is the only saturated blue.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Ground = new Color(0.66f, 0.6f, 0.4f);      // dry grass, straw
        public static readonly Color Grass = new Color(0.42f, 0.47f, 0.27f);     // greener tufts
        public static readonly Color Bush = new Color(0.33f, 0.39f, 0.22f);
        public static readonly Color Tree = new Color(0.28f, 0.36f, 0.2f);
        public static readonly Color Bark = new Color(0.36f, 0.27f, 0.19f);
        public static readonly Color Mud = new Color(0.4f, 0.31f, 0.22f);        // wet pond wall
        public static readonly Color DryMud = new Color(0.7f, 0.6f, 0.46f);      // exposed, sun-dried bank
        public static readonly Color WetEdge = new Color(0.26f, 0.21f, 0.15f);   // dark line just above the water
        public static readonly Color Stain = new Color(0.36f, 0.33f, 0.24f);     // bathtub-ring band
        public static readonly Color Water = new Color(0.12f, 0.36f, 0.62f);
        public static readonly Color Reed = new Color(0.45f, 0.5f, 0.24f);
        public static readonly Color Cattail = new Color(0.36f, 0.23f, 0.13f);
        public static readonly Color Rock = new Color(0.55f, 0.53f, 0.5f);
        public static readonly Color Orange = new Color(1f, 0.42f, 0.05f);
        public static readonly Color Grey = new Color(0.55f, 0.56f, 0.58f);
        public static readonly Color Dark = new Color(0.13f, 0.13f, 0.15f);
        public static readonly Color Concrete = new Color(0.74f, 0.73f, 0.7f);
        public static readonly Color Joint = new Color(0.5f, 0.5f, 0.48f);
        public static readonly Color Corporate = new Color(0.1f, 0.62f, 0.6f);
        public static readonly Color CorporateDark = new Color(0.06f, 0.3f, 0.32f);
        public static readonly Color Crate = new Color(0.74f, 0.6f, 0.38f);
        public static readonly Color CrateSlat = new Color(0.58f, 0.44f, 0.26f);
        public static readonly Color Wood = new Color(0.45f, 0.33f, 0.22f);
        public static readonly Color Marker = new Color(0.98f, 0.82f, 0.12f);
        public static readonly Color Steel = new Color(0.42f, 0.46f, 0.5f);
        public static readonly Color White = new Color(0.93f, 0.93f, 0.91f);
        public static readonly Color BarnRed = new Color(0.58f, 0.16f, 0.12f);
        public static readonly Color Roof = new Color(0.3f, 0.3f, 0.32f);
        public static readonly Color Hill = new Color(0.52f, 0.52f, 0.34f);
        public static readonly Color Screen = new Color(0.05f, 0.12f, 0.12f);
        public static readonly Color ScreenGlow = new Color(0.1f, 0.9f, 0.8f);
        public static readonly Color GhostOk = new Color(0.2f, 0.9f, 0.3f);
        public static readonly Color GhostBad = new Color(0.95f, 0.2f, 0.2f);
    }
}
