using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// Draws every tree in the closest map squares: real models built in code (TreeBank: 14 kinds, 3 shapes
    /// each), chosen per region and ground by TreeSpecies when the square is built, each with its own size, turn
    /// and leaf colour. Three levels of detail by distance from the camera (full, lighter, impostor), drawn with
    /// GPU instancing from one buffer rebuilt only when the camera has moved a few metres.
    /// </summary>
    public sealed class EarthTrees : MonoBehaviour
    {
        public static float FullRange = 45f;      // full models out to here (game metres)
        public static float LiteRange = 170f;     // lighter models out to here, impostors beyond
        public static float MaxRange = 1500f;
        const float Rebuild = 6f;                 // rebuild the buffer after the camera moves this far

        struct Inst                               // 32 bytes, matches EarthTree.shader
        {
            public Vector3 Pos; public float Yaw; public float Height; public float Width; public uint Tint; public float Rand;
        }

        sealed class Square
        {
            public Transform Root; public TreeSpot[] Spots; public Vector3[] World; public Matrix4x4 At; public bool Placed;
        }

        EarthWorld world;
        Task<TreeBank> job;
        Mesh[] meshes;
        Material mat;
        Texture2D leaves, impostors;
        Texture2DArray bark;
        readonly Dictionary<long, Square> squares = new Dictionary<long, Square>();
        GraphicsBuffer buf;
        Inst[] scratch = new Inst[0];
        int[] slot = new int[0];
        readonly int[] counts = new int[TreeSpecies.Count * TreeSpecies.Variants * TreeModels.Lods];
        readonly int[] starts = new int[TreeSpecies.Count * TreeSpecies.Variants * TreeModels.Lods];
        MaterialPropertyBlock[] blocks;
        Vector3 lastCam = new Vector3(1e9f, 0, 0);
        bool dirty, failed;
        int total;

        public int Count { get { return total; } }

        public void Init(EarthWorld w)
        {
            world = w;
            Shader sh = Shader.Find("MarsNeedsWater/EarthTree");
            if (sh == null || !sh.isSupported || !SystemInfo.supportsInstancing) { Debug.LogWarning("EarthTree shader missing: no trees"); enabled = false; return; }
            mat = new Material(sh);
            mat.SetFloat("_Wind", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            // the models and pictures take a few seconds to make: done on a worker thread
            job = Task.Run(() => TreeBank.Build());
            EarthWorld.Shifted += OnShifted;
            EarthWorld.Jumped += OnJumped;
        }

        void OnDestroy()
        {
            EarthWorld.Shifted -= OnShifted;
            EarthWorld.Jumped -= OnJumped;
            if (buf != null) buf.Release();
            if (meshes != null) foreach (Mesh m in meshes) if (m != null) Destroy(m);
            if (mat != null) Destroy(mat);
            if (leaves != null) Destroy(leaves);
            if (impostors != null) Destroy(impostors);
            if (bark != null) Destroy(bark);
        }

        void OnShifted(Vector3 d) { dirty = true; }
        void OnJumped() { dirty = true; }

        /// <summary>A map square with trees has been shown.</summary>
        public void Add(long key, Transform root, List<TreeSpot> spots)
        {
            squares[key] = new Square { Root = root, Spots = spots.ToArray(), World = new Vector3[spots.Count] };
            dirty = true;
        }

        public void Remove(long key)
        {
            if (squares.Remove(key)) dirty = true;
        }

        void Update()
        {
            if (meshes == null)
            {
                if (failed || job == null || !job.IsCompleted) return;
                if (job.IsFaulted) { failed = true; Debug.LogError("Trees could not be built: " + job.Exception.GetBaseException()); return; }
                MakeAssets(job.Result);
                job = null;
                dirty = true;
            }

            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : (world.PlayerTransform != null ? world.PlayerTransform.position : Vector3.zero);
            if (dirty || (eye - lastCam).sqrMagnitude > Rebuild * Rebuild)
            {
                dirty = false;
                lastCam = eye;
                Fill(eye);
            }
            if (total == 0) return;
            var bounds = new Bounds(eye, new Vector3(MaxRange * 2.2f, 4000f, MaxRange * 2.2f));
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == 0) continue;
                int lod = i % TreeModels.Lods;
                Graphics.DrawMeshInstancedProcedural(meshes[i], 0, mat, bounds, counts[i], blocks[i],
                    lod < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off, true, 0);
            }
        }

        /// <summary>Sort every tree near the camera into its model and level of detail, and upload them.</summary>
        void Fill(Vector3 eye)
        {
            System.Array.Clear(counts, 0, counts.Length);
            int n = 0;
            foreach (Square s in squares.Values) n += s.Spots.Length;
            if (slot.Length < n) slot = new int[Mathf.NextPowerOfTwo(n)];
            if (scratch.Length < n) scratch = new Inst[Mathf.NextPowerOfTwo(n)];
            float full2 = FullRange * FullRange, lite2 = LiteRange * LiteRange, max2 = MaxRange * MaxRange;
            int k = 0;
            foreach (Square s in squares.Values)
            {
                if (s.Root == null) { for (int i = 0; i < s.Spots.Length; i++) slot[k++] = -1; continue; }
                Matrix4x4 m = s.Root.localToWorldMatrix;
                if (!s.Placed || m != s.At)
                {
                    for (int i = 0; i < s.Spots.Length; i++) s.World[i] = m.MultiplyPoint3x4(new Vector3(s.Spots[i].X, s.Spots[i].Y, s.Spots[i].Z));
                    s.At = m; s.Placed = true;
                }
                for (int i = 0; i < s.Spots.Length; i++)
                {
                    Vector3 p = s.World[i];
                    float dx = p.x - eye.x, dy = p.y - eye.y, dz = p.z - eye.z;
                    float d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 > max2) { slot[k++] = -1; continue; }
                    int lod = d2 < full2 ? 0 : d2 < lite2 ? 1 : 2;
                    int b = TreeBank.Index(s.Spots[i].Species, s.Spots[i].Variant, lod);
                    slot[k++] = b;
                    counts[b]++;
                }
            }
            int o = 0;
            for (int i = 0; i < counts.Length; i++) { starts[i] = o; o += counts[i]; }
            total = o;
            if (total == 0) return;
            var at = (int[])starts.Clone();
            k = 0;
            foreach (Square s in squares.Values)
                for (int i = 0; i < s.Spots.Length; i++, k++)
                {
                    int b = slot[k];
                    if (b < 0) continue;
                    TreeSpot t = s.Spots[i];
                    scratch[at[b]++] = new Inst { Pos = s.World[i], Yaw = t.Yaw, Height = t.Height, Width = t.Width, Tint = t.Tint, Rand = t.Rand };
                }
            if (buf == null || buf.count < total)
            {
                if (buf != null) buf.Release();
                buf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(total), 32);
                mat.SetBuffer("_Instances", buf);
            }
            buf.SetData(scratch, 0, 0, total);
            for (int i = 0; i < counts.Length; i++) blocks[i].SetFloat("_Base", starts[i]);
        }

        // ------------------------------------------------------------ models and pictures into Unity

        void MakeAssets(TreeBank b)
        {
            meshes = new Mesh[b.Meshes.Length];
            blocks = new MaterialPropertyBlock[b.Meshes.Length];
            for (int i = 0; i < b.Meshes.Length; i++)
            {
                meshes[i] = ToMesh(b.Meshes[i], "Tree " + i);
                blocks[i] = new MaterialPropertyBlock();
            }

            leaves = new Texture2D(TreeTextures.AtlasN, TreeTextures.AtlasN, TextureFormat.RGBA32, Mathf.Min(7, b.Atlas.Count), false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Tree leaves" };
            for (int i = 0; i < leaves.mipmapCount; i++) leaves.SetPixelData(b.Atlas[i], i);
            leaves.Apply(false, true);

            impostors = new Texture2D(b.ImpW, b.ImpH, TextureFormat.RGBA32, Mathf.Min(6, b.Impostors.Count), false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2, name = "Tree impostors" };
            for (int i = 0; i < impostors.mipmapCount; i++) impostors.SetPixelData(b.Impostors[i], i);
            impostors.Apply(false, true);

            bark = new Texture2DArray(TreeTextures.BarkW, TreeTextures.BarkH, TreeTextures.BarkLayers, TextureFormat.RGBA32, true, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Tree bark" };
            for (int l = 0; l < TreeTextures.BarkLayers; l++) bark.SetPixelData(b.Bark[l], 0, l);
            bark.Apply(true, true);

            mat.SetTexture("_Leaves", leaves);
            mat.SetTexture("_Impostors", impostors);
            mat.SetTexture("_Bark", bark);
        }

        static Mesh ToMesh(TreeMesh t, string name)
        {
            int n = t.Count;
            var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new List<Vector3>(n); var c = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(t.P[i * 3], t.P[i * 3 + 1], t.P[i * 3 + 2]);
                nn[i] = new Vector3(t.N[i * 3], t.N[i * 3 + 1], t.N[i * 3 + 2]);
                uv.Add(new Vector3(t.U[i * 3], t.U[i * 3 + 1], t.U[i * 3 + 2]));
                c[i] = new Color32(t.C[i * 4], t.C[i * 4 + 1], t.C[i * 4 + 2], t.C[i * 4 + 3]);
            }
            var m = new Mesh { name = name };
            if (n > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = v; m.normals = nn; m.SetUVs(0, uv); m.colors32 = c;
            m.SetTriangles(t.I, 0);
            m.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(3, 3, 3));
            m.UploadMeshData(true);
            return m;
        }
    }
}
