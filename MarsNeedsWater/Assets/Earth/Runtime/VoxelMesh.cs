using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The voxel look's helpers on the Unity side: turn block faces (Core/Voxel.cs) into a Unity mesh, rebuild an
    /// existing mesh out of small blocks (the jeep), and the player's blocky figure.
    /// </summary>
    public static class VoxelMesh
    {
        /// <summary>Block faces as a Unity mesh for EarthVoxel.shader.</summary>
        public static Mesh ToMesh(MeshData d, string name)
        {
            int n = d.Count;
            var v = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n]; var u1 = new Vector4[n]; var u2 = new Vector4[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(d.P[i * 3], d.P[i * 3 + 1], d.P[i * 3 + 2]);
                nrm[i] = new Vector3(d.N[i * 3], d.N[i * 3 + 1], d.N[i * 3 + 2]);
                uv[i] = new Vector2(d.U[i * 2], d.U[i * 2 + 1]);
                u1[i] = new Vector4(d.X[i * 4], d.X[i * 4 + 1], d.X[i * 4 + 2], d.X[i * 4 + 3]);
                u2[i] = new Vector4(d.Y[i * 4], d.Y[i * 4 + 1], d.Y[i * 4 + 2], d.Y[i * 4 + 3]);
            }
            var mesh = new Mesh { name = name };
            if (n > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = v; mesh.normals = nrm; mesh.uv = uv;
            mesh.SetUVs(1, u1); mesh.SetUVs(2, u2);
            mesh.triangles = d.I.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        static uint Pack(Color c)
        {
            return ((uint)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255) << 16) | ((uint)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255) << 8) | (uint)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
        }

        /// <summary>A smooth mesh rebuilt out of blocks of the given size; colour gives each vertex its colour.</summary>
        public static Mesh Blocks(Mesh src, Func<Vector2, Color> colour, float block, float seed)
        {
            Vector3[] v = src.vertices; Vector2[] uv = src.uv; int[] tri = src.triangles;
            var p = new float[v.Length * 3];
            for (int i = 0; i < v.Length; i++) { p[i * 3] = v[i].x; p[i * 3 + 1] = v[i].y; p[i * 3 + 2] = v[i].z; }
            var d = new MeshData { X = new List<float>(), Y = new List<float>() };
            Voxel.FromMesh(d, p, tri, i => Pack(colour(i < uv.Length ? uv[i] : Vector2.zero)), block, block, block, seed);
            return ToMesh(d, src.name + " (blocks)");
        }

        // ------------------------------------------------------------ the player

        static void Box(MeshData m, float px, float x0, float y0, float z0, float x1, float y1, float z1, uint col, float seed)
        {
            var q = new float[12];
            // the six faces; block coordinates are the figure's own pixels, so every pixel has its own shade
            for (int f = 0; f < 6; f++)
            {
                int d = f / 2, sign = f % 2 == 0 ? -1 : 1;
                int a = d == 0 ? 2 : 0, b = d == 1 ? 2 : 1;
                float[] lo = { x0, y0, z0 }, hi = { x1, y1, z1 };
                for (int k = 0; k < 4; k++)
                {
                    q[k * 3 + d] = (sign > 0 ? hi[d] : lo[d]) * px;
                    q[k * 3 + a] = ((k & 1) != 0 ? hi[a] : lo[a]) * px;
                    q[k * 3 + b] = (k >= 2 ? hi[b] : lo[b]) * px;
                }
                Voxel.Quad(m, q, d == 0 ? sign : 0, d == 1 ? sign : 0, d == 2 ? sign : 0, lo[a], lo[b], hi[a], hi[b], col, 0, 0, seed + f * 3.1f + d);
            }
        }

        /// <summary>
        /// The player as a figure of blocks, 'height' tall with its middle at the origin, facing +z: boots, legs,
        /// an orange suit, a pack on the back and a white helmet with a dark visor.
        /// </summary>
        public static Mesh Figure(float height)
        {
            float px = height / 32f;
            var m = new MeshData { X = new List<float>(), Y = new List<float>() };
            const uint suit = 0xe6733a, suitDark = 0xb8562a, leg = 0x39424e, boot = 0x23272e, glove = 0xe9e4d8, helmet = 0xf2efe8, visor = 0x24384e, pack = 0x8d949c, strap = 0x4a515a;
            float y = -16f;
            // legs and boots
            Box(m, px, -4, y, -2, -0.2f, y + 3, 2.6f, boot, 1); Box(m, px, 0.2f, y, -2, 4, y + 3, 2.6f, boot, 2);
            Box(m, px, -4, y + 3, -2, -0.2f, y + 12, 2, leg, 3); Box(m, px, 0.2f, y + 3, -2, 4, y + 12, 2, leg, 4);
            // body, belt, arms, gloves
            Box(m, px, -4, y + 12, -2, 4, y + 24, 2, suit, 5);
            Box(m, px, -4.2f, y + 12, -2.2f, 4.2f, y + 14, 2.2f, strap, 6);
            Box(m, px, -7.6f, y + 14, -2, -4, y + 24, 2, suitDark, 7); Box(m, px, 4, y + 14, -2, 7.6f, y + 24, 2, suitDark, 8);
            Box(m, px, -7.6f, y + 11, -2, -4, y + 14, 2, glove, 9); Box(m, px, 4, y + 11, -2, 7.6f, y + 14, 2, glove, 10);
            // the pack on the back
            Box(m, px, -3, y + 14, -5, 3, y + 23, -2, pack, 11);
            // helmet and visor
            Box(m, px, -4, y + 24, -4, 4, y + 32, 4, helmet, 12);
            Box(m, px, -3, y + 26, 3.6f, 3, y + 30, 4.4f, visor, 13);
            return ToMesh(m, "Player (blocks)");
        }
    }
}
