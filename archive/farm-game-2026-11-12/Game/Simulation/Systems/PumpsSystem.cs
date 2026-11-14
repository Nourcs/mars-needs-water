using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// Pumps &amp; Machines: hand pump (crank), electric motor kit, second electric pump, power cable and switch.
    /// Derives each pump's state at tick step 2a; the Network decides flow at 2b.
    /// </summary>
    public sealed class PumpsSystem
    {
        readonly Simulation _sim;
        PumpsSlice Slice => _sim.State.Pumps;

        // Per-player crank flag (never saved) and last derived states (presentation-facing).
        readonly Dictionary<int, int> _crank = new Dictionary<int, int>();
        readonly Dictionary<int, PumpRunState> _state = new Dictionary<int, PumpRunState>();
        bool _firstStep = true;

        internal PumpsSystem(Simulation sim) { _sim = sim; }

        internal void NewGame()
        {
            Slice.Pumps.Clear();
            _crank.Clear();
            _state.Clear();
            _firstStep = true;
        }

        public IReadOnlyList<PumpState> All => Slice.Pumps;

        public PumpState Get(int id)
        {
            foreach (var p in Slice.Pumps) if (p.Id == id) return p;
            return null;
        }

        /// <summary>The state derived at the last step 2a.</summary>
        public PumpRunState StateOf(int id) => _state.TryGetValue(id, out var s) ? s : PumpRunState.Crated;

        /// <summary>Which pump a player is cranking (0 = none).</summary>
        public int CrankFlag(int player) => _crank.TryGetValue(player, out var p) ? p : 0;

        public bool IsElectric(PumpState p) => p.MotorFitted || p.Kind == "electric_pump_2";

        double RateOf(PumpState p)
        {
            if (p.Kind == "hand_pump" && !p.MotorFitted) return _sim.Tuning.Item("hand_pump").rate_Lps;
            if (p.Kind == "hand_pump") return _sim.Tuning.Item("electric_motor").rate_Lps;
            return _sim.Tuning.Item(p.Kind).rate_Lps;
        }

        /// <summary>Economy calls this in the same step 1 as the purchase. Returns the new pump id (0 for the kit).</summary>
        public int Grant(string item, int crateId)
        {
            if (item != "hand_pump" && item != "electric_pump_2") return 0;
            int id = _sim.NewId();
            Slice.Pumps.Add(new PumpState { Id = id, Kind = item, CrateId = crateId, HasCable = item == "electric_pump_2" });
            return id;
        }

        Entity EntityOf(PumpState p) => _sim.Placement.Get(p.Id);

        bool IsPlaced(PumpState p)
        {
            var e = EntityOf(p);
            return e != null && e.HeldBy < 0;
        }

        /// <summary>First missing part for the pump's prompt: no_intake, no_outlet, no_power, or null.</summary>
        public string MissingPart(int pumpId)
        {
            var p = Get(pumpId);
            if (p == null) return null;
            if (_sim.Network.IntakeBound(p.Id) == 0) return "no_intake";
            if (_sim.Network.OutletPool(p.Id) == null) return "no_outlet";
            if (IsElectric(p) && p.CableSocket < 0) return "no_power";
            return null;
        }

        /// <summary>Handle position (hand pump) in world space.</summary>
        public bool TryPoint(int pumpId, string kind, out Pose pose) => _sim.Network.TrySocketPose(pumpId, kind, 0, out pose);

        // ---------------- step 2a ----------------

        internal void Step()
        {
            var crankRange = _sim.Tuning.Data.range_crank_m;
            // Clear crank flags of players who walked away from the handle.
            var players = new List<int>(_crank.Keys);
            foreach (var pl in players)
            {
                int pumpId = _crank[pl];
                if (!TryPoint(pumpId, "handle", out var handle) || !_sim.Placement.InRange(pl, handle, crankRange))
                    _crank.Remove(pl);
            }
            foreach (var p in Slice.Pumps)
            {
                var e = EntityOf(p);
                // A carried pump loses its cable socket; the end lies at the socket.
                if (e != null && e.HeldBy >= 0 && p.CableSocket >= 0)
                {
                    p.CableEndPose = _sim.Level.PostSockets[p.CableSocket];
                    p.HasCableEndPose = true;
                    p.CableSocket = -1;
                }
                var s = Derive(p);
                bool had = _state.TryGetValue(p.Id, out var from);
                _state[p.Id] = s;
                if (!_firstStep && had && from != s)
                    _sim.Raise(new SimEvent { Type = SimEventType.PumpStateChanged, Id = p.Id, Text = from.ToString(), Text2 = s.ToString() });
                else if (!_firstStep && !had && s != PumpRunState.Crated)
                    _sim.Raise(new SimEvent { Type = SimEventType.PumpStateChanged, Id = p.Id, Text = PumpRunState.Crated.ToString(), Text2 = s.ToString() });
            }
            _firstStep = false;
        }

        PumpRunState Derive(PumpState p)
        {
            if (!_sim.Economy.CrateOpened(p.CrateId)) return PumpRunState.Crated;
            var e = EntityOf(p);
            if (e == null) return PumpRunState.Crated;
            if (e.HeldBy >= 0) return PumpRunState.Carried;
            int src = _sim.Network.IntakeBound(p.Id);
            var pool = _sim.Network.OutletPool(p.Id);
            bool connected = src != 0 && pool != null;
            bool powered = !IsElectric(p) || p.CableSocket >= 0;
            if (!connected || !powered) return PumpRunState.Placed;
            bool drained = _sim.WaterSource.State(src) == SourceCondition.Drained;
            bool demanded = IsElectric(p) ? p.LeverOn : IsCranked(p.Id);
            if (!demanded) return drained ? PumpRunState.Placed : PumpRunState.Ready;
            if (drained) return PumpRunState.Dry;
            if (_sim.Network.IsFull(pool)) return PumpRunState.Stalled;
            return PumpRunState.Running;
        }

        bool IsCranked(int pumpId)
        {
            foreach (var kv in _crank) if (kv.Value == pumpId) return true;
            return false;
        }

        /// <summary>Rate sent to the Network for the flow solve: the table rate while Running, else 0.</summary>
        internal double DemandRate(PumpState p) => StateOf(p.Id) == PumpRunState.Running ? RateOf(p) : 0;

        // ---------------- requests (step 1) ----------------

        internal string Apply(Request r)
        {
            switch (r)
            {
                case CrankStartRequest c: return CrankStart(c);
                case CrankStopRequest c: return CrankStop(c);
                case BoltMotorRequest b: return BoltMotor(b);
                case SetSwitchRequest s: return SetSwitch(s);
                case PlugCableRequest p: return PlugCable(p);
                case UnplugCableRequest u: return UnplugCable(u);
            }
            return Codes.Unknown;
        }

        string CrankStart(CrankStartRequest r)
        {
            var p = Get(r.Pump);
            if (p == null) return Codes.Unknown;
            if (!_sim.Economy.CrateOpened(p.CrateId) || !IsPlaced(p)) return Codes.NotPlaced;
            if (IsElectric(p)) return Codes.NoHandle;
            if (!TryPoint(p.Id, "handle", out var handle) || !_sim.Placement.InRange(r.Player, handle, _sim.Tuning.Data.range_crank_m)) return Codes.TooFar;
            _crank[r.Player] = p.Id;
            return null;
        }

        string CrankStop(CrankStopRequest r)
        {
            if (_crank.TryGetValue(r.Player, out var id) && id == r.Pump) _crank.Remove(r.Player);
            return null;
        }

        string BoltMotor(BoltMotorRequest r)
        {
            var hands = _sim.Placement.Hands(r.Player);
            var kit = hands.Kind == HandsKind.Entity ? _sim.Placement.Get(hands.Id) : null;
            if (kit == null || kit.Kind != EntityKind.MotorKit) return Codes.NotHoldingKit;
            var p = Get(r.Pump);
            if (p == null || !_sim.Economy.CrateOpened(p.CrateId) || !IsPlaced(p)) return Codes.NoPump;
            if (p.Kind == "electric_pump_2") return Codes.AlreadyPowered;
            if (p.MotorFitted) return Codes.AlreadyFitted;
            if (!_sim.Placement.InRange(r.Player, _sim.Placement.InteractPoint(EntityOf(p)), _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
            p.MotorFitted = true;
            p.HasCable = true;
            p.CableEndPose = EntityOf(p).Pose.Offset(0.6, 0, 0);
            p.HasCableEndPose = true;
            _crank.Remove(r.Player);
            foreach (var k in new List<int>(_crank.Keys)) if (_crank[k] == p.Id) _crank.Remove(k);
            _sim.Placement.Remove(kit.Id);
            return null;
        }

        string SetSwitch(SetSwitchRequest r)
        {
            var p = Get(r.Pump);
            if (p == null) return Codes.Unknown;
            if (!_sim.Economy.CrateOpened(p.CrateId) || !IsPlaced(p)) return Codes.NotPlaced;
            if (!IsElectric(p)) return Codes.NoMotor;
            if (!TryPoint(p.Id, "lever", out var lever)) lever = EntityOf(p).Pose;
            if (!_sim.Placement.InRange(r.Player, lever, _sim.Tuning.Data.range_lever_m)) return Codes.TooFar;
            p.LeverOn = r.On;
            return null;
        }

        string PlugCable(PlugCableRequest r)
        {
            var hands = _sim.Placement.Hands(r.Player);
            if (hands.Kind != HandsKind.CableEnd || hands.Id != r.Pump) return Codes.NotHolding;
            var p = Get(r.Pump);
            if (p == null) return Codes.Unknown;
            if (!IsPlaced(p)) return Codes.NotPlaced;
            if (!IsElectric(p)) return Codes.NoMotor;
            if (r.Socket < 0 || r.Socket >= _sim.Level.PostSockets.Count || r.Socket >= _sim.Tuning.Data.post_sockets) return Codes.Unknown;
            foreach (var q in Slice.Pumps) if (q.CableSocket == r.Socket) return Codes.SocketInUse;
            var socket = _sim.Level.PostSockets[r.Socket];
            if (!_sim.Placement.InRange(r.Player, socket, _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
            if (!TryPoint(p.Id, "cable_port", out var port)) port = EntityOf(p).Pose;
            if (port.DistanceTo(socket) > _sim.Tuning.Data.power_cable_m + 1e-9) return Codes.TooFar;
            p.CableSocket = r.Socket;
            p.HasCableEndPose = false;
            _sim.Placement.SetHands(r.Player, HandsItem.Empty);
            return null;
        }

        string UnplugCable(UnplugCableRequest r)
        {
            var p = Get(r.Pump);
            if (p == null) return Codes.Unknown;
            if (p.CableSocket < 0) return Codes.NotPlugged;
            if (!_sim.Placement.InRange(r.Player, _sim.Level.PostSockets[p.CableSocket], _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
            if (_sim.Placement.Hands(r.Player).IsCarried) return Codes.HandsFull;
            p.CableSocket = -1;
            _sim.Placement.SetHands(r.Player, HandsItem.OfCableEnd(p.Id));
            return null;
        }

        // ---------------- direct calls from Placement ----------------

        /// <summary>An electric pump that is switched on cannot be picked up.</summary>
        public string CanPickUp(int pumpId)
        {
            var p = Get(pumpId);
            if (p != null && IsElectric(p) && p.LeverOn) return Codes.SwitchOffFirst;
            return null;
        }

        internal void OnPickedUp(int pumpId)
        {
            foreach (var k in new List<int>(_crank.Keys)) if (_crank[k] == pumpId) _crank.Remove(k);
        }

        /// <summary>Where a pump's loose cable end is (rest pose, a holder's hands, or the socket).</summary>
        public bool TryCableEndPose(int pumpId, out Pose pose)
        {
            pose = default;
            var p = Get(pumpId);
            if (p == null || !p.HasCable) return false;
            if (p.CableSocket >= 0) { pose = _sim.Level.PostSockets[p.CableSocket]; return true; }
            if (p.HasCableEndPose) { pose = p.CableEndPose; return true; }
            foreach (var pl in _sim.State.Placement.Players.Values)
                if (pl.Hands.Kind == HandsKind.CableEnd && pl.Hands.Id == pumpId) { pose = pl.Pose.Raised(1.0); return true; }
            var e = EntityOf(p);
            if (e != null) { pose = e.Pose; return true; }
            return false;
        }

        public string CanPickUpCable(int pumpId)
        {
            var p = Get(pumpId);
            if (p == null || !p.HasCable) return Codes.Unknown;
            if (p.CableSocket >= 0) return Codes.Blocked; // use Unplug instead
            if (!IsPlaced(p)) return Codes.NotPlaced;
            return null;
        }

        public void CableEndHeld(int pumpId, int player)
        {
            var p = Get(pumpId);
            if (p != null) p.HasCableEndPose = false;
        }

        public void CableEndDropped(int pumpId, Pose pose)
        {
            var p = Get(pumpId);
            if (p == null) return;
            p.CableEndPose = pose;
            p.HasCableEndPose = true;
        }

        /// <summary>Hands change or a menu: release any crank (FPC Rule 5, 10).</summary>
        internal void ReleaseCrank(int player) => _crank.Remove(player);
    }
}
