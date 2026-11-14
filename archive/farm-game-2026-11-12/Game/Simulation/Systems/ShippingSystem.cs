using System;

namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// Shipping &amp; Launch: the LAUNCH lever loads the pad pool instantly, a 3 s countdown, liftoff credits the
    /// launch (step 2c), and a new rocket lands 12 s later. Litres aboard and shipped are whole microlitres.
    /// </summary>
    public sealed class ShippingSystem
    {
        readonly Simulation _sim;
        ShippingSlice Slice => _sim.State.Shipping;

        internal ShippingSystem(Simulation sim) { _sim = sim; }

        internal void NewGame()
        {
            Slice.State = PadState.Ready;
            Slice.TimerTicks = 0;
            Slice.AboardUl = 0;
            Slice.ShippedUl = 0;
            Slice.Launches = 0;
        }

        /// <summary>Seconds until the pad is ready again (0 when Ready).</summary>
        public double SecondsUntilReady()
        {
            if (Slice.State == PadState.Ready) return 0;
            int ticks = Slice.TimerTicks;
            if (Slice.State == PadState.Countdown) ticks += Turnaround();
            return ticks * _sim.Dt;
        }

        int Countdown() => SimClock.SecondsToTicks(_sim.Tuning.Data.countdown_s, _sim.Dt);
        int Cycle() => SimClock.SecondsToTicks(_sim.Tuning.Data.launch_cycle_s, _sim.Dt);
        int Turnaround() => Math.Max(1, Cycle() - Countdown());

        internal string Apply(Request r)
        {
            if (!(r is LaunchRequest)) return Codes.Unknown;
            var d = _sim.Tuning.Data;
            if (!_sim.Placement.InRange(r.Player, _sim.Level.LaunchLever, d.range_lever_m)) return Codes.TooFar;
            if (Slice.State != PadState.Ready) return Codes.PadBusy;
            var pool = _sim.Network.PadPool();
            double contents = pool?.Contents ?? 0;
            if (contents < d.min_launch_L) return Codes.NothingToShip;
            // Request whole microlitres so litres aboard equal exactly what is shipped (Shipping Rule 12).
            double want = Math.Min(contents, d.cargo_capacity_L);
            long wantUl = (long)Math.Floor(want * 1e6 + 1e-9);
            double taken = _sim.Network.Take(pool, wantUl / 1e6);
            long takenUl = (long)Math.Round(taken * 1e6);
            Slice.AboardUl = takenUl;
            Slice.State = PadState.Countdown;
            Slice.TimerTicks = Countdown();
            _sim.Raise(new SimEvent { Type = SimEventType.LaunchStarted, Player = r.Player, Value = takenUl / 1e6 });
            return null;
        }

        /// <summary>Step 2c: advance the one timer; at liftoff credit the launch in the same step.</summary>
        internal void Step(bool startedThisTick)
        {
            if (Slice.State == PadState.Ready) return;
            if (startedThisTick) return; // a timer is not decremented in the tick it starts
            Slice.TimerTicks--;
            if (Slice.TimerTicks > 0) return;
            if (Slice.State == PadState.Countdown)
            {
                long ul = Slice.AboardUl;
                Slice.ShippedUl += ul;
                Slice.AboardUl = 0;
                Slice.Launches++;
                _sim.Economy.CreditLaunch(ul / 1e6);
                Slice.State = PadState.Away;
                Slice.TimerTicks = Turnaround();
                _sim.Raise(new SimEvent { Type = SimEventType.LaunchCompleted, Value = ul / 1e6 });
            }
            else
            {
                Slice.State = PadState.Ready;
                Slice.TimerTicks = 0;
            }
        }
    }
}
