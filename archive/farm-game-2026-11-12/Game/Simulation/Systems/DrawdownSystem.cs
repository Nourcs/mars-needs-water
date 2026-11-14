using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// Drawdown (simulation half): thresholds and reveals passed, checked in litres at tick step 2e.
    /// The waterline height curve and stain bands are presentation (Drawdown Rules 1, 5).
    /// </summary>
    public sealed class DrawdownSystem
    {
        /// <summary>Litre tolerance for threshold checks (Drawdown Rule 4).</summary>
        public const double Epsilon = 0.001;

        readonly Simulation _sim;
        DrawdownSlice Slice => _sim.State.Drawdown;

        internal DrawdownSystem(Simulation sim) { _sim = sim; }

        internal void NewGame()
        {
            Slice.Passed.Clear();
            Slice.Reveals.Clear();
            foreach (var s in _sim.Level.Sources) Slice.Passed[s.Id] = new List<double>();
        }

        public bool IsPassed(int source, double pct) => Slice.Passed.TryGetValue(source, out var l) && l.Contains(pct);

        internal void Step()
        {
            foreach (var def in _sim.Level.Sources)
            {
                if (def.Thresholds.Count == 0 && def.Reveals.Count == 0) continue; // nothing to watch (world seas and lakes)
                if (!_sim.WaterSource.Exists(def.Id)) continue;
                double start = _sim.WaterSource.Start(def.Id);
                double rem = _sim.WaterSource.Available(def.Id);
                if (!Slice.Passed.TryGetValue(def.Id, out var passed)) Slice.Passed[def.Id] = passed = new List<double>();
                var sorted = new List<double>(def.Thresholds);
                sorted.Sort();
                foreach (var t in sorted)
                {
                    if (passed.Contains(t)) continue;
                    if (rem <= start * (1 - t / 100.0) + Epsilon)
                    {
                        passed.Add(t);
                        _sim.Raise(new SimEvent { Type = SimEventType.DrawdownThreshold, Id = def.Id, Value = t });
                    }
                }
                var reveals = new List<RevealDef>(def.Reveals);
                reveals.Sort((a, b) => a.Id.CompareTo(b.Id));
                foreach (var rv in reveals)
                {
                    if (Slice.Reveals.Contains(rv.Id)) continue;
                    if (rem <= start * (1 - rv.Pct / 100.0) + Epsilon)
                    {
                        Slice.Reveals.Add(rv.Id);
                        _sim.Raise(new SimEvent { Type = SimEventType.Reveal, Id = rv.Id, Value = rv.Pct });
                    }
                }
            }
        }
    }
}
