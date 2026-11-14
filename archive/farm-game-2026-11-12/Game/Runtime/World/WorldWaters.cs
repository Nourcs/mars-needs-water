using System;
using System.Collections.Generic;
using System.Globalization;
using MarsNeedsWater.Simulation;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Every body of water on the planet (oceans, seas, named lakes, and nameless lakes called after the nearest
    /// town), each a real water source in the simulation with its real-world volume. Baked into
    /// Resources/Earth/earth_water.bytes (which body each 2048 × 1024 map cell belongs to) and earth_waters.txt
    /// (index, name, litres). A shared sea-level trigger under the player (<see cref="Planet"/>) is the water
    /// surface the scoop ray hits; the body is looked up from where it hits.
    /// </summary>
    public static class WorldWaters
    {
        public const int FirstId = 1000;    // simulation source id = FirstId + body index
        const int W = 2048, H = 1024;

        static ushort[] _cells;
        static readonly List<string> _names = new List<string>();
        static readonly List<double> _litres = new List<double>();

        public static bool Load()
        {
            if (_cells != null) return true;
            var map = Resources.Load<TextAsset>("Earth/earth_water");
            var table = Resources.Load<TextAsset>("Earth/earth_waters");
            if (map == null || table == null) { Debug.LogWarning("Earth water bodies missing: seas and lakes cannot be scooped."); return false; }
            var bytes = map.bytes;
            if (bytes.Length != W * H * 2) { Debug.LogWarning("earth_water.bytes has the wrong size."); return false; }
            _cells = new ushort[W * H];
            Buffer.BlockCopy(bytes, 0, _cells, 0, bytes.Length);
            _names.Clear(); _litres.Clear();
            _names.Add(""); _litres.Add(0); // index 0 = land
            foreach (var raw in table.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int a = line.IndexOf(','), b = line.LastIndexOf(',');
                if (a <= 0 || b <= a) continue;
                if (!int.TryParse(line.Substring(0, a), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)) continue;
                if (!double.TryParse(line.Substring(b + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double litres)) continue;
                while (_names.Count <= idx) { _names.Add(""); _litres.Add(0); }
                _names[idx] = line.Substring(a + 1, b - a - 1);
                _litres[idx] = litres;
            }
            return true;
        }

        /// <summary>Adds every body of water to the level as a source the player can scoop from anywhere.</summary>
        public static void Register(LevelData level)
        {
            if (_cells == null) return;
            for (int i = 1; i < _names.Count; i++)
                if (_litres[i] > 0)
                    level.Sources.Add(new SourceDef { Id = FirstId + i, Name = _names[i], StartVolume_L = _litres[i], Anywhere = true, IntakeRadius = 0 });
        }

        public static bool IsWorld(int sourceId) => sourceId > FirstId && sourceId - FirstId < _names.Count;

        public static string Name(int sourceId) => IsWorld(sourceId) ? _names[sourceId - FirstId] : "";

        /// <summary>The water body at a planet direction (the nearest one within a few cells, for shorelines); 0 = none.</summary>
        public static int SourceAt(Vector3 dir)
        {
            if (_cells == null) return 0;
            EarthData.LatLon(dir, out float lat, out float lon);
            int cx = Mathf.FloorToInt((lon + 180f) / 360f * W), cy = Mathf.FloorToInt((90f - lat) / 180f * H);
            for (int r = 0; r <= 3; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                int x = ((cx + dx) % W + W) % W, y = Mathf.Clamp(cy + dy, 0, H - 1);
                int v = _cells[y * W + x];
                if (v > 0 && _litres[v] > 0) return FirstId + v;
            }
            return 0;
        }

        /// <summary>A short readable volume: "710 quintillion L", "12.1 trillion L".</summary>
        public static string Litres(double l)
        {
            string[] names = { "", " thousand", " million", " billion", " trillion", " quadrillion", " quintillion", " sextillion" };
            int k = 0;
            while (l >= 1000 && k < names.Length - 1) { l /= 1000; k++; }
            return (l >= 100 ? l.ToString("0", CultureInfo.InvariantCulture) : l.ToString("0.#", CultureInfo.InvariantCulture)) + names[k] + " L";
        }
    }
}
