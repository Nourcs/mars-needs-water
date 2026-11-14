using System.Collections.Generic;
using MarsNeedsWater.Simulation;
using Pose = MarsNeedsWater.Simulation.Pose;
using UnityEngine;
using Sim = MarsNeedsWater.Simulation.Simulation;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Presentation: mirrors simulation state into greybox GameObjects every frame (entities, hoses, cables,
    /// water planes, stain bands, reveals, rocket). Reads state only; never changes it.
    /// </summary>
    public sealed class WorldView : MonoBehaviour, ISimEventListener
    {
        Sim _sim;
        GreyboxLevel _level;
        Transform _hands;
        bool _ownBucket;

        readonly Dictionary<int, EntityView> _entities = new Dictionary<int, EntityView>();
        readonly Dictionary<int, HoseView> _hoses = new Dictionary<int, HoseView>();
        readonly Dictionary<int, CableView> _cables = new Dictionary<int, CableView>();
        readonly List<GameObject> _bands = new List<GameObject>();
        int _bandCount = -1;

        // Interpolation (Drawdown Rule 1): previous and current tick values.
        double _pondPrev, _pondCur, _puddlePrev, _puddleCur;

        // Hand tools shown in first person (presentation only).
        GameObject _spoonView, _bucketView;
        Transform _spoonWater, _bucketWater, _spoonGrip, _bucketGrip;

        // Rocket presentation timeline.
        float _rocketFlyT = -1f;
        PadState _lastPad = PadState.Ready;

        sealed class EntityView
        {
            public GameObject Root;
            public Transform Gauge;
            public float GaugeHeight;
            public GameObject Handle, Motor, Lever;
            public bool Held;
        }

        sealed class HoseView
        {
            public LineRenderer Line;
            public GameObject[] Ends = new GameObject[2];
            public GameObject Coil;
        }

        sealed class CableView
        {
            public LineRenderer Line;
            public GameObject End;
        }

        public void Init(Sim sim, GreyboxLevel level, Transform handsAnchor)
        {
            _sim = sim;
            _level = level;
            _hands = handsAnchor;
            BuildToolViews();
            SnapshotWater();
            _pondPrev = _pondCur;
            _puddlePrev = _puddleCur;
            _sim.Bus.Subscribe(this);
        }

        /// <summary>Called by the driver before each tick so visuals can interpolate.</summary>
        public void BeforeTick()
        {
            _pondPrev = _pondCur;
            _puddlePrev = _puddleCur;
        }

        /// <summary>Called by the driver after each tick.</summary>
        public void AfterTick() => SnapshotWater();

        void SnapshotWater()
        {
            var ws = _sim.WaterSource;
            // Before the sim's first NewGame there are no sources yet: show a full pond, no errors.
            if (!ws.Exists(GreyboxLevel.PondId) || !ws.Exists(GreyboxLevel.PuddleId)) { _pondCur = 1; _puddleCur = 0; return; }
            double pondStart = ws.Start(GreyboxLevel.PondId);
            _pondCur = pondStart > 0 ? ws.Available(GreyboxLevel.PondId) / pondStart : 1; // never 0/0 (NaN broke the bank walls)
            _puddleCur = ws.Available(GreyboxLevel.PuddleId);
        }

        public void OnSimEvent(in SimEvent e, SimState state)
        {
            if (e.Type == SimEventType.LaunchCompleted) _rocketFlyT = 0f;
        }

        void LateUpdate()
        {
            if (_sim == null) return;
            float a = (float)_sim.Alpha;
            SyncWater(a);
            SyncEntities();
            SyncHoses();
            SyncCables();
            SyncBands();
            SyncRocket();
            SyncTool();
        }

        // ------------------------------------------------------------ first-person tools

        void BuildToolViews()
        {
            if (_hands == null) return;
            _spoonView = new GameObject("View_Spoon");
            _spoonView.transform.SetParent(_hands, false);
            _spoonView.transform.localPosition = new Vector3(0.26f, -0.27f, 0.52f);
            _spoonView.transform.localRotation = Quaternion.Euler(-12f, -18f, 0f);
            if (Own.Make("own-spoon", "Spoon", _spoonView.transform, Vector3.zero, 0.7f, false) == null)
            {
                NoShadow(Prims.Deco(PrimitiveType.Cube, "Handle", new Vector3(0, 0, -0.1f), new Vector3(0.022f, 0.01f, 0.2f), Palette.Grey, _spoonView.transform, 0.85f, 1f));
                NoShadow(Prims.Deco(PrimitiveType.Sphere, "Bowl", new Vector3(0, 0, 0.03f), new Vector3(0.065f, 0.022f, 0.085f), Palette.Grey, _spoonView.transform, 0.85f, 1f));
            }
            var sw = Prims.Deco(PrimitiveType.Sphere, "Water", new Vector3(0, 0.007f, 0.03f), new Vector3(0.052f, 0.01f, 0.07f), Palette.Water, _spoonView.transform, 0.95f);
            NoShadow(sw);
            _spoonWater = sw.transform;

            _bucketView = new GameObject("View_Bucket");
            _bucketView.transform.SetParent(_hands, false);
            _bucketView.transform.localPosition = new Vector3(0.34f, -0.5f, 0.62f);
            var pail = new Color(0.72f, 0.74f, 0.76f);
            _ownBucket = Own.Make("own-bucket", "Bucket", _bucketView.transform, Vector3.zero, 0.5f, false) != null;
            if (!_ownBucket)
            {
                NoShadow(Prims.Deco(PrimitiveType.Cylinder, "Pail", Vector3.zero, new Vector3(0.28f, 0.15f, 0.28f), pail, _bucketView.transform, 0.7f, 0.9f));
                NoShadow(Prims.Deco(PrimitiveType.Cylinder, "Rim", new Vector3(0, 0.15f, 0), new Vector3(0.3f, 0.01f, 0.3f), pail, _bucketView.transform, 0.7f, 0.9f));
                NoShadow(Prims.Deco(PrimitiveType.Cube, "Bail", new Vector3(0, 0.25f, 0), new Vector3(0.3f, 0.012f, 0.012f), Palette.Dark, _bucketView.transform, 0.5f, 0.8f));
            }
            var bw = Prims.Deco(PrimitiveType.Cylinder, "Water", Vector3.zero, new Vector3(0.265f, 0.004f, 0.265f), Palette.Water, _bucketView.transform, 0.95f);
            NoShadow(bw);
            _bucketWater = bw.transform;
            // Where the right hand closes round each tool (z = along the handle); HandsIK brings the fist here.
            _spoonGrip = new GameObject("Grip").transform;
            _spoonGrip.SetParent(_spoonView.transform, false);
            _spoonGrip.localPosition = new Vector3(0f, 0.016f, -0.155f);
            _bucketGrip = new GameObject("Grip").transform;
            _bucketGrip.SetParent(_bucketView.transform, false);
            _bucketGrip.localPosition = new Vector3(0f, _ownBucket ? 0.28f : 0.25f, 0f);
            _bucketGrip.localRotation = Quaternion.Euler(0f, -90f, 0f);
            _spoonView.SetActive(false);
            _bucketView.SetActive(false);
        }

        static void NoShadow(GameObject g)
        {
            var r = g.GetComponent<Renderer>();
            if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void SyncTool()
        {
            if (_spoonView == null) return;
            var hands = _sim.Placement.Hands(0);
            string tool = hands.Kind == HandsKind.Tool ? hands.Tool : null;
            _spoonView.SetActive(tool == "spoon");
            _bucketView.SetActive(tool == "bucket");
            HandsIK.Grip = tool == "spoon" ? _spoonGrip : tool == "bucket" ? _bucketGrip : null;
            if (tool == null) return;
            double cap = _sim.HandTools.Capacity(tool);
            float f = cap <= 0 ? 0f : Mathf.Clamp01((float)(_sim.HandTools.Held(0, tool) / cap));
            if (tool == "spoon") _spoonWater.gameObject.SetActive(f > 0.01f);
            else
            {
                _bucketWater.gameObject.SetActive(f > 0.01f);
                float wy = _ownBucket ? -0.13f + 0.25f * f : -0.14f + 0.27f * f;
                _bucketWater.localPosition = new Vector3(0, wy, 0);
                if (_ownBucket)
                {
                    // The pail tapers: the water disc is as wide as the inside at its height.
                    float d = 2f * (0.104f + (wy + 0.136f) / 0.272f * 0.031f) - 0.004f;
                    _bucketWater.localScale = new Vector3(d, 0.004f, d);
                }
            }
        }

        // ------------------------------------------------------------ water

        void SyncWater(float a)
        {
            double pond = _pondPrev + (_pondCur - _pondPrev) * a;
            if (double.IsNaN(pond)) pond = 1;
            // Everything here is in the farm's local frame (the planet may be turned under the player).
            var p = _level.PondWater.localPosition;
            _level.PondWater.localPosition = new Vector3(p.x, GreyboxLevel.PondHeight(pond), p.z);
            _level.PondWater.gameObject.SetActive(pond > 1e-6);
            SyncBank(GreyboxLevel.PondHeight(pond), pond > 1e-6);

            double pl = _puddlePrev + (_puddleCur - _puddlePrev) * a;
            float d = GreyboxLevel.PuddleDepthFor(pl);
            var w = _level.PuddleWater;
            w.gameObject.SetActive(pl > 1e-6);
            w.localScale = new Vector3(w.localScale.x, Mathf.Max(d, 0.002f), w.localScale.z);
            w.localPosition = new Vector3(w.localPosition.x, 0.006f + d / 2f, w.localPosition.z);

            // Reveals exist from the start under the (opaque) water and simply break the surface as it drops.
        }

        /// <summary>The bathtub ring: dry bank above the water line, a dark wet strip right at it.</summary>
        void SyncBank(float waterY, bool hasWater)
        {
            for (int i = 0; i < 4; i++)
            {
                var dry = _level.DryWalls[i];
                if (dry != null)
                {
                    float top = 0f, bottom = waterY + (hasWater ? 0.05f : 0f);
                    float h = Mathf.Max(0.001f, top - bottom);
                    dry.gameObject.SetActive(top - bottom > 0.002f);
                    dry.localScale = new Vector3(dry.localScale.x, h, dry.localScale.z);
                    dry.localPosition = new Vector3(dry.localPosition.x, top - h / 2f, dry.localPosition.z);
                }
                var wet = _level.WetEdges[i];
                if (wet != null)
                {
                    float h = Mathf.Min(0.05f, -waterY);
                    wet.gameObject.SetActive(hasWater && h > 0.002f);
                    wet.localScale = new Vector3(wet.localScale.x, Mathf.Max(0.001f, h), wet.localScale.z);
                    wet.localPosition = new Vector3(wet.localPosition.x, waterY + h / 2f, wet.localPosition.z);
                }
            }
        }

        void SyncBands()
        {
            if (!_sim.State.Drawdown.Passed.TryGetValue(GreyboxLevel.PondId, out var passed)) return;
            if (passed.Count == _bandCount) return;
            _bandCount = passed.Count;
            foreach (var b in _bands) Destroy(b);
            _bands.Clear();
            var r = GreyboxLevel.PondRect;
            foreach (var pct in passed)
            {
                float y = GreyboxLevel.PondHeight(1 - pct / 100.0);
                float t = 0.04f; // 4 cm band
                _bands.Add(Prims.Box($"Band_{pct}_N", new Vector3(r.center.x, y, r.yMax - 0.03f), new Vector3(r.width, t, 0.02f), Palette.Stain, _level.BandRoot, false));
                _bands.Add(Prims.Box($"Band_{pct}_S", new Vector3(r.center.x, y, r.yMin + 0.03f), new Vector3(r.width, t, 0.02f), Palette.Stain, _level.BandRoot, false));
                _bands.Add(Prims.Box($"Band_{pct}_E", new Vector3(r.xMax - 0.03f, y, r.center.y), new Vector3(0.02f, t, r.height), Palette.Stain, _level.BandRoot, false));
                _bands.Add(Prims.Box($"Band_{pct}_W", new Vector3(r.xMin + 0.03f, y, r.center.y), new Vector3(0.02f, t, r.height), Palette.Stain, _level.BandRoot, false));
            }
        }

        // ------------------------------------------------------------ rocket

        void SyncRocket()
        {
            var sh = _sim.State.Shipping;
            var rocket = _level.Rocket;
            if (_rocketFlyT >= 0f)
            {
                _rocketFlyT += Time.deltaTime;
                rocket.localPosition = _level.RocketBase + Vector3.up * (_rocketFlyT * _rocketFlyT * 8f);
                if (_level.RocketFlame != null)
                {
                    _level.RocketFlame.SetActive(true);
                    _level.RocketFlame.transform.localScale = new Vector3(0.6f, 1.6f + Mathf.Sin(Time.time * 40f) * 0.25f, 0.6f);
                }
                if (_rocketFlyT > 4f) { _rocketFlyT = -1f; rocket.gameObject.SetActive(false); }
            }
            else if (sh.State == PadState.Ready)
            {
                if (_level.RocketFlame != null) _level.RocketFlame.SetActive(false);
                if (_lastPad != PadState.Ready) rocket.gameObject.SetActive(true);
                rocket.localPosition = _level.RocketBase;
            }
            else if (sh.State == PadState.Countdown)
            {
                rocket.gameObject.SetActive(true);
                rocket.localPosition = _level.RocketBase + Random.insideUnitSphere * 0.02f;
            }
            else rocket.gameObject.SetActive(false);
            _lastPad = sh.State;
        }

        // ------------------------------------------------------------ entities

        void SyncEntities()
        {
            var ents = _sim.State.Placement.Entities;
            // Remove views of entities that are gone.
            List<int> gone = null;
            foreach (var id in _entities.Keys) if (!ents.ContainsKey(id)) (gone ??= new List<int>()).Add(id);
            if (gone != null) foreach (var id in gone) { Destroy(_entities[id].Root); _entities.Remove(id); }

            foreach (var e in ents.Values)
            {
                if (!_entities.TryGetValue(e.Id, out var v)) { v = MakeEntity(e); _entities[e.Id] = v; }
                bool held = e.HeldBy >= 0;
                if (held != v.Held)
                {
                    v.Held = held;
                    foreach (var c in v.Root.GetComponentsInChildren<Collider>()) c.enabled = !held;
                    v.Root.transform.SetParent(held ? _hands : _level.Root, false);
                }
                if (held)
                {
                    v.Root.transform.localPosition = HeldOffset(e);
                    v.Root.transform.localRotation = Quaternion.identity;
                }
                else
                {
                    v.Root.transform.position = e.Pose.ToV3();
                    v.Root.transform.rotation = e.Pose.ToRot();
                }
                UpdateEntityDetails(e, v);
            }
        }

        static Vector3 HeldOffset(Entity e)
        {
            switch (e.Kind)
            {
                case EntityKind.InletFitting: return new Vector3(0.3f, -0.35f, 0.6f);
                case EntityKind.MotorKit: return new Vector3(0.25f, -0.55f, 0.9f);
                case EntityKind.Pump: return new Vector3(0f, -1.0f, 1.1f);
                case EntityKind.Tank: return new Vector3(0f, -1.1f, 1.1f);
                default: return new Vector3(0f, -0.9f, 1.1f);
            }
        }

        /// <summary>
        /// Shows our own model in place of a greybox body: the body stays as the invisible thing you aim at and
        /// bump into. False (and nothing changes) if the model file is missing.
        /// </summary>
        static bool UseOwn(string model, GameObject body, Transform root)
        {
            if (Own.Make(model, model, root, Vector3.zero) == null) return false;
            var r = body.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
            return true;
        }

        EntityView MakeEntity(Entity e)
        {
            var v = new EntityView { Root = new GameObject($"{e.Kind}_{e.Item}_{e.Id}") };
            var root = v.Root.transform;
            root.SetParent(_level.Root, false);
            switch (e.Kind)
            {
                case EntityKind.Crate:
                {
                    var b = Prims.Box("Crate", new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.8f), Palette.Crate, root);
                    SimTarget.Add(b, TargetKind.Entity, e.Id);
                    Prims.Label("CLH", new Vector3(0, 0.3f, -0.406f), 0f, 0.08f, Palette.White, root, 0.4f);
                    if (UseOwn("own-crate", b, root)) break;
                    Prims.Box("Stencil", new Vector3(0, 0.3f, -0.401f), new Vector3(0.5f, 0.15f, 0.005f), Palette.Orange, root, false);
                    foreach (float y in new[] { 0.06f, 0.54f })
                    {
                        Prims.Deco(PrimitiveType.Cube, "Slat", new Vector3(0, y, 0), new Vector3(0.83f, 0.07f, 0.83f), Palette.CrateSlat, root);
                    }
                    for (int k = 0; k < 4; k++)
                        Prims.Deco(PrimitiveType.Cube, "Corner", new Vector3(k % 2 == 0 ? -0.39f : 0.39f, 0.3f, k < 2 ? -0.39f : 0.39f), new Vector3(0.06f, 0.6f, 0.06f), Palette.CrateSlat, root);
                    break;
                }
                case EntityKind.Tank:
                    BuildTank(e, v, root);
                    break;
                case EntityKind.Pump:
                {
                    var body = Prims.Box("PumpBody", new Vector3(0, 0.25f, 0), new Vector3(0.5f, 0.5f, 0.6f), Palette.Orange, root);
                    Prims.SetFinish(body, Palette.Orange, 0.45f);
                    if (!UseOwn("own-pump", body, root))
                    {
                        Prims.Deco(PrimitiveType.Cube, "PumpSkid", new Vector3(0, 0.025f, 0), new Vector3(0.62f, 0.05f, 0.72f), Palette.Dark, root, 0.3f, 0.5f);
                        Prims.Deco(PrimitiveType.Cylinder, "PumpDome", new Vector3(0, 0.52f, 0.1f), new Vector3(0.3f, 0.04f, 0.3f), Palette.Steel, root, 0.6f, 0.8f);
                    }
                    Prims.Label("PUMP", new Vector3(0, 0.3f, -0.305f), 0f, 0.06f, Palette.Dark, root, 0.4f);
                    SimTarget.Add(body, TargetKind.Entity, e.Id);
                    AddSocket(e, root, "intake", 0, Palette.Dark);
                    AddSocket(e, root, "outlet", 0, Palette.Marker);
                    if (_sim.Tuning.TryPoint(e.Item, "handle", 0, out var hp))
                    {
                        v.Handle = Prims.Box("CrankHandle", new Vector3((float)hp.x, (float)hp.y - 0.25f, (float)hp.z), new Vector3(0.08f, 0.5f, 0.08f), Palette.Dark, root);
                        SimTarget.Add(v.Handle, TargetKind.Handle, e.Id);
                    }
                    v.Motor = Prims.Cyl("Motor", new Vector3(0, 0.6f, 0), new Vector3(0.35f, 0.15f, 0.35f), Palette.Steel, root, false);
                    if (_sim.Tuning.TryPoint(e.Item, "lever", 0, out var lp))
                    {
                        v.Lever = Prims.Box("MotorLever", new Vector3((float)lp.x, (float)lp.y, (float)lp.z), new Vector3(0.06f, 0.2f, 0.06f), Palette.Marker, root);
                        SimTarget.Add(v.Lever, TargetKind.MotorLever, e.Id);
                    }
                    break;
                }
                case EntityKind.MotorKit:
                {
                    var b = Prims.Cyl("MotorKit", new Vector3(0, 0.15f, 0), new Vector3(0.35f, 0.15f, 0.35f), Palette.Steel, root);
                    UseOwn("own-motorkit", b, root);
                    SimTarget.Add(b, TargetKind.Entity, e.Id);
                    break;
                }
                case EntityKind.InletFitting:
                {
                    var b = Prims.Cyl("InletFitting", new Vector3(0, 0.05f, 0), new Vector3(0.18f, 0.05f, 0.18f), Palette.Marker, root);
                    UseOwn("own-inlet", b, root);
                    SimTarget.Add(b, TargetKind.Entity, e.Id);
                    break;
                }
                default:
                {
                    var b = Prims.Box(e.Item, new Vector3(0, 0.1f, 0), new Vector3(0.3f, 0.2f, 0.05f), Palette.Marker, root);
                    SimTarget.Add(b, TargetKind.Entity, e.Id);
                    break;
                }
            }
            return v;
        }

        void BuildTank(Entity e, EntityView v, Transform root)
        {
            GameObject body;
            float h;
            switch (e.Item)
            {
                case "jerrycan_10l":
                {
                    h = 0.45f;
                    var olive = new Color(0.33f, 0.4f, 0.2f);
                    body = Prims.Box("Jerrycan", new Vector3(0, h / 2, 0), new Vector3(0.18f, h, 0.32f), olive, root);
                    Prims.SetFinish(body, olive, 0.4f, 0.3f);
                    if (Own.Make("own-jerrycan", "JerrycanModel", root, Vector3.zero, 0.45f) != null)
                    {
                        // Our own model: the box stays as the (invisible) thing you aim at and bump into.
                        body.GetComponent<Renderer>().enabled = false;
                        h = 0.4f;
                        break;
                    }
                    // Three-bar handle, the spout and the pressed X on each side.
                    for (int k = -1; k <= 1; k++)
                        Prims.Deco(PrimitiveType.Cube, "HandlePost", new Vector3(0, h + 0.03f, k * 0.08f), new Vector3(0.03f, 0.06f, 0.025f), olive, root, 0.4f, 0.3f);
                    Prims.Deco(PrimitiveType.Cube, "HandleBar", new Vector3(0, h + 0.065f, 0), new Vector3(0.035f, 0.025f, 0.2f), olive, root, 0.4f, 0.3f);
                    Prims.Deco(PrimitiveType.Cylinder, "Spout", new Vector3(0, h + 0.03f, -0.13f), new Vector3(0.05f, 0.03f, 0.05f), Palette.Dark, root, 0.5f);
                    foreach (float sx in new[] { -0.091f, 0.091f })
                    foreach (float a in new[] { 50f, -50f })
                        Prims.Deco(PrimitiveType.Cube, "X", new Vector3(sx, h * 0.48f, 0), new Vector3(0.004f, 0.42f, 0.025f), new Color(0.27f, 0.33f, 0.16f), root).transform.localRotation = Quaternion.Euler(a, 0, 0);
                    break;
                }
                case "barrel_100l":
                {
                    h = 0.8f;
                    var blue = new Color(0.12f, 0.3f, 0.62f);
                    body = Prims.Cyl("Barrel", new Vector3(0, h / 2, 0), new Vector3(0.5f, h / 2, 0.5f), blue, root);
                    Prims.SetFinish(body, blue, 0.55f);
                    if (UseOwn("own-barrel", body, root)) break;
                    foreach (float y in new[] { 0.27f, 0.53f })
                        Prims.Deco(PrimitiveType.Cylinder, "Rib", new Vector3(0, y, 0), new Vector3(0.52f, 0.02f, 0.52f), blue, root, 0.55f);
                    Prims.Deco(PrimitiveType.Cylinder, "Bung", new Vector3(0.12f, h + 0.01f, 0.08f), new Vector3(0.07f, 0.015f, 0.07f), Palette.White, root, 0.4f);
                    break;
                }
                case "drum_200l":
                {
                    h = 0.9f;
                    body = Prims.Cyl("Drum", new Vector3(0, h / 2, 0), new Vector3(0.6f, h / 2, 0.6f), Palette.Steel, root);
                    Prims.SetFinish(body, Palette.Steel, 0.6f, 0.75f);
                    Prims.Label("200 L", new Vector3(0, 0.45f, -0.305f), 0f, 0.06f, Palette.White, root, 0.3f);
                    if (UseOwn("own-drum", body, root)) break;
                    foreach (float y in new[] { 0.3f, 0.6f })
                        Prims.Deco(PrimitiveType.Cylinder, "Hoop", new Vector3(0, y, 0), new Vector3(0.625f, 0.018f, 0.625f), Palette.Steel, root, 0.6f, 0.8f);
                    Prims.Deco(PrimitiveType.Cylinder, "Chime", new Vector3(0, h, 0), new Vector3(0.62f, 0.012f, 0.62f), Palette.Dark, root, 0.5f, 0.8f);
                    break;
                }
                default:
                {
                    h = 1.15f;
                    body = Prims.Box("IBC", new Vector3(0, h / 2, 0), new Vector3(1.0f, h, 1.2f), Palette.White, root);
                    Prims.SetFinish(body, new Color(0.9f, 0.9f, 0.86f), 0.7f);
                    if (UseOwn("own-ibc", body, root)) break;
                    // Steel cage and pallet.
                    Prims.Deco(PrimitiveType.Cube, "Pallet", new Vector3(0, 0.06f, 0), new Vector3(1.04f, 0.12f, 1.24f), Palette.Dark, root, 0.3f, 0.5f);
                    for (int k = 0; k < 4; k++)
                        Prims.Deco(PrimitiveType.Cube, "CagePost", new Vector3(k % 2 == 0 ? -0.51f : 0.51f, h / 2, k < 2 ? -0.61f : 0.61f), new Vector3(0.03f, h, 0.03f), Palette.Steel, root, 0.5f, 0.8f);
                    for (float y = 0.4f; y < h; y += 0.35f)
                    {
                        Prims.Deco(PrimitiveType.Cube, "CageRing", new Vector3(0, y, -0.61f), new Vector3(1.04f, 0.025f, 0.025f), Palette.Steel, root, 0.5f, 0.8f);
                        Prims.Deco(PrimitiveType.Cube, "CageRing", new Vector3(0, y, 0.61f), new Vector3(1.04f, 0.025f, 0.025f), Palette.Steel, root, 0.5f, 0.8f);
                        Prims.Deco(PrimitiveType.Cube, "CageRing", new Vector3(-0.51f, y, 0), new Vector3(0.025f, 0.025f, 1.24f), Palette.Steel, root, 0.5f, 0.8f);
                        Prims.Deco(PrimitiveType.Cube, "CageRing", new Vector3(0.51f, y, 0), new Vector3(0.025f, 0.025f, 1.24f), Palette.Steel, root, 0.5f, 0.8f);
                    }
                    break;
                }
            }
            SimTarget.Add(body, TargetKind.Entity, e.Id);
            // Float gauge on the front face.
            var gauge = Prims.Box("Gauge", new Vector3(0, 0, -0.36f), new Vector3(0.08f, 0.01f, 0.02f), Palette.Water, root, false);
            gauge.GetComponent<Renderer>().sharedMaterial = Prims.Mat(Palette.Water, 0.8f, 0f, new Color(0.05f, 0.25f, 0.6f));
            v.Gauge = gauge.transform;
            v.GaugeHeight = h;
            AddSocket(e, root, "inlet", 0, Palette.Marker);
            AddSocket(e, root, "inlet", 1, Palette.Marker);
            AddSocket(e, root, "link", 0, Palette.Corporate);
        }

        void AddSocket(Entity e, Transform root, string kind, int index, Color c)
        {
            if (!_sim.Tuning.TryPoint(e.Item, kind, index, out var p)) return;
            // Link sockets sit low on the front, so they are a little larger and glow teal to be easy to find.
            var s = Prims.Ball($"Socket_{kind}_{index}", new Vector3((float)p.x, (float)p.y, (float)p.z), kind == "link" ? 0.16f : 0.12f, c, root);
            if (kind == "link") s.GetComponent<Renderer>().sharedMaterial = Prims.Mat(c, 0.6f, 0f, new Color(0.05f, 0.5f, 0.45f));
            SimTarget.Add(s, TargetKind.Socket, e.Id, kind, index);
        }

        void UpdateEntityDetails(Entity e, EntityView v)
        {
            if (e.Kind == EntityKind.Tank && v.Gauge != null)
            {
                var t = _sim.Network.Tank(e.Id);
                double cap = t == null ? 1 : _sim.Network.TankCapacity(t);
                float f = t == null ? 0 : Mathf.Clamp01((float)(t.Contents / cap));
                float gh = Mathf.Max(0.01f, f * v.GaugeHeight);
                v.Gauge.localScale = new Vector3(0.08f, gh, 0.02f);
                v.Gauge.localPosition = new Vector3(0, gh / 2f, e.Item == "ibc_tank" ? -0.61f : e.Item == "jerrycan_10l" ? -0.17f : -0.31f);
            }
            if (e.Kind == EntityKind.Pump)
            {
                var p = _sim.Pumps.Get(e.Id);
                bool motor = p != null && _sim.Pumps.IsElectric(p);
                if (v.Handle != null) v.Handle.SetActive(!motor);
                if (v.Motor != null) v.Motor.SetActive(motor);
                if (v.Lever != null)
                {
                    v.Lever.SetActive(motor);
                    v.Lever.transform.localRotation = Quaternion.Euler(p != null && p.LeverOn ? -35f : 35f, 0, 0);
                }
                if (v.Handle != null && motor == false)
                {
                    var st = _sim.Pumps.StateOf(e.Id);
                    float swing = st == PumpRunState.Running || st == PumpRunState.Stalled ? Mathf.Sin(Time.time * Mathf.PI * 2f / 0.8f) * 30f : 0f;
                    v.Handle.transform.localRotation = Quaternion.Euler(swing, 0, 0);
                }
            }
        }

        // ------------------------------------------------------------ hoses and cables

        void SyncHoses()
        {
            foreach (var h in _sim.State.Network.Hoses)
            {
                if (!_hoses.TryGetValue(h.Id, out var v)) { v = MakeHose(h); _hoses[h.Id] = v; }
                _sim.Network.TryEndPose(h.Id, 0, out var p0);
                _sim.Network.TryEndPose(h.Id, 1, out var p1);
                var a = LocalEnd(h, 0, p0);
                var b = LocalEnd(h, 1, p1);
                bool coil = !h.Ends[0].Target.IsSet && !h.Ends[1].Target.IsSet && !IsHeld(h.Id, 0) && !IsHeld(h.Id, 1) && (a - b).sqrMagnitude < 0.01f;
                v.Coil.SetActive(coil);
                // Anything riding along with the carried item must not be solid, or the player stands on it and
                // is lifted every frame ("flying" while carrying a pump with its hose attached).
                bool onHeld0 = EndOnHeldEntity(h, 0), onHeld1 = EndOnHeldEntity(h, 1);
                var coilCol = v.Coil.GetComponent<Collider>();
                if (coilCol != null) coilCol.enabled = !(onHeld0 || onHeld1 || IsHeld(h.Id, 0) || IsHeld(h.Id, 1));
                v.Coil.transform.localPosition = a + new Vector3(0, 0.08f, 0);
                v.Line.enabled = !coil;
                if (!coil) SetSag(v.Line, a, b);
                for (int i = 0; i < 2; i++)
                {
                    bool held = IsHeld(h.Id, i);
                    var end = v.Ends[i];
                    end.SetActive(!coil || i == 0);
                    end.transform.localPosition = i == 0 ? a : b;
                    var col = end.GetComponent<Collider>();
                    if (col != null) col.enabled = !held && !(i == 0 ? onHeld0 : onHeld1);
                }
            }
        }

        bool EndOnHeldEntity(HoseState h, int idx)
        {
            var t = h.Ends[idx].Target;
            if (!t.IsSet || t.Kind != EndTargetKind.Socket) return false;
            var e = _sim.Placement.Get(t.NodeId);
            return e != null && e.HeldBy >= 0;
        }

        /// <summary>A hose end in the farm's local frame (hoses and cables draw in local space).</summary>
        Vector3 LocalEnd(HoseState h, int idx, Pose p)
        {
            if (IsHeld(h.Id, idx) && _hands != null) return Local(_hands.TransformPoint(new Vector3(0.25f, -0.4f, 0.7f)));
            if (h.Ends[idx].Target.Kind == EndTargetKind.Intake)
            {
                // The intake end follows the water down (presentation only).
                float y = _level.PondWater.localPosition.y;
                return new Vector3((float)p.X, y + 0.05f, (float)p.Z);
            }
            var v = p.ToLocal();
            if (!h.Ends[idx].Target.IsSet) v.y = Mathf.Max(v.y, 0.05f);
            return v;
        }

        Vector3 Local(Vector3 world) => _level.Root.InverseTransformPoint(world);

        bool IsHeld(int hoseId, int end)
        {
            var hands = _sim.Placement.Hands(0);
            return hands.Kind == HandsKind.HoseEnd && hands.Id == hoseId && hands.EndIdx == end;
        }

        HoseView MakeHose(HoseState h)
        {
            var v = new HoseView();
            var g = new GameObject($"Hose_{h.Kind}_{h.Id}");
            g.transform.SetParent(_level.Root, false);
            v.Line = g.AddComponent<LineRenderer>();
            v.Line.useWorldSpace = false;
            v.Line.widthMultiplier = h.Kind == HoseKind.Suction ? 0.07f : 0.06f;
            v.Line.positionCount = 12;
            v.Line.sharedMaterial = Prims.Mat(h.Kind == HoseKind.Discharge ? new Color(0.85f, 0.75f, 0.2f) : h.Kind == HoseKind.Link ? Palette.Corporate : Palette.Dark);
            for (int i = 0; i < 2; i++)
            {
                v.Ends[i] = Prims.Ball($"HoseEnd_{i}", Vector3.zero, 0.13f, h.Kind == HoseKind.Suction ? Palette.Dark : Palette.Orange, g.transform);
                SimTarget.Add(v.Ends[i], TargetKind.HoseEnd, h.Id, "", i);
            }
            v.Coil = Prims.Cyl("Coil", Vector3.zero, new Vector3(0.5f, 0.08f, 0.5f), h.Kind == HoseKind.Link ? Palette.Corporate : new Color(0.85f, 0.75f, 0.2f), g.transform);
            SimTarget.Add(v.Coil, TargetKind.HoseEnd, h.Id, "", 0);
            return v;
        }

        static void SetSag(LineRenderer lr, Vector3 a, Vector3 b)
        {
            int n = lr.positionCount;
            float len = Vector3.Distance(a, b);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                var p = Vector3.Lerp(a, b, t);
                float sag = Mathf.Sin(t * Mathf.PI) * Mathf.Min(0.6f, len * 0.08f);
                p.y = Mathf.Max(0.03f, p.y - sag);
                lr.SetPosition(i, p);
            }
        }

        void SyncCables()
        {
            foreach (var p in _sim.State.Pumps.Pumps)
            {
                if (!p.HasCable) continue;
                var e = _sim.Placement.Get(p.Id);
                if (e == null) continue;
                if (!_cables.TryGetValue(p.Id, out var v))
                {
                    v = new CableView();
                    var g = new GameObject($"Cable_{p.Id}");
                    g.transform.SetParent(_level.Root, false);
                    v.Line = g.AddComponent<LineRenderer>();
                    v.Line.useWorldSpace = false;
                    v.Line.widthMultiplier = 0.03f;
                    v.Line.positionCount = 12;
                    v.Line.sharedMaterial = Prims.Mat(Palette.Dark);
                    v.End = Prims.Box("CablePlug", Vector3.zero, new Vector3(0.1f, 0.1f, 0.16f), Palette.Orange, g.transform);
                    SimTarget.Add(v.End, TargetKind.CableEnd, p.Id);
                    _cables[p.Id] = v;
                }
                Vector3 port = e.HeldBy >= 0 && _hands != null ? Local(_hands.position) : e.Pose.ToLocal() + Vector3.up * 0.3f;
                if (_sim.Tuning.TryPoint(e.Item, "cable_port", 0, out var cp) && e.HeldBy < 0) port = e.Pose.Offset(cp.x, cp.y, cp.z).ToLocal();
                Vector3 end;
                var hands = _sim.Placement.Hands(0);
                bool held = hands.Kind == HandsKind.CableEnd && hands.Id == p.Id;
                if (held && _hands != null) end = Local(_hands.TransformPoint(new Vector3(0.25f, -0.4f, 0.7f)));
                else { _sim.Pumps.TryCableEndPose(p.Id, out var cpose); end = cpose.ToLocal(); if (p.CableSocket < 0) end.y = Mathf.Max(end.y, 0.05f); }
                SetSag(v.Line, port, end);
                v.End.transform.localPosition = end;
                var col = v.End.GetComponent<Collider>();
                if (col != null) col.enabled = !held && !(e.HeldBy >= 0 && p.CableSocket < 0);
            }
        }
    }
}
