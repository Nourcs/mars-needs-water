using System;

namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// The game's own plain-data position and heading (Game State Rule 2: no engine types in state).
    /// Metres in world space; Yaw in degrees around the up axis. Pitch and roll are presentation only.
    /// </summary>
    [Serializable]
    public struct Pose : IEquatable<Pose>
    {
        public double X;
        public double Y;
        public double Z;
        public double Yaw;

        /// <summary>Creates a pose.</summary>
        public Pose(double x, double y, double z, double yaw = 0)
        {
            X = x; Y = y; Z = z; Yaw = yaw;
        }

        /// <summary>Straight-line distance between the two positions, in metres.</summary>
        public double DistanceTo(Pose o)
        {
            double dx = X - o.X, dy = Y - o.Y, dz = Z - o.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Horizontal (XZ) distance, in metres.</summary>
        public double FlatDistanceTo(Pose o)
        {
            double dx = X - o.X, dz = Z - o.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>This pose raised by <paramref name="dy"/> metres.</summary>
        public Pose Raised(double dy) => new Pose(X, Y + dy, Z, Yaw);

        /// <summary>A point given in this pose's local frame (x right, y up, z forward), rotated by Yaw.</summary>
        public Pose Offset(double lx, double ly, double lz)
        {
            double r = Yaw * Math.PI / 180.0;
            double c = Math.Cos(r), s = Math.Sin(r);
            // Unity convention: yaw rotates clockwise seen from above; forward (0,0,1) -> (sin, 0, cos).
            double wx = lx * c + lz * s;
            double wz = -lx * s + lz * c;
            return new Pose(X + wx, Y + ly, Z + wz, Yaw);
        }

        public bool Equals(Pose o) => X == o.X && Y == o.Y && Z == o.Z && Yaw == o.Yaw;
        public override bool Equals(object obj) => obj is Pose p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, Yaw);
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###}, yaw {Yaw:0.#})";
    }

    /// <summary>An axis-aligned rectangle on the ground plane (XZ), used for authored zones.</summary>
    [Serializable]
    public struct ZoneRect
    {
        public double MinX, MinZ, MaxX, MaxZ;
        /// <summary>Ground height of the zone (metres).</summary>
        public double Y;

        public ZoneRect(double minX, double minZ, double maxX, double maxZ, double y = 0)
        {
            MinX = Math.Min(minX, maxX); MaxX = Math.Max(minX, maxX);
            MinZ = Math.Min(minZ, maxZ); MaxZ = Math.Max(minZ, maxZ);
            Y = y;
        }

        /// <summary>True when the point lies inside the rectangle (edges included), ignoring height.</summary>
        public bool Contains(double x, double z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
    }
}
