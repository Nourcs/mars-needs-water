using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    // Plain serialisable data so Unity's JsonUtility can fill it from Data/Resources/mvp_tuning.json.
    // Field names follow the Economy GDD's tuning-table columns (snake case kept on purpose).

    /// <summary>The Economy tuning table (Economy Rule 4) plus the interaction ranges (FPC Rule 3).</summary>
    [Serializable]
    public class TuningData
    {
        public double credits_per_litre = 10;
        public double region_multiplier = 1;
        public double contract_multiplier = 1;
        public double delivery_delay_s = 4;
        public int pad_crate_spots = 4;
        public double discharge_hose_cap_Lps = 10;
        public double discharge_hose_m = 20;
        public double suction_hose_m = 6;
        public double link_hose_m = 5;
        public double power_cable_m = 30;
        public int post_sockets = 2;
        public double cargo_capacity_L = 1500;
        public double launch_cycle_s = 15;
        public double countdown_s = 3;
        public double min_launch_L = 0.001;
        public string earth_total_water_L = "1386000000000000000000";

        // Interaction (FPC InteractionTuning; kept here so the simulation's range checks use the same numbers).
        public double eye_height_m = 1.6;
        public double range_default_m = 2;
        public double range_lever_m = 3;
        public double range_crank_m = 1.5;

        public ItemRow[] items = Array.Empty<ItemRow>();
        public SourceRow[] sources = Array.Empty<SourceRow>();
    }

    /// <summary>One shop / tool / machine / tank row.</summary>
    [Serializable]
    public class ItemRow
    {
        public string id = "";
        /// <summary>tool, tank, machine, kit or joke.</summary>
        public string kind = "";
        public string displayName = "";
        public long price_cr;
        public string prereq_id = "";
        public bool unique = true;
        public bool ownedAtStart;
        public double capacity_L;
        public double rate_Lps;
        public double scoopCooldown_s;
        public double pourTime_s;
        public double footprint_m = 0.4;
        /// <summary>Height above the item's pose that range checks aim at (its rim or lid).</summary>
        public double interact_h = 0.5;
        public string jobTitle = "";
        public string realWorldRef = "";
        /// <summary>Extra things in the crate: suction_hose, discharge_hose, link_hose, power_cable, inlet_fitting.</summary>
        public string[] crateContents = Array.Empty<string>();
        /// <summary>Named points on the item in its local frame (sockets, handle, cable port, lever).</summary>
        public ItemPoint[] points = Array.Empty<ItemPoint>();
    }

    /// <summary>A named point on an item: kind is intake, outlet, inlet, link, handle, cable_port or lever.</summary>
    [Serializable]
    public class ItemPoint
    {
        public string kind = "";
        public int index;
        public double x, y, z;
    }

    /// <summary>A water source's designed start volume (copied into state at new game).</summary>
    [Serializable]
    public class SourceRow
    {
        public int id;
        public string type = "";
        public string displayName = "";
        public double startVolume_L;
    }

    /// <summary>Read-only lookups over <see cref="TuningData"/>, with the load checks of Economy Rule 4.</summary>
    public sealed class Tuning
    {
        public TuningData Data { get; }
        readonly Dictionary<string, ItemRow> _items = new Dictionary<string, ItemRow>();
        readonly Dictionary<int, SourceRow> _sources = new Dictionary<int, SourceRow>();

        /// <summary>The linear shop chain in order (items with a price, following prereq links).</summary>
        public IReadOnlyList<ItemRow> ShopChain { get; }

        public Tuning(TuningData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            foreach (var r in data.items ?? Array.Empty<ItemRow>())
            {
                if (string.IsNullOrEmpty(r.id)) throw new TuningException("An item row has no id.");
                if (_items.ContainsKey(r.id)) throw new TuningException($"Duplicate item id '{r.id}'.");
                _items[r.id] = r;
            }
            foreach (var s in data.sources ?? Array.Empty<SourceRow>()) _sources[s.id] = s;
            Validate();
            ShopChain = BuildChain();
        }

        public bool TryItem(string id, out ItemRow row) => _items.TryGetValue(id ?? "", out row);

        public ItemRow Item(string id)
        {
            if (!_items.TryGetValue(id ?? "", out var r)) throw new TuningException($"Unknown item '{id}'.");
            return r;
        }

        public bool TrySource(int id, out SourceRow row) => _sources.TryGetValue(id, out row);
        public IEnumerable<SourceRow> Sources => _sources.Values;
        public IEnumerable<ItemRow> Items => _items.Values;

        /// <summary>The local offset of a named point on an item, or false if the item has none.</summary>
        public bool TryPoint(string itemId, string kind, int index, out ItemPoint p)
        {
            p = null;
            if (!TryItem(itemId, out var r) || r.points == null) return false;
            foreach (var q in r.points)
                if (q.kind == kind && q.index == index) { p = q; return true; }
            return false;
        }

        /// <summary>Number of points of a kind on an item (for example drum inlets = 2).</summary>
        public int PointCount(string itemId, string kind)
        {
            if (!TryItem(itemId, out var r) || r.points == null) return 0;
            int n = 0;
            foreach (var q in r.points) if (q.kind == kind) n++;
            return n;
        }

        void Validate()
        {
            foreach (var r in _items.Values)
            {
                if (r.price_cr < 0) throw new TuningException($"Item '{r.id}' has a negative price.");
                if (!string.IsNullOrEmpty(r.prereq_id) && !_items.ContainsKey(r.prereq_id))
                    throw new TuningException($"Item '{r.id}' requires unknown item '{r.prereq_id}'.");
            }
            foreach (var r in _items.Values)
            {
                var seen = new HashSet<string>();
                var cur = r;
                while (cur != null && !string.IsNullOrEmpty(cur.prereq_id))
                {
                    if (!seen.Add(cur.id)) throw new TuningException($"Prerequisite cycle through '{r.id}'.");
                    cur = _items[cur.prereq_id];
                }
            }
            if (Data.credits_per_litre <= 0 || Data.region_multiplier <= 0 || Data.contract_multiplier <= 0)
                throw new TuningException("Payout multipliers must be > 0.");
            double largestPool = 0;
            double largestHand = 0;
            foreach (var r in _items.Values)
            {
                if (r.kind != "tank") continue;
                if (r.id == "ibc_tank") largestPool += r.capacity_L;
                else largestHand = Math.Max(largestHand, r.capacity_L);
            }
            largestPool += largestHand;
            if (Data.cargo_capacity_L < largestPool)
                throw new TuningException($"Rocket cargo {Data.cargo_capacity_L} L is below the largest pool {largestPool} L.");
            foreach (var s in _sources.Values)
                if (!(s.startVolume_L > 0)) throw new TuningException($"Source {s.id} has start volume {s.startVolume_L}.");
        }

        List<ItemRow> BuildChain()
        {
            // Root = a priced item with no prereq; then follow "whose prereq is me".
            var chain = new List<ItemRow>();
            ItemRow cur = null;
            foreach (var r in _items.Values)
                if (!r.ownedAtStart && string.IsNullOrEmpty(r.prereq_id) && r.price_cr > 0) { cur = r; break; }
            var guard = new HashSet<string>();
            while (cur != null && guard.Add(cur.id))
            {
                chain.Add(cur);
                ItemRow next = null;
                foreach (var r in _items.Values)
                    if (r.prereq_id == cur.id && !r.ownedAtStart) { next = r; break; }
                cur = next;
            }
            return chain;
        }
    }

    /// <summary>Thrown when the tuning table fails its load checks; the game refuses to start.</summary>
    public sealed class TuningException : Exception
    {
        public TuningException(string message) : base(message) { }
    }
}
