using System;
using System.Collections.Generic;

namespace MarsNeedsWater.Simulation
{
    public enum RunState { Booting, Menu, Loading, Running, Paused, Quitting }

    /// <summary>Called at tick step 4 when a save is due (Save &amp; Load implements it; the MVP build may leave it null).</summary>
    public interface ISaveWriter
    {
        void Write(SimState state, long tick);
    }

    /// <summary>
    /// Game State &amp; Simulation: the container. Holds the state, the request queue, the event bus and the
    /// systems, and runs the fixed tick order: 1 requests, 2a Pumps, 2b Network, 2c Shipping, 2d Water Source,
    /// 2e Drawdown, 2f Economy, 3 event flush, 4 pending save. Create a fresh one on every Play (clean start).
    /// </summary>
    public sealed class Simulation
    {
        public SimState State { get; private set; } = new SimState();
        public Tuning Tuning { get; }
        public LevelData Level { get; }
        public ISimLog Log { get; }
        public EventBus Bus { get; }
        public SimClock Clock { get; }
        public RunState Run { get; private set; } = RunState.Booting;

        public WaterSourceSystem WaterSource { get; }
        public PlacementSystem Placement { get; }
        public HandToolsSystem HandTools { get; }
        public NetworkSystem Network { get; }
        public PumpsSystem Pumps { get; }
        public ShippingSystem Shipping { get; }
        public EconomySystem Economy { get; }
        public DrawdownSystem Drawdown { get; }

        /// <summary>Optional save writer used at step 4 (and immediately while paused or quitting).</summary>
        public ISaveWriter SaveWriter { get; set; }
        /// <summary>Autosave interval in seconds (Game State knob; default 120).</summary>
        public double AutosaveIntervalS { get; set; } = 120;

        /// <summary>Hook for tests: called at each tick step with its label ("1", "2a" ... "4").</summary>
        public Action<string> StepSpy { get; set; }

        /// <summary>Invoked just before each tick's step 1, so the controller can submit PlayerPose then buffered requests.</summary>
        public Action BeforeTick { get; set; }

        public double Dt => Clock.Dt;
        public double Alpha => Clock.Alpha;

        readonly Queue<Request> _queue = new Queue<Request>();
        bool _savePending;
        long _lastSaveTick;
        bool _launchStartedThisTick;

        public Simulation(TuningData tuning, LevelData level, ISimLog log = null, double dt = 0.1, double maxCatchUp = 0.5)
        {
            Log = log ?? new NullSimLog();
            Tuning = new Tuning(tuning);
            Level = level ?? throw new ArgumentNullException(nameof(level));
            Bus = new EventBus(Log);
            Clock = new SimClock(dt, maxCatchUp, Log);
            WaterSource = new WaterSourceSystem(this);
            Placement = new PlacementSystem(this);
            HandTools = new HandToolsSystem(this);
            Network = new NetworkSystem(this);
            Pumps = new PumpsSystem(this);
            Shipping = new ShippingSystem(this);
            Economy = new EconomySystem(this);
            Drawdown = new DrawdownSystem(this);
            Boot();
        }

        /// <summary>Clears state, the queue, the accumulator and all subscribers (Rule 11), then goes to Menu.</summary>
        public void Boot()
        {
            Run = RunState.Booting;
            State = new SimState();
            _queue.Clear();
            Clock.Reset();
            Bus.Clear();
            WaterSource.ClearTransients();
            _savePending = false;
            Run = RunState.Menu;
        }

        /// <summary>Starts a new game from the tuning table and level, then raises SessionStarted.</summary>
        public void NewGame(string sessionId = null)
        {
            if (Run != RunState.Menu) { Log.Warn($"NewGame ignored in {Run}."); return; }
            Run = RunState.Loading;
            State = new SimState();
            State.Session.SessionId = sessionId ?? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            WaterSource.NewGame();
            Placement.NewGame();
            HandTools.NewGame();
            Network.NewGame();
            Pumps.NewGame();
            Shipping.NewGame();
            Economy.NewGame();
            Drawdown.NewGame();
            _lastSaveTick = 0;
            Clock.Reset();
            Run = RunState.Running;
            RaiseNow(new SimEvent { Type = SimEventType.SessionStarted });
        }

        /// <summary>Hands out a new runtime id (only the simulation creates ids).</summary>
        internal int NewId() => State.Session.NextId++;

        /// <summary>Queues a request; it is validated and applied at step 1 of the next tick.</summary>
        public void Submit(Request r)
        {
            if (r == null) return;
            _queue.Enqueue(r);
        }

        public int QueuedRequests => _queue.Count;

        internal void Raise(SimEvent e)
        {
            e.Tick = State.Session.Tick;
            Bus.Raise(e);
        }

        /// <summary>Events raised outside a tick are flushed immediately (Rule 6).</summary>
        void RaiseNow(SimEvent e)
        {
            Raise(e);
            Bus.Flush(State);
        }

        /// <summary>Feeds one frame of unscaled real time. Runs 0 to 5 ticks while Running.</summary>
        public int Advance(double frameDt)
        {
            if (Run != RunState.Running) return 0;
            int n = Clock.Advance(frameDt);
            for (int i = 0; i < n; i++) Tick();
            return n;
        }

        /// <summary>Runs exactly one tick (tests call this directly).</summary>
        public void Tick()
        {
            if (Run != RunState.Running) return;
            BeforeTick?.Invoke();
            State.Session.Tick++;
            _launchStartedThisTick = false;

            // 1. Requests, first in first out.
            StepSpy?.Invoke("1");
            while (_queue.Count > 0)
            {
                var r = _queue.Dequeue();
                string why = ApplyRequest(r);
                if (why != null)
                    Raise(new SimEvent { Type = SimEventType.RequestRejected, Player = r.Player, Text = why, Text2 = r.Name });
            }
            // 2a-2f.
            StepSpy?.Invoke("2a"); Pumps.Step();
            StepSpy?.Invoke("2b"); Network.Step(Pumps.All, Pumps.DemandRate);
            StepSpy?.Invoke("2c"); Shipping.Step(_launchStartedThisTick);
            StepSpy?.Invoke("2d"); WaterSource.Step();
            StepSpy?.Invoke("2e"); Drawdown.Step();
            StepSpy?.Invoke("2f"); Economy.Step();
            State.Session.SessionTimeS += Dt;
            // 3. Flush.
            StepSpy?.Invoke("3");
            Bus.Flush(State);
            // 4. Pending save (queue is always empty here).
            if (AutosaveIntervalS > 0 && (State.Session.Tick - _lastSaveTick) >= SimClock.SecondsToTicks(AutosaveIntervalS, Dt))
                _savePending = true;
            if (_savePending)
            {
                StepSpy?.Invoke("4");
                WriteSave();
            }
        }

        string ApplyRequest(Request r)
        {
            try
            {
                switch (r)
                {
                    case PlayerPoseRequest _:
                    case PickUpRequest _:
                    case DropRequest _:
                    case PlaceItemRequest _:
                    case PropSettledRequest _:
                    {
                        if (r is PickUpRequest) Pumps.ReleaseCrank(r.Player);
                        return Placement.Apply(r);
                    }
                    case SelectToolRequest _:
                        Pumps.ReleaseCrank(r.Player);
                        return HandTools.Apply(r);
                    case ScoopRequest _:
                    case PourRequest _:
                        return HandTools.Apply(r);
                    case ConnectRequest _:
                    case DisconnectRequest _:
                    case FitInletRequest _:
                    case SwapStorageRequest _:
                        return Network.Apply(r);
                    case CrankStartRequest _:
                    case CrankStopRequest _:
                    case BoltMotorRequest _:
                    case SetSwitchRequest _:
                    case PlugCableRequest _:
                    case UnplugCableRequest _:
                        return Pumps.Apply(r);
                    case OrderRequest _:
                    case OpenCrateRequest _:
                        return Economy.Apply(r);
                    case LaunchRequest _:
                    {
                        string why = Shipping.Apply(r);
                        if (why == null) _launchStartedThisTick = true;
                        return why;
                    }
                }
                return Codes.Unknown;
            }
            catch (Exception ex)
            {
                Log.Error($"Request {r.Name} failed: {ex}");
                return Codes.Unknown;
            }
        }

        /// <summary>Asks for a save at this tick's step 4 (Economy calls this after every purchase).</summary>
        public void RequestSave()
        {
            if (Run == RunState.Running) { _savePending = true; return; }
            if (Run == RunState.Paused || Run == RunState.Quitting)
            {
                DropQueued();
                WriteSave();
                return;
            }
            Log.Warn($"Save refused in {Run}.");
        }

        void DropQueued()
        {
            if (_queue.Count == 0) return;
            Log.Info($"Dropped {_queue.Count} queued request(s) before saving.");
            _queue.Clear();
        }

        void WriteSave()
        {
            _savePending = false;
            _lastSaveTick = State.Session.Tick;
            if (SaveWriter == null) return;
            try { SaveWriter.Write(State, State.Session.Tick); }
            catch (Exception ex) { Log.Error($"Save failed: {ex.Message}"); return; }
            RaiseNow(new SimEvent { Type = SimEventType.Saved });
        }

        /// <summary>Pauses: no ticks run and the clock stops. Requests queue until resume.</summary>
        public void Pause()
        {
            if (Run != RunState.Running) { Log.Info($"Pause ignored in {Run}."); return; }
            Run = RunState.Paused;
            foreach (var p in State.Placement.Players.Keys) Pumps.ReleaseCrank(p);
            RaiseNow(new SimEvent { Type = SimEventType.Paused });
        }

        /// <summary>Resumes; the accumulator is reset so resuming never triggers catch-up ticks.</summary>
        public void Resume()
        {
            if (Run != RunState.Paused) { Log.Info($"Resume ignored in {Run}."); return; }
            Clock.Reset();
            Run = RunState.Running;
            RaiseNow(new SimEvent { Type = SimEventType.Resumed });
        }

        /// <summary>Quit to menu or quit the game: SessionEnded, save, then Menu or Quitting.</summary>
        public void EndSession(bool quitGame)
        {
            if (Run != RunState.Running && Run != RunState.Paused) return;
            if (quitGame) Run = RunState.Quitting;
            RaiseNow(new SimEvent { Type = SimEventType.SessionEnded });
            DropQueued();
            WriteSave();
            if (!quitGame) Run = RunState.Menu;
        }

        /// <summary>Total water in the world: sources + tools + tanks + aboard + shipped (conservation check).</summary>
        public double TotalWater() =>
            WaterSource.TotalRemaining() + HandTools.TotalHeld() + Network.TotalInTanks() + State.Shipping.AboardL + State.Shipping.ShippedL;
    }
}
