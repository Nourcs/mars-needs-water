using System;

namespace MarsNeedsWater.Simulation
{
    /// <summary>
    /// Fixed-step accumulator (Game State formula simulation_step). Fed with unscaled real frame time;
    /// independent of frame rate, physics and Time.timeScale.
    /// </summary>
    public sealed class SimClock
    {
        /// <summary>Tolerance so floating-point sums of frame times don't lose a tick.</summary>
        public const double Epsilon = 1e-9;
        /// <summary>Hard cap on ticks per frame.</summary>
        public const int MaxTicksPerFrame = 5;

        /// <summary>Seconds per tick (default 0.1 = 10 Hz).</summary>
        public double Dt { get; private set; }
        /// <summary>Most simulated time made up in one frame.</summary>
        public double MaxCatchUp { get; private set; }
        /// <summary>Time owed to the simulation.</summary>
        public double Acc { get; private set; }
        /// <summary>Interpolation factor for visuals between the last two ticks, in [0, 1).</summary>
        public double Alpha => Dt > 0 ? Math.Min(Acc / Dt, 0.999999) : 0;

        public SimClock(double dt = 0.1, double maxCatchUp = 0.5, ISimLog log = null)
        {
            Configure(dt, maxCatchUp, log);
        }

        /// <summary>Applies the knob clamps from Game State Edge Cases (dt 0.033-0.2, maxCatchUp [max(dt,0.2), 1.0]).</summary>
        public void Configure(double dt, double maxCatchUp, ISimLog log = null)
        {
            log ??= new NullSimLog();
            double cdt = Clamp(dt, 0.033, 0.2);
            if (cdt != dt) log.Warn($"dt {dt} outside 0.033-0.2 s; clamped to {cdt}.");
            double lo = Math.Max(cdt, 0.2);
            double cmc = Clamp(maxCatchUp, lo, 1.0);
            if (cmc != maxCatchUp) log.Warn($"maxCatchUp {maxCatchUp} outside [{lo}, 1.0] s; clamped to {cmc}.");
            Dt = cdt;
            MaxCatchUp = cmc;
            Acc = 0;
        }

        /// <summary>Resets the accumulator (on boot and on every resume).</summary>
        public void Reset() => Acc = 0;

        /// <summary>
        /// Adds one frame of real time and returns how many ticks to run now (0 to 5).
        /// </summary>
        public int Advance(double frameDt)
        {
            if (double.IsNaN(frameDt) || frameDt < 0) frameDt = 0;
            Acc = Math.Min(Acc + frameDt, MaxCatchUp);
            int n = 0;
            while (Acc >= Dt - Epsilon && n < MaxTicksPerFrame)
            {
                n++;
                Acc = Math.Max(Acc - Dt, 0);
            }
            if (n == MaxTicksPerFrame) Acc = Math.Min(Acc, Dt - Epsilon);
            return n;
        }

        /// <summary>Converts seconds to whole ticks (Game State Rule 8): max(1, round(seconds / dt)).</summary>
        public static int SecondsToTicks(double seconds, double dt) => Math.Max(1, (int)Math.Round(seconds / dt, MidpointRounding.AwayFromZero));

        static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : (v < lo ? lo : (v > hi ? hi : v));
    }
}
