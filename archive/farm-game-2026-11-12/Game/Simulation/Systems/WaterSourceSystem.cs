using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    public enum SourceCondition { Full, Draining, Low, Drained }

    /// <summary>
    /// Water Source Model: the only way water leaves a source is <see cref="Withdraw"/>.
    /// Raises SourceDrained at tick step 2d. No deposit API (water never returns).
    /// </summary>
    public sealed class WaterSourceSystem
    {
        /// <summary>A leftover this small is granted to the caller (drainedEpsilon).</summary>
        public const double DrainedEpsilon = 1e-6;
        /// <summary>Low threshold as a fraction of start (unused in the MVP).</summary>
        public const double LowFraction = 0.10;

        readonly Simulation _sim;
        WaterSourceSlice Slice => _sim.State.WaterSource;

        // Per-tick transients (Rule 8), never saved.
        readonly List<int> _drainedThisTick = new List<int>();
        readonly Dictionary<int, double> _lastGrant = new Dictionary<int, double>();

        internal WaterSourceSystem(Simulation sim) { _sim = sim; }

        /// <summary>New game: copy start volumes from the tuning table into state (Rule 1, 13).</summary>
        internal void NewGame()
        {
            Slice.Sources.Clear();
            var rows = new List<SourceRow>(_sim.Tuning.Sources);
            rows.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var r in rows)
            {
                if (!(r.startVolume_L > 0) || double.IsInfinity(r.startVolume_L))
                    throw new TuningException($"Source {r.id} start volume {r.startVolume_L} is invalid.");
                Slice.Sources.Add(new SourceState
                {
                    Id = r.id,
                    Kind = r.type == "Puddle" ? SourceKind.Puddle : SourceKind.Lake,
                    Start = r.startVolume_L,
                    Remaining = r.startVolume_L,
                });
            }
            // Sources the level defines with their own volume (the world's seas and lakes).
            foreach (var def in _sim.Level.Sources)
            {
                if (!(def.StartVolume_L > 0) || Find(def.Id) != null) continue;
                if (double.IsInfinity(def.StartVolume_L)) throw new TuningException($"Source {def.Id} start volume is invalid.");
                Slice.Sources.Add(new SourceState { Id = def.Id, Kind = SourceKind.Lake, Start = def.StartVolume_L, Remaining = def.StartVolume_L });
            }
            Slice.Sources.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        SourceState Find(int id)
        {
            var list = Slice.Sources;
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i];
            return null;
        }

        /// <summary>Litres remaining (never negative). Unknown id returns 0 and logs.</summary>
        public double Available(int id)
        {
            var s = Find(id);
            if (s == null) { _sim.Log.Error($"Unknown source {id}."); return 0; }
            return s.Remaining;
        }

        /// <summary>The start copy for this save.</summary>
        public double Start(int id)
        {
            var s = Find(id);
            if (s == null) { _sim.Log.Error($"Unknown source {id}."); return 0; }
            return s.Start;
        }

        /// <summary>(start − remaining) / start × 100.</summary>
        public double DrawdownPct(int id)
        {
            var s = Find(id);
            if (s == null) { _sim.Log.Error($"Unknown source {id}."); return 0; }
            return Math.Max(s.Start - s.Remaining, s.Extracted) / s.Start * 100.0;
        }

        /// <summary>Litres taken from a source so far (exact even for an ocean).</summary>
        public double Extracted(int id)
        {
            var s = Find(id);
            if (s == null) { _sim.Log.Error($"Unknown source {id}."); return 0; }
            return s.Extracted;
        }

        /// <summary>Derived state. Unknown id reads as Drained so scoops are rejected.</summary>
        public SourceCondition State(int id)
        {
            var s = Find(id);
            if (s == null) { _sim.Log.Error($"Unknown source {id}."); return SourceCondition.Drained; }
            if (s.Drained) return SourceCondition.Drained;
            if (s.Remaining >= s.Start) return SourceCondition.Full;
            if (s.Remaining <= s.Start * LowFraction) return SourceCondition.Low;
            return SourceCondition.Draining;
        }

        /// <summary>True when the id names a source in this level.</summary>
        public bool Exists(int id) => Find(id) != null;

        /// <summary>
        /// Takes up to <paramref name="amount"/> litres. Returns the litres granted (may exceed the request by
        /// at most <see cref="DrainedEpsilon"/> when it empties the source, so no water is ever deleted).
        /// </summary>
        public double Withdraw(int id, double amount)
        {
            if (amount == 0) return 0;
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0)
            {
                _sim.Log.Warn($"Withdraw({id}, {amount}) ignored: amount must be a finite positive number.");
                return 0;
            }
            var s = Find(id);
            if (s == null) { _sim.Log.Error($"Unknown source {id}."); return 0; }
            if (s.Drained) return 0;
            double granted = Math.Min(amount, s.Remaining);
            if (s.Remaining - granted <= DrainedEpsilon) granted = s.Remaining;
            s.Remaining -= granted;
            s.Extracted += granted;
            if (s.Remaining <= 0)
            {
                s.Remaining = 0;
                s.Drained = true;
                _lastGrant[id] = granted;
                _drainedThisTick.Add(id);
            }
            return granted;
        }

        /// <summary>Tick step 2d: raise SourceDrained once per source drained this tick, in id order.</summary>
        internal void Step()
        {
            if (_drainedThisTick.Count == 0) return;
            _drainedThisTick.Sort();
            foreach (var id in _drainedThisTick)
                _sim.Raise(new SimEvent { Type = SimEventType.SourceDrained, Id = id, Value = _lastGrant.TryGetValue(id, out var g) ? g : 0 });
            _drainedThisTick.Clear();
            _lastGrant.Clear();
        }

        internal void ClearTransients() { _drainedThisTick.Clear(); _lastGrant.Clear(); }

        /// <summary>Sum of all remaining water (for conservation checks).</summary>
        public double TotalRemaining()
        {
            double t = 0;
            foreach (var s in Slice.Sources) t += s.Remaining;
            return t;
        }
    }
}
