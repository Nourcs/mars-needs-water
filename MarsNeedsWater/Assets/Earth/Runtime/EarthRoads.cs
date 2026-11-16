using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The road material: EarthRoad.shader draws asphalt, lines, paving, rails and so on from the numbers
    /// Core/RoadLook.cs puts on each road vertex. Returns null (flat map colours) if the shader is missing.
    /// </summary>
    public static class EarthRoads
    {
        public static Material MakeMaterial()
        {
            Shader sh = Shader.Find("MarsNeedsWater/EarthRoad");
            if (sh == null || !sh.isSupported) { Debug.LogWarning("EarthRoad shader missing: flat road colours"); return null; }
            var m = new Material(sh) { name = "EarthRoad" };
            m.enableInstancing = false;
            return m;
        }
    }
}
