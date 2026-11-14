using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>The list of towns and cities you can jump to (Resources/Earth/places.txt, from Overture divisions).</summary>
    public static class Places
    {
        public struct Place
        {
            public string Name, Region; public double Lat, Lon; public int Pop; public string Lower, Alt;
            public bool Landmark;            // a famous place rather than a town
            public double GoLat, GoLon;      // where "go to" puts you (in front of a landmark)
            public float Facing;             // which way you then look, degrees clockwise from north; NaN = unchanged
        }

        public static readonly List<Place> All = new List<Place>();
        public static readonly List<Place> Famous = new List<Place>();

        public static void Load()
        {
            All.Clear(); Famous.Clear();
            foreach (Landmark l in Landmarks.All) Famous.Add(FromLandmark(l));
            var asset = Resources.Load<TextAsset>("Earth/places");
            string text = asset != null ? asset.text :
                "San Francisco|US-CA|37.7793|-122.4193|873965\nParis|FR|48.8566|2.3522|2148000\nLondon|GB|51.5074|-0.1278|8800000\n" +
                "New York|US-NY|40.7128|-74.0060|8336000\nTokyo|JP|35.6762|139.6503|13960000\nTunis|TN|36.8065|10.1815|638845\n";
            foreach (string line in text.Split('\n'))
            {
                string[] f = line.Split('|');
                if (f.Length < 5) continue;
                double lat, lon; int pop;
                if (!double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)) continue;
                if (!double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out lon)) continue;
                int.TryParse(f[4].Trim(), out pop);
                string alt = f.Length > 5 ? f[5].Trim() : "";
                All.Add(new Place { Name = alt.Length > 0 ? alt + " (" + f[0] + ")" : f[0], Region = f[1], Lat = lat, Lon = lon, GoLat = lat, GoLon = lon, Facing = float.NaN, Pop = pop, Lower = f[0].ToLowerInvariant(), Alt = alt.ToLowerInvariant() });
            }
        }

        /// <summary>Lower case without accents, so "sagrada familia" finds "Sagrada Família".</summary>
        public static string Plain(string s)
        {
            string d = (s ?? "").Normalize(System.Text.NormalizationForm.FormD);
            var b = new System.Text.StringBuilder(d.Length);
            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                b.Append(c == '\'' || c == '\u2019' || c == '-' ? ' ' : char.ToLowerInvariant(c));
            }
            return b.ToString().Replace("  ", " ").Trim();
        }

        static Place FromLandmark(Landmark l)
        {
            double goLat = l.Lat, goLon = l.Lon; float facing = float.NaN;
            if (l.Build != null)
            {
                // stand in front of it (bridges: off to one side) and look back at it
                float bearing = l.View > 0 ? l.Yaw : l.View < 0 ? l.Yaw + 180f : l.Yaw + 90f, dist = l.View != 0 ? Math.Abs(l.View) : 160f;
                double real = dist * Geo.Scale, b = bearing * Geo.Deg;
                goLat = l.Lat + real * Math.Cos(b) / 111195.0;
                goLon = l.Lon + real * Math.Sin(b) / (111195.0 * Math.Cos(l.Lat * Geo.Deg));
                facing = bearing + 180f;
            }
            return new Place { Name = l.Name, Region = l.Where, Lat = l.Lat, Lon = l.Lon, GoLat = goLat, GoLon = goLon, Facing = facing, Landmark = true, Lower = Plain(l.Name), Alt = Plain(l.Where) };
        }

        /// <summary>Best matches for what was typed: names that start with it first, biggest places first.</summary>
        public static List<Place> Find(string query, int max)
        {
            var result = new List<Place>();
            string plain = Plain(query);
            query = (query ?? "").Trim().ToLowerInvariant();
            if (query.Length == 0) return result;
            // famous places first: by name, or (after the towns) by where they are
            var nearby = new List<Place>();
            if (plain.Length >= 2)
                foreach (Place p in Famous)
                {
                    int i = p.Lower.IndexOf(plain, StringComparison.Ordinal);
                    bool word = i == 0 || (i > 0 && p.Lower[i - 1] == ' ');
                    if (word || (i > 0 && plain.Length >= 4)) { if (result.Count < 5) result.Add(p); }
                    else if (plain.Length >= 3 && p.Alt.IndexOf(plain, StringComparison.Ordinal) >= 0) nearby.Add(p);
                }
            int townMax = max - Math.Min(nearby.Count, 3);
            var starts = new List<Place>(); var contains = new List<Place>();
            foreach (Place p in All)
            {
                int i = p.Lower.IndexOf(query, StringComparison.Ordinal);
                if (i != 0 && p.Alt.Length > 0) { int k = p.Alt.IndexOf(query, StringComparison.Ordinal); if (k >= 0 && (i < 0 || k < i)) i = k; }
                if (i == 0) starts.Add(p); else if (i > 0 && query.Length >= 3) contains.Add(p);
            }
            starts.Sort((a, b) => b.Pop.CompareTo(a.Pop));
            contains.Sort((a, b) => b.Pop.CompareTo(a.Pop));
            foreach (Place p in starts) { if (result.Count >= townMax) break; result.Add(p); }
            foreach (Place p in contains) { if (result.Count >= townMax) break; result.Add(p); }
            foreach (Place p in nearby) { if (result.Count >= max) break; result.Add(p); }
            return result;
        }

        /// <summary>A typed "lat, lon" pair, so any spot on Earth can be reached.</summary>
        public static bool ParseLatLon(string s, out double lat, out double lon)
        {
            lat = lon = 0;
            string[] f = (s ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return f.Length == 2 &&
                   double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
                   double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon) &&
                   Math.Abs(lat) <= 85 && Math.Abs(lon) <= 180;
        }

        public static bool Nearest(double lat, double lon, out Place best, out double km)
        {
            best = default(Place); km = 1e9;
            double cos = Math.Cos(lat * Geo.Deg);
            double bestScore = 1e18;
            bool found = false;
            // standing at a famous place beats the town it is in
            foreach (Place p in Famous)
            {
                double dy = p.Lat - lat, dx = (p.Lon - lon) * cos, d2 = dx * dx + dy * dy;
                if (d2 < 0.004 * 0.004 && d2 < bestScore) { bestScore = d2; best = p; km = Math.Sqrt(d2) * 111.2; found = true; }
            }
            if (found) return true;
            foreach (Place p in All)
            {
                double dy = p.Lat - lat, dx = (p.Lon - lon) * cos;
                double d2 = dx * dx + dy * dy;
                if (d2 > 1.0) continue;
                // prefer big places a little, so a suburb does not beat the city you are in
                double score = d2 / (1.0 + Math.Log10(Math.Max(10, p.Pop)) * 0.5);
                if (score < bestScore) { bestScore = score; best = p; km = Math.Sqrt(d2) * 111.2; found = true; }
            }
            return found;
        }
    }
}
