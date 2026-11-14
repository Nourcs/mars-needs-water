using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Real towns and cities (Natural Earth populated places, Resources/Earth/earth_places.txt) placed on the planet.
    /// Each has a radius from its population. A coarse 2° bucket grid makes "is this spot in a town?" cheap enough
    /// to ask for every terrain vertex. Read-only after loading.
    /// </summary>
    public static class Places
    {
        public struct Place
        {
            public string Name;
            public float Lat, Lon, Pop, Radius;
            public Vector3 Dir;
        }

        public static readonly List<Place> All = new List<Place>();
        const int Bucket = 2; // degrees
        static readonly Dictionary<int, List<int>> _grid = new Dictionary<int, List<int>>();
        static bool _loaded;

        /// <summary>Town radius in metres on the 1 : 255 planet: a village is a few streets, Tokyo a big blob.</summary>
        public static float RadiusFor(float pop) => Mathf.Clamp(22f + Mathf.Sqrt(pop) * 0.075f, 30f, 260f);

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var txt = Resources.Load<TextAsset>("Earth/earth_places");
            if (txt == null) { Debug.LogWarning("Earth/earth_places missing: no towns."); return; }
            foreach (var raw in txt.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                // name may contain commas: the last three fields are numbers.
                int c3 = line.LastIndexOf(','), c2 = c3 > 0 ? line.LastIndexOf(',', c3 - 1) : -1, c1 = c2 > 0 ? line.LastIndexOf(',', c2 - 1) : -1;
                if (c1 <= 0) continue;
                if (!float.TryParse(line.Substring(c1 + 1, c2 - c1 - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float lat)) continue;
                if (!float.TryParse(line.Substring(c2 + 1, c3 - c2 - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float lon)) continue;
                if (!float.TryParse(line.Substring(c3 + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float pop)) continue;
                var p = new Place { Name = line.Substring(0, c1), Lat = lat, Lon = lon, Pop = pop, Radius = RadiusFor(pop), Dir = EarthData.Dir(lat, lon) };
                int idx = All.Count;
                All.Add(p);
                // Register in every bucket the town could touch (radius ≤ 260 m ≈ 0.6°, so neighbours suffice).
                int bx = Bx(lon), by = By(lat);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = Key(bx + dx, by + dy);
                    if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>();
                    list.Add(idx);
                }
            }
        }

        static int Bx(float lon) => Mathf.FloorToInt((lon + 180f) / Bucket);
        static int By(float lat) => Mathf.FloorToInt((lat + 90f) / Bucket);
        static int Key(int bx, int by)
        {
            int nx = 360 / Bucket;
            bx = ((bx % nx) + nx) % nx;
            return by * 1000 + bx;
        }

        /// <summary>How "town" a spot is (0 countryside .. 1 downtown), for painting streets into the ground.</summary>
        public static float Urban(Vector3 dir, float lat, float lon)
        {
            if (!_grid.TryGetValue(Key(Bx(lon), By(lat)), out var list)) return 0f;
            float best = 0f;
            foreach (int i in list)
            {
                var p = All[i];
                float d = (dir - p.Dir).magnitude * Planet.Radius;
                if (d >= p.Radius * 1.15f) continue;
                best = Mathf.Max(best, 1f - Mathf.SmoothStep(0f, 1f, (d - p.Radius * 0.75f) / (p.Radius * 0.4f)));
            }
            return best;
        }
    }

    /// <summary>The world's famous places, at their real latitude and longitude.</summary>
    public static class Landmarks
    {
        public enum Kind
        {
            Eiffel, Liberty, Pyramids, TajMahal, Colosseum, BigBen, OperaHouse, Redeemer, GoldenGate, Burj, Pisa,
            GreatWall, MachuPicchu, Stonehenge, Moai, SpaceNeedle, CNTower, StBasil, Sagrada, TokyoTower, AngkorWat,
            ChichenItza, Parthenon, Brandenburg, Hollywood
        }

        public struct Landmark
        {
            public string Name, Where;
            public float Lat, Lon, Yaw;
            public Kind Kind;
            public Vector3 Dir;
        }

        public static readonly List<Landmark> All = new List<Landmark>();

        static void Add(Kind k, string name, string where, float lat, float lon, float yaw = 0f)
            => All.Add(new Landmark { Kind = k, Name = name, Where = where, Lat = lat, Lon = lon, Yaw = yaw, Dir = EarthData.Dir(lat, lon) });

        static Landmarks()
        {
            Add(Kind.Eiffel, "Eiffel Tower", "Paris, France", 48.8584f, 2.2945f);
            Add(Kind.Liberty, "Statue of Liberty", "New York, USA", 40.6892f, -74.0445f, 140f);
            Add(Kind.Pyramids, "Pyramids of Giza", "Cairo, Egypt", 29.9792f, 31.1342f);
            Add(Kind.TajMahal, "Taj Mahal", "Agra, India", 27.1751f, 78.0421f);
            Add(Kind.Colosseum, "Colosseum", "Rome, Italy", 41.8902f, 12.4922f, 20f);
            Add(Kind.BigBen, "Big Ben", "London, UK", 51.5007f, -0.1246f, 90f); // the palace runs south along the Thames
            Add(Kind.OperaHouse, "Sydney Opera House", "Sydney, Australia", -33.8568f, 151.2153f, 195f); // the sails open north-north-east onto the harbour
            Add(Kind.Redeemer, "Christ the Redeemer", "Rio de Janeiro, Brazil", -22.9519f, -43.2105f, 60f);
            Add(Kind.GoldenGate, "Golden Gate Bridge", "San Francisco, USA", 39.299f, -122.031f, 0f); // at the north-west tip of the rebuilt city (CityReplica), not its true spot
            Add(Kind.Burj, "Burj Khalifa", "Dubai, UAE", 25.1972f, 55.2744f);
            Add(Kind.Pisa, "Leaning Tower of Pisa", "Pisa, Italy", 43.7230f, 10.3966f);
            Add(Kind.GreatWall, "Great Wall of China", "Mutianyu, China", 40.4319f, 116.5704f, 25f);
            Add(Kind.MachuPicchu, "Machu Picchu", "Cusco, Peru", -13.1631f, -72.5450f);
            Add(Kind.Stonehenge, "Stonehenge", "Wiltshire, UK", 51.1789f, -1.8262f);
            Add(Kind.Moai, "Moai", "Easter Island, Chile", -27.1258f, -109.2770f, 200f);
            Add(Kind.SpaceNeedle, "Space Needle", "Seattle, USA", 47.6205f, -122.3493f);
            Add(Kind.CNTower, "CN Tower", "Toronto, Canada", 43.6426f, -79.3871f);
            Add(Kind.StBasil, "St Basil's Cathedral", "Moscow, Russia", 55.7525f, 37.6231f);
            Add(Kind.Sagrada, "Sagrada Família", "Barcelona, Spain", 41.4036f, 2.1744f);
            Add(Kind.TokyoTower, "Tokyo Tower", "Tokyo, Japan", 35.6586f, 139.7454f);
            Add(Kind.AngkorWat, "Angkor Wat", "Siem Reap, Cambodia", 13.4125f, 103.8670f);
            Add(Kind.ChichenItza, "Chichén Itzá", "Yucatán, Mexico", 20.6843f, -88.5678f);
            Add(Kind.Parthenon, "Parthenon", "Athens, Greece", 37.9715f, 23.7267f);
            Add(Kind.Brandenburg, "Brandenburg Gate", "Berlin, Germany", 52.5163f, 13.3777f, 90f); // the avenue runs through it east to west
            Add(Kind.Hollywood, "Hollywood Sign", "Los Angeles, USA", 34.1341f, -118.3215f, 0f);
        }

        /// <summary>Metres of town kept clear around a landmark so buildings do not grow through it.</summary>
        public const float Clearing = 70f;
    }
}
