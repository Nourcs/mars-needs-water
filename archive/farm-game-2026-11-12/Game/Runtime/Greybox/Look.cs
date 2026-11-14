using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The look: a bright, sunny, saturated cartoon day (think colourful battle-royale island, not grim realism).
    /// Sets the sun, a clear blue sky, soft sky-coloured ambient light and a light blue haze, and adds a global
    /// post-processing volume (neutral tonemapping, a punchy grade, soft bloom, a hint of vignette). Presentation only;
    /// everything it creates belongs to the scene and goes away on Restart.
    /// </summary>
    public static class Look
    {
        public static readonly Color Haze = new Color(0.6f, 0.75f, 0.92f);

        public static void Apply(Camera cam)
        {
            Sun();
            Sky();
            Post(cam);
        }

        static void Sun()
        {
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            // A warm afternoon sun, a little lower than noon so hills and buildings get long, readable shadows.
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            sun.color = new Color(1f, 0.92f, 0.78f);
            sun.intensity = 1.9f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;
        }

        static void Sky()
        {
            var sh = Shader.Find("Skybox/Procedural");
            if (sh != null)
            {
                var sky = new Material(sh);
                sky.SetFloat("_SunSize", 0.025f);   // small: a big sun disc bloomed into a white flash when you turned
                sky.SetFloat("_SunSizeConvergence", 10f);
                sky.SetFloat("_AtmosphereThickness", 0.85f);
                sky.SetColor("_SkyTint", new Color(0.46f, 0.6f, 0.88f));
                sky.SetColor("_GroundColor", new Color(0.62f, 0.74f, 0.88f));
                sky.SetFloat("_Exposure", 1.2f);
                RenderSettings.skybox = sky;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Cooler, dimmer fill light than before, so shaded sides read as shade (more shape, less flat pastel).
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.62f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.3f, 0.26f);
            // Clear air: the haze starts far out and only swallows the land near the horizon.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Haze;
            RenderSettings.fogStartDistance = 450f;
            RenderSettings.fogEndDistance = 7000f;
        }

        static void Post(Camera cam)
        {
            if (cam != null)
            {
                cam.farClipPlane = 9000f; // the horizon from a 200 m peak is about 3.5 km away
                cam.fieldOfView = 70f;      // a little wider: more world on screen
                cam.nearClipPlane = 0.08f;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = true;
                    data.stopNaN = true; // a single NaN pixel otherwise blooms into a big flashing white blob
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                }
            }

            // Shadows that reach far enough for trees, towns and landmarks, with four cascades so the ones near
            // you stay sharp. (This edits the project's URP asset, so it sticks after Play too.)
            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                if (urp.shadowDistance < 160f) urp.shadowDistance = 160f;
                urp.shadowCascadeCount = 4;
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(0.1f);
            grade.contrast.Override(20f);
            grade.saturation.Override(12f);
            grade.colorFilter.Override(new Color(1f, 0.99f, 0.97f));
            // Cool shadows, warm highlights: the classic sunny-cartoon split.
            var split = profile.Add<SplitToning>(true);
            split.shadows.Override(new Color(0.47f, 0.5f, 0.56f));     // subtle: stronger values tinted everything purple
            split.highlights.Override(new Color(0.56f, 0.53f, 0.48f));
            split.balance.Override(0f);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.3f);
            bloom.threshold.Override(1.25f);
            bloom.scatter.Override(0.6f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.16f);
            vig.smoothness.Override(0.45f);

            var go = new GameObject("Look_PostVolume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
        }
    }
}
