using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The material for roads, streets, paths and railways: EarthStreet.shader draws the flat shapes that
    /// Core/RoadNet.cs lays on the ground. Returns null (plain colours) if the shader is missing.
    /// </summary>
    public static class EarthStreets
    {
        public static Material MakeMaterial()
        {
            Shader sh = Shader.Find("MarsNeedsWater/EarthStreet");
            if (sh == null || !sh.isSupported) { Debug.LogWarning("EarthStreet shader missing: plain road colours"); return null; }
            return new Material(sh) { name = "EarthStreet", enableInstancing = false };
        }
    }
}
