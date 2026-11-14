using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Which tree grows where: picks one of our own tree models (own-tree-*) from the biome, the latitude, the
    /// height and the part of the world (baobabs in Africa, gum trees in Australia, cherry blossom in Japan,
    /// saguaros in American deserts...), and adds it to a deco mesh. Models are in metres at natural size.
    /// </summary>
    public static class Flora
    {
        static readonly Color[] Oak = { new Color(0.36f, 0.64f, 0.2f), new Color(0.46f, 0.72f, 0.24f), new Color(0.28f, 0.56f, 0.18f), new Color(0.55f, 0.74f, 0.22f) };
        static readonly Color[] Autumn = { new Color(0.92f, 0.55f, 0.15f), new Color(0.85f, 0.3f, 0.15f), new Color(0.95f, 0.78f, 0.2f) };
        static readonly Color[] Cherry = { new Color(0.98f, 0.66f, 0.76f), new Color(1f, 0.78f, 0.84f), new Color(0.95f, 0.56f, 0.7f) };
        static readonly Color[] Olive = { new Color(0.45f, 0.55f, 0.32f), new Color(0.5f, 0.6f, 0.36f) };
        static readonly Color[] Deep = { new Color(0.16f, 0.5f, 0.2f), new Color(0.22f, 0.56f, 0.22f), new Color(0.14f, 0.44f, 0.2f) };

        static bool In(float v, float a, float b) => v >= a && v <= b;

        /// <summary>A tree for a forest or grassland spot. False if the models are missing (the caller draws its old tree).</summary>
        public static bool Tree(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R, bool forest, float lat, float lon, float h, float snowLine)
        {
            if (Kit.Get("tree-round", 0.35f) == null) return false;
            float alat = Mathf.Abs(lat), r = R();
            bool highCold = h > snowLine * 0.62f, cold = alat > 50f || h > snowLine * 0.45f;
            bool africa = In(lon, -18f, 52f) && In(lat, -35f, 16f);
            bool australia = In(lon, 112f, 155f) && In(lat, -44f, -10f);
            bool medit = In(lat, 30f, 46f) && In(lon, -10f, 42f);
            bool japan = In(lon, 128f, 146f) && In(lat, 30f, 46f);
            bool eastAsia = In(lon, 95f, 125f) && In(lat, 18f, 35f);
            bool eastNA = In(lon, -95f, -60f) && In(lat, 35f, 50f);

            if (alat > 62f || highCold) { if (r < 0.7f) Add(mb, "tree-spruce", at, up, R, 0.8f, 1.3f); else Pine(mb, at, up, R); return true; }
            if (cold) { if (r < 0.6f) Pine(mb, at, up, R); else if (r < 0.85f) Add(mb, "tree-birch", at, up, R, 0.8f, 1.3f); else Add(mb, "tree-spruce", at, up, R, 0.8f, 1.2f); return true; }
            if (australia) { if (r < 0.75f) Add(mb, "tree-gum", at, up, R, 0.8f, 1.3f); else if (alat < 20f && forest) Add(mb, "tree-palm", at, up, R, 0.8f, 1.2f); else Round(mb, at, up, R, Olive); return true; }
            if (alat < 18f)
            {
                if (forest) { if (r < 0.5f) Add(mb, "tree-jungle", at, up, R, 0.7f, 1.25f); else if (r < 0.72f) Add(mb, "tree-palm", at, up, R, 0.8f, 1.3f); else Round(mb, at, up, R, Deep); }
                else if (africa) { if (r < 0.68f) Add(mb, "tree-acacia", at, up, R, 0.8f, 1.4f); else if (r < 0.8f) Add(mb, "tree-baobab", at, up, R, 0.7f, 1.2f); else Round(mb, at, up, R, Olive); }
                else { if (r < 0.4f) Add(mb, "tree-palm", at, up, R, 0.8f, 1.3f); else if (r < 0.7f) Add(mb, "tree-acacia", at, up, R, 0.8f, 1.3f); else Round(mb, at, up, R, Deep); }
                return true;
            }
            if (alat < 35f)
            {
                if (eastAsia && r < 0.35f) Add(mb, "tree-bamboo", at, up, R, 0.8f, 1.3f);
                else if (africa && !forest && r < 0.6f) Add(mb, "tree-acacia", at, up, R, 0.8f, 1.3f);
                else if (medit && r < 0.3f) Add(mb, "tree-cypress", at, up, R, 0.7f, 1.2f);
                else if (h < 60f && r > 0.8f) Add(mb, "tree-palm", at, up, R, 0.8f, 1.2f);
                else if (japan && r > 0.55f) Round(mb, at, up, R, Cherry);
                else Round(mb, at, up, R, medit ? Olive : Oak);
                return true;
            }
            // Temperate.
            if (japan && r < 0.35f) Round(mb, at, up, R, Cherry);
            else if (medit && r < 0.3f) Add(mb, "tree-cypress", at, up, R, 0.7f, 1.2f);
            else if (eastNA && r < 0.3f) Round(mb, at, up, R, Autumn);
            else if (r > 0.86f) Add(mb, "tree-birch", at, up, R, 0.8f, 1.2f);
            else if (r > 0.66f) Pine(mb, at, up, R);
            else Round(mb, at, up, R, r < 0.08f ? Autumn : medit ? Olive : Oak);
            return true;
        }

        /// <summary>A lone plant for bare ground (desert or tundra): saguaro, date palm, dead tree. False if nothing fits or models are missing.</summary>
        public static bool Desert(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R, float lat, float lon)
        {
            if (Kit.Get("tree-cactus") == null) return false;
            float alat = Mathf.Abs(lat), r = R();
            if (alat > 50f) return false;                                   // tundra: rocks and low scrub only
            bool americas = In(lon, -125f, -60f);
            if (americas && alat < 36f) { if (r < 0.75f) Add(mb, "tree-cactus", at, up, R, 0.7f, 1.4f, 0f); else Add(mb, "tree-snag", at, up, R, 0.7f, 1.3f, 0f); }
            else if (In(lon, -18f, 62f) && In(lat, 12f, 36f)) { if (r < 0.3f) Add(mb, "tree-palm", at, up, R, 0.7f, 1.1f); else Add(mb, "tree-snag", at, up, R, 0.7f, 1.3f, 0f); }
            else Add(mb, "tree-snag", at, up, R, 0.7f, 1.4f, 0f);
            return true;
        }

        static void Pine(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R)
        {
            float sc = 4f + R() * 2.6f, t = 0.9f + R() * 0.2f;              // the pine model is 1.9 units tall
            mb.Model(Matrix4x4.TRS(at + up * new Vector3(0f, -0.2f, 0f), up, new Vector3(sc, sc * (0.9f + R() * 0.25f), sc)), Kit.Get("tree", 0.45f), new Color(t, t, t * 0.95f));
        }

        static void Round(MeshBuilder mb, Vector3 at, Quaternion up, System.Func<float> R, Color[] leaves)
        {
            float sc = 0.8f + R() * 0.6f, k = 0.9f + R() * 0.2f;
            var c = leaves[(int)(R() * leaves.Length) % leaves.Length];
            mb.Model(Matrix4x4.TRS(at + up * new Vector3(0f, -0.2f, 0f), up, new Vector3(sc, sc * (0.9f + R() * 0.25f), sc)), Kit.Get("tree-round", 0.35f),
                     new Color(c.r * k, c.g * k, c.b * k), true);
        }

        static void Add(MeshBuilder mb, string model, Vector3 at, Quaternion up, System.Func<float> R, float min, float max, float sway = 0.35f)
        {
            var m = Kit.Get(model, sway);
            if (m == null) { Pine(mb, at, up, R); return; }
            float sc = min + R() * (max - min), t = 0.9f + R() * 0.2f;
            mb.Model(Matrix4x4.TRS(at + up * new Vector3(0f, -0.2f, 0f), up, new Vector3(sc, sc * (0.9f + R() * 0.2f), sc)), m, new Color(t, t, t));
        }
    }
}
