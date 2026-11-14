using System;
using NUnit.Framework;

namespace MarsNeedsWater.Simulation.Tests
{
    public class WaterSourceTests
    {
        [Test] // Water Source formula example
        public void BucketScoop_DrainsPuddleWithLastDrop()
        {
            var w = new TestWorld();
            var ws = w.Sim.WaterSource;
            ws.Withdraw(TestWorld.Puddle, 40 - 9.19);
            w.Rec.Clear();
            double got = ws.Withdraw(TestWorld.Puddle, 10);
            Assert.AreEqual(9.19, got, 1e-12);
            Assert.AreEqual(SourceCondition.Drained, ws.State(TestWorld.Puddle));
            Assert.AreEqual(0, ws.Withdraw(TestWorld.Puddle, 1));
            w.Tick();
            var drained = w.Rec.Of(SimEventType.SourceDrained);
            Assert.AreEqual(1, drained.Count);
            Assert.AreEqual(9.19, drained[0].Value, 1e-12);
        }

        [Test]
        public void TinyLeftover_IsGrantedToCaller()
        {
            var w = new TestWorld();
            double got = w.Sim.WaterSource.Withdraw(TestWorld.Puddle, 40 - 5e-7);
            Assert.AreEqual(40, got);
            Assert.IsTrue(w.Sim.State.WaterSource.Sources[0].Drained);
        }

        [Test]
        public void BadAmounts_GrantNothingAndWarn()
        {
            var w = new TestWorld();
            Assert.AreEqual(0, w.Sim.WaterSource.Withdraw(TestWorld.Puddle, double.NaN));
            Assert.AreEqual(0, w.Sim.WaterSource.Withdraw(TestWorld.Puddle, -3));
            Assert.AreEqual(0, w.Sim.WaterSource.Withdraw(TestWorld.Puddle, double.PositiveInfinity));
            Assert.AreEqual(3, w.Log.Warns.Count);
            Assert.AreEqual(40, w.Sim.WaterSource.Available(TestWorld.Puddle));
        }

        [Test] // AC-R3 (tick-rate independence for continuous quantities)
        public void PumpFlow_IsTheSameAt10And20Hz()
        {
            double Run(double dt)
            {
                var w = new TestWorld(dt: dt);
                PumpRig.SetUpElectric(w, 1.25); // hand pump rate, cranked
                double before = w.Sim.WaterSource.Available(TestWorld.Pond);
                int ticks = (int)Math.Round(20 / dt);
                for (int i = 0; i < ticks; i++) w.Tick();
                return before - w.Sim.WaterSource.Available(TestWorld.Pond);
            }
            double a = Run(0.1), b = Run(0.05);
            Assert.AreEqual(1.25 * 20, a, 25 * 1e-6);
            Assert.AreEqual(a, b, a * 1e-6);
        }
    }

    public class HandToolTests
    {
        [Test]
        public void Spoon_ScoopAndPour_ConservesWater()
        {
            var w = new TestWorld();
            double total = w.Sim.TotalWater();
            w.MoveTo(-0.2, 0);
            Assert.IsNull(w.Do(new ScoopRequest { Tool = "spoon", Source = TestWorld.Puddle, AimPoint = new Pose(-0.8, 0, 0) }));
            Assert.AreEqual(0.015, w.Held("spoon"), 1e-12);
            Assert.AreEqual(Codes.ToolFull, w.Do(new ScoopRequest { Tool = "spoon", Source = TestWorld.Puddle, AimPoint = new Pose(-0.8, 0, 0) }));
            Assert.IsNull(w.Do(new PourRequest { Tool = "spoon", Container = 100 }));
            Assert.AreEqual(0, w.Held("spoon"), 1e-12);
            Assert.AreEqual(0.015, w.Sim.Network.Tank(100).Contents, 1e-12);
            Assert.AreEqual(total, w.Sim.TotalWater(), 1e-9);
        }

        [Test]
        public void Scoop_RejectionsInOrder()
        {
            var w = new TestWorld();
            w.MoveTo(8, 8);
            Assert.AreEqual(Codes.TooFar, w.Do(new ScoopRequest { Tool = "spoon", Source = TestWorld.Puddle, AimPoint = new Pose(-0.8, 0, 0) }));
            w.MoveTo(-0.2, 0);
            Assert.AreEqual(Codes.NotInHand, w.Do(new ScoopRequest { Tool = "bucket", Source = TestWorld.Puddle, AimPoint = new Pose(-0.8, 0, 0) }));
            Assert.AreEqual(Codes.ToolEmpty, w.Do(new PourRequest { Tool = "spoon", Container = 100 }));
            Assert.AreEqual(Codes.CannotDeposit, w.Do(new PourRequest { Tool = "spoon", Container = TestWorld.Puddle }));
            Assert.AreEqual(Codes.NotOwned, w.Do(new SelectToolRequest { Tool = "bucket" }));
        }

        [Test] // Hand Tools edge case: 10 L into 3 L of space
        public void PartialPour_KeepsTheRest()
        {
            var w = new TestWorld();
            w.GiveCredits(6);
            w.BuyAndOpen("bucket");
            Assert.AreEqual(HandsKind.Tool, w.P.Hands.Kind);
            Assert.AreEqual("bucket", w.P.Hands.Tool);
            w.Sim.State.Network.Tanks[0].Contents = 7;
            w.MoveTo(-0.2, 0);
            Assert.IsNull(w.Do(new ScoopRequest { Tool = "bucket", Source = TestWorld.Puddle, AimPoint = new Pose(-0.8, 0, 0) }));
            Assert.AreEqual(10, w.Held("bucket"), 1e-12);
            Assert.IsNull(w.Do(new PourRequest { Tool = "bucket", Container = 100 }));
            Assert.AreEqual(7, w.Held("bucket"), 1e-9);
            Assert.AreEqual(Codes.PoolFull, w.Do(new PourRequest { Tool = "bucket", Container = 100 }));
        }
    }

    public class WorldWaterTests
    {
        [Test] // Nour 2026-11-09: every water on Earth can be scooped; an ocean keeps exact track of litres taken
        public void Ocean_ScoopAnywhere_TracksExtractedExactly()
        {
            var level = TestWorld.MakeLevel();
            level.Sources.Add(new SourceDef { Id = 1001, Name = "Pacific Ocean", StartVolume_L = 7.1e20, Anywhere = true, IntakeRadius = 0 });
            var sim = new Simulation(TestWorld.LoadTuning(), level, new ListLog(), 0.1);
            sim.AutosaveIntervalS = 0;
            sim.NewGame("ocean");
            Assert.AreEqual(7.1e20, sim.WaterSource.Start(1001));
            // Far from the farm's scoop areas: the planet checks reach for world water, the sim does not.
            sim.Submit(new PlayerPoseRequest { Pose = new Pose(900, -40, 2000) });
            sim.Submit(new ScoopRequest { Tool = "spoon", Source = 1001, AimPoint = new Pose(901, -41, 2000) });
            sim.Tick();
            Assert.AreEqual(0.015, sim.HandTools.Held(0, "spoon"), 1e-12);
            Assert.AreEqual(0.015, sim.WaterSource.Extracted(1001), 1e-15);
            Assert.Greater(sim.WaterSource.DrawdownPct(1001), 0);
            Assert.AreNotEqual(SourceCondition.Drained, sim.WaterSource.State(1001));
        }
    }

    public class ShippingEconomyTests
    {
        [Test] // Shipping Rules 4-5: liftoff at N+30, ready at N+150, first accept at N+151
        public void LaunchCycle_TimersAndPayout()
        {
            var w = new TestWorld();
            w.Sim.State.Network.Tanks[0].Contents = 10;
            w.MoveTo(0, -0.5);
            long n = w.Sim.State.Session.Tick + 1;
            Assert.IsNull(w.Do(new LaunchRequest()));
            Assert.AreEqual(1, w.Rec.Of(SimEventType.LaunchStarted).Count);
            Assert.AreEqual(0, w.Sim.Network.PadPool().Contents);
            Assert.AreEqual(10, w.Sim.State.Shipping.AboardL, 1e-12);
            while (w.Sim.State.Session.Tick < n + 29) w.Tick();
            Assert.AreEqual(0, w.Sim.Economy.Credits);
            w.Tick(); // N+30
            Assert.AreEqual(100, w.Sim.Economy.Credits);
            Assert.AreEqual(10, w.Sim.State.Shipping.ShippedL, 1e-12);
            Assert.AreEqual(n + 30, w.Rec.Of(SimEventType.LaunchCompleted)[0].Tick);
            w.Sim.State.Network.Tanks[0].Contents = 1;
            while (w.Sim.State.Session.Tick < n + 149) w.Tick();
            Assert.AreEqual(Codes.PadBusy, w.Do(new LaunchRequest())); // applied at N+150
            Assert.IsNull(w.Do(new LaunchRequest()));                  // N+151
        }

        [Test]
        public void Launch_RejectsEmptyPad()
        {
            var w = new TestWorld();
            w.MoveTo(0, -0.5);
            Assert.AreEqual(Codes.NothingToShip, w.Do(new LaunchRequest()));
            w.MoveTo(10, 10);
            Assert.AreEqual(Codes.TooFar, w.Do(new LaunchRequest()));
        }

        [Test] // AC-E9: payout and shipped total change in the same tick
        public void Payout_IsAtomicWithShippedTotal()
        {
            var w = new TestWorld();
            w.Sim.State.Network.Tanks[0].Contents = 10;
            w.MoveTo(0, -0.5);
            w.Do(new LaunchRequest());
            for (int i = 0; i < 40; i++)
            {
                w.Tick();
                bool paid = w.Sim.State.Economy.CreditsMc > 0;
                bool shipped = w.Sim.State.Shipping.ShippedUl > 0;
                Assert.AreEqual(paid, shipped);
            }
        }

        [Test]
        public void SpoonLaunch_PaysExactlyTheBucketPrice()
        {
            var w = new TestWorld();
            Assert.AreEqual(6000, w.Sim.Economy.PayoutFor(0.6));
            Assert.AreEqual("bucket", w.Sim.Economy.NextItem().id);
            Assert.AreEqual(ShopItemState.Locked, w.Sim.Economy.StateOf("barrel_100l"));
        }

        [Test] // Economy Rule 8: crate lands after 40 ticks on spot 0
        public void Crate_LandsAfterDeliveryDelay()
        {
            var w = new TestWorld();
            w.GiveCredits(6);
            w.MoveTo(0, 1);
            long n = w.Sim.State.Session.Tick + 1;
            w.Do(new OrderRequest { Item = "bucket" });
            var c = w.Sim.State.Economy.Crates[0];
            while (w.Sim.State.Session.Tick < n + 39) w.Tick();
            Assert.AreEqual(CrateStage.Inbound, c.Stage);
            w.Tick();
            Assert.AreEqual(CrateStage.Landed, c.Stage);
            Assert.AreEqual(0, c.Spot);
        }

        [Test]
        public void EarthCounter_IsExactDecimal()
        {
            var w = new TestWorld();
            Assert.AreEqual(1386000000000000000000m, w.Sim.Economy.EarthRemainingL());
        }
    }

    public class StorageTests
    {
        [Test] // Hose Rule 13: swap pours all the water into the bigger container
        public void SwapStorage_ToBarrel()
        {
            var w = new TestWorld();
            w.GiveCredits(6 + 220);
            w.BuyAndOpen("bucket");
            w.BuyAndOpen("barrel_100l");
            var barrel = w.FindEntity("barrel_100l");
            Assert.IsNotNull(barrel);
            w.Sim.State.Network.Tanks.Find(t => t.Id == 100).Contents = 8;
            double total = w.Sim.TotalWater();
            // Pick up the barrel, carry it to the staging spot, set it down, swap in.
            w.MoveTo(barrel.Pose.X, barrel.Pose.Z - 0.8);
            Assert.IsNull(w.Do(new PickUpRequest { Kind = PickUpKind.Entity, Id = barrel.Id }));
            Assert.AreEqual(HandsKind.Entity, w.P.Hands.Kind);
            w.MoveTo(2.2, -1.0);
            Assert.AreEqual(Codes.NotInstalled, NotInstalledPour(w, barrel.Id));
            Assert.IsNull(w.Do(new PlaceItemRequest { Pose = new Pose(2.2, 0, 0) }));
            Assert.AreEqual("bucket", w.P.Hands.Tool); // stowed tool comes back
            Assert.IsNull(w.Do(new SwapStorageRequest { Tank = barrel.Id }));
            Assert.IsNull(w.Sim.Placement.Get(100));
            Assert.AreEqual(barrel.Id, w.Sim.Placement.PadTank().Id);
            Assert.AreEqual(8, w.Sim.Network.PadPool().Contents, 1e-12);
            Assert.AreEqual(100, w.Sim.Network.PadPool().Capacity);
            Assert.AreEqual(total, w.Sim.TotalWater(), 1e-9);
        }

        static string NotInstalledPour(TestWorld w, int id)
        {
            string why = w.Sim.Network.PourInto(id, 1, out _);
            return why;
        }

        [Test]
        public void PadTank_CannotBePickedUp()
        {
            var w = new TestWorld();
            w.MoveTo(1, -0.8);
            Assert.AreEqual(Codes.Blocked, w.Do(new PickUpRequest { Kind = PickUpKind.Entity, Id = 100 }));
        }
    }

    /// <summary>Builds a working pump rig directly (bypassing the shop) for flow tests.</summary>
    public static class PumpRig
    {
        /// <summary>Hand pump at the pond plumbed into a fitted drum on the storage spot. rate 1.25 = cranked hand pump.</summary>
        public static int SetUpElectric(TestWorld w, double rate)
        {
            var sim = w.Sim;
            // Replace the jerrycan with an installed, fitted drum.
            sim.Placement.Remove(100);
            sim.State.Network.Tanks.Clear();
            sim.Placement.Spawn(500, "drum_200l", EntityKind.Tank, sim.Level.StorageSpot);
            var drum = sim.Network.RegisterTank(500, "drum_200l", installed: true);
            drum.InletFitted = true;
            int crate = 0;
            int pumpId = sim.Pumps.Grant("hand_pump", crate);
            sim.Placement.Spawn(pumpId, "hand_pump", EntityKind.Pump, new Pose(-3.6, 0, -5.0));
            var suction = sim.Network.CreateHose(sim.NewId(), HoseKind.Suction, new Pose(-3.6, 0, -5.0), pumpId);
            suction.Ends[1].Target = EndTarget.IntakeZone(TestWorld.Pond, new Pose(-4.5, 0, -5.5));
            var dis = sim.Network.CreateHose(sim.NewId(), HoseKind.Discharge, new Pose(-3, 0, -4));
            dis.Ends[0].Target = EndTarget.At(pumpId, "outlet", 0);
            dis.Ends[1].Target = EndTarget.At(500, "inlet", 0);
            sim.Network.Invalidate();
            if (rate > 2)
            {
                var p = sim.Pumps.Get(pumpId);
                p.MotorFitted = true; p.HasCable = true; p.CableSocket = 0; p.LeverOn = true;
            }
            else
            {
                // Stand at the handle and crank.
                w.MoveTo(-3.25, -5.0);
                if (w.Do(new CrankStartRequest { Pump = pumpId }) != null) throw new InvalidOperationException("crank rejected");
            }
            return pumpId;
        }
    }

    public class PumpTests
    {
        [Test] // Hand pump moves 1.25 L/s while cranked
        public void HandPump_Cranked_Moves125LitresPerSecond()
        {
            var w = new TestWorld();
            int pump = PumpRig.SetUpElectric(w, 1.25);
            double total = w.Sim.TotalWater();
            w.Tick(10);
            Assert.AreEqual(PumpRunState.Running, w.Sim.Pumps.StateOf(pump));
            Assert.AreEqual(1.25, w.Sim.Network.PadPool().Contents, 0.13);
            Assert.AreEqual(total, w.Sim.TotalWater(), 1e-9);
            // Walk away: crank flag clears at 2a.
            w.MoveTo(5, 5);
            w.Tick();
            Assert.AreEqual(PumpRunState.Ready, w.Sim.Pumps.StateOf(pump));
        }

        [Test] // Pool full -> Stalled, PoolFull raised once; Take frees space -> Running again
        public void ElectricPump_StallsWhenFull_AndResumesAfterLaunch()
        {
            var w = new TestWorld();
            int pump = PumpRig.SetUpElectric(w, 8);
            w.Tick(250); // 200 L at 0.8 L per tick = 250 ticks
            Assert.AreEqual(200, w.Sim.Network.PadPool().Contents, 1e-6);
            w.Tick();
            Assert.AreEqual(PumpRunState.Stalled, w.Sim.Pumps.StateOf(pump));
            Assert.AreEqual(1, w.Rec.Of(SimEventType.PoolFull).Count);
            Assert.IsTrue(w.Rec.Of(SimEventType.PumpStateChanged).Exists(e => e.Text2 == "Stalled"));
            w.MoveTo(0, -0.5);
            Assert.IsNull(w.Do(new LaunchRequest()));
            Assert.AreEqual(PumpRunState.Running, w.Sim.Pumps.StateOf(pump));
            Assert.AreEqual(0.8, w.Sim.Network.PadPool().Contents, 1e-9);
        }

        [Test] // AC-E2 style: several thresholds in one tick, ascending, same tick
        public void Drawdown_SeveralThresholdsInOneTick()
        {
            var w = new TestWorld();
            w.Sim.WaterSource.Withdraw(TestWorld.Pond, 20000 * 0.31);
            w.Tick();
            var th = w.Rec.Of(SimEventType.DrawdownThreshold);
            CollectionAssert.AreEqual(new[] { 1, 2.5, 5, 10, 20, 30 }, th.ConvertAll(e => e.Value));
            Assert.IsTrue(th.TrueForAll(e => e.Tick == th[0].Tick));
            var rv = w.Rec.Of(SimEventType.Reveal);
            CollectionAssert.AreEqual(new[] { 201, 202 }, rv.ConvertAll(e => e.Id));
        }
    }
}

namespace MarsNeedsWater.Simulation.Tests
{
    public class LadderTests
    {
        /// <summary>Plays the hand ladder through requests only, up to a cranked hand pump filling the drum.</summary>
        [Test]
        public void HandLadder_ToCrankedHandPump_ThroughRequestsOnly()
        {
            var w = new TestWorld();
            w.GiveCredits(6 + 220 + 260 + 380);
            double total0 = w.Sim.TotalWater();
            w.BuyAndOpen("bucket");
            SwapIn(w, "barrel_100l");
            SwapIn(w, "drum_200l");
            var drum = w.Sim.Placement.PadTank();
            Assert.AreEqual("drum_200l", drum.Item);

            // Screw in the inlet fitting.
            var fitting = w.FindEntity("inlet_fitting");
            w.MoveTo(fitting.Pose.X, fitting.Pose.Z - 0.6);
            Assert.IsNull(w.Do(new PickUpRequest { Kind = PickUpKind.Entity, Id = fitting.Id }));
            w.MoveTo(1, -0.7);
            Assert.IsNull(w.Do(new FitInletRequest { Tank = drum.Id }));
            Assert.IsTrue(w.Sim.Network.Tank(drum.Id).InletFitted);

            // Hand pump: carry it to the pond and set it down.
            w.BuyAndOpen("hand_pump");
            var pump = w.FindEntity("hand_pump");
            w.MoveTo(pump.Pose.X, pump.Pose.Z - 0.7);
            Assert.IsNull(w.Do(new PickUpRequest { Kind = PickUpKind.Entity, Id = pump.Id }));
            w.MoveTo(-3.6, -3.4);
            Assert.IsNull(w.Do(new PlaceItemRequest { Pose = new Pose(-3.6, 0, -4.5, 180) }));
            Assert.AreEqual("no_intake", w.Sim.Pumps.MissingPart(pump.Id));

            // Suction end into the pond's intake zone.
            var suction = w.Sim.State.Network.Hoses.Find(h => h.Kind == HoseKind.Suction);
            w.MoveTo(-3.8, -5.0);
            Assert.IsNull(w.Do(new PickUpRequest { Kind = PickUpKind.HoseEnd, Id = suction.Id, EndIdx = 1 }));
            Assert.IsNull(w.Do(new ConnectRequest { Hose = suction.Id, EndIdx = 1, Target = EndTarget.IntakeZone(TestWorld.Pond, new Pose(-4.5, 0, -5.5)) }));
            Assert.AreEqual(TestWorld.Pond, w.Sim.Network.IntakeBound(pump.Id));
            Assert.AreEqual("no_outlet", w.Sim.Pumps.MissingPart(pump.Id));

            // Discharge hose: coil end 0 into the pump outlet, walk end 1 to the drum inlet.
            var dis = w.Sim.State.Network.Hoses.Find(h => h.Kind == HoseKind.Discharge);
            w.Sim.Network.TryEndPose(dis.Id, 0, out var coil);
            w.MoveTo(coil.X, coil.Z - 0.6);
            Assert.IsNull(w.Do(new PickUpRequest { Kind = PickUpKind.HoseEnd, Id = dis.Id, EndIdx = 0 }));
            w.Sim.Network.TrySocketPose(pump.Id, "outlet", 0, out var outlet);
            w.MoveTo(outlet.X, outlet.Z + 0.6);
            Assert.AreEqual(Codes.WrongSocket, w.Sim.Network.CanConnect(0, dis.Id, 0, EndTarget.At(pump.Id, "intake", 0)));
            Assert.IsNull(w.Do(new ConnectRequest { Hose = dis.Id, EndIdx = 0, Target = EndTarget.At(pump.Id, "outlet", 0) }));
            Assert.AreEqual(HandsKind.HoseEnd, w.P.Hands.Kind);
            Assert.AreEqual(1, w.P.Hands.EndIdx);
            w.MoveTo(1, -0.6);
            Assert.IsNull(w.Do(new ConnectRequest { Hose = dis.Id, EndIdx = 1, Target = EndTarget.At(drum.Id, "inlet", 0) }));
            Assert.AreEqual("bucket", w.P.Hands.Tool);
            Assert.IsNull(w.Sim.Pumps.MissingPart(pump.Id));

            // Crank for 10 s.
            w.Sim.Network.TrySocketPose(pump.Id, "handle", 0, out var handle);
            w.MoveTo(handle.X, handle.Z);
            Assert.IsNull(w.Do(new CrankStartRequest { Pump = pump.Id }));
            double before = w.Sim.Network.PadPool().Contents;
            w.Tick(100);
            Assert.AreEqual(12.5, w.Sim.Network.PadPool().Contents - before, 1e-6);
            Assert.AreEqual(total0, w.Sim.TotalWater(), 1e-6);
        }

        static void SwapIn(TestWorld w, string item)
        {
            w.BuyAndOpen(item);
            var e = w.FindEntity(item);
            w.MoveTo(e.Pose.X, e.Pose.Z - 0.8);
            Assert.IsNull(w.Do(new PickUpRequest { Kind = PickUpKind.Entity, Id = e.Id }), "pick up " + item);
            w.MoveTo(2.2, -1.0);
            Assert.IsNull(w.Do(new PlaceItemRequest { Pose = new Pose(2.2, 0, 0) }), "place " + item);
            Assert.IsNull(w.Do(new SwapStorageRequest { Tank = e.Id }), "swap " + item);
        }
    }
}
