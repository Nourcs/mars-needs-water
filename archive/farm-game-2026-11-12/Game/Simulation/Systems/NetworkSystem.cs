using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    /// <summary>A pool: installed tanks joined by link hoses (Hose Rule 5). Derived, never saved.</summary>
    public sealed class Pool
    {
        public int Id;
        /// <summary>Tanks in fill order (pad tank first).</summary>
        public readonly List<TankState> Tanks = new List<TankState>();
        public double Capacity;
        public double Contents
        {
            get { double c = 0; foreach (var t in Tanks) c += t.Contents; return c; }
        }
    }

    /// <summary>
    /// Hose, Storage &amp; Flow Network: tanks, hoses, pools, pours, Take, the storage swap and the flow
    /// solve at tick step 2b. Hoses hold no water, so water is conserved exactly.
    /// </summary>
    public sealed class NetworkSystem
    {
        public const double FullEpsilon = 1e-6;

        readonly Simulation _sim;
        NetworkSlice Slice => _sim.State.Network;

        readonly List<Pool> _pools = new List<Pool>();
        readonly Dictionary<int, Pool> _poolOfTank = new Dictionary<int, Pool>();
        readonly Dictionary<int, bool> _fullLatch = new Dictionary<int, bool>();
        readonly Dictionary<int, double> _movedLastTick = new Dictionary<int, double>();
        bool _dirty = true;

        internal NetworkSystem(Simulation sim) { _sim = sim; }

        internal void NewGame()
        {
            Slice.Tanks.Clear();
            Slice.Hoses.Clear();
            _fullLatch.Clear();
            _movedLastTick.Clear();
            RegisterTank(_sim.Level.StarterTankId, _sim.Level.StarterTankItem, installed: true);
            Rebuild(silent: true);
        }

        // ================= tanks =================

        /// <summary>Creates a tank record (Economy calls this when a crate unboxes a tank).</summary>
        public TankState RegisterTank(int id, string item, bool installed)
        {
            var row = _sim.Tuning.Item(item);
            bool needsFitting = Array.IndexOf(row.crateContents ?? Array.Empty<string>(), "inlet_fitting") >= 0;
            var t = new TankState { Id = id, Item = item, Installed = installed, InletFitted = !needsFitting };
            Slice.Tanks.Add(t);
            Slice.Tanks.Sort((a, b) => a.Id.CompareTo(b.Id));
            _dirty = true;
            return t;
        }

        public TankState Tank(int id)
        {
            foreach (var t in Slice.Tanks) if (t.Id == id) return t;
            return null;
        }

        public double TankCapacity(TankState t) => _sim.Tuning.Item(t.Item).capacity_L;

        /// <summary>Sum of all tank contents (for conservation checks).</summary>
        public double TotalInTanks()
        {
            double c = 0;
            foreach (var t in Slice.Tanks) c += t.Contents;
            return c;
        }

        // ================= hoses =================

        /// <summary>Creates a hose (Economy calls this on unboxing). A suction hose's end 0 is fixed to the pump.</summary>
        public HoseState CreateHose(int id, HoseKind kind, Pose restPose, int pumpId = 0)
        {
            var d = _sim.Tuning.Data;
            var h = new HoseState
            {
                Id = id,
                Kind = kind,
                LengthM = kind == HoseKind.Suction ? d.suction_hose_m : kind == HoseKind.Link ? d.link_hose_m : d.discharge_hose_m,
                PumpId = pumpId,
            };
            h.Ends[0].RestPose = restPose;
            h.Ends[1].RestPose = restPose;
            if (kind == HoseKind.Suction) h.Ends[0].Target = EndTarget.At(pumpId, "intake", 0);
            Slice.Hoses.Add(h);
            Slice.Hoses.Sort((a, b) => a.Id.CompareTo(b.Id));
            _dirty = true;
            return h;
        }

        public HoseState Hose(int id)
        {
            foreach (var h in Slice.Hoses) if (h.Id == id) return h;
            return null;
        }

        /// <summary>World position of a node's named point, or false if the node has no such point or is not placed.</summary>
        public bool TrySocketPose(int nodeId, string kind, int index, out Pose pose)
        {
            pose = default;
            var e = _sim.Placement.Get(nodeId);
            if (e == null || e.HeldBy >= 0) return false;
            if (!_sim.Tuning.TryPoint(e.Item, kind, index, out var p)) return false;
            pose = e.Pose.Offset(p.x, p.y, p.z);
            return true;
        }

        /// <summary>Where a hose end is now: its socket, its intake aim point, a holder's hands, or its rest pose.</summary>
        public bool TryEndPose(int hoseId, int endIdx, out Pose pose)
        {
            pose = default;
            var h = Hose(hoseId);
            if (h == null || endIdx < 0 || endIdx > 1) return false;
            var end = h.Ends[endIdx];
            if (end.Target.Kind == EndTargetKind.Socket)
            {
                if (TrySocketPose(end.Target.NodeId, end.Target.Socket, end.Target.Index, out pose)) return true;
                // A suction hose on a carried pump: follow the carrier.
                var holder = HolderOfEntity(end.Target.NodeId);
                if (holder != null) { pose = holder.Pose; return true; }
                pose = end.RestPose;
                return true;
            }
            if (end.Target.Kind == EndTargetKind.Intake) { pose = end.Target.AimPoint; return true; }
            var held = HolderOfEnd(hoseId, endIdx);
            if (held != null) { pose = held.Pose.Raised(1.0); return true; }
            pose = end.RestPose;
            return true;
        }

        PlayerRecord HolderOfEnd(int hoseId, int endIdx)
        {
            foreach (var p in _sim.State.Placement.Players.Values)
                if (p.Hands.Kind == HandsKind.HoseEnd && p.Hands.Id == hoseId && p.Hands.EndIdx == endIdx) return p;
            return null;
        }

        PlayerRecord HolderOfEntity(int id)
        {
            var e = _sim.Placement.Get(id);
            return e != null && e.HeldBy >= 0 ? _sim.Placement.Player(e.HeldBy) : null;
        }

        /// <summary>The source a pump's suction hose is bound to, or 0 (none).</summary>
        public int IntakeBound(int pumpId)
        {
            foreach (var h in Slice.Hoses)
                if (h.Kind == HoseKind.Suction && h.PumpId == pumpId && h.Ends[1].Target.Kind == EndTargetKind.Intake)
                    return h.Ends[1].Target.SourceId;
            return 0;
        }

        /// <summary>The pool a pump's outlet feeds (outlet hose plugged at both ends into an installed tank inlet), or null.</summary>
        public Pool OutletPool(int pumpId)
        {
            EnsurePools();
            foreach (var h in Slice.Hoses)
            {
                if (h.Kind != HoseKind.Discharge) continue;
                for (int i = 0; i < 2; i++)
                {
                    var a = h.Ends[i].Target;
                    var b = h.Ends[1 - i].Target;
                    if (a.Kind == EndTargetKind.Socket && a.NodeId == pumpId && a.Socket == "outlet" &&
                        b.Kind == EndTargetKind.Socket && b.Socket == "inlet")
                    {
                        var t = Tank(b.NodeId);
                        if (t != null && t.Installed && _poolOfTank.TryGetValue(t.Id, out var pool)) return pool;
                    }
                }
            }
            return null;
        }

        /// <summary>Litres a pump moved at the last flow solve (for sound and gauges only).</summary>
        public double MovedLastTick(int pumpId) => _movedLastTick.TryGetValue(pumpId, out var v) ? v : 0;

        // ================= pools =================

        public IReadOnlyList<Pool> Pools { get { EnsurePools(); return _pools; } }

        public Pool PoolOf(int tankId)
        {
            EnsurePools();
            return _poolOfTank.TryGetValue(tankId, out var p) ? p : null;
        }

        public Pool PoolById(int poolId)
        {
            EnsurePools();
            foreach (var p in _pools) if (p.Id == poolId) return p;
            return null;
        }

        /// <summary>The pad pool (the pad tank and any tank linked to it), or null.</summary>
        public Pool PadPool()
        {
            var pad = _sim.Placement.PadTank();
            return pad == null ? null : PoolOf(pad.Id);
        }

        /// <summary>Full when contents ≥ capacity − 1e-6 L, computed live (Hose Rule 9).</summary>
        public bool IsFull(Pool p) => p != null && p.Contents >= p.Capacity - FullEpsilon;

        public static double Level(Pool p) => p == null || p.Capacity <= 0 ? 0 : Math.Min(1, p.Contents / p.Capacity);

        void EnsurePools() { if (_dirty) Rebuild(silent: true); }

        /// <summary>Rebuilds pools from installed tanks and link hoses. Sets the PoolFull latches silently.</summary>
        internal void Rebuild(bool silent)
        {
            _dirty = false;
            _pools.Clear();
            _poolOfTank.Clear();
            var installed = new List<TankState>();
            foreach (var t in Slice.Tanks) if (t.Installed) installed.Add(t);
            // Union through link hoses (one link socket per tank -> pools of at most two in the MVP).
            var parent = new Dictionary<int, int>();
            foreach (var t in installed) parent[t.Id] = t.Id;
            int FindRoot(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            foreach (var h in Slice.Hoses)
            {
                if (h.Kind != HoseKind.Link) continue;
                var a = h.Ends[0].Target; var b = h.Ends[1].Target;
                if (a.Kind != EndTargetKind.Socket || b.Kind != EndTargetKind.Socket) continue;
                if (!parent.ContainsKey(a.NodeId) || !parent.ContainsKey(b.NodeId)) continue;
                int ra = FindRoot(a.NodeId), rb = FindRoot(b.NodeId);
                if (ra != rb) parent[ra] = rb;
            }
            var pad = _sim.Placement.PadTank();
            var groups = new SortedDictionary<int, List<TankState>>();
            foreach (var t in installed)
            {
                int r = FindRoot(t.Id);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<TankState>();
                g.Add(t);
            }
            foreach (var g in groups.Values)
            {
                g.Sort((x, y) => x.Id.CompareTo(y.Id));
                var pool = new Pool();
                int padIdx = pad == null ? -1 : g.FindIndex(t => t.Id == pad.Id);
                if (padIdx > 0) { var p0 = g[padIdx]; g.RemoveAt(padIdx); g.Insert(0, p0); }
                pool.Tanks.AddRange(g);
                pool.Id = g[0].Id; // pad tank's id if present (it is first), else the lowest id
                foreach (var t in g) { pool.Capacity += TankCapacity(t); _poolOfTank[t.Id] = pool; }
                _pools.Add(pool);
            }
            if (silent)
            {
                _fullLatch.Clear();
                foreach (var p in _pools) _fullLatch[p.Id] = IsFull(p);
            }
        }

        void MarkDirtyAndRebuild() { _dirty = true; Rebuild(silent: true); }

        /// <summary>Deposits litres into a pool in fill order (pad tank first). Overshoot ≤ 1e-6 L goes to the last tank.</summary>
        static void Deposit(Pool pool, double litres, Func<TankState, double> cap)
        {
            double left = litres;
            for (int i = 0; i < pool.Tanks.Count && left > 0; i++)
            {
                var t = pool.Tanks[i];
                double space = Math.Max(0, cap(t) - t.Contents);
                double put = i == pool.Tanks.Count - 1 ? left : Math.Min(space, left);
                t.Contents += put;
                left -= put;
            }
        }

        /// <summary>
        /// The hand pour rule (Hose Rule 10): pours min(held, pool space) into the tank's pool.
        /// Returns null and the litres poured, or a reason (not_installed, pool_full).
        /// </summary>
        public string PourInto(int containerId, double held, out double poured)
        {
            poured = 0;
            var t = Tank(containerId);
            if (t == null || !t.Installed) return Codes.NotInstalled;
            var pool = PoolOf(t.Id);
            if (pool == null) return Codes.NotInstalled;
            if (IsFull(pool)) return Codes.PoolFull;
            double space = Math.Max(0, pool.Capacity - pool.Contents);
            poured = Math.Min(Math.Max(held, 0), space);
            Deposit(pool, poured, TankCapacity);
            return null;
        }

        /// <summary>Removes min(amount, contents) from a pool, linked tank first, then the pad tank. Returns litres taken.</summary>
        public double Take(Pool pool, double amount)
        {
            if (pool == null || !(amount > 0)) return 0;
            double want = Math.Min(amount, pool.Contents), got = 0;
            for (int i = pool.Tanks.Count - 1; i >= 0 && got < want; i--)
            {
                var t = pool.Tanks[i];
                double take = Math.Min(t.Contents, want - got);
                t.Contents -= take;
                got += take;
            }
            return got;
        }

        // ================= flow solve (step 2b) =================

        internal void Step(IReadOnlyList<PumpState> pumps, Func<PumpState, double> rateOf)
        {
            EnsurePools();
            double dt = _sim.Dt;
            double hoseCap = _sim.Tuning.Data.discharge_hose_cap_Lps;
            _movedLastTick.Clear();
            var demand = new Dictionary<int, double>();
            var poolOf = new Dictionary<int, Pool>();
            var srcOf = new Dictionary<int, int>();
            // d_p
            foreach (var p in pumps)
            {
                int src = IntakeBound(p.Id);
                var pool = OutletPool(p.Id);
                if (src == 0 || pool == null) continue;
                double rate = rateOf(p);
                demand[p.Id] = rate > 0 ? Math.Min(rate, hoseCap) * dt : 0;
                poolOf[p.Id] = pool;
                srcOf[p.Id] = src;
            }
            // Space scale per pool.
            var scaled = new Dictionary<int, double>();
            foreach (var pool in _pools)
            {
                double space = IsFull(pool) ? 0 : Math.Max(0, pool.Capacity - pool.Contents);
                double D = 0;
                foreach (var kv in demand) if (poolOf[kv.Key] == pool) D += kv.Value;
                double k = D > 0 ? Math.Min(1, space / D) : 0;
                foreach (var kv in demand) if (poolOf[kv.Key] == pool) scaled[kv.Key] = kv.Value * k;
            }
            // One Withdraw per source with at least one connected pump, in id order.
            var sources = new SortedSet<int>(srcOf.Values);
            var moved = new Dictionary<int, double>();
            foreach (var s in sources)
            {
                var onS = new List<int>();
                foreach (var kv in srcOf) if (kv.Value == s) onS.Add(kv.Key);
                onS.Sort();
                double Ds = 0;
                foreach (var id in onS) Ds += scaled[id];
                double got = _sim.WaterSource.Withdraw(s, Ds);
                double given = 0;
                for (int i = 0; i < onS.Count; i++)
                {
                    double a = i == onS.Count - 1 ? got - given : (Ds > 0 ? scaled[onS[i]] * got / Ds : 0);
                    if (a < 0) a = 0;
                    moved[onS[i]] = a;
                    given += a;
                }
            }
            foreach (var kv in moved)
            {
                if (kv.Value > 0) Deposit(poolOf[kv.Key], kv.Value, TankCapacity);
                _movedLastTick[kv.Key] = kv.Value;
            }
            // PoolFull edge detection (covers this tick's pours and pumping).
            foreach (var pool in _pools)
            {
                bool full = IsFull(pool);
                _fullLatch.TryGetValue(pool.Id, out bool was);
                if (full && !was) _sim.Raise(new SimEvent { Type = SimEventType.PoolFull, Id = pool.Id });
                _fullLatch[pool.Id] = full;
            }
        }

        // ================= requests (step 1) =================

        internal string Apply(Request r)
        {
            switch (r)
            {
                case ConnectRequest c: return Connect(c);
                case DisconnectRequest d: return Disconnect(d);
                case FitInletRequest f: return FitInlet(f);
                case SwapStorageRequest s: return SwapStorage(s);
            }
            return Codes.Unknown;
        }

        /// <summary>Read-only check shared by step 1 and the hover prompt, so the prompt always matches the result.</summary>
        public string CanConnect(int player, int hoseId, int endIdx, EndTarget target)
        {
            var h = Hose(hoseId);
            if (h == null || endIdx < 0 || endIdx > 1) return Codes.Unknown;
            var hands = _sim.Placement.Hands(player);
            if (hands.Kind != HandsKind.HoseEnd || hands.Id != hoseId || hands.EndIdx != endIdx) return Codes.NotHolding;

            Pose targetPose;
            if (target.Kind == EndTargetKind.Intake)
            {
                var src = _sim.Level.Sources.Find(s => s.Id == target.SourceId);
                if (src == null) return Codes.WrongSocket;
                targetPose = target.AimPoint;
                if (!_sim.Placement.InRange(player, targetPose, _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
                if (h.Kind != HoseKind.Suction || endIdx != 1) return Codes.WrongSocket;
                if (targetPose.FlatDistanceTo(src.IntakeCentre) > src.IntakeRadius) return Codes.WrongSocket;
            }
            else if (target.Kind == EndTargetKind.Socket)
            {
                if (!TrySocketPose(target.NodeId, target.Socket, target.Index, out targetPose))
                {
                    var e = _sim.Placement.Get(target.NodeId);
                    if (e == null) return Codes.Unknown;
                    // Node exists but has no such socket (e.g. jerrycan, barrel).
                    if (!_sim.Placement.InRange(player, e.Pose, _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
                    return Codes.WrongSocket;
                }
                if (!_sim.Placement.InRange(player, targetPose, _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
                if (!PairAllowed(h, endIdx, target)) return Codes.WrongSocket;
                var tank = Tank(target.NodeId);
                if (tank != null && !tank.Installed) return Codes.NotInstalled;
                if (tank != null && target.Socket == "inlet" && !tank.InletFitted) return Codes.NoInlet;
                if (SocketInUse(target, h.Id, endIdx)) return Codes.SocketInUse;
            }
            else return Codes.WrongSocket;

            // Length rule: straight line between the two ends' positions.
            if (TryEndPose(h.Id, 1 - endIdx, out var other))
            {
                bool otherFree = !h.Ends[1 - endIdx].Target.IsSet && HolderOfEnd(h.Id, 1 - endIdx) == null;
                // A coil (both ends free together) has no tether: the other end comes along in hand.
                if (!otherFree || h.Kind == HoseKind.Suction)
                {
                    if (other.DistanceTo(targetPose) > h.LengthM + 1e-9) return Codes.HoseTooShort;
                }
            }
            return null;
        }

        bool PairAllowed(HoseState h, int endIdx, EndTarget target)
        {
            bool isPump = _sim.Pumps.Get(target.NodeId) != null;
            var other = h.Ends[1 - endIdx].Target;
            switch (h.Kind)
            {
                case HoseKind.Suction:
                    return false; // only the free end, and only into an intake zone
                case HoseKind.Discharge:
                    if (target.Socket == "outlet" && isPump) return !(other.IsSet && (other.Socket != "inlet"));
                    if (target.Socket == "inlet" && !isPump) return !(other.IsSet && (other.Socket != "outlet"));
                    return false;
                case HoseKind.Link:
                    if (target.Socket != "link" || isPump) return false;
                    return !(other.IsSet && (other.Socket != "link" || other.NodeId == target.NodeId));
            }
            return false;
        }

        bool SocketInUse(EndTarget target, int exceptHose, int exceptEnd)
        {
            foreach (var h in Slice.Hoses)
                for (int i = 0; i < 2; i++)
                {
                    if (h.Id == exceptHose && i == exceptEnd) continue;
                    if (h.Ends[i].Target.SameSocket(target)) return true;
                }
            return false;
        }

        string Connect(ConnectRequest r)
        {
            string why = CanConnect(r.Player, r.Hose, r.EndIdx, r.Target);
            if (why != null) return why;
            var h = Hose(r.Hose);
            h.Ends[r.EndIdx].Target = r.Target;
            int other = 1 - r.EndIdx;
            if (!h.Ends[other].Target.IsSet)
            {
                // Hand-over: the player walks the hose out with the other end in hand.
                _sim.Placement.SetHands(r.Player, HandsItem.OfHoseEnd(h.Id, other));
            }
            else _sim.Placement.SetHands(r.Player, HandsItem.Empty);
            MarkDirtyAndRebuild();
            return null;
        }

        string Disconnect(DisconnectRequest r)
        {
            var h = Hose(r.Hose);
            if (h == null || r.EndIdx < 0 || r.EndIdx > 1 || !h.Ends[r.EndIdx].Target.IsSet) return Codes.Unknown;
            TryEndPose(h.Id, r.EndIdx, out var pose);
            if (!_sim.Placement.InRange(r.Player, pose, _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
            if (h.Kind == HoseKind.Suction && r.EndIdx == 0) return Codes.FixedEnd;
            if (_sim.Placement.Hands(r.Player).IsCarried) return Codes.HandsFull;
            h.Ends[r.EndIdx].RestPose = pose;
            h.Ends[r.EndIdx].Target = EndTarget.None;
            _sim.Placement.SetHands(r.Player, HandsItem.OfHoseEnd(h.Id, r.EndIdx));
            MarkDirtyAndRebuild();
            return null;
        }

        string FitInlet(FitInletRequest r)
        {
            var hands = _sim.Placement.Hands(r.Player);
            var fitting = hands.Kind == HandsKind.Entity ? _sim.Placement.Get(hands.Id) : null;
            if (fitting == null || fitting.Kind != EntityKind.InletFitting) return Codes.NotHolding;
            var e = _sim.Placement.Get(r.Tank);
            var t = Tank(r.Tank);
            if (e == null || t == null) return Codes.Unknown;
            if (!_sim.Placement.InRange(r.Player, _sim.Placement.InteractPoint(e), _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
            if (t.InletFitted) return Codes.AlreadyFitted;
            t.InletFitted = true;
            _sim.Placement.Remove(fitting.Id);
            return null;
        }

        string SwapStorage(SwapStorageRequest r)
        {
            var e = _sim.Placement.Get(r.Tank);
            var t = Tank(r.Tank);
            if (e == null || t == null) return Codes.Unknown;
            if (!_sim.Placement.InRange(r.Player, _sim.Placement.InteractPoint(e), _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
            if (!_sim.Placement.OnStagingSpot(e)) return Codes.NotOnStagingSpot;
            var padE = _sim.Placement.PadTank();
            var pad = padE == null ? null : Tank(padE.Id);
            if (pad == null || t.Installed || t.Contents > 0 || TankCapacity(t) <= TankCapacity(pad) || t.Item == "ibc_tank")
                return Codes.NotAnUpgrade;
            // Move all water, retire the old container (the drone hauls it away), move the new one in.
            t.Contents += pad.Contents;
            pad.Contents = 0;
            DetachAll(pad.Id);
            Slice.Tanks.Remove(pad);
            _sim.Placement.Remove(pad.Id);
            _sim.Placement.Move(t.Id, _sim.Level.StorageSpot);
            t.Installed = true;
            MarkDirtyAndRebuild();
            return null;
        }

        // ================= direct calls from Placement =================

        /// <summary>The pad tank can never be picked up; any other tank only when empty.</summary>
        public string CanPickUp(int tankId)
        {
            var pad = _sim.Placement.PadTank();
            if (pad != null && pad.Id == tankId) return Codes.Blocked;
            var t = Tank(tankId);
            if (t != null && t.Contents > 0) return Codes.NotEmpty;
            return null;
        }

        /// <summary>A loose (unconnected) end can be picked up; a plugged end needs Disconnect.</summary>
        public string CanPickUpEnd(int hoseId, int endIdx)
        {
            var h = Hose(hoseId);
            if (h == null) return Codes.Unknown;
            if (h.Ends[endIdx].Target.IsSet) return Codes.Blocked;
            if (HolderOfEnd(hoseId, endIdx) != null) return Codes.Blocked;
            return null;
        }

        /// <summary>Unplugs every hose end in the node's sockets (they lie at the socket); a pump keeps its suction hose.</summary>
        public void DetachAll(int nodeId)
        {
            bool changed = false;
            foreach (var h in Slice.Hoses)
            {
                for (int i = 0; i < 2; i++)
                {
                    var tg = h.Ends[i].Target;
                    if (tg.Kind != EndTargetKind.Socket || tg.NodeId != nodeId) continue;
                    if (h.Kind == HoseKind.Suction && i == 0) continue; // fixed, travels with the pump
                    TrySocketPose(tg.NodeId, tg.Socket, tg.Index, out var p);
                    h.Ends[i].RestPose = p;
                    h.Ends[i].Target = EndTarget.None;
                    changed = true;
                }
                if (h.Kind == HoseKind.Suction && h.PumpId == nodeId && h.Ends[1].Target.IsSet)
                {
                    var e = _sim.Placement.Get(nodeId);
                    h.Ends[1].RestPose = e != null ? e.Pose : h.Ends[1].RestPose;
                    h.Ends[1].Target = EndTarget.None;
                    changed = true;
                }
            }
            if (changed || Tank(nodeId) != null) MarkDirtyAndRebuild();
        }

        /// <summary>A pump was set down: its unbound suction end comes along and lies beside it.</summary>
        public void OnNodePlaced(int nodeId)
        {
            var e = _sim.Placement.Get(nodeId);
            if (e == null) return;
            foreach (var h in Slice.Hoses)
                if (h.Kind == HoseKind.Suction && h.PumpId == nodeId && !h.Ends[1].Target.IsSet && HolderOfEnd(h.Id, 1) == null)
                    h.Ends[1].RestPose = e.Pose.Offset(0, 0, 0.7);
            MarkDirtyAndRebuild();
        }

        public void EndHeld(int hoseId, int endIdx, int player) { /* held ends are tracked by Placement's hands record */ }

        public void EndDropped(int hoseId, int endIdx, Pose pose)
        {
            var h = Hose(hoseId);
            if (h == null) return;
            h.Ends[endIdx].RestPose = pose;
            h.Ends[endIdx].Target = EndTarget.None;
        }

        /// <summary>Called when the tank set changes outside requests (spawned IBC, swap).</summary>
        internal void Invalidate() => _dirty = true;
    }
}
