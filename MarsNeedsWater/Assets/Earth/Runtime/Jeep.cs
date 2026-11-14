using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The player's car: an open-topped off-road 4x4, built in code. Boxy tub and short bonnet, flared arches,
    /// a tubular roll bar, a fold-flat windscreen, chunky treaded tyres on a raised chassis, a spare on the back
    /// and a snorkel up the right pillar. Every face takes its colour from a small palette texture with slight
    /// per-patch variations of each colour, so big panels look worn rather than flat, with nothing repeating.
    /// Local space: +z forward, y up, the origin at the player's centre, the ground 0.85 below it.
    /// </summary>
    public sealed class Jeep : MonoBehaviour
    {
        public const float Ground = -0.85f;      // the bottom of the tyres, where the character controller stands
        public const float TyreR = 0.43f, TyreW = 0.34f;
        public const float Track = 0.80f, FrontZ = 1.08f, RearZ = -1.02f;
        const float AxleY = Ground + TyreR;

        // palette rows
        const int Paint = 0, Flare = 1, Rubber = 2, Tread = 3, Steel = 4, DarkSteel = 5, Rim = 6, Seat = 7,
                  Glass = 8, Lamp = 9, Tail = 10, Amber = 11, Floor = 12, Tube = 13, Grille = 14, PaintLight = 15;
        const int Variants = 16;

        static readonly Color[] Palette =
        {
            new Color(0.42f, 0.41f, 0.27f),   // olive-khaki paint
            new Color(0.09f, 0.09f, 0.085f),  // black arch flares
            new Color(0.055f, 0.055f, 0.055f),// tyre rubber
            new Color(0.085f, 0.082f, 0.078f),// tread blocks
            new Color(0.50f, 0.51f, 0.52f),   // bright steel
            new Color(0.17f, 0.17f, 0.18f),   // dark steel: chassis, bumpers
            new Color(0.33f, 0.33f, 0.30f),   // wheel rims
            new Color(0.17f, 0.14f, 0.11f),   // canvas seats
            new Color(0.52f, 0.62f, 0.67f),   // windscreen glass
            new Color(0.96f, 0.95f, 0.86f),   // headlight lens
            new Color(0.62f, 0.07f, 0.05f),   // tail lights
            new Color(0.90f, 0.52f, 0.10f),   // indicators
            new Color(0.21f, 0.21f, 0.17f),   // floor mats
            new Color(0.11f, 0.11f, 0.11f),   // roll bar, snorkel
            new Color(0.045f, 0.045f, 0.045f),// grille backing
            new Color(0.47f, 0.46f, 0.31f),   // paint, sun-faded
        };

        // ---- tuning: change these on the Jeep object in the Hierarchy while playing to try them out

        [Header("Body")]
        [Tooltip("Mass, kg.")] public float Mass = 1650f;
        [Tooltip("Height of the centre of mass above the ground, m. Higher = tips over more easily.")] public float CentreHeight = 0.55f;
        [Tooltip("Air drag. Higher = more slowing at speed.")] public float AirDrag = 0.6f;

        [Header("Engine and brakes")]
        [Tooltip("Engine pull at low speed, as m/s^2 of acceleration on the flat (it fades to nothing at top speed).")] public float Accel = 6.5f;
        [Tooltip("Top speed, m/s.")] public float TopSpeed = 30f;
        [Tooltip("Top speed holding Shift, m/s.")] public float BoostSpeed = 70f;
        [Tooltip("Engine pull holding Shift, times the normal pull.")] public float BoostPower = 1.8f;
        [Tooltip("Top speed backwards, m/s.")] public float ReverseSpeed = 10f;
        [Tooltip("Brakes (S while moving forward), m/s^2 if the tyres can take it.")] public float Brake = 9f;
        [Tooltip("Handbrake (Space): locks the rear wheels, m/s^2.")] public float Handbrake = 7f;
        [Tooltip("Engine braking and holding with no pedal pressed, m/s^2.")] public float EngineBrake = 1.2f;
        [Tooltip("Rolling resistance per wheel, newtons per m/s.")] public float RollingResistance = 12f;

        [Header("Tyres and steering")]
        [Tooltip("Tyre friction: 1 is good rubber on dry ground. Lower = slidier everywhere.")] public float Grip = 1.0f;
        [Tooltip("Share of the grip left once a tyre is sliding.")] [Range(0.3f, 1f)] public float SlideGrip = 0.8f;
        [Tooltip("How sharply sideways grip builds with slip angle. Higher = more direct.")] public float CorneringStiffness = 8f;
        [Tooltip("Rear sideways grip with the handbrake on, as a share. Lower = easier handbrake turns.")] [Range(0f, 1f)] public float HandbrakeRearGrip = 0.4f;
        [Tooltip("Front wheel angle at full lock, degrees.")] public float WheelLock = 32f;
        [Tooltip("Share of full lock left at top speed.")] [Range(0.1f, 1f)] public float HighSpeedLock = 0.35f;
        [Tooltip("How quickly the steering follows the keys.")] public float SteerSpeed = 5f;

        [Header("Suspension")]
        [Tooltip("Spring rate per wheel, N/m. Lower = softer and more body roll.")] public float SpringRate = 32000f;
        [Tooltip("Damping while a wheel is pushed up, as a share of critical (1 = no overshoot).")] [Range(0.1f, 2f)] public float BumpDamping = 0.9f;
        [Tooltip("Damping while a wheel extends again, as a share of critical. Higher than bump so it does not spring back up.")] [Range(0.1f, 2f)] public float ReboundDamping = 1.3f;
        [Tooltip("Anti-roll bars, N/m of difference between left and right. Higher = flatter in corners.")] public float AntiRoll = 8000f;
        [Tooltip("How far a wheel can push up into its arch, m.")] public float Bump = 0.1f;
        [Tooltip("How far a wheel can hang down, m.")] public float Droop = 0.2f;

        [Header("Air and water")]
        [Tooltip("Weak pitch and roll control in the air (W/S, A/D), rad/s^2.")] public float AirControl = 0.35f;
        [Tooltip("Buoyancy with the hull fully under water, times its weight.")] public float Buoyancy = 1.5f;
        [Tooltip("Water drag per hull point, N per m/s.")] public float WaterDrag = 120f;
        [Tooltip("Push from the wheels paddling, m/s^2.")] public float WaterThrust = 4f;
        [Tooltip("Turning afloat, rad/s^2.")] public float WaterTurn = 0.8f;

        /// <summary>Ground height (world y) under a point, if known: used where the ground has no collider yet.</summary>
        public delegate bool GroundAt(Vector3 world, out float y);
        /// <summary>The water surface (world y) over a point, if there is water.</summary>
        public delegate bool WaterAt(Vector3 world, out float surface);

        public GroundAt GroundFn;
        public WaterAt WaterFn;

        // input, set each frame by the player
        [System.NonSerialized] public float Throttle, Steer;
        [System.NonSerialized] public bool Handbraking, Boosting;

        public Rigidbody Body { get; private set; }
        /// <summary>Wheels on the ground (0-4).</summary>
        public int Grounded { get; private set; }
        public bool Afloat { get; private set; }
        /// <summary>On its side or roof and nearly stopped.</summary>
        public bool Upturned { get { return transform.up.y < 0.5f && Body.linearVelocity.magnitude < 3f; } }
        public float Speed { get { Vector3 v = Body.linearVelocity; v.y = 0f; return v.magnitude; } }

        const float AttachY = AxleY + 0.25f;            // where each spring meets the body; at rest the axle hangs 0.25 below
        Transform[] spin, steer, pivot;
        float steerNow;
        readonly float[] length = new float[4], shown = new float[4], angle = new float[4], spinRate = new float[4], spinAt = new float[4], squash = new float[4];
        readonly bool[] contact = new bool[4];
        [System.NonSerialized] public readonly float[] LastLoad = new float[4], LastPush = new float[4], LastSide = new float[4];   // for tests
        static readonly float[] WX = { -Track, Track, -Track, Track }, WZ = { FrontZ, FrontZ, RearZ, RearZ };
        static readonly Vector3[] Hull =
        {
            new Vector3(-0.6f, -0.2f, 1.45f), new Vector3(0.6f, -0.2f, 1.45f), new Vector3(-0.6f, -0.2f, -1.45f), new Vector3(0.6f, -0.2f, -1.45f),
            new Vector3(-0.6f, -0.2f, 0f), new Vector3(0.6f, -0.2f, 0f), new Vector3(0f, -0.2f, 0.75f), new Vector3(0f, -0.2f, -0.75f),
        };
        readonly RaycastHit[] hits = new RaycastHit[8];

        float RestLength { get { return 0.25f + Mass * 9.81f / 4f / Mathf.Max(SpringRate, 1f); } }

        /// <summary>Builds a jeep (its own physics body, not parented to anything) and returns it.</summary>
        public static Jeep Build()
        {
            var root = new GameObject("Jeep");
            Jeep j = root.AddComponent<Jeep>();
            Material mat = EarthWorld.Mat(PaletteTexture(), 0.18f);

            Mesh bodyMesh = BodyMesh(), wheel = WheelMesh();
            Transform body = Part(root.transform, "Body", bodyMesh, mat, Vector3.zero, Quaternion.identity).transform;
            Part(body, "Spare", wheel, mat, new Vector3(0f, 0.16f, -1.71f), Quaternion.Euler(0f, -90f, 0f));

            j.spin = new Transform[4];
            j.steer = new Transform[2];
            j.pivot = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2, right = i % 2 == 1;
                var p = new GameObject(front ? "Steer" : "Axle").transform;
                p.SetParent(root.transform, false);
                p.localPosition = new Vector3(WX[i], AxleY, WZ[i]);
                if (front) j.steer[i] = p;
                j.pivot[i] = p;
                // the right-hand wheels are turned round so the rims face outwards
                Transform w = Part(p, "Wheel", wheel, mat, Vector3.zero, Quaternion.identity).transform;
                w.localScale = new Vector3(right ? -1f : 1f, 1f, 1f);
                j.spin[i] = w;
                j.length[i] = j.shown[i] = 0.25f;
            }

            // the body's solid shape: the tub and bonnet, and the roll bar it lands on if it goes over.
            // The wheels have none: they find the ground with rays. On the Ignore Raycast layer so those rays,
            // and the camera's, pass through the jeep itself.
            var pm = new PhysicsMaterial("Jeep") { dynamicFriction = 0.5f, staticFriction = 0.6f, bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum };
            Box(root.transform, new Vector3(0f, 0.12f, 0.02f), new Vector3(1.3f, 0.6f, 3.3f), pm);
            Box(root.transform, new Vector3(0f, 0.78f, -0.5f), new Vector3(1.2f, 0.7f, 0.2f), pm);
            Box(root.transform, new Vector3(0f, 0.66f, 0.45f), new Vector3(1.3f, 0.5f, 0.1f), pm);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.3f;
            rb.maxAngularVelocity = 12f;
            j.Body = rb;
            j.Tune();
            return j;
        }

        static void Box(Transform parent, Vector3 centre, Vector3 size, PhysicsMaterial pm)
        {
            var go = new GameObject("Collider") { layer = 2 };
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<BoxCollider>();
            b.center = centre; b.size = size; b.sharedMaterial = pm;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Mass, balance and inertia from the settings (a box of the jeep's size).</summary>
        void Tune()
        {
            Rigidbody rb = Body;
            rb.mass = Mathf.Max(Mass, 100f);
            rb.centerOfMass = new Vector3(0f, Ground + CentreHeight, 0.05f);
            float m = rb.mass / 12f;
            rb.inertiaTensor = new Vector3(m * (1.3f * 1.3f + 3.4f * 3.4f), m * (1.9f * 1.9f + 3.4f * 3.4f), m * (1.9f * 1.9f + 1.3f * 1.3f));
            rb.inertiaTensorRotation = Quaternion.identity;
        }

        /// <summary>Holds the jeep still at a spot (while the ground there loads, or getting in).</summary>
        public void Hold(Vector3 pos, float yawDeg)
        {
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
            Quaternion r = Quaternion.Euler(0f, yawDeg, 0f);
            Body.position = pos; Body.rotation = r;
            transform.SetPositionAndRotation(pos, r);
            for (int i = 0; i < 4; i++) { length[i] = shown[i] = 0.25f; contact[i] = false; spinRate[i] = 0f; }
            steerNow = 0f; Grounded = 0; Afloat = false;
        }

        /// <summary>Lets it go: physics from here on.</summary>
        public void Release()
        {
            if (!Body.isKinematic) return;
            Body.isKinematic = false;
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
        }

        /// <summary>Back on its wheels, a little above where it lay, facing the way it pointed.</summary>
        public void Flip()
        {
            float heading = Heading(transform.eulerAngles.y);
            Vector3 pos = Body.position + Vector3.up * 1.2f;
            Quaternion r = Quaternion.Euler(0f, heading, 0f);
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
            Body.position = pos; Body.rotation = r;
            transform.SetPositionAndRotation(pos, r);
        }

        /// <summary>The way the jeep points, degrees clockwise from +z, even when it is on its nose or tail.</summary>
        public float Heading(float fallback)
        {
            Vector3 f = transform.forward;
            if (f.x * f.x + f.z * f.z < 0.04f) f = transform.up * (f.y > 0f ? -1f : 1f);   // pointing straight up or down: use its roof
            if (f.x * f.x + f.z * f.z < 1e-4f) return fallback;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        /// <summary>The world shifted under us (floating origin): move and turn the jeep with it, velocity and all.</summary>
        public void Shift(Vector3 oldPivot, Vector3 newPivot, Quaternion turn)
        {
            Vector3 pos = newPivot + turn * (Body.position - oldPivot);
            Quaternion r = turn * Body.rotation;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = turn * Body.linearVelocity;
                Body.angularVelocity = turn * Body.angularVelocity;
            }
            Body.position = pos; Body.rotation = r;
            transform.SetPositionAndRotation(pos, r);
        }

        void FixedUpdate() { Tick(Time.fixedDeltaTime); }

        /// <summary>One physics step of forces: springs, tyres, engine, water. (Called by FixedUpdate, or by a test.)</summary>
        public void Tick(float dt)
        {
            if (Body == null || Body.isKinematic) return;
            Tune();
            Rigidbody rb = Body;
            Vector3 up = transform.up, gv = Physics.gravity;
            float g = gv.magnitude, share = rb.mass / 4f;
            float fwdSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

            // steering, less of it at speed, with the inner wheel turned further than the outer (Ackermann)
            steerNow = Mathf.Lerp(steerNow, Mathf.Clamp(Steer, -1f, 1f), 1f - Mathf.Exp(-SteerSpeed * dt));
            float lockDeg = WheelLock * Mathf.Lerp(1f, HighSpeedLock, Mathf.Clamp01(Mathf.Abs(fwdSpeed) / Mathf.Max(TopSpeed, 1f)));
            float delta = steerNow * lockDeg * Mathf.Deg2Rad;
            angle[0] = angle[1] = angle[2] = angle[3] = 0f;
            if (Mathf.Abs(delta) > 1e-4f)
            {
                float wb = FrontZ - RearZ, r = wb / Mathf.Tan(Mathf.Abs(delta));
                float inner = Mathf.Atan(wb / Mathf.Max(r - Track, 0.05f)) * Mathf.Rad2Deg, outer = Mathf.Atan(wb / (r + Track)) * Mathf.Rad2Deg;
                if (delta > 0f) { angle[1] = inner; angle[0] = outer; } else { angle[0] = -inner; angle[1] = -outer; }
            }

            float top = Boosting ? BoostSpeed : TopSpeed;
            float pull = Accel * rb.mass * (Boosting ? BoostPower : 1f) / 4f;
            bool braking = (Throttle < 0f && fwdSpeed > 1f) || (Throttle > 0f && fwdSpeed < -1f);
            float rest = RestLength, minLen = 0.25f - Bump, maxLen = 0.25f + Droop;
            float critical = 2f * Mathf.Sqrt(Mathf.Max(SpringRate, 1f) * share);
            int on = 0;

            for (int i = 0; i < 4; i++)
            {
                Vector3 attach = transform.TransformPoint(new Vector3(WX[i], AttachY, WZ[i]));
                Vector3 point, normal; float dist;
                bool hit = CastWheel(attach, -up, maxLen + TyreR, out point, out normal, out dist);
                float len = hit ? dist - TyreR : maxLen;
                float rate = (length[i] - len) / dt;          // how fast it is being squashed
                length[i] = len;
                contact[i] = hit;
                if (!hit) { squash[i] = LastLoad[i] = LastPush[i] = LastSide[i] = 0f; spinRate[i] *= Mathf.Exp(-0.4f * dt); if (Throttle != 0f) spinRate[i] = Mathf.MoveTowards(spinRate[i], Throttle * top / TyreR, 30f * dt); continue; }
                on++;

                // spring and damper, then the bump stop
                float damp = critical * (rate > 0f ? BumpDamping : ReboundDamping);
                float fz = SpringRate * (rest - len) + damp * rate;
                if (len < minLen) fz += SpringRate * 10f * (minLen - len) + critical * 2f * Mathf.Max(rate, 0f);   // bump stop: hard, and soaks up the hit
                fz = Mathf.Clamp(fz, 0f, share * g * 8f);
                squash[i] = rest - len;
                rb.AddForceAtPosition(up * fz, attach);

                // the tyre: which way it rolls and slides on the ground it stands on
                Vector3 wf = Quaternion.AngleAxis(angle[i], up) * transform.forward;
                Vector3 along = Vector3.ProjectOnPlane(wf, normal).normalized, side = Vector3.Cross(normal, along);
                Vector3 pv = rb.GetPointVelocity(point);
                float vl = Vector3.Dot(pv, along), vs = Vector3.Dot(pv, side);
                bool rear = i >= 2, locked = Handbraking && rear;
                float mu = Mathf.Max(Grip, 0f), maxF = mu * fz;

                // sideways: grip builds with slip angle up to a peak, then fades a little (never more than it takes to stop the slide)
                float slip = Mathf.Atan2(vs, Mathf.Max(Mathf.Abs(vl), 0.5f));
                float fy = -Mathf.Sign(slip) * maxF * (locked ? HandbrakeRearGrip : 1f) * Curve(Mathf.Abs(slip));
                float stopY = share * Mathf.Abs(vs) / dt;
                fy = Mathf.Clamp(fy, -stopY, stopY);
                if (pv.sqrMagnitude < 1f) fy -= Mathf.Clamp(Vector3.Dot(gv, side) * share, -maxF, maxF);    // parked on a side slope: the tyres hold

                // along: engine (all four wheels), brakes, engine braking, rolling resistance
                float fx = 0f, stopX = share * Mathf.Abs(vl) / dt;
                if (Throttle > 0f && !braking) { float x = Mathf.Max(vl, 0f) / top; fx += Throttle * pull * Mathf.Clamp01(1f - x * x * x * x); }
                else if (Throttle < 0f && !braking) fx += Throttle * pull * 0.7f * Mathf.Clamp01(1f + (vl / ReverseSpeed) * Mathf.Abs(vl / ReverseSpeed));
                // brakes biased to the front so the rear does not lock and spin it; engine braking holds it near top speed downhill
                float hold = braking ? Brake * (rear ? 0.7f : 1.3f) : Throttle == 0f ? EngineBrake : 0f;
                if (vl > top) hold = Mathf.Max(hold, Mathf.Min((vl - top) * 0.6f, 4f));
                if (locked) hold = Mathf.Max(hold, Handbrake * 2f);
                if (hold > 0f)
                {
                    float fb = hold * share;
                    fx -= Mathf.Sign(vl) * Mathf.Min(fb, stopX);
                    if (Mathf.Abs(vl) < 0.5f) fx -= Mathf.Clamp(Vector3.Dot(gv, along) * share, -fb, fb);   // holding still on a hill
                }
                fx -= vl * RollingResistance;

                // a tyre can only push so hard in all directions together
                Vector2 f2 = new Vector2(fx, fy);
                if (f2.magnitude > maxF) f2 = f2.normalized * maxF * SlideGrip;
                rb.AddForceAtPosition(along * f2.x + side * f2.y, point);
                LastLoad[i] = fz; LastPush[i] = f2.x; LastSide[i] = f2.y;
                spinRate[i] = locked ? 0f : vl / TyreR;
            }

            // anti-roll bars: a squashed wheel lifts its side and pulls the other side down
            for (int a = 0; a < 4; a += 2)
            {
                float f = AntiRoll * (squash[a] - squash[a + 1]);
                Vector3 l = transform.TransformPoint(new Vector3(WX[a], AttachY, WZ[a])), r = transform.TransformPoint(new Vector3(WX[a + 1], AttachY, WZ[a + 1]));
                if (contact[a]) rb.AddForceAtPosition(up * f, l);
                if (contact[a + 1]) rb.AddForceAtPosition(-up * f, r);
            }
            Grounded = on;

            Vector3 v = rb.linearVelocity;
            rb.AddForce(-v * v.magnitude * AirDrag);
            if (on == 0) rb.AddRelativeTorque(new Vector3(Throttle, 0f, -Steer) * AirControl, ForceMode.Acceleration);

            Swim(dt, fwdSpeed);
            KeepAboveGround();
        }

        /// <summary>Peak grip at about 15 degrees of slip, a little less beyond.</summary>
        float Curve(float slip) { return Mathf.Sin(1.4f * Mathf.Atan(CorneringStiffness * slip)); }

        bool CastWheel(Vector3 from, Vector3 dir, float reach, out Vector3 point, out Vector3 normal, out float dist)
        {
            RaycastHit h;
            if (Physics.Raycast(from, dir, out h, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                point = h.point; normal = h.normal; dist = h.distance; return true;
            }
            // no collider here yet: use the height of the ground
            point = normal = Vector3.zero; dist = 0f;
            float gy;
            if (GroundFn == null || dir.y > -0.3f || !GroundFn(from, out gy)) return false;
            dist = (gy - from.y) / dir.y;
            if (dist < 0f || dist > reach) return false;
            point = from + dir * dist; normal = Vector3.up;
            return true;
        }

        /// <summary>Floating: buoyancy and drag at points round the hull, so it bobs and pitches on the swell, and the wheels paddle.</summary>
        void Swim(float dt, float fwdSpeed)
        {
            Afloat = false;
            if (WaterFn == null) return;
            Rigidbody rb = Body;
            float each = Buoyancy * rb.mass * Physics.gravity.magnitude / Hull.Length;
            int wet = 0;
            for (int i = 0; i < Hull.Length; i++)
            {
                Vector3 p = transform.TransformPoint(Hull[i]);
                float s;
                if (!WaterFn(p, out s) || s <= p.y) continue;
                float depth = Mathf.Clamp01((s - p.y) / 0.6f);
                wet++;
                rb.AddForceAtPosition(Vector3.up * each * depth, p);
                Vector3 drag = -rb.GetPointVelocity(p) * WaterDrag * depth;
                drag.y *= 8f;                                   // much more resistance to bobbing than to moving along
                rb.AddForceAtPosition(drag, p);
            }
            Afloat = wet >= 3 && Grounded < 2;
            if (!Afloat) return;
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            rb.AddForceAtPosition(fwd * Throttle * (Throttle < 0f ? 0.5f : 1f) * WaterThrust * rb.mass, transform.TransformPoint(new Vector3(0f, -0.2f, -1.4f)));
            rb.AddTorque(Vector3.up * Steer * WaterTurn * Mathf.Clamp(fwdSpeed / 3f, -1f, 1f), ForceMode.Acceleration);
            rb.AddForce(-right * Vector3.Dot(rb.linearVelocity, right) * rb.mass * 0.6f);        // the hull resists going sideways
        }

        /// <summary>Where the ground has no collider yet, never let the body sink into it.</summary>
        void KeepAboveGround()
        {
            float gy;
            if (GroundFn == null || !GroundFn(Body.position, out gy)) return;
            if (Physics.Raycast(Body.position + Vector3.up * 2f, Vector3.down, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            float low = gy - (Ground + 0.3f);       // centre no lower than this
            if (Body.position.y >= low) return;
            Body.position = new Vector3(Body.position.x, low, Body.position.z);
            Vector3 v = Body.linearVelocity;
            if (v.y < 0f) Body.linearVelocity = new Vector3(v.x, 0f, v.z);
        }

        /// <summary>The wheels where the suspension has put them, turned and spinning.</summary>
        void LateUpdate()
        {
            if (spin == null) return;
            float dt = Time.deltaTime, maxLen = 0.25f + Droop, minLen = 0.25f - Bump;
            for (int i = 0; i < 4; i++)
            {
                float want = Mathf.Clamp(contact[i] ? length[i] : maxLen, minLen, maxLen);
                shown[i] = contact[i] ? want : Mathf.MoveTowards(shown[i], want, 1.5f * dt);
                pivot[i].localPosition = new Vector3(WX[i], AttachY - shown[i], WZ[i]);
                pivot[i].localRotation = Quaternion.Euler(0f, angle[i], 0f);
                spinAt[i] = Mathf.Repeat(spinAt[i] + spinRate[i] * Mathf.Rad2Deg * dt, 360f);
                spin[i].localRotation = Quaternion.Euler(spinAt[i], 0f, 0f);
            }
        }

        // ------------------------------------------------------------ palette

        static Texture2D PaletteTexture()
        {
            var t = new Texture2D(Palette.Length, Variants, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "JeepPalette" };
            var px = new Color32[Palette.Length * Variants];
            var rng = new System.Random(4417);
            for (int v = 0; v < Variants; v++)
                for (int c = 0; c < Palette.Length; c++)
                {
                    // a little lighter or darker, a little warmer or cooler: dust, sun and wear
                    float k = 1f + ((float)rng.NextDouble() - 0.5f) * 0.09f;
                    float warm = ((float)rng.NextDouble() - 0.5f) * 0.025f;
                    Color b = Palette[c];
                    px[v * Palette.Length + c] = new Color(Mathf.Clamp01(b.r * k + warm), Mathf.Clamp01(b.g * k), Mathf.Clamp01(b.b * k - warm), 1f);
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // ------------------------------------------------------------ body

        static Mesh BodyMesh()
        {
            var k = new Kit();
            float top = 0.42f;   // the tub's top edge

            // chassis: frame rails, axles and differentials, side steps
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.43f, -0.22f, 0.02f), new Vector3(0.1f, 0.16f, 3.2f), DarkSteel);
                k.Box(new Vector3(s * 0.69f, -0.19f, -0.02f), new Vector3(0.18f, 0.06f, 1.0f), DarkSteel);
            }
            foreach (float z in new[] { FrontZ, RearZ })
            {
                k.Tube(new Vector3(-Track + 0.12f, AxleY, z), new Vector3(Track - 0.12f, AxleY, z), 0.055f, 8, DarkSteel);
                k.Box(new Vector3(0.08f, AxleY, z), new Vector3(0.24f, 0.2f, 0.22f), DarkSteel);
                for (int s = -1; s <= 1; s += 2)   // springs up to the frame
                    k.Tube(new Vector3(s * 0.43f, AxleY, z), new Vector3(s * 0.43f, -0.3f, z), 0.045f, 8, Steel);
            }

            // the tub: an open box with a floor, a dip in the sides for the doorless doorways
            k.Box(new Vector3(0f, -0.09f, -0.5f), new Vector3(1.24f, 0.06f, 2.0f), Floor);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.595f, (top - 0.12f) * 0.5f, -0.95f), new Vector3(0.05f, top + 0.12f, 1.1f), Paint);
                k.Box(new Vector3(s * 0.595f, (0.18f - 0.12f) * 0.5f, 0.05f), new Vector3(0.05f, 0.30f, 0.9f), Paint);
                k.Box(new Vector3(s * 0.605f, top + 0.012f, -0.95f), new Vector3(0.08f, 0.025f, 1.12f), PaintLight);   // rolled top edge
                k.Box(new Vector3(s * 0.605f, 0.192f, 0.05f), new Vector3(0.08f, 0.025f, 0.9f), PaintLight);
            }
            k.Box(new Vector3(0f, (top - 0.12f) * 0.5f, -1.475f), new Vector3(1.24f, top + 0.12f, 0.05f), Paint);
            k.Box(new Vector3(0f, (top - 0.12f) * 0.5f, 0.475f), new Vector3(1.24f, top + 0.12f, 0.05f), Paint);     // firewall
            k.Box(new Vector3(0f, top + 0.012f, -1.475f), new Vector3(1.26f, 0.025f, 0.08f), PaintLight);

            // bonnet: engine box, a slightly wider lid, latches
            k.Box(new Vector3(0f, 0.13f, 1.0f), new Vector3(1.16f, 0.5f, 1.0f), Paint);
            k.Box(new Vector3(0f, 0.40f, 0.99f), new Vector3(1.2f, 0.04f, 1.04f), PaintLight);
            k.Box(new Vector3(0f, 0.423f, 0.99f), new Vector3(0.5f, 0.012f, 0.9f), Paint);   // a low ridge down the middle
            for (int s = -1; s <= 1; s += 2)
                k.Box(new Vector3(s * 0.6f, 0.33f, 1.32f), new Vector3(0.03f, 0.09f, 0.05f), DarkSteel);

            // front: a dark panel with horizontal bars, round lamps either side, indicators below
            k.Box(new Vector3(0f, 0.14f, 1.505f), new Vector3(0.74f, 0.4f, 0.03f), Grille);
            for (int b = 0; b < 5; b++)
                k.Box(new Vector3(0f, -0.02f + b * 0.08f, 1.53f), new Vector3(0.74f, 0.025f, 0.025f), Paint);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.38f, 0.14f, 1.53f), new Vector3(0.03f, 0.4f, 0.03f), Paint);
                k.Tube(new Vector3(s * 0.48f, 0.22f, 1.49f), new Vector3(s * 0.48f, 0.22f, 1.57f), 0.088f, 16, Steel);
                k.Tube(new Vector3(s * 0.48f, 0.22f, 1.57f), new Vector3(s * 0.48f, 0.22f, 1.578f), 0.07f, 16, Lamp);
                k.Box(new Vector3(s * 0.48f, 0.05f, 1.52f), new Vector3(0.08f, 0.045f, 0.03f), Amber);
            }

            // bumpers, tow points
            k.Box(new Vector3(0f, -0.2f, 1.64f), new Vector3(1.86f, 0.16f, 0.16f), DarkSteel);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.43f, -0.2f, 1.555f), new Vector3(0.1f, 0.1f, 0.05f), DarkSteel);
                k.Box(new Vector3(s * 0.3f, -0.2f, 1.74f), new Vector3(0.04f, 0.08f, 0.06f), Tail);
            }
            k.Box(new Vector3(0f, -0.2f, -1.56f), new Vector3(1.5f, 0.16f, 0.14f), DarkSteel);

            // rear: tail lights and the spare wheel's bracket
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.48f, 0.27f, -1.505f), new Vector3(0.09f, 0.11f, 0.015f), Tail);
                k.Box(new Vector3(s * 0.48f, 0.18f, -1.505f), new Vector3(0.09f, 0.045f, 0.015f), Amber);
            }
            k.Box(new Vector3(0f, 0.16f, -1.53f), new Vector3(0.3f, 0.08f, 0.06f), DarkSteel);
            k.Tube(new Vector3(0f, 0.16f, -1.5f), new Vector3(0f, 0.16f, -1.56f), 0.05f, 10, DarkSteel);

            // flared arches over each wheel: painted arch, black lip on its outer edge
            foreach (float z in new[] { FrontZ, RearZ })
                for (int s = -1; s <= 1; s += 2)
                {
                    Arch(k, s, z, 0.53f, 0.585f, 1.0f, 0.035f, Paint);
                    Arch(k, s, z, 0.555f, 0.97f, 1.03f, 0.05f, Flare);
                }

            // windscreen: a flat frame that would fold forward onto the bonnet, leaning back a little
            k.Push(new Vector3(0f, top, 0.5f), Quaternion.Euler(-12f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(s * 0.6f, 0.25f, 0f), new Vector3(0.045f, 0.5f, 0.045f), Paint);
                k.Box(new Vector3(s * 0.45f, 0.0f, 0.02f), new Vector3(0.06f, 0.04f, 0.05f), DarkSteel);   // hinges
            }
            k.Box(new Vector3(-0.70f, 0.08f, 0.03f), new Vector3(0.14f, 0.02f, 0.02f), DarkSteel);   // a mirror on the driver's side
            k.Box(new Vector3(-0.79f, 0.12f, 0.03f), new Vector3(0.05f, 0.12f, 0.1f), Tube);
            k.Box(new Vector3(0f, 0.025f, 0f), new Vector3(1.24f, 0.05f, 0.05f), Paint);
            k.Box(new Vector3(0f, 0.48f, 0f), new Vector3(1.24f, 0.045f, 0.05f), Paint);
            k.Box(new Vector3(0f, 0.25f, 0f), new Vector3(1.15f, 0.42f, 0.012f), Glass);
            k.Pop();

            // dash and steering wheel (driver on the left)
            k.Box(new Vector3(0f, 0.31f, 0.4f), new Vector3(1.14f, 0.16f, 0.12f), Seat);
            Vector3 col0 = new Vector3(-0.3f, 0.26f, 0.42f), col1 = new Vector3(-0.3f, 0.42f, 0.2f);
            k.Tube(col0, col1, 0.022f, 6, Tube);
            k.Push(col1, Quaternion.LookRotation(col1 - col0));
            for (int i = 0; i < 14; i++)
            {
                float a0 = i * Mathf.PI * 2f / 14f, a1 = (i + 1) * Mathf.PI * 2f / 14f;
                k.Tube(new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.15f, new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.15f, 0.018f, 6, Tube);
            }
            k.Tube(new Vector3(-0.15f, 0f, 0f), new Vector3(0.15f, 0f, 0f), 0.015f, 6, Tube);
            k.Pop();

            // seats: two buckets in front, a bench behind
            for (int s = -1; s <= 1; s += 2)
                Chair(k, new Vector3(s * 0.3f, -0.06f, -0.16f), 0.42f);
            Chair(k, new Vector3(0f, -0.06f, -1.12f), 1.05f);

            // roll bar: a hoop behind the front seats, stays back to the tail, bars forward to the windscreen
            const float r = 0.034f;
            float hz = -0.5f, hy = 1.12f, hx = 0.55f;
            Vector3 wsTop = new Vector3(0f, top + 0.48f * Mathf.Cos(12f * Mathf.Deg2Rad), 0.5f - 0.48f * Mathf.Sin(12f * Mathf.Deg2Rad));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 foot = new Vector3(s * hx, top, hz), knee = new Vector3(s * hx, hy - 0.09f, hz),
                        corner = new Vector3(s * (hx - 0.09f), hy, hz);
                k.Tube(foot, knee, r, 10, Tube);
                k.Tube(knee, corner, r, 10, Tube);
                k.Tube(new Vector3(s * hx, hy - 0.06f, hz), new Vector3(s * hx, top, -1.42f), r, 10, Tube);
                k.Tube(new Vector3(s * hx, hy - 0.05f, hz), new Vector3(s * 0.58f, wsTop.y, wsTop.z), r * 0.9f, 10, Tube);
                k.Box(new Vector3(s * hx, top + 0.01f, hz), new Vector3(0.1f, 0.02f, 0.1f), DarkSteel);      // base plates
                k.Box(new Vector3(s * hx, top + 0.01f, -1.42f), new Vector3(0.1f, 0.02f, 0.1f), DarkSteel);
            }
            k.Tube(new Vector3(-(hx - 0.09f), hy, hz), new Vector3(hx - 0.09f, hy, hz), r, 10, Tube);
            k.Tube(new Vector3(-hx, 0.78f, hz), new Vector3(hx, 0.78f, hz), r * 0.8f, 10, Tube);

            // snorkel: out of the right side of the bonnet, up the windscreen pillar, a forward-facing head on top
            Vector3 sn0 = new Vector3(0.56f, 0.26f, 0.74f), sn1 = new Vector3(0.68f, 0.26f, 0.62f), sn2 = new Vector3(0.68f, 1.0f, 0.36f);
            k.Tube(sn0, sn1, 0.045f, 10, Tube);
            k.Tube(sn1, sn2, 0.045f, 10, Tube);
            k.Push(sn2, Quaternion.Euler(-12f, 0f, 0f));
            k.Box(new Vector3(0f, 0.03f, 0.04f), new Vector3(0.11f, 0.1f, 0.2f), Tube);
            k.Box(new Vector3(0f, 0.03f, 0.145f), new Vector3(0.09f, 0.07f, 0.01f), Grille);
            k.Pop();

            return k.Finish("Jeep body");
        }

        /// <summary>An arch over a wheel, from front to back, x from x0 to x1 on side s.</summary>
        static void Arch(Kit k, int s, float z, float radius, float x0, float x1, float thick, int col)
        {
            const int n = 9;
            float a0 = 12f * Mathf.Deg2Rad, a1 = 168f * Mathf.Deg2Rad;
            float xc = s * (x0 + x1) * 0.5f, w = x1 - x0;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(a0, a1, (i + 0.5f) / n);
                float len = radius * (a1 - a0) / n + 0.01f;
                Vector3 c = new Vector3(xc, AxleY + Mathf.Sin(a) * (radius + thick * 0.5f), z + Mathf.Cos(a) * (radius + thick * 0.5f));
                k.Push(c, Quaternion.Euler(90f - a * Mathf.Rad2Deg, 0f, 0f));
                k.Box(Vector3.zero, new Vector3(w, thick, len), col);
                k.Pop();
            }
        }

        static void Chair(Kit k, Vector3 at, float width)
        {
            k.Box(at + new Vector3(0f, 0.04f, 0f), new Vector3(width * 0.8f, 0.08f, 0.3f), DarkSteel);   // frame
            k.Box(at + new Vector3(0f, 0.13f, 0.02f), new Vector3(width, 0.1f, 0.42f), Seat);            // cushion
            k.Push(at + new Vector3(0f, 0.16f, -0.18f), Quaternion.Euler(-12f, 0f, 0f));
            k.Box(new Vector3(0f, 0.25f, 0f), new Vector3(width, 0.5f, 0.1f), Seat);
            k.Pop();
        }

        // ------------------------------------------------------------ wheel

        /// <summary>One wheel round the origin, axle along x, the rim on the -x side (mirrored for the right-hand wheels).</summary>
        static Mesh WheelMesh()
        {
            var k = new Kit();
            float hw = TyreW * 0.5f, core = TyreR - 0.055f;
            k.Tube(new Vector3(hw, 0f, 0f), new Vector3(-hw, 0f, 0f), core, 28, Rubber);
            k.Tube(new Vector3(hw + 0.01f, 0f, 0f), new Vector3(-hw - 0.01f, 0f, 0f), core - 0.06f, 28, Rubber);   // bulging sidewalls

            // tread: two staggered rows of chunky blocks, the outer ends wrapping a little onto the sidewalls
            const int lugs = 18;
            for (int i = 0; i < lugs; i++)
                for (int row = 0; row < 2; row++)
                {
                    float a = (i + row * 0.5f) * 360f / lugs;
                    float side = row == 0 ? -1f : 1f;
                    k.Push(Vector3.zero, Quaternion.Euler(a, 0f, 0f));
                    k.Box(new Vector3(side * (hw * 0.5f + 0.008f), core + 0.03f, 0f), new Vector3(hw + 0.01f, 0.065f, 0.085f), Tread);
                    k.Box(new Vector3(side * (hw + 0.012f), core - 0.01f, 0f), new Vector3(0.025f, 0.07f, 0.07f), Tread);
                    k.Box(new Vector3(0f, core + 0.012f, 0.03f * side), new Vector3(0.05f, 0.03f, 0.05f), Tread);   // centre ribs
                    k.Pop();
                }

            // steel rim, set into the tyre, with a hub and five nuts
            float face = -hw - 0.012f;
            k.Tube(new Vector3(face + 0.03f, 0f, 0f), new Vector3(face, 0f, 0f), 0.24f, 24, Rim);
            k.Tube(new Vector3(face, 0f, 0f), new Vector3(face - 0.006f, 0f, 0f), 0.2f, 24, Steel);
            k.Tube(new Vector3(face - 0.006f, 0f, 0f), new Vector3(face - 0.05f, 0f, 0f), 0.075f, 12, DarkSteel);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                Vector3 c = new Vector3(face - 0.006f, Mathf.Cos(a) * 0.12f, Mathf.Sin(a) * 0.12f);
                k.Tube(c, c + new Vector3(-0.025f, 0f, 0f), 0.017f, 6, Steel);
            }
            return k.Finish("Jeep wheel");
        }

        // ------------------------------------------------------------ mesh kit

        /// <summary>
        /// Boxes and tubes into one mesh. Every face gets its own vertices and a palette cell picked by where it is,
        /// and long faces are cut into patches, each its own shade of the colour.
        /// </summary>
        sealed class Kit
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();
            readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
            Matrix4x4 m = Matrix4x4.identity;
            const float Patch = 0.28f;

            public void Push(Vector3 pos, Quaternion rot) { stack.Push(m); m = m * Matrix4x4.TRS(pos, rot, Vector3.one); }
            public void Pop() { m = stack.Pop(); }

            static Vector2 Cell(int col, Vector3 p)
            {
                int h = Mathf.FloorToInt(p.x * 73.1f) * 73856093 ^ Mathf.FloorToInt(p.y * 79.3f) * 19349663 ^ Mathf.FloorToInt(p.z * 83.7f) * 83492791;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                int variant = (h & 0x7fffffff) % Variants;
                return new Vector2((col + 0.5f) / Palette.Length, (variant + 0.5f) / Variants);
            }

            /// <summary>A flat quad a-b-c-d facing out along normal, cut into patches when it is big.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, int col)
            {
                a = m.MultiplyPoint3x4(a); b = m.MultiplyPoint3x4(b); c = m.MultiplyPoint3x4(c); d = m.MultiplyPoint3x4(d);
                normal = m.MultiplyVector(normal).normalized;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) { Vector3 x = b; b = d; d = x; }
                int nu = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / Patch)), nv = Mathf.Max(1, Mathf.CeilToInt((d - a).magnitude / Patch));
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        float u0 = (float)i / nu, u1 = (float)(i + 1) / nu, v0 = (float)j / nv, v1 = (float)(j + 1) / nv;
                        Vector3 p00 = Bi(a, b, c, d, u0, v0), p10 = Bi(a, b, c, d, u1, v0), p11 = Bi(a, b, c, d, u1, v1), p01 = Bi(a, b, c, d, u0, v1);
                        Vector2 cell = Cell(col, (p00 + p11) * 0.5f);
                        int k = v.Count;
                        v.Add(p00); v.Add(p10); v.Add(p11); v.Add(p01);
                        for (int q = 0; q < 4; q++) { n.Add(normal); uv.Add(cell); }
                        t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
                    }
            }

            static Vector3 Bi(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u, float w)
            {
                return Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), w);
            }

            public void Box(Vector3 c, Vector3 size, int col)
            {
                Vector3 h = size * 0.5f;
                Vector3 X = new Vector3(h.x, 0, 0), Y = new Vector3(0, h.y, 0), Z = new Vector3(0, 0, h.z);
                Face(c + X, Y, Z, Vector3.right, col); Face(c - X, Y, Z, Vector3.left, col);
                Face(c + Y, X, Z, Vector3.up, col); Face(c - Y, X, Z, Vector3.down, col);
                Face(c + Z, X, Y, Vector3.forward, col); Face(c - Z, X, Y, Vector3.back, col);
            }

            void Face(Vector3 c, Vector3 u, Vector3 w, Vector3 normal, int col)
            {
                Quad(c - u - w, c + u - w, c + u + w, c - u + w, normal, col);
            }

            /// <summary>A capped round bar from a to b; its sides are smooth, each strip its own shade.</summary>
            public void Tube(Vector3 a, Vector3 b, float radius, int sides, int col)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, e2 = Vector3.Cross(axis, e1);
                var ring = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides;
                    ring[i] = e1 * Mathf.Cos(ang) + e2 * Mathf.Sin(ang);
                }
                for (int i = 0; i < sides; i++)
                {
                    Vector3 r0 = ring[i], r1 = ring[(i + 1) % sides];
                    Vector3 p0 = m.MultiplyPoint3x4(a + r0 * radius), p1 = m.MultiplyPoint3x4(a + r1 * radius),
                            p2 = m.MultiplyPoint3x4(b + r1 * radius), p3 = m.MultiplyPoint3x4(b + r0 * radius);
                    Vector3 n0 = m.MultiplyVector(r0).normalized, n1 = m.MultiplyVector(r1).normalized;
                    Vector2 cell = Cell(col, (p0 + p2) * 0.5f);
                    int k = v.Count;
                    v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                    n.Add(n0); n.Add(n1); n.Add(n1); n.Add(n0);
                    for (int q = 0; q < 4; q++) uv.Add(cell);
                    if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n0 + n1) >= 0f) { t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3); }
                    else { t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2); }
                }
                Cap(a, ring, radius, -axis, col);
                Cap(b, ring, radius, axis, col);
            }

            void Cap(Vector3 c, Vector3[] ring, float radius, Vector3 normal, int col)
            {
                Vector3 wc = m.MultiplyPoint3x4(c), wn = m.MultiplyVector(normal).normalized;
                int k = v.Count;
                v.Add(wc); n.Add(wn); uv.Add(Cell(col, wc));
                Vector2 cell = Cell(col, wc + wn * 0.01f);
                for (int i = 0; i < ring.Length; i++) { v.Add(m.MultiplyPoint3x4(c + ring[i] * radius)); n.Add(wn); uv.Add(cell); }
                uv[k] = cell;
                for (int i = 0; i < ring.Length; i++)
                {
                    int i0 = k + 1 + i, i1 = k + 1 + (i + 1) % ring.Length;
                    if (Vector3.Dot(Vector3.Cross(v[i0] - wc, v[i1] - wc), wn) >= 0f) { t.Add(k); t.Add(i0); t.Add(i1); }
                    else { t.Add(k); t.Add(i1); t.Add(i0); }
                }
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
