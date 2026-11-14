using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// Hand Tools: water held per tool, and the SelectTool / Scoop / Pour requests (applied at step 1).
    /// No tick step of its own.
    /// </summary>
    public sealed class HandToolsSystem
    {
        /// <summary>A held amount at or below this counts as Empty.</summary>
        public const double EmptyEpsilon = 1e-6;

        readonly Simulation _sim;
        HandToolsSlice Slice => _sim.State.HandTools;

        internal HandToolsSystem(Simulation sim) { _sim = sim; }

        internal void NewGame()
        {
            Slice.Held.Clear();
            var mine = new Dictionary<string, double>();
            foreach (var row in _sim.Tuning.Items)
                if (row.kind == "tool" && row.ownedAtStart) mine[row.id] = 0;
            Slice.Held[0] = mine;
        }

        Dictionary<string, double> Tools(int player)
        {
            if (!Slice.Held.TryGetValue(player, out var d)) { d = new Dictionary<string, double>(); Slice.Held[player] = d; }
            return d;
        }

        /// <summary>Litres held in a player's tool (0 if not owned).</summary>
        public double Held(int player, string tool) => Tools(player).TryGetValue(tool ?? "", out var v) ? v : 0;

        public bool Owns(int player, string tool) => Tools(player).ContainsKey(tool ?? "");

        public double Capacity(string tool) => _sim.Tuning.TryItem(tool, out var r) ? r.capacity_L : 0;

        /// <summary>Full when held ≥ capacity.</summary>
        public bool IsFull(int player, string tool) => Held(player, tool) >= Capacity(tool);

        /// <summary>Usable = owned and (owned at start, or its crate is Opened).</summary>
        public bool Usable(int player, string tool)
        {
            if (!Owns(player, tool)) return false;
            if (_sim.Tuning.TryItem(tool, out var r) && r.ownedAtStart) return true;
            return _sim.Economy.ItemCrateOpened(tool);
        }

        /// <summary>Economy calls this in the same step 1 as the purchase (ownership only, 0 L).</summary>
        public void Grant(int player, string tool)
        {
            var d = Tools(player);
            if (!d.ContainsKey(tool)) d[tool] = 0;
        }

        /// <summary>Sum of all held water (for conservation checks).</summary>
        public double TotalHeld()
        {
            double t = 0;
            foreach (var p in Slice.Held.Values) foreach (var v in p.Values) t += v;
            return t;
        }

        internal string Apply(Request r)
        {
            switch (r)
            {
                case SelectToolRequest s: return SelectTool(s);
                case ScoopRequest s: return Scoop(s);
                case PourRequest p: return Pour(p);
            }
            return Codes.Unknown;
        }

        string SelectTool(SelectToolRequest r)
        {
            if (!Owns(r.Player, r.Tool)) return Codes.NotOwned;
            if (!Usable(r.Player, r.Tool)) return Codes.CrateUnopened;
            var hands = _sim.Placement.Hands(r.Player);
            if (hands.IsCarried) return Codes.HandsFull;
            if (hands.Kind == HandsKind.Tool && hands.Tool == r.Tool) return null;
            _sim.Placement.SetHands(r.Player, HandsItem.OfTool(r.Tool));
            return null;
        }

        string Scoop(ScoopRequest r)
        {
            var range = _sim.Tuning.Data.range_default_m;
            var src = _sim.Level.Sources.Find(s => s.Id == r.Source);
            if (src == null) return Codes.Unknown;
            if (!src.Anywhere && (!src.ScoopArea.Contains(r.AimPoint.X, r.AimPoint.Z) || !_sim.Placement.InRange(r.Player, r.AimPoint, range))) return Codes.TooFar;
            var hands = _sim.Placement.Hands(r.Player);
            if (hands.Kind != HandsKind.Tool || hands.Tool != r.Tool) return Codes.NotInHand;
            double held = Held(r.Player, r.Tool), cap = Capacity(r.Tool);
            if (held >= cap) return Codes.ToolFull;
            if (_sim.WaterSource.State(r.Source) == SourceCondition.Drained) return Codes.SourceDry;
            double got = _sim.WaterSource.Withdraw(r.Source, cap - held);
            Tools(r.Player)[r.Tool] = held + got;
            return null;
        }

        string Pour(PourRequest r)
        {
            var range = _sim.Tuning.Data.range_default_m;
            if (_sim.WaterSource.Exists(r.Container))
            {
                // Defensive: Use on a source always sends Scoop.
                var hands0 = _sim.Placement.Hands(r.Player);
                if (hands0.Kind != HandsKind.Tool || hands0.Tool != r.Tool) return Codes.NotInHand;
                return Codes.CannotDeposit;
            }
            var target = _sim.Placement.Get(r.Container);
            if (target == null) return Codes.Unknown;
            if (!_sim.Placement.InRange(r.Player, _sim.Placement.InteractPoint(target), range)) return Codes.TooFar;
            var hands = _sim.Placement.Hands(r.Player);
            if (hands.Kind != HandsKind.Tool || hands.Tool != r.Tool) return Codes.NotInHand;
            double held = Held(r.Player, r.Tool);
            if (held <= EmptyEpsilon) return Codes.ToolEmpty;
            string why = _sim.Network.PourInto(r.Container, held, out double poured);
            if (why != null) return why;
            Tools(r.Player)[r.Tool] = held - poured;
            return null;
        }
    }
}
