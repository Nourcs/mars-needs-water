using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The ground material: loads the detail textures (Resources/Earth/Ground, made by tools/ground/bake_ground.py)
    /// into two texture arrays and hands back a material using the EarthGround shader. Falls back to plain URP Lit
    /// (the flat map colours) if the shader or textures are missing.
    /// </summary>
    public static class EarthGround
    {
        // per kind: metres per repeat, may turn (1/0), normal strength, occlusion strength
        static readonly Vector4[] Params =
        {
            new Vector4(1.6f, 1, 0.7f, 0.8f),   // grass
            new Vector4(1.8f, 1, 0.7f, 0.8f),   // dry grass
            new Vector4(2.4f, 1, 0.9f, 0.9f),   // forest floor
            new Vector4(2.4f, 1, 0.6f, 0.7f),   // farmland
            new Vector4(2.6f, 1, 0.5f, 0.5f),   // sand
            new Vector4(4.5f, 1, 1.0f, 0.9f),   // rock
            new Vector4(5.0f, 1, 0.5f, 0.4f),   // snow
            new Vector4(3.0f, 1, 0.4f, 0.5f),   // paving
            new Vector4(2.2f, 1, 0.8f, 0.8f),   // bare earth
            new Vector4(2.4f, 1, 0.5f, 0.6f),   // mud
        };

        static Texture2DArray albedo, normal;
        static Vector2 offset;

        /// <summary>The ground material every map square copies, or null to keep the old one.</summary>
        public static Material MakeMaterial()
        {
            Shader sh = Shader.Find("MarsNeedsWater/EarthGround");
            if (sh == null || !sh.isSupported) { Debug.LogWarning("EarthGround shader missing: flat ground colours"); return null; }
            if (albedo == null)
            {
                albedo = Load("_a", false);
                normal = Load("_n", true);
            }
            if (albedo == null || normal == null) { Debug.LogWarning("Ground textures missing in Resources/Earth/Ground: flat ground colours"); return null; }

            var mean = new Vector4[GroundKind.Count];
            for (int k = 0; k < GroundKind.Count; k++)
                mean[k] = new Vector4(GroundKind.MeanLinear[k * 3], GroundKind.MeanLinear[k * 3 + 1], GroundKind.MeanLinear[k * 3 + 2], 1);
            Shader.SetGlobalVectorArray("_GroundMean", mean);
            Shader.SetGlobalVectorArray("_GroundParams", Params);
            offset = Vector2.zero;
            Shader.SetGlobalVector("_GroundOffset", Vector4.zero);

            var m = new Material(sh);
            m.SetTexture("_Albedo", albedo);
            m.SetTexture("_Normal", normal);
            return m;
        }

        static Texture2DArray Load(string suffix, bool linear)
        {
            Texture2DArray arr = null;
            var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            try
            {
                for (int k = 0; k < GroundKind.Count; k++)
                {
                    var file = Resources.Load<TextAsset>("Earth/Ground/" + GroundKind.Names[k] + suffix);
                    if (file == null || !tmp.LoadImage(file.bytes, false)) return null;
                    if (arr == null)
                        arr = new Texture2DArray(tmp.width, tmp.height, GroundKind.Count, TextureFormat.RGBA32, true, linear)
                        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                    if (tmp.width != arr.width || tmp.height != arr.height) return null;
                    Color32[] px = tmp.GetPixels32();
                    var bytes = new byte[px.Length * 4];
                    for (int i = 0; i < px.Length; i++)
                    {
                        bytes[i * 4] = px[i].r; bytes[i * 4 + 1] = px[i].g; bytes[i * 4 + 2] = px[i].b; bytes[i * 4 + 3] = px[i].a;
                    }
                    arr.SetPixelData(bytes, 0, k);
                    Resources.UnloadAsset(file);
                }
                arr.Apply(true, true);
                return arr;
            }
            finally { Object.Destroy(tmp); }
        }

        /// <summary>The world moved by 'delta' (x, z): keep the textures where they were on the ground.</summary>
        public static void Shift(Vector2 delta)
        {
            offset -= delta;
            // keep the numbers small enough for the GPU to stay sharp (a one-off jump every 20 km or so)
            if (Mathf.Abs(offset.x) > 20000f) offset.x -= Mathf.Sign(offset.x) * 16384f;
            if (Mathf.Abs(offset.y) > 20000f) offset.y -= Mathf.Sign(offset.y) * 16384f;
            Shader.SetGlobalVector("_GroundOffset", new Vector4(offset.x, offset.y, 0, 0));
        }

        public static Vector2 Offset { get { return offset; } }
    }
}
