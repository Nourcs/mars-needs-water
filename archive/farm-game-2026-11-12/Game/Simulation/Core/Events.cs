using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    /// <summary>The 14 MVP domain events (Game State Rule 7).</summary>
    public enum SimEventType
    {
        SessionStarted,
        SessionEnded,
        Paused,
        Resumed,
        Saved,
        RequestRejected,
        Purchased,
        LaunchStarted,
        LaunchCompleted,
        SourceDrained,
        PoolFull,
        PumpStateChanged,
        DrawdownThreshold,
        Reveal,
    }

    /// <summary>
    /// One domain event. A single flat struct so raising events never allocates.
    /// Field meaning depends on <see cref="Type"/>:
    /// RequestRejected: Player, Text = reason code, Text2 = request type.
    /// Purchased: Player, Text = item id, Id = crate id.
    /// LaunchStarted / LaunchCompleted: Value = litres.
    /// SourceDrained: Id = source id, Value = last grant litres.
    /// PoolFull: Id = pool id.
    /// PumpStateChanged: Id = pump id, Text = from, Text2 = to.
    /// DrawdownThreshold: Id = source id, Value = percent.
    /// Reveal: Id = reveal id.
    /// </summary>
    public struct SimEvent
    {
        public SimEventType Type;
        public long Tick;
        public int Player;
        public int Id;
        public double Value;
        public string Text;
        public string Text2;

        public override string ToString() => $"{Type}@{Tick} id={Id} v={Value} {Text} {Text2}";
    }

    /// <summary>Receives flushed events. Listeners are presentation only and never change state.</summary>
    public interface ISimEventListener
    {
        /// <summary>Called once per event, in the order raised, with a read-only view of state.</summary>
        void OnSimEvent(in SimEvent e, SimState state);
    }

    /// <summary>Logging seam so the simulation never touches UnityEngine.Debug.</summary>
    public interface ISimLog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    /// <summary>A log that does nothing (used when no log is supplied).</summary>
    public sealed class NullSimLog : ISimLog
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
    }

    /// <summary>
    /// Deferred event bus (Game State Rule 6): events raised during a tick are queued and delivered at
    /// step 3, in order, at most once. A listener that throws is logged and skipped. A flush delivers at
    /// most <see cref="MaxPerFlush"/> events; the rest are dropped with one error.
    /// </summary>
    public sealed class EventBus
    {
        readonly List<ISimEventListener> _listeners = new List<ISimEventListener>();
        readonly Queue<SimEvent> _queue = new Queue<SimEvent>();
        readonly ISimLog _log;
        bool _flushing;

        /// <summary>Per-flush safety cap (tuning knob, default 256).</summary>
        public int MaxPerFlush { get; set; } = 256;

        /// <summary>Number of subscribed listeners.</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>Events waiting for the next flush.</summary>
        public int Pending => _queue.Count;

        public EventBus(ISimLog log) { _log = log ?? new NullSimLog(); }

        public void Subscribe(ISimEventListener l) { if (l != null && !_listeners.Contains(l)) _listeners.Add(l); }
        public void Unsubscribe(ISimEventListener l) { _listeners.Remove(l); }
        public void Clear() { _listeners.Clear(); _queue.Clear(); }

        /// <summary>Queues an event for the next flush.</summary>
        public void Raise(in SimEvent e) => _queue.Enqueue(e);

        /// <summary>Delivers all queued events (and any raised during delivery, up to the cap).</summary>
        public void Flush(SimState state)
        {
            if (_flushing) return;
            _flushing = true;
            int delivered = 0;
            bool capped = false;
            try
            {
                while (_queue.Count > 0)
                {
                    if (delivered >= MaxPerFlush)
                    {
                        capped = true;
                        _queue.Clear();
                        break;
                    }
                    var e = _queue.Dequeue();
                    delivered++;
                    for (int i = 0; i < _listeners.Count; i++)
                    {
                        try { _listeners[i].OnSimEvent(e, state); }
                        catch (Exception ex) { _log.Error($"Event listener {_listeners[i].GetType().Name} failed on {e.Type}: {ex.Message}"); }
                    }
                }
            }
            finally { _flushing = false; }
            if (capped) _log.Error($"Event flush exceeded {MaxPerFlush} events; the rest were dropped (event loop?).");
        }
    }
}
