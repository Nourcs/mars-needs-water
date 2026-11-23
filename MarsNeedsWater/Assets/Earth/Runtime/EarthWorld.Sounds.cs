using System;
using UnityEngine;

namespace MarsNeedsWater.Earth
{
    public sealed partial class EarthWorld
    {
        /// <summary>
        /// How built-up the map square around a point is: buildings per square kilometre, from the closest level.
        /// False where that level is not loaded yet.
        /// </summary>
        public bool BuildingsNear(Vector3 world, out float perSqKm)
        {
            perSqKm = 0f;
            double lat, lon, alt;
            Frame.ToGeo(world.x, world.y, world.z, out lat, out lon, out alt);
            int z = Zoom[0], n = 1 << z;
            int x = ((int)Math.Floor(Geo.LonToX(lon, z)) % n + n) % n, y = (int)Math.Floor(Geo.LatToY(lat, z));
            Tile t;
            if (!tiles.TryGetValue(KeyOf(0, x, y), out t) || t.Data == null || t.Data.Size <= 0f) return false;
            perSqKm = t.Data.BuildingCount / (t.Data.Size * t.Data.Size) * 1e6f;
            return true;
        }
    }
}
