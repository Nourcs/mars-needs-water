using System.Collections.Generic;
using MarsNeedsWater.Simulation;
using Pose = MarsNeedsWater.Simulation.Pose;
using UnityEngine;
using UnityEngine.InputSystem;
using Sim = MarsNeedsWater.Simulation.Simulation;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// First-Person Controller &amp; Interaction (greybox version): CharacterController movement, mouse look,
    /// one interaction ray, the context table (hands × target -> request), hold actions, the tool input gate,
    /// the placement ghost and rejection text. Sends requests only; never changes state.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour, ISimEventListener
    {
        // Movement (FPC Rule 1). Greybox values; move to an InteractionTuning asset with the Input Actions asset.
        const float Walk = 4f, Swim = 3.2f, Sprint = 6.5f, Crouch = 2f, JumpHeight = 1f, LookSens = 0.1f;
        // Explore boost: holding Shift builds up from a sprint to a very fast run over about two seconds.
        const float BoostMax = 200f, BoostRamp = 120f;
        const float LeapHeight = 150f, LeapPush = 2.2f; // a full-speed leap: 150 m up, ~11 s in the air, several km long
        const float FlySpeed = 14f, FlyFast = 90f;
        bool _flying;
        float _lastSpace = -10f;
        float _leapSpeed;
        float _boost;
        Vector3 _leapDir;
        const float RayLength = 3.2f, TapThreshold = 0.25f, BufferWindow = 0.15f, SwitchLock = 0.3f;

        Sim _sim;
        CharacterController _cc;
        Transform _cam;
        float _pitch, _yVel;

        public Transform HandsAnchor { get; private set; }
        public Camera Camera { get; private set; }
        public ThirdPerson View { get; private set; }

        /// <summary>
        /// The aiming ray: through the middle of the screen (where the crosshair is), starting level with the eye,
        /// so in third person you aim where you look but still reach only as far as the character could.
        /// </summary>
        Ray AimRay()
        {
            var c = Camera != null ? Camera.transform : _cam;
            var dir = c.forward;
            float skip = Mathf.Max(0f, Vector3.Dot(_cam.position - c.position, dir));
            return new Ray(c.position + dir * skip, dir);
        }

        // Requests buffered until the next tick, sent after PlayerPose (FPC Rule 6).
        readonly List<Request> _outbox = new List<Request>();
        string _lastRequestName;

        // Prompt and feedback (read by the HUD).
        public string Prompt { get; private set; } = "";
        public bool PromptGrey { get; private set; }
        /// <summary>The last sea or lake the player aimed at (its source id), for the HUD; 0 = none yet.</summary>
        public int LastWorldWater { get; private set; }
        public string Rejection { get; private set; } = "";
        float _rejectionUntil;
        public float HoldProgress { get; private set; } = -1f;

        // Hold action in progress.
        Action _hold;
        float _holdStart;
        SimTarget _holdTarget;
        int _crankPump;

        // Tool input gate (Hand Tools Rule 10), presentation clock.
        readonly Dictionary<string, float> _scoopReadyAt = new Dictionary<string, float>();
        float _pourLockUntil, _switchLockUntil;
        Action _buffered;
        float _bufferedAt = -1f;

        // Placement ghost.
        GameObject _ghost;
        float _ghostYaw;
        bool _ghostValid;
        Pose _ghostPose;

        /// <summary>A resolved context action for the current target.</summary>
        sealed class Action
        {
            public string Prompt;
            public System.Func<Request> Make;
            public float Hold;          // > 0: a fixed hold (pry, bolt, fit)
            public int CrankPump;       // > 0: a crank hold on this pump
            public bool ToolUse;        // scoop or pour: goes through the tool gate
            public bool IsScoop;
            public string Tool;
            public bool Grey;
        }

        public void Init(Sim sim, Pose start)
        {
            _sim = sim;
            _cc = gameObject.AddComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.3f;
            _cc.center = new Vector3(0, 0.9f, 0);
            _cc.slopeLimit = 40f;
            _cc.stepOffset = 0.3f;
            transform.position = start.ToV3();
            transform.rotation = start.ToRot();

            // The eye: head height, carries the mouse pitch; aiming, reach and held tools hang off it. The real
            // camera is its child: at the eye in first person, over the shoulder in third (see ThirdPerson).
            _cam = new GameObject("Eye").transform;
            _cam.SetParent(transform, false);
            _cam.localPosition = new Vector3(0, (float)sim.Tuning.Data.eye_height_m, 0);
            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            Camera = camGo.AddComponent<Camera>();
            Camera.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.SetParent(_cam, false);

            HandsAnchor = new GameObject("Hands").transform;
            HandsAnchor.SetParent(_cam, false);
            View = gameObject.AddComponent<ThirdPerson>();
            View.Init(transform, _cam, camGo.transform, _cc);

            _sim.Bus.Subscribe(this);
            LockCursor(true);
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Driver calls this before every tick: PlayerPose first, then buffered interaction requests.</summary>
        public void SendForTick()
        {
            _sim.Submit(new PlayerPoseRequest { Player = 0, Pose = transform.ToPose() });
            foreach (var r in _outbox) _sim.Submit(r);
            _outbox.Clear();
        }

        void Send(Request r)
        {
            r.Player = 0;
            _outbox.Add(r);
            if (!(r is CrankStopRequest)) _lastRequestName = r.Name;
        }

        public void OnSimEvent(in SimEvent e, SimState state)
        {
            if (e.Type != SimEventType.RequestRejected || e.Player != 0) return;
            if (e.Text2 == "PlayerPose" || e.Text2 == "CrankStop" || e.Text2 == "PropSettled") return;
            Rejection = RejectionText.For(e.Text2 ?? _lastRequestName, e.Text);
            if (e.Text == Codes.CannotAfford)
            {
                var n = _sim.Economy.NextItem();
                if (n != null) Rejection = $"Short {n.price_cr - _sim.Economy.Credits} cr";
            }
            if (e.Text == Codes.PadBusy) Rejection = $"Next rocket in {Mathf.CeilToInt((float)_sim.Shipping.SecondsUntilReady())} s";
            _rejectionUntil = Time.unscaledTime + 1.5f;
        }

        /// <summary>Cancels holds and releases any crank (pause or menus; FPC Rule 10).</summary>
        public void CancelHolds()
        {
            if (_crankPump > 0) { Send(new CrankStopRequest { Pump = _crankPump }); _crankPump = 0; }
            _hold = null;
            HoldProgress = -1f;
        }

        void Update()
        {
            if (_sim == null || _sim.Run != RunState.Running) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (mouse.leftButton.wasPressedThisFrame) LockCursor(true);
                return;
            }
            Look(mouse);
            bool moved = Move(kb);
            if (Time.unscaledTime > _rejectionUntil) Rejection = "";

            var hit = CastRay(out var hitPoint, out var hitCollider);
            var action = Resolve(hit, hitPoint, hitCollider);
            UpdateGhost(hit, hitPoint, hitCollider);

            Prompt = action?.Prompt ?? (_ghost != null && _ghost.activeSelf ? (_ghostValid ? "Place (E)" : GhostReason) : "");
            PromptGrey = action?.Grey ?? false;

            bool usePressed = kb.eKey.wasPressedThisFrame || mouse.leftButton.wasPressedThisFrame;
            bool useHeld = kb.eKey.isPressed || mouse.leftButton.isPressed;

            // Holds and the crank.
            if (_crankPump > 0)
            {
                bool stillOn = action != null && action.CrankPump == _crankPump;
                if (!useHeld || moved || !stillOn) { Send(new CrankStopRequest { Pump = _crankPump }); _crankPump = 0; }
            }
            if (_hold != null)
            {
                bool sameTarget = hit != null && hit == _holdTarget;
                if (!useHeld || moved || !sameTarget) { _hold = null; HoldProgress = -1f; }
                else
                {
                    float t = Time.unscaledTime - _holdStart;
                    HoldProgress = t > TapThreshold ? Mathf.Clamp01(t / _hold.Hold) : -1f;
                    if (t >= _hold.Hold) { Send(_hold.Make()); _hold = null; HoldProgress = -1f; }
                }
            }

            if (usePressed)
            {
                if (action != null) Use(action, hit);
                else if (_ghost != null && _ghost.activeSelf && _ghostValid) Send(new PlaceItemRequest { Pose = _ghostPose });
            }
            FlushBuffer();

            if (kb.gKey.wasPressedThisFrame) DropHeld();
            if (kb.digit1Key.wasPressedThisFrame) SelectTool("spoon");
            if (kb.digit2Key.wasPressedThisFrame) SelectTool("bucket");
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0)
            {
                if (_ghost != null && _ghost.activeSelf) _ghostYaw += Mathf.Sign(scroll) * 15f;
                else
                {
                    var h = _sim.Placement.Hands(0);
                    if (h.Kind == HandsKind.Tool) SelectTool(h.Tool == "spoon" ? "bucket" : "spoon");
                }
            }
        }

        // ------------------------------------------------------------ movement

        void Look(Mouse mouse)
        {
            Vector2 d = mouse.delta.ReadValue() * LookSens;
            transform.Rotate(0, d.x, 0);
            _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
            _cam.localEulerAngles = new Vector3(_pitch, 0, 0);
        }

        bool Move(Keyboard kb)
        {
            Vector3 dir = Vector3.zero;
            if (kb.wKey.isPressed) dir += transform.forward;
            if (kb.sKey.isPressed) dir -= transform.forward;
            if (kb.dKey.isPressed) dir += transform.right;
            if (kb.aKey.isPressed) dir -= transform.right;
            bool moving = dir.sqrMagnitude > 0.01f;
            // Fly mode (for looking around and taking screenshots): tap Space twice to take off, twice again to drop.
            // Space climbs, C or Ctrl sinks, Shift goes fast. You hang still when you let go of the keys.
            if (kb.spaceKey.wasPressedThisFrame)
            {
                if (Time.unscaledTime - _lastSpace < 0.3f) { _flying = !_flying; _lastSpace = -10f; _yVel = 0f; _boost = 0f; _leapSpeed = 0f; }
                else _lastSpace = Time.unscaledTime;
            }
            if (_flying)
            {
                bool fast = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                float climb = (kb.spaceKey.isPressed ? 1f : 0f) - (kb.cKey.isPressed || kb.leftCtrlKey.isPressed ? 1f : 0f);
                float fly = fast ? FlyFast : FlySpeed;
                _cc.Move((dir.normalized * fly + Vector3.up * climb * fly * 0.6f) * Time.deltaTime);
                var here = Planet.Current;
                if (here != null)
                {
                    float floor = here.SurfaceY(transform.position, false);
                    if (transform.position.y < floor + 0.1f)
                    {
                        _cc.enabled = false;
                        transform.position = new Vector3(transform.position.x, floor + 0.1f, transform.position.z);
                        _cc.enabled = true;
                    }
                }
                return moving;
            }
            var hands = _sim.Placement.Hands(0);
            bool heavy = false;
            float carry = 1f;
            if (hands.Kind == HandsKind.Tool && hands.Tool == "bucket" && _sim.HandTools.IsFull(0, "bucket")) carry = 0.75f;
            if (hands.Kind == HandsKind.HoseEnd || hands.Kind == HandsKind.CableEnd) carry = 0.9f;
            if (hands.Kind == HandsKind.Entity)
            {
                var e = _sim.Placement.Get(hands.Id);
                if (e != null && (e.Kind == EntityKind.Crate || e.Kind == EntityKind.Tank)) { carry = 0.7f; heavy = true; }
                if (e != null && e.Kind == EntityKind.Pump) { carry = 0.6f; heavy = true; }
            }
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (shift && moving && !heavy) _boost = Mathf.Min(BoostMax, Mathf.Max(Sprint, _boost) + BoostRamp * Time.deltaTime);
            else if (_cc.isGrounded) _boost = 0f; // in a leap you keep your speed until you land
            if (_cc.isGrounded && _yVel <= 0f) _leapSpeed = 0f;
            if (!_cc.isGrounded && (_boost > Sprint || _leapSpeed > 0f) && _leapDir.sqrMagnitude > 0.01f && !moving) dir = _leapDir;
            float speed = _boost > 0f ? _boost : (kb.cKey.isPressed || kb.leftCtrlKey.isPressed ? Crouch : Walk);
            if (_leapSpeed > speed) speed = _leapSpeed;
            // Wading: water deeper than 0.3 m under the player (pond only in the greybox).
            float ground = 1f;
            var pr = GreyboxLevel.PondRect;
            var pos = transform.position;
            if (pr.Contains(new Vector2(pos.x, pos.z)))
            {
                double frac = _sim.WaterSource.Available(GreyboxLevel.PondId) / _sim.WaterSource.Start(GreyboxLevel.PondId);
                float waterY = GreyboxLevel.PondHeight(frac);
                if (waterY - pos.y > 0.3f) ground = 0.6f;
                else if (waterY < pos.y) ground = 0.85f; // exposed lakebed: mud
            }
            // Seas and lakes: at boost speed you run across the top; otherwise you sink in and swim. Space swims up
            // (and keeps your head above water at the surface), C or Ctrl dives; left alone you drift slowly down.
            var planet = Planet.Current;
            float seaY = planet != null ? planet.WaterY(pos) : float.MinValue;
            if (!shift && pos.y < seaY + 0.2f && !_cc.isGrounded) { _boost = 0f; _leapSpeed = 0f; } // let go on water: in you go
            bool skimming = _boost > Sprint || _leapSpeed > 0f;
            bool swimming = !skimming && pos.y < seaY - 1.1f;
            bool surfacing = skimming && pos.y < seaY - 2f; // boosting from under water: shoot up to the top first
            if (swimming)
            {
                bool swimUp = kb.spaceKey.isPressed, dive = kb.cKey.isPressed || kb.leftCtrlKey.isPressed;
                float target = swimUp ? (pos.y > seaY - 1.3f ? 0f : 3f) : dive ? -3.5f : -0.6f;
                _yVel = Mathf.MoveTowards(_yVel, target, 8f * Time.deltaTime);
                speed = Swim;
            }
            else if (surfacing) _yVel = 40f;
            else if (_cc.isGrounded)
            {
                _yVel = -1f;
                if (kb.spaceKey.wasPressedThisFrame && !heavy)
                {
                    // Hulk leap: the faster you are going, the higher and farther you jump (full speed: 150 m up, ~5 km long).
                    float k = Mathf.Clamp01((_boost - Sprint) / (BoostMax - Sprint));
                    _yVel = Mathf.Sqrt(2f * -Physics.gravity.y * Mathf.Lerp(JumpHeight, LeapHeight, k));
                    _leapDir = dir.normalized;
                    _leapSpeed = k > 0f ? _boost * Mathf.Lerp(1f, LeapPush, k) : 0f;
                }
            }
            if (!swimming && !surfacing) _yVel += Physics.gravity.y * Time.deltaTime;
            _cc.Move((dir.normalized * speed * (_boost > Sprint || _leapSpeed > 0f ? 1f : carry * ground) + Vector3.up * _yVel) * Time.deltaTime);
            // Running faster than the ground's colliders are made: never sink below the planet's surface.
            if (planet != null)
            {
                float surface = planet.SurfaceY(transform.position, skimming && !surfacing);
                if (transform.position.y < surface - 0.3f)
                {
                    _cc.enabled = false;
                    transform.position = new Vector3(transform.position.x, surface + 0.1f, transform.position.z);
                    _cc.enabled = true;
                    _yVel = 0f;
                }
            }
            if (transform.position.y < -400f) { _cc.enabled = false; transform.position = _sim.Level.PlayerStart.ToV3() + transform.up * 0.1f; _cc.enabled = true; } // fell through the world: back to the farm
            return moving;
        }

        // ------------------------------------------------------------ interaction

        SimTarget CastRay(out Vector3 point, out Collider col)
        {
            point = default;
            col = null;
            var ray = AimRay();
            var hits = Physics.RaycastAll(ray, RayLength, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            SimTarget bestT = null;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                var t = h.collider.GetComponentInParent<SimTarget>();
                // Ignore decorative triggers (spot markers) unless they mean something.
                if (t == null && h.collider.isTrigger) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    bestT = t;
                    point = h.point;
                    col = h.collider;
                }
            }
            return bestT;
        }

        Action Resolve(SimTarget t, Vector3 point, Collider col)
        {
            if (t == null) return null;
            var hands = _sim.Placement.Hands(0);
            float dist = Vector3.Distance(_cam.position, point);
            var d = _sim.Tuning.Data;
            switch (t.Kind)
            {
                case TargetKind.Water:
                {
                    if (dist > d.range_default_m + 0.3f) return null;
                    // The planet's shared sea-level surface (Id -1): which sea or lake is it here?
                    int waterId = t.Id;
                    if (waterId < 0)
                    {
                        waterId = Planet.Current != null ? WorldWaters.SourceAt(Planet.Current.WorldToDir(point)) : 0;
                        if (waterId == 0 || !_sim.WaterSource.Exists(waterId)) return null;
                        LastWorldWater = waterId;
                    }
                    string where = WorldWaters.IsWorld(waterId) ? " from the " + WorldWaters.Name(waterId).Replace("Lake Lake ", "Lake ") : "";
                    if (where.StartsWith(" from the Lake ") || where.StartsWith(" from the Lago ")) where = where.Replace(" from the ", " from ");
                    if (hands.Kind == HandsKind.Tool)
                    {
                        string tool = hands.Tool;
                        bool full = _sim.HandTools.IsFull(0, tool);
                        bool dry = _sim.WaterSource.State(waterId) == SourceCondition.Drained;
                        int src = waterId;
                        var aim = point.ToPose();
                        return new Action
                        {
                            Prompt = full ? "Full: go pour it" : dry ? "Dry. Nothing left here" : $"Scoop{where} (E)",
                            Grey = full || dry,
                            Make = () => new ScoopRequest { Tool = tool, Source = src, AimPoint = aim },
                            ToolUse = true, IsScoop = true, Tool = tool,
                        };
                    }
                    if (hands.Kind == HandsKind.HoseEnd)
                    {
                        var h = _sim.Network.Hose(hands.Id);
                        if (h != null && h.Kind == HoseKind.Suction && hands.EndIdx == 1)
                        {
                            int hoseId = h.Id, src = waterId;
                            var lp = point.ToPose(); // farm frame
                            var aim = new Pose(lp.X, 0, lp.Z);
                            var target = EndTarget.IntakeZone(src, aim);
                            string why = _sim.Network.CanConnect(0, hoseId, 1, target);
                            return new Action
                            {
                                Prompt = why == null ? "Drop in (E)" : RejectionText.For("Connect", why),
                                Grey = why != null,
                                Make = () => new ConnectRequest { Hose = hoseId, EndIdx = 1, Target = target },
                            };
                        }
                    }
                    return null;
                }
                case TargetKind.LaunchLever:
                {
                    if (dist > d.range_lever_m + 0.3f) return null;
                    var pool = _sim.Network.PadPool();
                    double litres = System.Math.Min(pool?.Contents ?? 0, d.cargo_capacity_L);
                    long pays = _sim.Economy.PayoutFor(litres) / 1000;
                    bool busy = _sim.State.Shipping.State != PadState.Ready;
                    bool empty = litres < d.min_launch_L;
                    return new Action
                    {
                        Prompt = busy ? $"Next rocket in {Mathf.CeilToInt((float)_sim.Shipping.SecondsUntilReady())} s" : empty ? "No cargo: fill the tank" : $"Launch: pays {pays} cr (E)",
                        Grey = busy || empty,
                        Make = () => new LaunchRequest(),
                    };
                }
                case TargetKind.OrderLever:
                {
                    if (dist > d.range_lever_m + 0.3f) return null;
                    var next = _sim.Economy.NextItem();
                    if (next == null) return new Action { Prompt = "Catalogue complete", Grey = true, Make = null };
                    string id = next.id;
                    bool poor = _sim.Economy.Credits < next.price_cr;
                    return new Action
                    {
                        Prompt = poor ? $"Order: {next.displayName}, {next.price_cr} cr (short {next.price_cr - _sim.Economy.Credits} cr)" : $"Order: {next.displayName}, {next.price_cr} cr (E)",
                        Grey = poor,
                        Make = () => new OrderRequest { Item = id },
                    };
                }
                case TargetKind.Handle:
                {
                    if (dist > d.range_crank_m + 0.4f) return null;
                    if (hands.IsCarried) return null;
                    int pump = t.Id;
                    string missing = _sim.Pumps.MissingPart(pump);
                    string extra = missing == "no_intake" ? " — No intake: connect the suction hose to the water"
                        : missing == "no_outlet" ? " — No outlet: run a hose to the tank" : "";
                    return new Action { Prompt = "Hold to crank (E)" + extra, CrankPump = pump, Make = () => new CrankStartRequest { Pump = pump } };
                }
                case TargetKind.MotorLever:
                {
                    if (dist > d.range_lever_m + 0.3f) return null;
                    var p = _sim.Pumps.Get(t.Id);
                    if (p == null) return null;
                    bool on = !p.LeverOn;
                    int pump = p.Id;
                    string missing = _sim.Pumps.MissingPart(pump);
                    string extra = missing == "no_power" ? " — No power: plug in the cable" : missing == "no_intake" ? " — No intake" : missing == "no_outlet" ? " — No outlet" : "";
                    return new Action { Prompt = (on ? "Switch on (E)" : "Switch off (E)") + extra, Make = () => new SetSwitchRequest { Pump = pump, On = on } };
                }
                case TargetKind.PostSocket:
                {
                    if (dist > d.range_default_m + 0.3f) return null;
                    int socket = t.Index;
                    if (hands.Kind == HandsKind.CableEnd)
                    {
                        int pump = hands.Id;
                        return new Action { Prompt = "Plug in (E)", Make = () => new PlugCableRequest { Pump = pump, Socket = socket } };
                    }
                    foreach (var p in _sim.State.Pumps.Pumps)
                        if (p.CableSocket == socket)
                        {
                            int pump = p.Id;
                            return new Action { Prompt = "Unplug (E)", Make = () => new UnplugCableRequest { Pump = pump } };
                        }
                    return null;
                }
                case TargetKind.CableEnd:
                {
                    if (dist > d.range_default_m + 0.3f) return null;
                    var p = _sim.Pumps.Get(t.Id);
                    if (p == null) return null;
                    int pump = p.Id;
                    if (p.CableSocket >= 0) return new Action { Prompt = "Unplug (E)", Make = () => new UnplugCableRequest { Pump = pump } };
                    if (hands.IsCarried) return null;
                    return new Action { Prompt = "Pick up cable end (E)", Make = () => new PickUpRequest { Kind = PickUpKind.CableEnd, Id = pump } };
                }
                case TargetKind.HoseEnd:
                {
                    if (dist > d.range_default_m + 0.3f) return null;
                    var h = _sim.Network.Hose(t.Id);
                    if (h == null) return null;
                    int hoseId = h.Id, end = t.Index;
                    if (h.Ends[end].Target.IsSet)
                    {
                        if (h.Kind == HoseKind.Suction && end == 0) return null;
                        return new Action { Prompt = "Unplug (E)", Make = () => new DisconnectRequest { Hose = hoseId, EndIdx = end } };
                    }
                    if (hands.IsCarried) return null;
                    bool coil = !h.Ends[0].Target.IsSet && !h.Ends[1].Target.IsSet;
                    return new Action { Prompt = coil ? $"Pick up {h.Kind.ToString().ToLower()} hose (E)" : "Pick up hose end (E)", Make = () => new PickUpRequest { Kind = PickUpKind.HoseEnd, Id = hoseId, EndIdx = end } };
                }
                case TargetKind.Socket:
                    return ResolveSocket(t, hands, dist);
                case TargetKind.Entity:
                    return ResolveEntity(t, hands, dist);
                case TargetKind.StagingSpot:
                    return null;
            }
            return null;
        }

        /// <summary>Says where this hose does go, instead of a bare "Wrong fitting".</summary>
        static string WrongFittingHint(HoseKind? kind)
        {
            switch (kind)
            {
                case HoseKind.Link: return "Wrong fitting: a link hose joins two teal LINK sockets (200 L drum, IBC)";
                case HoseKind.Discharge: return "Wrong fitting: a discharge hose runs from a pump outlet to a yellow tank inlet";
                case HoseKind.Suction: return "Wrong fitting: drop the suction hose's free end in the pond";
                default: return "Wrong fitting";
            }
        }

        Action ResolveSocket(SimTarget t, HandsItem hands, float dist)
        {
            if (dist > _sim.Tuning.Data.range_default_m + 0.3f) return null;
            var target = EndTarget.At(t.Id, t.Socket, t.Index);
            if (hands.Kind == HandsKind.HoseEnd)
            {
                int hoseId = hands.Id, end = hands.EndIdx;
                string why = _sim.Network.CanConnect(0, hoseId, end, target);
                string text = why == null ? "Plug in (E)" : RejectionText.For("Connect", why);
                if (why == Codes.WrongSocket) text = WrongFittingHint(_sim.Network.Hose(hoseId)?.Kind);
                return new Action { Prompt = text, Grey = why != null, Make = () => new ConnectRequest { Hose = hoseId, EndIdx = end, Target = target } };
            }
            if (hands.Kind == HandsKind.Entity && t.Socket == "inlet")
            {
                var e = _sim.Placement.Get(hands.Id);
                if (e != null && e.Kind == EntityKind.InletFitting)
                {
                    int tank = t.Id;
                    return new Action { Prompt = "Hold to fit inlet (E)", Hold = 2f, Make = () => new FitInletRequest { Tank = tank } };
                }
            }
            // A plugged hose end in this socket can be unplugged.
            foreach (var h in _sim.State.Network.Hoses)
                for (int i = 0; i < 2; i++)
                    if (h.Ends[i].Target.SameSocket(target) && !(h.Kind == HoseKind.Suction && i == 0))
                    {
                        int hoseId = h.Id, end = i;
                        return new Action { Prompt = "Unplug (E)", Make = () => new DisconnectRequest { Hose = hoseId, EndIdx = end } };
                    }
            var tankSt = _sim.Network.Tank(t.Id);
            if (tankSt != null && t.Socket == "inlet" && !tankSt.InletFitted) return new Action { Prompt = "No inlet: screw in the fitting", Grey = true, Make = null };
            return null;
        }

        Action ResolveEntity(SimTarget t, HandsItem hands, float dist)
        {
            if (dist > _sim.Tuning.Data.range_default_m + 0.3f) return null;
            var e = _sim.Placement.Get(t.Id);
            if (e == null) return null;
            int id = e.Id;
            switch (e.Kind)
            {
                case EntityKind.Crate:
                {
                    var stage = _sim.Economy.CrateStageOf(id);
                    if (stage == CrateStage.Landed)
                    {
                        if (hands.IsCarried) return null;
                        string name = _sim.Tuning.TryItem(e.Item, out var row) ? row.displayName : e.Item;
                        return new Action { Prompt = $"Hold to pry open: {name} (E)", Hold = 1f, Make = () => new OpenCrateRequest { Crate = id } };
                    }
                    if (hands.IsCarried) return null;
                    return new Action { Prompt = "Pick up empty crate (E)", Make = () => new PickUpRequest { Kind = PickUpKind.Entity, Id = id } };
                }
                case EntityKind.Tank:
                {
                    var tank = _sim.Network.Tank(id);
                    if (tank == null) return null;
                    if (hands.Kind == HandsKind.Tool && tank.Installed)
                    {
                        string tool = hands.Tool;
                        bool empty = _sim.HandTools.Held(0, tool) <= HandToolsSystem.EmptyEpsilon;
                        bool full = _sim.Network.IsFull(_sim.Network.PoolOf(id));
                        return new Action
                        {
                            Prompt = empty ? "Nothing to pour" : full ? "Tank full: LAUNCH it" : "Pour (E)",
                            Grey = empty || full,
                            Make = () => new PourRequest { Tool = tool, Container = id },
                            ToolUse = true, Tool = tool,
                        };
                    }
                    if (hands.Kind == HandsKind.Entity)
                    {
                        var held = _sim.Placement.Get(hands.Id);
                        if (held != null && held.Kind == EntityKind.InletFitting && !tank.InletFitted)
                            return new Action { Prompt = "Hold to fit inlet (E)", Hold = 2f, Make = () => new FitInletRequest { Tank = id } };
                        return null;
                    }
                    if (hands.IsCarried) return null;
                    if (!tank.Installed && _sim.Placement.OnStagingSpot(e))
                        return new Action { Prompt = "Swap in (E)", Make = () => new SwapStorageRequest { Tank = id } };
                    var pad = _sim.Placement.PadTank();
                    if (pad != null && pad.Id == id) return null; // no prompt on the pad tank
                    return new Action { Prompt = "Pick up (E)", Make = () => new PickUpRequest { Kind = PickUpKind.Entity, Id = id } };
                }
                case EntityKind.Pump:
                {
                    if (hands.Kind == HandsKind.Entity)
                    {
                        var held = _sim.Placement.Get(hands.Id);
                        if (held != null && held.Kind == EntityKind.MotorKit)
                            return new Action { Prompt = "Hold to bolt on (E)", Hold = 2f, Make = () => new BoltMotorRequest { Pump = id } };
                        return null;
                    }
                    if (hands.IsCarried) return null;
                    var p = _sim.Pumps.Get(id);
                    string missing = _sim.Pumps.MissingPart(id);
                    string state = p == null ? "" : $" [{_sim.Pumps.StateOf(id)}]";
                    string hint = missing == "no_intake" ? " — No intake: connect the suction hose to the water"
                        : missing == "no_outlet" ? " — No outlet: run a hose to the tank"
                        : missing == "no_power" ? " — No power: plug in the cable" : "";
                    return new Action { Prompt = "Pick up pump (E)" + state + hint, Make = () => new PickUpRequest { Kind = PickUpKind.Entity, Id = id } };
                }
                default:
                {
                    if (hands.IsCarried) return null;
                    string name = _sim.Tuning.TryItem(e.Item, out var row) ? row.displayName : e.Item;
                    return new Action { Prompt = $"Pick up {name} (E)", Make = () => new PickUpRequest { Kind = PickUpKind.Entity, Id = id } };
                }
            }
        }

        void Use(Action a, SimTarget target)
        {
            if (a.Make == null) return;
            if (a.CrankPump > 0)
            {
                _crankPump = a.CrankPump;
                Send(a.Make());
                return;
            }
            if (a.Hold > 0)
            {
                _hold = a;
                _holdStart = Time.unscaledTime;
                _holdTarget = target;
                return;
            }
            if (a.ToolUse)
            {
                if (!GateOpen(a, out float readyAt))
                {
                    if (readyAt - Time.unscaledTime <= BufferWindow) { _buffered = a; _bufferedAt = Time.unscaledTime; }
                    return;
                }
                SendToolUse(a);
                return;
            }
            Send(a.Make());
        }

        bool GateOpen(Action a, out float readyAt)
        {
            float now = Time.unscaledTime;
            readyAt = Mathf.Max(_pourLockUntil, _switchLockUntil);
            if (a.IsScoop && _scoopReadyAt.TryGetValue(a.Tool, out var s)) readyAt = Mathf.Max(readyAt, s);
            return now >= readyAt;
        }

        void SendToolUse(Action a)
        {
            float now = Time.unscaledTime;
            if (_sim.Tuning.TryItem(a.Tool, out var row))
            {
                if (a.IsScoop) _scoopReadyAt[a.Tool] = now + (float)row.scoopCooldown_s;
                else _pourLockUntil = now + (float)row.pourTime_s;
            }
            Send(a.Make());
        }

        void FlushBuffer()
        {
            if (_buffered == null) return;
            if (Time.unscaledTime - _bufferedAt > 2f) { _buffered = null; return; }
            if (GateOpen(_buffered, out _)) { SendToolUse(_buffered); _buffered = null; }
        }

        void SelectTool(string tool)
        {
            var h = _sim.Placement.Hands(0);
            if (h.Kind == HandsKind.Tool && h.Tool == tool) return;
            CancelHolds();
            _switchLockUntil = Time.unscaledTime + SwitchLock;
            Send(new SelectToolRequest { Tool = tool });
        }

        void DropHeld()
        {
            var h = _sim.Placement.Hands(0);
            if (!h.IsCarried) return;
            var ahead = transform.position + transform.forward * 0.8f;
            ahead.y = GroundY(ahead);
            Send(new DropRequest { Pose = ahead.ToPose(transform.eulerAngles.y) });
        }

        float GroundY(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return at.y;
        }

        // ------------------------------------------------------------ placement ghost

        string GhostReason = "";

        void UpdateGhost(SimTarget t, Vector3 point, Collider col)
        {
            var hands = _sim.Placement.Hands(0);
            Entity held = hands.Kind == HandsKind.Entity ? _sim.Placement.Get(hands.Id) : null;
            bool show = held != null && (t == null || t.Kind == TargetKind.StagingSpot) && col != null &&
                        Vector3.Distance(_cam.position, point) <= _sim.Tuning.Data.range_default_m + 0.3f;
            if (!show)
            {
                if (_ghost != null) _ghost.SetActive(false);
                return;
            }
            if (_ghost == null)
            {
                _ghost = Prims.Box("PlacementGhost", Vector3.zero, new Vector3(0.6f, 0.04f, 0.6f), Palette.GhostOk, null, false);
            }
            _ghost.SetActive(true);
            var at = point;
            // Snap to the staging spot so "Swap in" is easy to reach.
            var st = _sim.Level.StagingSpot.ToV3();
            if (new Vector2(at.x - st.x, at.z - st.z).magnitude <= _sim.Level.StagingRadius) at = new Vector3(st.x, at.y, st.z);
            at.y = GroundY(at);
            float yaw = transform.eulerAngles.y + _ghostYaw;
            _ghostPose = at.ToPose(yaw);
            string why = PlacementValidator.Check(_sim.State, _sim.Level, _sim.Tuning, held.Item, _ghostPose, held.Id);
            _ghostValid = why == null;
            GhostReason = why ?? "";
            float r = _sim.Tuning.TryItem(held.Item, out var row) ? (float)row.footprint_m * 2f : 0.6f;
            _ghost.transform.position = at + Vector3.up * 0.03f;
            _ghost.transform.rotation = Quaternion.Euler(0, yaw, 0);
            _ghost.transform.localScale = new Vector3(r, 0.04f, r);
            Prims.SetColor(_ghost, _ghostValid ? Palette.GhostOk : Palette.GhostBad);
        }

        void OnDestroy()
        {
            if (_ghost != null) Destroy(_ghost);
        }
    }
}
