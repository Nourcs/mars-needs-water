using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The game's models: our own (own-*.bytes, designed in code) and Kenney's free (CC0) low-poly ones, baked into Resources/Kit/*.bytes as plain vertex-coloured triangles
    /// (no textures, no import settings), so they merge into the same one-draw-call meshes as everything else via
    /// <see cref="MeshBuilder.Model"/>. Format: 'MNWK', version, vertex count, index count, bounds min/max, then
    /// per vertex position, normal and sRGB colour bytes, then the indices. Units: 1 = one Kenney tile.
    /// </summary>
    public static class Kit
    {
        public sealed class Model
        {
            public Vector3[] V, N;
            public Color[] C;
            public int[] I;
            public Vector3 Min, Max;
            public Vector3 Size => Max - Min;
        }

        static readonly Dictionary<string, Model> _cache = new Dictionary<string, Model>();

        /// <summary>
        /// The model, or null if it is missing. sway &gt; 0 makes the top of the model sway in the wind (vertex
        /// alpha falls from 1 at the base to 1 - sway at the top), for trees and bushes.
        /// </summary>
        public static Model Get(string name, float sway = 0f)
        {
            string key = sway > 0f ? name + "#" + sway : name;
            if (_cache.TryGetValue(key, out var m)) return m;
            m = Load(name, sway);
            _cache[key] = m;
            return m;
        }

        static Model Load(string name, float sway)
        {
            // Our own model of the same name wins over the Kenney one (own-tree over tree, and so on).
            var ta = Resources.Load<TextAsset>("Kit/own-" + name);
            if (ta == null) ta = Resources.Load<TextAsset>("Kit/" + name);
            if (ta == null) { Debug.LogWarning($"Kit model {name} missing."); return null; }
            var b = ta.bytes;
            if (b.Length < 40 || b[0] != 'M' || b[1] != 'N' || b[2] != 'W' || b[3] != 'K') { Debug.LogWarning($"Kit model {name} is not a kit file."); return null; }
            int vc = System.BitConverter.ToInt32(b, 8), ic = System.BitConverter.ToInt32(b, 12);
            var m = new Model { V = new Vector3[vc], N = new Vector3[vc], C = new Color[vc], I = new int[ic] };
            m.Min = new Vector3(F(b, 16), F(b, 20), F(b, 24));
            m.Max = new Vector3(F(b, 28), F(b, 32), F(b, 36));
            int o = 40;
            float hgt = Mathf.Max(1e-4f, m.Max.y - m.Min.y);
            for (int i = 0; i < vc; i++, o += 28)
            {
                m.V[i] = new Vector3(F(b, o), F(b, o + 4), F(b, o + 8));
                m.N[i] = new Vector3(F(b, o + 12), F(b, o + 16), F(b, o + 20));
                if (!(m.N[i].sqrMagnitude > 0.25f)) m.N[i] = Vector3.up; // never a zero or NaN normal (NaN pixels bloom into white flashes)
                float a = sway > 0f ? 1f - sway * Mathf.Clamp01((m.V[i].y - m.Min.y) / hgt) : 1f;
                m.C[i] = new Color(b[o + 24] / 255f, b[o + 25] / 255f, b[o + 26] / 255f, a);
            }
            for (int i = 0; i < ic; i++, o += 4) m.I[i] = System.BitConverter.ToInt32(b, o);
            return m;
        }

        static float F(byte[] b, int o) => System.BitConverter.ToSingle(b, o);
    }
}
