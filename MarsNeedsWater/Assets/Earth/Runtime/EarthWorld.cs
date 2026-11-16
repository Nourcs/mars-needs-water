using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The mini Earth. Streams squares of the real world around the player from Overture Maps
    /// (release 2026-10-21.0) at four levels of detail, and keeps the player near the scene origin.
    /// Press Play in any scene: it sets itself up.
    /// </summary>
    public sealed partial class EarthWorld : MonoBehaviour
    {
        public static EarthWorld I;

        // Start: the Ferry Building, San Francisco.
        public const double StartLat = 37.79535, StartLon = -122.39370;

        static readonly int[] Zoom = { 14, 12, 10, 8 };
        static readonly int[] Radius = { 2, 2, 2, 2 };
        static readonly int[] Tex = { 1024, 512, 512, 512 };
        static readonly float[] MaxAlt = { 2500f, 14000f, 1e9f, 1e9f };
        static readonly float[] Sink = { 0f, 0.6f, 2.5f, 10f };

        sealed class Part { public Vector3[] V, N; public Vector2[] U; public Vector4[] U1, U2; public int[] I; }

        sealed class Prepared
        {
            public long Key; public int Level; public TileResult Data;
            public Part[] Ground, Walls, Water; public Part Roofs, Roads, Trees, Bridges;
        }

        sealed class Tile
        {
            public long Key; public int Level, X, Y; public TileResult Data; public Frame Frame;
            public GameObject Root; public GameObject[] Patches;
            public Texture2D Texture, WaterMask; public Material Material;
            public readonly List<Mesh> Meshes = new List<Mesh>();
        }

        struct Job { public long Key; public int Level, X, Y; }

        public Frame Frame { get; private set; }
        public double Lat { get; private set; }
        public double Lon { get; private set; }
        public float Alt { get; private set; }
        public int Loading { get { lock (jobs) return jobs.Count + working; } }
        public int Loaded { get { return tiles.Count; } }
        public string Error { get; private set; }

        TileSources src;
        EarthPlayer player;
        EarthWater water;
        MaterialPropertyBlock waterBlock;       // made in Show: Unity forbids making one in a field initializer
        readonly Dictionary<long, Tile> tiles = new Dictionary<long, Tile>();
        readonly HashSet<long> requested = new HashSet<long>();
        readonly Dictionary<long, float> retryAt = new Dictionary<long, float>();
        readonly List<Job> jobs = new List<Job>();
        readonly ConcurrentQueue<Prepared> done = new ConcurrentQueue<Prepared>();
        readonly ConcurrentQueue<long> failed = new ConcurrentQueue<long>();
        readonly Queue<KeyValuePair<long, MeshFilter>> colliders = new Queue<KeyValuePair<long, MeshFilter>>();
        readonly HashSet<long> wanted = new HashSet<long>(), keep = new HashSet<long>();
        readonly int[] centreX = new int[4], centreY = new int[4];
        int working;
        volatile bool quit;
        float nextRefresh;
        bool patchesDirty;
        Material groundBase, flatMat, roadMat;
        Material[] wallMats;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            new GameObject("Earth").AddComponent<EarthWorld>();
        }

        static long KeyOf(int level, int x, int y) { return ((long)level << 60) | ((long)x << 30) | (long)y; }

        void Awake()
        {
            I = this;
            src = new TileSources(Application.persistentDataPath);
            Frame = new Frame(StartLat, StartLon);
            Lat = StartLat; Lon = StartLon;
            MakeMaterials();
            SetUpScene();
            water = gameObject.AddComponent<EarthWater>();
            water.Init(this);
            Places.Load();
            player = new GameObject("Player").AddComponent<EarthPlayer>();
            player.Init(this);
            gameObject.AddComponent<EarthLandmarks>().Init(this);
            StartGround();
            gameObject.AddComponent<EarthClouds>().Init(this);
            StartTrees();
            for (int i = 0; i < 4; i++)
            {
                var th = new Thread(Work) { IsBackground = true, Name = "EarthTiles" + i };
                th.Start();
            }
        }

        void OnDestroy()
        {
            quit = true;
            lock (jobs) Monitor.PulseAll(jobs);
            if (I == this) I = null;
        }

        // ------------------------------------------------------------ scene

        static Shader Lit()
        {
            // the stylized stand-in for URP's Lit (same property names), then Lit itself
            Shader s = Resources.Load<Shader>("Earth/Shaders/EarthToonLit");
            if (s != null && s.isSupported) return s;
            s = Shader.Find("Universal Render Pipeline/Lit");
            return s != null ? s : Shader.Find("Standard");
        }

        static Color32 Col(uint c) { return new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255); }

        internal static Material Mat(Texture2D tex, float smooth)
        {
            var m = new Material(Lit());
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            return m;
        }

        void MakeMaterials()
        {
            // the realistic ground (EarthGround.shader); plain lit map colours if that is missing
            groundBase = EarthGround.MakeMaterial();
            if (groundBase == null) groundBase = Mat(null, 0.05f);

            var pal = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[256];
            for (int i = 0; i < 256; i++) px[i] = Col(Style.Palette[i]);
            pal.SetPixels32(px); pal.Apply();
            flatMat = Mat(pal, 0.08f);
            roadMat = EarthRoads.MakeMaterial();       // the realistic road surface (EarthRoad.shader); flat colours if missing

            // every building, walls and roofs alike, is drawn by one material (EarthBuilding.shader)
            Material building = EarthBuildings.MakeMaterial();
            wallMats = new Material[Buildings.Chunks];
            for (int k = 0; k < wallMats.Length; k++) wallMats[k] = building;
        }

        void SetUpScene()
        {
            // Whatever else is in the scene (old prototypes, their cameras and text) is switched off, not deleted.
            foreach (GameObject go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go == gameObject) continue;
                Light light = go.GetComponent<Light>();
                bool keepIt = (light != null && light.type == LightType.Directional) || go.name == "Global Volume" || go.GetComponent<EarthHud>() != null;
                if (!keepIt) go.SetActive(false);
            }
            bool sun = false;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = true;
            if (!sun)
            {
                var go = new GameObject("Sun");
                var l = go.AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.2f; l.shadows = LightShadows.Soft;
                l.color = new Color(1f, 0.96f, 0.9f);
                go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.80f, 0.92f);
            EarthLook.Apply();
        }

        // ------------------------------------------------------------ background building

        void Work()
        {
            while (!quit)
            {
                Job job;
                lock (jobs)
                {
                    while (jobs.Count == 0 && !quit) Monitor.Wait(jobs, 500);
                    if (quit) return;
                    int best = 0; long bestD = long.MaxValue;
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        Job j = jobs[i];
                        long dx = Math.Abs(j.X - centreX[j.Level]), dy = Math.Abs(j.Y - centreY[j.Level]);
                        long n = 1L << Zoom[j.Level];
                        if (dx > n / 2) dx = n - dx;
                        // nearest first; among equals the coarser levels, so there is always ground to see
                        long d = (dx * dx + dy * dy) * 8 + (3 - j.Level);
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    job = jobs[best];
                    jobs.RemoveAt(best);
                    working++;
                }
                try
                {
                    TileResult t = TileBuilder.Build(src, Zoom[job.Level], job.X, job.Y, Tex[job.Level]);
                    var p = new Prepared { Key = job.Key, Level = job.Level, Data = t };
                    p.Ground = new Part[t.Ground.Length];
                    for (int i = 0; i < t.Ground.Length; i++) p.Ground[i] = Conv(t.Ground[i]);
                    if (t.Water != null)
                    {
                        p.Water = new Part[t.Water.Length];
                        for (int i = 0; i < t.Water.Length; i++) p.Water[i] = Conv(t.Water[i]);
                    }
                    if (t.Walls != null)
                    {
                        p.Walls = new Part[t.Walls.Length];
                        for (int i = 0; i < t.Walls.Length; i++) p.Walls[i] = Conv(t.Walls[i]);
                    }
                    p.Roofs = Conv(t.Roofs); p.Roads = Conv(t.Roads); p.Trees = Conv(t.Trees); p.Bridges = Conv(t.Bridges);
                    t.Ground = null; t.Walls = null; t.Water = null; t.Roofs = null; t.Roads = null; t.Trees = null; t.Bridges = null;
                    done.Enqueue(p);
                }
                catch (Exception e)
                {
                    Net.LastError = e.GetBaseException().Message;
                    failed.Enqueue(job.Key);
                }
                finally { lock (jobs) working--; }
            }
        }

        static Part Conv(MeshData m)
        {
            if (m == null || m.I.Count == 0) return null;
            int n = m.Count;
            var p = new Part { V = new Vector3[n], N = new Vector3[n], U = new Vector2[n], I = m.I.ToArray() };
            for (int i = 0; i < n; i++)
            {
                p.V[i] = new Vector3(m.P[i * 3], m.P[i * 3 + 1], m.P[i * 3 + 2]);
                p.N[i] = new Vector3(m.N[i * 3], m.N[i * 3 + 1], m.N[i * 3 + 2]);
                p.U[i] = new Vector2(m.U[i * 2], m.U[i * 2 + 1]);
            }
            if (m.X != null && m.X.Count == n * 4 && m.Y != null && m.Y.Count == n * 4)
            {
                p.U1 = new Vector4[n]; p.U2 = new Vector4[n];
                for (int i = 0; i < n; i++)
                {
                    p.U1[i] = new Vector4(m.X[i * 4], m.X[i * 4 + 1], m.X[i * 4 + 2], m.X[i * 4 + 3]);
                    p.U2[i] = new Vector4(m.Y[i * 4], m.Y[i * 4 + 1], m.Y[i * 4 + 2], m.Y[i * 4 + 3]);
                }
            }
            return p;
        }

        // ------------------------------------------------------------ main loop

        void Update()
        {
            if (player == null) return;
            Vector3 pos = player.transform.position;
            double lat, lon, alt;
            Frame.ToGeo(pos.x, pos.y, pos.z, out lat, out lon, out alt);
            Lat = lat; Lon = lon; Alt = (float)alt;

            if (pos.x * pos.x + pos.z * pos.z > 2000f * 2000f) Reanchor(lat, lon, alt);

            long key;
            while (failed.TryDequeue(out key)) { retryAt[key] = Time.unscaledTime + 4f; Error = Net.LastError; }

            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.2f; Refresh(); }

            Prepared p;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 0.008f && done.TryDequeue(out p))
            {
                requested.Remove(p.Key);
                if (keep.Contains(p.Key) && !tiles.ContainsKey(p.Key)) { Show(p); Error = null; }
            }

            if (colliders.Count > 0)
            {
                var c = colliders.Dequeue();
                if (c.Value != null && tiles.ContainsKey(c.Key)) c.Value.gameObject.AddComponent<MeshCollider>().sharedMesh = c.Value.sharedMesh;
            }

            if (patchesDirty) { patchesDirty = false; UpdatePatches(); }

            float far = Mathf.Clamp(Alt * 30f + 9000f, 9000f, 400000f);
            RenderSettings.fogStartDistance = far * 0.35f;
            RenderSettings.fogEndDistance = far;
        }

        void Refresh()
        {
            wanted.Clear(); keep.Clear();
            for (int level = 0; level < Zoom.Length; level++)
            {
                if (Alt > MaxAlt[level]) continue;
                int z = Zoom[level], n = 1 << z;
                int cx = (int)Math.Floor(Geo.LonToX(Lon, z)), cy = (int)Math.Floor(Geo.LatToY(Lat, z));
                centreX[level] = cx; centreY[level] = cy;
                int r = Radius[level];
                for (int dy = -r - 1; dy <= r + 1; dy++)
                    for (int dx = -r - 1; dx <= r + 1; dx++)
                    {
                        int y = cy + dy;
                        if (y < 0 || y >= n) continue;
                        int x = ((cx + dx) % n + n) % n;
                        long k = KeyOf(level, x, y);
                        keep.Add(k);
                        if (Math.Abs(dx) > r || Math.Abs(dy) > r) continue;
                        wanted.Add(k);
                        if (tiles.ContainsKey(k) || requested.Contains(k)) continue;
                        float at;
                        if (retryAt.TryGetValue(k, out at)) { if (Time.unscaledTime < at) continue; retryAt.Remove(k); }
                        requested.Add(k);
                        lock (jobs) { jobs.Add(new Job { Key = k, Level = level, X = x, Y = y }); Monitor.Pulse(jobs); }
                    }
            }
            // forget work that is no longer near, and drop squares that are far away
            lock (jobs)
            {
                for (int i = jobs.Count - 1; i >= 0; i--)
                    if (!keep.Contains(jobs[i].Key)) { requested.Remove(jobs[i].Key); jobs.RemoveAt(i); }
            }
            List<long> drop = null;
            foreach (var kv in tiles) if (!keep.Contains(kv.Key)) { if (drop == null) drop = new List<long>(); drop.Add(kv.Key); }
            if (drop != null) foreach (long k in drop) Unload(k);
        }

        void Show(Prepared p)
        {
            TileResult d = p.Data;
            var t = new Tile { Key = p.Key, Level = p.Level, X = d.X, Y = d.Y, Data = d, Frame = new Frame(d.Lat, d.Lon) };
            t.Root = new GameObject("Tile " + d.Z + "/" + d.X + "/" + d.Y);
            t.Root.transform.SetParent(transform, false);

            t.Texture = new Texture2D(d.TexSize, d.TexSize, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.Texture.SetPixelData(d.Texture, 0);          // top level only; Apply builds the smaller ones
            t.Texture.Apply(true, true);
            d.Texture = null;
            t.Material = new Material(groundBase);
            t.Material.mainTexture = t.Texture;
            if (t.Material.HasProperty("_BaseMap")) t.Material.SetTexture("_BaseMap", t.Texture);

            t.Patches = new GameObject[p.Ground.Length];
            for (int i = 0; i < p.Ground.Length; i++)
            {
                MeshFilter mf = Child(t, "Ground", p.Ground[i], t.Material, false);
                if (mf == null) continue;
                t.Patches[i] = mf.gameObject;
                if (p.Level == 0) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            // the water surface of each patch hangs under that patch, so it hides and shows with it
            if (p.Water != null && d.WMask != null)
            {
                // with smaller copies, so the shader can read a softened outline for the foam along shores
                t.WaterMask = new Texture2D(d.MaskN, d.MaskN, TextureFormat.R8, true, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                t.WaterMask.SetPixelData(d.WMask, 0);
                t.WaterMask.Apply(true, true);
                if (waterBlock == null) waterBlock = new MaterialPropertyBlock();
                waterBlock.Clear();
                waterBlock.SetTexture("_WaterMask", t.WaterMask);
                waterBlock.SetFloat("_TileSize", d.Size);
            }
            if (p.Water != null)
                for (int i = 0; i < p.Water.Length && i < t.Patches.Length; i++)
                {
                    if (p.Water[i] == null || t.Patches[i] == null) continue;
                    MeshFilter mf = Child(t, "Water", p.Water[i], water.Material, false);
                    mf.transform.SetParent(t.Patches[i].transform, false);
                    mf.gameObject.layer = WaterReflection.WaterLayer;
                    // the coarse squares' ground is sunk a little to sit under the finer ones; the water is not, so
                    // the sea is level all the way to the horizon instead of stepping down at each ring
                    mf.transform.localPosition = new Vector3(0f, Sink[p.Level], 0f);
                    MeshRenderer wr = mf.GetComponent<MeshRenderer>();
                    wr.receiveShadows = true;
                    if (t.WaterMask != null) wr.SetPropertyBlock(waterBlock);
                }
            if (p.Walls != null)
                for (int i = 0; i < p.Walls.Length; i++)
                {
                    MeshFilter mf = Child(t, "Walls", p.Walls[i], wallMats[i], true);
                    if (mf != null) colliders.Enqueue(new KeyValuePair<long, MeshFilter>(t.Key, mf));
                }
            MeshFilter roofs = Child(t, "Roofs", p.Roofs, wallMats[0], true);
            if (roofs != null) colliders.Enqueue(new KeyValuePair<long, MeshFilter>(t.Key, roofs));
            Child(t, "Roads", p.Roads, roadMat != null ? roadMat : flatMat, false);
            MeshFilter decks = Child(t, "Bridges", p.Bridges, flatMat, true);
            if (decks != null) colliders.Enqueue(new KeyValuePair<long, MeshFilter>(t.Key, decks));
            TreesShown(t);

            Place(t);
            tiles[t.Key] = t;
            patchesDirty = true;
        }

        MeshFilter Child(Tile t, string name, Part p, Material mat, bool shadows)
        {
            if (p == null) return null;
            var mesh = new Mesh();
            if (p.V.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = p.V; mesh.normals = p.N; mesh.uv = p.U;
            if (p.U1 != null) { mesh.SetUVs(1, p.U1); mesh.SetUVs(2, p.U2); }
            mesh.triangles = p.I;
            mesh.RecalculateBounds();
            t.Meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(t.Root.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return mf;
        }

        void Unload(long key)
        {
            Tile t;
            if (!tiles.TryGetValue(key, out t)) return;
            tiles.Remove(key);
            TreesGone(key);
            foreach (Mesh m in t.Meshes) Destroy(m);
            Destroy(t.Texture); Destroy(t.Material); Destroy(t.Root);
            if (t.WaterMask != null) Destroy(t.WaterMask);
            patchesDirty = true;
        }

        /// <summary>Put a square where it belongs on the globe, as seen from the current anchor.</summary>
        void Place(Tile t)
        {
            double x, y, z, fx, fy, fz, ux, uy, uz;
            Frame.ToLocal(t.Data.Lat, t.Data.Lon, 0, out x, out y, out z);
            Frame.DirFrom(t.Frame, 0, 0, 1, out fx, out fy, out fz);
            Frame.DirFrom(t.Frame, 0, 1, 0, out ux, out uy, out uz);
            t.Root.transform.SetPositionAndRotation(
                new Vector3((float)x, (float)y - Sink[t.Level], (float)z),
                Quaternion.LookRotation(new Vector3((float)fx, (float)fy, (float)fz), new Vector3((float)ux, (float)uy, (float)uz)));
        }

        /// <summary>A coarse patch is hidden once the finer square that covers it has arrived.</summary>
        void UpdatePatches()
        {
            foreach (Tile t in tiles.Values)
            {
                if (t.Level == 0) continue;
                for (int i = 0; i < t.Patches.Length; i++)
                {
                    if (t.Patches[i] == null) continue;
                    int px = i % 4, py = i / 4;
                    bool covered = tiles.ContainsKey(KeyOf(t.Level - 1, t.X * 4 + px, t.Y * 4 + py));
                    if (t.Patches[i].activeSelf == covered) t.Patches[i].SetActive(!covered);
                }
            }
        }

        // ------------------------------------------------------------ moving about the globe

        void Reanchor(double lat, double lon, double alt)
        {
            Vector3 was = player.transform.position;
            Frame old = Frame;
            Frame = new Frame(lat, lon);
            Vector3 f = player.transform.forward;
            double fx, fy, fz;
            Frame.DirFrom(old, f.x, f.y, f.z, out fx, out fy, out fz);
            foreach (Tile t in tiles.Values) Place(t);
            player.Moved(new Vector3(0f, (float)alt, 0f), new Vector3((float)fx, 0f, (float)fz));
            Physics.SyncTransforms();
            water.Shifted(new Vector3(was.x, 0f, was.z));
            GroundShifted(new Vector3(-was.x, (float)alt - was.y, -was.z));
        }

        /// <summary>Jump to a spot. facing (degrees clockwise from north) turns the player that way.</summary>
        public void Teleport(double lat, double lon, float facing = float.NaN)
        {
            Frame = new Frame(lat, lon);
            Lat = lat; Lon = lon;
            foreach (Tile t in tiles.Values) Place(t);
            player.Arrive();
            water.Clear();
            GroundJumped();
            if (!float.IsNaN(facing)) player.Look(facing);
            nextRefresh = 0;
        }

        /// <summary>
        /// World height of the ground under a point, from the most detailed square loaded there. Over seas, lakes
        /// and rivers that is the water surface, unless bed is true (then the sea floor or lake bed).
        /// </summary>
        public bool Ground(Vector3 world, out float height, bool detailedOnly = false, bool bed = false)
        {
            double lat, lon, alt;
            Frame.ToGeo(world.x, world.y, world.z, out lat, out lon, out alt);
            int levels = detailedOnly ? 1 : Zoom.Length;
            for (int level = 0; level < levels; level++)
            {
                int z = Zoom[level], n = 1 << z;
                int x = ((int)Math.Floor(Geo.LonToX(lon, z)) % n + n) % n, y = (int)Math.Floor(Geo.LatToY(lat, z));
                Tile t;
                if (!tiles.TryGetValue(KeyOf(level, x, y), out t)) continue;
                Vector3 local = t.Root.transform.InverseTransformPoint(world);
                float standing = local.y, deckY;
                local.y = bed ? t.Data.BedAt(local.x, local.z) : t.Data.HeightAt(local.x, local.z);
                // a bridge deck counts as ground for anyone on or above it
                if (t.Data.DeckAt(local.x, local.z, standing, out deckY) && deckY > local.y) local.y = deckY;
                height = t.Root.transform.TransformPoint(local).y;
                // the deck of a landmark bridge counts as ground for anyone on or above it
                float deck;
                if (Landmarks.Deck(lat, lon, 0, out deck) && alt > deck - 1.0)
                {
                    double dx, dy, dz;
                    Frame.ToLocal(lat, lon, deck, out dx, out dy, out dz);
                    if ((float)dy > height) height = (float)dy;
                }
                return true;
            }
            height = 0;
            return false;
        }

        /// <summary>
        /// The water surface (world y) over a point, the bed under it, and whether it is a lake or river rather than
        /// the sea, from the most detailed square loaded there. False where there is no water. On a bank right at the
        /// edge, level can be below bed: the water stops before it gets there.
        /// </summary>
        public bool WaterAt(Vector3 world, out float level, out float bed, out bool lake)
        {
            level = bed = 0; lake = false;
            double lat, lon, alt;
            Frame.ToGeo(world.x, world.y, world.z, out lat, out lon, out alt);
            for (int lv = 0; lv < Zoom.Length; lv++)
            {
                int z = Zoom[lv], n = 1 << z;
                int x = ((int)Math.Floor(Geo.LonToX(lon, z)) % n + n) % n, y = (int)Math.Floor(Geo.LatToY(lat, z));
                Tile t;
                if (!tiles.TryGetValue(KeyOf(lv, x, y), out t)) continue;
                Vector3 local = t.Root.transform.InverseTransformPoint(world);
                float wy;
                if (!t.Data.WaterHeightAt(local.x, local.z, out wy)) return false;
                wy += Sink[lv];
                lake = t.Data.LakeAt(local.x, local.z);
                float by = t.Data.BedAt(local.x, local.z);
                level = t.Root.transform.TransformPoint(new Vector3(local.x, wy, local.z)).y;
                bed = t.Root.transform.TransformPoint(new Vector3(local.x, by, local.z)).y;
                return true;
            }
            return false;
        }
    }
}
