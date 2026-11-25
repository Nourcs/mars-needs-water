using System;
using UnityEngine;

namespace MarsNeedsWater.Earth
{
    // What the ground is made of, for the grass and stones near the player (EarthGrass).
    public sealed partial class EarthWorld
    {
        /// <summary>The world was moved by this much (re-centred on the player): add it to anything kept in world space.</summary>
        public static event Action<Vector3> Shifted;
        /// <summary>The player jumped somewhere else: drop anything kept for the old place.</summary>
        public static event Action Jumped;

        public Transform PlayerTransform { get { return player != null ? player.transform : null; } }

        void StartGround()
        {
            if (!Voxel.On) gameObject.AddComponent<EarthGrass>().Init(this);     // no blades of grass on blocks
        }

        void GroundShifted(Vector3 delta)
        {
            EarthGround.Shift(new Vector2(delta.x, delta.z));
            if (Shifted != null) Shifted(delta);
        }

        void GroundJumped()
        {
            if (Jumped != null) Jumped();
        }

        Tile lastInfo;

        /// <summary>
        /// The ground under a point from the closest level only: its height (world y), its GroundKind and its colour.
        /// False where that level is not loaded yet or where there is water.
        /// </summary>
        public bool GroundInfo(Vector3 world, out float y, out byte kind, out Color32 tint)
        {
            y = 0; kind = GroundKind.Paved; tint = new Color32(128, 128, 128, 255);
            Tile t = lastInfo;
            Vector3 local = Vector3.zero;
            bool inside = false;
            if (t != null && t.Root != null && tiles.ContainsKey(t.Key))
            {
                local = t.Root.transform.InverseTransformPoint(world);
                float h = t.Data.Size * 0.5f;
                inside = local.x > -h && local.x < h && local.z > -h && local.z < h;
            }
            if (!inside)
            {
                double lat, lon, alt;
                Frame.ToGeo(world.x, world.y, world.z, out lat, out lon, out alt);
                int z = Zoom[0], n = 1 << z;
                int x = ((int)Math.Floor(Geo.LonToX(lon, z)) % n + n) % n, yy = (int)Math.Floor(Geo.LatToY(lat, z));
                if (!tiles.TryGetValue(KeyOf(0, x, yy), out t) || t.Data.Kinds == null) return false;
                lastInfo = t;
                local = t.Root.transform.InverseTransformPoint(world);
            }
            float bed = t.Data.BedAt(local.x, local.z), wy;
            if (t.Data.WaterHeightAt(local.x, local.z, out wy) && wy > bed + 0.02f) return false;
            kind = t.Data.KindAt(local.x, local.z);
            byte r, g, b;
            t.Data.TintAt(local.x, local.z, out r, out g, out b);
            tint = new Color32(r, g, b, 255);
            y = t.Root.transform.TransformPoint(new Vector3(local.x, bed, local.z)).y;
            return true;
        }
    }
}
