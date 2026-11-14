using System;

namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// Pure-C# placement check over authored zones and item footprints (Game State Rule 12). The
    /// controller's ghost calls the same function, so a green ghost is always accepted.
    /// </summary>
    public static class PlacementValidator
    {
        /// <summary>Returns null when the pose is valid ground with no overlap, else a short reason.</summary>
        public static string Check(SimState state, LevelData level, Tuning tuning, string item, Pose pose, int ignoreId)
        {
            bool inWork = false;
            foreach (var z in level.WorkAreas) if (z.Contains(pose.X, pose.Z)) { inWork = true; break; }
            if (!inWork) return "Off the work area";
            foreach (var z in level.NoPlaceAreas) if (z.Contains(pose.X, pose.Z)) return "On water";
            double r = Footprint(tuning, item);
            foreach (var e in state.Placement.Entities.Values)
            {
                if (e.Id == ignoreId || e.HeldBy >= 0) continue;
                double d = e.Pose.FlatDistanceTo(pose);
                if (d < r + Footprint(tuning, e.Item) - 1e-9) return "Something's in the way";
            }
            return null;
        }

        static double Footprint(Tuning t, string item) => t.TryItem(item ?? "", out var row) ? row.footprint_m : 0.3;
    }

    /// <summary>
    /// The Placement module (Game State Rule 12): where every object is, and what each player holds.
    /// Changes through four requests (validated at step 1) and four direct calls from simulation systems.
    /// </summary>
    public sealed class PlacementSystem
    {
        readonly Simulation _sim;
        PlacementSlice Slice => _sim.State.Placement;

        internal PlacementSystem(Simulation sim) { _sim = sim; }

        internal void NewGame()
        {
            Slice.Entities.Clear();
            Slice.Players.Clear();
            Slice.Players[0] = new PlayerRecord { Id = 0, Pose = _sim.Level.PlayerStart, Hands = HandsItem.OfTool("spoon") };
            Spawn(_sim.Level.StarterTankId, _sim.Level.StarterTankItem, EntityKind.Tank, _sim.Level.StorageSpot);
        }

        // ------------- queries -------------

        public PlayerRecord Player(int id) => Slice.Players.TryGetValue(id, out var p) ? p : null;

        /// <summary>Recorded pose + eye height: the origin of every range check.</summary>
        public Pose EyePoint(int player)
        {
            var p = Player(player);
            return p == null ? default : p.Pose.Raised(_sim.Tuning.Data.eye_height_m);
        }

        /// <summary>True when the target point is within <paramref name="range"/> of the player's eye point.</summary>
        public bool InRange(int player, Pose target, double range) => EyePoint(player).DistanceTo(target) <= range + 1e-9;

        /// <summary>The point on an object that range checks measure to (pose raised by the item's interact height).</summary>
        public Pose InteractPoint(Entity e)
        {
            double h = _sim.Tuning.TryItem(e.Item ?? "", out var row) ? row.interact_h : 0.5;
            if (e.Kind == EntityKind.Crate) h = 0.5;
            return e.Pose.Raised(h);
        }

        public Entity Get(int id) => Slice.Entities.TryGetValue(id, out var e) ? e : null;

        /// <summary>The tank standing on the storage spot (the pad tank), or null.</summary>
        public Entity PadTank()
        {
            foreach (var e in Slice.Entities.Values)
                if (e.Kind == EntityKind.Tank && e.HeldBy < 0 && e.Pose.FlatDistanceTo(_sim.Level.StorageSpot) < 0.05) return e;
            return null;
        }

        /// <summary>True when the entity stands on the staging spot.</summary>
        public bool OnStagingSpot(Entity e) => e != null && e.HeldBy < 0 && e.Pose.FlatDistanceTo(_sim.Level.StagingSpot) <= _sim.Level.StagingRadius;

        public HandsItem Hands(int player) => Player(player)?.Hands ?? HandsItem.Empty;

        // ------------- direct calls (Rule 5b) -------------

        /// <summary>Creates an object at a pose.</summary>
        public Entity Spawn(int id, string item, EntityKind kind, Pose pose)
        {
            var e = new Entity { Id = id, Item = item, Kind = kind, Pose = pose };
            Slice.Entities[id] = e;
            return e;
        }

        /// <summary>Removes an object (and clears it from any hands).</summary>
        public void Remove(int id)
        {
            if (!Slice.Entities.TryGetValue(id, out var e)) return;
            if (e.HeldBy >= 0)
            {
                var p = Player(e.HeldBy);
                if (p != null && p.Hands.Kind == HandsKind.Entity && p.Hands.Id == id) SetHands(p.Id, HandsItem.Empty);
            }
            Slice.Entities.Remove(id);
        }

        /// <summary>Moves an object to a pose (it is no longer held).</summary>
        public void Move(int id, Pose pose)
        {
            if (!Slice.Entities.TryGetValue(id, out var e)) return;
            if (e.HeldBy >= 0)
            {
                var p = Player(e.HeldBy);
                if (p != null && p.Hands.Kind == HandsKind.Entity && p.Hands.Id == id) SetHands(p.Id, HandsItem.Empty);
                e.HeldBy = -1;
            }
            e.Pose = pose;
        }

        /// <summary>
        /// Sets a player's hands item. A tool that leaves the hands is stowed (it keeps its water);
        /// emptying the hands brings a stowed tool back (FPC States: Carrying -> Tool(t)).
        /// </summary>
        public void SetHands(int player, HandsItem item)
        {
            var p = Player(player);
            if (p == null) return;
            var cur = p.Hands;
            if (item.Kind == HandsKind.Tool)
            {
                p.StowedTool = null;
                p.Hands = item;
                return;
            }
            if (item.Kind == HandsKind.Empty)
            {
                if (!string.IsNullOrEmpty(p.StowedTool)) { p.Hands = HandsItem.OfTool(p.StowedTool); p.StowedTool = null; }
                else p.Hands = HandsItem.Empty;
                return;
            }
            if (cur.Kind == HandsKind.Tool) p.StowedTool = cur.Tool;
            p.Hands = item;
        }

        // ------------- requests (step 1) -------------

        internal string Apply(Request r)
        {
            switch (r)
            {
                case PlayerPoseRequest pp: return PlayerPose(pp);
                case PickUpRequest pu: return PickUp(pu);
                case DropRequest d: return Drop(d);
                case PlaceItemRequest pi: return PlaceItem(pi);
                case PropSettledRequest ps: return PropSettled(ps);
            }
            return Codes.Unknown;
        }

        string PlayerPose(PlayerPoseRequest r)
        {
            var p = Player(r.Player);
            if (p == null) return Codes.Unknown;
            p.Pose = r.Pose;
            return null;
        }

        string PickUp(PickUpRequest r)
        {
            var player = Player(r.Player);
            if (player == null) return Codes.Unknown;
            double range = _sim.Tuning.Data.range_default_m;
            switch (r.Kind)
            {
                case PickUpKind.Entity:
                {
                    var e = Get(r.Id);
                    if (e == null || e.HeldBy >= 0) return Codes.Unknown;
                    if (!InRange(r.Player, InteractPoint(e), range)) return Codes.TooFar;
                    if (player.Hands.IsCarried) return Codes.HandsFull;
                    if (e.Kind == EntityKind.Crate && _sim.Economy.CrateStageOf(e.Id) != CrateStage.Opened) return Codes.Blocked;
                    if (e.Kind == EntityKind.Tank)
                    {
                        string why = _sim.Network.CanPickUp(e.Id);
                        if (why != null) return why;
                    }
                    if (e.Kind == EntityKind.Pump)
                    {
                        string why = _sim.Pumps.CanPickUp(e.Id);
                        if (why != null) return why;
                    }
                    e.HeldBy = r.Player;
                    SetHands(r.Player, HandsItem.OfEntity(e.Id));
                    if (e.Kind == EntityKind.Tank || e.Kind == EntityKind.Pump) _sim.Network.DetachAll(e.Id);
                    if (e.Kind == EntityKind.Pump) _sim.Pumps.OnPickedUp(e.Id);
                    return null;
                }
                case PickUpKind.HoseEnd:
                {
                    if (!_sim.Network.TryEndPose(r.Id, r.EndIdx, out var pose)) return Codes.Unknown;
                    if (!InRange(r.Player, pose, range)) return Codes.TooFar;
                    if (player.Hands.IsCarried) return Codes.HandsFull;
                    string why = _sim.Network.CanPickUpEnd(r.Id, r.EndIdx);
                    if (why != null) return why;
                    SetHands(r.Player, HandsItem.OfHoseEnd(r.Id, r.EndIdx));
                    _sim.Network.EndHeld(r.Id, r.EndIdx, r.Player);
                    return null;
                }
                case PickUpKind.CableEnd:
                {
                    if (!_sim.Pumps.TryCableEndPose(r.Id, out var pose)) return Codes.Unknown;
                    if (!InRange(r.Player, pose, range)) return Codes.TooFar;
                    if (player.Hands.IsCarried) return Codes.HandsFull;
                    string why = _sim.Pumps.CanPickUpCable(r.Id);
                    if (why != null) return why;
                    SetHands(r.Player, HandsItem.OfCableEnd(r.Id));
                    _sim.Pumps.CableEndHeld(r.Id, r.Player);
                    return null;
                }
            }
            return Codes.Unknown;
        }

        string Drop(DropRequest r)
        {
            var player = Player(r.Player);
            if (player == null) return Codes.Unknown;
            var h = player.Hands;
            switch (h.Kind)
            {
                case HandsKind.Entity:
                {
                    var e = Get(h.Id);
                    if (e == null) { SetHands(r.Player, HandsItem.Empty); return null; }
                    if (PlacementValidator.Check(_sim.State, _sim.Level, _sim.Tuning, e.Item, r.Pose, e.Id) != null) return Codes.Blocked;
                    e.HeldBy = -1;
                    e.Pose = r.Pose;
                    SetHands(r.Player, HandsItem.Empty);
                    return null;
                }
                case HandsKind.HoseEnd:
                    SetHands(r.Player, HandsItem.Empty);
                    _sim.Network.EndDropped(h.Id, h.EndIdx, r.Pose);
                    return null;
                case HandsKind.CableEnd:
                    SetHands(r.Player, HandsItem.Empty);
                    _sim.Pumps.CableEndDropped(h.Id, r.Pose);
                    return null;
            }
            return Codes.NotHolding;
        }

        string PlaceItem(PlaceItemRequest r)
        {
            var player = Player(r.Player);
            if (player == null) return Codes.Unknown;
            if (player.Hands.Kind != HandsKind.Entity) return Codes.NotHolding;
            var e = Get(player.Hands.Id);
            if (e == null) return Codes.Unknown;
            if (!InRange(r.Player, r.Pose, _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
            if (PlacementValidator.Check(_sim.State, _sim.Level, _sim.Tuning, e.Item, r.Pose, e.Id) != null) return Codes.Blocked;
            e.HeldBy = -1;
            e.Pose = r.Pose;
            SetHands(r.Player, HandsItem.Empty);
            if (e.Kind == EntityKind.Pump || e.Kind == EntityKind.Tank) _sim.Network.OnNodePlaced(e.Id);
            return null;
        }

        string PropSettled(PropSettledRequest r)
        {
            var e = Get(r.Id);
            if (e == null || e.HeldBy >= 0) return Codes.Unknown;
            e.Pose = r.Pose;
            return null;
        }
    }
}
