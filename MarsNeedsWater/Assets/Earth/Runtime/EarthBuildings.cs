using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The building material: EarthBuilding.shader draws walls, windows, balconies, shop fronts and roof coverings
    /// from the numbers Core/Buildings.cs puts on each vertex. One material for every building in the world.
    /// Returns a plain lit material if the shader is missing.
    /// </summary>
    public static class EarthBuildings
    {
        public static Material MakeMaterial()
        {
            Shader sh = Shader.Find("MarsNeedsWater/EarthBuilding");
            if (sh == null || !sh.isSupported)
            {
                Debug.LogWarning("EarthBuilding shader missing: plain buildings");
                return EarthWorld.Mat(null, 0.1f);
            }
            var m = new Material(sh) { name = "EarthBuilding" };
            m.enableInstancing = false;
            return m;
        }
    }
}
