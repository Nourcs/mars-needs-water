using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MarsNeedsWater.Earth.Editor
{
    /// <summary>
    /// Renders the buildings of a few places into Logs/building-preview/*.png without entering Play: one picture
    /// from above and one from the street for each. Tools > Mars Needs Water > Render Building Preview, or create
    /// Temp/building-preview.request with lines of "name lat lon" (picked up within a second, unless playing).
    /// </summary>
    [InitializeOnLoad]
    static class BuildingPreview
    {
        const string Request = "Temp/building-preview.request";
        static double next;

        static readonly string[] Default =
        {
            "paris 48.8698 2.3320", "sf 37.7763 -122.4330", "nyc 40.7295 -73.9875", "tunis 36.7985 10.1710", "london 51.5290 -0.1050", "tokyo 35.7120 139.7800",
        };

        static BuildingPreview() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Request)) return;
            string[] lines = File.ReadAllLines(Request);
            File.Delete(Request);
            Render(lines.Length > 0 && lines[0].Trim().Length > 0 ? lines : Default);
        }

        [MenuItem("Tools/Mars Needs Water/Render Building Preview")]
        static void RenderDefault() { Render(Default); }

        static void Render(string[] places)
        {
            Directory.CreateDirectory("Logs/building-preview");
            var src = new TileSources(Application.persistentDataPath);
            Material mat = EarthBuildings.MakeMaterial();
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMat.SetColor("_BaseColor", new Color(0.36f, 0.37f, 0.38f));
            groundMat.SetFloat("_Smoothness", 0.05f);
            foreach (string line in places)
            {
                string[] p = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) continue;
                try { One(src, mat, groundMat, p[0], double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture)); }
                catch (System.Exception e) { Debug.LogError("Building preview " + p[0] + ": " + e); File.WriteAllText("Logs/building-preview/" + p[0] + ".error.txt", e.ToString()); }
            }
            Object.DestroyImmediate(mat); Object.DestroyImmediate(groundMat);
            File.WriteAllText("Logs/building-preview/done.txt", System.DateTime.Now.ToString("o"));
        }

        static Mesh ToMesh(MeshData m)
        {
            if (m == null || m.I.Count == 0) return null;
            int n = m.Count;
            var v = new Vector3[n]; var nn = new Vector3[n]; var u = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(m.P[i * 3], m.P[i * 3 + 1], m.P[i * 3 + 2]);
                nn[i] = new Vector3(m.N[i * 3], m.N[i * 3 + 1], m.N[i * 3 + 2]);
                u[i] = new Vector2(m.U[i * 2], m.U[i * 2 + 1]);
            }
            var mesh = new Mesh { indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = v; mesh.normals = nn; mesh.uv = u;
            if (m.X != null && m.X.Count == n * 4)
            {
                var a = new List<Vector4>(n); var b = new List<Vector4>(n);
                for (int i = 0; i < n; i++)
                {
                    a.Add(new Vector4(m.X[i * 4], m.X[i * 4 + 1], m.X[i * 4 + 2], m.X[i * 4 + 3]));
                    b.Add(new Vector4(m.Y[i * 4], m.Y[i * 4 + 1], m.Y[i * 4 + 2], m.Y[i * 4 + 3]));
                }
                mesh.SetUVs(1, a); mesh.SetUVs(2, b);
            }
            mesh.triangles = m.I.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void One(TileSources src, Material mat, Material groundMat, string name, double lat, double lon)
        {
            const int w = 1500, h = 1000;
            int X = (int)Geo.LonToX(lon, 14), Y = (int)Geo.LatToY(lat, 14);
            TileResult t = TileBuilder.Build(src, 14, X, Y, 256);
            float cx = (float)((Geo.LonToX(lon, 14) - X - 0.5) * t.Size), cz = (float)((0.5 - (Geo.LatToY(lat, 14) - Y)) * t.Size);
            float cy = t.HeightAt(cx, cz);

            Scene scene = EditorSceneManager.NewPreviewScene();
            var made = new List<Object>();
            try
            {
                var all = new List<MeshData>(t.Walls) { t.Roofs };
                foreach (MeshData md in all)
                {
                    Mesh mesh = ToMesh(md);
                    if (mesh == null) continue;
                    made.Add(mesh);
                    var go = new GameObject("Buildings");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
                Mesh gm = ToMesh(t.Ground[0]);
                made.Add(gm);
                var gg = new GameObject("Ground");
                SceneManager.MoveGameObjectToScene(gg, scene);
                gg.AddComponent<MeshFilter>().sharedMesh = gm;
                gg.AddComponent<MeshRenderer>().sharedMaterial = groundMat;

                var sunGo = new GameObject("Sun");
                SceneManager.MoveGameObjectToScene(sunGo, scene);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
                sun.color = new Color(1f, 0.96f, 0.9f);
                sunGo.transform.rotation = Quaternion.Euler(44f, -150f, 0f);

                var camGo = new GameObject("Camera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.74f, 0.9f);
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;

                var sheet = new Texture2D(w * 2, h * 2, TextureFormat.RGB24, false);
                Vector3 c = new Vector3(cx, cy, cz);
                // from above; from a rooftop; down the street; close to a wall
                Vector3[] eyes = { c + new Vector3(55f, 48f, -62f), c + new Vector3(-20f, 13f, -24f), c + new Vector3(9f, 1.2f, -11f), c + new Vector3(-4.5f, 1.6f, -5f) };
                Vector3[] aims = { c + new Vector3(0f, 2f, 0f), c + new Vector3(0f, 3f, 0f), c + new Vector3(0f, 2.6f, 0f), c + new Vector3(0f, 2.2f, 0f) };
                float[] fovs = { 34f, 46f, 60f, 55f };
                for (int i = 0; i < eyes.Length; i++)
                {
                    cam.fieldOfView = fovs[i];
                    cam.transform.position = eyes[i];
                    cam.transform.LookAt(aims[i]);
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, w, h), (i % 2) * w, (1 - i / 2) * h);
                    RenderTexture.active = null;
                }
                sheet.Apply();
                File.WriteAllBytes("Logs/building-preview/" + name + ".png", sheet.EncodeToPNG());
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            }
        }
    }
}
