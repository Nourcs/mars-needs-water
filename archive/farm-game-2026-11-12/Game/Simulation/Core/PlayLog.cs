using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarsNeedsWater.Simulation
{
    /// <summary>Where the play log writes lines (a file in the game, a list in tests).</summary>
    public interface IPlayLogSink : IDisposable
    {
        void WriteLine(string line);
        void Flush();
    }

    /// <summary>
    /// The play log (Game State Rule 10): one CSV row per domain event, flushed as it happens, invariant culture.
    /// Write-only; nothing in the game reads it. If writing fails, one warning and logging turns off.
    /// </summary>
    public sealed class PlayLog : ISimEventListener, IDisposable
    {
        public const string Header = "session_id,wall_utc,session_time_s,tick,event,detail,value,credits_after,lake_drained_pct,since_prev_same_s";

        readonly IPlayLogSink _sink;
        readonly ISimLog _log;
        readonly Simulation _sim;
        readonly Func<DateTime> _clock;
        readonly Dictionary<string, double> _prev = new Dictionary<string, double>();
        bool _enabled = true;
        bool _headerWritten;

        public bool Enabled => _enabled;

        public PlayLog(Simulation sim, IPlayLogSink sink, ISimLog log = null, Func<DateTime> wallClock = null)
        {
            _sim = sim;
            _sink = sink;
            _log = log ?? new NullSimLog();
            _clock = wallClock ?? (() => DateTime.UtcNow);
        }

        public void OnSimEvent(in SimEvent e, SimState state)
        {
            if (!_enabled || _sink == null) return;
            try
            {
                if (!_headerWritten) { _sink.WriteLine(Header); _headerWritten = true; }
                var inv = CultureInfo.InvariantCulture;
                double t = state.Session.SessionTimeS;
                string detail = Detail(e);
                string key = e.Type + "|" + (e.Type == SimEventType.DrawdownThreshold ? e.Id.ToString(inv) : "");
                string since = "";
                if (e.Type == SimEventType.DrawdownThreshold)
                {
                    // Multiples of 10 measure from the previous multiple-of-10 row (or session start).
                    bool ten = Math.Abs(e.Value % 10) < 1e-9;
                    if (ten)
                    {
                        string k10 = key + "|10";
                        since = (_prev.TryGetValue(k10, out var p10) ? t - p10 : t).ToString("0.0", inv);
                        _prev[k10] = t;
                    }
                    else if (_prev.TryGetValue(key, out var p)) since = (t - p).ToString("0.0", inv);
                    _prev[key] = t;
                }
                else
                {
                    if (_prev.TryGetValue(key, out var p)) since = (t - p).ToString("0.0", inv);
                    _prev[key] = t;
                }
                double lakePct = 0;
                foreach (var s in state.WaterSource.Sources)
                    if (s.Kind == SourceKind.Lake) { lakePct = (s.Start - s.Remaining) / s.Start * 100.0; break; }
                string line = string.Join(",",
                    Csv(state.Session.SessionId),
                    _clock().ToString("o", inv),
                    t.ToString("0.0", inv),
                    e.Tick.ToString(inv),
                    e.Type.ToString(),
                    Csv(detail),
                    e.Value.ToString("0.######", inv),
                    (state.Economy.CreditsMc / 1000.0).ToString("0.###", inv),
                    lakePct.ToString("0.####", inv),
                    since);
                _sink.WriteLine(line);
                _sink.Flush();
            }
            catch (Exception ex)
            {
                _enabled = false;
                _log.Warn($"Play log could not be written ({ex.Message}); logging is off for this session.");
            }
        }

        static string Detail(in SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.RequestRejected: return $"{e.Text2}:{e.Text}";
                case SimEventType.Purchased: return $"{e.Text} crate {e.Id}";
                case SimEventType.PumpStateChanged: return $"pump {e.Id} {e.Text}->{e.Text2}";
                case SimEventType.SourceDrained: return $"source {e.Id}";
                case SimEventType.PoolFull: return $"pool {e.Id}";
                case SimEventType.DrawdownThreshold: return $"source {e.Id}";
                case SimEventType.Reveal: return $"reveal {e.Id}";
                default: return e.Text ?? "";
            }
        }

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public void Dispose()
        {
            try { _sink?.Flush(); _sink?.Dispose(); } catch { /* closing a broken log is not an error */ }
            _enabled = false;
        }
    }
}
