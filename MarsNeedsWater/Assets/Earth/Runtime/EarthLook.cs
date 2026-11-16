using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The stylized look of the whole scene, in one place: the painted sky (EarthSky.shader), a warm sun, bright
    /// blue-violet fill light so nothing goes black, haze in the horizon's colour, and a final grade that pushes
    /// colour and adds a soft glow. The surfaces' own part of the look is in Shaders/EarthToon.hlsl.
    /// </summary>
    public static class EarthLook
    {
        public static readonly Color Horizon = new Color(0.78f, 0.92f, 1.0f);

        public static void Apply()
        {
            Shader sky = Resources.Load<Shader>("Earth/Shaders/EarthSky");
            if (sky != null && sky.isSupported)
            {
                var m = new Material(sky) { name = "Earth sky" };
                m.SetColor("_Horizon", Horizon);
                RenderSettings.skybox = m;
            }
            RenderSettings.fogColor = Horizon;

            // light from the sky, the horizon and the ground, set by hand so shade is always colourful
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.78f, 1.0f);
            RenderSettings.ambientEquatorColor = new Color(0.60f, 0.66f, 0.86f);
            RenderSettings.ambientGroundColor = new Color(0.50f, 0.47f, 0.52f);

            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.color = new Color(1f, 0.95f, 0.82f);
                l.intensity = 1.25f;
                l.shadowStrength = 1f;
            }

            // the final grade, over whatever the scene's own volume does
            var go = new GameObject("Earth look");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true; v.priority = 20f;
            VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
            v.profile = p;
            var grade = p.Add<ColorAdjustments>(true);
            grade.postExposure.Override(0.1f);
            grade.contrast.Override(10f);
            grade.saturation.Override(14f);
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.72f);
            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.16f);
        }
    }
}
