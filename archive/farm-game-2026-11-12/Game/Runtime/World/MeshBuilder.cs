using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Builds one vertex-coloured mesh out of many simple low-poly shapes (boxes, cylinders, cones, spheres,
    /// blades), so a whole patch of trees or a whole city is a single draw call. Shapes are placed with a
    /// matrix in the mesh's local space; flat shading (no shared vertices across faces) for the chunky look.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();

        public int VertexCount => _v.Count;

        public void Tri(Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            var wa = m.MultiplyPoint3x4(a);
            var wb = m.MultiplyPoint3x4(b);
            var wc = m.MultiplyPoint3x4(c);
            var n = Vector3.Cross(wb - wa, wc - wa).normalized;
            int i = _v.Count;
            _v.Add(wa); _v.Add(wb); _v.Add(wc);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _c.Add(col); _c.Add(col); _c.Add(col);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        public void Quad(Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            Tri(m, a, b, c, col);
            Tri(m, a, c, d, col);
        }

        /// <summary>Unit cube centred at the origin (−0.5..0.5); the top face can take its own colour.</summary>
        public void Box(Matrix4x4 m, Color side, Color top)
        {
            Vector3 p(float x, float y, float z) => new Vector3(x, y, z);
            float h = 0.5f;
            Quad(m, p(-h, h, -h), p(-h, h, h), p(h, h, h), p(h, h, -h), top);       // top
            Quad(m, p(-h, -h, -h), p(h, -h, -h), p(h, -h, h), p(-h, -h, h), side);  // bottom
            Quad(m, p(-h, -h, -h), p(-h, h, -h), p(h, h, -h), p(h, -h, -h), side);  // -z
            Quad(m, p(h, -h, h), p(h, h, h), p(-h, h, h), p(-h, -h, h), side);      // +z
            Quad(m, p(-h, -h, h), p(-h, h, h), p(-h, h, -h), p(-h, -h, -h), side);  // -x
            Quad(m, p(h, -h, -h), p(h, h, -h), p(h, h, h), p(h, -h, h), side);      // +x
        }

        /// <summary>Gable roof (triangular prism) over a unit footprint, ridge along X, from y = 0 to 1.</summary>
        public void Roof(Matrix4x4 m, Color col)
        {
            var a = new Vector3(-0.5f, 0, -0.5f); var b = new Vector3(0.5f, 0, -0.5f);
            var c = new Vector3(0.5f, 0, 0.5f); var d = new Vector3(-0.5f, 0, 0.5f);
            var r0 = new Vector3(-0.5f, 1, 0); var r1 = new Vector3(0.5f, 1, 0);
            Quad(m, a, r0, r1, b, col);
            Quad(m, c, r1, r0, d, col);
            Tri(m, d, r0, a, col * 0.9f);
            Tri(m, b, r1, c, col * 0.9f);
        }

        /// <summary>Cylinder from y = 0 to 1, radius 0.5, with a top cap.</summary>
        public void Cylinder(Matrix4x4 m, int sides, Color col, Color cap)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                Quad(m, p0, p0 + Vector3.up, p1 + Vector3.up, p1, col);
                Tri(m, Vector3.up, p1 + Vector3.up, p0 + Vector3.up, cap);
            }
        }

        /// <summary>Cone from y = 0 (radius 0.5) to an apex at y = 1.</summary>
        public void Cone(Matrix4x4 m, int sides, Color col)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                Tri(m, p0, Vector3.up, p1, col);
                Tri(m, Vector3.zero, p0, p1, col * 0.8f);
            }
        }

        /// <summary>A pyramid (square cone) from y = 0 to an apex at y = 1.</summary>
        public void Pyramid(Matrix4x4 m, Color col) => Cone(m, 4, col);

        /// <summary>Low-poly sphere of diameter 1 centred at the origin: a ring of <paramref name="seg"/> sides, two bands each way.</summary>
        public void Blob(Matrix4x4 m, Color col, int seg = 8)
        {
            var ring = new Vector3[seg];
            var upper = new Vector3[seg];
            var lower = new Vector3[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                ring[i] = new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f);
                // The bands are turned half a step so the facets interlock like a cut gem.
                float b = a + Mathf.PI / seg;
                upper[i] = new Vector3(Mathf.Cos(b) * 0.36f, 0.33f, Mathf.Sin(b) * 0.36f);
                lower[i] = new Vector3(Mathf.Cos(b) * 0.36f, -0.33f, Mathf.Sin(b) * 0.36f);
            }
            var top = new Vector3(0, 0.5f, 0); var bot = new Vector3(0, -0.5f, 0);
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                Tri(m, top, upper[j], upper[i], col * 1.06f);
                Tri(m, upper[i], upper[j], ring[j], col);
                Tri(m, upper[i], ring[j], ring[i], col);
                Tri(m, ring[i], ring[j], lower[i], col * 0.92f);
                Tri(m, lower[i], ring[j], lower[j], col * 0.92f);
                Tri(m, bot, lower[i], lower[j], col * 0.85f);
            }
        }

        /// <summary>A grass blade: a thin triangle in the local XY plane, dark at the root, light at the tip.</summary>
        public void Blade(Matrix4x4 m, Color root, Color tip)
        {
            var a = m.MultiplyPoint3x4(new Vector3(-0.5f, 0, 0));
            var b = m.MultiplyPoint3x4(new Vector3(0.5f, 0, 0));
            var c = m.MultiplyPoint3x4(new Vector3(0.1f, 1, 0));
            // Normals lean up so blades light like the ground they grow from.
            var n = (Vector3.Cross(b - a, c - a).normalized + m.MultiplyVector(Vector3.up).normalized * 2f).normalized;
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _c.Add(root); _c.Add(root); _c.Add(tip);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        /// <summary>Adds a whole <see cref="Kit"/> model, placed by a matrix, optionally tinted (colour multiplied; whiteOnly limits that to its white parts).</summary>
        public void Model(Matrix4x4 m, Kit.Model k, Color? tint = null, bool whiteOnly = false)
        {
            if (k == null) return;
            int b = _v.Count;
            var t = tint ?? Color.white;
            for (int i = 0; i < k.V.Length; i++)
            {
                _v.Add(m.MultiplyPoint3x4(k.V[i]));
                var nn = m.MultiplyVector(k.N[i]);
                _n.Add(nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.up);
                var c = k.C[i];
                // whiteOnly: tint just the model's near-white parts (a tree's foliage, not its trunk).
                if (whiteOnly && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b)) > 0.12f) { _c.Add(c); continue; }
                _c.Add(new Color(c.r * t.r, c.g * t.g, c.b * t.b, c.a));
            }
            for (int i = 0; i < k.I.Length; i++) _t.Add(b + k.I[i]);
        }

        public Mesh ToMesh(string name)
        {
            if (_v.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            for (int i = 0; i < _c.Count; i++) _c[i] = Prims.VC(_c[i]);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
