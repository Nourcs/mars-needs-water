using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Earth
{
    /// <summary>Numbers shared with EarthBuilding.shader: what each building vertex says about its surface.</summary>
    public static class BK
    {
        // surface
        public const int Wall = 0, Roof = 1, Plain = 2;
        // wall materials
        public const int Stucco = 0, Brick = 1, Stone = 2, Siding = 3, Concrete = 4, Glass = 5, Metal = 6, Adobe = 7, Timber = 8, Tile = 9, Boards = 10, Lime = 11, Azulejo = 12, Lattice = 13;
        // windows
        public const int WNone = 0, WPlain = 1, WFrench = 2, WSash = 3, WArch = 4, WRibbon = 5, WCurtain = 6, WSmall = 7, WMoorish = 8, WBay = 9, WGrid = 10, WSliding = 11;
        // ground floors
        public const int GSame = 0, GShop = 1, GArcade = 2, GRustic = 3, GGarage = 4, GStoop = 5, GBlank = 6;
        // balconies, and which windows get one
        public const int BNone = 0, BIron = 1, BSlab = 2, BWood = 3;
        public const int BmAll = 0, BmParis = 1, BmColumns = 2, BmAlternate = 3;
        // shutters
        public const int SNone = 0, SLouver = 1, SRoller = 2, SScreen = 3;
        // roof coverings
        public const int RMembrane = 0, RClay = 1, RSlate = 2, RSeam = 3, RShingle = 4, RThatch = 5, RConcrete = 6, RCorrugated = 7, RKawara = 8, RGravel = 9;
        // roof shapes (geometry only)
        public const int ShFlat = 0, ShGable = 1, ShHip = 2, ShMansard = 3, ShPyramid = 4, ShGableAcross = 5;
        // flags (second packed number); the low two bits are the cornice, bits 15-17 the glass tint, 18-19 the balcony rule
        public const int FString = 1 << 2, FPilaster = 1 << 3, FSurround = 1 << 4, FSill = 1 << 5, FAc = 1 << 6, FFire = 1 << 7, FAwning = 1 << 8,
            FWeather = 1 << 9, FAttic = 1 << 10, FDormer = 1 << 11, FLintel = 1 << 12, FGrille = 1 << 13, FTwoTone = 1 << 14;
        // things on roofs
        public const int ClBox = 1, ClNyTank = 2, ClTank = 4, ClSolar = 8, ClChimney = 16;
        // what a building is used as
        public const int CatHouse = 0, CatMid = 1, CatTower = 2, CatShed = 3, CatIndustrial = 4;
        // things on the map's building layer that are not buildings (Look.Struct): no windows, and a shape of their own
        public const int StNone = 0, StMast = 1, StShaft = 2, StCooling = 3, StWaterTower = 4, StTank = 5, StSilo = 6, StCovered = 7, StChimney = 8, StLighthouse = 9;
    }

    /// <summary>One way of building: a Paris apartment block, a London terrace, a Tunis medina house...</summary>
    public sealed class Arch
    {
        public string Name;
        public int Mat, Win = BK.WPlain, Shut, Bal, BalMode, Cor0, Cor1, On, Maybe, Clutter, RoofMat;
        public uint[] Walls, Trim, Accent, RoofCols, Glass = { 0 };
        public float Ww0 = 0.4f, Ww1 = 0.5f, Wh0 = 0.5f, Wh1 = 0.6f, ShutChance, BalChance, Pitch = 0.6f, FloorReal = 3.0f, BayReal = 3.4f, Jitter = 0.05f;
        public int F0 = 2, F1 = 3;
        public int[] Ground = { BK.GSame, 1 }, Shapes = { BK.ShFlat, 1 };

        public Arch W(int win, float ww0, float ww1, float wh0, float wh1) { Win = win; Ww0 = ww0; Ww1 = ww1; Wh0 = wh0; Wh1 = wh1; return this; }
        public Arch R(int mat, uint[] cols, float pitch, params int[] shapes) { RoofMat = mat; RoofCols = cols; Pitch = pitch; Shapes = shapes; return this; }
        public Arch Fl(int f0, int f1, float floorReal = 3.0f, float bayReal = 3.4f) { F0 = f0; F1 = f1; FloorReal = floorReal; BayReal = bayReal; return this; }
        public Arch G(params int[] ground) { Ground = ground; return this; }
        public Arch B(int type, int mode, float chance) { Bal = type; BalMode = mode; BalChance = chance; return this; }
        public Arch S(int type, float chance) { Shut = type; ShutChance = chance; return this; }
        public Arch F(int on, int maybe = 0) { On = on; Maybe = maybe; return this; }
        public Arch C(int c0, int c1) { Cor0 = c0; Cor1 = c1; return this; }
        public Arch Gl(params uint[] tints) { Glass = tints; return this; }
        public Arch Cl(int clutter) { Clutter = clutter; return this; }
        public Arch J(float jitter) { Jitter = jitter; return this; }
    }

    /// <summary>Everything decided about one building's look.</summary>
    public struct Look
    {
        public int Struct, Cat, Mat, Win, WwI, WhI, Shut, Bal, BalMode, Ground, Cornice, Flags, Glass, RoofShape, RoofMat, Clutter, DefFloors;
        public uint Wall, Trim, Accent, RoofCol;
        public float BayReal, FloorReal, Pitch;
        public string Name, Place;

        public float BitsA(int surface, int mat) { return surface | (mat << 3) | (Win << 7) | (WwI << 11) | (WhI << 14) | (Shut << 17) | (Bal << 19) | (Ground << 21); }
        public float BitsB { get { return Cornice | Flags | (Glass << 15) | (BalMode << 18); } }
    }

    /// <summary>
    /// Which way of building belongs where. Cities with a strong look of their own come first (a centre and a radius);
    /// the rest of the world is cut into rough boxes. Each place lists its houses, its blocks and its towers, with
    /// weights; every building then draws its own colours and details from the chosen kind, so no two match.
    /// </summary>
    public static class BuildingStyle
    {
        sealed class Region { public string Name; public Arch[] House, Mid, Tower; public float[] HouseW, MidW, TowerW; }
        struct City { public double Lat, Lon, Km; public Region R; }
        struct Box { public double Lat0, Lat1, Lon0, Lon1; public Region R; }

        static readonly Dictionary<string, Arch> archs = new Dictionary<string, Arch>();
        static readonly Dictionary<string, Region> regions = new Dictionary<string, Region>();
        static readonly List<City> cities = new List<City>();
        static readonly List<Box> boxes = new List<Box>();
        static Region world;

        // ---- colours
        static readonly uint[] White = { 0xf2efe8, 0xece8df, 0xf5f3ee, 0xe6e2d8, 0xefe9dc };
        static readonly uint[] PureWhite = { 0xf7f6f2, 0xf3f1ea, 0xfaf9f6 };
        static readonly uint[] Cream = { 0xe6dcc6, 0xddd2ba, 0xe9e0cd, 0xd8ccb2, 0xe2d6bc };
        static readonly uint[] ParisStone = { 0xd9cdb4, 0xe0d5bf, 0xcfc3a8, 0xd6c9ae, 0xe4dac6, 0xc9bda3, 0xdcd2bd };
        static readonly uint[] Limestone = { 0xe3d9c2, 0xd9ceb4, 0xeadfc8, 0xd2c6aa, 0xe6d6bd };
        static readonly uint[] GreyStone = { 0xa9a59c, 0x9c9990, 0xb5b1a8, 0x8f8c84, 0xbab3a3 };
        static readonly uint[] Zinc = { 0x7c8791, 0x6f7b87, 0x8a949c, 0x66717c, 0x848c92 };
        static readonly uint[] SlateC = { 0x4a4f57, 0x565b63, 0x3f444b, 0x5d5f66 };
        static readonly uint[] Terracotta = { 0xb5603c, 0xa9553a, 0xc06a45, 0x9c4e36, 0xb86a4a, 0xad6646 };
        static readonly uint[] OrangeTile = { 0xc8683a, 0xd0733f, 0xbd5f35, 0xc5703f };
        static readonly uint[] RedTile = { 0x9b4232, 0xa84a36, 0x8c3a2e, 0x7d3a30 };
        static readonly uint[] BrownTile = { 0x7a5140, 0x6b473a, 0x85604c, 0x5e4338 };
        static readonly uint[] DarkTile = { 0x3f4348, 0x4a4d52, 0x35393e, 0x54504c };
        static readonly uint[] BlueTile = { 0x3f5068, 0x4a5a70, 0x364458 };
        static readonly uint[] BrickRed = { 0x8f4a3a, 0x9a5544, 0x7f4034, 0xa25c48, 0x884e40 };
        static readonly uint[] BrickYellow = { 0xb39b72, 0xa89068, 0xbda67e, 0x9c8560, 0xae9a78 };
        static readonly uint[] BrickBrown = { 0x6e4c3c, 0x7a5646, 0x5f4234, 0x76584a };
        static readonly uint[] BrickTan = { 0xb89a7a, 0xc4a888, 0xa88a6c, 0xbfa07e };
        static readonly uint[] BrickDutch = { 0x7a4234, 0x6a3a30, 0x8a4e3c, 0x5c3a32, 0x94614a };
        static readonly uint[] Brownstone = { 0x6b4a3a, 0x7a5545, 0x5d4034, 0x70503f, 0x64483c };
        static readonly uint[] NyBrick = { 0x8f4a3a, 0xa0604a, 0xb89a7a, 0xc9b79a, 0x6e4c3c, 0x9c8272, 0xd2c8af, 0x7f4034, 0xb47051 };
        static readonly uint[] Pastel = { 0xe8c9a0, 0xe6b89a, 0xd9c27e, 0xc9d3b0, 0xb9cbd6, 0xe0a898, 0xd8b8c8, 0xf0e0b0, 0xbfd8cc, 0xe9d2b8 };
        static readonly uint[] Bold = { 0x3f6f8f, 0x7a9a5a, 0xc4573f, 0xd9a03f, 0x6a4f7a, 0x2f7f7a, 0xb8456a, 0xe8c85a, 0x4f5f9a, 0xd97a4a };
        static readonly uint[] SfPaint = { 0xe8dcc0, 0xc9d3b0, 0xb9cbd6, 0xd8b8c8, 0xe6b89a, 0xf0e0b0, 0x8fa7b8, 0x9ab08a, 0xc98a7a, 0x6f8fa8, 0xd9c27e, 0xb0a0c0, 0xf2efe8, 0x7a9488, 0xcf9a6a, 0x5f7f9a, 0xe0d0d8, 0xa8c0b0 };
        static readonly uint[] Ochre = { 0xd9a85a, 0xcf9550, 0xe0b872, 0xc8884a, 0xd6a070, 0xe2c08a, 0xc97b55, 0xdba27a, 0xe3b48c, 0xd08a62 };
        static readonly uint[] Sienna = { 0xc97b55, 0xb8674a, 0xd08a62, 0xa85a44, 0xd9956c };
        static readonly uint[] Sand = { 0xd8c4a0, 0xcdb893, 0xe0cfae, 0xc4ad88, 0xd2bc98 };
        static readonly uint[] Mud = { 0xa8845c, 0x9a7650, 0xb49068, 0x8c6c4a };
        static readonly uint[] Marrakech = { 0xc98b6b, 0xbf7f60, 0xd29878, 0xb87458, 0xcc9070 };
        static readonly uint[] ConcreteC = { 0xb8b4ac, 0xa9a59d, 0xc4c0b8, 0x9d9a93, 0xbfb6a6, 0xaeb0b0 };
        static readonly uint[] PanelC = { 0xb9b5aa, 0xc6c0b0, 0xa8a9a8, 0xcabfa8, 0xb0b8bc, 0xd0c8b8 };
        static readonly uint[] GreyRoof = { 0x77797d, 0x8a8c8f, 0x6a6c70, 0x94928c, 0x807c76 };
        static readonly uint[] LightRoof = { 0xc9c6bd, 0xbdb9ae, 0xd4d1c9, 0xb2aea4, 0xddd9cf };
        static readonly uint[] Shingle = { 0x4b4c50, 0x5a5650, 0x3e4044, 0x6a625a, 0x545a60, 0x66544a };
        static readonly uint[] Tin = { 0x9aa0a4, 0x8a5a48, 0xa8aeb0, 0x7a4a3c, 0x5f7a8a, 0x6a8a6a, 0xb0a898, 0x94483a };
        static readonly uint[] Colorbond = { 0x8c9094, 0x6f3a34, 0x4f6a5a, 0xcfc8b4, 0x5a6470, 0x9a9a92, 0x3f4a54 };
        static readonly uint[] CopperGreen = { 0x6fa08c, 0x7aa894, 0x5f907e };
        static readonly uint[] NordicRoof = { 0x3f4348, 0x8a4a3a, 0x5a5c60, 0x9b4232, 0x35393e };
        static readonly uint[] Falu = { 0x8b2e24, 0x94362a, 0x7e2a22, 0xd8b257, 0xe0c070, 0xf0ece2, 0x5f7488, 0x3a3c40, 0x9a4034, 0xc9a24e, 0x6f8a78 };
        static readonly uint[] SidingUs = { 0xeceae4, 0xd8d0bc, 0xc4c8c4, 0xb9c8d4, 0xb8c0a8, 0xe0d4b8, 0x9aa4a8, 0xc8b8a0, 0xd9c9a8, 0x8a98a4, 0xe8e0d0, 0xa8b0a0 };
        static readonly uint[] DarkWood = { 0x4a3a2e, 0x5a4636, 0x3e3028, 0x66503e };
        static readonly uint[] LightWood = { 0xb89a6e, 0xa88a60, 0xc4a87c };
        static readonly uint[] JpSiding = { 0xe6e0d0, 0xd4ccb8, 0xc0bcb0, 0xb0a898, 0x9c948a, 0xe0d8c8, 0x8a7a68, 0xc8c4bc, 0xd8d0c4 };
        static readonly uint[] JpTile = { 0xd8d0c0, 0xc4b8a4, 0xe4e0d8, 0xa89888, 0x8a7868, 0xb8b4ac, 0x9a5a48 };
        static readonly uint[] CnTile = { 0xe0d6c4, 0xd8c0b0, 0xe8e4dc, 0xcfc4b4, 0xe0c8b8, 0xc8ccc8, 0xd9b8a0 };
        static readonly uint[] HutongGrey = { 0x8a8c8c, 0x7c7e80, 0x969896, 0x868480 };
        static readonly uint[] Azulejo = { 0x7f9fc0, 0xa8c0d8, 0x6a8fa8, 0xd9d0a8, 0x8ab09a, 0xc9d8e0, 0xb86a5a };
        static readonly uint[] LisbonPaint = { 0xe8c9a0, 0xe6b0a0, 0xf0d890, 0xf2efe8, 0xd9a0a0, 0xc9d8e0, 0xe0c0a0 };
        static readonly uint[] RuPastel = { 0xe6d08a, 0xd9c27e, 0xb8c8b0, 0xe0b8a8, 0xa8c0d0, 0xe8dcc0, 0xc9a890, 0x9ab8a8 };
        static readonly uint[] RuRoof = { 0x6a8a6a, 0x8a4a3a, 0x77797d, 0x5a6a5a, 0x9b4232 };
        static readonly uint[] KrGreen = { 0x4f8a5a, 0x5a9666, 0x44784e, 0x77797d, 0x8a8c8f };
        static readonly uint[] HollowBrick = { 0xb06a4a, 0xa86244, 0xba7452, 0x9c5a40 };
        static readonly uint[] IndiaPaint = { 0xe8dcc0, 0xe6c89a, 0xd9c8a0, 0xc9d8c8, 0xb9d0d8, 0xe8b8a0, 0xf0e0b0, 0xd8c0d0, 0xe0e0d8, 0xc8d8a8, 0xf0c890 };
        static readonly uint[] JaipurPink = { 0xd98c7a, 0xd0806e, 0xe09886, 0xc97862 };
        static readonly uint[] JodhpurBlue = { 0x8fb0d8, 0x9cbce0, 0x7fa0cc, 0xa8c4e4 };
        static readonly uint[] ChaouenBlue = { 0x6f9fd8, 0x8ab4e4, 0x5f8fcc, 0xa8c8ec, 0xf2efe8 };
        static readonly uint[] MexBold = { 0xe8a04a, 0xd9584a, 0xe8c85a, 0x4f8fb8, 0xd87a9a, 0x7ab87a, 0xf0e0b0, 0xc9573f, 0x8a6ab0, 0xf2efe8, 0xe07a3a, 0x3f9a9a };
        static readonly uint[] HavanaPastel = { 0xe8c9a0, 0xa8d0c8, 0xe6b0a0, 0xf0d890, 0xb8c8e0, 0xd8b8c8, 0xc9d8a8, 0xe0d0c0 };
        static readonly uint[] Deco = { 0xf2efe8, 0xf0d8c0, 0xc0e0d8, 0xf0c8c0, 0xd0d8f0, 0xf0e8b0, 0xb8e0e0 };
        static readonly uint[] MetalSiding = { 0xc8ccd0, 0xb0b8bc, 0xd8d4c8, 0x9aa8b0, 0xe0e0dc, 0xa8b0a0, 0x8a98a8, 0xc4b8a0 };
        static readonly uint[] AfrPaint = { 0xe8dcc0, 0xe0c890, 0xd9b88a, 0xc9d8c8, 0xf0e0b0, 0xe8b890, 0xb9d0d8, 0xe0d0b8, 0xd8a878, 0xf2efe8 };
        static readonly uint[] TrimWhite = { 0xf4f2ec, 0xeeeae0, 0xf8f6f0 };
        static readonly uint[] TrimStone = { 0xe6dcc8, 0xd9cdb6, 0xefe6d4 };
        static readonly uint[] TrimDark = { 0x4a443e, 0x3c3a38, 0x5a5048 };
        static readonly uint[] TrimGrey = { 0xb0aca4, 0x9c9890, 0xc4c0b8 };
        static readonly uint[] ShutFr = { 0x8c9aa0, 0x6f8a80, 0x9aa48a, 0x7a8a9c, 0xb0b4b0, 0x5f6f66, 0x8a6a5a, 0xa8b8c0, 0xe8e4dc };
        static readonly uint[] ShutProv = { 0x8fa8c0, 0x9ab0a0, 0x7f98b8, 0xa8a0c0, 0x6f9088, 0xb86a5a };
        static readonly uint[] ShutIt = { 0x4f6a54, 0x5a4a3a, 0x3f5a48, 0x6a5a48, 0x445c50, 0x7a6a50 };
        static readonly uint[] ShutBlue = { 0x2f6fb0, 0x3a7fc0, 0x2a5f9a, 0x4a8fd0, 0x1f5a94 };
        static readonly uint[] ShutDark = { 0x2a2c30, 0x30443a, 0x3a2a26, 0x26323e, 0x4a2a2a };
        static readonly uint[] Doors = { 0x8a2a2a, 0x2a4a6a, 0x2f5a44, 0x2a2c30, 0xd9a83f, 0x5a3a2a, 0x6a2a4a, 0xf0ece2, 0x3f6f8f };
        static readonly uint[] WoodAcc = { 0x5a4636, 0x6a503c, 0x4a3a2e, 0x7a5a40 };
        static readonly uint[] GreenAcc = { 0x3f6a4a, 0x4a7a56, 0x2f5a40, 0x5a8a66 };
        static readonly uint[] AwnAcc = { 0x9a3a2f, 0x2f5a44, 0x2a4a6a, 0xd8c8a0, 0x4a4a4a, 0xb86a2f, 0x7a2a3a };
        static readonly uint[] AlumAcc = { 0xc8ccd0, 0x8a8e92, 0x5a5e62, 0xe8e8e4, 0x6a5a48 };

        static Arch A(string name, int mat, uint[] walls, uint[] trim, uint[] accent)
        {
            var a = new Arch { Name = name, Mat = mat, Walls = walls, Trim = trim, Accent = accent, RoofCols = GreyRoof };
            archs[name] = a;
            return a;
        }

        static void Reg(string name, string house, string mid, string tower)
        {
            var r = new Region { Name = name };
            Parse(house, out r.House, out r.HouseW);
            Parse(mid, out r.Mid, out r.MidW);
            Parse(tower, out r.Tower, out r.TowerW);
            regions[name] = r;
        }

        static void Parse(string s, out Arch[] a, out float[] w)
        {
            string[] parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            a = new Arch[parts.Length]; w = new float[parts.Length];
            float sum = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                int c = parts[i].IndexOf(':');
                a[i] = archs[c < 0 ? parts[i] : parts[i].Substring(0, c)];
                sum += c < 0 ? 1f : float.Parse(parts[i].Substring(c + 1), System.Globalization.CultureInfo.InvariantCulture);
                w[i] = sum;
            }
            for (int i = 0; i < w.Length; i++) w[i] /= sum;
        }

        static void C(double lat, double lon, double km, string region) { cities.Add(new City { Lat = lat, Lon = lon, Km = km, R = regions[region] }); }
        static void Bx(double lat0, double lat1, double lon0, double lon1, string region) { boxes.Add(new Box { Lat0 = lat0, Lat1 = lat1, Lon0 = lon0, Lon1 = lon1, R = regions[region] }); }

        static BuildingStyle()
        {
            const int Fl = BK.ShFlat, Ga = BK.ShGable, Hi = BK.ShHip, Ma = BK.ShMansard, Py = BK.ShPyramid, Gx = BK.ShGableAcross;

            // ================= Europe
            A("HAUSS", BK.Stone, ParisStone, TrimStone, ShutFr).W(BK.WFrench, 0.36f, 0.46f, 0.62f, 0.74f).R(BK.RSeam, Zinc, 1.9f, Ma, 9, Fl, 1)
                .Fl(5, 7, 3.3f, 3.0f).G(BK.GShop, 6, BK.GRustic, 3, BK.GArcade, 1).B(BK.BIron, BK.BmParis, 0.9f).S(BK.SLouver, 0.3f)
                .F(BK.FString | BK.FSurround | BK.FDormer | BK.FAttic, BK.FLintel | BK.FAwning | BK.FPilaster).C(2, 2).Cl(BK.ClChimney).J(0.03f);
            A("PMOD", BK.Concrete, new uint[] { 0xd8d4c8, 0xc9c4b8, 0xe4e0d4, 0xb8b4a8, 0xd0c4b0 }, TrimGrey, AlumAcc).W(BK.WSliding, 0.6f, 0.8f, 0.55f, 0.68f).R(BK.RGravel, GreyRoof, 0, Fl, 1)
                .Fl(5, 9, 2.9f, 3.6f).G(BK.GShop, 2, BK.GBlank, 1, BK.GSame, 1).B(BK.BSlab, BK.BmAll, 0.6f).S(BK.SRoller, 0.6f).F(0, BK.FAwning | BK.FWeather).Cl(BK.ClBox);
            A("FRN", BK.Stucco, new uint[] { 0xe6dcc6, 0xddd2ba, 0xece6d8, 0xd8ccb2, 0xcfc4ae, 0xe0d0b8 }, TrimStone, ShutFr).W(BK.WFrench, 0.34f, 0.44f, 0.5f, 0.62f)
                .R(BK.RSlate, SlateC, 0.95f, Ga, 5, Hi, 4, Ma, 1).Fl(2, 3, 2.9f, 3.6f).G(BK.GSame, 3, BK.GStoop, 2, BK.GGarage, 1).S(BK.SLouver, 0.75f)
                .F(BK.FSill, BK.FSurround | BK.FString | BK.FDormer).C(0, 1).Cl(BK.ClChimney);
            A("FRBRICK", BK.Brick, BrickRed, TrimStone, ShutFr).W(BK.WFrench, 0.34f, 0.44f, 0.5f, 0.62f).R(BK.RClay, RedTile, 0.9f, Ga, 6, Hi, 3)
                .Fl(2, 3, 2.9f, 3.4f).G(BK.GSame, 3, BK.GStoop, 2).S(BK.SLouver, 0.5f).F(BK.FSill | BK.FSurround, BK.FString | BK.FLintel).C(0, 1).Cl(BK.ClChimney);
            A("FRS", BK.Stucco, new uint[] { 0xe8d8b8, 0xe0c8a0, 0xecdcc0, 0xd9b890, 0xe6c8a8, 0xf0e4cc, 0xdcc0a0 }, TrimStone, ShutProv).W(BK.WFrench, 0.32f, 0.42f, 0.48f, 0.6f)
                .R(BK.RClay, Terracotta, 0.42f, Hi, 5, Ga, 5).Fl(1, 3, 2.9f, 3.8f).G(BK.GSame, 3, BK.GStoop, 1, BK.GGarage, 1).S(BK.SLouver, 0.9f).F(0, BK.FSill | BK.FSurround).C(0, 1);
            A("PROV", BK.Stucco, Ochre, TrimStone, ShutProv).W(BK.WFrench, 0.32f, 0.42f, 0.52f, 0.64f).R(BK.RClay, Terracotta, 0.4f, Hi, 4, Ga, 4, Fl, 1)
                .Fl(3, 5, 3.0f, 3.2f).G(BK.GShop, 3, BK.GSame, 2, BK.GStoop, 1).S(BK.SLouver, 0.95f).B(BK.BIron, BK.BmColumns, 0.3f).F(BK.FWeather, BK.FString | BK.FSurround | BK.FAwning).C(1, 2).J(0.07f);
            A("UKTER", BK.Brick, new uint[] { 0xb39b72, 0xa89068, 0x8f4a3a, 0x9a5544, 0x7f4034, 0x6e4c3c, 0xbda67e, 0xa25c48 }, TrimWhite, Doors).W(BK.WSash, 0.36f, 0.46f, 0.5f, 0.6f)
                .R(BK.RSlate, SlateC, 0.8f, Gx, 8, Hi, 1, Ga, 1).Fl(2, 3, 2.9f, 2.8f).G(BK.GStoop, 6, BK.GSame, 2, BK.GShop, 1).F(BK.FSill | BK.FLintel, BK.FSurround | BK.FString | BK.FWeather).C(0, 1).Cl(BK.ClChimney);
            A("UKGEO", BK.Brick, new uint[] { 0x6e5a48, 0x7a6450, 0x8a7458, 0x5f4c3e, 0xa89068, 0x76584a }, TrimWhite, Doors).W(BK.WSash, 0.34f, 0.42f, 0.56f, 0.7f)
                .R(BK.RSlate, SlateC, 1.4f, Fl, 5, Ma, 4).Fl(4, 5, 3.2f, 2.9f).G(BK.GRustic, 6, BK.GShop, 3).B(BK.BIron, BK.BmParis, 0.5f)
                .F(BK.FSill | BK.FTwoTone | BK.FString, BK.FSurround | BK.FLintel | BK.FDormer).C(1, 2).Cl(BK.ClChimney);
            A("UKSTUC", BK.Stucco, new uint[] { 0xf2efe8, 0xece8df, 0xf5f3ee, 0xe8dcc0, 0xd8b8c8, 0xb9cbd6, 0xf0e0b0, 0xc9d3b0, 0xe6b89a }, TrimWhite, Doors).W(BK.WSash, 0.36f, 0.44f, 0.58f, 0.72f)
                .R(BK.RSlate, SlateC, 1.4f, Fl, 6, Ma, 3).Fl(3, 5, 3.2f, 3.0f).G(BK.GRustic, 6, BK.GStoop, 2, BK.GShop, 2).B(BK.BIron, BK.BmParis, 0.6f)
                .F(BK.FSurround | BK.FString | BK.FPilaster, BK.FLintel).C(2, 2).Cl(BK.ClChimney);
            A("UKSEMI", BK.Brick, new uint[] { 0x9a5544, 0x8f4a3a, 0xa25c48, 0xb39b72, 0x7f4034, 0xd8d0bc, 0xcfc4ae }, TrimWhite, Doors).W(BK.WBay, 0.5f, 0.62f, 0.46f, 0.56f)
                .R(BK.RClay, BrownTile, 0.75f, Hi, 6, Ga, 3).Fl(2, 2, 2.8f, 3.6f).G(BK.GStoop, 4, BK.GSame, 2, BK.GGarage, 1).F(BK.FSill, BK.FLintel | BK.FTwoTone).Cl(BK.ClChimney);
            A("UKCOUNCIL", BK.Brick, new uint[] { 0x8a6a54, 0x9c8560, 0x7a5646, 0xa89a88, 0xb0a898 }, TrimGrey, Doors).W(BK.WPlain, 0.5f, 0.66f, 0.42f, 0.52f)
                .R(BK.RGravel, GreyRoof, 0, Fl, 1).Fl(4, 8, 2.8f, 3.4f).G(BK.GSame, 3, BK.GBlank, 1, BK.GShop, 1).B(BK.BSlab, BK.BmAll, 0.5f).F(BK.FString, BK.FWeather).Cl(BK.ClBox);
            A("NLCANAL", BK.Brick, BrickDutch, TrimWhite, Doors).W(BK.WSash, 0.5f, 0.62f, 0.62f, 0.76f).R(BK.RClay, new uint[] { 0x8a4a3a, 0x3f4348, 0x9b4232, 0x4a4d52 }, 1.3f, Ga, 7, Hi, 2, Fl, 1)
                .Fl(3, 5, 3.3f, 2.2f).G(BK.GStoop, 5, BK.GShop, 3).F(BK.FSill | BK.FLintel | BK.FAttic, BK.FString | BK.FSurround).C(1, 2).J(0.06f);
            A("NLROW", BK.Brick, new uint[] { 0x9a6a4a, 0x8a5a44, 0xb08a64, 0x7a4a3a, 0xc0a078, 0x6a5044 }, TrimWhite, Doors).W(BK.WPlain, 0.6f, 0.78f, 0.52f, 0.64f)
                .R(BK.RClay, new uint[] { 0x8a4a3a, 0x3f4348, 0x9b4232, 0x4a4d52, 0x7a5140 }, 0.8f, Gx, 6, Fl, 3).Fl(2, 3, 2.8f, 5.0f).G(BK.GStoop, 3, BK.GSame, 2).F(BK.FSill, BK.FLintel);
            A("DEALT", BK.Stucco, new uint[] { 0xe8dcc0, 0xe0c890, 0xd9c8a0, 0xc9d3b0, 0xe6b89a, 0xd8d4c8, 0xc8c0b0, 0xe8d0a8, 0xb8c0b8, 0xd9b0a0, 0xf0e8d4 }, TrimWhite, Doors).W(BK.WSash, 0.36f, 0.46f, 0.54f, 0.66f)
                .R(BK.RClay, new uint[] { 0x9b4232, 0xa84a36, 0x8c3a2e, 0x7a5140, 0x4a4d52 }, 1.1f, Ma, 4, Hi, 3, Ga, 2, Fl, 1).Fl(4, 6, 3.3f, 3.0f).G(BK.GShop, 4, BK.GRustic, 4, BK.GSame, 1)
                .B(BK.BIron, BK.BmColumns, 0.45f).F(BK.FSurround | BK.FString, BK.FLintel | BK.FPilaster | BK.FDormer | BK.FSill).C(1, 2).Cl(BK.ClChimney).J(0.06f);
            A("DEHOUSE", BK.Stucco, new uint[] { 0xf2efe8, 0xece8df, 0xe8dcc0, 0xf0e8d4, 0xe0d8c4, 0xd9c8a0, 0xe6dccc, 0xc8c8c0 }, TrimWhite, Doors).W(BK.WPlain, 0.4f, 0.56f, 0.44f, 0.54f)
                .R(BK.RClay, new uint[] { 0x9b4232, 0xa84a36, 0x3f4348, 0x7a5140, 0x4a4d52, 0x8c3a2e }, 1.0f, Ga, 7, Hi, 2).Fl(2, 3, 2.8f, 4.0f).G(BK.GSame, 3, BK.GStoop, 2, BK.GGarage, 1)
                .S(BK.SRoller, 0.5f).F(BK.FSill, BK.FDormer | BK.FTwoTone).Cl(BK.ClChimney);
            A("FACH", BK.Timber, new uint[] { 0xf0ece2, 0xe8dcc0, 0xe0c890, 0xd9a878, 0xf2efe8 }, TrimDark, Doors).W(BK.WGrid, 0.38f, 0.5f, 0.42f, 0.52f)
                .R(BK.RClay, RedTile, 1.4f, Ga, 8, Hi, 1).Fl(2, 4, 2.8f, 3.0f).G(BK.GRustic, 3, BK.GShop, 2, BK.GSame, 2).S(BK.SLouver, 0.3f).F(BK.FAttic, BK.FSill).Cl(BK.ClChimney);
            A("PANEL", BK.Concrete, PanelC, TrimGrey, AlumAcc).W(BK.WPlain, 0.5f, 0.62f, 0.42f, 0.5f).R(BK.RGravel, GreyRoof, 0, Fl, 1)
                .Fl(5, 9, 2.8f, 3.2f).G(BK.GSame, 3, BK.GBlank, 2, BK.GShop, 1).B(BK.BSlab, BK.BmColumns, 0.85f).F(BK.FString | BK.FWeather, BK.FPilaster | BK.FAc | BK.FTwoTone).Cl(BK.ClBox).J(0.04f);
            A("PANELT", BK.Concrete, PanelC, TrimGrey, AlumAcc).W(BK.WPlain, 0.5f, 0.62f, 0.42f, 0.5f).R(BK.RGravel, GreyRoof, 0, Fl, 1)
                .Fl(12, 22, 2.8f, 3.2f).G(BK.GBlank, 2, BK.GShop, 1).B(BK.BSlab, BK.BmColumns, 0.9f).F(BK.FString | BK.FWeather, BK.FPilaster | BK.FAc).Cl(BK.ClBox);
            A("NWOOD", BK.Boards, Falu, TrimWhite, TrimWhite).W(BK.WGrid, 0.34f, 0.46f, 0.42f, 0.54f).R(BK.RSeam, NordicRoof, 1.0f, Ga, 8, Hi, 1)
                .Fl(1, 2, 2.7f, 3.6f).G(BK.GSame, 3, BK.GStoop, 2).F(BK.FSurround, BK.FSill).Cl(BK.ClChimney).J(0.04f);
            A("NCITY", BK.Stucco, new uint[] { 0xd9a85a, 0xe0b872, 0xc97b55, 0xe8dcc0, 0xd9c8a0, 0xb8674a, 0xe6c89a, 0xc8c0b0, 0xa8b8a8, 0xd08a62 }, TrimWhite, Doors).W(BK.WSash, 0.36f, 0.46f, 0.54f, 0.66f)
                .R(BK.RSeam, new uint[] { 0x3f4348, 0x6fa08c, 0x4a4d52, 0x8a4a3a, 0x5a5c60, 0x7aa894 }, 1.3f, Ma, 5, Hi, 3, Ga, 1).Fl(4, 6, 3.3f, 3.0f).G(BK.GShop, 4, BK.GRustic, 4)
                .F(BK.FSurround | BK.FString, BK.FLintel | BK.FDormer | BK.FPilaster).C(1, 2).Cl(BK.ClChimney);
            A("NMOD", BK.Brick, new uint[] { 0xc9a24e, 0xb08a64, 0x8f4a3a, 0xd8d0bc, 0x9a6a4a, 0xe8e4dc, 0x6e4c3c }, TrimGrey, AlumAcc).W(BK.WPlain, 0.5f, 0.7f, 0.46f, 0.58f)
                .R(BK.RSeam, NordicRoof, 0.5f, Fl, 5, Ga, 2, Hi, 2).Fl(3, 6, 2.9f, 3.6f).G(BK.GSame, 3, BK.GShop, 1).B(BK.BSlab, BK.BmColumns, 0.7f).F(0, BK.FSill).Cl(BK.ClBox);
            A("ITOCHRE", BK.Stucco, Ochre, TrimStone, ShutIt).W(BK.WFrench, 0.32f, 0.4f, 0.54f, 0.66f).R(BK.RClay, Terracotta, 0.36f, Hi, 7, Ga, 2, Fl, 1)
                .Fl(3, 6, 3.4f, 3.2f).G(BK.GShop, 4, BK.GRustic, 3, BK.GArcade, 2, BK.GSame, 1).S(BK.SLouver, 0.92f).B(BK.BIron, BK.BmColumns, 0.3f)
                .F(BK.FWeather | BK.FString, BK.FSurround | BK.FLintel | BK.FAwning | BK.FTwoTone).C(1, 2).J(0.08f);
            A("ITHOUSE", BK.Stucco, new uint[] { 0xe8d8b8, 0xe0c8a0, 0xd9a85a, 0xecdcc0, 0xe6c8a8, 0xdba27a, 0xf0e4cc, 0xd6a070 }, TrimStone, ShutIt).W(BK.WPlain, 0.34f, 0.44f, 0.46f, 0.58f)
                .R(BK.RClay, Terracotta, 0.38f, Hi, 6, Ga, 4).Fl(2, 3, 3.0f, 3.8f).G(BK.GSame, 3, BK.GStoop, 1, BK.GGarage, 1).S(BK.SLouver, 0.9f).B(BK.BSlab, BK.BmColumns, 0.3f).F(0, BK.FSill | BK.FWeather).J(0.07f);
            A("VENICE", BK.Stucco, new uint[] { 0xd9956c, 0xc97b55, 0xe0b872, 0xb8674a, 0xe6b89a, 0xd0a080, 0xa85a44, 0xe8d0a8 }, TrimStone, ShutIt).W(BK.WArch, 0.32f, 0.42f, 0.56f, 0.68f)
                .R(BK.RClay, Terracotta, 0.36f, Hi, 6, Ga, 3).Fl(3, 5, 3.4f, 2.8f).G(BK.GRustic, 4, BK.GArcade, 2, BK.GShop, 2).S(BK.SLouver, 0.85f).B(BK.BIron, BK.BmColumns, 0.4f)
                .F(BK.FWeather | BK.FSurround, BK.FString | BK.FSill).C(1, 2).Cl(BK.ClChimney).J(0.08f);
            A("ESWHITE", BK.Lime, PureWhite, new uint[] { 0xd9a83f, 0xe0b85a, 0xf4f2ec, 0xf4f2ec, 0x8fa8c0 }, new uint[] { 0x2a2c30, 0x5a4636, 0x2f5a44, 0x2a4a6a }).W(BK.WPlain, 0.28f, 0.38f, 0.44f, 0.6f)
                .R(BK.RClay, Terracotta, 0.36f, Fl, 4, Ga, 4, Hi, 2).Fl(2, 3, 3.0f, 3.4f).G(BK.GSame, 3, BK.GStoop, 2).B(BK.BIron, BK.BmColumns, 0.5f).F(BK.FGrille, BK.FTwoTone | BK.FSurround).J(0.02f);
            A("ESCITY", BK.Stucco, new uint[] { 0xe8dcc0, 0xe6c89a, 0xe0b8a0, 0xd9c8a0, 0xf0e4cc, 0xd0a080, 0xe8d0b8, 0xc8b8a0, 0xdcc0a8 }, TrimStone, new uint[] { 0x4f6a54, 0x6a5a48, 0x8a8e92, 0x5a4636, 0x3f5a48 }).W(BK.WFrench, 0.34f, 0.42f, 0.6f, 0.72f)
                .R(BK.RClay, Terracotta, 0.36f, Fl, 6, Hi, 3).Fl(5, 7, 3.2f, 3.0f).G(BK.GShop, 7, BK.GRustic, 2).B(BK.BIron, BK.BmAll, 0.9f).S(BK.SRoller, 0.7f)
                .F(BK.FString, BK.FSurround | BK.FLintel | BK.FAwning | BK.FPilaster).C(1, 2).Cl(BK.ClBox).J(0.06f);
            A("ESMOD", BK.Brick, new uint[] { 0xb8674a, 0xc98a60, 0xa85a44, 0xd0a080, 0xe0d0b8, 0x9a5544 }, TrimGrey, new uint[] { 0x4f6a54, 0x8a8e92, 0xd8c8a0, 0x9a3a2f, 0x6a5a48 }).W(BK.WSliding, 0.5f, 0.7f, 0.5f, 0.62f)
                .R(BK.RGravel, GreyRoof, 0, Fl, 1).Fl(5, 9, 2.9f, 3.6f).G(BK.GShop, 5, BK.GBlank, 2).B(BK.BSlab, BK.BmAll, 0.8f).S(BK.SRoller, 0.8f).F(BK.FAwning, BK.FAc).Cl(BK.ClBox);
            A("LISBOA", BK.Azulejo, Azulejo, TrimStone, new uint[] { 0x3f5a48, 0x8a2a2a, 0x2a4a6a, 0xf0ece2, 0x5a4636 }).W(BK.WFrench, 0.36f, 0.44f, 0.6f, 0.72f).R(BK.RClay, OrangeTile, 0.5f, Hi, 5, Ga, 3, Ma, 2)
                .Fl(3, 5, 3.2f, 2.8f).G(BK.GShop, 5, BK.GRustic, 3).B(BK.BIron, BK.BmAll, 0.85f).F(BK.FSurround | BK.FString, BK.FDormer | BK.FWeather | BK.FLintel).C(1, 2).J(0.05f);
            A("LISPAINT", BK.Stucco, LisbonPaint, TrimStone, new uint[] { 0x3f5a48, 0x8a2a2a, 0x2a4a6a, 0xf0ece2, 0x5a4636 }).W(BK.WFrench, 0.36f, 0.44f, 0.6f, 0.72f).R(BK.RClay, OrangeTile, 0.5f, Hi, 5, Ga, 3, Ma, 2)
                .Fl(3, 5, 3.2f, 2.8f).G(BK.GShop, 5, BK.GRustic, 3).B(BK.BIron, BK.BmAll, 0.8f).F(BK.FSurround | BK.FWeather, BK.FDormer | BK.FString | BK.FTwoTone).C(1, 2).J(0.06f);
            A("PTHOUSE", BK.Stucco, new uint[] { 0xf2efe8, 0xf5f3ee, 0xf0e0b0, 0xe8dcc0, 0xece8df }, new uint[] { 0xd9a83f, 0x3a6fb0, 0xb0aca4, 0xf4f2ec }, Doors).W(BK.WPlain, 0.32f, 0.44f, 0.46f, 0.58f)
                .R(BK.RClay, OrangeTile, 0.45f, Hi, 6, Ga, 4).Fl(1, 3, 2.9f, 3.6f).G(BK.GSame, 3, BK.GStoop, 1, BK.GGarage, 1).S(BK.SRoller, 0.5f).F(BK.FSurround, BK.FTwoTone | BK.FSill).Cl(BK.ClChimney);
            A("CYCLADES", BK.Lime, PureWhite, PureWhite, ShutBlue).W(BK.WSmall, 0.26f, 0.36f, 0.36f, 0.5f).R(BK.RConcrete, new uint[] { 0xf0eee8, 0xe8e6e0, 0xdcdad4 }, 0, Fl, 1)
                .Fl(1, 2, 2.9f, 3.6f).G(BK.GSame, 3, BK.GStoop, 2).S(BK.SLouver, 0.85f).F(0, BK.FSurround).J(0.015f);
            A("POLYK", BK.Concrete, new uint[] { 0xf0ece2, 0xe8e4d8, 0xe6dcc6, 0xdcd8cc, 0xf5f3ee, 0xe0d0b8, 0xd8d8d0 }, TrimWhite, AwnAcc).W(BK.WSliding, 0.6f, 0.8f, 0.58f, 0.7f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 1).Fl(4, 7, 3.0f, 3.8f).G(BK.GShop, 5, BK.GArcade, 2, BK.GBlank, 1).B(BK.BSlab, BK.BmAll, 0.95f).S(BK.SRoller, 0.5f)
                .F(BK.FAwning, BK.FWeather | BK.FAc).Cl(BK.ClSolar | BK.ClBox);
            A("GRHOUSE", BK.Stucco, new uint[] { 0xf2efe8, 0xf0e4cc, 0xe8d8b8, 0xe6c8a8, 0xf5f3ee, 0xe0c8a0 }, TrimWhite, new uint[] { 0x2f6fb0, 0x4f6a54, 0x5a4636, 0x8a2a2a }).W(BK.WPlain, 0.34f, 0.46f, 0.46f, 0.58f)
                .R(BK.RClay, Terracotta, 0.38f, Hi, 6, Ga, 2, Fl, 3).Fl(1, 3, 3.0f, 3.8f).G(BK.GSame, 3, BK.GStoop, 1).S(BK.SLouver, 0.8f).B(BK.BSlab, BK.BmColumns, 0.4f).Cl(BK.ClSolar);
            A("TRAPT", BK.Stucco, new uint[] { 0xe8dcc0, 0xe6c89a, 0xd9c8a0, 0xc9d8c8, 0xe8b8a0, 0xf0e0b0, 0xd8d8d0, 0xe0c0a0, 0xb9d0d8, 0xe0a898, 0xc8d8a8 }, TrimWhite, AlumAcc).W(BK.WPlain, 0.46f, 0.6f, 0.46f, 0.56f)
                .R(BK.RClay, new uint[] { 0xa84a36, 0x9b4232, 0xb5603c, 0x8c3a2e }, 0.4f, Hi, 6, Fl, 3).Fl(4, 7, 2.9f, 3.4f).G(BK.GShop, 6, BK.GBlank, 1).B(BK.BSlab, BK.BmColumns, 0.85f)
                .F(0, BK.FAc | BK.FWeather | BK.FString | BK.FAwning).Cl(BK.ClSolar | BK.ClTank).J(0.07f);
            A("TROLD", BK.Siding, new uint[] { 0x6a503c, 0x8a6a4c, 0xd9c8a0, 0xa85a44, 0xe8dcc0, 0x7a8a8a, 0xc98a60, 0x5a4636 }, TrimWhite, WoodAcc).W(BK.WSash, 0.34f, 0.44f, 0.52f, 0.64f)
                .R(BK.RClay, RedTile, 0.4f, Hi, 7, Ga, 2).Fl(2, 4, 3.0f, 2.6f).G(BK.GRustic, 3, BK.GShop, 3, BK.GSame, 1).S(BK.SScreen, 0.3f).F(BK.FWeather | BK.FSurround, BK.FString).C(1, 2);
            A("RUNEO", BK.Stucco, RuPastel, TrimWhite, Doors).W(BK.WSash, 0.36f, 0.46f, 0.56f, 0.68f).R(BK.RSeam, RuRoof, 0.5f, Hi, 8, Fl, 1)
                .Fl(3, 6, 3.5f, 3.0f).G(BK.GRustic, 5, BK.GShop, 3, BK.GArcade, 1).F(BK.FSurround | BK.FString, BK.FPilaster | BK.FLintel | BK.FTwoTone).C(2, 2).J(0.05f);
            A("STALIN", BK.Stone, new uint[] { 0xc8b898, 0xd4c4a4, 0xb8a888, 0xc0b49c, 0xa89880 }, TrimStone, Doors).W(BK.WSash, 0.4f, 0.5f, 0.52f, 0.62f).R(BK.RSeam, RuRoof, 0.4f, Hi, 6, Fl, 3)
                .Fl(7, 11, 3.3f, 3.2f).G(BK.GRustic, 5, BK.GShop, 4).B(BK.BSlab, BK.BmColumns, 0.5f).F(BK.FString | BK.FTwoTone, BK.FPilaster | BK.FSurround | BK.FAttic).C(2, 2);
            A("EEHOUSE", BK.Stucco, new uint[] { 0xe8dcc0, 0xf0e8d4, 0xe0c890, 0xd9c8a0, 0xc9d3b0, 0xe6b89a, 0xf2efe8, 0xd8d0bc, 0xe8c080, 0xb8c8b8 }, TrimWhite, Doors).W(BK.WPlain, 0.36f, 0.5f, 0.44f, 0.54f)
                .R(BK.RClay, new uint[] { 0x9b4232, 0x7a5140, 0x4a4d52, 0x8a4a3a, 0x6a6c70, 0xa84a36 }, 0.8f, Ga, 5, Hi, 5).Fl(1, 2, 2.8f, 4.0f).G(BK.GSame, 3, BK.GStoop, 1, BK.GGarage, 1).F(0, BK.FSill | BK.FTwoTone | BK.FSurround).Cl(BK.ClChimney);
            A("DACHA", BK.Siding, new uint[] { 0x6f8a78, 0x5f7f9a, 0xc9a24e, 0x8a6a4c, 0x6a503c, 0x9ab08a, 0xd8d0bc, 0x7a4a3c }, TrimWhite, TrimWhite).W(BK.WGrid, 0.34f, 0.44f, 0.4f, 0.5f)
                .R(BK.RCorrugated, Tin, 0.8f, Ga, 7, Hi, 3).Fl(1, 2, 2.7f, 3.6f).G(BK.GSame, 1).F(BK.FSurround, BK.FWeather).Cl(BK.ClChimney);
            A("CHALET", BK.Boards, new uint[] { 0x6a503c, 0x7a5a40, 0x5a4636, 0x8a6a4c, 0x4a3a2e }, TrimWhite, new uint[] { 0x3f6a4a, 0x8a2a2a, 0x5a4636, 0x4a3a2e }).W(BK.WGrid, 0.36f, 0.48f, 0.42f, 0.52f)
                .R(BK.RShingle, new uint[] { 0x5a5650, 0x6a625a, 0x4b4c50, 0x7a5140 }, 0.5f, Ga, 9, Hi, 1).Fl(2, 3, 2.8f, 3.6f).G(BK.GSame, 1).B(BK.BWood, BK.BmAll, 0.8f).S(BK.SLouver, 0.7f)
                .F(BK.FTwoTone, BK.FSill).Cl(BK.ClChimney);
            A("ALPCITY", BK.Stucco, new uint[] { 0xf0e8d4, 0xe8dcc0, 0xe0c890, 0xd9c8a0, 0xe6b89a, 0xc9d3b0, 0xb8c8b8, 0xe8d0a8, 0xd9b0a0 }, TrimWhite, new uint[] { 0x3f6a4a, 0x8a2a2a, 0x5a4636, 0x6f8a80 }).W(BK.WSash, 0.36f, 0.46f, 0.5f, 0.62f)
                .R(BK.RClay, new uint[] { 0x8c3a2e, 0x7a5140, 0x9b4232, 0x6b473a }, 0.9f, Hi, 5, Ga, 4).Fl(3, 5, 3.1f, 3.2f).G(BK.GShop, 4, BK.GArcade, 2, BK.GRustic, 2).S(BK.SLouver, 0.7f)
                .F(BK.FSurround, BK.FString | BK.FDormer | BK.FSill).C(0, 1).Cl(BK.ClChimney);

            // ================= the Americas
            A("BROWN", BK.Stone, Brownstone, new uint[] { 0x7a5a4a, 0x6b4a3a, 0x8a6a58 }, Doors).W(BK.WSash, 0.34f, 0.42f, 0.56f, 0.68f).R(BK.RMembrane, GreyRoof, 0, Fl, 1)
                .Fl(3, 5, 3.4f, 2.1f).G(BK.GStoop, 8, BK.GShop, 1).F(BK.FSurround | BK.FLintel | BK.FSill, BK.FFire | BK.FAc | BK.FWeather).C(2, 2).Cl(BK.ClChimney | BK.ClBox).J(0.04f);
            A("NYROW", BK.Brick, NyBrick, TrimStone, Doors).W(BK.WSash, 0.34f, 0.44f, 0.54f, 0.66f).R(BK.RMembrane, GreyRoof, 0, Fl, 1)
                .Fl(3, 5, 3.3f, 2.2f).G(BK.GStoop, 5, BK.GShop, 3).F(BK.FLintel | BK.FSill, BK.FFire | BK.FAc | BK.FWeather | BK.FString).C(1, 2).Cl(BK.ClChimney | BK.ClBox).J(0.05f);
            A("TENEMENT", BK.Brick, NyBrick, TrimStone, AwnAcc).W(BK.WSash, 0.36f, 0.46f, 0.52f, 0.62f).R(BK.RMembrane, GreyRoof, 0, Fl, 1)
                .Fl(5, 7, 3.1f, 2.4f).G(BK.GShop, 8, BK.GStoop, 2).F(BK.FLintel | BK.FSill | BK.FFire, BK.FAc | BK.FWeather | BK.FString | BK.FAwning).C(2, 2).Cl(BK.ClNyTank | BK.ClBox).J(0.05f);
            A("NYLOFT", BK.Brick, NyBrick, TrimStone, AwnAcc).W(BK.WGrid, 0.62f, 0.78f, 0.6f, 0.72f).R(BK.RMembrane, GreyRoof, 0, Fl, 1)
                .Fl(6, 12, 3.8f, 3.4f).G(BK.GShop, 8, BK.GRustic, 2).F(BK.FString | BK.FPilaster, BK.FFire | BK.FAc | BK.FWeather | BK.FAttic).C(2, 2).Cl(BK.ClNyTank | BK.ClBox);
            A("PREWAR", BK.Brick, new uint[] { 0x9c8272, 0xd2c8af, 0xc9b79a, 0x8f4a3a, 0xb89a7a, 0xdecfb8, 0xa0604a, 0x928a77, 0xe1d6c9, 0x6e4c3c }, TrimStone, AwnAcc).W(BK.WSash, 0.38f, 0.5f, 0.5f, 0.6f)
                .R(BK.RMembrane, GreyRoof, 0, Fl, 9, Py, 1).Fl(14, 30, 3.4f, 2.8f).G(BK.GShop, 6, BK.GRustic, 4).F(BK.FString | BK.FTwoTone, BK.FPilaster | BK.FAc | BK.FAttic | BK.FSill).C(1, 2).Cl(BK.ClNyTank | BK.ClBox);
            A("GLASS", BK.Glass, new uint[] { 0x8a9298, 0x6a7278, 0xa8b0b4, 0x4a5258, 0xb8bcc0, 0x7a8288, 0x3a4248 }, TrimGrey, AlumAcc).W(BK.WCurtain, 0.8f, 0.9f, 0.7f, 0.9f)
                .R(BK.RGravel, GreyRoof, 0, Fl, 1).Fl(16, 40, 3.9f, 1.6f).G(BK.GShop, 1).Gl(0, 1, 1, 2, 3, 4, 1).F(0, BK.FPilaster | BK.FString).Cl(BK.ClBox);
            A("GLASSGULF", BK.Glass, new uint[] { 0xb8b0a0, 0x8a9298, 0xc8c0b0, 0x6a7278, 0xd8d0c0 }, TrimGrey, AlumAcc).W(BK.WCurtain, 0.8f, 0.9f, 0.7f, 0.9f)
                .R(BK.RGravel, LightRoof, 0, Fl, 1).Fl(20, 50, 3.9f, 1.6f).G(BK.GShop, 1).Gl(1, 3, 3, 4, 2, 1).F(0, BK.FPilaster | BK.FString).Cl(BK.ClBox);
            A("OFFICE", BK.Concrete, new uint[] { 0xb8b4ac, 0xc4c0b8, 0xd8d4c8, 0x9d9a93, 0xc8b8a0, 0xa8a098, 0xe0dcd0, 0x8a7a6a }, TrimGrey, AlumAcc).W(BK.WRibbon, 0.8f, 0.9f, 0.42f, 0.56f)
                .R(BK.RGravel, GreyRoof, 0, Fl, 1).Fl(4, 12, 3.7f, 2.4f).G(BK.GShop, 3, BK.GBlank, 1, BK.GArcade, 1).Gl(0, 1, 2, 3).F(0, BK.FPilaster | BK.FWeather).Cl(BK.ClBox);
            A("SLAB", BK.Concrete, new uint[] { 0xe0dcd0, 0xd0ccc0, 0xc4c0b8, 0xe8e0d0, 0xb8b4ac, 0xd8c8b0, 0xc8d0d0 }, TrimGrey, AlumAcc).W(BK.WSliding, 0.56f, 0.76f, 0.52f, 0.64f)
                .R(BK.RGravel, GreyRoof, 0, Fl, 1).Fl(12, 28, 2.9f, 3.4f).G(BK.GBlank, 2, BK.GShop, 2).B(BK.BSlab, BK.BmColumns, 0.85f).F(0, BK.FString | BK.FWeather | BK.FPilaster).Cl(BK.ClBox);
            A("SFVIC", BK.Siding, SfPaint, TrimWhite, Doors).W(BK.WBay, 0.56f, 0.68f, 0.52f, 0.64f).R(BK.RShingle, Shingle, 0.85f, Ga, 6, Fl, 4)
                .Fl(2, 3, 3.3f, 2.4f).G(BK.GGarage, 6, BK.GStoop, 4).F(BK.FSurround | BK.FLintel, BK.FString | BK.FAttic | BK.FTwoTone).C(1, 2).J(0.07f);
            A("SFMARINA", BK.Stucco, new uint[] { 0xf0e8d4, 0xe8dcc0, 0xe6c8a8, 0xd9c8a0, 0xc9d3b0, 0xf2efe8, 0xe0b8a0, 0xb9cbd6, 0xf0e0b0, 0xd8b8a8 }, TrimWhite, Doors).W(BK.WBay, 0.56f, 0.7f, 0.46f, 0.58f)
                .R(BK.RMembrane, GreyRoof, 0, Fl, 8, Hi, 2).Fl(2, 3, 3.1f, 2.6f).G(BK.GGarage, 8, BK.GStoop, 2).F(0, BK.FSurround | BK.FString | BK.FGrille).C(1, 2).J(0.06f);
            A("SFEDW", BK.Stucco, new uint[] { 0xe8dcc0, 0xd8ccb2, 0xc8c0b0, 0xe6c8a8, 0xb8a890, 0xd0c4b0, 0xa89c8c, 0xe0d4c0, 0xc9b79a }, TrimWhite, AwnAcc).W(BK.WBay, 0.52f, 0.66f, 0.52f, 0.62f)
                .R(BK.RMembrane, GreyRoof, 0, Fl, 1).Fl(3, 6, 3.2f, 2.8f).G(BK.GShop, 5, BK.GGarage, 3, BK.GRustic, 2).F(BK.FString, BK.FFire | BK.FSurround | BK.FAwning | BK.FLintel).C(2, 2).Cl(BK.ClBox).J(0.05f);
            A("SFBRICK", BK.Brick, new uint[] { 0x8f4a3a, 0x9a5544, 0xb89a7a, 0x7f4034, 0xc4a888, 0x6e4c3c }, TrimStone, AwnAcc).W(BK.WSash, 0.44f, 0.6f, 0.56f, 0.68f)
                .R(BK.RMembrane, GreyRoof, 0, Fl, 1).Fl(3, 8, 3.6f, 2.8f).G(BK.GShop, 8, BK.GRustic, 2).F(BK.FString | BK.FLintel, BK.FFire | BK.FPilaster | BK.FAwning | BK.FAttic).C(2, 2).Cl(BK.ClBox);
            A("USSUB", BK.Siding, SidingUs, TrimWhite, ShutDark).W(BK.WSash, 0.3f, 0.42f, 0.42f, 0.52f).R(BK.RShingle, Shingle, 0.55f, Ga, 6, Hi, 4)
                .Fl(1, 2, 2.9f, 4.2f).G(BK.GSame, 3, BK.GGarage, 2, BK.GStoop, 2).S(BK.SLouver, 0.45f).F(BK.FSurround, BK.FTwoTone).Cl(BK.ClChimney).J(0.05f);
            A("USBRICKH", BK.Brick, new uint[] { 0x8f4a3a, 0x9a5544, 0x7f4034, 0xa25c48, 0xb89a7a, 0x884e40, 0xc4a888 }, TrimWhite, ShutDark).W(BK.WSash, 0.3f, 0.42f, 0.44f, 0.54f)
                .R(BK.RShingle, Shingle, 0.6f, Ga, 6, Hi, 4).Fl(1, 2, 2.9f, 4.0f).G(BK.GSame, 3, BK.GGarage, 1, BK.GStoop, 2).S(BK.SLouver, 0.5f).F(BK.FSill, BK.FLintel).Cl(BK.ClChimney);
            A("USBRICK", BK.Brick, new uint[] { 0x8f4a3a, 0x9a5544, 0x7f4034, 0xa25c48, 0x884e40, 0x6e4c3c, 0xb89a7a }, TrimWhite, ShutDark).W(BK.WSash, 0.34f, 0.44f, 0.52f, 0.64f)
                .R(BK.RSlate, SlateC, 0.9f, Fl, 6, Gx, 2, Ma, 2).Fl(2, 4, 3.2f, 2.4f).G(BK.GStoop, 6, BK.GShop, 2, BK.GSame, 1).S(BK.SLouver, 0.4f)
                .F(BK.FSill | BK.FLintel, BK.FString | BK.FDormer | BK.FWeather).C(1, 2).Cl(BK.ClChimney);
            A("USMAIN", BK.Brick, new uint[] { 0x8f4a3a, 0x9a5544, 0xb89a7a, 0x7f4034, 0xc4a888, 0x6e4c3c, 0xd8d0bc, 0xa0604a }, TrimStone, AwnAcc).W(BK.WSash, 0.4f, 0.54f, 0.54f, 0.66f)
                .R(BK.RMembrane, GreyRoof, 0, Fl, 1).Fl(2, 5, 3.6f, 2.8f).G(BK.GShop, 9, BK.GRustic, 1).F(BK.FLintel, BK.FString | BK.FPilaster | BK.FAwning | BK.FWeather | BK.FSill).C(1, 2).Cl(BK.ClBox);
            A("USSW", BK.Adobe, new uint[] { 0xc8a078, 0xd4ac84, 0xb89068, 0xdcb890, 0xc09470, 0xe0c8a8 }, new uint[] { 0x5a4636, 0x6a503c, 0x3f8a8a }, new uint[] { 0x3f8a8a, 0x5a4636, 0x2a6a8a, 0x8a2a2a }).W(BK.WSmall, 0.3f, 0.42f, 0.4f, 0.5f)
                .R(BK.RGravel, LightRoof, 0, Fl, 7, Hi, 3).Fl(1, 2, 2.9f, 4.4f).G(BK.GSame, 3, BK.GGarage, 1).F(0, BK.FSurround).J(0.04f);
            A("LASPAN", BK.Stucco, new uint[] { 0xf2efe8, 0xf0e8d4, 0xe8dcc0, 0xe6c8a8, 0xf5f3ee, 0xe0c8a0, 0xd9c8a0, 0xe8d0b8 }, TrimWhite, new uint[] { 0x5a4636, 0x2a4a6a, 0x3f6a4a, 0x8a2a2a }).W(BK.WArch, 0.32f, 0.46f, 0.44f, 0.56f)
                .R(BK.RClay, Terracotta, 0.36f, Hi, 5, Ga, 3, Fl, 3).Fl(1, 2, 2.9f, 4.2f).G(BK.GSame, 3, BK.GGarage, 2, BK.GStoop, 1).F(0, BK.FGrille | BK.FSurround | BK.FAwning);
            A("LAAPT", BK.Stucco, new uint[] { 0xf0e8d4, 0xe8dcc0, 0xe6c8a8, 0xc9d3b0, 0xd8c0a8, 0xb9cbd6, 0xe0d4c0, 0xd0b8a0, 0xc8c8c0 }, TrimWhite, AlumAcc).W(BK.WSliding, 0.5f, 0.7f, 0.44f, 0.56f)
                .R(BK.RGravel, LightRoof, 0, Fl, 1).Fl(2, 5, 2.9f, 3.8f).G(BK.GGarage, 4, BK.GShop, 3, BK.GSame, 2).B(BK.BSlab, BK.BmColumns, 0.6f).F(0, BK.FAc | BK.FString | BK.FWeather).Cl(BK.ClBox);
            A("NOLA", BK.Siding, new uint[] { 0xe8c9a0, 0xb9cbd6, 0xc9d3b0, 0xe6b89a, 0xf0e0b0, 0xd8b8c8, 0xf2efe8, 0x8fa7b8, 0xc98a7a, 0xe0a898, 0x9ab08a }, TrimWhite, new uint[] { 0x2f5a44, 0x2a2c30, 0x2a4a6a, 0x8a2a2a, 0x3f6a4a }).W(BK.WFrench, 0.34f, 0.42f, 0.62f, 0.74f)
                .R(BK.RSlate, SlateC, 0.7f, Hi, 5, Ga, 4, Fl, 1).Fl(1, 3, 3.6f, 2.6f).G(BK.GSame, 3, BK.GStoop, 2, BK.GShop, 1).B(BK.BIron, BK.BmAll, 0.7f).S(BK.SLouver, 0.9f).F(BK.FSurround, BK.FWeather).C(1, 2).J(0.06f);
            A("DECO", BK.Stucco, Deco, TrimWhite, new uint[] { 0x3f9a9a, 0xe07a8a, 0xe8c85a, 0x4f8fb8, 0xf0a060 }).W(BK.WRibbon, 0.7f, 0.86f, 0.4f, 0.5f)
                .R(BK.RGravel, LightRoof, 0, Fl, 1).Fl(2, 5, 3.1f, 3.4f).G(BK.GShop, 4, BK.GSame, 2).F(BK.FString | BK.FTwoTone, BK.FPilaster | BK.FAwning).C(1, 1).Cl(BK.ClBox).J(0.04f);
            A("MXCOL", BK.Stucco, MexBold, new uint[] { 0xf4f2ec, 0xe6dcc8, 0x8a2a2a, 0xd9a83f, 0x5a4636 }, Doors).W(BK.WFrench, 0.3f, 0.4f, 0.54f, 0.68f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 8, Hi, 1).Fl(1, 3, 3.6f, 3.4f).G(BK.GSame, 3, BK.GShop, 3, BK.GStoop, 2).B(BK.BIron, BK.BmColumns, 0.5f)
                .F(BK.FGrille | BK.FTwoTone, BK.FSurround | BK.FWeather | BK.FString).C(1, 2).Cl(BK.ClTank).J(0.06f);
            A("COLTILE", BK.Stucco, new uint[] { 0xf2efe8, 0xf5f3ee, 0xe8c85a, 0xe8a04a, 0x4f8fb8, 0xd9584a, 0xf0e0b0, 0x7ab87a, 0xece8df }, new uint[] { 0x3f6a4a, 0x2a4a6a, 0x8a2a2a, 0x5a4636, 0xd9a83f }, Doors).W(BK.WFrench, 0.3f, 0.4f, 0.54f, 0.68f)
                .R(BK.RClay, Terracotta, 0.42f, Ga, 5, Hi, 5).Fl(1, 2, 3.4f, 3.4f).G(BK.GSame, 3, BK.GStoop, 2, BK.GShop, 1).B(BK.BWood, BK.BmAll, 0.5f).F(BK.FTwoTone | BK.FGrille, BK.FWeather).J(0.05f);
            A("HAVANA", BK.Stucco, HavanaPastel, TrimWhite, Doors).W(BK.WFrench, 0.32f, 0.42f, 0.62f, 0.76f).R(BK.RConcrete, LightRoof, 0, Fl, 1)
                .Fl(2, 4, 4.0f, 3.2f).G(BK.GArcade, 5, BK.GShop, 2, BK.GSame, 2).B(BK.BIron, BK.BmAll, 0.8f).S(BK.SLouver, 0.7f).F(BK.FWeather | BK.FSurround, BK.FPilaster | BK.FString).C(2, 2).J(0.07f);
            A("LATINF", BK.Brick, HollowBrick, new uint[] { 0xa8a59c, 0x9c9990, 0xb5b1a8 }, new uint[] { 0x2a2c30, 0x3f6a4a, 0x2a4a6a, 0x8a8e92, 0xf0ece2 }).W(BK.WSmall, 0.34f, 0.5f, 0.38f, 0.5f)
                .R(BK.RConcrete, new uint[] { 0xa8a59c, 0x9c9990, 0xb5b1a8, 0x8f8c84 }, 0, Fl, 1).Fl(1, 4, 2.7f, 3.4f).G(BK.GSame, 3, BK.GShop, 2, BK.GGarage, 1, BK.GBlank, 1)
                .F(BK.FString | BK.FWeather, BK.FGrille).Cl(BK.ClTank).J(0.05f);
            A("LATPAINT", BK.Stucco, new uint[] { 0xe8dcc0, 0xe8c85a, 0x7ab8c8, 0xe8a04a, 0xc9d8a8, 0xe6b0a0, 0xf2efe8, 0x8fc0a0, 0xd8b8c8, 0xe07a5a, 0xa8c0e0, 0xf0e0b0 }, TrimWhite, Doors).W(BK.WPlain, 0.36f, 0.52f, 0.4f, 0.52f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 1).Fl(1, 3, 2.8f, 3.6f).G(BK.GSame, 3, BK.GShop, 2, BK.GGarage, 2).F(BK.FGrille, BK.FWeather | BK.FTwoTone | BK.FAwning).Cl(BK.ClTank).J(0.07f);
            A("LATAPT", BK.Concrete, new uint[] { 0xf0ece2, 0xe8e4d8, 0xe6dcc6, 0xd8d8d0, 0xe0c8a0, 0xc8d0d8, 0xd9b8a0, 0xe8d8c0 }, TrimGrey, AlumAcc).W(BK.WSliding, 0.56f, 0.78f, 0.5f, 0.64f)
                .R(BK.RGravel, GreyRoof, 0, Fl, 1).Fl(8, 22, 2.9f, 3.6f).G(BK.GBlank, 2, BK.GShop, 3).B(BK.BSlab, BK.BmAll, 0.9f).F(0, BK.FAc | BK.FWeather | BK.FString | BK.FPilaster).Cl(BK.ClBox | BK.ClTank);
            A("BRHOUSE", BK.Stucco, new uint[] { 0xf2efe8, 0xe8dcc0, 0xf0e0b0, 0xe6c8a8, 0xc9d8c8, 0xe8c080, 0xd8d0c4, 0xe0a080, 0xb9d0d8 }, TrimWhite, Doors).W(BK.WPlain, 0.36f, 0.52f, 0.4f, 0.52f)
                .R(BK.RClay, new uint[] { 0xb5603c, 0xa9553a, 0x9c4e36, 0x8a5a48, 0xad6646 }, 0.4f, Hi, 5, Ga, 4, Fl, 1).Fl(1, 2, 2.8f, 3.8f).G(BK.GSame, 3, BK.GGarage, 2).F(BK.FGrille, BK.FWeather | BK.FTwoTone).Cl(BK.ClTank).J(0.06f);
            A("ANDES", BK.Adobe, new uint[] { 0xa8845c, 0xb49068, 0xf0ece2, 0xe8dcc0, 0x9a7650, 0xc8a078, 0xe8c85a }, new uint[] { 0x2a4a6a, 0x3f6a4a, 0x5a4636, 0xf4f2ec }, new uint[] { 0x2a4a9a, 0x3f6a4a, 0x8a2a2a, 0x5a4636 }).W(BK.WSmall, 0.28f, 0.4f, 0.38f, 0.5f)
                .R(BK.RClay, Terracotta, 0.42f, Ga, 5, Hi, 3, Fl, 2).Fl(1, 2, 2.8f, 3.6f).G(BK.GSame, 3, BK.GStoop, 1).B(BK.BWood, BK.BmAll, 0.3f).F(BK.FWeather, BK.FTwoTone | BK.FSurround).J(0.05f);
            A("CARIB", BK.Siding, new uint[] { 0xe8c85a, 0x7ab8c8, 0xe07a8a, 0x8fc0a0, 0xf2efe8, 0xe8a04a, 0xa8c0e0, 0xd8b8c8, 0xc9d8a8, 0xf0b0a0, 0x5fa8b8 }, TrimWhite, Doors).W(BK.WSash, 0.32f, 0.44f, 0.46f, 0.6f)
                .R(BK.RCorrugated, Tin, 0.55f, Hi, 6, Ga, 4).Fl(1, 2, 3.0f, 3.2f).G(BK.GSame, 3, BK.GStoop, 2).S(BK.SLouver, 0.8f).F(BK.FSurround, BK.FWeather).J(0.06f);
            A("CAHOUSE", BK.Siding, new uint[] { 0xeceae4, 0xd8d0bc, 0xc4c8c4, 0xb9c8d4, 0x9aa4a8, 0xe0d4b8, 0x8a4a3a, 0x6a7a8a, 0xb8c0a8, 0x5a6a7a, 0xc8b8a0 }, TrimWhite, ShutDark).W(BK.WSash, 0.32f, 0.44f, 0.42f, 0.54f)
                .R(BK.RShingle, Shingle, 0.75f, Ga, 6, Hi, 4).Fl(1, 2, 2.9f, 4.0f).G(BK.GSame, 3, BK.GGarage, 2, BK.GStoop, 2).F(BK.FSurround, BK.FTwoTone).Cl(BK.ClChimney);
            A("PLEX", BK.Brick, new uint[] { 0x8f4a3a, 0x9a5544, 0x7f4034, 0xb89a7a, 0x6e4c3c, 0xa0604a, 0xc9b79a }, TrimStone, Doors).W(BK.WSash, 0.36f, 0.48f, 0.52f, 0.62f)
                .R(BK.RMembrane, GreyRoof, 0, Fl, 7, Ma, 3).Fl(2, 3, 3.2f, 2.6f).G(BK.GStoop, 6, BK.GSame, 1).B(BK.BIron, BK.BmAll, 0.7f).F(BK.FLintel | BK.FSill, BK.FDormer | BK.FString).C(1, 2);

            // ================= North Africa, the Middle East, Africa
            A("MEDINA", BK.Lime, PureWhite, PureWhite, ShutBlue).W(BK.WSmall, 0.24f, 0.36f, 0.34f, 0.5f).R(BK.RConcrete, new uint[] { 0xeceae4, 0xe0ded8, 0xd4d0c8, 0xf2f0ea }, 0, Fl, 1)
                .Fl(1, 3, 3.2f, 3.6f).G(BK.GStoop, 5, BK.GBlank, 2, BK.GShop, 2).S(BK.SScreen, 0.5f).F(BK.FGrille, BK.FSurround | BK.FWeather).J(0.02f);
            A("MEDINAR", BK.Adobe, Marrakech, new uint[] { 0xb87458, 0xd29878, 0xe6dcc8 }, new uint[] { 0x5a4636, 0x3f6a4a, 0x2a4a6a, 0x6a503c }).W(BK.WSmall, 0.22f, 0.34f, 0.32f, 0.48f)
                .R(BK.RConcrete, new uint[] { 0xc99a7c, 0xbf8f70, 0xd2a688 }, 0, Fl, 1).Fl(1, 3, 3.2f, 3.8f).G(BK.GStoop, 4, BK.GBlank, 3, BK.GShop, 2).S(BK.SScreen, 0.3f).F(BK.FGrille, BK.FSurround).J(0.035f);
            A("MEDINAF", BK.Lime, new uint[] { 0xe8dcc0, 0xe0d0b0, 0xece4d0, 0xd8c8a8, 0xf0ece2 }, TrimStone, new uint[] { 0x5a4636, 0x3f6a4a, 0x2a4a6a, 0x6a503c }).W(BK.WSmall, 0.22f, 0.34f, 0.32f, 0.48f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 1).Fl(2, 4, 3.2f, 3.8f).G(BK.GStoop, 4, BK.GBlank, 3, BK.GShop, 2).S(BK.SScreen, 0.3f).F(BK.FGrille | BK.FWeather, BK.FSurround);
            A("CHAOUEN", BK.Lime, ChaouenBlue, PureWhite, ShutBlue).W(BK.WSmall, 0.22f, 0.34f, 0.32f, 0.48f).R(BK.RClay, Terracotta, 0.35f, Fl, 6, Hi, 3)
                .Fl(1, 3, 3.0f, 3.6f).G(BK.GStoop, 5, BK.GBlank, 2).F(BK.FGrille | BK.FTwoTone, BK.FSurround).J(0.05f);
            A("MAGMOD", BK.Stucco, new uint[] { 0xf2efe8, 0xf5f3ee, 0xece8df, 0xe8dcc0, 0xf0e8d4, 0xe6dcc6 }, TrimWhite, ShutBlue).W(BK.WPlain, 0.38f, 0.54f, 0.44f, 0.56f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 1).Fl(2, 5, 3.0f, 3.6f).G(BK.GShop, 4, BK.GGarage, 2, BK.GSame, 2, BK.GBlank, 1).B(BK.BSlab, BK.BmColumns, 0.6f).S(BK.SLouver, 0.6f)
                .F(BK.FGrille, BK.FWeather | BK.FAc | BK.FSurround | BK.FAwning).Cl(BK.ClTank).J(0.03f);
            A("MAGMODM", BK.Stucco, new uint[] { 0xe8c8a8, 0xdcb898, 0xc98b6b, 0xe8dcc0, 0xd8a888, 0xf0e0c8, 0xe0c0a0 }, TrimWhite, new uint[] { 0x5a4636, 0x8a8e92, 0x3f6a4a, 0x6a503c }).W(BK.WPlain, 0.38f, 0.54f, 0.44f, 0.56f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 1).Fl(2, 5, 3.0f, 3.6f).G(BK.GShop, 5, BK.GGarage, 2, BK.GSame, 1).B(BK.BSlab, BK.BmColumns, 0.5f).S(BK.SRoller, 0.5f)
                .F(BK.FGrille, BK.FWeather | BK.FAc | BK.FSurround).Cl(BK.ClTank).J(0.04f);
            A("TUNCOL", BK.Stucco, PureWhite, TrimWhite, ShutBlue).W(BK.WFrench, 0.34f, 0.42f, 0.6f, 0.72f).R(BK.RConcrete, LightRoof, 0, Fl, 1)
                .Fl(3, 6, 3.5f, 3.0f).G(BK.GShop, 6, BK.GArcade, 3).B(BK.BIron, BK.BmAll, 0.85f).S(BK.SLouver, 0.9f).F(BK.FSurround | BK.FString, BK.FWeather | BK.FLintel | BK.FPilaster | BK.FAwning).C(1, 2).Cl(BK.ClBox).J(0.02f);
            A("ALGCOL", BK.Stucco, PureWhite, TrimWhite, new uint[] { 0x2f6fb0, 0x8c9aa0, 0x2a5f9a, 0x6f8a80 }).W(BK.WFrench, 0.34f, 0.42f, 0.6f, 0.72f).R(BK.RClay, Terracotta, 0.36f, Fl, 6, Hi, 3)
                .Fl(4, 7, 3.4f, 3.0f).G(BK.GShop, 6, BK.GArcade, 3).B(BK.BIron, BK.BmAll, 0.9f).S(BK.SLouver, 0.85f).F(BK.FSurround | BK.FString | BK.FWeather, BK.FLintel | BK.FPilaster).C(2, 2).J(0.02f);
            A("CAIRO", BK.Stucco, new uint[] { 0xc8b898, 0xd4c4a4, 0xb8a888, 0xc0ac8c, 0xdcd0b4, 0xa89878, 0xcab49a }, TrimStone, new uint[] { 0x5a4636, 0x4f6a54, 0x6a5a48, 0x8a8e92 }).W(BK.WFrench, 0.34f, 0.44f, 0.54f, 0.66f)
                .R(BK.RConcrete, new uint[] { 0xb8ac94, 0xa89c84, 0xc4b8a0 }, 0, Fl, 1).Fl(4, 9, 3.2f, 3.2f).G(BK.GShop, 8, BK.GArcade, 1).B(BK.BSlab, BK.BmAll, 0.8f).S(BK.SLouver, 0.7f)
                .F(BK.FWeather | BK.FAc, BK.FString | BK.FAwning | BK.FSurround).C(0, 2).Cl(BK.ClTank | BK.ClBox).J(0.05f);
            A("CAIROINF", BK.Brick, new uint[] { 0x9c5a44, 0xa8644c, 0x8f5240, 0xb06e54 }, new uint[] { 0xa8a59c, 0x9c9990, 0xb5b1a8 }, AlumAcc).W(BK.WSmall, 0.34f, 0.48f, 0.38f, 0.5f)
                .R(BK.RConcrete, new uint[] { 0xa8a094, 0x9c9488, 0xb4ac9c }, 0, Fl, 1).Fl(4, 10, 2.9f, 3.4f).G(BK.GShop, 3, BK.GBlank, 2, BK.GSame, 1).F(BK.FString | BK.FPilaster | BK.FWeather, BK.FAc).Cl(BK.ClTank);
            A("GULFVILLA", BK.Stucco, Sand, new uint[] { 0xf0e8d8, 0xe6dcc8, 0xc4ad88 }, new uint[] { 0x5a4636, 0x6a503c, 0x8a8e92, 0x2a2c30 }).W(BK.WArch, 0.3f, 0.44f, 0.44f, 0.58f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 9, Hi, 1).Fl(1, 3, 3.4f, 4.2f).G(BK.GSame, 3, BK.GGarage, 1, BK.GBlank, 1).F(BK.FSurround, BK.FString | BK.FAc | BK.FTwoTone).C(0, 2).Cl(BK.ClTank | BK.ClBox).J(0.04f);
            A("GULFAPT", BK.Stucco, new uint[] { 0xd8c4a0, 0xe0cfae, 0xf0e8d8, 0xcdb893, 0xe8dcc0, 0xc8c0b0, 0xd9c8a8 }, TrimStone, AlumAcc).W(BK.WPlain, 0.44f, 0.6f, 0.44f, 0.58f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 1).Fl(3, 9, 3.1f, 3.4f).G(BK.GShop, 7, BK.GBlank, 1).B(BK.BSlab, BK.BmColumns, 0.5f).Gl(3, 1, 0).F(BK.FAc, BK.FString | BK.FWeather | BK.FPilaster).Cl(BK.ClTank | BK.ClBox);
            A("LEVANT", BK.Stone, Limestone, TrimStone, new uint[] { 0x4f6a54, 0x5a4636, 0x8a8e92, 0x2a4a6a, 0x2a2c30 }).W(BK.WArch, 0.34f, 0.46f, 0.46f, 0.6f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 8, Hi, 2).Fl(2, 6, 3.1f, 3.6f).G(BK.GShop, 4, BK.GSame, 3, BK.GGarage, 1).B(BK.BSlab, BK.BmColumns, 0.6f).S(BK.SRoller, 0.4f)
                .F(0, BK.FGrille | BK.FString | BK.FAc | BK.FSill).Cl(BK.ClSolar | BK.ClTank).J(0.035f);
            A("BEIRUT", BK.Stucco, new uint[] { 0xe8dcc0, 0xe6c89a, 0xd9c8a0, 0xf0e8d4, 0xe0b8a0, 0xc8c0b0, 0xd8d0c4 }, TrimStone, new uint[] { 0x4f6a54, 0x8a2a2a, 0x2a4a6a, 0x5a4636 }).W(BK.WArch, 0.36f, 0.46f, 0.58f, 0.7f)
                .R(BK.RClay, RedTile, 0.45f, Hi, 5, Fl, 5).Fl(3, 8, 3.3f, 3.4f).G(BK.GShop, 6, BK.GArcade, 2).B(BK.BSlab, BK.BmAll, 0.9f).S(BK.SLouver, 0.6f).F(BK.FWeather, BK.FAwning | BK.FAc | BK.FString).Cl(BK.ClTank | BK.ClSolar);
            A("MUD", BK.Adobe, Mud, new uint[] { 0xf0ece2, 0xe8e0d0, 0xa8845c }, new uint[] { 0x5a4636, 0x2a4a6a, 0x3f6a4a, 0x6a503c }).W(BK.WSmall, 0.2f, 0.32f, 0.3f, 0.44f)
                .R(BK.RConcrete, Mud, 0, Fl, 1).Fl(1, 4, 3.0f, 3.8f).G(BK.GBlank, 3, BK.GStoop, 3, BK.GSame, 1).F(BK.FSurround, BK.FString | BK.FWeather).C(0, 3).J(0.04f);
            A("IRAN", BK.Brick, new uint[] { 0xc8b088, 0xbca47c, 0xd4bc94, 0xb09870, 0xe0d0b0, 0xa89070 }, TrimStone, AlumAcc).W(BK.WPlain, 0.44f, 0.62f, 0.46f, 0.6f)
                .R(BK.RConcrete, new uint[] { 0xb8ac94, 0xa89c84, 0xc4b8a0, 0x8a8c8f }, 0, Fl, 1).Fl(2, 6, 3.1f, 3.6f).G(BK.GGarage, 3, BK.GShop, 3, BK.GSame, 2).B(BK.BSlab, BK.BmColumns, 0.5f)
                .F(BK.FAc, BK.FGrille | BK.FString | BK.FWeather | BK.FSill).Cl(BK.ClTank | BK.ClBox);
            A("AFRBLOCK", BK.Stucco, AfrPaint, TrimWhite, Doors).W(BK.WPlain, 0.36f, 0.52f, 0.4f, 0.52f).R(BK.RCorrugated, Tin, 0.4f, Hi, 6, Ga, 4, Fl, 2)
                .Fl(1, 2, 2.9f, 3.8f).G(BK.GSame, 3, BK.GShop, 2, BK.GStoop, 1).F(BK.FGrille, BK.FWeather | BK.FTwoTone | BK.FAwning).J(0.07f);
            A("AFRMID", BK.Concrete, new uint[] { 0xe8dcc0, 0xe0c890, 0xc9d8c8, 0xf0e0b0, 0xe8b890, 0xb9d0d8, 0xd8d0c4, 0xf2efe8, 0xd0a878 }, TrimWhite, AlumAcc).W(BK.WPlain, 0.44f, 0.62f, 0.42f, 0.54f)
                .R(BK.RConcrete, LightRoof, 0, Fl, 7, Hi, 2).Fl(3, 8, 3.0f, 3.6f).G(BK.GShop, 7, BK.GBlank, 1).B(BK.BSlab, BK.BmColumns, 0.6f).F(BK.FWeather, BK.FAc | BK.FString | BK.FAwning | BK.FGrille).Cl(BK.ClTank).J(0.07f);
            A("AFRINF", BK.Metal, new uint[] { 0x9aa0a4, 0x8a5a48, 0xa8aeb0, 0x7a6a58, 0xb0a898, 0x6a8a9a, 0x94705a }, TrimGrey, Doors).W(BK.WNone, 0.3f, 0.3f, 0.3f, 0.3f)
                .R(BK.RCorrugated, Tin, 0.2f, Ga, 5, Fl, 5).Fl(1, 1, 2.6f, 3.0f).G(BK.GBlank, 1).F(BK.FWeather).J(0.06f);
            A("ZASUB", BK.Stucco, new uint[] { 0xf0e8d4, 0xe8dcc0, 0xe6c8a8, 0xd9c8a0, 0xf2efe8, 0xc8b8a0, 0xe0d0b8, 0xb8a890 }, TrimWhite, Doors).W(BK.WPlain, 0.4f, 0.56f, 0.42f, 0.52f)
                .R(BK.RClay, new uint[] { 0x8a5a48, 0x7a5140, 0x9b4232, 0x4a4d52, 0x6b473a }, 0.5f, Hi, 7, Ga, 3).Fl(1, 2, 2.9f, 4.2f).G(BK.GSame, 3, BK.GGarage, 2).F(BK.FGrille, BK.FTwoTone | BK.FSill);

            // ================= Asia and the Pacific
            A("INCONC", BK.Stucco, IndiaPaint, TrimWhite, new uint[] { 0x5a4636, 0x8a8e92, 0x2a4a6a, 0x3f6a4a, 0x8a2a2a }).W(BK.WPlain, 0.4f, 0.56f, 0.42f, 0.54f)
                .R(BK.RConcrete, new uint[] { 0xb8b0a0, 0xa8a094, 0xc4bcac, 0x9c948a }, 0, Fl, 1).Fl(2, 5, 3.0f, 3.4f).G(BK.GShop, 5, BK.GGarage, 2, BK.GSame, 2).B(BK.BSlab, BK.BmColumns, 0.8f)
                .F(BK.FWeather | BK.FGrille, BK.FAc | BK.FString | BK.FAwning | BK.FTwoTone).Cl(BK.ClTank).J(0.08f);
            A("INAPT", BK.Concrete, IndiaPaint, TrimWhite, AlumAcc).W(BK.WSliding, 0.5f, 0.7f, 0.46f, 0.58f).R(BK.RConcrete, new uint[] { 0xb8b0a0, 0xa8a094, 0xc4bcac }, 0, Fl, 1)
                .Fl(7, 24, 2.9f, 3.4f).G(BK.GBlank, 2, BK.GShop, 2, BK.GArcade, 1).B(BK.BSlab, BK.BmColumns, 0.85f).F(BK.FWeather | BK.FAc, BK.FString | BK.FPilaster | BK.FGrille).Cl(BK.ClTank | BK.ClBox).J(0.07f);
            A("INPINK", BK.Stucco, JaipurPink, new uint[] { 0xf4f2ec, 0xeeeae0 }, new uint[] { 0x5a4636, 0x3f6a4a, 0x2a4a6a }).W(BK.WMoorish, 0.3f, 0.42f, 0.46f, 0.6f).R(BK.RConcrete, new uint[] { 0xc99a8c, 0xb8b0a0, 0xa8a094 }, 0, Fl, 1)
                .Fl(2, 4, 3.2f, 2.8f).G(BK.GShop, 6, BK.GArcade, 3).S(BK.SScreen, 0.5f).F(BK.FSurround | BK.FWeather, BK.FString | BK.FAwning).C(1, 3).Cl(BK.ClTank).J(0.04f);
            A("INBLUE", BK.Lime, JodhpurBlue, new uint[] { 0xf4f2ec, 0xa8c4e4 }, new uint[] { 0x2a4a9a, 0x5a4636, 0x3f6a4a }).W(BK.WSmall, 0.26f, 0.38f, 0.38f, 0.52f).R(BK.RConcrete, new uint[] { 0xb8b0a0, 0xa8b8cc, 0xc4bcac }, 0, Fl, 1)
                .Fl(1, 4, 3.0f, 3.2f).G(BK.GStoop, 4, BK.GShop, 2, BK.GSame, 2).F(BK.FWeather | BK.FGrille, BK.FSurround).Cl(BK.ClTank).J(0.05f);
            A("KERALA", BK.Stucco, IndiaPaint, TrimWhite, WoodAcc).W(BK.WPlain, 0.36f, 0.5f, 0.42f, 0.54f).R(BK.RClay, new uint[] { 0xa9553a, 0x9c4e36, 0x8a4a3a, 0xb5603c }, 0.6f, Hi, 7, Ga, 3)
                .Fl(1, 2, 3.0f, 3.8f).G(BK.GSame, 3, BK.GStoop, 1).F(BK.FGrille | BK.FWeather, BK.FTwoTone).J(0.07f);
            A("CNWALK", BK.Tile, CnTile, TrimGrey, AlumAcc).W(BK.WSliding, 0.56f, 0.76f, 0.48f, 0.6f).R(BK.RConcrete, new uint[] { 0xa8a59c, 0x9c9990, 0xb5b1a8, 0x8a5a48 }, 0, Fl, 8, Hi, 2)
                .Fl(5, 7, 2.9f, 3.4f).G(BK.GShop, 5, BK.GBlank, 2, BK.GSame, 2).B(BK.BSlab, BK.BmColumns, 0.7f).F(BK.FAc | BK.FWeather | BK.FGrille, BK.FString).Cl(BK.ClSolar | BK.ClBox).J(0.05f);
            A("CNTOWER", BK.Tile, new uint[] { 0xe0d6c4, 0xd8c0b0, 0xe8e4dc, 0xcfc4b4, 0xd0c8c0, 0xc8b8a8, 0xe0d0c0, 0xb8a898, 0xd9c8b0 }, TrimGrey, AlumAcc).W(BK.WSliding, 0.56f, 0.76f, 0.5f, 0.62f)
                .R(BK.RConcrete, GreyRoof, 0, Fl, 9, Py, 1).Fl(18, 34, 2.9f, 3.2f).G(BK.GShop, 3, BK.GBlank, 2).B(BK.BSlab, BK.BmColumns, 0.75f).Gl(1, 2, 0).F(BK.FAc, BK.FString | BK.FPilaster | BK.FTwoTone | BK.FAttic).Cl(BK.ClBox);
            A("HUTONG", BK.Brick, HutongGrey, new uint[] { 0x8a2a2a, 0x7a3a2e, 0x6a6c70 }, new uint[] { 0x8a2a2a, 0x9a3a2f, 0x5a4636, 0x3f6a4a }).W(BK.WGrid, 0.5f, 0.7f, 0.46f, 0.6f)
                .R(BK.RKawara, new uint[] { 0x5a5c5e, 0x66686a, 0x4e5052, 0x707274 }, 0.55f, Ga, 9, Hi, 1).Fl(1, 1, 3.2f, 3.2f).G(BK.GSame, 3, BK.GStoop, 2, BK.GBlank, 2).F(BK.FWeather, BK.FSill).J(0.03f);
            A("JIANGNAN", BK.Lime, PureWhite, new uint[] { 0x3a3c40, 0x4a4c50 }, DarkWood).W(BK.WGrid, 0.38f, 0.54f, 0.42f, 0.56f).R(BK.RKawara, DarkTile, 0.55f, Ga, 8, Hi, 2)
                .Fl(1, 2, 3.0f, 3.4f).G(BK.GSame, 3, BK.GShop, 2, BK.GStoop, 2).F(BK.FWeather, BK.FSurround).C(0, 1).J(0.02f);
            A("SHLILONG", BK.Brick, new uint[] { 0x8a4a3a, 0x7a5a50, 0x6e6a68, 0x9a5544, 0x807c78 }, TrimStone, new uint[] { 0x2a2c30, 0x5a4636, 0x8a2a2a }).W(BK.WFrench, 0.36f, 0.46f, 0.52f, 0.64f)
                .R(BK.RClay, new uint[] { 0x8a4a3a, 0x7a5140, 0x9b4232 }, 0.7f, Gx, 6, Ga, 3, Fl, 1).Fl(2, 3, 3.2f, 2.6f).G(BK.GStoop, 5, BK.GShop, 3).B(BK.BIron, BK.BmColumns, 0.4f).F(BK.FLintel | BK.FSill | BK.FWeather, BK.FString | BK.FDormer | BK.FAc).C(0, 1);
            A("HKTONG", BK.Concrete, new uint[] { 0xd8c8b0, 0xc8d0c8, 0xe0c0b0, 0xb8c8d0, 0xd9c8a0, 0xe8dcc0, 0xc0b8a8, 0xd0b0a8, 0xa8b8b0 }, TrimGrey, AlumAcc).W(BK.WGrid, 0.56f, 0.74f, 0.46f, 0.58f)
                .R(BK.RConcrete, GreyRoof, 0, Fl, 1).Fl(5, 12, 2.9f, 3.0f).G(BK.GShop, 9, BK.GBlank, 1).F(BK.FAc | BK.FWeather | BK.FString, BK.FAwning | BK.FGrille | BK.FFire).Cl(BK.ClBox | BK.ClTank).J(0.06f);
            A("HKTOWER", BK.Tile, new uint[] { 0xe0d0c0, 0xd8c0b0, 0xc8d0c8, 0xe8dcc8, 0xd0b8b0, 0xb8c8c8, 0xe0c8b0, 0xc8b8a8 }, TrimGrey, AlumAcc).W(BK.WGrid, 0.5f, 0.68f, 0.46f, 0.56f)
                .R(BK.RConcrete, GreyRoof, 0, Fl, 1).Fl(25, 50, 2.8f, 2.6f).G(BK.GShop, 3, BK.GBlank, 1).Gl(2, 1, 0).F(BK.FAc | BK.FPilaster, BK.FString | BK.FWeather | BK.FTwoTone).Cl(BK.ClBox);
            A("JPHOUSE", BK.Siding, JpSiding, TrimDark, new uint[] { 0x4a3a2e, 0x8a8e92, 0x5a5e62, 0x2a2c30, 0x6a503c }).W(BK.WSliding, 0.42f, 0.62f, 0.4f, 0.54f)
                .R(BK.RKawara, new uint[] { 0x3f4348, 0x4a4d52, 0x35393e, 0x3f5068, 0x5e4338, 0x54504c, 0x4a5a70, 0x6a6c70 }, 0.55f, Hi, 5, Ga, 5).Fl(2, 2, 2.7f, 3.4f).G(BK.GSame, 3, BK.GGarage, 2, BK.GStoop, 1)
                .B(BK.BSlab, BK.BmColumns, 0.5f).F(0, BK.FAc | BK.FTwoTone | BK.FSill).J(0.04f);
            A("JPMID", BK.Tile, JpTile, TrimGrey, AlumAcc).W(BK.WSliding, 0.54f, 0.74f, 0.48f, 0.6f).R(BK.RConcrete, GreyRoof, 0, Fl, 1)
                .Fl(3, 10, 2.9f, 3.2f).G(BK.GShop, 5, BK.GGarage, 2, BK.GBlank, 2).B(BK.BSlab, BK.BmColumns, 0.7f).F(BK.FAc, BK.FString | BK.FPilaster | BK.FAwning | BK.FFire).Cl(BK.ClBox).J(0.05f);
            A("JPOFFICE", BK.Tile, new uint[] { 0xc8c4bc, 0xb8b4ac, 0xd8d4cc, 0xa8a098, 0x8a8680, 0xe4e0d8, 0x9a9088 }, TrimGrey, AlumAcc).W(BK.WRibbon, 0.74f, 0.88f, 0.44f, 0.6f).R(BK.RConcrete, GreyRoof, 0, Fl, 1)
                .Fl(4, 12, 3.4f, 2.4f).G(BK.GShop, 8, BK.GBlank, 1).Gl(0, 1, 2).F(0, BK.FPilaster | BK.FString | BK.FAwning).Cl(BK.ClBox);
            A("MACHIYA", BK.Boards, DarkWood, new uint[] { 0xe8e0d0, 0x3e3028 }, DarkWood).W(BK.WGrid, 0.56f, 0.8f, 0.44f, 0.6f).R(BK.RKawara, DarkTile, 0.5f, Gx, 7, Ga, 2, Hi, 1)
                .Fl(1, 2, 2.7f, 2.8f).G(BK.GSame, 3, BK.GShop, 2).S(BK.SScreen, 0.6f).F(BK.FTwoTone, BK.FWeather).J(0.03f);
            A("KRVILLA", BK.Brick, new uint[] { 0x9a5a48, 0x8a5244, 0xa8685a, 0xd8d0c4, 0xb8b0a8, 0xc8a890, 0x7a4a40 }, TrimGrey, AlumAcc).W(BK.WSliding, 0.46f, 0.66f, 0.42f, 0.54f)
                .R(BK.RConcrete, KrGreen, 0, Fl, 1).Fl(2, 5, 2.9f, 3.4f).G(BK.GGarage, 4, BK.GShop, 3, BK.GSame, 2).F(BK.FAc | BK.FSill, BK.FGrille | BK.FString | BK.FWeather).Cl(BK.ClTank | BK.ClBox);
            A("KRAPT", BK.Concrete, new uint[] { 0xe8e4dc, 0xdcd8d0, 0xe0d8c8, 0xd0d4d8, 0xece8e0, 0xd8d0c0 }, new uint[] { 0x8a9aa8, 0xa89080, 0x7a9a8a, 0xb0a0a0, 0x6a8ab0 }, AlumAcc).W(BK.WSliding, 0.66f, 0.84f, 0.52f, 0.62f)
                .R(BK.RConcrete, GreyRoof, 0, Fl, 9, Hi, 1).Fl(15, 30, 2.8f, 3.4f).G(BK.GBlank, 3, BK.GSame, 1).F(BK.FTwoTone | BK.FAc, BK.FString | BK.FPilaster).Cl(BK.ClBox).J(0.03f);
            A("HANOK", BK.Lime, new uint[] { 0xf0ece2, 0xe8e0d0 }, DarkWood, DarkWood).W(BK.WGrid, 0.5f, 0.7f, 0.44f, 0.58f).R(BK.RKawara, DarkTile, 0.5f, Hi, 6, Ga, 4)
                .Fl(1, 1, 3.0f, 3.0f).G(BK.GSame, 1).F(BK.FTwoTone | BK.FSurround).J(0.02f);
            A("SHOPHOUSE", BK.Stucco, new uint[] { 0xe8c9a0, 0xa8d0c8, 0xe6b0a0, 0xf0d890, 0xb8c8e0, 0xd8b8c8, 0xc9d8a8, 0xf2efe8, 0xe0a070, 0x8fc0b0, 0xf0c0a0 }, TrimWhite, new uint[] { 0x3f6a4a, 0x2a4a6a, 0x8a2a2a, 0x5a4636, 0x2f7f7a, 0xd9a83f }).W(BK.WFrench, 0.4f, 0.52f, 0.58f, 0.72f)
                .R(BK.RClay, Terracotta, 0.55f, Gx, 8, Fl, 2).Fl(2, 3, 3.6f, 1.8f).G(BK.GArcade, 6, BK.GShop, 4).S(BK.SLouver, 0.95f).F(BK.FSurround | BK.FPilaster, BK.FString | BK.FWeather | BK.FLintel).C(1, 2).J(0.06f);
            A("VNTUBE", BK.Stucco, new uint[] { 0xf0e0b0, 0xe8dcc0, 0xc9d8c8, 0xe8c85a, 0xb9d0d8, 0xe6b0a0, 0xf2efe8, 0xd8c0d0, 0xc8d8a8, 0xe0a070, 0xa8c8c0 }, TrimWhite, new uint[] { 0x3f6a4a, 0x5a4636, 0x8a8e92, 0x2a4a6a }).W(BK.WFrench, 0.46f, 0.6f, 0.56f, 0.7f)
                .R(BK.RCorrugated, Tin, 0.3f, Fl, 6, Gx, 4).Fl(3, 5, 3.3f, 2.4f).G(BK.GShop, 8, BK.GGarage, 2).B(BK.BIron, BK.BmAll, 0.85f).F(BK.FWeather, BK.FAc | BK.FAwning | BK.FSurround | BK.FGrille).C(0, 2).Cl(BK.ClTank).J(0.07f);
            A("SEACONC", BK.Concrete, new uint[] { 0xe0d8c8, 0xd0c8b8, 0xc8d0c8, 0xe8dcc0, 0xd8c0b0, 0xb8c0c0, 0xe6c89a, 0xc0b8a8 }, TrimGrey, AlumAcc).W(BK.WPlain, 0.5f, 0.68f, 0.44f, 0.56f)
                .R(BK.RCorrugated, Tin, 0.25f, Fl, 7, Hi, 3).Fl(2, 5, 3.2f, 3.6f).G(BK.GShop, 9, BK.GGarage, 1).F(BK.FWeather | BK.FAc, BK.FAwning | BK.FGrille | BK.FString).Cl(BK.ClTank).J(0.06f);
            A("IDHOUSE", BK.Stucco, new uint[] { 0xf2efe8, 0xe8dcc0, 0xf0e0b0, 0xc9d8c8, 0xe8c9a0, 0xb9d0d8, 0xe6b0a0, 0xd8d0c4, 0xc8d8a8 }, TrimWhite, Doors).W(BK.WPlain, 0.38f, 0.54f, 0.42f, 0.54f)
                .R(BK.RClay, new uint[] { 0xc8683a, 0xb5603c, 0xbd5f35, 0x9c4e36, 0x8a5a48 }, 0.6f, Hi, 6, Ga, 4).Fl(1, 2, 3.0f, 3.6f).G(BK.GSame, 3, BK.GGarage, 1, BK.GShop, 1).F(BK.FGrille | BK.FWeather, BK.FTwoTone).J(0.06f);
            A("THAIWOOD", BK.Boards, new uint[] { 0x6a503c, 0x8a6a4c, 0x5a4636, 0xb89a6e, 0x7a5a40 }, TrimWhite, WoodAcc).W(BK.WGrid, 0.36f, 0.5f, 0.44f, 0.56f).R(BK.RCorrugated, Tin, 0.7f, Ga, 7, Hi, 3)
                .Fl(1, 2, 2.8f, 3.2f).G(BK.GSame, 1).S(BK.SLouver, 0.5f).F(BK.FWeather).J(0.05f);
            A("AUSHOUSE", BK.Brick, new uint[] { 0x9a5a48, 0xb8866a, 0xc8a890, 0x8a4a3a, 0xd8c8b0, 0x7a5a50, 0xe0d4c0, 0xa87058 }, TrimWhite, Doors).W(BK.WPlain, 0.42f, 0.6f, 0.42f, 0.52f)
                .R(BK.RCorrugated, Colorbond, 0.45f, Hi, 8, Ga, 2).Fl(1, 2, 2.8f, 4.4f).G(BK.GSame, 3, BK.GGarage, 2).F(0, BK.FSill | BK.FTwoTone | BK.FAwning);
            A("AUSTER", BK.Stucco, new uint[] { 0xe8dcc0, 0xf2efe8, 0xd9c8a0, 0xc9d3b0, 0xe6c8a8, 0xb9cbd6, 0xd8ccb2, 0x9a5a48, 0xc8c0b0 }, TrimWhite, Doors).W(BK.WFrench, 0.34f, 0.44f, 0.58f, 0.7f)
                .R(BK.RCorrugated, Colorbond, 0.6f, Gx, 6, Hi, 2, Fl, 2).Fl(2, 3, 3.3f, 2.6f).G(BK.GStoop, 6, BK.GShop, 3).B(BK.BIron, BK.BmAll, 0.9f).F(BK.FSurround, BK.FString | BK.FLintel | BK.FWeather).C(1, 2).Cl(BK.ClChimney);
            A("NZVILLA", BK.Siding, new uint[] { 0xeceae4, 0xe8dcc0, 0xc9d3b0, 0xb9cbd6, 0xd8b8a8, 0x8a98a4, 0xe0d4b8, 0x6a7a8a, 0xa8b0a0, 0xc98a7a }, TrimWhite, Doors).W(BK.WSash, 0.34f, 0.46f, 0.48f, 0.6f)
                .R(BK.RCorrugated, Colorbond, 0.6f, Hi, 6, Ga, 4).Fl(1, 2, 3.0f, 3.8f).G(BK.GSame, 3, BK.GStoop, 2).F(BK.FSurround, BK.FTwoTone).Cl(BK.ClChimney);

            // ================= everywhere: works, sheds, places of worship
            A("INDMETAL", BK.Metal, MetalSiding, TrimGrey, new uint[] { 0x2a4a8a, 0x8a2a2a, 0x3f6a4a, 0xd9a83f, 0x5a5e62, 0xe07a3a }).W(BK.WRibbon, 0.7f, 0.86f, 0.14f, 0.22f)
                .R(BK.RCorrugated, new uint[] { 0xb0b4b8, 0xc8ccd0, 0x9aa0a4, 0xd4d0c4, 0x8a9098 }, 0.12f, Fl, 5, Ga, 5).Fl(1, 2, 5.0f, 6.0f).G(BK.GGarage, 3, BK.GBlank, 3).F(BK.FTwoTone, BK.FWeather).J(0.04f);
            A("INDBRICK", BK.Brick, new uint[] { 0x8f4a3a, 0x7f4034, 0x9a5544, 0x6e4c3c, 0xb39b72, 0xa89068 }, TrimStone, new uint[] { 0x2a2c30, 0x3f5a48, 0x5a5e62 }).W(BK.WGrid, 0.62f, 0.8f, 0.5f, 0.66f)
                .R(BK.RMembrane, GreyRoof, 0.3f, Fl, 6, Ga, 4).Fl(1, 4, 4.4f, 4.0f).G(BK.GGarage, 2, BK.GBlank, 2, BK.GSame, 2).F(BK.FWeather | BK.FLintel, BK.FPilaster | BK.FString).C(0, 1).Cl(BK.ClBox);
            A("BIGBOX", BK.Concrete, new uint[] { 0xd8d4c8, 0xc8c4b8, 0xe4e0d4, 0xc8b8a0, 0xb8bcc0, 0xe0d0b8 }, TrimGrey, new uint[] { 0x2a4a8a, 0x9a2a2a, 0x3f6a4a, 0xd9a83f, 0xe07a3a, 0x5a5e62 }).W(BK.WNone, 0.3f, 0.3f, 0.3f, 0.3f)
                .R(BK.RGravel, LightRoof, 0, Fl, 1).Fl(1, 2, 5.5f, 6.0f).G(BK.GShop, 3, BK.GBlank, 3, BK.GGarage, 1).F(BK.FTwoTone, BK.FWeather | BK.FPilaster).Cl(BK.ClBox);
            A("SHED", BK.Siding, new uint[] { 0xc8c0b0, 0xa89c8c, 0x8a7a68, 0xd8d4c8, 0x9aa0a4, 0x6a5a4c, 0xb8b4ac, 0x7a8a7a }, TrimGrey, Doors).W(BK.WNone, 0.3f, 0.3f, 0.3f, 0.3f)
                .R(BK.RCorrugated, new uint[] { 0x77797d, 0x8a8c8f, 0x6a6c70, 0x7a5a48, 0x94928c, 0x5a5650 }, 0.35f, Fl, 5, Ga, 5).Fl(1, 1, 2.8f, 3.0f).G(BK.GBlank, 2, BK.GGarage, 1).F(0, BK.FWeather).J(0.06f);
            A("CHURCH", BK.Stone, new uint[] { 0xb8b0a0, 0xc8c0ac, 0xa8a090, 0xd4cab4, 0x9c9488, 0xc0b49c }, TrimStone, new uint[] { 0x5a3a2a, 0x3a2a26, 0x2a3a4a }).W(BK.WArch, 0.3f, 0.4f, 0.7f, 0.86f)
                .R(BK.RSlate, new uint[] { 0x4a4f57, 0x565b63, 0x7a5140, 0x6fa08c, 0x5d5f66 }, 1.2f, Ga, 8, Hi, 2).Fl(1, 2, 7.0f, 4.4f).G(BK.GRustic, 1).F(BK.FSurround | BK.FWeather, BK.FPilaster | BK.FString).C(0, 1);
            A("CHURCHBR", BK.Brick, new uint[] { 0x8f4a3a, 0x7f4034, 0x9a5544, 0xa25c48, 0x6e4c3c }, TrimStone, new uint[] { 0x5a3a2a, 0x3a2a26, 0x2a3a4a }).W(BK.WArch, 0.3f, 0.4f, 0.7f, 0.86f)
                .R(BK.RSlate, new uint[] { 0x4a4f57, 0x565b63, 0x6fa08c, 0x5d5f66 }, 1.2f, Ga, 8, Hi, 2).Fl(1, 2, 7.0f, 4.4f).G(BK.GRustic, 1).F(BK.FSurround, BK.FPilaster | BK.FString | BK.FWeather).C(0, 1);
            A("CHURCHW", BK.Lime, PureWhite, TrimStone, new uint[] { 0x5a3a2a, 0x2a4a8a, 0x3a2a26 }).W(BK.WArch, 0.28f, 0.38f, 0.6f, 0.8f)
                .R(BK.RClay, Terracotta, 0.6f, Ga, 8, Hi, 2).Fl(1, 2, 6.5f, 4.4f).G(BK.GRustic, 1).F(BK.FSurround, BK.FPilaster | BK.FTwoTone).C(0, 2);
            A("MOSQUE", BK.Stucco, new uint[] { 0xf2efe8, 0xe8dcc0, 0xd8c4a0, 0xf5f3ee, 0xe0cfae, 0xc8d8c8 }, TrimStone, new uint[] { 0x2f7f5a, 0x2a5f9a, 0x5a4636, 0xd9a83f }).W(BK.WMoorish, 0.3f, 0.42f, 0.6f, 0.78f)
                .R(BK.RConcrete, new uint[] { 0xd8d4c8, 0x6fa08c, 0xe8e4d8, 0x5a8ab0, 0xd9b84a }, 0.9f, Py, 5, Fl, 5).Fl(1, 2, 6.0f, 4.0f).G(BK.GArcade, 3, BK.GRustic, 1).F(BK.FSurround | BK.FString, BK.FTwoTone).C(1, 3);
            A("TEMPLE", BK.Boards, new uint[] { 0x8a2a22, 0x6a3a2e, 0x5a4636, 0x9a3a2f, 0x4a3a2e, 0xb0483a }, new uint[] { 0xe8e0d0, 0xd9a83f, 0x3e3028 }, DarkWood).W(BK.WGrid, 0.6f, 0.8f, 0.5f, 0.66f)
                .R(BK.RKawara, new uint[] { 0x4a4d52, 0x3f4348, 0x5a6a5a, 0x6a6c70, 0xb07a3a }, 0.6f, Hi, 6, Py, 3, Ga, 1).Fl(1, 2, 4.5f, 3.2f).G(BK.GSame, 1).F(BK.FTwoTone | BK.FPilaster).C(0, 1);
            A("TEMPLEIN", BK.Stone, new uint[] { 0xd8c4a0, 0xe8dcc0, 0xc8a078, 0xf2efe8, 0xe0a070, 0xd9b84a }, TrimStone, new uint[] { 0x8a2a2a, 0xd9a83f, 0x5a4636 }).W(BK.WMoorish, 0.28f, 0.4f, 0.5f, 0.7f)
                .R(BK.RConcrete, new uint[] { 0xd8c4a0, 0xe8dcc0, 0xc8a078, 0xd9b84a }, 1.4f, Py, 7, Fl, 3).Fl(1, 2, 5.0f, 3.6f).G(BK.GArcade, 1, BK.GRustic, 1).F(BK.FSurround | BK.FString | BK.FWeather).C(1, 3);
            A("GENERIC", BK.Stucco, new uint[] { 0xd9cfc0, 0xc9b8a3, 0xb7c0c7, 0xe2ddd4, 0xc58f73, 0xa9b3a6, 0x9aa7b5, 0xd6c48f, 0xe8dcc0, 0xc8c0b0 }, TrimWhite, Doors).W(BK.WPlain, 0.38f, 0.54f, 0.44f, 0.56f)
                .R(BK.RShingle, new uint[] { 0x8c8f94, 0x9b958a, 0x7b8087, 0x8a6a5a, 0x9c5f4b, 0x6f7479, 0x5f6b78 }, 0.6f, Fl, 4, Hi, 3, Ga, 3).Fl(1, 3, 3.0f, 3.6f).G(BK.GSame, 3, BK.GShop, 1, BK.GGarage, 1).F(0, BK.FSill | BK.FSurround | BK.FWeather);
            A("GENMID", BK.Concrete, new uint[] { 0xd9cfc0, 0xc9b8a3, 0xb7c0c7, 0xe2ddd4, 0xc8c0b0, 0xa9b3a6, 0xd8d4c8, 0xe0d0b8 }, TrimGrey, AlumAcc).W(BK.WPlain, 0.46f, 0.64f, 0.44f, 0.58f)
                .R(BK.RGravel, GreyRoof, 0, Fl, 1).Fl(3, 8, 3.1f, 3.4f).G(BK.GShop, 4, BK.GBlank, 2, BK.GSame, 1).B(BK.BSlab, BK.BmColumns, 0.4f).F(0, BK.FString | BK.FWeather | BK.FAc | BK.FPilaster).Cl(BK.ClBox);

            // ================= who builds what, where
            const string T = "GLASS:5 SLAB:3 OFFICE:1";
            Reg("world", "GENERIC", "GENMID:3 OFFICE:1", T);
            Reg("paris", "HAUSS:6 FRN:2 PMOD:1", "HAUSS:10 PMOD:1.5", "PMOD:3 GLASS:3 SLAB:2");
            Reg("france-n", "FRN:6 FRBRICK:2 DEHOUSE:1", "HAUSS:3 PMOD:3 FRN:2 FRBRICK:1", "SLAB:4 GLASS:2 PMOD:2");
            Reg("france-s", "FRS:8 PROV:2", "PROV:5 PMOD:3 HAUSS:1", "SLAB:4 GLASS:2 PMOD:2");
            Reg("lyon", "PROV:4 HAUSS:3 FRS:2", "HAUSS:5 PROV:4 PMOD:1", "SLAB:3 GLASS:3");
            Reg("london", "UKTER:6 UKSTUC:2 UKGEO:2 UKSEMI:1", "UKGEO:4 UKSTUC:3 UKCOUNCIL:2 UKTER:1 OFFICE:1", "GLASS:6 SLAB:2 UKCOUNCIL:1");
            Reg("uk", "UKSEMI:5 UKTER:5 UKSTUC:0.5", "UKTER:3 UKCOUNCIL:3 UKGEO:2 OFFICE:1", "SLAB:3 GLASS:3 UKCOUNCIL:2");
            Reg("scotland", "UKSEMI:3 UKTER:3", "UKGEO:3 UKCOUNCIL:2 UKTER:2", "SLAB:3 GLASS:2 UKCOUNCIL:2");
            Reg("amsterdam", "NLCANAL:8 NLROW:2", "NLCANAL:7 NLROW:1 NMOD:2", "GLASS:4 SLAB:2 NMOD:2");
            Reg("benelux", "NLROW:6 NLCANAL:2 FRBRICK:2", "NLCANAL:3 NMOD:4 DEALT:1 NLROW:2", "SLAB:3 GLASS:3 NMOD:2");
            Reg("germany", "DEHOUSE:8 FACH:0.6 FRBRICK:0.6", "DEALT:6 PANEL:1.5 NMOD:2 FACH:0.5", "SLAB:3 GLASS:3 PANELT:2");
            Reg("germany-e", "DEHOUSE:7 FACH:0.4", "DEALT:5 PANEL:4 NMOD:1", "PANELT:5 GLASS:2 SLAB:2");
            Reg("alps", "CHALET:6 DEHOUSE:3", "ALPCITY:7 DEALT:2 NMOD:1", "SLAB:3 GLASS:2");
            Reg("nordic", "NWOOD:9 NMOD:1", "NCITY:5 NMOD:4 NWOOD:1", "SLAB:3 GLASS:3 NMOD:2");
            Reg("denmark", "NMOD:4 NWOOD:2 DEHOUSE:3", "NCITY:6 NMOD:3", "SLAB:3 GLASS:3");
            Reg("iceland", "CARIB:3 NWOOD:3 NMOD:3", "NMOD:5 NCITY:3", "SLAB:3 GLASS:3");
            Reg("italy", "ITHOUSE:8 ITOCHRE:2", "ITOCHRE:8 POLYK:1 PMOD:1", "SLAB:4 GLASS:2 PMOD:2");
            Reg("venice", "VENICE:8 ITOCHRE:2", "VENICE:8 ITOCHRE:2", "SLAB:1 GLASS:1");
            Reg("spain", "ESWHITE:3 FRS:3 PTHOUSE:2", "ESCITY:5 ESMOD:5", "ESMOD:4 SLAB:3 GLASS:2");
            Reg("andalusia", "ESWHITE:9 FRS:1", "ESWHITE:3 ESCITY:4 ESMOD:3", "ESMOD:4 SLAB:3 GLASS:1");
            Reg("portugal", "PTHOUSE:8 LISPAINT:1", "LISPAINT:4 LISBOA:3 ESMOD:2", "SLAB:4 GLASS:2 ESMOD:2");
            Reg("lisbon", "LISPAINT:5 LISBOA:4 PTHOUSE:1", "LISBOA:5 LISPAINT:5 ESMOD:1", "SLAB:3 GLASS:2 ESMOD:2");
            Reg("greece", "GRHOUSE:7 CYCLADES:1 POLYK:2", "POLYK:9 GRHOUSE:1", "POLYK:3 SLAB:3 GLASS:1");
            Reg("cyclades", "CYCLADES:10", "CYCLADES:10", "CYCLADES:1");
            Reg("balkans", "EEHOUSE:5 GRHOUSE:3 ITHOUSE:2", "TRAPT:4 PANEL:3 DEALT:2 ITOCHRE:1", "PANELT:4 SLAB:3 GLASS:1");
            Reg("turkey", "TRAPT:3 GRHOUSE:4 TROLD:1 EEHOUSE:2", "TRAPT:9 TROLD:1", "SLAB:4 GLASS:3 TRAPT:2");
            Reg("istanbul", "TROLD:3 TRAPT:6", "TRAPT:8 TROLD:2 DEALT:1", "GLASS:4 SLAB:4");
            Reg("easteurope", "EEHOUSE:8 DACHA:1.5", "PANEL:5 DEALT:3 RUNEO:1 NMOD:1", "PANELT:5 SLAB:2 GLASS:2");
            Reg("russia", "DACHA:6 EEHOUSE:3", "PANEL:7 RUNEO:2 STALIN:1", "PANELT:6 STALIN:1 GLASS:2");
            Reg("petersburg", "RUNEO:6 DACHA:2 EEHOUSE:1", "RUNEO:8 STALIN:1 PANEL:2", "PANELT:5 GLASS:2 STALIN:1");
            Reg("moscow", "DACHA:3 EEHOUSE:2 RUNEO:3", "STALIN:3 PANEL:4 RUNEO:3", "PANELT:5 GLASS:3 STALIN:2");
            Reg("nyc", "BROWN:4 NYROW:5 USSUB:1 USBRICKH:1", "TENEMENT:6 NYROW:2 NYLOFT:2 BROWN:1", "PREWAR:6 GLASS:5");
            Reg("nyc-outer", "NYROW:4 USSUB:4 USBRICKH:3 BROWN:1", "TENEMENT:5 USMAIN:3 NYROW:1", "PREWAR:5 GLASS:3 SLAB:2");
            Reg("us-ne", "USSUB:5 USBRICKH:3 USBRICK:2", "USBRICK:4 USMAIN:4 OFFICE:2", "GLASS:5 PREWAR:3 SLAB:1");
            Reg("boston", "USBRICK:6 USSUB:4", "USBRICK:6 USMAIN:3 OFFICE:1", "GLASS:5 PREWAR:3");
            Reg("chicago", "USBRICKH:5 PLEX:3 USSUB:3", "USMAIN:5 NYLOFT:2 PLEX:2 OFFICE:1", "GLASS:5 PREWAR:5");
            Reg("us-mid", "USSUB:7 USBRICKH:3", "USMAIN:6 OFFICE:3", "GLASS:5 PREWAR:2 SLAB:1");
            Reg("us-south", "USSUB:6 USBRICKH:4", "USMAIN:5 OFFICE:4", "GLASS:6 PREWAR:1 SLAB:1");
            Reg("nola", "NOLA:8 USSUB:2", "NOLA:6 USMAIN:3 OFFICE:1", "GLASS:5 SLAB:2");
            Reg("florida", "LASPAN:4 USSUB:4 DECO:1", "DECO:4 LAAPT:4 OFFICE:2", "SLAB:5 GLASS:5");
            Reg("miami", "LASPAN:5 DECO:3 USSUB:1", "DECO:7 LAAPT:3", "SLAB:5 GLASS:6");
            Reg("us-sw", "USSW:5 LASPAN:3 USSUB:2", "LAAPT:4 USSW:3 OFFICE:3", "GLASS:6 SLAB:2");
            Reg("california", "LASPAN:4 USSUB:5 USSW:0.5", "LAAPT:6 USMAIN:2 OFFICE:3", "GLASS:6 SLAB:2");
            Reg("sf", "SFVIC:6 SFMARINA:4", "SFEDW:6 SFBRICK:3 SFVIC:1 OFFICE:1", "GLASS:6 PREWAR:2 SLAB:1");
            Reg("us-nw", "USSUB:8 SFVIC:1 CAHOUSE:2", "USMAIN:4 LAAPT:3 OFFICE:3", "GLASS:7 SLAB:2");
            Reg("canada", "CAHOUSE:7 USBRICKH:3", "USMAIN:4 PLEX:2 OFFICE:3 UKCOUNCIL:1", "GLASS:6 SLAB:4");
            Reg("montreal", "PLEX:7 CAHOUSE:2", "PLEX:5 USMAIN:3 OFFICE:2", "GLASS:5 SLAB:4");
            Reg("mexico", "LATINF:4 LATPAINT:4 MXCOL:2", "MXCOL:3 LATPAINT:3 LAAPT:2 LATINF:2", "LATAPT:5 GLASS:4");
            Reg("mexico-old", "MXCOL:8 LATPAINT:2", "MXCOL:8 LATPAINT:1 LAAPT:1", "LATAPT:4 GLASS:4");
            Reg("caribbean", "CARIB:7 LATPAINT:3", "HAVANA:3 LATPAINT:4 CARIB:2 DECO:1", "LATAPT:5 SLAB:3 GLASS:2");
            Reg("havana", "HAVANA:7 CARIB:1 LATPAINT:2", "HAVANA:9 DECO:1", "LATAPT:5 SLAB:3");
            Reg("andes", "LATINF:5 ANDES:3 COLTILE:2", "LATINF:4 LATPAINT:3 MXCOL:2 LAAPT:1", "LATAPT:6 GLASS:3");
            Reg("colombia", "COLTILE:4 LATINF:4 LATPAINT:2", "LATINF:3 LATPAINT:3 MXCOL:2 LAAPT:2", "LATAPT:6 GLASS:3");
            Reg("brazil", "BRHOUSE:6 LATINF:3 LATPAINT:1", "LATPAINT:3 LAAPT:3 LATINF:2 MXCOL:1", "LATAPT:8 GLASS:2");
            Reg("southcone", "BRHOUSE:5 LATPAINT:3 ITHOUSE:2", "ESCITY:3 HAUSS:1.5 LAAPT:3 ESMOD:3", "LATAPT:6 SLAB:2 GLASS:2");
            Reg("tunis-medina", "MEDINA:10", "MEDINA:7 TUNCOL:3", "MAGMOD:1");
            Reg("tunis", "MAGMOD:6 MEDINA:2 TUNCOL:2", "TUNCOL:5 MAGMOD:5", "SLAB:3 GLASS:3 MAGMOD:1");
            Reg("tunisia", "MAGMOD:6 MEDINA:4", "MAGMOD:7 TUNCOL:2 MEDINA:1", "SLAB:3 GLASS:2 MAGMOD:1");
            Reg("algeria", "MAGMOD:5 MEDINA:2 MAGMODM:2", "ALGCOL:4 MAGMOD:4 PANEL:1.5", "SLAB:4 PANELT:3 GLASS:1");
            Reg("morocco", "MAGMODM:5 MEDINAF:2 MAGMOD:3", "MAGMODM:6 MAGMOD:3 TUNCOL:1", "SLAB:3 GLASS:2");
            Reg("marrakech", "MEDINAR:9 MAGMODM:1", "MEDINAR:7 MAGMODM:3", "MAGMODM:1");
            Reg("fes", "MEDINAF:9 MAGMODM:1", "MEDINAF:8 MAGMODM:2", "MAGMODM:1");
            Reg("chaouen", "CHAOUEN:10", "CHAOUEN:10", "CHAOUEN:1");
            Reg("egypt", "CAIROINF:4 MAGMODM:3 MUD:2", "CAIRO:4 CAIROINF:6", "CAIROINF:3 SLAB:3 GLASS:2");
            Reg("cairo", "CAIROINF:5 CAIRO:4", "CAIRO:6 CAIROINF:4", "SLAB:3 GLASS:3 CAIROINF:2");
            Reg("sahara", "MUD:5 MAGMODM:4 MEDINAF:1", "MAGMODM:6 MUD:3", "MAGMODM:1");
            Reg("gulf", "GULFVILLA:9 GULFAPT:1", "GULFAPT:9 OFFICE:1", "GLASSGULF:8 SLAB:2");
            Reg("levant", "LEVANT:8 MAGMOD:2", "LEVANT:8 BEIRUT:1 GULFAPT:1", "SLAB:4 GLASS:3 LEVANT:1");
            Reg("beirut", "BEIRUT:6 LEVANT:4", "BEIRUT:8 LEVANT:2", "SLAB:5 GLASS:4");
            Reg("yemen", "MUD:8 GULFVILLA:2", "MUD:7 GULFAPT:3", "GULFAPT:1");
            Reg("iran", "IRAN:8 MUD:1 GULFVILLA:1", "IRAN:9 GULFAPT:1", "SLAB:5 GLASS:2 IRAN:1");
            Reg("sahel", "MUD:5 AFRBLOCK:4 AFRINF:1", "AFRMID:6 MUD:2 AFRBLOCK:2", "AFRMID:3 GLASS:1");
            Reg("africa", "AFRBLOCK:7 AFRINF:2 ZASUB:1", "AFRMID:8 AFRBLOCK:2", "AFRMID:3 GLASS:3 SLAB:3");
            Reg("southafrica", "ZASUB:6 AFRBLOCK:3 AFRINF:1", "AFRMID:4 USMAIN:2 OFFICE:3 LAAPT:2", "GLASS:5 SLAB:4");
            Reg("india", "INCONC:8 KERALA:0.6 AFRBLOCK:1", "INCONC:8 INAPT:2", "INAPT:8 GLASS:2");
            Reg("india-s", "INCONC:6 KERALA:4", "INCONC:8 INAPT:2", "INAPT:8 GLASS:2");
            Reg("jaipur", "INPINK:8 INCONC:2", "INPINK:8 INCONC:2", "INAPT:1");
            Reg("jodhpur", "INBLUE:8 INCONC:2", "INBLUE:6 INCONC:4", "INAPT:1");
            Reg("china", "CNWALK:3 JIANGNAN:2 HUTONG:2 SEACONC:3", "CNWALK:8 JPOFFICE:1 OFFICE:1", "CNTOWER:8 GLASS:3");
            Reg("beijing", "HUTONG:8 CNWALK:2", "CNWALK:7 HUTONG:1 OFFICE:2", "CNTOWER:6 GLASS:4");
            Reg("shanghai", "SHLILONG:6 JIANGNAN:2 CNWALK:2", "SHLILONG:3 CNWALK:6 OFFICE:1", "CNTOWER:6 GLASS:5");
            Reg("jiangnan", "JIANGNAN:7 CNWALK:3", "CNWALK:8 JIANGNAN:1 OFFICE:1", "CNTOWER:7 GLASS:3");
            Reg("hongkong", "HKTONG:6 SEACONC:3 CNWALK:1", "HKTONG:9 OFFICE:1", "HKTOWER:8 GLASS:3");
            Reg("japan", "JPHOUSE:9 MACHIYA:0.5", "JPMID:7 JPOFFICE:3", "GLASS:5 SLAB:3 JPOFFICE:2");
            Reg("kyoto", "MACHIYA:5 JPHOUSE:5", "JPMID:6 JPOFFICE:2 MACHIYA:2", "GLASS:3 SLAB:3 JPOFFICE:2");
            Reg("korea", "KRVILLA:8 HANOK:0.6 JPHOUSE:1", "KRVILLA:6 JPOFFICE:3 JPMID:1", "KRAPT:8 GLASS:3");
            Reg("sea-shop", "SHOPHOUSE:8 IDHOUSE:2", "SHOPHOUSE:6 SEACONC:3 OFFICE:1", "LATAPT:4 GLASS:5 HKTOWER:2");
            Reg("vietnam", "VNTUBE:8 SEACONC:1 THAIWOOD:1", "VNTUBE:7 SEACONC:3", "LATAPT:4 GLASS:3 CNTOWER:2");
            Reg("sea", "SEACONC:4 THAIWOOD:2 IDHOUSE:3 SHOPHOUSE:1", "SEACONC:7 SHOPHOUSE:1 OFFICE:1 INAPT:1", "LATAPT:5 GLASS:4 HKTOWER:1");
            Reg("indonesia", "IDHOUSE:8 SEACONC:2", "SEACONC:7 SHOPHOUSE:1 OFFICE:1", "LATAPT:5 GLASS:4");
            Reg("australia", "AUSHOUSE:8 NZVILLA:1 AUSTER:1", "AUSTER:2 USMAIN:3 LAAPT:3 OFFICE:2", "GLASS:6 SLAB:4");
            Reg("sydney", "AUSTER:5 AUSHOUSE:5", "AUSTER:4 USMAIN:3 LAAPT:2 OFFICE:1", "GLASS:6 SLAB:4");
            Reg("nz", "NZVILLA:7 AUSHOUSE:3", "USMAIN:4 LAAPT:3 OFFICE:3", "GLASS:5 SLAB:4");
            Reg("pacific", "CARIB:5 AFRBLOCK:4 THAIWOOD:1", "SEACONC:6 AFRMID:4", "SLAB:1 GLASS:1");
            world = regions["world"];

            // ---- cities with a look of their own: centre, radius in km
            C(48.8566, 2.3522, 7.5, "paris"); C(45.76, 4.84, 5, "lyon");
            C(51.509, -0.118, 11, "london");
            C(52.372, 4.895, 4.5, "amsterdam");
            C(45.4375, 12.335, 4, "venice");
            C(38.715, -9.14, 6, "lisbon"); C(41.15, -8.61, 4, "lisbon");
            C(41.02, 28.97, 9, "istanbul");
            C(59.94, 30.32, 9, "petersburg"); C(55.752, 37.62, 14, "moscow");
            C(40.76, -73.97, 9, "nyc"); C(40.70, -73.92, 24, "nyc-outer");
            C(42.36, -71.07, 7, "boston"); C(39.95, -75.16, 7, "boston"); C(39.29, -76.61, 6, "boston"); C(38.905, -77.035, 7, "boston");
            C(41.88, -87.65, 22, "chicago");
            C(29.958, -90.065, 6, "nola");
            C(25.78, -80.16, 9, "miami");
            C(37.765, -122.44, 9.5, "sf");
            C(45.51, -73.58, 12, "montreal"); C(46.813, -71.21, 5, "montreal");
            C(19.433, -99.135, 3.5, "mexico-old"); C(21.017, -101.255, 3, "mexico-old"); C(20.915, -100.745, 3, "mexico-old"); C(17.06, -96.725, 3, "mexico-old"); C(19.043, -98.198, 3, "mexico-old");
            C(10.423, -75.55, 2.5, "mexico-old"); C(23.135, -82.36, 8, "havana"); C(21.8, -79.98, 2.5, "havana");
            C(36.7985, 10.171, 0.95, "tunis-medina"); C(36.806, 10.18, 14, "tunis"); C(36.87, 10.342, 1.2, "tunis-medina"); C(35.678, 10.1, 1.0, "tunis-medina");
            C(35.827, 10.639, 0.8, "tunis-medina"); C(34.739, 10.76, 0.8, "tunis-medina");
            C(31.629, -7.989, 2.6, "marrakech"); C(34.063, -4.976, 2.2, "fes"); C(35.169, -5.263, 1.6, "chaouen");
            C(30.045, 31.24, 22, "cairo");
            C(33.89, 35.50, 8, "beirut");
            C(26.923, 75.826, 3.2, "jaipur"); C(26.297, 73.018, 2.2, "jodhpur");
            C(39.925, 116.397, 7.5, "beijing"); C(31.225, 121.47, 8, "shanghai"); C(31.31, 120.62, 6, "jiangnan"); C(30.25, 120.16, 6, "jiangnan");
            C(22.32, 114.17, 30, "hongkong"); C(22.19, 113.55, 7, "hongkong");
            C(35.005, 135.765, 7, "kyoto"); C(34.685, 135.83, 3, "kyoto"); C(36.565, 136.66, 3, "kyoto");
            C(1.30, 103.84, 24, "sea-shop"); C(5.415, 100.335, 5, "sea-shop"); C(2.195, 102.25, 4, "sea-shop"); C(3.145, 101.695, 6, "sea-shop"); C(7.885, 98.39, 3, "sea-shop"); C(15.877, 108.328, 2, "sea-shop");
            C(-33.88, 151.2, 9, "sydney"); C(-37.81, 144.97, 9, "sydney");
            C(36.42, 25.43, 14, "cyclades"); C(37.445, 25.33, 9, "cyclades"); C(37.08, 25.15, 14, "cyclades"); C(37.05, 25.48, 16, "cyclades"); C(36.72, 24.45, 12, "cyclades"); C(36.09, 28.09, 3, "cyclades");

            // ---- the rest of the world, first match wins: south, north, west, east
            Bx(36.2, 37.9, 24.2, 26.2, "cyclades");
            Bx(63.2, 66.8, -25, -13, "iceland");
            Bx(54.6, 61, -8.7, -0.6, "scotland"); Bx(49.8, 61, -11, 2, "uk");
            Bx(50.7, 53.7, 3.2, 7.2, "benelux"); Bx(49.4, 51.6, 2.5, 6.4, "benelux");
            Bx(54.5, 57.9, 7.9, 13, "denmark"); Bx(55, 71.5, 4, 32, "nordic");
            Bx(45.7, 47.9, 5.9, 16.5, "alps");
            Bx(50.2, 54.7, 11.6, 15.1, "germany-e"); Bx(47.2, 55.1, 5.8, 15.1, "germany");
            Bx(46.2, 51.2, -5.2, 8.3, "france-n"); Bx(42.3, 46.2, -1.9, 7.8, "france-s"); Bx(41.3, 43.1, 8.5, 9.6, "france-s");
            Bx(36.9, 42.2, -9.6, -6.2, "portugal"); Bx(32.3, 33.2, -17.4, -16.2, "portugal"); Bx(36.5, 40, -32, -24, "portugal");
            Bx(35.9, 38.6, -7.6, -1.6, "andalusia"); Bx(27.5, 29.5, -18.3, -13.3, "andalusia"); Bx(35.9, 43.9, -9.4, 4.4, "spain");
            Bx(36.6, 47.1, 6.6, 18.6, "italy"); Bx(35.8, 36.1, 14.1, 14.6, "levant");
            Bx(34.8, 41.8, 19.3, 28.3, "greece"); Bx(34.5, 35.7, 32.2, 34.6, "greece");
            Bx(35.8, 42.2, 25.6, 44.8, "turkey");
            Bx(39.6, 46.9, 13.3, 23.2, "balkans"); Bx(41.2, 44.3, 22.3, 28.7, "balkans");
            Bx(43.6, 59.7, 14.1, 32, "easteurope"); Bx(44.3, 52.4, 22, 40.3, "easteurope");
            Bx(38.4, 43.6, 39.9, 50.4, "easteurope");
            Bx(41, 82, 27, 180, "russia"); Bx(35.1, 55.5, 46.4, 87.4, "russia"); Bx(41.5, 52.2, 87.4, 120, "russia");
            Bx(30.2, 37.6, 7.5, 11.6, "tunisia"); Bx(18.9, 37.2, -8.7, 12, "algeria"); Bx(27.6, 35.95, -13.2, -1, "morocco");
            Bx(19.5, 33.3, 9.3, 25.2, "algeria"); Bx(21.9, 31.7, 24.7, 36.9, "egypt");
            Bx(29.4, 37.4, 34.2, 42.4, "levant"); Bx(29, 37.4, 38.8, 48.6, "levant");
            Bx(12.1, 19, 42, 54, "yemen"); Bx(12, 32.3, 34.5, 59.9, "gulf");
            Bx(25, 39.8, 44, 63.3, "iran"); Bx(29.3, 38.5, 60.5, 75, "iran"); Bx(23.6, 37.1, 60.8, 77.9, "iran");
            Bx(10.5, 27.6, -17.6, 38, "sahel");
            Bx(-35, -22, 16.4, 33, "southafrica");
            Bx(-35, 16, -18, 52, "africa"); Bx(-26, -11.9, 43, 51, "africa");
            Bx(8, 14.5, 74, 80.4, "india-s"); Bx(5.9, 9.9, 79.5, 82, "india-s"); Bx(6, 35.7, 68, 89.9, "india"); Bx(20.5, 29.5, 88, 97.4, "india");
            Bx(30, 45.6, 129.3, 146, "japan"); Bx(24, 30, 122.9, 131.4, "japan");
            Bx(33, 43.1, 124.5, 129.6, "korea");
            Bx(21.5, 25.4, 119.9, 122.1, "hongkong");
            Bx(8.4, 23.4, 102.1, 109.5, "vietnam");
            Bx(-11, 6, 95, 141, "indonesia"); Bx(0.8, 7.5, 99.5, 119.3, "indonesia");
            Bx(5, 28.6, 92, 109.5, "sea"); Bx(4.5, 21.2, 116.8, 127, "sea");
            Bx(18.1, 53.6, 73.4, 135.1, "china");
            Bx(-47.5, -34, 166, 179, "nz"); Bx(-44, -10, 112.8, 154, "australia");
            Bx(-25, 25, 130, 180, "pacific"); Bx(-25, 30, -180, -128, "pacific");
            Bx(24.3, 31, -87.7, -79.8, "florida");
            Bx(32.3, 42.1, -124.5, -114.2, "california"); Bx(31.2, 37.1, -114.9, -103, "us-sw"); Bx(35.9, 42.1, -120.1, -109, "us-sw");
            Bx(42, 49.1, -125, -116.5, "us-nw"); Bx(42, 49.1, -116.5, -104, "us-mid"); Bx(37, 42.1, -109.1, -102, "us-mid");
            Bx(38.7, 47.6, -80.6, -66.8, "us-ne");
            Bx(25.8, 36.6, -106.7, -75.3, "us-south"); Bx(36.5, 39.6, -89.6, -75, "us-south");
            Bx(36, 49.5, -104.1, -80, "us-mid");
            Bx(41.6, 84, -141.1, -52, "canada"); Bx(51, 72, -170, -129.9, "canada"); Bx(18.8, 22.3, -160.4, -154.7, "california");
            Bx(9.9, 27.9, -85.6, -59.3, "caribbean");
            Bx(14.4, 32.8, -118, -86.6, "mexico"); Bx(7, 18.6, -92.4, -77.1, "mexico");
            Bx(-4.3, 12.6, -79.2, -66.8, "colombia"); Bx(0.6, 12.3, -73, -59.7, "colombia");
            Bx(-23.9, 1.6, -81.5, -62.5, "andes"); Bx(-56, -17.4, -76, -66.5, "southcone");
            Bx(-56, -21.7, -70.6, -53, "southcone"); Bx(-34, 5.4, -74.1, -34.6, "brazil");
            Bx(1, 9, -62, -51.5, "caribbean");
        }

        // ------------------------------------------------------------ choosing

        static float Rnd(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xffffff) / 16777216f; }

        static Region Find(double lat, double lon, ref uint s)
        {
            double cos = Math.Cos(lat * Geo.Deg);
            float r = Rnd(ref s);
            foreach (City c in cities)
            {
                double dy = (lat - c.Lat) * 111.2, dx = (lon - c.Lon) * 111.2 * cos;
                if (Math.Abs(dy) > c.Km || Math.Abs(dx) > c.Km) continue;
                double d = Math.Sqrt(dx * dx + dy * dy) / c.Km;
                // the city's look thins out over the outer third of its radius
                if (d < 0.67 + 0.33 * r) return c.R;
            }
            // region borders are soft: each building looks a little way off to find its region
            double jl = lat + (Rnd(ref s) - 0.5) * 0.5, jo = lon + (Rnd(ref s) - 0.5) * 0.5 / Math.Max(0.2, cos);
            foreach (Box b in boxes)
                if (jl >= b.Lat0 && jl < b.Lat1 && jo >= b.Lon0 && jo < b.Lon1) return b.R;
            return world;
        }

        static Arch Draw(Arch[] a, float[] w, float r)
        {
            for (int i = 0; i < w.Length; i++) if (r < w[i]) return a[i];
            return a[a.Length - 1];
        }

        static int Weighted(int[] pairs, float r)
        {
            int sum = 0;
            for (int i = 1; i < pairs.Length; i += 2) sum += pairs[i];
            float x = r * sum;
            for (int i = 0; i < pairs.Length; i += 2) { x -= pairs[i + 1]; if (x < 0) return pairs[i]; }
            return pairs[0];
        }

        static uint Tone(uint c, float jitter, ref uint s)
        {
            float r = ((c >> 16) & 255) / 255f, g = ((c >> 8) & 255) / 255f, b = (c & 255) / 255f;
            float v = 1f + (Rnd(ref s) - 0.5f) * 2f * jitter;                 // lighter or darker
            float sat = 1f + (Rnd(ref s) - 0.5f) * 2.4f * jitter;             // more or less colour
            float warm = (Rnd(ref s) - 0.5f) * jitter * 0.6f;                 // warmer or cooler
            float m = (r + g + b) / 3f;
            r = (m + (r - m) * sat) * v + warm; g = (m + (g - m) * sat) * v; b = (m + (b - m) * sat) * v - warm;
            return Pack(r, g, b);
        }

        static uint Pack(float r, float g, float b)
        {
            int ri = (int)(Math.Min(1f, Math.Max(0f, r)) * 255f + 0.5f), gi = (int)(Math.Min(1f, Math.Max(0f, g)) * 255f + 0.5f), bi = (int)(Math.Min(1f, Math.Max(0f, b)) * 255f + 0.5f);
            return (uint)((ri << 16) | (gi << 8) | bi);
        }

        static bool Hex(string s, out uint c)
        {
            c = 0;
            if (s == null || s.Length != 7 || s[0] != '#') return false;
            return uint.TryParse(s.Substring(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out c);
        }

        static bool IsShed(string cls, string sub)
        {
            switch (cls)
            {
                case "garage": case "garages": case "shed": case "roof": case "hut": case "carport": case "greenhouse": case "kiosk": case "cabin":
                case "static_caravan": case "farm_auxiliary": case "barn": case "stable": case "sty": case "cowshed": case "toilets": case "bunker": case "boathouse": case "allotment_house":
                    return true;
            }
            return sub == "outbuilding" || sub == "agricultural";
        }

        /// <summary>
        /// Decide how a building looks. height and floors are what the map says (NaN when it says nothing; then
        /// DefFloors times FloorReal is a fair height); area is the footprint in real square metres.
        /// </summary>
        static readonly uint[] TankC = { 0xeceae4, 0xe2e4e3, 0xc9ccce, 0xd9d2b8, 0xbccab8, 0xe9e6dc, 0xb9c4cc };

        /// <summary>
        /// The look of a thing that is not a building: a mast, a cooling tower, a tank... Bare walls of steel or
        /// concrete, in the map's own colours where it gives them.
        /// </summary>
        public static Look Structure(int kind, uint hash, string fCol, string fMat, string rCol)
        {
            uint s = hash * 2654435761u + 0x51ed270bu;
            if (s == 0) s = 1;
            Rnd(ref s); Rnd(ref s);
            var k = new Look { Struct = kind, Name = "STRUCT", Place = "", Cat = BK.CatShed, Win = BK.WNone, Ground = BK.GBlank, FloorReal = 3f, BayReal = 3f, Pitch = 0.5f, RoofShape = BK.ShFlat, DefFloors = 1 };
            uint wall;
            switch (kind)
            {
                case BK.StMast: k.Mat = BK.Metal; wall = 0xb4b8ba; break;
                case BK.StTank: k.Mat = BK.Metal; wall = TankC[(int)(Rnd(ref s) * TankC.Length) % TankC.Length]; break;
                case BK.StSilo: k.Mat = Rnd(ref s) < 0.5f ? BK.Metal : BK.Concrete; wall = k.Mat == BK.Metal ? 0xc4c8cau : 0xc9c4b8u; break;
                case BK.StWaterTower: k.Mat = Rnd(ref s) < 0.5f ? BK.Metal : BK.Concrete; wall = k.Mat == BK.Metal ? TankC[(int)(Rnd(ref s) * 3f) % 3] : 0xcfcac0u; break;
                case BK.StChimney: k.Mat = Rnd(ref s) < 0.4f ? BK.Brick : BK.Concrete; wall = k.Mat == BK.Brick ? 0x9a5a48u : 0xbdb8adu; break;
                case BK.StLighthouse: k.Mat = BK.Lime; wall = 0xeeece6; break;
                default: k.Mat = BK.Concrete; wall = ConcreteC[(int)(Rnd(ref s) * ConcreteC.Length) % ConcreteC.Length]; wall = Pack(((wall >> 16) & 255) / 255f * 1.08f, ((wall >> 8) & 255) / 255f * 1.08f, (wall & 255) / 255f * 1.08f); break;
            }
            switch (fMat)
            {
                case "metal": case "steel": k.Mat = BK.Metal; break;
                case "concrete": case "cement_block": k.Mat = BK.Concrete; break;
                case "brick": k.Mat = BK.Brick; if (fCol == null) wall = 0x9a5a48; break;
                case "stone": k.Mat = BK.Stone; break;
                case "glass": k.Mat = BK.Glass; if (fCol == null) wall = 0x8fa3b0; break;
            }
            k.Wall = Tone(wall, 0.03f, ref s);
            uint c;
            if (Hex(fCol, out c))
            {
                float r = ((c >> 16) & 255) / 255f, g = ((c >> 8) & 255) / 255f, b = (c & 255) / 255f;
                k.Wall = Pack(0.08f + r * 0.88f, 0.08f + g * 0.88f, 0.08f + b * 0.88f);
                k.Flags = 1;        // painted in a colour of its own
            }
            k.Trim = k.Wall; k.Accent = 0x4a4d50;
            k.RoofMat = k.Mat == BK.Metal ? BK.RSeam : BK.RConcrete;
            k.RoofCol = k.Wall;
            if (Hex(rCol, out c))
            {
                float r = ((c >> 16) & 255) / 255f, g = ((c >> 8) & 255) / 255f, b = (c & 255) / 255f;
                k.RoofCol = Pack(0.08f + r * 0.88f, 0.08f + g * 0.88f, 0.08f + b * 0.88f);
            }
            return k;
        }

        public static Look Pick(double lat, double lon, uint hash, string cls, string sub, double height, double floors, double area,
                                string fCol, string fMat, string rShape, string rMat, string rCol)
        {
            uint s = hash * 2654435761u + 0x9e3779b9u;
            if (s == 0) s = 1;
            Rnd(ref s); Rnd(ref s);
            Region reg = Find(lat, lon, ref s);
            bool known = !double.IsNaN(height) || !double.IsNaN(floors);
            double h = !double.IsNaN(height) ? height : !double.IsNaN(floors) ? floors * 3.2 : 0;

            int cat;
            Arch a = null;
            if (IsShed(cls, sub) || (area < 22 && (!known || h < 5))) { cat = BK.CatShed; a = archs["SHED"]; }
            else if (cls == "warehouse" || cls == "industrial" || cls == "hangar" || cls == "factory" || cls == "manufacture" || sub == "industrial")
            {
                cat = BK.CatIndustrial;
                a = archs[Rnd(ref s) < (reg.Name == "uk" || reg.Name == "london" || reg.Name == "us-ne" || reg.Name == "nyc-outer" || reg.Name == "chicago" ? 0.45f : 0.12f) ? "INDBRICK" : "INDMETAL"];
            }
            else if ((cls == "retail" || cls == "supermarket" || cls == "commercial" || cls == "parking") && area > 1800 && (!known || h < 14)) { cat = BK.CatIndustrial; a = archs["BIGBOX"]; }
            else if (cls == "church" || cls == "cathedral" || cls == "chapel" || cls == "monastery")
            {
                cat = BK.CatMid;
                string n = reg.Name;
                bool whiteLands = n == "mexico" || n == "mexico-old" || n == "andes" || n == "colombia" || n == "brazil" || n == "caribbean" || n == "havana" || n == "andalusia" || n == "greece" || n == "cyclades" || n == "portugal" || n == "lisbon" || n == "southcone";
                bool brickLands = n == "uk" || n == "london" || n == "benelux" || n == "amsterdam" || n == "us-ne" || n == "us-mid" || n == "us-south" || n == "nyc" || n == "nyc-outer" || n == "chicago" || n == "boston" || n == "denmark" || n == "nordic";
                a = archs[whiteLands ? "CHURCHW" : brickLands && Rnd(ref s) < 0.5f ? "CHURCHBR" : "CHURCH"];
            }
            else if (cls == "mosque") { cat = BK.CatMid; a = archs["MOSQUE"]; }
            else if (cls == "temple" || cls == "shrine" || cls == "pagoda")
            {
                cat = BK.CatMid;
                string n = reg.Name;
                a = archs[n == "india" || n == "india-s" || n == "jaipur" || n == "jodhpur" || n == "sea" || n == "indonesia" ? "TEMPLEIN" : "TEMPLE"];
            }
            else if (known ? (h > 50 || (!double.IsNaN(floors) && floors >= 15)) : false) cat = BK.CatTower;
            else
            {
                bool houseClass = cls == "house" || cls == "detached" || cls == "semidetached_house" || cls == "bungalow" || cls == "terrace" || cls == "farm" || cls == "residential" && area < 260;
                bool midClass = cls == "apartments" || cls == "dormitory" || cls == "hotel" || cls == "office" || cls == "commercial" || cls == "retail" || cls == "school" || cls == "university" || cls == "hospital" || cls == "civic" || cls == "public" || cls == "government";
                if (known) cat = h <= 10.5 && (area < 420 || houseClass) ? BK.CatHouse : BK.CatMid;
                else cat = houseClass || (!midClass && area < 420) ? BK.CatHouse : BK.CatMid;
            }
            if (a == null)
            {
                float r = Rnd(ref s);
                a = cat == BK.CatTower ? Draw(reg.Tower, reg.TowerW, r) : cat == BK.CatMid ? Draw(reg.Mid, reg.MidW, r) : Draw(reg.House, reg.HouseW, r);
            }

            var k = new Look { Name = a.Name, Place = reg.Name, Cat = cat, Mat = a.Mat, Win = a.Win, FloorReal = a.FloorReal, BayReal = a.BayReal * (0.88f + 0.3f * Rnd(ref s)), RoofMat = a.RoofMat, Pitch = a.Pitch * (0.82f + 0.36f * Rnd(ref s)) };
            k.Wall = Tone(a.Walls[(int)(Rnd(ref s) * a.Walls.Length) % a.Walls.Length], a.Jitter, ref s);
            k.Trim = Tone(a.Trim[(int)(Rnd(ref s) * a.Trim.Length) % a.Trim.Length], 0.03f, ref s);
            k.Accent = Tone(a.Accent[(int)(Rnd(ref s) * a.Accent.Length) % a.Accent.Length], 0.05f, ref s);
            k.RoofCol = Tone(a.RoofCols[(int)(Rnd(ref s) * a.RoofCols.Length) % a.RoofCols.Length], 0.05f, ref s);
            k.WwI = Math.Min(7, Math.Max(0, (int)Math.Round((a.Ww0 + (a.Ww1 - a.Ww0) * Rnd(ref s) - 0.25f) / 0.09f)));
            k.WhI = Math.Min(7, Math.Max(0, (int)Math.Round((a.Wh0 + (a.Wh1 - a.Wh0) * Rnd(ref s) - 0.3f) / 0.085f)));
            k.Shut = Rnd(ref s) < a.ShutChance ? a.Shut : BK.SNone;
            k.Bal = Rnd(ref s) < a.BalChance ? a.Bal : BK.BNone;
            k.BalMode = a.BalMode;
            k.Ground = Weighted(a.Ground, Rnd(ref s));
            k.Cornice = a.Cor0 + (int)(Rnd(ref s) * (a.Cor1 - a.Cor0 + 0.999f));
            k.Flags = a.On;
            for (int bit = 2; bit < 15; bit++) if ((a.Maybe & (1 << bit)) != 0 && Rnd(ref s) < 0.5f) k.Flags |= 1 << bit;
            k.Glass = (int)a.Glass[(int)(Rnd(ref s) * a.Glass.Length) % a.Glass.Length];
            k.RoofShape = Weighted(a.Shapes, Rnd(ref s));
            k.Clutter = a.Clutter;
            k.DefFloors = a.F0 + (int)(Rnd(ref s) * (a.F1 - a.F0 + 0.999f));
            if (cat == BK.CatShed) { k.Flags &= ~BK.FString; k.Cornice = 0; }

            // ---- what the map itself says wins
            uint c;
            if (fMat != null)
            {
                switch (fMat)
                {
                    case "brick": if (k.Mat != BK.Brick) { k.Mat = BK.Brick; if (fCol == null) k.Wall = Tone(BrickRed[(int)(Rnd(ref s) * BrickRed.Length) % BrickRed.Length], 0.05f, ref s); } break;
                    case "glass": k.Mat = BK.Glass; k.Win = BK.WCurtain; k.Bal = BK.BNone; k.Shut = BK.SNone; k.Flags &= ~(BK.FFire | BK.FAc | BK.FSill | BK.FLintel | BK.FSurround | BK.FAwning | BK.FGrille); k.Cornice = 0;
                        if (a.Mat != BK.Glass) { k.Wall = Tone(0x8a9298, 0.08f, ref s); k.Glass = (int)(Rnd(ref s) * 4.99f); k.WwI = 7; k.WhI = 6; k.BayReal = 1.6f; }
                        break;
                    case "concrete": case "cement_block": if (k.Mat != BK.Concrete && k.Mat != BK.Tile) { k.Mat = BK.Concrete; if (fCol == null) k.Wall = Tone(ConcreteC[(int)(Rnd(ref s) * ConcreteC.Length) % ConcreteC.Length], 0.04f, ref s); } break;
                    case "stone": k.Mat = BK.Stone; break;
                    case "plaster": if (k.Mat != BK.Lime) k.Mat = BK.Stucco; break;
                    case "metal": k.Mat = BK.Metal; break;
                    case "wood": if (k.Mat != BK.Boards) k.Mat = BK.Siding; break;
                    case "timber_framing": k.Mat = BK.Timber; break;
                    case "clay": k.Mat = BK.Adobe; break;
                }
            }
            if (Hex(fCol, out c))
            {
                // keep away from pure black and pure white, which no wall really is
                float r = ((c >> 16) & 255) / 255f, g = ((c >> 8) & 255) / 255f, b = (c & 255) / 255f;
                k.Wall = Tone(Pack(0.1f + r * 0.84f, 0.1f + g * 0.84f, 0.1f + b * 0.84f), 0.025f, ref s);
            }
            if (rShape != null)
            {
                switch (rShape)
                {
                    case "flat": case "skillion": k.RoofShape = BK.ShFlat; break;
                    case "gabled": case "gambrel": case "saltbox": case "round": k.RoofShape = BK.ShGable; break;
                    case "hipped": case "half-hipped": case "half_hipped": k.RoofShape = BK.ShHip; break;
                    case "mansard": k.RoofShape = BK.ShMansard; break;
                    case "pyramidal": case "dome": case "onion": case "cone": case "spherical": k.RoofShape = BK.ShPyramid; break;
                }
                if (k.RoofShape != BK.ShFlat && k.RoofMat != BK.RClay && k.RoofMat != BK.RSlate && k.RoofMat != BK.RSeam && k.RoofMat != BK.RShingle && k.RoofMat != BK.RKawara && k.RoofMat != BK.RCorrugated && k.RoofMat != BK.RThatch)
                { k.RoofMat = BK.RSeam; k.RoofCol = Tone(Zinc[(int)(Rnd(ref s) * Zinc.Length) % Zinc.Length], 0.04f, ref s); }
                if (k.Pitch < 0.3f) k.Pitch = 0.6f;
                if (rShape == "dome" || rShape == "onion") k.Pitch = Math.Max(k.Pitch, 1.3f);
            }
            if (rMat != null)
            {
                switch (rMat)
                {
                    case "roof_tiles": if (k.RoofMat != BK.RKawara && k.RoofMat != BK.RClay) { k.RoofMat = BK.RClay; if (rCol == null) k.RoofCol = Tone(Terracotta[(int)(Rnd(ref s) * Terracotta.Length) % Terracotta.Length], 0.05f, ref s); } break;
                    case "slate": k.RoofMat = BK.RSlate; if (rCol == null) k.RoofCol = Tone(SlateC[(int)(Rnd(ref s) * SlateC.Length) % SlateC.Length], 0.04f, ref s); break;
                    case "metal": k.RoofMat = BK.RSeam; if (rCol == null && a.RoofMat != BK.RSeam) k.RoofCol = Tone(Zinc[(int)(Rnd(ref s) * Zinc.Length) % Zinc.Length], 0.04f, ref s); break;
                    case "copper": k.RoofMat = BK.RSeam; if (rCol == null) k.RoofCol = Tone(CopperGreen[(int)(Rnd(ref s) * CopperGreen.Length) % CopperGreen.Length], 0.04f, ref s); break;
                    case "glass": k.RoofMat = BK.RSeam; if (rCol == null) k.RoofCol = Tone(0x8fa8b8, 0.04f, ref s); break;
                    case "gravel": k.RoofMat = BK.RGravel; break;
                    case "thatch": k.RoofMat = BK.RThatch; if (rCol == null) k.RoofCol = Tone(0x8a7a58, 0.05f, ref s); break;
                    case "grass": k.RoofMat = BK.RGravel; if (rCol == null) k.RoofCol = Tone(0x6f8f55, 0.05f, ref s); break;
                    case "wood": k.RoofMat = BK.RShingle; break;
                    case "concrete": case "tar_paper": if (k.RoofShape == BK.ShFlat && k.RoofMat != BK.RGravel) k.RoofMat = BK.RConcrete; break;
                }
            }
            if (Hex(rCol, out c))
            {
                float r = ((c >> 16) & 255) / 255f, g = ((c >> 8) & 255) / 255f, b = (c & 255) / 255f;
                k.RoofCol = Tone(Pack(0.08f + r * 0.8f, 0.08f + g * 0.8f, 0.08f + b * 0.8f), 0.025f, ref s);
            }
            return k;
        }
    }
}
