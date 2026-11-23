using System;

namespace MarsNeedsWater.Earth
{
    /// <summary>One famous place. Those with a Build have their own model; the rest are places to go to.</summary>
    public sealed class Landmark
    {
        public string Name, Where;
        public double Lat, Lon;
        public float Yaw;                 // degrees clockwise from north that the model's front (+z) faces
        public Action<Sculpt> Build;      // null: no model, the map itself shows the place
        public double ClearLat, ClearLon; // centre of the area where plain map buildings are left out
        public double ClearRadius;        // real metres
        public float View;                // how far in front of it (game metres) "go to" puts you; negative: its front is the -z side; 0: off to one side
        public bool AtSeaLevel;           // stands in water: use sea level, not the sea bed
        // a bridge deck you can walk and drive on: it runs along the model's z axis, real metres
        public double DeckHalfLength, DeckHalfWidth, DeckTop;
    }

    /// <summary>The landmarks, at their real latitude and longitude.</summary>
    public static class Landmarks
    {
        static Landmark L(Action<Sculpt> build, string name, string where, double lat, double lon, float yaw, double clear, float view,
                          double clearLat = double.NaN, double clearLon = double.NaN, bool sea = false,
                          double deckLength = 0, double deckWidth = 0, double deckTop = 0)
        {
            return new Landmark
            {
                Build = build, Name = name, Where = where, Lat = lat, Lon = lon, Yaw = yaw,
                ClearLat = double.IsNaN(clearLat) ? lat : clearLat, ClearLon = double.IsNaN(clearLon) ? lon : clearLon,
                ClearRadius = clear, View = view, AtSeaLevel = sea,
                DeckHalfLength = deckLength / 2, DeckHalfWidth = deckWidth / 2, DeckTop = deckTop
            };
        }

        public static readonly Landmark[] All =
        {
            L(LandmarkModels.Eiffel, "Eiffel Tower", "Paris, France", 48.85837, 2.29448, 45f, 70, 70f),
            L(LandmarkModels.Liberty, "Statue of Liberty", "New York, USA", 40.68925, -74.04450, 147f, 60, 45f),
            L(LandmarkModels.Pyramids, "Pyramids of Giza", "Cairo, Egypt", 29.97918, 31.13420, 0f, 620, 110f, clearLat: 29.9761, clearLon: 31.1308),
            L(LandmarkModels.Taj, "Taj Mahal", "Agra, India", 27.17501, 78.04214, 0f, 55, 45f),
            L(LandmarkModels.Colosseum, "Colosseum", "Rome, Italy", 41.89021, 12.49223, 18f, 100, 60f),
            L(LandmarkModels.BigBen, "Big Ben and the Houses of Parliament", "London, UK", 51.50073, -0.12462, 8f, 150, 45f, clearLat: 51.4994, clearLon: -0.1247),
            L(LandmarkModels.OperaHouse, "Sydney Opera House", "Sydney, Australia", -33.85678, 151.21530, 15f, 100, 60f),
            L(LandmarkModels.Redeemer, "Christ the Redeemer", "Rio de Janeiro, Brazil", -22.95192, -43.21049, 105f, 15, 22f),
            L(LandmarkModels.GoldenGate, "Golden Gate Bridge", "San Francisco, USA", 37.81989, -122.47857, -5.15f, 0, 0f, sea: true, deckLength: 2000, deckWidth: 27, deckTop: 69),
            L(LandmarkModels.Sutro, "Sutro Tower", "San Francisco, USA", 37.75524, -122.45286, 0f, 62, 80f),
            L(LandmarkModels.Burj, "Burj Khalifa", "Dubai, UAE", 25.19720, 55.27438, 0f, 100, 90f),
            L(LandmarkModels.Pisa, "Leaning Tower of Pisa", "Pisa, Italy", 43.72295, 10.39660, 0f, 12, 25f),
            L(LandmarkModels.Stonehenge, "Stonehenge", "Wiltshire, UK", 51.17888, -1.82621, 0f, 0, 18f),
            L(LandmarkModels.Moai, "Moai of Ahu Tongariki", "Easter Island, Chile", -27.12580, -109.27700, 320f, 0, 25f),
            L(LandmarkModels.SpaceNeedle, "Space Needle", "Seattle, USA", 47.62051, -122.34930, 0f, 30, 45f),
            L(LandmarkModels.CNTower, "CN Tower", "Toronto, Canada", 43.64257, -79.38706, 0f, 40, 70f),
            L(LandmarkModels.StBasil, "St Basil's Cathedral", "Moscow, Russia", 55.75252, 37.62309, 0f, 30, 25f),
            L(LandmarkModels.Sagrada, "Sagrada Família", "Barcelona, Spain", 41.40363, 2.17436, -45f, 60, 50f),
            L(LandmarkModels.TokyoTower, "Tokyo Tower", "Tokyo, Japan", 35.65858, 139.74543, 0f, 45, 60f),
            L(LandmarkModels.Angkor, "Angkor Wat", "Siem Reap, Cambodia", 13.41247, 103.86696, 0f, 120, 70f),
            L(LandmarkModels.Chichen, "Chichén Itzá", "Yucatán, Mexico", 20.68430, -88.56779, 21f, 32, 25f),
            L(LandmarkModels.Parthenon, "Parthenon", "Athens, Greece", 37.97152, 23.72672, -13f, 40, 25f),
            L(LandmarkModels.Brandenburg, "Brandenburg Gate", "Berlin, Germany", 52.51627, 13.37770, 90f, 35, 25f),
            L(LandmarkModels.ArcDeTriomphe, "Arc de Triomphe", "Paris, France", 48.87378, 2.29503, 115f, 28, 30f),
            L(LandmarkModels.TowerBridge, "Tower Bridge", "London, UK", 51.50550, -0.07538, 22.40f, 0, 0f, sea: true, deckLength: 264, deckWidth: 16, deckTop: 10),
            L(LandmarkModels.LondonEye, "London Eye", "London, UK", 51.50329, -0.11951, 118f, 0, 45f),
            L(LandmarkModels.WashingtonMonument, "Washington Monument", "Washington DC, USA", 38.88948, -77.03528, 0f, 12, 45f),
            L(LandmarkModels.GatewayArch, "Gateway Arch", "St. Louis, USA", 38.62470, -90.18497, -90f, 0, 55f),
            L(LandmarkModels.Hollywood, "Hollywood Sign", "Los Angeles, USA", 34.13411, -118.32156, 12f, 0, 45f),
            L(LandmarkModels.HarbourBridge, "Sydney Harbour Bridge", "Sydney, Australia", -33.85223, 151.21067, 27.20f, 0, 0f, sea: true, deckLength: 560, deckWidth: 49, deckTop: 52.4),
            L(LandmarkModels.Kaaba, "Kaaba", "Mecca, Saudi Arabia", 21.42249, 39.82620, 40f, 12, 15f),
            L(LandmarkModels.HagiaSophia, "Hagia Sophia", "Istanbul, Türkiye", 41.00853, 28.98017, 33f, 60, 40f),
            L(LandmarkModels.Petronas, "Petronas Towers", "Kuala Lumpur, Malaysia", 3.15785, 101.71165, 38f, 90, 80f),
            L(LandmarkModels.Atomium, "Atomium", "Brussels, Belgium", 50.89494, 4.34153, 0f, 50, 40f),
            L(LandmarkModels.OrientalPearl, "Oriental Pearl Tower", "Shanghai, China", 31.23969, 121.49976, 0f, 45, 70f),
            L(LandmarkModels.Taipei101, "Taipei 101", "Taipei, Taiwan", 25.03361, 121.56476, 0f, 45, 75f),
            L(LandmarkModels.LouvrePyramid, "Louvre Pyramid", "Paris, France", 48.86103, 2.33583, 25f, 20, 25f),
            L(LandmarkModels.NotreDame, "Notre-Dame de Paris", "Paris, France", 48.85296, 2.34990, 116f, 70, -45f),
            L(LandmarkModels.StPeters, "St Peter's Basilica", "Vatican City", 41.90217, 12.45394, 270f, 115, -70f),
            L(LandmarkModels.MarinaBaySands, "Marina Bay Sands", "Singapore", 1.28389, 103.86074, 8f, 190, 90f),
            L(LandmarkModels.Capitol, "United States Capitol", "Washington DC, USA", 38.88984, -77.00905, 0f, 120, 60f),
            L(LandmarkModels.Skytree, "Tokyo Skytree", "Tokyo, Japan", 35.71006, 139.81070, 0f, 45, 80f),
            // famous places the map already shows: natural wonders and sites
            L(null, "Mount Everest", "Nepal and China", 27.98810, 86.92500, 0f, 0, 0f),
            L(null, "Grand Canyon", "Arizona, USA", 36.05790, -112.14310, 0f, 0, 0f),
            L(null, "Niagara Falls", "Canada and USA", 43.07990, -79.07470, 0f, 0, 0f),
            L(null, "Machu Picchu", "Cusco, Peru", -13.16310, -72.54500, 0f, 0, 0f),
            L(null, "Great Wall of China at Mutianyu", "Beijing, China", 40.43190, 116.57040, 0f, 0, 0f),
            L(null, "Petra", "Jordan", 30.32850, 35.44440, 0f, 0, 0f),
            L(null, "Uluru", "Northern Territory, Australia", -25.34440, 131.03690, 0f, 0, 0f),
            L(null, "Mount Fuji", "Japan", 35.36060, 138.72740, 0f, 0, 0f),
            L(null, "Victoria Falls", "Zambia and Zimbabwe", -17.92430, 25.85720, 0f, 0, 0f),
            L(null, "Matterhorn", "Switzerland and Italy", 45.97630, 7.65860, 0f, 0, 0f),
            L(null, "Table Mountain", "Cape Town, South Africa", -33.96280, 18.40980, 0f, 0, 0f),
            L(null, "Mount Kilimanjaro", "Tanzania", -3.06740, 37.35560, 0f, 0, 0f),
            L(null, "Iguazu Falls", "Argentina and Brazil", -25.69530, -54.43670, 0f, 0, 0f),
            L(null, "Yosemite Valley", "California, USA", 37.74560, -119.59360, 0f, 0, 0f),
            L(null, "Mont Blanc", "France and Italy", 45.83260, 6.86520, 0f, 0, 0f),
            L(null, "Dead Sea", "Jordan and Israel", 31.50000, 35.50000, 0f, 0, 0f),
            L(null, "Giant's Causeway", "Northern Ireland, UK", 55.24080, -6.51160, 0f, 0, 0f),
            L(null, "Ha Long Bay", "Vietnam", 20.91010, 107.18390, 0f, 0, 0f),
            L(null, "Forbidden City", "Beijing, China", 39.91630, 116.39720, 0f, 0, 0f),
            L(null, "Neuschwanstein Castle", "Bavaria, Germany", 47.55760, 10.74980, 0f, 0, 0f),
            L(null, "Times Square", "New York, USA", 40.75800, -73.98550, 0f, 0, 0f),
            L(null, "Empire State Building", "New York, USA", 40.74840, -73.98570, 0f, 0, 0f),
            L(null, "Central Park", "New York, USA", 40.78290, -73.96540, 0f, 0, 0f),
            L(null, "Mont-Saint-Michel", "Normandy, France", 48.63610, -1.51150, 0f, 0, 0f),
            L(null, "Alhambra", "Granada, Spain", 37.17600, -3.58810, 0f, 0, 0f),
            L(null, "Red Square", "Moscow, Russia", 55.75390, 37.62080, 0f, 0, 0f),
            L(null, "St Mark's Square", "Venice, Italy", 45.43410, 12.33880, 0f, 0, 0f),
            L(null, "Mount Rushmore", "South Dakota, USA", 43.87910, -103.45910, 0f, 0, 0f),
            L(null, "Carthage", "Tunis, Tunisia", 36.85280, 10.32330, 0f, 0, 0f),
            L(null, "Sidi Bou Said", "Tunisia", 36.87030, 10.34140, 0f, 0, 0f),
            L(null, "Amphitheatre of El Jem", "Tunisia", 35.29640, 10.70690, 0f, 0, 0f),
            L(null, "Louvre Museum", "Paris, France", 48.86060, 2.33760, 0f, 0, 0f),
            L(null, "Buckingham Palace", "London, UK", 51.50140, -0.14190, 0f, 0, 0f),
            L(null, "Acropolis of Athens", "Athens, Greece", 37.97150, 23.72570, 0f, 0, 0f),
            L(null, "Burj Al Arab", "Dubai, UAE", 25.14120, 55.18530, 0f, 0, 0f),
            L(null, "Hoover Dam", "Nevada and Arizona, USA", 36.01600, -114.73770, 0f, 0, 0f),
            L(null, "Palace of Versailles", "Versailles, France", 48.80490, 2.12040, 0f, 0, 0f),
            L(null, "Great Sphinx of Giza", "Cairo, Egypt", 29.97530, 31.13760, 0f, 0, 0f),
            L(null, "White House", "Washington DC, USA", 38.89770, -77.03650, 0f, 0, 0f),
            L(null, "Golden Gate Park", "San Francisco, USA", 37.76940, -122.48620, 0f, 0, 0f),
            L(null, "Alcatraz Island", "San Francisco, USA", 37.82700, -122.42300, 0f, 0, 0f),
        };

        static Landmark[] decks;

        /// <summary>
        /// Is this spot on (or within margin real metres of) the deck of a landmark bridge?
        /// top is the deck surface in game metres above sea level.
        /// </summary>
        public static bool Deck(double lat, double lon, double margin, out float top)
        {
            if (decks == null) decks = Array.FindAll(All, l => l.DeckHalfLength > 0);
            foreach (Landmark l in decks)
            {
                double dz = (lat - l.Lat) * 111195.0;
                if (Math.Abs(dz) > l.DeckHalfLength + margin + 50) continue;
                double dx = (lon - l.Lon) * 111195.0 * Math.Cos(l.Lat * Geo.Deg);
                double a = l.Yaw * Geo.Deg, cs = Math.Cos(a), sn = Math.Sin(a);
                double mx = dx * cs - dz * sn, mz = dx * sn + dz * cs;
                if (Math.Abs(mx) <= l.DeckHalfWidth + margin && Math.Abs(mz) <= l.DeckHalfLength + margin) { top = (float)(l.DeckTop / Geo.Scale); return true; }
            }
            top = 0;
            return false;
        }

        /// <summary>
        /// Is this spot on the run-up to a landmark bridge: the first reach real metres of road beyond either end
        /// of its deck? The road there is carried up to the deck, whether or not the map calls it a bridge.
        /// </summary>
        public static bool Landing(double lat, double lon, double reach)
        {
            if (decks == null) decks = Array.FindAll(All, l => l.DeckHalfLength > 0);
            foreach (Landmark l in decks)
            {
                double dz = (lat - l.Lat) * 111195.0;
                if (Math.Abs(dz) > l.DeckHalfLength + reach + 50) continue;
                double dx = (lon - l.Lon) * 111195.0 * Math.Cos(l.Lat * Geo.Deg);
                double a = l.Yaw * Geo.Deg, cs = Math.Cos(a), sn = Math.Sin(a);
                double mx = dx * cs - dz * sn, mz = Math.Abs(dx * sn + dz * cs);
                if (Math.Abs(mx) <= l.DeckHalfWidth + 10 && mz > l.DeckHalfLength && mz <= l.DeckHalfLength + reach) return true;
            }
            return false;
        }

        /// <summary>
        /// Map roads are drawn wider than life; a landmark bridge is true to size. So over the last 150 real metres
        /// before a deck end, a road (width real metres as drawn) is drawn in towards the bridge's centre line and
        /// narrowed to fit on the deck. Gives the new spot and the width factor; false if this spot is not affected.
        /// </summary>
        public static bool DrawIn(double lat, double lon, double width, out double lat2, out double lon2, out float narrow)
        {
            lat2 = lat; lon2 = lon; narrow = 1;
            if (decks == null) decks = Array.FindAll(All, l => l.DeckHalfLength > 0);
            const double Reach = 150;
            foreach (Landmark l in decks)
            {
                double dz = (lat - l.Lat) * 111195.0;
                if (Math.Abs(dz) > l.DeckHalfLength + Reach + 80) continue;
                double k = 111195.0 * Math.Cos(l.Lat * Geo.Deg), dx = (lon - l.Lon) * k;
                double a = l.Yaw * Geo.Deg, cs = Math.Cos(a), sn = Math.Sin(a);
                double mx = dx * cs - dz * sn, mz = dx * sn + dz * cs;
                double beyond = Math.Abs(mz) - l.DeckHalfLength;
                if (beyond >= Reach || beyond < -20 || Math.Abs(mx) > l.DeckHalfWidth + 60) continue;
                double f = Math.Max(0, beyond) / Reach;
                f = f * f * (3 - 2 * f);
                double fit = Math.Min(width, l.DeckHalfWidth * 0.8);          // two carriageways share the deck
                double limit = Math.Max(0, l.DeckHalfWidth - fit / 2 - 1);
                double to = Math.Max(-limit, Math.Min(limit, mx));
                mx = to + (mx - to) * f;
                narrow = (float)((fit + (width - fit) * f) / width);
                dx = mx * cs + mz * sn; dz = -mx * sn + mz * cs;
                lat2 = l.Lat + dz / 111195.0; lon2 = l.Lon + dx / k;
                return true;
            }
            return false;
        }

        static readonly System.Collections.Generic.Dictionary<Landmark, System.Collections.Generic.HashSet<long>> shadows = new System.Collections.Generic.Dictionary<Landmark, System.Collections.Generic.HashSet<long>>();

        /// <summary>
        /// Does this landmark's model stand over the spot (game metres east and north of the landmark)?
        /// Plain map buildings there are left out, so none pokes through the model.
        /// </summary>
        public static bool Covers(Landmark l, double east, double north)
        {
            if (l.Build == null) return false;
            System.Collections.Generic.HashSet<long> cells;
            lock (shadows)
            {
                if (!shadows.TryGetValue(l, out cells))
                {
                    cells = new System.Collections.Generic.HashSet<long>();
                    MeshData m = Model(l).Mesh;
                    for (int t = 0; t + 2 < m.I.Count; t += 3)
                    {
                        int a = m.I[t] * 3, b = m.I[t + 1] * 3, c = m.I[t + 2] * 3;
                        double ax = m.P[a], az = m.P[a + 2], bx = m.P[b], bz = m.P[b + 2], cx = m.P[c], cz = m.P[c + 2];
                        double longest = Math.Max(Math.Max(Math.Abs(bx - ax) + Math.Abs(bz - az), Math.Abs(cx - ax) + Math.Abs(cz - az)), Math.Abs(cx - bx) + Math.Abs(cz - bz));
                        int steps = Math.Min(400, Math.Max(1, (int)Math.Ceiling(longest / 0.7)));
                        for (int i = 0; i <= steps; i++)
                            for (int j = 0; i + j <= steps; j++)
                            {
                                double u = (double)i / steps, v = (double)j / steps;
                                double x = ax + (bx - ax) * u + (cx - ax) * v, z = az + (bz - az) * u + (cz - az) * v;
                                cells.Add((long)Math.Floor(x) * 2000003L + (long)Math.Floor(z));
                            }
                    }
                    shadows[l] = cells;
                }
            }
            long ex = (long)Math.Floor(east), nz = (long)Math.Floor(north);
            for (long i = ex - 2; i <= ex + 2; i++)
                for (long j = nz - 2; j <= nz + 2; j++)
                    if (cells.Contains(i * 2000003L + j)) return true;
            return false;
        }

        /// <summary>The model of a landmark at game size, or null if it has none.</summary>
        public static LandmarkMesh Model(Landmark l)
        {
            if (l.Build == null) return null;
            var s = new Sculpt();
            l.Build(s);
            return s.Finish(l.Yaw);
        }
    }
}
