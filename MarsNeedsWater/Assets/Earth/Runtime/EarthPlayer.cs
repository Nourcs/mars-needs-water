using UnityEngine;
using UnityEngine.InputSystem;

namespace MarsNeedsWater.Earth
{
    /// <summary>The player: on foot, in a car, or flying. One body, three ways of moving.</summary>
    public sealed class EarthPlayer : MonoBehaviour
    {
        public enum Mode { Walk, Drive, Fly }

        public Mode Current { get; private set; }
        public float Speed { get; private set; }
        public bool Waiting { get { return placing; } }
        public static bool InputBlocked;

        /// <summary>For tests: drive the jeep from code instead of the keyboard.</summary>
        [System.NonSerialized] public bool Autopilot, AutoBoost;
        [System.NonSerialized] public float AutoThrottle, AutoSteer;
        public Jeep Jeep { get { return jeep; } }
        public void EnterJeep() { if (Current != Mode.Drive) SetMode(Mode.Drive); }

        /// <summary>In water deep enough to swim.</summary>
        public bool Swimming { get { return swimming; } }
        /// <summary>The car is afloat.</summary>
        public bool Afloat { get { return boat; } }

        const float Eye = 0.75f, HalfHeight = 0.85f;

        EarthWorld world;
        CharacterController cc;
        Camera cam;
        GameObject body, car, plane;
        Jeep jeep;
        float camY, camLift, camDist = 7.5f, heading;       // the chase camera: height, how far it has risen, how far back; the jeep's heading
        bool jeepHidden;
        readonly RaycastHit[] camHits = new RaycastHit[16];
        bool camSnap = true;
        float yaw, pitch = 10f, fall, flySpeed = 60f;
        bool third, placing = true, locked;
        Vector3 flyVel;

        // water: read each frame where the player is
        bool wet, lake, swimming, boat, wasIn;
        float level, depth, surface, swimVy, rippleIn, splashIn;

        public void Init(EarthWorld w)
        {
            world = w;
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = HalfHeight * 2f; cc.radius = 0.3f; cc.stepOffset = 0.35f; cc.slopeLimit = 60f; cc.skinWidth = 0.03f;
            cc.enabled = false;
            transform.position = new Vector3(0f, 300f, 0f);
            yaw = 225f;

            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 400000f;
            cam.fieldOfView = 65f;

            body = Shape(PrimitiveType.Capsule, transform, Vector3.zero, new Vector3(0.6f, HalfHeight, 0.6f), new Color(0.90f, 0.45f, 0.20f));
            jeep = Jeep.Build();
            jeep.GroundFn = JeepGround;
            jeep.WaterFn = JeepWater;
            car = jeep.gameObject;
            plane = new GameObject("Plane");
            plane.transform.SetParent(transform, false);
            Shape(PrimitiveType.Cube, plane.transform, Vector3.zero, new Vector3(0.7f, 0.7f, 4.2f), new Color(0.95f, 0.95f, 0.97f));
            Shape(PrimitiveType.Cube, plane.transform, new Vector3(0f, 0f, 0.2f), new Vector3(6.4f, 0.12f, 1.1f), new Color(0.9f, 0.3f, 0.2f));
            Shape(PrimitiveType.Cube, plane.transform, new Vector3(0f, 0.1f, -1.8f), new Vector3(2.2f, 0.1f, 0.6f), new Color(0.9f, 0.3f, 0.2f));
            Shape(PrimitiveType.Cube, plane.transform, new Vector3(0f, 0.55f, -1.8f), new Vector3(0.1f, 0.9f, 0.7f), new Color(0.9f, 0.3f, 0.2f));
            SetMode(Mode.Walk);
        }

        static GameObject Shape(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color colour)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Renderer r = go.GetComponent<Renderer>();
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(s != null ? s : Shader.Find("Standard"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            m.color = colour;
            r.sharedMaterial = m;
            return go;
        }

        void SetMode(Mode m)
        {
            if (Current == Mode.Drive && m != Mode.Drive && car.activeSelf)
            {
                // getting out: stand where the jeep was, facing its way
                cc.enabled = false;
                transform.position = jeep.transform.position + Vector3.up * 0.5f;
            }
            Current = m;
            car.SetActive(m == Mode.Drive);
            plane.SetActive(m == Mode.Fly);
            fall = 0; flyVel = Vector3.zero;
            StopJeep();
            if (m == Mode.Fly) { cc.enabled = false; placing = false; transform.position += Vector3.up * 3f; }
            else placing = true;
        }

        /// <summary>The world moved its origin under us.</summary>
        public void Moved(Vector3 pos, Vector3 forward)
        {
            bool was = cc.enabled;
            cc.enabled = false;
            Vector3 old = transform.position;
            float oldYaw = yaw;
            transform.position = pos;
            if (forward.sqrMagnitude > 1e-6f) yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            if (Current == Mode.Drive) jeep.Shift(old, pos, Quaternion.Euler(0f, yaw - oldYaw, 0f));
            cc.enabled = was;
            camSnap = true;
        }

        /// <summary>Jumped to a new place: hang in the air until its ground has loaded.</summary>
        public void Arrive()
        {
            cc.enabled = false;
            transform.position = new Vector3(0f, Current == Mode.Fly ? 150f : 300f, 0f);
            placing = Current != Mode.Fly;
            fall = 0; flyVel = Vector3.zero;
            StopJeep();
        }

        void StopJeep()
        {
            camLift = 0f; camDist = 7.5f; camSnap = true; heading = yaw;
            if (jeep != null && Current == Mode.Drive) jeep.Hold(transform.position, yaw);
        }

        public void Look(float yawDegrees) { yaw = yawDegrees; pitch = 12f; }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            if (InputBlocked) { SetCursor(false); }
            else
            {
                if (kb.escapeKey.wasPressedThisFrame) SetCursor(false);
                else if (mouse.leftButton.wasPressedThisFrame && !locked) SetCursor(true);
                if (locked)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * 0.12f;          // in the jeep too: the mouse alone points the camera, whatever the jeep does
                    pitch = Mathf.Clamp(pitch - d.y * 0.12f, -85f, 85f);
                }
                if (kb.cKey.wasPressedThisFrame) SetMode(Current == Mode.Drive ? Mode.Walk : Mode.Drive);
                if (kb.fKey.wasPressedThisFrame) SetMode(Current == Mode.Fly ? Mode.Walk : Mode.Fly);
                if (kb.vKey.wasPressedThisFrame) third = !third;
            }

            float ground;
            // on foot and in the car the ground is the bed under any water; flying, the water's surface counts as ground
            bool haveGround = world.Ground(transform.position, out ground, Current != Mode.Fly, Current != Mode.Fly);
            ReadWater(haveGround, ground);
            Vector3 before = transform.position;

            if (placing)
            {
                Speed = 0;
                if (haveGround)
                {
                    float top = wet ? Mathf.Max(ground, surface) : ground;
                    transform.position = new Vector3(transform.position.x, top + HalfHeight + 0.3f, transform.position.z);
                    Physics.SyncTransforms();
                    cc.enabled = true;
                    placing = false;
                    if (Current == Mode.Drive) { cc.enabled = false; jeep.Hold(transform.position, yaw); jeep.Release(); }
                }
                else if (Current == Mode.Drive) jeep.Hold(transform.position, yaw);
            }
            else if (Current == Mode.Drive) Drive(kb, dt);
            else if (Current == Mode.Fly) Fly(kb, mouse, dt, haveGround, ground);
            else if (!haveGround) { Speed = 0; }                       // the ground here has not arrived yet: wait
            else
            {
                if (transform.position.y < ground - 2f)                 // never fall through the world
                {
                    cc.enabled = false;
                    transform.position = new Vector3(transform.position.x, ground + HalfHeight + 0.2f, transform.position.z);
                    cc.enabled = true; fall = 0;
                }
                Walk(kb, dt);
            }
            if (Current != Mode.Walk) swimming = false;
            if (Current != Mode.Drive) boat = false;
            if (!placing) WaterEffects(dt, before);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Current == Mode.Fly) plane.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            PlaceCamera();
        }

        void SetCursor(bool lockIt)
        {
            locked = lockIt;
            Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockIt;
        }

        static float Axis(bool neg, bool pos) { return (pos ? 1f : 0f) - (neg ? 1f : 0f); }

        void Walk(Keyboard kb, float dt)
        {
            float f = InputBlocked ? 0 : Axis(kb.sKey.isPressed, kb.wKey.isPressed), s = InputBlocked ? 0 : Axis(kb.aKey.isPressed, kb.dKey.isPressed);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 dir = rot * new Vector3(s, 0f, f);
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            float sub = wet ? surface - (transform.position.y - HalfHeight) : 0f;      // how deep the player stands
            swimming = wet && sub > (swimming ? 1.0f : 1.2f);
            if (swimming) { Swim(kb, dt, f, s); return; }
            swimVy = 0;
            float speed = kb.leftShiftKey.isPressed ? 9f : 3.5f;
            if (sub > 0.15f) speed *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(sub / 1.2f));     // wading
            if (cc.isGrounded)
            {
                fall = -2f;
                if (!InputBlocked && kb.spaceKey.wasPressedThisFrame) fall = 6f;
            }
            else fall -= 18f * dt;
            Vector3 before = transform.position;
            cc.Move((dir * speed + Vector3.up * fall) * dt);
            Vector3 moved = transform.position - before; moved.y = 0;
            Speed = moved.magnitude / dt;
        }

        /// <summary>In the jeep: it is a physics body of its own (Jeep.cs); the player rides along and steers it.</summary>
        void Drive(Keyboard kb, float dt)
        {
            cc.enabled = false;
            bool free = !InputBlocked;
            jeep.Throttle = Autopilot ? AutoThrottle : free ? Axis(kb.sKey.isPressed, kb.wKey.isPressed) : 0f;
            jeep.Steer = Autopilot ? AutoSteer : free ? Axis(kb.aKey.isPressed, kb.dKey.isPressed) : 0f;
            jeep.Handbraking = !Autopilot && free && kb.spaceKey.isPressed;
            jeep.Boosting = Autopilot ? AutoBoost : free && kb.leftShiftKey.isPressed;
            if (free && kb.rKey.wasPressedThisFrame && jeep.Upturned) jeep.Flip();
            transform.position = jeep.transform.position;
            heading = jeep.Heading(heading);
            boat = jeep.Afloat;
            Speed = jeep.Speed;
        }

        bool JeepGround(Vector3 p, out float y) { return world.Ground(p, out y, true, true); }

        bool JeepWater(Vector3 p, out float s)
        {
            float l, bed; bool lk;
            s = 0f;
            if (!world.WaterAt(p, out l, out bed, out lk) || l <= bed + 0.02f) return false;
            s = l + (EarthWater.I != null ? EarthWater.I.Swell(p, lk, l - bed) : 0f);
            return true;
        }

        // ------------------------------------------------------------ water

        void ReadWater(bool haveGround, float ground)
        {
            Vector3 p = transform.position;
            float bed = 0f;
            wet = false;
            if (haveGround && world.WaterAt(p, out level, out bed, out lake) && level > bed + 0.02f)
                wet = Current == Mode.Fly ? level >= ground - 0.05f : level > ground + 0.02f;
            depth = wet ? level - bed : 0f;
            surface = wet ? level + (EarthWater.I != null ? EarthWater.I.Swell(p, lake, depth) : 0f) : float.MinValue;
        }

        /// <summary>Swimming: float with the head out, Space to swim up (or hop out at the edge), Ctrl to dive.</summary>
        void Swim(Keyboard kb, float dt, float f, float s)
        {
            float floatY = surface - 0.55f;                     // body centre when floating: eyes just above the water
            float y = transform.position.y;
            bool under = y < floatY - 0.3f;
            // under water you swim where you look; at the surface, along it
            Quaternion rot = under ? Quaternion.Euler(pitch, yaw, 0f) : Quaternion.Euler(0f, yaw, 0f);
            Vector3 dir = rot * new Vector3(s, 0f, f);
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            float speed = !InputBlocked && kb.leftShiftKey.isPressed ? 3.8f : 2.0f;

            float want = Mathf.Clamp((floatY - y) * 2.5f, -3f, 1.2f);   // buoyancy brings you back up to the surface
            if (!InputBlocked)
            {
                if (kb.spaceKey.wasPressedThisFrame && y > floatY - 0.2f) swimVy = 4.5f;     // a kick up and out
                else if (kb.spaceKey.isPressed) want = 2.2f;
                if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) want = -2.2f;
            }
            swimVy = Mathf.Lerp(swimVy, want, 1f - Mathf.Exp(-3f * dt));
            fall = 0f;
            Vector3 before = transform.position;
            cc.Move((dir * speed + Vector3.up * swimVy) * dt);
            Vector3 moved = transform.position - before; moved.y = 0;
            Speed = moved.magnitude / dt;
        }

        /// <summary>Rings, splashes and spray from whatever the player is doing in the water.</summary>
        void WaterEffects(float dt, Vector3 before)
        {
            EarthWater w = EarthWater.I;
            if (w == null) return;
            Vector3 p = transform.position;
            float vy = (p.y - before.y) / Mathf.Max(dt, 1e-4f);
            Vector3 hv = p - before; hv.y = 0f;
            float hs = hv.magnitude / Mathf.Max(dt, 1e-4f);
            float bottom = p.y - (Current == Mode.Drive ? 0.65f : Current == Mode.Fly ? 0.5f : HalfHeight);
            bool inNow = wet && bottom < surface;
            Vector3 at = new Vector3(p.x, surface, p.z);
            Quaternion rot = Quaternion.Euler(0f, Current == Mode.Drive ? heading : yaw, 0f);
            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right;

            if (inNow && !wasIn && vy < -2.5f) w.Splash(at, -vy * (Current == Mode.Walk ? 0.6f : 0.9f));
            wasIn = inNow;
            rippleIn -= dt; splashIn -= dt;

            if (!inNow)
            {
                // a plane skimming low over the water throws up a trail of spray
                if (Current == Mode.Fly && wet && bottom - surface < 5f && hs > 15f && splashIn <= 0f)
                {
                    float near = 1f - (bottom - surface) / 5f;
                    Vector3 under = new Vector3(p.x, surface, p.z) - fwd * 2f;
                    w.Spray(under, Vector3.up * (2f + hs * 0.06f) * near - fwd * hs * 0.15f, (int)(4 + 10 * near), 0.5f, 0.25f);
                    w.Ripple(under, 0.6f * near + 0.2f);
                    splashIn = 0.06f;
                }
                return;
            }
            if (rippleIn <= 0f)
            {
                bool moving = hs > 0.5f;
                Vector3 ringAt = Current == Mode.Drive ? at - fwd * 1.4f : at;
                w.Ripple(ringAt, Mathf.Clamp(0.15f + hs * 0.1f, 0.15f, 1.3f));
                rippleIn = moving ? Mathf.Lerp(0.3f, 0.07f, hs / 15f) : (swimming || boat ? 1.1f : 2f);
            }
            if (hs > 0.8f && splashIn <= 0f)
            {
                if (boat)
                {
                    // spray off both sides of the bow, more the faster it goes
                    Vector3 bow = at + fwd * 1.65f;
                    int n = Mathf.Clamp((int)(hs * 0.5f), 1, 14);
                    w.Spray(bow + right * 0.85f, (right * 0.7f + Vector3.up * 0.9f) * hs * 0.22f, n, 0.3f, 0.16f);
                    w.Spray(bow - right * 0.85f, (-right * 0.7f + Vector3.up * 0.9f) * hs * 0.22f, n, 0.3f, 0.16f);
                    if (hs > 10f) w.Spray(at - fwd * 1.9f, Vector3.up * 1.5f - fwd * 1.5f, n / 2, 0.6f, 0.2f);   // churned water behind
                    splashIn = 0.05f;
                }
                else if (swimming)
                {
                    w.Splash(at + fwd * 0.5f + right * Random.Range(-0.3f, 0.3f), 1.1f);   // arms over the water
                    splashIn = !InputBlocked && Keyboard.current.leftShiftKey.isPressed ? 0.35f : 0.6f;
                }
                else
                {
                    w.Spray(at + fwd * 0.2f, Vector3.up * 1.6f + hv.normalized * hs * 0.25f, Mathf.Clamp((int)(hs * 1.2f), 2, 10), 0.5f, 0.1f);   // wading
                    splashIn = 0.28f;
                    if (WaterSounds.I != null) WaterSounds.I.Splash(0.4f + hs * 0.05f);
                }
            }
        }

        /// <summary>
        /// Where the player's hands can reach water, if anywhere: in the water they stand in, or at an edge just ahead.
        /// </summary>
        public bool ReachWater(out Vector3 spot, out bool lakeWater)
        {
            spot = Vector3.zero; lakeWater = false;
            if (placing || Current != Mode.Walk) return false;
            Vector3 p = transform.position;
            float feet = p.y - HalfHeight;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            if (wet && surface > feet - 0.05f) { spot = new Vector3(p.x, surface, p.z) + fwd * 0.5f; lakeWater = lake; return true; }
            for (float reach = 0.6f; reach <= 1.6f; reach += 0.5f)
            {
                Vector3 a = p + fwd * reach;
                float l, bed; bool lk;
                if (world.WaterAt(a, out l, out bed, out lk) && l > bed + 0.05f && l > feet - 1.1f && l < p.y + 0.4f)
                {
                    spot = new Vector3(a.x, l, a.z); lakeWater = lk; return true;
                }
            }
            return false;
        }

        void Fly(Keyboard kb, Mouse mouse, float dt, bool haveGround, float ground)
        {
            if (!InputBlocked)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll > 0.01f) flySpeed *= 1.25f; else if (scroll < -0.01f) flySpeed /= 1.25f;
                if (kb.eKey.wasPressedThisFrame) flySpeed *= 2f;
                if (kb.qKey.wasPressedThisFrame) flySpeed /= 2f;
                flySpeed = Mathf.Clamp(flySpeed, 5f, 50000f);
            }
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 want = Vector3.zero;
            if (!InputBlocked)
            {
                want = rot * new Vector3(Axis(kb.aKey.isPressed, kb.dKey.isPressed), 0f, Axis(kb.sKey.isPressed, kb.wKey.isPressed));
                want += Vector3.up * Axis(kb.leftCtrlKey.isPressed, kb.spaceKey.isPressed);
                want *= flySpeed * (kb.leftShiftKey.isPressed ? 4f : 1f);
            }
            flyVel = Vector3.Lerp(flyVel, want, 1f - Mathf.Exp(-4f * dt));
            Vector3 p = transform.position + flyVel * dt;
            if (haveGround && p.y < ground + 1.5f) p.y = ground + 1.5f;
            transform.position = p;
            Speed = flyVel.magnitude;
        }

        void PlaceCamera()
        {
            bool chase = third || Current != Mode.Walk;
            body.SetActive(chase && Current == Mode.Walk);
            body.transform.localRotation = Quaternion.Euler(swimming && Speed > 0.5f ? 70f : 0f, 0f, 0f);    // lying flat to swim
            float viewYaw = yaw;
            Quaternion look = Quaternion.Euler(pitch, viewYaw, 0f);
            Vector3 head = transform.position + Vector3.up * Eye;
            // in the jeep the camera rides smoothly over bumps and hops rather than every jolt of the chassis
            if (Current == Mode.Drive && !camSnap && Mathf.Abs(head.y - camY) < 5f)
                head.y = camY = Mathf.Lerp(camY, head.y, 1f - Mathf.Exp(-7f * Time.deltaTime));
            else camY = head.y;
            camSnap = false;
            if (!chase) { cam.transform.SetPositionAndRotation(head, look); return; }
            float dist = Current == Mode.Fly ? 13f : Current == Mode.Drive ? 7.5f : 4f;
            Vector3 target = head + Vector3.up * (Current == Mode.Walk ? 0.2f : 0.6f);
            if (Current != Mode.Drive)
            {
                Vector3 back = look * Vector3.back;
                float d = CameraCast(target, back, dist);
                cam.transform.SetPositionAndRotation(target + back * Mathf.Max(0.6f, d), look);
                if (jeepHidden) ShowJeep(true);
                return;
            }

            // in the jeep: behind and above, and never inside it or under the ground. Where the hill or a wall
            // behind would pull the camera in close, it climbs higher and looks down instead.
            float dt = Time.deltaTime;
            float want = 0f, room = dist;
            for (float lift = 0f; lift <= 70f; lift += 10f)
            {
                Quaternion q = Quaternion.Euler(Mathf.Max(pitch, -10f) + lift, viewYaw, 0f);
                Vector3 back = q * Vector3.back;
                float d = CameraCast(target, back, dist);
                float gy;
                Vector3 at = target + back * d;
                bool clear = d > dist * 0.6f && !(world.Ground(at, out gy, false, false) && at.y < gy + 1.0f);
                if (clear || lift >= 70f) { want = lift; room = d; break; }
            }
            camLift = Mathf.Lerp(camLift, want, 1f - Mathf.Exp(-(want > camLift ? 12f : 2.5f) * dt));
            camDist = Mathf.Lerp(camDist, room, 1f - Mathf.Exp(-(room < camDist ? 20f : 3f) * dt));
            Quaternion orbit = Quaternion.Euler(Mathf.Max(pitch, -10f) + camLift, viewYaw, 0f);
            Vector3 dir = orbit * Vector3.back;
            float cd = Mathf.Min(camDist, CameraCast(target, dir, camDist));
            Vector3 pos = target + dir * cd;
            float ground;
            if (world.Ground(pos, out ground, false, false) && pos.y < ground + 0.8f) pos.y = ground + 0.8f;
            Quaternion view = camLift > 0.5f ? Quaternion.LookRotation(target - pos) : look;
            cam.transform.SetPositionAndRotation(pos, Quaternion.Slerp(look, view, Mathf.Clamp01(camLift / 10f)));
            // last resort: if it still ends up in the jeep, hide the jeep rather than show its insides
            bool tooClose = (pos - (transform.position + Vector3.up * 0.2f)).magnitude < 1.6f;
            if (tooClose != jeepHidden) ShowJeep(!tooClose);
        }

        /// <summary>How far the camera can go from target along dir before it hits something other than the player.</summary>
        float CameraCast(Vector3 target, Vector3 dir, float dist)
        {
            int n = Physics.SphereCastNonAlloc(target, 0.3f, dir, camHits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = dist;
            for (int i = 0; i < n; i++)
                if (camHits[i].collider != cc && camHits[i].distance > 0f && camHits[i].distance < best) best = camHits[i].distance;
            return best;
        }

        void ShowJeep(bool show)
        {
            jeepHidden = !show;
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>()) r.enabled = show;
        }
    }
}
