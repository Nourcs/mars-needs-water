using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MarsNeedsWater.Simulation.Tests
{
    /// <summary>Records every flushed event.</summary>
    public sealed class EventRecorder : ISimEventListener
    {
        public readonly List<SimEvent> Events = new List<SimEvent>();
        public void OnSimEvent(in SimEvent e, SimState state) => Events.Add(e);
        public List<SimEvent> Of(SimEventType t) => Events.FindAll(e => e.Type == t);
        public void Clear() => Events.Clear();
    }

    /// <summary>Collects log lines.</summary>
    public sealed class ListLog : ISimLog
    {
        public readonly List<string> Infos = new List<string>(), Warns = new List<string>(), Errors = new List<string>();
        public void Info(string m) => Infos.Add(m);
        public void Warn(string m) => Warns.Add(m);
        public void Error(string m) => Errors.Add(m);
    }

    /// <summary>A small test level and helpers to drive the simulation like a player.</summary>
    public sealed class TestWorld
    {
        public const int Puddle = 1, Pond = 2;
        public readonly Simulation Sim;
        public readonly EventRecorder Rec = new EventRecorder();
        public readonly ListLog Log = new ListLog();

        public static TuningData LoadTuning()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "mvp_tuning.json");
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TuningData>(json, new JsonSerializerOptions { IncludeFields = true });
        }

        public static LevelData MakeLevel()
        {
            var L = new LevelData
            {
                PlayerStart = new Pose(0, 0, 0),
                StorageSpot = new Pose(1, 0, 0),
                StagingSpot = new Pose(2.2, 0, 0),
                IbcSpot = new Pose(3, 0, -1.5),
                LaunchLever = new Pose(0, 1, -1),
                OrderLever = new Pose(0, 1, 1),
            };
            L.Sources.Add(new SourceDef { Id = Puddle, ScoopArea = new ZoneRect(-1.5, -0.5, -0.5, 0.5), IntakeCentre = new Pose(-1, 0, 0), IntakeRadius = 0.6 });
            var pond = new SourceDef { Id = Pond, ScoopArea = new ZoneRect(-10, -10, -4, -5), IntakeCentre = new Pose(-4.5, 0, -5.5), IntakeRadius = 1.5 };
            pond.Thresholds.AddRange(new[] { 1, 2.5, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 });
            pond.Reveals.Add(new RevealDef(201, 10));
            pond.Reveals.Add(new RevealDef(202, 15));
            L.Sources.Add(pond);
            for (int i = 0; i < 4; i++) L.CrateSpots.Add(new CrateSpot(new Pose(i * 1.5, 0, 3), new Pose(i * 1.5, 0, 4.2)));
            L.PostSockets.Add(new Pose(5, 1, 0));
            L.PostSockets.Add(new Pose(5, 1, 0.4));
            L.WorkAreas.Add(new ZoneRect(-20, -20, 20, 20));
            L.NoPlaceAreas.Add(new ZoneRect(-1.5, -0.5, -0.5, 0.5));
            L.NoPlaceAreas.Add(new ZoneRect(-10, -10, -4, -5));
            return L;
        }

        public TestWorld(TuningData tuning = null, double dt = 0.1)
        {
            Sim = new Simulation(tuning ?? LoadTuning(), MakeLevel(), Log, dt);
            Sim.AutosaveIntervalS = 0;
            Sim.Bus.Subscribe(Rec);
            Sim.NewGame("test");
        }

        public void Tick(int n = 1) { for (int i = 0; i < n; i++) Sim.Tick(); }

        /// <summary>Submits a request and runs one tick. Returns the rejection code, or null.</summary>
        public string Do(Request r)
        {
            int before = Rec.Of(SimEventType.RequestRejected).Count;
            Sim.Submit(r);
            Tick();
            var rej = Rec.Of(SimEventType.RequestRejected);
            return rej.Count > before ? rej[rej.Count - 1].Text : null;
        }

        public void MoveTo(double x, double z, double y = 0) => Do(new PlayerPoseRequest { Pose = new Pose(x, y, z) });

        public void GiveCredits(long cr) => Sim.State.Economy.CreditsMc += cr * 1000;

        public PlayerRecord P => Sim.Placement.Player(0);
        public double Held(string tool) => Sim.HandTools.Held(0, tool);

        /// <summary>Orders an item (teleporting to the ORDER lever), waits for the crate and opens it.</summary>
        public CrateRecord BuyAndOpen(string item)
        {
            MoveTo(0, 1);
            var why = Do(new OrderRequest { Item = item });
            if (why != null) throw new InvalidOperationException($"Order {item} rejected: {why}");
            var crate = Sim.State.Economy.Crates[Sim.State.Economy.Crates.Count - 1];
            Tick(45);
            var e = Sim.Placement.Get(crate.Id);
            if (e == null) throw new InvalidOperationException("Crate did not land.");
            MoveTo(e.Pose.X, e.Pose.Z - 0.8);
            why = Do(new OpenCrateRequest { Crate = crate.Id });
            if (why != null) throw new InvalidOperationException($"OpenCrate rejected: {why}");
            return crate;
        }

        public Entity FindEntity(string item)
        {
            foreach (var e in Sim.State.Placement.Entities.Values) if (e.Item == item && e.Kind != EntityKind.Crate) return e;
            return null;
        }

        public HoseState FindHose(HoseKind kind, int pumpId = -1)
        {
            foreach (var h in Sim.State.Network.Hoses)
                if (h.Kind == kind && (pumpId < 0 || h.PumpId == pumpId || kind != HoseKind.Suction)) return h;
            return null;
        }
    }
}
