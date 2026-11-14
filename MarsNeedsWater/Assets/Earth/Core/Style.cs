using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>How each kind of Overture feature looks: colours, widths, default heights.</summary>
    public static class Style
    {
        public struct Road { public float RealWidth; public int Pal; public int Layer; public uint Paint; public float MinPx;
            public Road(float w, int pal, int layer, uint paint, float minPx) { RealWidth = w; Pal = pal; Layer = layer; Paint = paint; MinPx = minPx; } }

        // Palette cells used by roof, road and tree meshes (a 16 x 16 colour swatch texture).
        public const int PalAsphalt = 0, PalAsphaltLight = 1, PalPave = 2, PalDirt = 3, PalRail = 4, PalSteps = 5, PalCycle = 6;
        public const int PalTree = 8, PalRoof = 16, RoofCount = 12, PalFacade = 32, TreeCount = 4;

        public static readonly uint[] Palette = MakePalette();

        static uint[] MakePalette()
        {
            var p = new uint[256];
            for (int i = 0; i < 256; i++) p[i] = 0x808080;
            p[PalAsphalt] = 0x3d3f44; p[PalAsphaltLight] = 0x55575c; p[PalPave] = 0xbdb7ab; p[PalDirt] = 0x9c8763;
            p[PalRail] = 0x5a4f47; p[PalSteps] = 0xa8a299; p[PalCycle] = 0x6f8f72;
            p[PalTree] = 0x3f7d3a; p[PalTree + 1] = 0x4f8f3f; p[PalTree + 2] = 0x356b3b; p[PalTree + 3] = 0x5d9a48;
            uint[] roofs = { 0x8c8f94, 0x9b958a, 0x7b8087, 0xa7a39b, 0x8a6a5a, 0x9c5f4b, 0x6f7479, 0xb3aea6, 0x7f8a86, 0x8d7f73, 0x5f6b78, 0xc2beb6 };
            for (int i = 0; i < roofs.Length; i++) p[PalRoof + i] = roofs[i];
            return p;
        }

        /// <summary>Wall colours; each gets its own window texture.</summary>
        public static readonly uint[] Walls = { 0xd9cfc0, 0xc9b8a3, 0xb7c0c7, 0xe2ddd4, 0xc58f73, 0xa9b3a6, 0x9aa7b5, 0xd6c48f };

        public const uint LandColour = 0xa9b27f, WaterColour = 0x3f7fb5, AsphaltPaint = 0x4a4c51;

        public static readonly Dictionary<string, Road> Roads = new Dictionary<string, Road>
        {
            { "motorway", new Road(13f, PalAsphalt, 6, 0x45474c, 2.4f) },
            { "trunk", new Road(12f, PalAsphalt, 6, 0x45474c, 2.0f) },
            { "primary", new Road(11f, PalAsphalt, 5, 0x484a4f, 1.7f) },
            { "secondary", new Road(10f, PalAsphalt, 5, 0x4a4c51, 1.4f) },
            { "tertiary", new Road(9f, PalAsphalt, 4, 0x4d4f54, 1.2f) },
            { "residential", new Road(7.5f, PalAsphaltLight, 3, 0x55575c, 1.0f) },
            { "living_street", new Road(6f, PalAsphaltLight, 3, 0x5c5e62, 1.0f) },
            { "unclassified", new Road(7f, PalAsphaltLight, 3, 0x55575c, 1.0f) },
            { "unknown", new Road(5f, PalAsphaltLight, 2, 0x5c5e62, 0.8f) },
            { "service", new Road(4f, PalAsphaltLight, 2, 0x606266, 0.8f) },
            { "pedestrian", new Road(6f, PalPave, 2, 0xbdb7ab, 0.8f) },
            { "footway", new Road(2.2f, PalPave, 1, 0xc4beb2, 0.7f) },
            { "steps", new Road(2.2f, PalSteps, 1, 0xa8a299, 0.7f) },
            { "cycleway", new Road(2.2f, PalCycle, 1, 0x6f8f72, 0.7f) },
            { "path", new Road(1.8f, PalDirt, 0, 0x9c8763, 0.7f) },
            { "bridleway", new Road(2.2f, PalDirt, 0, 0x9c8763, 0.7f) },
            { "track", new Road(3.2f, PalDirt, 0, 0x9c8763, 0.7f) },
        };

        public static readonly Dictionary<string, Road> Rails = new Dictionary<string, Road>
        {
            { "standard_gauge", new Road(4.5f, PalRail, 7, 0x5a4f47, 1.2f) },
            { "narrow_gauge", new Road(3.5f, PalRail, 7, 0x5a4f47, 1.0f) },
            { "light_rail", new Road(3.5f, PalRail, 7, 0x5a4f47, 1.0f) },
            { "tram", new Road(2.6f, PalRail, 7, 0x62574f, 0.8f) },
            { "monorail", new Road(2.5f, PalRail, 7, 0x62574f, 0.8f) },
            { "funicular", new Road(3f, PalRail, 7, 0x5a4f47, 0.8f) },
            { "unknown", new Road(4f, PalRail, 7, 0x5a4f47, 1.0f) },
        };

        public static readonly Dictionary<string, uint> LandCover = new Dictionary<string, uint>
        {
            { "forest", 0x4f7f49 }, { "grass", 0x8fb266 }, { "shrub", 0x8c9e6b }, { "crop", 0xbdb873 }, { "barren", 0xc2b394 },
            { "urban", 0xb3b0a8 }, { "wetland", 0x73947f }, { "snow", 0xf2f4f8 }, { "moss", 0x99a87f }, { "mangrove", 0x597f66 },
        };

        public static readonly Dictionary<string, uint> LandKinds = new Dictionary<string, uint>
        {
            { "sand", 0xe0d19e }, { "rock", 0x99948c }, { "glacier", 0xf2f6fa }, { "forest", 0x4f7f49 }, { "grass", 0x8fb266 },
            { "shrub", 0x8c9e6b }, { "wetland", 0x73947f }, { "desert", 0xdcc998 }, { "reef", 0x5f9fb8 },
        };

        public static readonly Dictionary<string, uint> LandUse = new Dictionary<string, uint>
        {
            { "park", 0x80b86b }, { "recreation", 0x7ab873 }, { "golf", 0x8cc073 }, { "residential", 0xbdb8ad }, { "developed", 0xb8b3ad },
            { "pedestrian", 0xccc7bd }, { "education", 0xc7bda8 }, { "medical", 0xc9bdb5 }, { "cemetery", 0x99b38c }, { "agriculture", 0xc2bd7a },
            { "horticulture", 0x8cb873 }, { "managed", 0x8fb86b }, { "transportation", 0xb3b3b3 }, { "construction", 0xb8a88f },
            { "landfill", 0xa39c8a }, { "resource_extraction", 0xa89f8f }, { "campground", 0x99b37f }, { "entertainment", 0xc2b8a3 },
            { "military", 0xb0ad9c }, { "religious", 0xc4bcae }, { "airfield", 0xb8b8b8 },
        };

        public static float WaterLineWidth(string cls)
        {
            switch (cls)
            {
                case "river": return 14f;
                case "canal": return 8f;
                case "stream": return 3f;
                case "drain": case "ditch": return 2f;
                default: return 3f;
            }
        }

        /// <summary>Real height in metres when a building says nothing about itself.</summary>
        public static double DefaultHeight(string cls, string subtype, uint hash)
        {
            double jitter = (hash % 100) / 100.0;
            switch (cls)
            {
                case "garage": case "garages": case "shed": case "roof": case "hut": case "carport": case "greenhouse": case "kiosk": case "cabin": return 3.0;
                case "house": case "detached": case "semidetached_house": case "bungalow": case "static_caravan": return 5.5 + jitter * 2.0;
                case "apartments": case "dormitory": case "hotel": case "office": return 12.0 + jitter * 6.0;
                case "warehouse": case "industrial": case "retail": case "supermarket": case "hangar": return 7.0 + jitter * 3.0;
                case "church": case "cathedral": case "mosque": case "temple": return 14.0;
            }
            switch (subtype)
            {
                case "outbuilding": return 3.0;
                case "commercial": case "civic": case "education": case "medical": return 9.0 + jitter * 4.0;
                case "industrial": return 8.0;
                case "religious": return 12.0;
            }
            return 5.0 + jitter * 4.0;
        }
    }
}
