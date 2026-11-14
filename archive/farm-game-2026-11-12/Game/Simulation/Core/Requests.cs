namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// A validated intent (Game State Rule 5). Requests carry the issuer and ids, never quantities;
    /// the only exception is a pose in Placement requests, which the simulation validates.
    /// </summary>
    public abstract class Request
    {
        /// <summary>Issuer's player id (always 0 in the MVP).</summary>
        public int Player;
        /// <summary>Short type name used for rejection text lookup (FPC Rule 4).</summary>
        public abstract string Name { get; }
    }

    // ---- Placement (Game State Rule 12) ----
    public sealed class PlayerPoseRequest : Request { public Pose Pose; public override string Name => "PlayerPose"; }
    public sealed class PlaceItemRequest : Request { public Pose Pose; public override string Name => "PlaceItem"; }
    public sealed class DropRequest : Request { public Pose Pose; public override string Name => "Drop"; }

    public enum PickUpKind { Entity, HoseEnd, CableEnd }
    public sealed class PickUpRequest : Request
    {
        public PickUpKind Kind;
        /// <summary>Entity id, hose id or pump id (cable end).</summary>
        public int Id;
        /// <summary>Hose end index (hose ends only). For a coil, 0.</summary>
        public int EndIdx;
        public override string Name => "PickUp";
    }
    public sealed class PropSettledRequest : Request { public int Id; public Pose Pose; public override string Name => "PropSettled"; }

    // ---- Hand Tools ----
    public sealed class SelectToolRequest : Request { public string Tool; public override string Name => "SelectTool"; }
    public sealed class ScoopRequest : Request
    {
        public string Tool;
        public int Source;
        /// <summary>The point aimed at on the water (range is measured to it).</summary>
        public Pose AimPoint;
        public override string Name => "Scoop";
    }
    public sealed class PourRequest : Request { public string Tool; public int Container; public override string Name => "Pour"; }

    // ---- Hose / Storage Network ----
    public sealed class ConnectRequest : Request { public int Hose; public int EndIdx; public EndTarget Target; public override string Name => "Connect"; }
    public sealed class DisconnectRequest : Request { public int Hose; public int EndIdx; public override string Name => "Disconnect"; }
    public sealed class FitInletRequest : Request { public int Tank; public override string Name => "FitInlet"; }
    public sealed class SwapStorageRequest : Request { public int Tank; public override string Name => "SwapStorage"; }

    // ---- Pumps ----
    public sealed class CrankStartRequest : Request { public int Pump; public override string Name => "CrankStart"; }
    public sealed class CrankStopRequest : Request { public int Pump; public override string Name => "CrankStop"; }
    public sealed class BoltMotorRequest : Request { public int Pump; public override string Name => "BoltMotor"; }
    public sealed class SetSwitchRequest : Request { public int Pump; public bool On; public override string Name => "SetSwitch"; }
    public sealed class PlugCableRequest : Request { public int Pump; public int Socket; public override string Name => "PlugCable"; }
    public sealed class UnplugCableRequest : Request { public int Pump; public override string Name => "UnplugCable"; }

    // ---- Economy ----
    public sealed class OrderRequest : Request { public string Item; public override string Name => "Order"; }
    public sealed class OpenCrateRequest : Request { public int Crate; public override string Name => "OpenCrate"; }

    // ---- Shipping ----
    public sealed class LaunchRequest : Request { public override string Name => "Launch"; }

    /// <summary>Rejection codes (owners' GDDs). Player-facing text lives in the controller's table.</summary>
    public static class Codes
    {
        public const string TooFar = "too_far";
        public const string Blocked = "blocked";
        public const string HandsFull = "hands_full";
        public const string NotEmpty = "not_empty";
        public const string SwitchOffFirst = "switch_off_first";
        public const string NotHolding = "not_holding";
        public const string NotOwned = "not_owned";
        public const string CrateUnopened = "crate_unopened";
        public const string NotInHand = "not_in_hand";
        public const string ToolFull = "tool_full";
        public const string SourceDry = "source_dry";
        public const string ToolEmpty = "tool_empty";
        public const string CannotDeposit = "cannot_deposit";
        public const string NotInstalled = "not_installed";
        public const string PoolFull = "pool_full";
        public const string WrongSocket = "wrong_socket";
        public const string NoInlet = "no_inlet";
        public const string SocketInUse = "socket_in_use";
        public const string HoseTooShort = "hose_too_short";
        public const string FixedEnd = "fixed_end";
        public const string AlreadyFitted = "already_fitted";
        public const string NotOnStagingSpot = "not_on_staging_spot";
        public const string NotAnUpgrade = "not_an_upgrade";
        public const string NotPlaced = "not_placed";
        public const string NoHandle = "no_handle";
        public const string NotHoldingKit = "not_holding_kit";
        public const string NoPump = "no_pump";
        public const string AlreadyPowered = "already_powered";
        public const string NoMotor = "no_motor";
        public const string NotPlugged = "not_plugged";
        public const string AlreadyOwned = "already_owned";
        public const string Locked = "locked";
        public const string CannotAfford = "cannot_afford";
        public const string AlreadyOpen = "already_open";
        public const string PadBusy = "pad_busy";
        public const string NothingToShip = "nothing_to_ship";
        public const string Unknown = "unknown_target";
    }
}
