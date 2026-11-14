using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MarsNeedsWater.Earth.Editor
{
    /// <summary>
    /// Renders the player's jeep from the front and the back into jeep-preview.png next to the Unity project.
    /// Run it from Tools > Mars Needs Water > Render Jeep Preview, or by creating Temp/jeep-preview.request
    /// (it runs once after the next script reload and deletes the request).
    /// </summary>
    static class JeepPreview
    {
        const string Request = "Temp/jeep-preview.request";

        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            if (!File.Exists(Request)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
                File.Delete(Request);
                Render();
            };
        }

        [MenuItem("Tools/Mars Needs Water/Render Jeep Preview")]
        static void Render()
        {
            const int w = 1200, h = 900;
            Scene scene = EditorSceneManager.NewPreviewScene();
            var made = new System.Collections.Generic.List<Object>();
            try
            {
                var holder = new GameObject("Preview");
                SceneManager.MoveGameObjectToScene(holder, scene);
                Jeep jeep = Jeep.Build();
                jeep.Body.isKinematic = true;
                jeep.transform.SetParent(holder.transform, false);
                jeep.transform.localPosition = new Vector3(0f, -Jeep.Ground, 0f);   // tyres on y = 0
                jeep.transform.GetChild(1).localRotation = Quaternion.Euler(0f, 18f, 0f);   // turn a front wheel to show the steering
                jeep.transform.GetChild(2).localRotation = Quaternion.Euler(0f, 18f, 0f);
                foreach (var f in holder.GetComponentsInChildren<MeshFilter>()) made.Add(f.sharedMesh);
                foreach (var r in holder.GetComponentsInChildren<MeshRenderer>()) { made.Add(r.sharedMaterial); made.Add(r.sharedMaterial.mainTexture); }

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.localScale = Vector3.one * 4f;
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                gm.SetColor("_BaseColor", new Color(0.62f, 0.55f, 0.43f));
                gm.SetFloat("_Smoothness", 0.05f);
                ground.GetComponent<MeshRenderer>().sharedMaterial = gm;
                made.Add(gm);

                var sunGo = new GameObject("Sun");
                SceneManager.MoveGameObjectToScene(sunGo, scene);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.3f; sun.shadows = LightShadows.Soft;
                sun.color = new Color(1f, 0.96f, 0.9f);
                sunGo.transform.rotation = Quaternion.Euler(42f, -140f, 0f);

                var camGo = new GameObject("Camera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.70f, 0.80f, 0.92f);
                cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;

                var sheet = new Texture2D(w * 2, h, TextureFormat.RGB24, false);
                Vector3 aim = new Vector3(0f, 0.7f, 0f);
                Vector3[] eyes = { new Vector3(5.2f, 2.6f, 6.4f), new Vector3(-5.4f, 3.0f, -5.8f) };
                for (int i = 0; i < eyes.Length; i++)
                {
                    cam.transform.position = eyes[i];
                    cam.transform.LookAt(aim);
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, w, h), i * w, 0);
                    RenderTexture.active = null;
                }
                sheet.Apply();
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "jeep-preview.png"));
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                Debug.Log("Jeep preview written to " + path);
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
