using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Puts the landmark models (Core/Landmarks) on the globe. A model is built when you come within
    /// ShowKm real kilometres and dropped beyond DropKm, so only a handful ever exist at once.
    /// </summary>
    public sealed class EarthLandmarks : MonoBehaviour
    {
        const double ShowKm = 30, DropKm = 36;

        sealed class Live
        {
            public Landmark Mark; public Frame Frame; public GameObject Root; public Mesh Mesh; public Texture2D Texture; public Material Material;
            public MeshCollider Collider;
            public LandmarkMesh Model; public Vector3[] Rest, Now;     // vertices as modelled, and as stood on the ground
            public float[] Lift; public bool[] Settled; public bool AllSettled;
        }

        EarthWorld world;
        readonly Dictionary<Landmark, Live> live = new Dictionary<Landmark, Live>();
        readonly List<Landmark> drop = new List<Landmark>();
        float nextScan, nextSettle;
        Frame placedIn;

        public void Init(EarthWorld w) { world = w; }

        static double Km(double lat0, double lon0, double lat1, double lon1)
        {
            double dy = lat1 - lat0, dx = (lon1 - lon0) * Math.Cos(lat0 * Geo.Deg);
            if (dx > 180) dx -= 360; else if (dx < -180) dx += 360;
            return Math.Sqrt(dx * dx + dy * dy) * 111.2;
        }

        void LateUpdate()
        {
            if (world == null) return;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.5f;
                drop.Clear();
                foreach (Live l in live.Values) if (Km(world.Lat, world.Lon, l.Mark.Lat, l.Mark.Lon) > DropKm) drop.Add(l.Mark);
                foreach (Landmark m in drop) Remove(m);
                foreach (Landmark m in Landmarks.All)
                {
                    if (m.Build == null || live.ContainsKey(m)) continue;
                    if (Km(world.Lat, world.Lon, m.Lat, m.Lon) < ShowKm) { Make(m); break; }   // one new model per scan
                }
            }
            bool settle = Time.unscaledTime >= nextSettle;
            if (settle) nextSettle = Time.unscaledTime + 0.4f;
            bool moved = !ReferenceEquals(placedIn, world.Frame);
            placedIn = world.Frame;
            if (!settle && !moved) return;
            foreach (Live l in live.Values) Put(l, settle);
        }

        void Make(Landmark m)
        {
            LandmarkMesh model = Landmarks.Model(m);
            if (model == null || model.Mesh.Count == 0) return;
            MeshData d = model.Mesh;
            var l = new Live { Mark = m, Frame = new Frame(m.Lat, m.Lon), Model = model, Lift = new float[model.Parts], Settled = new bool[model.Parts] };

            int n = d.Count;
            var v = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(d.P[i * 3], d.P[i * 3 + 1], d.P[i * 3 + 2]);
                nrm[i] = new Vector3(d.N[i * 3], d.N[i * 3 + 1], d.N[i * 3 + 2]);
                uv[i] = new Vector2(d.U[i * 2], d.U[i * 2 + 1]);
            }
            l.Rest = v; l.Now = (Vector3[])v.Clone();
            l.Mesh = new Mesh { name = m.Name };
            l.Mesh.MarkDynamic();
            if (n > 65000) l.Mesh.indexFormat = IndexFormat.UInt32;
            l.Mesh.vertices = v; l.Mesh.normals = nrm; l.Mesh.uv = uv; l.Mesh.triangles = d.I.ToArray();
            l.Mesh.RecalculateBounds();

            l.Texture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[256];
            for (int i = 0; i < 256; i++) px[i] = new Color32((byte)(model.Palette[i] >> 16), (byte)(model.Palette[i] >> 8), (byte)model.Palette[i], 255);
            l.Texture.SetPixels32(px); l.Texture.Apply();
            l.Material = EarthWorld.Mat(l.Texture, 0.15f);

            l.Root = new GameObject("Landmark: " + m.Name);
            l.Root.transform.SetParent(transform, false);
            l.Root.AddComponent<MeshFilter>().sharedMesh = l.Mesh;
            var mr = l.Root.AddComponent<MeshRenderer>();
            mr.sharedMaterial = l.Material;
            mr.shadowCastingMode = ShadowCastingMode.On;
            l.Collider = l.Root.AddComponent<MeshCollider>();
            l.Collider.sharedMesh = l.Mesh;
            if (m.AtSeaLevel) l.AllSettled = true;
            live[m] = l;
            Put(l, true);
        }

        void Remove(Landmark m)
        {
            Live l;
            if (!live.TryGetValue(m, out l)) return;
            live.Remove(m);
            Destroy(l.Mesh); Destroy(l.Texture); Destroy(l.Material); Destroy(l.Root);
        }

        static readonly float[] Around = { 0f, 0f, 0.7f, 0f, -0.7f, 0f, 0f, 0.7f, 0f, -0.7f, 0.5f, 0.5f, -0.5f, 0.5f, 0.5f, -0.5f, -0.5f, -0.5f };

        /// <summary>
        /// Stand the model on the globe. Each part of it is then lowered on to the lowest ground under its own
        /// footprint (its foundations go down from there), so nothing is left in the air on a slope.
        /// </summary>
        void Put(Live l, bool settle)
        {
            Frame f = world.Frame;
            double x, y, z, fx, fy, fz, ux, uy, uz;
            f.ToLocal(l.Mark.Lat, l.Mark.Lon, 0, out x, out y, out z);
            f.DirFrom(l.Frame, 0, 0, 1, out fx, out fy, out fz);
            f.DirFrom(l.Frame, 0, 1, 0, out ux, out uy, out uz);
            var at = new Vector3((float)x, (float)y, (float)z);
            var turn = Quaternion.LookRotation(new Vector3((float)fx, (float)fy, (float)fz), new Vector3((float)ux, (float)uy, (float)uz));
            l.Root.transform.SetPositionAndRotation(at, turn);
            if (!settle || l.AllSettled) return;

            bool changed = false, all = true;
            for (int part = 0; part < l.Model.Parts; part++)
            {
                if (l.Settled[part]) continue;
                float sx = l.Model.Stand[part * 3], sz = l.Model.Stand[part * 3 + 1], reach = l.Model.Stand[part * 3 + 2];
                float lowest = float.MaxValue; bool detailed = true, any = false;
                for (int k = 0; k < Around.Length; k += 2)
                {
                    Vector3 p = at + turn * new Vector3(sx + Around[k] * reach, 0f, sz + Around[k + 1] * reach);
                    float h;
                    if (world.Ground(p, out h, true)) { lowest = Mathf.Min(lowest, h - p.y); any = true; }
                    else { detailed = false; if (world.Ground(p, out h)) { lowest = Mathf.Min(lowest, h - p.y); any = true; } }
                }
                if (!any) { all = false; continue; }
                if (detailed) l.Settled[part] = true; else all = false;
                if (Mathf.Abs(lowest - l.Lift[part]) < 0.02f) continue;
                l.Lift[part] = lowest;
                for (int i = l.Model.Start[part]; i < l.Model.Start[part + 1]; i++) { Vector3 r = l.Rest[i]; r.y += lowest; l.Now[i] = r; }
                changed = true;
            }
            l.AllSettled = all;
            if (!changed) return;
            l.Mesh.vertices = l.Now;
            l.Mesh.RecalculateBounds();
            l.Collider.sharedMesh = null; l.Collider.sharedMesh = l.Mesh;
        }
    }
}
