using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    // Runtime state only (Game State Rule 1). Nine slices, one owner each. Every field is readable by
    // anyone and writable only inside this assembly (internal set), i.e. through the owning system.

    /// <summary>The whole ledger for one play session.</summary>
    public sealed class SimState
    {
        public WaterSourceSlice WaterSource { get; internal set; } = new WaterSourceSlice();
        public NetworkSlice Network { get; internal set; } = new NetworkSlice();
        public PumpsSlice Pumps { get; internal set; } = new PumpsSlice();
        public HandToolsSlice HandTools { get; internal set; } = new HandToolsSlice();
        public EconomySlice Economy { get; internal set; } = new EconomySlice();
        public DrawdownSlice Drawdown { get; internal set; } = new DrawdownSlice();
        public ShippingSlice Shipping { get; internal set; } = new ShippingSlice();
        public PlacementSlice Placement { get; internal set; } = new PlacementSlice();
        public SessionSlice Session { get; internal set; } = new SessionSlice();
    }

    // ---------------- Water Source ----------------

    public enum SourceKind { Puddle, Lake }

    public sealed class SourceState
    {
        public int Id { get; internal set; }
        public SourceKind Kind { get; internal set; }
        public double Start { get; internal set; }
        public double Remaining { get; internal set; }
        public bool Drained { get; internal set; }
        /// <summary>
        /// Litres taken so far. Kept apart from Remaining because a double cannot resolve a 10 L scoop out of an
        /// ocean (7.1e20 L): Remaining would not change, Extracted does.
        /// </summary>
        public double Extracted { get; internal set; }
    }

    public sealed class WaterSourceSlice
    {
        /// <summary>Sources in id order.</summary>
        public List<SourceState> Sources { get; } = new List<SourceState>();
    }

    // ---------------- Network ----------------

    public enum HoseKind { Suction, Discharge, Link }

    public enum EndTargetKind { None, Socket, Intake }

    /// <summary>Where a hose end is plugged: a node socket (kind + index) or a source's intake zone.</summary>
    public struct EndTarget
    {
        public EndTargetKind Kind;
        public int NodeId;
        /// <summary>intake, outlet, inlet or link.</summary>
        public string Socket;
        public int Index;
        public int SourceId;
        /// <summary>For an intake end: the point aimed at in the zone.</summary>
        public Pose AimPoint;

        public static EndTarget None => default;
        public static EndTarget At(int nodeId, string socket, int index) => new EndTarget { Kind = EndTargetKind.Socket, NodeId = nodeId, Socket = socket, Index = index };
        public static EndTarget IntakeZone(int sourceId, Pose aim) => new EndTarget { Kind = EndTargetKind.Intake, SourceId = sourceId, AimPoint = aim };
        public bool IsSet => Kind != EndTargetKind.None;
        public bool SameSocket(EndTarget o) => Kind == EndTargetKind.Socket && o.Kind == EndTargetKind.Socket && NodeId == o.NodeId && Socket == o.Socket && Index == o.Index;
        public override string ToString() => Kind == EndTargetKind.Socket ? $"{NodeId}.{Socket}[{Index}]" : Kind == EndTargetKind.Intake ? $"intake({SourceId})" : "none";
    }

    public sealed class HoseEnd
    {
        public EndTarget Target { get; internal set; }
        public Pose RestPose { get; internal set; }
    }

    public sealed class HoseState
    {
        public int Id { get; internal set; }
        public HoseKind Kind { get; internal set; }
        public double LengthM { get; internal set; }
        public HoseEnd[] Ends { get; } = { new HoseEnd(), new HoseEnd() };
        /// <summary>For a suction hose, the pump it is fixed to (end 0).</summary>
        public int PumpId { get; internal set; }
    }

    public sealed class TankState
    {
        public int Id { get; internal set; }
        public string Item { get; internal set; }
        public double Contents { get; internal set; }
        public bool InletFitted { get; internal set; }
        public bool Installed { get; internal set; }
    }

    public sealed class NetworkSlice
    {
        public List<TankState> Tanks { get; } = new List<TankState>();
        public List<HoseState> Hoses { get; } = new List<HoseState>();
    }

    // ---------------- Pumps ----------------

    public enum PumpRunState { Crated, Carried, Placed, Ready, Running, Stalled, Dry }

    public sealed class PumpState
    {
        public int Id { get; internal set; }
        /// <summary>hand_pump or electric_pump_2.</summary>
        public string Kind { get; internal set; }
        public int CrateId { get; internal set; }
        public bool MotorFitted { get; internal set; }
        public bool LeverOn { get; internal set; }
        /// <summary>Post socket index, or -1 when unplugged.</summary>
        public int CableSocket { get; internal set; } = -1;
        /// <summary>True when the pump has a power cable (fitted motor or second pump).</summary>
        public bool HasCable { get; internal set; }
        public bool HasCableEndPose { get; internal set; }
        public Pose CableEndPose { get; internal set; }
    }

    public sealed class PumpsSlice
    {
        public List<PumpState> Pumps { get; } = new List<PumpState>();
    }

    // ---------------- Hand Tools ----------------

    public sealed class HandToolsSlice
    {
        /// <summary>Per player: tool id -> litres held. A key exists for every owned tool.</summary>
        public Dictionary<int, Dictionary<string, double>> Held { get; } = new Dictionary<int, Dictionary<string, double>>();
    }

    // ---------------- Economy ----------------

    public enum CrateStage { Inbound, Landed, Opened }

    public sealed class CrateRecord
    {
        public int Id { get; internal set; }
        public string Item { get; internal set; }
        public CrateStage Stage { get; internal set; }
        public int TicksLeft { get; internal set; }
        public long LandedTick { get; internal set; }
        public int Spot { get; internal set; } = -1;
        public int Buyer { get; internal set; }
    }

    public sealed class EconomySlice
    {
        /// <summary>Credits in millicredits (1 cr = 1,000 mc).</summary>
        public long CreditsMc { get; internal set; }
        /// <summary>Owned items, in purchase order.</summary>
        public List<string> Owned { get; } = new List<string>();
        public List<CrateRecord> Crates { get; } = new List<CrateRecord>();
    }

    // ---------------- Drawdown ----------------

    public sealed class DrawdownSlice
    {
        /// <summary>Per source id: thresholds passed (percent), ascending.</summary>
        public Dictionary<int, List<double>> Passed { get; } = new Dictionary<int, List<double>>();
        /// <summary>Reveal ids triggered.</summary>
        public List<int> Reveals { get; } = new List<int>();
    }

    // ---------------- Shipping ----------------

    public enum PadState { Ready, Countdown, Away }

    public sealed class ShippingSlice
    {
        public PadState State { get; internal set; }
        /// <summary>Ticks until the current timer fires (0 in Ready).</summary>
        public int TimerTicks { get; internal set; }
        /// <summary>Litres aboard, in whole microlitres.</summary>
        public long AboardUl { get; internal set; }
        /// <summary>Total shipped, in whole microlitres.</summary>
        public long ShippedUl { get; internal set; }
        public int Launches { get; internal set; }

        public double AboardL => AboardUl / 1e6;
        public double ShippedL => ShippedUl / 1e6;
    }

    // ---------------- Placement ----------------

    public enum EntityKind { Crate, Tank, Pump, MotorKit, InletFitting, Prop }

    public sealed class Entity
    {
        public int Id { get; internal set; }
        public EntityKind Kind { get; internal set; }
        /// <summary>Tuning item id (for a crate, the item inside).</summary>
        public string Item { get; internal set; }
        public Pose Pose { get; internal set; }
        /// <summary>Held by a player (no world pose) when >= 0.</summary>
        public int HeldBy { get; internal set; } = -1;
    }

    public enum HandsKind { Empty, Tool, Entity, HoseEnd, CableEnd }

    /// <summary>What is in a player's hands.</summary>
    public struct HandsItem
    {
        public HandsKind Kind;
        /// <summary>Tool id when Kind is Tool.</summary>
        public string Tool;
        /// <summary>Entity id, hose id or pump id.</summary>
        public int Id;
        public int EndIdx;

        public static HandsItem Empty => default;
        public static HandsItem OfTool(string t) => new HandsItem { Kind = HandsKind.Tool, Tool = t };
        public static HandsItem OfEntity(int id) => new HandsItem { Kind = HandsKind.Entity, Id = id };
        public static HandsItem OfHoseEnd(int hose, int end) => new HandsItem { Kind = HandsKind.HoseEnd, Id = hose, EndIdx = end };
        public static HandsItem OfCableEnd(int pump) => new HandsItem { Kind = HandsKind.CableEnd, Id = pump };
        /// <summary>True for a carried item (not a tool, not empty).</summary>
        public bool IsCarried => Kind == HandsKind.Entity || Kind == HandsKind.HoseEnd || Kind == HandsKind.CableEnd;
        public override string ToString() => Kind switch
        {
            HandsKind.Tool => Tool,
            HandsKind.Entity => $"entity {Id}",
            HandsKind.HoseEnd => $"hose {Id} end {EndIdx}",
            HandsKind.CableEnd => $"cable of {Id}",
            _ => "empty",
        };
    }

    public sealed class PlayerRecord
    {
        public int Id { get; internal set; }
        public Pose Pose { get; internal set; }
        public HandsItem Hands { get; internal set; }
        /// <summary>The hand tool stowed when a carried item was picked up (restored when hands empty).</summary>
        public string StowedTool { get; internal set; }
    }

    public sealed class PlacementSlice
    {
        /// <summary>Every placed or carried object, by id.</summary>
        public SortedDictionary<int, Entity> Entities { get; } = new SortedDictionary<int, Entity>();
        public Dictionary<int, PlayerRecord> Players { get; } = new Dictionary<int, PlayerRecord>();
    }

    // ---------------- Session ----------------

    public sealed class SessionSlice
    {
        public string SessionId { get; internal set; } = "";
        public long Tick { get; internal set; }
        /// <summary>Play time in seconds, across loads.</summary>
        public double SessionTimeS { get; internal set; }
        /// <summary>Next runtime object id (starts at 1,000,000).</summary>
        public int NextId { get; internal set; } = Ids.FirstRuntimeId;
    }

    /// <summary>Stable id ranges (Game State Rule 2).</summary>
    public static class Ids
    {
        public const int FirstRuntimeId = 1_000_000;
    }
}
