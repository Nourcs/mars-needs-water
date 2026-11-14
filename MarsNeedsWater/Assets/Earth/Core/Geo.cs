using System;

namespace MarsNeedsWater.Earth
{
    /// <summary>Scale and projection maths for the mini Earth. One game metre is five real metres.</summary>
    public static class Geo
    {
        public const double Scale = 5.0;
        public const double RealRadius = 6371008.8;
        public const double R = RealRadius / Scale;
        public const double Deg = Math.PI / 180.0;

        public static double TileLon(double x, int z) { return x / (1 << z) * 360.0 - 180.0; }

        public static double TileLat(double y, int z)
        {
            double n = Math.PI * (1.0 - 2.0 * y / (1 << z));
            return Math.Atan(Math.Sinh(n)) / Deg;
        }

        public static double LonToX(double lon, int z) { return (lon + 180.0) / 360.0 * (1 << z); }

        public static double LatToY(double lat, int z)
        {
            if (lat > 85.05) lat = 85.05;
            if (lat < -85.05) lat = -85.05;
            double r = lat * Deg;
            return (1.0 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * (1 << z);
        }

        /// <summary>Width of one tile in game metres at this latitude.</summary>
        public static double TileSize(int z, double lat) { return 2.0 * Math.PI * R * Math.Cos(lat * Deg) / (1 << z); }

        /// <summary>Ground elevation: true 1:5.</summary>
        public static float Elevation(double realMetres) { return (float)(realMetres / Scale); }

        /// <summary>
        /// Height of things that stand on the ground (buildings, trees). Kept taller than 1:5 so a person
        /// can walk among them: half size up to 12 m, a quarter above that. Same rule as the first San Francisco core.
        /// </summary>
        public static float Height(double realMetres)
        {
            if (realMetres <= 12.0) return (float)(realMetres * 0.5);
            return (float)(6.0 + (realMetres - 12.0) * 0.25);
        }

        public static void Normal(double lat, double lon, out double x, out double y, out double z)
        {
            double cl = Math.Cos(lat * Deg);
            x = cl * Math.Cos(lon * Deg); y = cl * Math.Sin(lon * Deg); z = Math.Sin(lat * Deg);
        }
    }

    /// <summary>A flat east/up/north frame touching the globe at one spot. Unity x = east, y = up, z = north.</summary>
    public sealed class Frame
    {
        public readonly double Lat, Lon;
        readonly double ex, ey, ez, nx, ny, nz, ux, uy, uz;

        public Frame(double lat, double lon)
        {
            Lat = lat; Lon = lon;
            double sl = Math.Sin(lat * Geo.Deg), cl = Math.Cos(lat * Geo.Deg);
            double so = Math.Sin(lon * Geo.Deg), co = Math.Cos(lon * Geo.Deg);
            ex = -so; ey = co; ez = 0;
            nx = -sl * co; ny = -sl * so; nz = cl;
            ux = cl * co; uy = cl * so; uz = sl;
        }

        /// <summary>Position of a spot on the globe (alt in game metres) in this frame.</summary>
        public void ToLocal(double lat, double lon, double alt, out double x, out double y, out double z)
        {
            double px, py, pz;
            Geo.Normal(lat, lon, out px, out py, out pz);
            double r = Geo.R + alt;
            px = px * r - ux * Geo.R; py = py * r - uy * Geo.R; pz = pz * r - uz * Geo.R;
            x = px * ex + py * ey + pz * ez;
            y = px * ux + py * uy + pz * uz;
            z = px * nx + py * ny + pz * nz;
        }

        public void ToGeo(double x, double y, double z, out double lat, out double lon, out double alt)
        {
            double px = ux * Geo.R + x * ex + y * ux + z * nx;
            double py = uy * Geo.R + x * ey + y * uy + z * ny;
            double pz = uz * Geo.R + x * ez + y * uz + z * nz;
            double r = Math.Sqrt(px * px + py * py + pz * pz);
            lat = Math.Asin(pz / r) / Geo.Deg;
            lon = Math.Atan2(py, px) / Geo.Deg;
            alt = r - Geo.R;
        }

        /// <summary>A direction given in another frame's axes (east, up, north), expressed in this frame.</summary>
        public void DirFrom(Frame o, double x, double y, double z, out double rx, out double ry, out double rz)
        {
            double wx = x * o.ex + y * o.ux + z * o.nx;
            double wy = x * o.ey + y * o.uy + z * o.ny;
            double wz = x * o.ez + y * o.uz + z * o.nz;
            rx = wx * ex + wy * ey + wz * ez;
            ry = wx * ux + wy * uy + wz * uz;
            rz = wx * nx + wy * ny + wz * nz;
        }
    }
}
