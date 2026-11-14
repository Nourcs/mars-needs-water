using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The game's own models (designed in code, baked to Resources/Kit/own-*.bytes in the same format as the
    /// Kenney kit) as ready scene objects: one mesh, one shared vertex-colour material, no collider.
    /// Returns null when the model file is missing, so callers can fall back to their greybox shapes.
    /// </summary>
    public static class Own
    {
        static readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<int, Material> _mats = new Dictionary<int, Material>();

        public static GameObject Make(string model, string name, Transform parent, Vector3 localPos, float smoothness = 0.45f, bool shadows = true)
        {
            if (!_meshes.TryGetValue(model, out var mesh) || mesh == null)
            {
                var k = Kit.Get(model);
                if (k == null) return null;
                var mb = new MeshBuilder();
                mb.Model(Matrix4x4.identity, k);
                mesh = mb.ToMesh(model);
                if (mesh == null) return null;
                _meshes[model] = mesh;
            }
            int key = Mathf.RoundToInt(smoothness * 100f);
            if (!_mats.TryGetValue(key, out var mat) || mat == null) { mat = Prims.VertexColored(smoothness); _mats[key] = mat; }
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (!shadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }
    }
}
