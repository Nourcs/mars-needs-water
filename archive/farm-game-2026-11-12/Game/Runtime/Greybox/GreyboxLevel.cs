using System.Collections.Generic;
using MarsNeedsWater.Simulation;
using Pose = MarsNeedsWater.Simulation.Pose;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Builds the MVP greybox at runtime (art bible deferred) and the matching <see cref="LevelData"/>.
    /// Layout: launch-pad apron with the starter puddle beside the storage spot, crate spots, ORDER terminal,
    /// power post, and a 5 × 4 × 1 m box-pit farm pond (exactly 20,000 L, so height is linear in volume).
    /// </summary>
    public sealed class GreyboxLevel
    {
        public const int PuddleId = 1, PondId = 2;
        public const int DockRevealId = 201, CartRevealId = 202;

        // Pond pit (world XZ rect), surface at y = 0, bed at y = -PondDepth.
        public static readonly Rect PondRect = new Rect(-14f, -12f, 5f, 4f);
        public const float PondDepth = 1f;
        // Puddle: 1.4 × 1.0 m, 40 L -> 2.86 cm deep, sunk into the apron.
        public static readonly Rect PuddleRect = new Rect(-0.7f, -0.5f, 1.4f, 1.0f);

        public LevelData Data { get; } = new LevelData();
        public Transform Root { get; private set; }
        public Transform PondWater { get; private set; }
        public Transform PuddleWater { get; private set; }
        public Transform Rocket { get; private set; }
        public GameObject RocketFlame { get; private set; }
        public Vector3 RocketBase { get; private set; }
        public Transform DockReveal { get; private set; }
        public Transform CartReveal { get; private set; }
        public Transform BandRoot { get; private set; }
        public readonly List<Transform> PostSocketViews = new List<Transform>();

        /// <summary>Exposed-bank overlays on the four pit walls (N, S, E, W); WorldView sizes them to the water line.</summary>
        public readonly Transform[] DryWalls = new Transform[4];
        /// <summary>A dark wet strip that rides just above the pond surface.</summary>
        public readonly Transform[] WetEdges = new Transform[4];

        public void Build()
        {
            Root = new GameObject("Greybox").transform;
            var C = Palette.Ground;

            // ---- Ground: four slabs leave a hole for the pond pit; the puddle is a shallow dip on the apron.
            var pr = PondRect;
            float W = Planet.FarmHalf; // the streamed terrain starts exactly where these slabs end
            Ground("Ground_N", new Vector3(0, -0.5f, (pr.yMax + W) / 2f), new Vector3(2 * W, 1, W - pr.yMax), true);
            Ground("Ground_S", new Vector3(0, -0.5f, (pr.yMin - W) / 2f), new Vector3(2 * W, 1, W + pr.yMin), true);
            Ground("Ground_E", new Vector3((pr.xMax + W) / 2f, -0.5f, pr.center.y), new Vector3(W - pr.xMax, 1, pr.height), true);
            Ground("Ground_W", new Vector3((pr.xMin - W) / 2f, -0.5f, pr.center.y), new Vector3(W + pr.xMin, 1, pr.height), true);

            // ---- Pond pit: bed, and a ramp on the east side so the player can walk down and back out.
            var bed = Prims.Box("PondBed", new Vector3(pr.center.x, -PondDepth - 0.25f, pr.center.y), new Vector3(pr.width, 0.5f, pr.height), Palette.Mud, Root);
            Prims.SetFinish(bed, Palette.Mud, 0.45f);
            var ramp = Prims.Box("PondRamp", new Vector3(pr.xMax - 1.1f, -PondDepth / 2f, pr.yMin + 0.6f), new Vector3(2.4f, 0.12f, 1.0f), Palette.Wood, Root);
            ramp.transform.rotation = Quaternion.Euler(0, 0, 25f);
            for (int i = 0; i < 5; i++) // duckboard slats on the ramp
                Prims.Deco(PrimitiveType.Cube, $"RampSlat_{i}", new Vector3(-0.4f + i * 0.2f, 0.6f, 0), new Vector3(0.03f, 0.4f, 1.0f), Palette.Bark, ramp.transform);
            // Pit walls (visible faces; the ground slabs are the colliders).
            var wallPos = new[]
            {
                new Vector3(pr.center.x, 0, pr.yMax - 0.01f), new Vector3(pr.center.x, 0, pr.yMin + 0.01f),
                new Vector3(pr.xMax - 0.01f, 0, pr.center.y), new Vector3(pr.xMin + 0.01f, 0, pr.center.y),
            };
            var wallSize = new[] { new Vector3(pr.width, 1, 0.02f), new Vector3(pr.width, 1, 0.02f), new Vector3(0.02f, 1, pr.height), new Vector3(0.02f, 1, pr.height) };
            var inward = new[] { new Vector3(0, 0, -1), new Vector3(0, 0, 1), new Vector3(-1, 0, 0), new Vector3(1, 0, 0) };
            string[] side = { "N", "S", "E", "W" };
            for (int i = 0; i < 4; i++)
            {
                var ws = wallSize[i];
                Prims.Box($"PondWall_{side[i]}", wallPos[i] + new Vector3(0, -PondDepth / 2f, 0), new Vector3(ws.x, PondDepth, ws.z), Palette.Mud, Root, false);
                // The bathtub ring: dry bank from the water line up to the rim, and a wet strip at the water line.
                DryWalls[i] = Prims.Deco(PrimitiveType.Cube, $"DryBank_{side[i]}", wallPos[i] + inward[i] * 0.015f, new Vector3(ws.x, 0.001f, ws.z), Palette.DryMud, Root, 0.05f).transform;
                WetEdges[i] = Prims.Deco(PrimitiveType.Cube, $"WetEdge_{side[i]}", wallPos[i] + inward[i] * 0.02f, new Vector3(ws.x, 0.05f, ws.z), Palette.WetEdge, Root, 0.6f).transform;
            }
            BandRoot = new GameObject("StainBands").transform;
            BandRoot.SetParent(Root, false);

            // Muddy lip around the pond, reeds and cattails on the north and west banks, a few stones.
            Prims.Deco(PrimitiveType.Cube, "PondLip_N", new Vector3(pr.center.x, 0.004f, pr.yMax + 0.35f), new Vector3(pr.width + 1.4f, 0.008f, 0.7f), Palette.Mud, Root, 0.3f);
            Prims.Deco(PrimitiveType.Cube, "PondLip_S", new Vector3(pr.center.x, 0.004f, pr.yMin - 0.35f), new Vector3(pr.width + 1.4f, 0.008f, 0.7f), Palette.Mud, Root, 0.3f);
            Prims.Deco(PrimitiveType.Cube, "PondLip_E", new Vector3(pr.xMax + 0.35f, 0.004f, pr.center.y), new Vector3(0.7f, 0.008f, pr.height), Palette.Mud, Root, 0.3f);
            Prims.Deco(PrimitiveType.Cube, "PondLip_W", new Vector3(pr.xMin - 0.35f, 0.004f, pr.center.y), new Vector3(0.7f, 0.008f, pr.height), Palette.Mud, Root, 0.3f);
            var rng = new System.Random(20261106);
            for (int i = 0; i < 70; i++)
            {
                bool north = i % 2 == 0;
                var at = north
                    ? new Vector3(Rand(rng, pr.xMin - 0.6f, pr.xMax - 2.6f), 0, pr.yMax + Rand(rng, 0.1f, 0.75f))
                    : new Vector3(pr.xMin - Rand(rng, 0.1f, 0.75f), 0, Rand(rng, pr.yMin, pr.yMax + 0.6f));
                if (north && Mathf.Abs(at.x - (pr.xMin + 1.2f)) < 0.8f) continue; // keep the dock clear
                float h = Rand(rng, 0.5f, 1.15f);
                var reed = Prims.Deco(PrimitiveType.Cylinder, "Reed", at + new Vector3(0, h / 2f, 0), new Vector3(0.03f, h / 2f, 0.03f), Palette.Reed, Root);
                reed.transform.rotation = Quaternion.Euler(Rand(rng, -8f, 8f), 0, Rand(rng, -8f, 8f));
                if (rng.NextDouble() < 0.35)
                    Prims.Deco(PrimitiveType.Cylinder, "Cattail", new Vector3(0, 0.85f, 0), new Vector3(1.9f, 0.09f, 1.9f), Palette.Cattail, reed.transform);
            }
            for (int i = 0; i < 9; i++)
            {
                var at = new Vector3(Rand(rng, pr.xMin - 1.2f, pr.xMax + 1.2f), 0.04f, i % 2 == 0 ? pr.yMin - Rand(rng, 0.4f, 1.3f) : pr.yMax + Rand(rng, 0.8f, 1.4f));
                float d = Rand(rng, 0.15f, 0.4f);
                Prims.Deco(PrimitiveType.Sphere, "Stone", at, new Vector3(d, d * 0.55f, d * 0.8f), Palette.Rock, Root, 0.15f);
            }
            Sign("FARM POND\n20,000 L\nProperty of Crystal Lake Holdings", new Vector3(pr.xMax + 1.4f, 0, pr.yMin + 0.4f), -90f, 1.3f, 0.6f, Palette.White, Palette.CorporateDark, false);

            // Reveals: the dock with a "Swim Safe" sign (top at 10% = -0.10 m) and a shopping cart (top at 15%).
            var dock = new GameObject("Reveal_Dock").transform;
            dock.SetParent(Root, false);
            dock.position = new Vector3(pr.xMin + 1.2f, 0, pr.yMax - 0.8f);
            Prims.Box("DockDeck", new Vector3(0, -0.12f, 0), new Vector3(1.2f, 0.04f, 1.6f), Palette.Wood, dock, false);
            for (int i = 0; i < 4; i++)
                Prims.Deco(PrimitiveType.Cylinder, $"DockPile_{i}", new Vector3(i % 2 == 0 ? -0.5f : 0.5f, -0.6f, i < 2 ? -0.7f : 0.7f), new Vector3(0.1f, 0.48f, 0.1f), Palette.Bark, dock);
            Prims.Box("SignPost", new Vector3(0.4f, -0.55f, 0.5f), new Vector3(0.06f, 0.9f, 0.06f), Palette.Grey, dock, false);
            Prims.Box("SignBoard_SwimSafe", new Vector3(0.4f, -0.2f, 0.5f), new Vector3(0.6f, 0.2f, 0.03f), Palette.Marker, dock, false);
            Prims.Label("SWIM SAFE", new Vector3(0.4f, -0.2f, 0.48f), 0f, 0.07f, Palette.Dark, dock, 0.5f);
            DockReveal = dock;
            CartReveal = BuildCart(new Vector3(pr.center.x + 0.8f, -PondDepth, pr.center.y - 0.5f));

            // Water planes: trigger colliders so the interaction ray can hit them but nobody stands on them.
            PondWater = MakeWater("Water_Pond", new Vector3(pr.center.x, 0, pr.center.y), new Vector2(pr.width, pr.height), PondId);
            Prims.Deco(PrimitiveType.Cube, "PuddleMud", new Vector3(PuddleRect.center.x, 0.003f, PuddleRect.center.y), new Vector3(PuddleRect.width + 0.3f, 0.006f, PuddleRect.height + 0.3f), Palette.Mud, Root, 0.5f);
            PuddleWater = MakeWater("Water_Puddle", new Vector3(PuddleRect.center.x, 0.02f, PuddleRect.center.y), new Vector2(PuddleRect.width, PuddleRect.height), PuddleId);

            // ---- Launch pad apron: concrete slab with expansion joints.
            var apron = Prims.Box("Apron", new Vector3(2.5f, 0.005f, 0.5f), new Vector3(9f, 0.01f, 9f), Palette.Concrete, Root, false);
            apron.GetComponent<Renderer>().sharedMaterial = Prims.Noisy(Palette.Concrete, new Vector3(9f, 1f, 9f), 3f);
            for (int i = 1; i < 3; i++)
            {
                Prims.Deco(PrimitiveType.Cube, $"Joint_X{i}", new Vector3(-2f + i * 3f, 0.011f, 0.5f), new Vector3(0.03f, 0.002f, 9f), Palette.Joint, Root);
                Prims.Deco(PrimitiveType.Cube, $"Joint_Z{i}", new Vector3(2.5f, 0.011f, -4f + i * 3f), new Vector3(9f, 0.002f, 0.03f), Palette.Joint, Root);
            }
            var storage = new Vector3(1.6f, 0, 0.2f);
            var staging = new Vector3(3.0f, 0, 0.2f);
            Marker("StorageSpot", storage, 0.7f, Palette.Orange);
            var stagingMark = Marker("StagingSpot", staging, 0.9f, Palette.Marker);
            SimTarget.Add(stagingMark, TargetKind.StagingSpot);
            FloorText("TANK", storage + new Vector3(0, 0, -0.55f), 0.1f);
            FloorText("LOAD", staging + new Vector3(0, 0, -0.65f), 0.1f);

            // Rocket pad, launch tower and LAUNCH lever.
            var pad = Prims.Box("RocketPad", new Vector3(7.5f, 0.1f, 2.5f), new Vector3(4f, 0.2f, 4f), Palette.Grey, Root);
            pad.GetComponent<Renderer>().sharedMaterial = Prims.Noisy(Palette.Grey, new Vector3(4f, 1f, 4f), 2f);
            for (int i = 0; i < 8; i++) // hazard stripes along the pad edge facing the apron
                Prims.Deco(PrimitiveType.Cube, $"PadStripe_{i}", new Vector3(5.49f, 0.12f, 0.75f + i * 0.5f), new Vector3(0.02f, 0.16f, 0.25f), i % 2 == 0 ? Palette.Marker : Palette.Dark, Root);
            RocketBase = new Vector3(7.5f, 2.2f, 2.5f);
            Rocket = BuildRocket(RocketBase);
            BuildTower(new Vector3(9.05f, 0.2f, 2.5f));
            var launchLever = new Vector3(2.4f, 1.0f, -1.6f);
            Prims.Box("LaunchConsole", new Vector3(2.4f, 0.45f, -1.75f), new Vector3(0.6f, 0.9f, 0.4f), Palette.Grey, Root);
            var panel = Prims.Deco(PrimitiveType.Cube, "LaunchPanel", new Vector3(2.4f, 0.93f, -1.8f), new Vector3(0.62f, 0.06f, 0.36f), Palette.Dark, Root, 0.5f);
            panel.transform.rotation = Quaternion.Euler(-20f, 0, 0);
            Prims.Deco(PrimitiveType.Sphere, "LaunchLamp", new Vector3(2.15f, 0.98f, -1.72f), new Vector3(0.07f, 0.04f, 0.07f), Palette.Orange, Root);
            for (int i = 0; i < 4; i++)
                Prims.Deco(PrimitiveType.Cube, $"ConsoleStripe_{i}", new Vector3(2.17f + i * 0.155f, 0.12f, -1.548f), new Vector3(0.075f, 0.2f, 0.005f), i % 2 == 0 ? Palette.Marker : Palette.Dark, Root);
            var ll = Prims.Box("LAUNCH_Lever", launchLever, new Vector3(0.12f, 0.35f, 0.12f), Palette.Orange, Root);
            SimTarget.Add(ll, TargetKind.LaunchLever);
            Prims.Deco(PrimitiveType.Sphere, "LeverKnob", new Vector3(0, 0.55f, 0), new Vector3(1.5f, 0.45f, 1.5f), Palette.Dark, ll.transform, 0.6f);
            Prims.Label("LAUNCH", new Vector3(2.4f, 0.62f, -1.545f), 180f, 0.09f, Palette.White, Root, 0.5f);

            var orderLever = new Vector3(-2.5f, 1.1f, -2.3f);
            // ORDER terminal: a corporate kiosk with a canopy, a glowing screen and the company sign.
            Prims.Box("Kiosk", new Vector3(-2.5f, 0.8f, -2.7f), new Vector3(1.2f, 1.6f, 0.6f), Palette.Corporate, Root);
            Prims.Deco(PrimitiveType.Cube, "KioskPlinth", new Vector3(-2.5f, 0.06f, -2.7f), new Vector3(1.3f, 0.12f, 0.7f), Palette.CorporateDark, Root);
            Prims.Deco(PrimitiveType.Cube, "KioskCanopy", new Vector3(-2.5f, 2.15f, -2.55f), new Vector3(1.7f, 0.08f, 1.1f), Palette.White, Root, 0.4f);
            Prims.Deco(PrimitiveType.Cube, "KioskMast", new Vector3(-2.5f, 1.88f, -2.85f), new Vector3(0.1f, 0.5f, 0.1f), Palette.Steel, Root, 0.4f, 0.6f);
            var screen = Prims.Deco(PrimitiveType.Cube, "KioskScreen", new Vector3(-2.5f, 1.38f, -2.395f), new Vector3(0.9f, 0.34f, 0.02f), Palette.Screen, Root);
            screen.GetComponent<Renderer>().sharedMaterial = Prims.Mat(Palette.Screen, 0.9f, 0f, new Color(0.02f, 0.18f, 0.16f));
            Prims.Label("ORDER SUPPLIES", new Vector3(-2.5f, 1.42f, -2.38f), 180f, 0.06f, Palette.ScreenGlow, Root, 0.8f);
            Prims.Label("pull lever", new Vector3(-2.5f, 1.3f, -2.38f), 180f, 0.035f, Palette.ScreenGlow, Root, 0.8f, false);
            Prims.Label("CRYSTAL LAKE HOLDINGS", new Vector3(-2.5f, 2.15f, -1.985f), 180f, 0.055f, Palette.CorporateDark, Root, 1.6f);
            var ol = Prims.Box("ORDER_Lever", orderLever, new Vector3(0.12f, 0.35f, 0.12f), Palette.Orange, Root);
            SimTarget.Add(ol, TargetKind.OrderLever);
            Prims.Deco(PrimitiveType.Sphere, "OrderKnob", new Vector3(0, 0.55f, 0), new Vector3(1.5f, 0.45f, 1.5f), Palette.Dark, ol.transform, 0.6f);

            // Power post: a timber pole with an orange junction box, and a line of poles running off to the haze.
            Prims.Box("PowerPost", new Vector3(4.5f, 0.75f, -3f), new Vector3(0.25f, 1.5f, 0.25f), Palette.Wood, Root);
            Prims.Deco(PrimitiveType.Cylinder, "PowerPole", new Vector3(4.5f, 3f, -3.15f), new Vector3(0.22f, 3f, 0.22f), Palette.Wood, Root);
            Prims.Deco(PrimitiveType.Cube, "PoleCrossbar", new Vector3(4.5f, 5.6f, -3.15f), new Vector3(1.6f, 0.1f, 0.1f), Palette.Wood, Root);
            Prims.Deco(PrimitiveType.Cube, "JunctionBox", new Vector3(4.5f, 1.05f, -2.9f), new Vector3(0.34f, 0.6f, 0.06f), Palette.Orange, Root, 0.35f);
            Prims.Label("POWER", new Vector3(4.5f, 1.42f, -2.86f), 180f, 0.05f, Palette.White, Root, 0.3f);
            var sockets = new[] { new Vector3(4.5f, 1.2f, -2.85f), new Vector3(4.5f, 0.9f, -2.85f) };
            for (int i = 0; i < sockets.Length; i++)
            {
                var s = Prims.Ball($"PostSocket_{i}", sockets[i], 0.14f, Palette.Marker, Root);
                SimTarget.Add(s, TargetKind.PostSocket, 0, "", i);
                PostSocketViews.Add(s.transform);
                Data.PostSockets.Add(sockets[i].ToPose());
            }
            PowerLine(new Vector3(4.5f, 5.6f, -3.15f));

            // Crate spots on the apron edge, each with an item pose next to it.
            for (int i = 0; i < 4; i++)
            {
                var spot = new Vector3(-3f + i * 1.6f, 0, 3.4f);
                Marker($"CrateSpot_{i}", spot, 0.8f, new Color(0.82f, 0.82f, 0.8f));
                Data.CrateSpots.Add(new CrateSpot(spot.ToPose(), (spot + new Vector3(0, 0, -1.1f)).ToPose(180)));
            }
            FloorText("DELIVERIES", new Vector3(-0.6f, 0, 2.75f), 0.12f);
            var ibc = new Vector3(3.4f, 0, -2.0f);
            Marker("IbcSpot", ibc, 1.3f, new Color(0.6f, 0.62f, 0.85f));

            // Company billboard behind the deliveries row, facing the start.
            Billboard(new Vector3(0.5f, 0, 8.5f));
            Farmstead();
            Scatter(rng);

            // ---- Level data for the simulation.
            Data.StarterTankId = 100;
            Data.StarterTankItem = "jerrycan_10l";
            Data.StorageSpot = storage.ToPose();
            Data.StagingSpot = staging.ToPose();
            Data.StagingRadius = 0.6;
            Data.IbcSpot = ibc.ToPose();
            Data.LaunchLever = launchLever.ToPose();
            Data.OrderLever = orderLever.ToPose();
            Data.PlayerStart = new Pose(0.9, 0, -1.4);
            Data.WorkAreas.Add(new ZoneRect(-30, -30, 30, 30));
            Data.NoPlaceAreas.Add(Zone(PuddleRect));
            Data.NoPlaceAreas.Add(Zone(PondRect));
            Data.NoPlaceAreas.Add(new ZoneRect(5.5, 0.5, 9.5, 4.5)); // rocket pad

            Data.Sources.Add(new SourceDef
            {
                Id = PuddleId,
                ScoopArea = Zone(PuddleRect),
                IntakeCentre = new Pose(PuddleRect.center.x, 0, PuddleRect.center.y),
                IntakeRadius = 0.5,
            });
            var pond = new SourceDef
            {
                Id = PondId,
                ScoopArea = Zone(PondRect),
                IntakeCentre = new Pose(pr.xMax - 0.9, 0, pr.yMax - 0.9),
                IntakeRadius = 1.4,
            };
            pond.Thresholds.AddRange(new double[] { 1, 2.5, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 });
            pond.Reveals.Add(new RevealDef(DockRevealId, 10));
            pond.Reveals.Add(new RevealDef(CartRevealId, 15));
            Data.Sources.Add(pond);

            // Intake zone marker (a ring of stakes on the shore).
            var ic = new Vector3((float)pond.IntakeCentre.X, 0, (float)pond.IntakeCentre.Z);
            Prims.Cyl("IntakeStake_A", new Vector3(pr.xMax + 0.2f, 0.3f, ic.z), new Vector3(0.05f, 0.3f, 0.05f), Palette.Marker, Root, false);
            Prims.Cyl("IntakeStake_B", new Vector3(ic.x, 0.3f, pr.yMax + 0.2f), new Vector3(0.05f, 0.3f, 0.05f), Palette.Marker, Root, false);
        }

        static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        void Ground(string name, Vector3 pos, Vector3 size, bool collider)
        {
            var g = Prims.Box(name, pos, size, Palette.Ground, Root, collider);
            g.GetComponent<Renderer>().sharedMaterial = Prims.Noisy(Palette.Ground, size, 6f);
        }

        void FloorText(string text, Vector3 at, float height)
        {
            var g = Prims.Label(text, at + new Vector3(0, 0.02f, 0), 0f, height, Palette.Dark, Root);
            g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>A post-mounted sign facing <paramref name="yaw"/> (0 = readable from -Z).</summary>
        void Sign(string text, Vector3 at, float yaw, float width, float height, Color ink, Color board, bool tall)
        {
            var g = new GameObject("Sign").transform;
            g.SetParent(Root, false);
            g.position = at;
            g.rotation = Quaternion.Euler(0, yaw, 0);
            float top = tall ? 2.2f : 1.1f;
            Prims.Deco(PrimitiveType.Cube, "Post", new Vector3(0, (top - height / 2f) / 2f, 0.03f), new Vector3(0.07f, top - height / 2f, 0.07f), Palette.Steel, g, 0.4f, 0.5f);
            Prims.Box("Board", new Vector3(0, top, 0), new Vector3(width, height, 0.04f), board, g);
            int lines = text.Split('\n').Length;
            Prims.Label(text, new Vector3(0, top, -0.025f), 0f, height / (lines * 1.6f + 0.6f), ink, g, width * 0.9f);
        }

        void Billboard(Vector3 at)
        {
            var g = new GameObject("Billboard").transform;
            g.SetParent(Root, false);
            g.position = at;
            Prims.Box("Leg_L", new Vector3(-2.4f, 1.6f, 0.1f), new Vector3(0.18f, 3.2f, 0.18f), Palette.Steel, g);
            Prims.Box("Leg_R", new Vector3(2.4f, 1.6f, 0.1f), new Vector3(0.18f, 3.2f, 0.18f), Palette.Steel, g);
            Prims.Box("Board", new Vector3(0, 3.6f, 0.15f), new Vector3(6.4f, 2.2f, 0.12f), Palette.Corporate, g);
            Prims.Deco(PrimitiveType.Cube, "Trim", new Vector3(0, 2.62f, 0.07f), new Vector3(6.4f, 0.22f, 0.04f), Palette.Orange, g);
            Prims.Deco(PrimitiveType.Sphere, "Logo", new Vector3(-2.45f, 3.95f, 0.07f), new Vector3(0.9f, 0.9f, 0.05f), Palette.Water, g, 0.6f);
            Prims.Label("CRYSTAL LAKE HOLDINGS", new Vector3(0.45f, 4.1f, 0.07f), 0f, 0.36f, Palette.White, g, 4.7f);
            Prims.Label("Hydrating Mars, one spoonful at a time.", new Vector3(0.45f, 3.45f, 0.07f), 0f, 0.2f, Palette.White, g, 4.7f, false);
            Prims.Label("Earth has plenty.*", new Vector3(0.45f, 3.0f, 0.07f), 0f, 0.13f, new Color(0.85f, 1f, 0.97f), g, 4.7f, false);
        }

        Transform BuildCart(Vector3 floor)
        {
            // A tipped-over shopping cart on the pond bed (top at about 15% drained).
            var cart = new GameObject("Reveal_ShoppingCart").transform;
            cart.SetParent(Root, false);
            cart.position = floor;
            cart.rotation = Quaternion.Euler(0, 25f, 0);
            var red = new Color(0.75f, 0.1f, 0.1f);
            Prims.Box("Basket", new Vector3(0, 0.6f, 0), new Vector3(0.55f, 0.5f, 0.85f), red, cart, false);
            Prims.Deco(PrimitiveType.Cube, "Handle", new Vector3(0, 0.82f, -0.48f), new Vector3(0.6f, 0.05f, 0.05f), Palette.Dark, cart);
            for (int i = 0; i < 4; i++)
            {
                Prims.Deco(PrimitiveType.Cube, $"Leg_{i}", new Vector3(i % 2 == 0 ? -0.22f : 0.22f, 0.19f, i < 2 ? -0.32f : 0.32f), new Vector3(0.03f, 0.34f, 0.03f), Palette.Steel, cart, 0.5f, 0.8f);
                Prims.Deco(PrimitiveType.Cylinder, $"Wheel_{i}", new Vector3(i % 2 == 0 ? -0.22f : 0.22f, 0.04f, i < 2 ? -0.32f : 0.32f), new Vector3(0.08f, 0.015f, 0.08f), Palette.Dark, cart).transform.localRotation = Quaternion.Euler(0, 0, 90f);
            }
            return cart;
        }

        Transform BuildRocket(Vector3 basePos)
        {
            // An empty root at the body centre, so WorldView can move and hide the whole stack.
            var r = new GameObject("Rocket").transform;
            r.SetParent(Root, false);
            r.position = basePos;
            bool own = Own.Make("own-rocket", "RocketModel", r, Vector3.zero, 0.55f) != null;
            if (!own) BuildGreyboxRocket(r);
            Prims.Label("CLH", new Vector3(-0.52f, -0.2f, 0), 90f, 0.32f, Palette.CorporateDark, r);
            Prims.Label("MARS", new Vector3(-0.52f, -0.75f, 0), 90f, 0.16f, Palette.Orange, r);
            var flame = Prims.Deco(PrimitiveType.Sphere, "Flame", new Vector3(0, -2.9f, 0), new Vector3(0.6f, 1.6f, 0.6f), Palette.Orange, r);
            flame.GetComponent<Renderer>().sharedMaterial = Prims.Mat(new Color(1f, 0.7f, 0.3f), 0f, 0f, new Color(6f, 2.6f, 0.6f));
            flame.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flame.SetActive(false);
            RocketFlame = flame;
            return r;
        }

        /// <summary>The old rocket made of primitives, used only if our own model file is missing.</summary>
        static void BuildGreyboxRocket(Transform r)
        {
            Prims.Deco(PrimitiveType.Cylinder, "Body", Vector3.zero, new Vector3(1.0f, 2.0f, 1.0f), Palette.White, r, 0.55f);
            Prims.Deco(PrimitiveType.Cylinder, "Band", new Vector3(0, 1.15f, 0), new Vector3(1.02f, 0.14f, 1.02f), Palette.Corporate, r, 0.5f);
            Prims.Deco(PrimitiveType.Cylinder, "BandLow", new Vector3(0, -1.6f, 0), new Vector3(1.02f, 0.08f, 1.02f), Palette.Dark, r, 0.4f);
            float[] dia = { 0.9f, 0.7f, 0.48f, 0.26f };
            for (int i = 0; i < dia.Length; i++)
                Prims.Deco(PrimitiveType.Cylinder, $"Nose_{i}", new Vector3(0, 2.1f + i * 0.24f, 0), new Vector3(dia[i], 0.12f, dia[i]), Palette.Orange, r, 0.45f);
            Prims.Deco(PrimitiveType.Sphere, "NoseTip", new Vector3(0, 3.0f, 0), new Vector3(0.18f, 0.18f, 0.18f), Palette.Orange, r, 0.45f);
            for (int i = 0; i < 3; i++)
            {
                float yaw = i * 120f + 30f;
                var fin = Prims.Deco(PrimitiveType.Cube, $"Fin_{i}", Vector3.zero, new Vector3(0.05f, 0.9f, 0.5f), Palette.Orange, r, 0.45f).transform;
                fin.localRotation = Quaternion.Euler(0, yaw, 0);
                fin.localPosition = fin.localRotation * new Vector3(0, 0, 0.7f) + new Vector3(0, -1.5f, 0);
            }
            Prims.Deco(PrimitiveType.Sphere, "Porthole", new Vector3(-0.47f, 0.55f, 0), new Vector3(0.1f, 0.28f, 0.28f), Palette.Water, r, 0.9f);
        }

        void BuildTower(Vector3 at)
        {
            var t = new GameObject("LaunchTower").transform;
            t.SetParent(Root, false);
            t.position = at;
            const float H = 5.2f, S = 0.6f;
            Prims.Deco(PrimitiveType.Sphere, "Beacon", new Vector3(0, H + 0.08f, 0), new Vector3(0.16f, 0.16f, 0.16f), Palette.Orange, t).GetComponent<Renderer>().sharedMaterial =
                Prims.Mat(Palette.Orange, 0.5f, 0f, new Color(2.5f, 0.8f, 0.1f));
            if (Own.Make("own-tower", "TowerModel", t, Vector3.zero, 0.4f) != null) return;
            for (int i = 0; i < 4; i++)
                Prims.Deco(PrimitiveType.Cube, $"TowerLeg_{i}", new Vector3(i % 2 == 0 ? -S / 2 : S / 2, H / 2, i < 2 ? -S / 2 : S / 2), new Vector3(0.07f, H, 0.07f), Palette.Orange, t, 0.35f);
            for (float y = 0.8f; y < H; y += 0.8f)
            {
                Prims.Deco(PrimitiveType.Cube, "Rung", new Vector3(0, y, -S / 2), new Vector3(S, 0.05f, 0.05f), Palette.Steel, t, 0.4f, 0.6f);
                Prims.Deco(PrimitiveType.Cube, "Rung", new Vector3(0, y, S / 2), new Vector3(S, 0.05f, 0.05f), Palette.Steel, t, 0.4f, 0.6f);
                Prims.Deco(PrimitiveType.Cube, "Rung", new Vector3(-S / 2, y, 0), new Vector3(0.05f, 0.05f, S), Palette.Steel, t, 0.4f, 0.6f);
                Prims.Deco(PrimitiveType.Cube, "Rung", new Vector3(S / 2, y, 0), new Vector3(0.05f, 0.05f, S), Palette.Steel, t, 0.4f, 0.6f);
            }
            Prims.Deco(PrimitiveType.Cube, "Arm", new Vector3(-0.68f, 3.6f, 0), new Vector3(0.75f, 0.08f, 0.16f), Palette.Steel, t, 0.4f, 0.6f);
        }

        void PowerLine(Vector3 first)
        {
            var prev = first;
            for (int i = 1; i <= 6; i++) // stays on the flat farm ground (the terrain beyond is not flat)
            {
                var top = first + new Vector3(i * 9f, 0, -i * 5f);
                Prims.Deco(PrimitiveType.Cylinder, $"LinePole_{i}", new Vector3(top.x, top.y / 2f, top.z), new Vector3(0.22f, top.y / 2f, 0.22f), Palette.Wood, Root);
                Prims.Deco(PrimitiveType.Cube, $"LineBar_{i}", top, new Vector3(1.6f, 0.1f, 0.1f), Palette.Wood, Root);
                for (int k = -1; k <= 1; k += 2)
                {
                    var g = new GameObject("Wire");
                    g.transform.SetParent(Root, false);
                    var lr = g.AddComponent<LineRenderer>();
                    lr.widthMultiplier = 0.025f;
                    lr.positionCount = 10;
                    lr.sharedMaterial = Prims.Mat(Palette.Dark);
                    var a = prev + new Vector3(0.7f * k, 0.05f, 0);
                    var b = top + new Vector3(0.7f * k, 0.05f, 0);
                    for (int j = 0; j < 10; j++)
                    {
                        float u = j / 9f;
                        lr.SetPosition(j, Vector3.Lerp(a, b, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * 0.5f);
                    }
                }
                prev = top;
            }
        }

        void Farmstead()
        {
            // Red barn and a grain silo across the field, a fence around the work area, hills on the horizon.
            var barn = new GameObject("Barn").transform;
            barn.SetParent(Root, false);
            barn.position = new Vector3(-21f, 0, 13f);
            barn.rotation = Quaternion.Euler(0, 15f, 0);
            Prims.Box("BarnBody", new Vector3(0, 2f, 0), new Vector3(7f, 4f, 9f), Palette.BarnRed, barn);
            // 45-degree gable: a cube turned on its edge, half buried in the body, with two roof planks on top.
            var gable = Prims.Deco(PrimitiveType.Cube, "Gable", new Vector3(0, 4.0f, 0), new Vector3(4.95f, 4.95f, 8.98f), Palette.BarnRed, barn).transform;
            gable.localRotation = Quaternion.Euler(0, 0, 45f);
            var roofL = Prims.Deco(PrimitiveType.Cube, "RoofL", new Vector3(-1.81f, 5.81f, 0), new Vector3(5.4f, 0.15f, 9.4f), Palette.Roof, barn, 0.35f, 0.4f).transform;
            roofL.localRotation = Quaternion.Euler(0, 0, 45f);
            var roofR = Prims.Deco(PrimitiveType.Cube, "RoofR", new Vector3(1.81f, 5.81f, 0), new Vector3(5.4f, 0.15f, 9.4f), Palette.Roof, barn, 0.35f, 0.4f).transform;
            roofR.localRotation = Quaternion.Euler(0, 0, -45f);
            Prims.Deco(PrimitiveType.Cube, "Door", new Vector3(0, 1.5f, -4.51f), new Vector3(2.6f, 3f, 0.04f), Palette.White, barn);
            Prims.Deco(PrimitiveType.Cube, "DoorX1", new Vector3(0, 1.5f, -4.54f), new Vector3(0.16f, 3.6f, 0.03f), Palette.BarnRed, barn).transform.localRotation = Quaternion.Euler(0, 0, 40f);
            Prims.Deco(PrimitiveType.Cube, "DoorX2", new Vector3(0, 1.5f, -4.54f), new Vector3(0.16f, 3.6f, 0.03f), Palette.BarnRed, barn).transform.localRotation = Quaternion.Euler(0, 0, -40f);
            var silo = new Vector3(-14.5f, 0, 17f);
            Prims.Cyl("Silo", silo + new Vector3(0, 4f, 0), new Vector3(3f, 4f, 3f), Palette.Grey, Root).GetComponent<Renderer>().sharedMaterial = Prims.Mat(Palette.Grey, 0.55f, 0.7f);
            Prims.Deco(PrimitiveType.Sphere, "SiloCap", silo + new Vector3(0, 8f, 0), new Vector3(3.05f, 1.6f, 3.05f), Palette.Grey, Root, 0.55f, 0.7f);
            Prims.Label("SOLD", silo + new Vector3(0, 6f, -1.53f), 0f, 0.5f, Palette.Corporate, Root);

            // Fence: posts every 4 m, two rails, around a 56 m square.
            const float E = 28f;
            for (int s = 0; s < 4; s++)
            {
                bool alongX = s < 2;
                float fixedC = s % 2 == 0 ? E : -E;
                for (float u = -E; u <= E + 0.01f; u += 4f)
                {
                    var p = alongX ? new Vector3(u, 0.6f, fixedC) : new Vector3(fixedC, 0.6f, u);
                    Prims.Deco(PrimitiveType.Cube, "FencePost", p, new Vector3(0.12f, 1.2f, 0.12f), Palette.Wood, Root);
                }
                for (int k = 0; k < 2; k++)
                {
                    float y = 0.55f + k * 0.45f;
                    var p = alongX ? new Vector3(0, y, fixedC) : new Vector3(fixedC, y, 0);
                    var size = alongX ? new Vector3(2 * E, 0.09f, 0.05f) : new Vector3(0.05f, 0.09f, 2 * E);
                    // Scenery only: the world beyond the fence is open to explore.
                    Prims.Deco(PrimitiveType.Cube, "FenceRail", p, size, Palette.Wood, Root);
                }
            }

        }

        void Scatter(System.Random rng)
        {
            // Trees outside the fence, bushes and stones inside it (kept off the apron, pad and pond).
            for (int i = 0; i < 46; i++)
            {
                float ang = Rand(rng, 0f, Mathf.PI * 2f), dist = Rand(rng, 33f, 58f);
                var at = new Vector3(Mathf.Cos(ang) * dist, 0, Mathf.Sin(ang) * dist);
                float h = Rand(rng, 3.5f, 7f);
                Prims.Deco(PrimitiveType.Cylinder, "Trunk", at + new Vector3(0, h * 0.3f, 0), new Vector3(0.35f, h * 0.3f, 0.35f), Palette.Bark, Root);
                float c = Rand(rng, 2.6f, 4.4f);
                var tint = Color.Lerp(Palette.Tree, Palette.Grass, Rand(rng, 0f, 0.5f));
                Prims.Deco(PrimitiveType.Sphere, "Canopy", at + new Vector3(0, h * 0.62f + c * 0.25f, 0), new Vector3(c, c * 0.85f, c), tint, Root, 0.05f);
                Prims.Deco(PrimitiveType.Sphere, "Canopy2", at + new Vector3(c * 0.3f, h * 0.62f + c * 0.55f, c * 0.2f), new Vector3(c * 0.7f, c * 0.6f, c * 0.7f), tint, Root, 0.05f);
            }
            var pr = PondRect;
            for (int i = 0; i < 160; i++)
            {
                var at = new Vector3(Rand(rng, -27f, 27f), 0, Rand(rng, -27f, 27f));
                if (at.x > -3.5f && at.x < 10.5f && at.z > -5f && at.z < 9.5f) continue;            // apron, pad, billboard
                if (at.x > pr.xMin - 2f && at.x < pr.xMax + 2.5f && at.z > pr.yMin - 2f && at.z < pr.yMax + 2f) continue; // pond
                if (at.x < -12f && at.z > 7f) continue;                                               // barn yard
                if (rng.NextDouble() < 0.8)
                {
                    float d = Rand(rng, 0.25f, 0.8f);
                    var tint = Color.Lerp(Palette.Bush, Palette.Ground, Rand(rng, 0f, 0.55f));
                    Prims.Deco(PrimitiveType.Sphere, "Tuft", at, new Vector3(d, d * 0.55f, d), tint, Root, 0.05f);
                }
                else
                {
                    float d = Rand(rng, 0.2f, 0.6f);
                    Prims.Deco(PrimitiveType.Sphere, "Stone", at + new Vector3(0, d * 0.15f, 0), new Vector3(d, d * 0.5f, d * 0.8f), Palette.Rock, Root, 0.15f);
                }
            }
        }

        static ZoneRect Zone(Rect r) => new ZoneRect(r.xMin, r.yMin, r.xMax, r.yMax);

        GameObject Marker(string name, Vector3 at, float size, Color c)
        {
            var g = Prims.Box(name, at + new Vector3(0, 0.012f, 0), new Vector3(size, 0.01f, size), c, Root);
            var col = g.GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = true;
            return g;
        }

        Transform MakeWater(string name, Vector3 at, Vector2 size, int sourceId)
        {
            var g = Prims.Box(name, at, new Vector3(size.x, 0.02f, size.y), Palette.Water, Root);
            Prims.SetFinish(g, Palette.Water, 0.92f);
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = g.GetComponent<BoxCollider>();
            col.isTrigger = true;
            SimTarget.Add(g, TargetKind.Water, sourceId);
            return g.transform;
        }

        /// <summary>Pond surface height for a fraction remaining (box pit: linear).</summary>
        public static float PondHeight(double fractionRemaining) => (float)(-PondDepth + PondDepth * fractionRemaining);

        /// <summary>Puddle water depth in metres (it sits on the apron): litres over the puddle footprint.</summary>
        public static float PuddleDepthFor(double litres)
        {
            double area = PuddleRect.width * PuddleRect.height;
            return (float)(litres / 1000.0 / area);
        }
    }
}
