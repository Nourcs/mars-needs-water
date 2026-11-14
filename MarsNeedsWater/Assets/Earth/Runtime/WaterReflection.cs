using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// A mirror image of the world for the water near the camera: a second camera renders the scene flipped about
    /// the water's surface (at half the screen's size), and the water shader blends it in where the surface reflects.
    /// Off when no water is near, or the camera is under it.
    /// </summary>
    public sealed class WaterReflection : MonoBehaviour
    {
        public const int WaterLayer = 4;         // Unity's built-in "Water" layer: the water itself is never reflected

        static readonly int IdTex = Shader.PropertyToID("_WaterReflection"), IdOn = Shader.PropertyToID("_WaterReflectionOn"),
            IdLevel = Shader.PropertyToID("_WaterReflectionLevel");

        EarthWorld world;
        Camera mirror;
        RenderTexture target;
        bool hooked;

        public void Init(EarthWorld w)
        {
            world = w;
            var go = new GameObject("Water reflection camera");
            go.transform.SetParent(transform, false);
            mirror = go.AddComponent<Camera>();
            mirror.enabled = false;
            mirror.cullingMask = ~(1 << WaterLayer);
            mirror.clearFlags = CameraClearFlags.Skybox;
            mirror.allowMSAA = false;
            mirror.useOcclusionCulling = false;
            mirror.depth = -10;
            QuietCamera(mirror);
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
            hooked = true;
            Shader.SetGlobalFloat(IdOn, 0f);
        }

        /// <summary>No shadows or post effects for the mirror camera (set through URP's camera data, found by name).</summary>
        static void QuietCamera(Camera cam)
        {
            try
            {
                Type t = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (t == null) return;
                Component data = cam.GetComponent(t);
                if (data == null) data = cam.gameObject.AddComponent(t);
                foreach (var pair in new[] { "renderShadows", "renderPostProcessing", "requiresDepthTexture", "requiresColorTexture" })
                {
                    var prop = t.GetProperty(pair);
                    if (prop != null && prop.CanWrite) prop.SetValue(data, false);
                }
            }
            catch { }
        }

        void OnDestroy()
        {
            if (hooked) { RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End; }
            if (target != null) { target.Release(); Destroy(target); }
            Shader.SetGlobalFloat(IdOn, 0f);
        }

        // the mirror flips the world inside out, so faces must be culled the other way round while it draws
        void Begin(ScriptableRenderContext ctx, Camera cam) { if (cam == mirror) GL.invertCulling = true; }
        void End(ScriptableRenderContext ctx, Camera cam) { if (cam == mirror) GL.invertCulling = false; }

        void LateUpdate()
        {
            Camera main = Camera.main;
            float level;
            if (main == null || world == null || !NearWater(main.transform.position, out level))
            {
                mirror.enabled = false;
                Shader.SetGlobalFloat(IdOn, 0f);
                return;
            }

            int w = Mathf.Max(64, Screen.width / 2), h = Mathf.Max(64, Screen.height / 2);
            if (target == null || target.width != w || target.height != h)
            {
                if (target != null) { target.Release(); Destroy(target); }
                target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf) { name = "Water reflection" };
                target.Create();
                mirror.targetTexture = target;
            }

            mirror.fieldOfView = main.fieldOfView;
            mirror.aspect = main.aspect;
            mirror.nearClipPlane = main.nearClipPlane;
            mirror.farClipPlane = Mathf.Min(main.farClipPlane, 20000f);
            mirror.backgroundColor = main.backgroundColor;

            // world flipped about the plane y = level
            Matrix4x4 flip = Matrix4x4.identity;
            flip.m11 = -1f; flip.m13 = 2f * level;
            Vector3 eye = main.transform.position;
            mirror.transform.position = new Vector3(eye.x, 2f * level - eye.y, eye.z);
            mirror.transform.rotation = main.transform.rotation;
            mirror.worldToCameraMatrix = main.worldToCameraMatrix * flip;

            // nothing below the water's surface may end up in the picture
            Matrix4x4 view = mirror.worldToCameraMatrix;
            Vector3 cp = view.MultiplyPoint(new Vector3(0f, level - 0.05f, 0f));
            Vector3 cn = view.MultiplyVector(Vector3.up).normalized;
            mirror.projectionMatrix = main.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn)));
            mirror.enabled = true;

            Shader.SetGlobalTexture(IdTex, target);
            Shader.SetGlobalFloat(IdOn, 1f);
            Shader.SetGlobalFloat(IdLevel, level);
        }

        /// <summary>The level of the water near the camera, if the camera is above some within about 300 m below.</summary>
        bool NearWater(Vector3 eye, out float level)
        {
            level = 0;
            float bed; bool lake;
            if (world.WaterAt(eye, out level, out bed, out lake) && level > bed) return eye.y > level + 0.05f && eye.y - level < 300f;
            // not over water: look a little way ahead and around
            Vector3 fwd = Camera.main.transform.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
            foreach (float d in new[] { 15f, 40f, 90f, 200f })
                foreach (float s in new[] { 0f, -0.6f, 0.6f })
                {
                    Vector3 p = eye + (fwd + side * s) * d;
                    if (world.WaterAt(p, out level, out bed, out lake) && level > bed && eye.y > level + 0.05f && eye.y - level < 300f) return true;
                }
            return false;
        }
    }
}
