using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    public enum ShopItemState { Locked, Visible, Affordable, Owned }

    /// <summary>
    /// Economy, Progression &amp; Shop: credits (whole millicredits), the linear shop, ORDER, crates
    /// (Inbound -> Landed -> Opened, step 2f) and the launch payout.
    /// </summary>
    public sealed class EconomySystem
    {
        readonly Simulation _sim;
        EconomySlice Slice => _sim.State.Economy;

        // Crates ordered this tick: their timer is not decremented in the tick it starts (Game State Rule 3).
        readonly HashSet<int> _orderedThisTick = new HashSet<int>();

        internal EconomySystem(Simulation sim) { _sim = sim; }

        internal void NewGame()
        {
            Slice.CreditsMc = 0;
            Slice.Owned.Clear();
            Slice.Crates.Clear();
            _orderedThisTick.Clear();
            foreach (var r in _sim.Tuning.Items) if (r.ownedAtStart) Slice.Owned.Add(r.id);
        }

        /// <summary>Whole credits (floor of millicredits).</summary>
        public long Credits => Slice.CreditsMc / 1000;

        public bool Owns(string item) => Slice.Owned.Contains(item);

        /// <summary>Pure payout in millicredits: round(litres × cr/L × multipliers × 1000).</summary>
        public long PayoutFor(double litres)
        {
            if (double.IsNaN(litres) || double.IsInfinity(litres) || litres <= 0) return 0;
            var d = _sim.Tuning.Data;
            return (long)Math.Round(litres * d.credits_per_litre * d.region_multiplier * d.contract_multiplier * 1000.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Called by Shipping at liftoff (step 2c). Ignores non-finite or non-positive litres.</summary>
        public void CreditLaunch(double litres)
        {
            if (double.IsNaN(litres) || double.IsInfinity(litres) || litres <= 0)
            {
                _sim.Log.Warn($"CreditLaunch({litres}) ignored.");
                return;
            }
            Slice.CreditsMc += PayoutFor(litres);
        }

        /// <summary>Earth remaining = max(1 L, earth total − shipped), as an exact decimal. Derived, never stored.</summary>
        public decimal EarthRemainingL()
        {
            decimal total = decimal.Parse(_sim.Tuning.Data.earth_total_water_L, System.Globalization.CultureInfo.InvariantCulture);
            decimal shipped = _sim.State.Shipping.ShippedUl / 1_000_000m;
            decimal r = total - shipped;
            return r < 1m ? 1m : r;
        }

        public ShopItemState StateOf(string item)
        {
            var row = _sim.Tuning.Item(item);
            if (Owns(item)) return ShopItemState.Owned;
            if (!string.IsNullOrEmpty(row.prereq_id) && !Owns(row.prereq_id)) return ShopItemState.Locked;
            return Credits >= row.price_cr ? ShopItemState.Affordable : ShopItemState.Visible;
        }

        /// <summary>The first not-owned item in the shop chain, or null when the catalogue is complete.</summary>
        public ItemRow NextItem()
        {
            foreach (var r in _sim.Tuning.ShopChain) if (!Owns(r.id)) return r;
            return null;
        }

        /// <summary>Progress toward the next item, 0..1.</summary>
        public double NextItemProgress()
        {
            var n = NextItem();
            if (n == null || n.price_cr <= 0) return 1;
            return Math.Min(1, (double)Credits / n.price_cr);
        }

        /// <summary>The job title from the latest purchase.</summary>
        public string JobTitle()
        {
            for (int i = Slice.Owned.Count - 1; i >= 0; i--)
                if (_sim.Tuning.TryItem(Slice.Owned[i], out var r) && !string.IsNullOrEmpty(r.jobTitle)) return r.jobTitle;
            return "Probationary Hydration Intern";
        }

        public CrateRecord Crate(int id)
        {
            foreach (var c in Slice.Crates) if (c.Id == id) return c;
            return null;
        }

        /// <summary>Crate stage, or Opened for id 0 (items owned at start have no crate).</summary>
        public CrateStage CrateStageOf(int crateId)
        {
            var c = Crate(crateId);
            return c == null ? CrateStage.Opened : c.Stage;
        }

        public bool CrateOpened(int crateId) => CrateStageOf(crateId) == CrateStage.Opened;

        /// <summary>True when some crate carrying this item has been opened.</summary>
        public bool ItemCrateOpened(string item)
        {
            foreach (var c in Slice.Crates) if (c.Item == item && c.Stage == CrateStage.Opened) return true;
            return false;
        }

        // ---------------- requests (step 1) ----------------

        internal string Apply(Request r)
        {
            switch (r)
            {
                case OrderRequest o: return Order(o);
                case OpenCrateRequest c: return OpenCrate(c);
            }
            return Codes.Unknown;
        }

        string Order(OrderRequest r)
        {
            if (!_sim.Placement.InRange(r.Player, _sim.Level.OrderLever, _sim.Tuning.Data.range_lever_m)) return Codes.TooFar;
            if (!_sim.Tuning.TryItem(r.Item, out var row)) return Codes.Unknown;
            if (row.unique && Owns(row.id)) return Codes.AlreadyOwned;
            if (!string.IsNullOrEmpty(row.prereq_id) && !Owns(row.prereq_id)) return Codes.Locked;
            if (Credits < row.price_cr) return Codes.CannotAfford;

            Slice.CreditsMc -= row.price_cr * 1000;
            if (row.unique) Slice.Owned.Add(row.id);
            int crateId = _sim.NewId();
            var crate = new CrateRecord
            {
                Id = crateId,
                Item = row.id,
                Stage = CrateStage.Inbound,
                TicksLeft = SimClock.SecondsToTicks(_sim.Tuning.Data.delivery_delay_s, _sim.Dt),
                Buyer = r.Player,
            };
            Slice.Crates.Add(crate);
            _orderedThisTick.Add(crateId);
            // Grant to the owning system in the same step (Game State 5b, AC-E20). Tanks get no Grant.
            if (row.kind == "tool") _sim.HandTools.Grant(r.Player, row.id);
            else if (row.kind == "machine") _sim.Pumps.Grant(row.id, crateId);
            _sim.Raise(new SimEvent { Type = SimEventType.Purchased, Player = r.Player, Text = row.id, Id = crateId });
            _sim.RequestSave();
            return null;
        }

        string OpenCrate(OpenCrateRequest r)
        {
            var c = Crate(r.Crate);
            var e = _sim.Placement.Get(r.Crate);
            if (c == null || e == null) return Codes.Unknown;
            if (!_sim.Placement.InRange(r.Player, _sim.Placement.InteractPoint(e), _sim.Tuning.Data.range_default_m)) return Codes.TooFar;
            if (c.Stage == CrateStage.Opened) return Codes.AlreadyOpen;
            if (c.Stage != CrateStage.Landed) return Codes.Unknown;
            c.Stage = CrateStage.Opened;
            Unbox(c, r.Player);
            return null;
        }

        void Unbox(CrateRecord c, int player)
        {
            var row = _sim.Tuning.Item(c.Item);
            var spot = c.Spot >= 0 && c.Spot < _sim.Level.CrateSpots.Count ? _sim.Level.CrateSpots[c.Spot] : new CrateSpot(_sim.Level.StagingSpot, _sim.Level.StagingSpot);
            Pose itemPose = spot.ItemPose;
            switch (row.kind)
            {
                case "tool":
                    _sim.Pumps.ReleaseCrank(player);
                    if (_sim.Placement.Hands(player).IsCarried)
                    {
                        // Hands full: the tool stays usable and can be selected later.
                    }
                    else _sim.Placement.SetHands(player, HandsItem.OfTool(row.id));
                    break;
                case "tank":
                {
                    bool ibc = row.id == "ibc_tank";
                    int id = _sim.NewId();
                    _sim.Placement.Spawn(id, row.id, EntityKind.Tank, ibc ? _sim.Level.IbcSpot : itemPose);
                    _sim.Network.RegisterTank(id, row.id, installed: ibc);
                    foreach (var extra in row.crateContents) SpawnExtra(extra, itemPose, 0);
                    _sim.Network.Invalidate();
                    break;
                }
                case "machine":
                {
                    var pump = FindPumpByCrate(c.Id);
                    if (pump == null) break;
                    _sim.Placement.Spawn(pump.Id, row.id, EntityKind.Pump, itemPose);
                    foreach (var extra in row.crateContents) SpawnExtra(extra, itemPose, pump.Id);
                    if (pump.HasCable)
                    {
                        pump.CableEndPose = itemPose.Offset(0.8, 0, 0);
                        pump.HasCableEndPose = true;
                    }
                    break;
                }
                case "kit":
                    _sim.Placement.Spawn(_sim.NewId(), row.id, EntityKind.MotorKit, itemPose);
                    break;
                default:
                    _sim.Placement.Spawn(_sim.NewId(), row.id, EntityKind.Prop, itemPose);
                    break;
            }
        }

        PumpState FindPumpByCrate(int crateId)
        {
            foreach (var p in _sim.State.Pumps.Pumps) if (p.CrateId == crateId) return p;
            return null;
        }

        void SpawnExtra(string extra, Pose near, int pumpId)
        {
            switch (extra)
            {
                case "suction_hose":
                    _sim.Network.CreateHose(_sim.NewId(), HoseKind.Suction, near.Offset(0, 0, 0.6), pumpId);
                    break;
                case "discharge_hose":
                    _sim.Network.CreateHose(_sim.NewId(), HoseKind.Discharge, near.Offset(-0.7, 0, 0));
                    break;
                case "link_hose":
                    _sim.Network.CreateHose(_sim.NewId(), HoseKind.Link, near.Offset(-0.7, 0, 0));
                    break;
                case "inlet_fitting":
                    _sim.Placement.Spawn(_sim.NewId(), "inlet_fitting", EntityKind.InletFitting, near.Offset(0.7, 0, 0));
                    break;
            }
        }

        // ---------------- step 2f ----------------

        internal void Step()
        {
            foreach (var c in Slice.Crates)
            {
                if (c.Stage != CrateStage.Inbound || _orderedThisTick.Contains(c.Id)) continue;
                if (c.TicksLeft > 0) { c.TicksLeft--; if (c.TicksLeft > 0) continue; }
                int spot = FreeSpot();
                if (spot < 0) continue; // waits at 0 until a spot frees
                // The landing drone carries off an empty crate on that spot.
                foreach (var old in Slice.Crates)
                    if (old.Spot == spot && old.Stage == CrateStage.Opened && _sim.Placement.Get(old.Id) != null)
                        _sim.Placement.Remove(old.Id);
                c.Spot = spot;
                c.Stage = CrateStage.Landed;
                c.LandedTick = _sim.State.Session.Tick;
                _sim.Placement.Spawn(c.Id, c.Item, EntityKind.Crate, _sim.Level.CrateSpots[spot].Spot);
            }
            _orderedThisTick.Clear();
        }

        int FreeSpot()
        {
            int n = Math.Min(_sim.Tuning.Data.pad_crate_spots, _sim.Level.CrateSpots.Count);
            for (int i = 0; i < n; i++)
            {
                bool taken = false;
                foreach (var c in Slice.Crates)
                    if (c.Spot == i && c.Stage == CrateStage.Landed) { taken = true; break; }
                if (!taken) return i;
            }
            return -1;
        }
    }
}
