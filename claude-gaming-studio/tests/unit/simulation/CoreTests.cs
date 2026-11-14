using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace MarsNeedsWater.Simulation.Tests
{
    public class ClockTests
    {
        [Test] // AC-F1
        public void TwoSecondHitch_RunsFiveTicks_AndDropsTheRest()
        {
            var c = new SimClock(0.1, 0.5);
            Assert.AreEqual(5, c.Advance(2.0));
            Assert.Less(c.Acc, c.Dt);
        }

        [Test] // AC-F1
        public void SixtyFps_TicksOnEverySixthFrame()
        {
            var c = new SimClock(0.1, 0.5);
            var tickFrames = new List<int>();
            for (int f = 1; f <= 60; f++)
            {
                if (c.Advance(1.0 / 60.0) > 0) tickFrames.Add(f);
                Assert.That(c.Alpha, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
            }
            Assert.AreEqual(10, tickFrames.Count);
            for (int i = 0; i < 10; i++) Assert.AreEqual((i + 1) * 6, tickFrames[i]);
        }

        [Test] // AC-E13
        public void KnobClamps_MaxCatchUpAtLeastDt()
        {
            var log = new ListLog();
            var c = new SimClock(0.2, 0.1, log);
            Assert.AreEqual(0.2, c.MaxCatchUp, 1e-12);
            Assert.AreEqual(1, log.Warns.Count);
            Assert.AreEqual(1, c.Advance(1.0));
        }

        [Test]
        public void SecondsToTicks_RoundsAndNeverZero()
        {
            Assert.AreEqual(30, SimClock.SecondsToTicks(3, 0.1));
            Assert.AreEqual(150, SimClock.SecondsToTicks(15, 0.1));
            Assert.AreEqual(1, SimClock.SecondsToTicks(0.01, 0.1));
        }
    }

    public class ContainerTests
    {
        [Test] // AC-R4
        public void TickOrder_IsFixed()
        {
            var w = new TestWorld();
            var steps = new List<string>();
            w.Sim.StepSpy = s => steps.Add(s);
            w.Sim.RequestSave();
            w.Tick();
            CollectionAssert.AreEqual(new[] { "1", "2a", "2b", "2c", "2d", "2e", "2f", "3", "4" }, steps);
        }

        [Test] // AC-R1
        public void State_HasNineSlices()
        {
            int n = 0;
            foreach (var p in typeof(SimState).GetProperties()) n++;
            Assert.AreEqual(9, n);
        }

        [Test] // AC-R2
        public void Slices_HaveNoPublicSetters_AndNoEngineTypes()
        {
            var types = new[] { typeof(SourceState), typeof(TankState), typeof(HoseState), typeof(HoseEnd), typeof(PumpState), typeof(CrateRecord),
                typeof(EconomySlice), typeof(ShippingSlice), typeof(Entity), typeof(PlayerRecord), typeof(SessionSlice), typeof(SimState) };
            foreach (var t in types)
            {
                foreach (var p in t.GetProperties())
                {
                    var set = p.GetSetMethod(nonPublic: false);
                    Assert.IsNull(set, $"{t.Name}.{p.Name} has a public setter");
                    Assert.IsFalse(p.PropertyType.Namespace?.StartsWith("UnityEngine") ?? false, $"{t.Name}.{p.Name} is an engine type");
                }
                foreach (var f in t.GetFields()) Assert.Fail($"{t.Name}.{f.Name} is a public field");
            }
        }

        [Test] // AC-R5
        public void RejectedRequest_ChangesNothing()
        {
            var w = new TestWorld();
            w.GiveCredits(1);
            w.MoveTo(0, 1);
            long creditsBefore = w.Sim.State.Economy.CreditsMc;
            int ownedBefore = w.Sim.State.Economy.Owned.Count;
            w.Rec.Clear();
            Assert.AreEqual(Codes.CannotAfford, w.Do(new OrderRequest { Item = "bucket" }));
            Assert.AreEqual(creditsBefore, w.Sim.State.Economy.CreditsMc);
            Assert.AreEqual(ownedBefore, w.Sim.State.Economy.Owned.Count);
            Assert.AreEqual(0, w.Sim.State.Economy.Crates.Count);
            Assert.AreEqual(1, w.Rec.Of(SimEventType.RequestRejected).Count);
            Assert.AreEqual("Order", w.Rec.Of(SimEventType.RequestRejected)[0].Text2);
        }

        [Test] // AC-E1
        public void DoubleClickBuy_ChargesOnce()
        {
            var w = new TestWorld();
            w.GiveCredits(6);
            w.MoveTo(0, 1);
            w.Rec.Clear();
            w.Sim.Submit(new OrderRequest { Item = "bucket" });
            w.Sim.Submit(new OrderRequest { Item = "bucket" });
            w.Tick();
            Assert.AreEqual(1, w.Rec.Of(SimEventType.Purchased).Count);
            Assert.AreEqual(1, w.Rec.Of(SimEventType.RequestRejected).Count);
            Assert.AreEqual(Codes.AlreadyOwned, w.Rec.Of(SimEventType.RequestRejected)[0].Text);
            Assert.AreEqual(0, w.Sim.Economy.Credits);
        }

        [Test] // AC-E20 + AC-E14
        public void Purchase_GrantsInSameStep_AndIdsComeFromTheCounter()
        {
            var w = new TestWorld();
            w.GiveCredits(6);
            w.MoveTo(0, 1);
            int next = w.Sim.State.Session.NextId;
            bool sawGrantBefore2a = false;
            w.Sim.StepSpy = s => { if (s == "2a") sawGrantBefore2a = w.Sim.HandTools.Owns(0, "bucket") && w.Sim.Economy.Owns("bucket"); };
            w.Do(new OrderRequest { Item = "bucket" });
            Assert.IsTrue(sawGrantBefore2a);
            Assert.AreEqual(next, w.Rec.Of(SimEventType.Purchased)[0].Id);
            Assert.AreEqual(next + 1, w.Sim.State.Session.NextId);
            Assert.AreEqual(Ids.FirstRuntimeId, next);
        }

        [Test] // AC-R6
        public void EventBus_ThrowingListenerIsSkipped()
        {
            var log = new ListLog();
            var bus = new EventBus(log);
            var good = new EventRecorder();
            bus.Subscribe(new Thrower());
            bus.Subscribe(good);
            bus.Raise(new SimEvent { Type = SimEventType.Paused, Id = 1 });
            bus.Raise(new SimEvent { Type = SimEventType.Paused, Id = 2 });
            bus.Raise(new SimEvent { Type = SimEventType.Paused, Id = 3 });
            bus.Flush(new SimState());
            Assert.AreEqual(3, good.Events.Count);
            Assert.AreEqual(2, good.Events[1].Id);
            Assert.AreEqual(1, log.Errors.Count);
        }

        sealed class Thrower : ISimEventListener
        {
            public void OnSimEvent(in SimEvent e, SimState s) { if (e.Id == 2) throw new Exception("boom"); }
        }

        [Test] // AC-E3
        public void EventBus_LoopIsCappedAt256()
        {
            var log = new ListLog();
            var bus = new EventBus(log);
            var looper = new Looper(bus);
            bus.Subscribe(looper);
            bus.Raise(new SimEvent { Type = SimEventType.Reveal });
            bus.Flush(new SimState());
            Assert.AreEqual(256, looper.Count);
            Assert.AreEqual(1, log.Errors.Count);
        }

        sealed class Looper : ISimEventListener
        {
            readonly EventBus _b; public int Count;
            public Looper(EventBus b) { _b = b; }
            public void OnSimEvent(in SimEvent e, SimState s) { Count++; _b.Raise(new SimEvent { Type = SimEventType.Reveal }); }
        }

        [Test] // AC-E4, AC-E19
        public void RequestWhilePaused_AppliesAfterResume()
        {
            var w = new TestWorld();
            w.Sim.Pause();
            w.Sim.Submit(new PlayerPoseRequest { Pose = new Pose(3, 0, 3) });
            Assert.AreEqual(0, w.Sim.Advance(1.0));
            Assert.AreEqual(0, w.P.Pose.X);
            w.Sim.Resume();
            Assert.AreEqual(0, w.Sim.Clock.Acc);
            w.Sim.Advance(0.1);
            Assert.AreEqual(3, w.P.Pose.X);
            Assert.AreEqual(1, w.Rec.Of(SimEventType.Paused).Count);
            Assert.AreEqual(1, w.Rec.Of(SimEventType.Resumed).Count);
        }

        [Test] // AC-F2
        public void SessionTime_12000Ticks_Is1200s()
        {
            var w = new TestWorld();
            w.Tick(12000);
            Assert.AreEqual(1200.0, w.Sim.State.Session.SessionTimeS, 0.001);
        }

        [Test] // AC-R11
        public void Boot_ClearsEverything()
        {
            var w = new TestWorld();
            w.Sim.Submit(new LaunchRequest());
            w.Sim.Clock.Advance(0.05);
            w.Sim.Boot();
            Assert.AreEqual(0, w.Sim.QueuedRequests);
            Assert.AreEqual(0, w.Sim.Clock.Acc);
            Assert.AreEqual(0, w.Sim.Bus.ListenerCount);
            Assert.AreEqual(RunState.Menu, w.Sim.Run);
        }
    }

    public class PlayLogTests
    {
        sealed class ListSink : IPlayLogSink
        {
            public readonly List<string> Lines = new List<string>();
            public int Flushes;
            public bool Throw;
            public void WriteLine(string l) { if (Throw) throw new System.IO.IOException("disk full"); Lines.Add(l); }
            public void Flush() => Flushes++;
            public void Dispose() { }
        }

        [Test] // AC-R10
        public void Row_IsInvariantCulture_WithTenColumns()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var w = new TestWorld();
                var sink = new ListSink();
                var log = new PlayLog(w.Sim, sink);
                var e = new SimEvent { Type = SimEventType.Purchased, Value = 1234.5, Text = "bucket", Id = 7 };
                log.OnSimEvent(e, w.Sim.State);
                Assert.AreEqual(2, sink.Lines.Count);
                Assert.AreEqual(PlayLog.Header, sink.Lines[0]);
                var cols = sink.Lines[1].Split(',');
                Assert.AreEqual(10, cols.Length);
                Assert.AreEqual("1234.5", cols[6]);
                Assert.GreaterOrEqual(sink.Flushes, 1);
            }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }

        [Test] // AC-E7
        public void UnwritableLog_WarnsOnceAndTurnsOff()
        {
            var w = new TestWorld();
            var sink = new ListSink { Throw = true };
            var slog = new ListLog();
            var log = new PlayLog(w.Sim, sink, slog);
            log.OnSimEvent(new SimEvent { Type = SimEventType.Paused }, w.Sim.State);
            log.OnSimEvent(new SimEvent { Type = SimEventType.Paused }, w.Sim.State);
            Assert.AreEqual(1, slog.Warns.Count);
            Assert.IsFalse(log.Enabled);
        }
    }
}
