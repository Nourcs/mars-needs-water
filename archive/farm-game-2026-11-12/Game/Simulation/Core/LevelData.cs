using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// Authored level facts the simulation needs (ids below 1,000,000, spots, zones). Built by the level
    /// in presentation from scene markers, or by tests in code.
    /// </summary>
    public sealed class LevelData
    {
        /// <summary>Water sources in the level.</summary>
        public List<SourceDef> Sources = new List<SourceDef>();

        /// <summary>The starter pad tank (10 L jerrycan) authored on the storage spot.</summary>
        public int StarterTankId = 100;
        public string StarterTankItem = "jerrycan_10l";

        /// <summary>The storage spot: the pad tank always sits here.</summary>
        public Pose StorageSpot;
        /// <summary>The staging spot next to it, where a bigger container is set down before "Swap in".</summary>
        public Pose StagingSpot;
        public double StagingRadius = 0.9;

        /// <summary>Numbered pad crate spots (0..3), each with an item pose next to it.</summary>
        public List<CrateSpot> CrateSpots = new List<CrateSpot>();
        /// <summary>Where the IBC tank appears when its crate is opened.</summary>
        public Pose IbcSpot;

        /// <summary>Power post sockets, by index.</summary>
        public List<Pose> PostSockets = new List<Pose>();

        public Pose LaunchLever;
        public Pose OrderLever;

        /// <summary>Ground where items may be placed or dropped.</summary>
        public List<ZoneRect> WorkAreas = new List<ZoneRect>();
        /// <summary>Areas where nothing may be placed (water, the pad itself).</summary>
        public List<ZoneRect> NoPlaceAreas = new List<ZoneRect>();

        /// <summary>The player's start pose.</summary>
        public Pose PlayerStart;
    }

    /// <summary>One authored water source and its drawdown profile.</summary>
    public sealed class SourceDef
    {
        public int Id;
        /// <summary>Where scoops are accepted (the water's surface footprint).</summary>
        public ZoneRect ScoopArea;
        /// <summary>The hand-placed intake zone near the shore (centre and radius on the ground plane).</summary>
        public Pose IntakeCentre;
        public double IntakeRadius = 1.5;
        /// <summary>Drawdown thresholds in percent, ascending (Drawdown Rule 3). Empty for the puddle.</summary>
        public List<double> Thresholds = new List<double>();
        /// <summary>Reveals (id, drawdown percent at which the object breaks the surface).</summary>
        public List<RevealDef> Reveals = new List<RevealDef>();
        /// <summary>Display name (world water bodies: "Pacific Ocean", "Lake Baikal").</summary>
        public string Name = "";
        /// <summary>
        /// Start volume for a source the level defines itself (world water bodies); 0 = take it from the tuning
        /// table's sources, as the farm's puddle and pond do.
        /// </summary>
        public double StartVolume_L;
        /// <summary>
        /// A world water body (sea, lake) the player can scoop from anywhere along its surface. The planet is not
        /// in the farm's flat frame, so the presentation layer checks reach for these; the sim skips the scoop
        /// area and range checks. Pumps cannot take from them (no intake zone).
        /// </summary>
        public bool Anywhere;
    }

    public struct RevealDef
    {
        public int Id;
        public double Pct;
        public RevealDef(int id, double pct) { Id = id; Pct = pct; }
    }

    public struct CrateSpot
    {
        public Pose Spot;
        public Pose ItemPose;
        public CrateSpot(Pose spot, Pose item) { Spot = spot; ItemPose = item; }
    }
}
