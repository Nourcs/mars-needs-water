using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The player's car: an open-topped off-road 4x4, built in code from soft, rounded shapes with smooth shading:
    /// a chunky tub and bonnet, wide curved mudguards, a round-bar roll hoop and windscreen frame, fat tyres with
    /// rounded tread blocks, a spare on the back and a snorkel up the right pillar. One clean colour per part, taken
    /// from a small palette texture.
    /// Local space: +z forward, y up, the ground -Ground below the origin. Drawn at full size and shrunk by Size.
    /// </summary>
    public sealed class Jeep : MonoBehaviour
    {
        /// <summary>
        /// How big the jeep is, against the size it was drawn at (about 1.9 m wide and 3.5 m long at 1). The streets of
        /// the mini Earth are narrow, about 1 m a lane, so it is half that: it fits a lane and can turn in a side street.
        /// </summary>
        public const float Size = 0.5f;
        // the model is drawn at these sizes and shrunk by Size
        const float DGround = -0.85f, DTyreR = 0.43f, DTyreW = 0.34f, DTrack = 0.80f, DFrontZ = 1.08f, DRearZ = -1.02f, DAxleY = DGround + DTyreR;
        public const float Ground = DGround * Size;      // the bottom of the tyres, this far below the jeep's origin
        public const float TyreR = DTyreR * Size, TyreW = DTyreW * Size;
        public const float Track = DTrack * Size, FrontZ = DFrontZ * Size, RearZ = DRearZ * Size;
        const float AxleY = Ground + TyreR;

        // palette rows
        const int Paint = 0, Flare = 1, Rubber = 2, Tread = 3, Steel = 4, DarkSteel = 5, Rim = 6, Seat = 7,
                  Glass = 8, Lamp = 9, Tail = 10, Amber = 11, Floor = 12, Tube = 13, Grille = 14, PaintLight = 15;
        const int Variants = 1;

        static readonly Color[] Palette =
        {
            new Color(0.93f, 0.43f, 0.12f),   // paint: a warm orange that stands out on grass, sand and snow
            new Color(0.14f, 0.14f, 0.16f),   // mudguards
            new Color(0.075f, 0.075f, 0.08f), // tyre rubber
            new Color(0.11f, 0.11f, 0.115f),  // tread blocks
            new Color(0.80f, 0.81f, 0.82f),   // bright metal
            new Color(0.21f, 0.21f, 0.24f),   // dark metal: chassis, bumpers
            new Color(0.86f, 0.85f, 0.80f),   // wheel rims
            new Color(0.63f, 0.43f, 0.25f),   // tan seats
            new Color(0.62f, 0.82f, 0.93f),   // windscreen glass
            new Color(1.00f, 0.97f, 0.82f),   // headlight lens
            new Color(0.85f, 0.12f, 0.08f),   // tail lights
            new Color(1.00f, 0.62f, 0.10f),   // indicators
            new Color(0.20f, 0.19f, 0.18f),   // floor
            new Color(0.13f, 0.13f, 0.15f),   // roll bar, snorkel, dash
            new Color(0.07f, 0.07f, 0.08f),   // grille backing
            new Color(0.97f, 0.92f, 0.80f),   // cream
        };

        // ---- tuning: change these on the Jeep object in the Hierarchy while playing to try them out

        [Header("Body")]
        [Tooltip("Mass, kg.")] public float Mass = 1650f;
        [Tooltip("Height of the centre of mass above the ground, m. Higher = tips over more easily.")] public float CentreHeight = 0.42f * Size;
        [Tooltip("Air drag. Higher = more slowing at speed.")] public float AirDrag = 0.6f;

        [Header("Engine and brakes")]
        [Tooltip("Engine pull at low speed, as m/s^2 of acceleration on the flat (it fades to nothing at top speed).")] public float Accel = 6.5f;
        [Tooltip("Top speed, m/s.")] public float TopSpeed = 22f;
        [Tooltip("Top speed holding Shift, m/s.")] public float BoostSpeed = 45f;
        [Tooltip("Engine pull holding Shift, times the normal pull.")] public float BoostPower = 1.8f;
        [Tooltip("Top speed backwards, m/s.")] public float ReverseSpeed = 7f;
        [Tooltip("Brakes (S while moving forward), m/s^2 if the tyres can take it.")] public float Brake = 22f;
        [Tooltip("Handbrake (Space): locks the rear wheels, m/s^2.")] public float Handbrake = 7f;
        [Tooltip("Engine braking and holding with no pedal pressed, m/s^2.")] public float EngineBrake = 3.5f;
        [Tooltip("Rolling resistance per wheel, newtons per m/s.")] public float RollingResistance = 12f;

        [Tooltip("Extra bite the tyres have under the brakes, times their normal grip. Higher = shorter stops.")] public float BrakeGrip = 1.8f;

        [Header("Tyres and steering")]
        [Tooltip("Tyre friction: 1 is good rubber on dry ground. Lower = slidier everywhere.")] public float Grip = 1.2f;
        [Tooltip("Share of the grip left once a tyre is sliding.")] [Range(0.3f, 1f)] public float SlideGrip = 0.8f;
        [Tooltip("How sharply sideways grip builds with slip angle. Higher = more direct.")] public float CorneringStiffness = 8f;
        [Tooltip("Rear sideways grip with the handbrake on, as a share. Lower = easier handbrake turns.")] [Range(0f, 1f)] public float HandbrakeRearGrip = 0.4f;
        [Tooltip("Front wheel angle at full lock, degrees.")] public float WheelLock = 32f;
        [Tooltip("Share of full lock left at top speed.")] [Range(0.1f, 1f)] public float HighSpeedLock = 0.2f;
        [Tooltip("How quickly the steering follows the keys.")] public float SteerSpeed = 6f;

        [Header("Drifting")]
        [Tooltip("Rear sideways grip left while drifting, as a share. Lower = longer, wider slides.")] [Range(0.3f, 1f)] public float DriftRearGrip = 0.68f;
        [Tooltip("How far sideways (degrees) the jeep must be sliding for a drift to start by itself. The handbrake with some steering starts one at once.")] public float DriftStart = 13f;
        [Tooltip("How far sideways the jeep slides in a drift with the steering held right over, degrees. Less steering = a shallower slide; none = it straightens.")] public float DriftAngle = 30f;
        [Tooltip("How firmly the jeep is held at that angle instead of spinning round. Higher = tidier, lower = wilder.")] public float DriftHold = 9f;
        [Tooltip("In a drift the front wheels turn into the slide by themselves; this is how far past that they bite into the corner, degrees. Higher = tighter drifts.")] public float DriftBite = 12f;
        [Tooltip("On loose ground (sand, snow, mud) full throttle while steering lets the tail step out: 0 never, 1 a lot.")] [Range(0f, 1f)] public float PowerSlide = 0.45f;
        [Tooltip("Share of full lock at top speed while drifting, so there is enough steering to catch the slide.")] [Range(0.1f, 1f)] public float DriftLock = 0.55f;

        [Header("Body movement (looks only)")]
        [Tooltip("How far the body leans out of corners, squats and dives, degrees per m/s^2. 0 = rigid.")] public float BodyLean = 0.42f;
        [Tooltip("How quickly the body follows, and how much it wobbles after (lower = wobblier).")] public float BodySpring = 9f;
        [Range(0.1f, 1.5f)] public float BodyDamping = 0.42f;
        [Tooltip("Turning in the air with the steering keys, rad/s^2.")] public float AirSteer = 1.0f;

        [Header("Suspension")]
        [Tooltip("Spring rate per wheel, N/m. Lower = softer and more body roll.")] public float SpringRate = 27000f;
        [Tooltip("Damping while a wheel is pushed up, as a share of critical (1 = no overshoot).")] [Range(0.1f, 2f)] public float BumpDamping = 0.85f;
        [Tooltip("Damping while a wheel extends again, as a share of critical. Higher than bump so it does not spring back up.")] [Range(0.1f, 2f)] public float ReboundDamping = 1.0f;
        [Tooltip("Anti-roll bars, N/m of difference between left and right. Higher = flatter in corners.")] public float AntiRoll = 30000f;
        [Tooltip("How far a wheel can push up into its arch, m.")] public float Bump = 0.5f * Size;
        [Tooltip("How far a wheel can hang down, m.")] public float Droop = 0.4f * Size;

        [Header("Driving aids")]
        [Tooltip("Keeps the jeep turning the way the wheels point instead of spinning out: 0 off, 1 strong. Off while the handbrake is on.")] [Range(0f, 1f)] public float StabilityAssist = 0.7f;
        [Tooltip("Pushes back against body roll beyond a few degrees while the wheels are down, so normal driving never tips it over.")] public float RollAssist = 6f;
        [Tooltip("Calms pitching and rolling while the wheels are down, 1/s. Higher = steadier over rough ground, lower = livelier.")] public float Steadiness = 3f;
        [Tooltip("Gently levels the roll (not the pitch) in the air.")] public float AirLevel = 2f;
        [Tooltip("Extra grip on the ground that grows with speed, newtons per (m/s)^2. Keeps it down over small crests at speed.")] public float DownForce = 6f;
        [Tooltip("Most upward acceleration a bump may give the body while the wheels are down, m/s^2. Stops it pogoing off terrain edges.")] public float BumpKick = 22f;
        [Tooltip("Radius of the ball each wheel feels the ground with, m. Bigger rolls more smoothly over edges in the ground.")] public float ContactRadius = 0.4f * Size;
        [Tooltip("On the terrain the wheels feel its height averaged over this radius (m), plus more with speed, so they roll over the creases between its flat facets (about 6 m apart) instead of being kicked by every one.")] public float TerrainSmoothing = 2.5f;
        [Tooltip("Extra terrain smoothing radius per m/s of speed.")] public float SmoothingPerSpeed = 0.08f;

        [Tooltip("Extra pull down while all four wheels are in the air, in g. Higher = shorter, lower hops.")] public float AirGravity = 1.0f;

        [Header("Water")]
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

        const float StaticLen = 0.4f * Size;                    // at rest each axle hangs this far below its spring's mount
        const float AttachY = AxleY + StaticLen;
        Transform[] spin, steer, pivot;
        float steerNow;
        readonly float[] rateF = new float[4], length = new float[4], shown = new float[4], angle = new float[4], spinRate = new float[4], spinAt = new float[4], squash = new float[4];
        readonly bool[] contact = new bool[4];

        // ---- what each wheel is doing, for the tyre tracks and dust (JeepTrails), which also tells the wheels what they are on
        /// <summary>The ground under each wheel: grip (1 = a dry road), how loose it is (0 hard, 1 deep sand or snow), and how much it holds the wheel back.</summary>
        [System.NonSerialized] public readonly float[] SurfGrip = { 1f, 1f, 1f, 1f }, SurfLoose = new float[4], SurfDrag = new float[4];
        [System.NonSerialized] public readonly Vector3[] WheelPoint = new Vector3[4], WheelNormal = new Vector3[4];
        /// <summary>Sideways slide of each tyre over the ground (m/s), its speed along the ground (m/s), and how much it is spinning or locked (0-1).</summary>
        [System.NonSerialized] public readonly float[] WheelSlide = new float[4], WheelRoll = new float[4], WheelSpin = new float[4];
        /// <summary>The wheel stands on the terrain itself (not a bridge, a roof or a landmark).</summary>
        [System.NonSerialized] public readonly bool[] WheelOnTerrain = new bool[4];
        public bool WheelDown(int i) { return contact[i] && Body != null && !Body.isKinematic; }
        /// <summary>0 = gripping, 1 = in a full drift.</summary>
        public float Drift { get; private set; }
        /// <summary>How far sideways the body is sliding, degrees (right positive).</summary>
        public float SlideAngle { get; private set; }

        Transform bodyT;
        Vector3 lastVel, accel;                  // the body's acceleration in its own axes, smoothed
        float lastBeta, betaRate;
        Vector3 leanAt, leanRate;                // x pitch, z roll (degrees), y heave (m)
        /// <summary>What the ride has been like since Reset: for tests and tuning.</summary>
        public sealed class Telemetry
        {
            public int Steps, AirSteps, BottomOuts, Landings, AfloatSteps, BodyHits;
            public float Climb, Drop, LastY = float.NaN;
            public readonly System.Collections.Generic.Dictionary<string, int> Hit = new System.Collections.Generic.Dictionary<string, int>(), Rode = new System.Collections.Generic.Dictionary<string, int>();
            public void Count(System.Collections.Generic.Dictionary<string, int> d, Collider c) { string k = c == null ? "(height only)" : c.name + " [" + (c.transform.parent != null ? c.transform.parent.name : "-") + "]"; int n; d.TryGetValue(k, out n); d[k] = n + 1; }
            public float Contact, VertAccelSq, MaxVertAccel, MaxPitchRate, MaxRollRate, SpeedSum;
            public void Reset() { Steps = AirSteps = BottomOuts = Landings = AfloatSteps = BodyHits = 0; Climb = Drop = 0f; LastY = float.NaN; Hit.Clear(); Rode.Clear(); Contact = VertAccelSq = MaxVertAccel = MaxPitchRate = MaxRollRate = SpeedSum = 0f; }
            static string Top(System.Collections.Generic.Dictionary<string, int> d)
            {
                var l = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(d);
                l.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < l.Count && i < 6; i++) sb.Append(l[i].Key).Append(" x").Append(l[i].Value).Append("; ");
                return sb.ToString();
            }

            public override string ToString()
            {
                float n = Mathf.Max(Steps, 1), secs = Steps * Time.fixedDeltaTime;
                return $"avg speed {SpeedSum / n:F1} m/s, wheels on the ground {100f * Contact / n:F0}% of the time, all four off {100f * AirSteps / n:F0}%, " +
                       $"{Landings} landings, {BottomOuts / Mathf.Max(secs, 0.01f):F1} bottom-outs/s, body vertical accel RMS {Mathf.Sqrt(VertAccelSq / n):F1} m/s^2 (peak {MaxVertAccel:F0}), " +
                       $"peak pitch rate {MaxPitchRate * Mathf.Rad2Deg:F0}°/s, peak roll rate {MaxRollRate * Mathf.Rad2Deg:F0}°/s, " +
                       $"afloat {100f * AfloatSteps / n:F0}%, {BodyHits} body hits, climbed {Climb:F0} m and dropped {Drop:F0} m" +
                       "\n      wheels on: " + Top(Rode) + "\n      body hit: " + Top(Hit);
            }
        }
        [System.NonSerialized] public readonly Telemetry Ride = new Telemetry();
        float lastVy; int lastOn;
        Collider lastCollider;

        [System.NonSerialized] public readonly float[] LastLoad = new float[4], LastPush = new float[4], LastSide = new float[4];   // for tests
        static readonly float[] WX = { -Track, Track, -Track, Track }, WZ = { FrontZ, FrontZ, RearZ, RearZ };
        static readonly Vector3[] Hull = Sized(
            new Vector3(-0.6f, -0.2f, 1.45f), new Vector3(0.6f, -0.2f, 1.45f), new Vector3(-0.6f, -0.2f, -1.45f), new Vector3(0.6f, -0.2f, -1.45f),
            new Vector3(-0.6f, -0.2f, 0f), new Vector3(0.6f, -0.2f, 0f), new Vector3(0f, -0.2f, 0.75f), new Vector3(0f, -0.2f, -0.75f));
        static Vector3[] Sized(params Vector3[] p) { for (int i = 0; i < p.Length; i++) p[i] *= Size; return p; }
        readonly RaycastHit[] hits = new RaycastHit[8];

        float RestLength { get { return StaticLen + Mass * 9.81f / 4f / Mathf.Max(SpringRate, 1f); } }

        /// <summary>Builds a jeep (its own physics body, not parented to anything) and returns it.</summary>
        public static Jeep Build()
        {
            var root = new GameObject("Jeep");
            Jeep j = root.AddComponent<Jeep>();
            Material mat = EarthWorld.Mat(PaletteTexture(), 0.3f);

            Mesh bodyMesh = BodyMesh(), wheel = WheelMesh();
            Transform body = Part(root.transform, "Body", bodyMesh, mat, Vector3.zero, Quaternion.identity).transform;
            j.bodyT = body;
            Part(body, "Spare", wheel, mat, new Vector3(0f, 0.16f, -1.71f) * Size, Quaternion.Euler(0f, -90f, 0f));

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
                j.length[i] = j.shown[i] = StaticLen;
            }

            // the body's solid shape: the tub and bonnet, and the roll bar it lands on if it goes over.
            // The wheels have none: they find the ground with rays. On the Ignore Raycast layer so those rays,
            // and the camera's, pass through the jeep itself.
            var pm = new PhysicsMaterial("Jeep") { dynamicFriction = 0.5f, staticFriction = 0.6f, bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum };
            // (its underside is kept well clear of the ground: the wheels feel the terrain smoothed, and a low belly would
            // catch on every sharp crease the wheels have just rolled over)
            Box(root.transform, new Vector3(0f, 0.34f, 0.02f) * Size, new Vector3(1.3f, 0.36f, 3.3f) * Size, pm);
            Box(root.transform, new Vector3(0f, 0.78f, -0.5f) * Size, new Vector3(1.2f, 0.7f, 0.2f) * Size, pm);
            Box(root.transform, new Vector3(0f, 0.66f, 0.45f) * Size, new Vector3(1.3f, 0.5f, 0.1f) * Size, pm);

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.3f;
            rb.maxAngularVelocity = 7f;
            if (Time.fixedDeltaTime > 0.01f) Time.fixedDeltaTime = 0.01f;      // 100 steps a second: at 60 m/s the wheels feel the ground every 0.6 m
            j.Body = rb;
            j.Tune();
            if (Application.isPlaying) root.AddComponent<JeepTrails>().Init(j);      // tyre tracks, dust and what the ground does to the grip
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
            rb.centerOfMass = new Vector3(0f, Ground + CentreHeight, 0.05f * Size);
            float m = rb.mass / 12f * Size;      // a little more than its size alone would give: a small light body turns too nervously
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
            for (int i = 0; i < 4; i++) { length[i] = shown[i] = StaticLen; rateF[i] = 0f; contact[i] = false; spinRate[i] = 0f; }
            steerNow = 0f; Grounded = 0; Afloat = false;
            Drift = 0f; SlideAngle = 0f; lastBeta = betaRate = 0f; lastVel = accel = Vector3.zero;
            for (int i = 0; i < 4; i++) WheelSlide[i] = WheelRoll[i] = WheelSpin[i] = 0f;
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
            Vector3 pos = Body.position + Vector3.up * (0.4f + 0.8f * Size);
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
            steerNow = Mathf.Lerp(steerNow, Mathf.Clamp(Steer, -1f, 1f), 1f - Mathf.Exp(-(Steer == 0f ? 12f : SteerSpeed) * dt));   // quick to centre again

            // a single bump may only throw the body up so hard while the wheels are down
            float rise = rb.linearVelocity.y - lastVy;
            if (lastOn > 0 && rise > BumpKick * dt) rb.linearVelocity -= Vector3.up * (rise - BumpKick * dt) * 0.8f;

            // the body's own acceleration (for the lean of the body) and how far sideways it is sliding
            accel = Vector3.Lerp(accel, transform.InverseTransformDirection((rb.linearVelocity - lastVel) / dt), 1f - Mathf.Exp(-10f * dt));
            lastVel = rb.linearVelocity;
            Vector3 lv = transform.InverseTransformDirection(rb.linearVelocity);
            float flat = Mathf.Sqrt(lv.x * lv.x + lv.z * lv.z);
            float beta = flat > 3f ? Mathf.Atan2(lv.x, Mathf.Abs(lv.z)) : 0f;
            SlideAngle = beta * Mathf.Rad2Deg;
            betaRate = Mathf.Lerp(betaRate, (beta - lastBeta) / dt, 0.25f);
            lastBeta = beta;

            // drifting: begun with the handbrake and some steering, or by sliding wide enough; it lasts while the jeep is
            // still well sideways, and ends quickly once it has straightened
            float loose = (SurfLoose[2] + SurfLoose[3]) * 0.5f;
            bool start = flat > 6f && lv.z > 0f && lastOn >= 2 && ((Handbraking && Mathf.Abs(Steer) > 0.1f) || Mathf.Abs(SlideAngle) > DriftStart * Mathf.Lerp(1f, 0.85f, loose));
            if (start) Drift = Mathf.MoveTowards(Drift, 1f, 5f * dt);
            else Drift = Mathf.MoveTowards(Drift, 0f, (Mathf.Abs(SlideAngle) < 7f || flat < 4f ? 3f : 0.5f) * dt);

            float lockShare = Mathf.Lerp(1f, HighSpeedLock, Mathf.Clamp01(Mathf.Abs(fwdSpeed) / Mathf.Max(TopSpeed, 1f)));
            float lockDeg = WheelLock * Mathf.Lerp(lockShare, Mathf.Max(lockShare, DriftLock), Drift);
            float delta = steerNow * lockDeg * Mathf.Deg2Rad;
            // in a drift the front wheels point where the jeep is going (opposite lock), and a little further into the corner
            if (Drift > 0.01f) delta = Mathf.Lerp(delta, Mathf.Clamp(beta + steerNow * DriftBite * Mathf.Deg2Rad, -0.8f, 0.8f), Drift);
            angle[0] = angle[1] = angle[2] = angle[3] = 0f;
            if (Mathf.Abs(delta) > 1e-4f)
            {
                float wb = FrontZ - RearZ, r = wb / Mathf.Tan(Mathf.Abs(delta));
                float inner = Mathf.Atan(wb / Mathf.Max(r - Track, 0.05f)) * Mathf.Rad2Deg, outer = Mathf.Atan(wb / (r + Track)) * Mathf.Rad2Deg;
                if (delta > 0f) { angle[1] = inner; angle[0] = outer; } else { angle[0] = -inner; angle[1] = -outer; }
            }

            float top = Boosting ? BoostSpeed : TopSpeed;
            float pull = Accel * rb.mass * (Boosting ? BoostPower : 1f) / 4f;
            bool braking = (Throttle < 0f && fwdSpeed > 0.5f) || (Throttle > 0f && fwdSpeed < -0.5f);   // below that it drives the other way at once
            float rest = RestLength, minLen = StaticLen - Bump, maxLen = StaticLen + Droop;
            float critical = 2f * Mathf.Sqrt(Mathf.Max(SpringRate, 1f) * share);
            int on = 0;
            Vector3 groundUp = Vector3.zero;

            for (int i = 0; i < 4; i++)
            {
                Vector3 attach = transform.TransformPoint(new Vector3(WX[i], AttachY, WZ[i]));
                Vector3 point, normal; float dist;
                bool hit = CastWheel(attach, -up, maxLen + TyreR, out point, out normal, out dist);
                float len = hit ? dist - TyreR : maxLen;
                rateF[i] = Mathf.Lerp(rateF[i], (length[i] - len) / dt, 0.4f);   // how fast it is being squashed, smoothed over a few steps
                float rate = rateF[i];
                length[i] = len;
                contact[i] = hit;
                WheelOnTerrain[i] = hit && lastCollider != null && lastCollider.name == "Ground";
                if (hit) { WheelPoint[i] = point; WheelNormal[i] = normal; }
                if (!hit) { rateF[i] = 0f; squash[i] = LastLoad[i] = LastPush[i] = LastSide[i] = 0f; WheelSlide[i] = WheelRoll[i] = WheelSpin[i] = 0f; spinRate[i] *= Mathf.Exp(-0.4f * dt); if (Throttle != 0f) spinRate[i] = Mathf.MoveTowards(spinRate[i], Throttle * top / TyreR, 30f * dt); continue; }
                on++;
                groundUp += normal;
                if (Ride.Steps % 10 == 0) Ride.Count(Ride.Rode, lastCollider);

                // spring and damper, then the bump stop
                // spring, damper (soft over sharp bumps, firm on rebound, and blowing off rather than kicking on a sudden edge),
                // then a bump stop that comes in gradually over the last few centimetres
                float damp = Mathf.Clamp(critical * (rate > 0f ? BumpDamping : ReboundDamping) * rate, -share * g * 2f, share * g * 2f);
                float fz = SpringRate * (rest - len) + damp;
                const float zone = 0.06f;
                if (len < minLen + zone) { float x = minLen + zone - len; fz += SpringRate * 4f * x * x / zone; }
                if (len < minLen) fz += SpringRate * 15f * (minLen - len);
                fz = Mathf.Clamp(fz, 0f, share * g * 8f);
                squash[i] = rest - len;
                rb.AddForceAtPosition(up * fz, attach);

                // the tyre: which way it rolls and slides on the ground it stands on
                Vector3 wf = Quaternion.AngleAxis(angle[i], up) * transform.forward;
                Vector3 along = Vector3.ProjectOnPlane(wf, normal).normalized, side = Vector3.Cross(normal, along);
                Vector3 pv = rb.GetPointVelocity(point);
                float vl = Vector3.Dot(pv, along), vs = Vector3.Dot(pv, side);
                bool rear = i >= 2, locked = Handbraking && rear;
                float looseHere = Mathf.Clamp01(SurfLoose[i]);
                float mu = Mathf.Max(Grip * SurfGrip[i], 0f), maxF = mu * fz;
                // under the brakes the tyres bite harder than they corner, and count on at least most of the wheel's share of
                // the weight even while the suspension is still moving: stopping is strong and the same every time
                float brakeF = braking ? Mathf.Max(maxF, mu * share * g * 0.8f) * Mathf.Max(BrakeGrip, 1f) : maxF;
                float slideGrip = SlideGrip * Mathf.Lerp(1f, 0.85f, looseHere);
                // the rear tyres let go a little in a drift, and under power with the wheel turned on loose ground
                float rearShare = 1f;
                if (rear && !locked)
                    rearShare = Mathf.Lerp(1f, DriftRearGrip, Drift) * (1f - PowerSlide * looseHere * Mathf.Clamp01(Throttle) * Mathf.Abs(steerNow) * Mathf.Clamp01(flat / 8f));

                // sideways: grip builds with slip angle up to a peak, then fades a little (never more than it takes to stop the slide)
                float slip = Mathf.Atan2(vs, Mathf.Max(Mathf.Abs(vl), 0.5f));
                float fy = -Mathf.Sign(slip) * maxF * (locked ? HandbrakeRearGrip : rearShare) * Curve(Mathf.Abs(slip));
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
                // soft ground holds the wheels back, more the faster they plough through it
                if (SurfDrag[i] > 0f) fx -= Mathf.Sign(vl) * Mathf.Min(SurfDrag[i] * share * (1f + 0.08f * Mathf.Abs(vl)), stopX);

                // a tyre can only push so hard in all directions together. Cornering grip comes first (traction control and
                // anti-lock brakes), except for wheels locked by the handbrake, which simply slide.
                Vector2 f2 = new Vector2(fx, fy);
                float spinning = 0f;
                if (locked) { if (f2.magnitude > maxF) f2 = f2.normalized * maxF * slideGrip; spinning = Mathf.Clamp01(Mathf.Abs(vl) / 3f); }
                else
                {
                    if (Mathf.Abs(f2.y) > maxF) f2.y = Mathf.Sign(f2.y) * maxF * slideGrip;
                    float room = braking ? brakeF : Mathf.Sqrt(Mathf.Max(maxF * maxF - f2.y * f2.y, 0.06f * maxF * maxF));
                    if (Throttle != 0f && !braking && Mathf.Abs(f2.x) > room) spinning = Mathf.Clamp01((Mathf.Abs(f2.x) - room) / Mathf.Max(share * 2f, 1f));
                    f2.x = Mathf.Clamp(f2.x, -room, room);
                }
                WheelSlide[i] = vs; WheelRoll[i] = vl; WheelSpin[i] = Mathf.Lerp(WheelSpin[i], spinning, 0.2f);
                // the brakes act part of the way up to the centre of mass, so a hard stop does not stand it on its nose
                Vector3 push = braking ? point + up * (CentreHeight * 0.6f) : point;
                rb.AddForceAtPosition(along * f2.x, push);
                rb.AddForceAtPosition(side * f2.y, point);
                LastLoad[i] = fz; LastPush[i] = f2.x; LastSide[i] = f2.y;
                if (len < minLen) Ride.BottomOuts++;
                spinRate[i] = locked ? 0f : (vl + Mathf.Sign(Throttle) * WheelSpin[i] * 6f) / TyreR;     // a wheel that has broken loose turns faster than the ground passes
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
            {
                float vy = Vector3.Dot(rb.linearVelocity, Vector3.up), a = Mathf.Abs(vy - lastVy) / dt;
                Vector3 w = transform.InverseTransformDirection(rb.angularVelocity);
                Ride.Steps++; Ride.Contact += on / 4f; if (on == 0) Ride.AirSteps++; if (on > 0 && lastOn == 0) Ride.Landings++;
                Ride.VertAccelSq += a * a; Ride.MaxVertAccel = Mathf.Max(Ride.MaxVertAccel, a);
                Ride.MaxPitchRate = Mathf.Max(Ride.MaxPitchRate, Mathf.Abs(w.x)); Ride.MaxRollRate = Mathf.Max(Ride.MaxRollRate, Mathf.Abs(w.z));
                Ride.SpeedSum += Speed;
                if (Afloat) Ride.AfloatSteps++;
                float y = rb.position.y;
                if (!float.IsNaN(Ride.LastY)) { if (y > Ride.LastY) Ride.Climb += y - Ride.LastY; else Ride.Drop += Ride.LastY - y; }
                Ride.LastY = y;
                lastVy = vy; lastOn = on;
            }

            Vector3 v = rb.linearVelocity;
            rb.AddForce(-v * v.magnitude * AirDrag);
            Assist(on, groundUp, fwdSpeed, delta, beta);

            Swim(dt, fwdSpeed);
            KeepAboveGround();
        }

        void OnCollisionEnter(Collision c) { if (c.relativeVelocity.magnitude > 2f) { Ride.BodyHits++; Ride.Count(Ride.Hit, c.collider); } }

        /// <summary>Driving aids: stability control, roll assist, down-force, and levelling the roll in the air.</summary>
        void Assist(int on, Vector3 groundUp, float fwdSpeed, float delta, float beta)
        {
            Rigidbody rb = Body;
            Vector3 up = transform.up, fwd = transform.forward, w = rb.angularVelocity;
            if (on == 0)
            {
                float roll = Vector3.Dot(transform.right, Vector3.up);          // right side up is positive
                rb.AddTorque(-fwd * (roll * AirLevel + Vector3.Dot(w, fwd) * 0.5f), ForceMode.Acceleration);
                if (!Afloat) rb.AddTorque(Vector3.up * Mathf.Clamp(Steer, -1f, 1f) * AirSteer, ForceMode.Acceleration);
                if (!Afloat) rb.AddForce(Physics.gravity * AirGravity, ForceMode.Acceleration);     // comes down sooner
                return;
            }
            float v = rb.linearVelocity.magnitude;
            rb.AddForce(-up * DownForce * v * v);
            groundUp = groundUp.normalized;

            // stability: turn at the rate the front wheels ask for (no more than the tyres can hold), not spin past it
            if (on >= 3 && !Handbraking && Mathf.Abs(fwdSpeed) > 3f)
            {
                float grip = Grip * (SurfGrip[0] + SurfGrip[1] + SurfGrip[2] + SurfGrip[3]) * 0.25f;
                float want = fwdSpeed * Mathf.Tan(delta) / (FrontZ - RearZ), most = grip * 9.81f / Mathf.Abs(fwdSpeed);
                want = Mathf.Clamp(want, -most, most);
                float err = want - Vector3.Dot(w, up);
                rb.AddTorque(up * err * StabilityAssist * 8f * (1f - 0.9f * Drift), ForceMode.Acceleration);
            }

            // in a drift: the steering sets how far sideways it slides, and it is held there rather than left to spin round
            if (on >= 2 && Drift > 0.01f)
            {
                // steering right swings the nose right, so the body slides to the left of where it points (beta < 0).
                // Sliding further right than wanted means the nose is too far left: turn it right.
                float wanted = -Mathf.Clamp(Steer, -1f, 1f) * DriftAngle * Mathf.Deg2Rad;
                float yaw = Mathf.Clamp((beta - wanted) * DriftHold + betaRate * DriftHold * 0.35f, -8f, 8f);
                rb.AddTorque(up * yaw * Drift, ForceMode.Acceleration);
            }

            // with wheels down the body does not pitch or roll faster than the ground under it asks: no bucking over creases
            if (on >= 2)
                rb.AddTorque(-(transform.right * Vector3.Dot(w, transform.right) + fwd * Vector3.Dot(w, fwd)) * Steadiness, ForceMode.Acceleration);

            // roll assist: past a few degrees of lean against the ground, push it back
            if (on >= 2)
            {
                float lean = Vector3.SignedAngle(groundUp, up, fwd) * Mathf.Deg2Rad;       // positive leans left
                float over = Mathf.Max(Mathf.Abs(lean) - 6f * Mathf.Deg2Rad, 0f) * Mathf.Sign(lean);
                rb.AddTorque(-fwd * (over * RollAssist * 10f + Vector3.Dot(w, fwd) * RollAssist * 0.3f), ForceMode.Acceleration);
            }
        }

        /// <summary>Peak grip at about 15 degrees of slip, a little less beyond.</summary>
        float Curve(float slip) { return Mathf.Sin(1.4f * Mathf.Atan(CorneringStiffness * slip)); }

        bool CastWheel(Vector3 from, Vector3 dir, float reach, out Vector3 point, out Vector3 normal, out float dist)
        {
            lastCollider = null;
            // a ball rather than a thin ray, so the wheel rolls over the edges between the ground's flat facets
            RaycastHit h;
            float r = Mathf.Clamp(ContactRadius, 0.02f, TyreR), back = 0.3f;
            if (Physics.SphereCast(from - dir * back, r, dir, out h, reach + back - r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.name == "Ground" && SmoothGround(from, dir, reach, out point, out normal, out dist)) { lastCollider = h.collider; return true; }
                point = h.point; normal = h.normal; dist = h.distance - back + r; lastCollider = h.collider; return true;
            }
            if (Physics.Raycast(from, dir, out h, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                point = h.point; normal = h.normal; dist = h.distance; lastCollider = h.collider; return true;
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

        /// <summary>
        /// The terrain as the tyres feel it: its height averaged over a few metres round the wheel, and the slope of that,
        /// rather than the sharp creases of its mesh.
        /// </summary>
        bool SmoothGround(Vector3 from, Vector3 dir, float reach, out Vector3 point, out Vector3 normal, out float dist)
        {
            point = normal = Vector3.zero; dist = 0f;
            if (GroundFn == null || dir.y > -0.5f || TerrainSmoothing <= 0.01f) return false;
            float rs = TerrainSmoothing + Speed * SmoothingPerSpeed, d = rs * 0.7071f;
            float c, xp, xm, zp, zm, a, b, e, f;
            if (!GroundFn(from, out c) || !GroundFn(from + new Vector3(rs, 0f, 0f), out xp) || !GroundFn(from - new Vector3(rs, 0f, 0f), out xm) ||
                !GroundFn(from + new Vector3(0f, 0f, rs), out zp) || !GroundFn(from - new Vector3(0f, 0f, rs), out zm) ||
                !GroundFn(from + new Vector3(d, 0f, d), out a) || !GroundFn(from + new Vector3(-d, 0f, d), out b) ||
                !GroundFn(from + new Vector3(d, 0f, -d), out e) || !GroundFn(from + new Vector3(-d, 0f, -d), out f)) return false;
            float height = (c * 2f + xp + xm + zp + zm + a + b + e + f) / 10f;
            normal = new Vector3(-(xp - xm) / (2f * rs), 1f, -(zp - zm) / (2f * rs)).normalized;
            Vector3 onGround = new Vector3(from.x, height, from.z);
            float along = Vector3.Dot(dir, normal);
            if (along > -0.2f) return false;
            dist = Vector3.Dot(onGround - from, normal) / along;
            if (dist < -0.2f || dist > reach) return false;
            point = from + dir * dist;
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
                float depth = Mathf.Clamp01((s - p.y) / (0.6f * Size));
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
            rb.AddForceAtPosition(fwd * Throttle * (Throttle < 0f ? 0.5f : 1f) * WaterThrust * rb.mass, transform.TransformPoint(new Vector3(0f, -0.2f, -1.4f) * Size));
            rb.AddTorque(Vector3.up * Steer * WaterTurn * Mathf.Clamp(fwdSpeed / 3f, -1f, 1f), ForceMode.Acceleration);
            rb.AddForce(-right * Vector3.Dot(rb.linearVelocity, right) * rb.mass * 0.6f);        // the hull resists going sideways
        }

        /// <summary>Where the ground has no collider yet, never let the body sink into it.</summary>
        void KeepAboveGround()
        {
            float gy;
            if (GroundFn == null || !GroundFn(Body.position, out gy)) return;
            if (Physics.Raycast(Body.position + Vector3.up * 2f, Vector3.down, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            float low = gy - (Ground + 0.3f * Size);       // centre no lower than this
            if (Body.position.y >= low) return;
            Body.position = new Vector3(Body.position.x, low, Body.position.z);
            Vector3 v = Body.linearVelocity;
            if (v.y < 0f) Body.linearVelocity = new Vector3(v.x, 0f, v.z);
        }

        /// <summary>The wheels where the suspension has put them, turned and spinning.</summary>
        void LateUpdate()
        {
            if (spin == null) return;
            float dt = Time.deltaTime, maxLen = StaticLen + Droop, minLen = StaticLen - Bump;
            for (int i = 0; i < 4; i++)
            {
                // the wheel is drawn no further up than its mudguard allows, though the spring can go further
                float want = Mathf.Clamp(contact[i] ? length[i] : maxLen, Mathf.Max(minLen, StaticLen - 0.3f * Size), maxLen);
                shown[i] = contact[i] ? want : Mathf.MoveTowards(shown[i], want, 1.5f * dt);
                pivot[i].localPosition = new Vector3(WX[i], AttachY - shown[i], WZ[i]);
                pivot[i].localRotation = Quaternion.Euler(0f, angle[i], 0f);
                spinAt[i] = Mathf.Repeat(spinAt[i] + spinRate[i] * Mathf.Rad2Deg * dt, 360f);
                spin[i].localRotation = Quaternion.Euler(spinAt[i], 0f, 0f);
            }
            LeanBody(dt);
        }

        /// <summary>
        /// The body rocks on its springs more than the physics alone would show: out of corners, back under power, onto
        /// its nose under the brakes, down on a landing, with a little wobble after. Looks only; the wheels stay put.
        /// </summary>
        void LeanBody(float dt)
        {
            if (bodyT == null) return;
            bool live = Body != null && !Body.isKinematic && Grounded > 0 && BodyLean > 0f;
            Vector3 want = Vector3.zero;
            if (live)
            {
                want.x = Mathf.Clamp(-accel.z * BodyLean, -5f, 5f);                 // positive pitches the nose down
                want.z = Mathf.Clamp(accel.x * BodyLean * 1.2f, -7f, 7f);           // positive leans to the left
                want.y = Mathf.Clamp(-accel.y * 0.004f, -0.07f, 0.05f) * Size;
            }
            float w = Mathf.Max(BodySpring, 1f);
            for (float left = Mathf.Min(dt, 0.1f); left > 1e-5f; left -= 0.01f)   // small steps keep the spring steady at any frame rate
            {
                float h = Mathf.Min(left, 0.01f);
                leanRate += ((want - leanAt) * w * w - leanRate * 2f * BodyDamping * w) * h;
                leanAt += leanRate * h;
            }
            Quaternion r = Quaternion.Euler(leanAt.x, 0f, leanAt.z);
            Vector3 pivot = new Vector3(0f, AxleY, 0f);                             // it rocks about the axles, not its middle
            bodyT.localRotation = r;
            bodyT.localPosition = pivot - r * pivot + new Vector3(0f, leanAt.y, 0f);
        }

        // ------------------------------------------------------------ palette

        static Texture2D PaletteTexture()
        {
            var t = new Texture2D(Palette.Length, Variants, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "JeepPalette" };
            var px = new Color32[Palette.Length * Variants];
            for (int v = 0; v < Variants; v++)
                for (int c = 0; c < Palette.Length; c++)
                {
                    px[v * Palette.Length + c] = Palette[c];
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // ------------------------------------------------------------ body

        // Everything is made of soft shapes with smooth shading: boxes with rounded edges, bars swept along curves,
        // shapes turned on a lathe. One clean colour per part.
        static Mesh BodyMesh()
        {
            var k = new Kit { Scale = Size };
            const float top = 0.45f;   // the tub's top edge

            // chassis: frame rails, axles, differentials, dampers, side steps
            for (int s = -1; s <= 1; s += 2)
            {
                k.Round(new Vector3(s * 0.40f, -0.24f, 0.02f), new Vector3(0.12f, 0.14f, 3.0f), 0.04f, DarkSteel, 2);
                k.Round(new Vector3(s * 0.74f, -0.21f, 0.03f), new Vector3(0.20f, 0.07f, 0.92f), 0.03f, DarkSteel, 2);
            }
            foreach (float z in new[] { DFrontZ, DRearZ })
            {
                k.Tube(new Vector3(-DTrack + 0.12f, DAxleY, z), new Vector3(DTrack - 0.12f, DAxleY, z), 0.055f, 12, DarkSteel);
                k.Ball(new Vector3(0.08f, DAxleY, z), new Vector3(0.15f, 0.13f, 0.14f), DarkSteel);
                for (int s = -1; s <= 1; s += 2)
                    k.Tube(new Vector3(s * 0.45f, DAxleY, z), new Vector3(s * 0.43f, -0.22f, z), 0.045f, 10, Steel);
            }

            // the tub: an open cockpit with thick rounded walls, low at the doorways
            k.Round(new Vector3(0f, -0.10f, -0.5f), new Vector3(1.22f, 0.08f, 2.0f), 0.03f, Floor, 2);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Round(new Vector3(s * 0.60f, 0.15f, -1.02f), new Vector3(0.15f, 0.60f, 1.08f), 0.065f, Paint);
                k.Round(new Vector3(s * 0.60f, 0.02f, 0.0f), new Vector3(0.15f, 0.36f, 1.1f), 0.065f, Paint);
            }
            k.Round(new Vector3(0f, 0.15f, -1.50f), new Vector3(1.35f, 0.60f, 0.15f), 0.065f, Paint);
            k.Round(new Vector3(0f, 0.17f, 0.50f), new Vector3(1.35f, 0.62f, 0.17f), 0.07f, Paint);       // the cowl under the windscreen

            // bonnet: one big soft block, narrower and lower towards the nose
            k.Round(new Vector3(0f, 0.17f, 1.03f), new Vector3(1.27f, 0.60f, 1.04f), 0.16f, Paint, 5, 0.10f, 0.10f);
            for (int s = -1; s <= 1; s += 2)
                k.Round(new Vector3(s * 0.25f, 0.455f, 0.82f), new Vector3(0.20f, 0.03f, 0.34f), 0.014f, Flare, 2);   // air vents

            // nose: a dark panel with three bars, round lamps in bright rings, indicators beneath
            k.Round(new Vector3(0f, 0.15f, 1.535f), new Vector3(0.56f, 0.30f, 0.07f), 0.035f, Grille, 2);
            for (int i = 0; i < 3; i++)
                k.Round(new Vector3(0f, 0.07f + i * 0.08f, 1.572f), new Vector3(0.46f, 0.036f, 0.03f), 0.014f, Steel, 2);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 lamp = new Vector3(s * 0.415f, 0.2f, 1.5f);
                k.Lathe(new[] { new Vector2(0.0f, 0.118f), new Vector2(0.06f, 0.118f), new Vector2(0.075f, 0.105f), new Vector2(0.075f, 0.088f) }, 20, Steel, lamp, Vector3.forward);
                k.Ball(lamp + new Vector3(0f, 0f, 0.068f), new Vector3(0.092f, 0.092f, 0.04f), Lamp);
                k.Round(new Vector3(s * 0.415f, 0.03f, 1.545f), new Vector3(0.13f, 0.05f, 0.05f), 0.022f, Amber, 2);
            }

            // bumpers and tow hooks
            k.Round(new Vector3(0f, -0.20f, 1.66f), new Vector3(1.82f, 0.17f, 0.18f), 0.075f, DarkSteel);
            k.Round(new Vector3(0f, -0.20f, -1.61f), new Vector3(1.58f, 0.17f, 0.16f), 0.07f, DarkSteel);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Round(new Vector3(s * 0.40f, -0.2f, 1.56f), new Vector3(0.1f, 0.1f, 0.14f), 0.03f, DarkSteel, 2);
                k.Round(new Vector3(s * 0.30f, -0.2f, 1.76f), new Vector3(0.065f, 0.095f, 0.07f), 0.028f, Tail, 2);
            }

            // rear: tail lights and the spare wheel's mount
            for (int s = -1; s <= 1; s += 2)
            {
                k.Round(new Vector3(s * 0.50f, 0.27f, -1.575f), new Vector3(0.13f, 0.15f, 0.04f), 0.03f, Tail, 2);
                k.Round(new Vector3(s * 0.50f, 0.14f, -1.575f), new Vector3(0.13f, 0.06f, 0.04f), 0.02f, Amber, 2);
            }
            k.Tube(new Vector3(0f, 0.16f, -1.5f), new Vector3(0f, 0.16f, -1.62f), 0.075f, 12, DarkSteel);

            // wide mudguards curving over each wheel
            Kit.P[] guard = Kit.RoundRect(0.40f, 0.06f, 0.028f, 3);
            foreach (float z in new[] { DFrontZ, DRearZ })
                for (int s = -1; s <= 1; s += 2)
                {
                    var path = new List<Vector3>();
                    for (float a = 14f; a <= 166.01f; a += 9.5f)
                        path.Add(new Vector3(s * 0.79f, DAxleY + Mathf.Sin(a * Mathf.Deg2Rad) * 0.55f, z + Mathf.Cos(a * Mathf.Deg2Rad) * 0.55f));
                    k.Sweep(path, Vector3.right, guard, Flare, false);
                }

            // windscreen: a frame of round bar with soft corners, leaning back a little, and mirrors on stalks
            k.Push(new Vector3(0f, top, 0.5f), Quaternion.Euler(-14f, 0f, 0f));
            k.Sweep(Kit.Bend(new[] { new Vector3(-0.6f, 0.04f, 0f), new Vector3(-0.6f, 0.56f, 0f), new Vector3(0.6f, 0.56f, 0f), new Vector3(0.6f, 0.04f, 0f) }, 0.1f, true), Vector3.forward, Kit.Circle(0.036f, 10), Paint, true);
            k.Round(new Vector3(0f, 0.30f, 0f), new Vector3(1.17f, 0.50f, 0.018f), 0.008f, Glass, 1);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Tube(new Vector3(s * 0.62f, 0.22f, 0.02f), new Vector3(s * 0.77f, 0.27f, 0.03f), 0.016f, 8, Tube);
                k.Ball(new Vector3(s * 0.80f, 0.29f, 0.03f), new Vector3(0.04f, 0.075f, 0.1f), Tube);
            }
            k.Pop();

            // dash and steering wheel (driver on the left)
            k.Round(new Vector3(0f, 0.34f, 0.39f), new Vector3(1.12f, 0.17f, 0.15f), 0.06f, Tube);
            Vector3 col0 = new Vector3(-0.3f, 0.28f, 0.42f), col1 = new Vector3(-0.3f, 0.45f, 0.2f);
            k.Tube(col0, col1, 0.024f, 8, Tube);
            k.Push(col1, Quaternion.LookRotation(col1 - col0));
            var rim = new List<Vector3>();
            for (int i = 0; i < 20; i++) { float a = i * Mathf.PI * 2f / 20f; rim.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.155f); }
            k.Sweep(rim, Vector3.forward, Kit.Circle(0.022f, 8), Tube, true);
            k.Tube(new Vector3(-0.15f, 0f, 0f), new Vector3(0.15f, 0f, 0f), 0.016f, 8, Tube);
            k.Ball(Vector3.zero, new Vector3(0.05f, 0.05f, 0.03f), Paint);
            k.Pop();

            // seats: two buckets in front, a bench behind
            for (int s = -1; s <= 1; s += 2)
                Chair(k, new Vector3(s * 0.3f, -0.06f, -0.16f), 0.44f, true);
            Chair(k, new Vector3(0f, -0.06f, -1.08f), 1.04f, false);

            // roll bar: a hoop with soft corners behind the front seats, stays back to the tail, bars forward to the windscreen
            const float r = 0.04f;
            float hz = -0.5f, hy = 1.14f, hx = 0.55f;
            Vector3 wsTop = new Vector3(0f, top + 0.56f * Mathf.Cos(14f * Mathf.Deg2Rad), 0.5f - 0.56f * Mathf.Sin(14f * Mathf.Deg2Rad));
            Kit.P[] bar = Kit.Circle(r, 12);
            k.Sweep(Kit.Bend(new[] { new Vector3(-hx, top - 0.05f, hz), new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz), new Vector3(hx, top - 0.05f, hz) }, 0.16f, false), Vector3.forward, bar, Tube, false);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Sweep(Kit.Bend(new[] { new Vector3(s * hx, top - 0.05f, -1.42f), new Vector3(s * hx, 0.62f, -1.36f), new Vector3(s * hx, hy - 0.1f, hz) }, 0.12f, false), Vector3.right, bar, Tube, false);
                k.Tube(new Vector3(s * hx, hy - 0.035f, hz), new Vector3(s * 0.585f, wsTop.y - 0.01f, wsTop.z), r * 0.85f, 10, Tube);
            }
            k.Tube(new Vector3(-hx, 0.80f, hz), new Vector3(hx, 0.80f, hz), r * 0.8f, 10, Tube);

            // snorkel: out of the right side of the bonnet, up the windscreen pillar, a forward-facing head on top
            k.Sweep(Kit.Bend(new[] { new Vector3(0.55f, 0.27f, 0.80f), new Vector3(0.70f, 0.27f, 0.64f), new Vector3(0.70f, 1.0f, 0.455f) }, 0.07f, false), new Vector3(0.7071f, 0f, 0.7071f), Kit.Circle(0.05f, 10), Tube, false);
            k.Push(new Vector3(0.70f, 1.0f, 0.455f), Quaternion.Euler(-14f, 0f, 0f));
            k.Round(new Vector3(0f, 0.04f, 0.05f), new Vector3(0.13f, 0.13f, 0.24f), 0.05f, Tube, 2);
            k.Round(new Vector3(0f, 0.04f, 0.165f), new Vector3(0.085f, 0.085f, 0.02f), 0.01f, Grille, 1);
            k.Pop();

            return k.Finish("Jeep body");
        }

        static void Chair(Kit k, Vector3 at, float width, bool headrest)
        {
            k.Round(at + new Vector3(0f, 0.11f, 0.02f), new Vector3(width, 0.15f, 0.46f), 0.065f, Seat);
            k.Push(at + new Vector3(0f, 0.15f, -0.19f), Quaternion.Euler(-12f, 0f, 0f));
            k.Round(new Vector3(0f, 0.26f, 0f), new Vector3(width, 0.54f, 0.14f), 0.065f, Seat);
            if (headrest) k.Round(new Vector3(0f, 0.60f, 0.0f), new Vector3(0.26f, 0.15f, 0.12f), 0.055f, Seat, 2);
            k.Pop();
        }

        // ------------------------------------------------------------ wheel

        /// <summary>One wheel round the origin, axle along x, the rim on the -x side (mirrored for the right-hand wheels).</summary>
        static Mesh WheelMesh()
        {
            var k = new Kit { Scale = Size };
            float hw = DTyreW * 0.5f;

            // the tyre: a fat rounded ring
            const float outer = 0.405f, inner = 0.23f;
            float mid = (outer + inner) * 0.5f, half = (outer - inner) * 0.5f;
            var section = new Vector2[25];
            for (int i = 0; i <= 24; i++)
            {
                float a = Mathf.PI * 0.5f - i * Mathf.PI * 2f / 24f;       // from the top, towards +x, round and back
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                section[i] = new Vector2(hw * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.55f), mid + half * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.55f));
            }
            k.Lathe(section, 32, Rubber, Vector3.zero, Vector3.right);

            // tread: two staggered rows of chunky rounded blocks
            const int lugs = 13;
            for (int i = 0; i < lugs; i++)
                for (int row = 0; row < 2; row++)
                {
                    float a = (i + row * 0.5f) * 360f / lugs;
                    float side = row == 0 ? -1f : 1f;
                    k.Push(Vector3.zero, Quaternion.Euler(a, 0f, 0f));
                    k.Round(new Vector3(side * 0.082f, outer - 0.012f, 0f), new Vector3(0.15f, 0.075f, 0.105f), 0.026f, Tread, 2);
                    k.Pop();
                }

            // the rim: a soft dish with a painted cap
            float f = -hw + 0.03f;
            k.Lathe(new[] { new Vector2(f - 0.035f, 0f), new Vector2(f - 0.035f, 0.07f), new Vector2(f - 0.012f, 0.10f), new Vector2(f + 0.02f, 0.125f), new Vector2(f + 0.02f, 0.185f), new Vector2(f - 0.02f, 0.225f), new Vector2(f - 0.02f, 0.245f), new Vector2(f + 0.03f, 0.25f) }, 28, Rim, Vector3.zero, Vector3.right);
            k.Ball(new Vector3(f - 0.035f, 0f, 0f), new Vector3(0.03f, 0.06f, 0.06f), Paint);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                k.Ball(new Vector3(f + 0.015f, Mathf.Cos(a) * 0.155f, Mathf.Sin(a) * 0.155f), new Vector3(0.018f, 0.022f, 0.022f), Steel);
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
            const float Patch = 100f;

            public void Push(Vector3 pos, Quaternion rot) { stack.Push(m); m = m * Matrix4x4.TRS(pos, rot, Vector3.one); }
            public void Pop() { m = stack.Pop(); }

            static Vector2 Cell(int col, Vector3 p) { return new Vector2((col + 0.5f) / Palette.Length, 0.5f); }

            /// <summary>A point of a cross-section and the way its surface faces.</summary>
            public struct P { public Vector2 At, Out; public P(Vector2 at, Vector2 o) { At = at; Out = o; } }

            public static P[] Circle(float radius, int sides)
            {
                var p = new P[sides];
                for (int i = 0; i < sides; i++) { float a = i * Mathf.PI * 2f / sides; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p[i] = new P(d * radius, d); }
                return p;
            }

            /// <summary>A rectangle w wide and h high with rounded corners.</summary>
            public static P[] RoundRect(float w, float h, float r, int perCorner)
            {
                var p = new List<P>();
                r = Mathf.Min(r, Mathf.Min(w, h) * 0.499f);
                for (int c = 0; c < 4; c++)
                {
                    var centre = new Vector2((c == 0 || c == 3 ? 1f : -1f) * (w * 0.5f - r), (c < 2 ? 1f : -1f) * (h * 0.5f - r));
                    for (int i = 0; i <= perCorner; i++)
                    {
                        float a = (c + (float)i / perCorner) * Mathf.PI * 0.5f;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        p.Add(new P(centre + d * r, d));
                    }
                }
                return p.ToArray();
            }

            /// <summary>A line through the points with its corners rounded off.</summary>
            public static List<Vector3> Bend(Vector3[] pts, float radius, bool closed)
            {
                var o = new List<Vector3>();
                int n = pts.Length;
                for (int i = 0; i < n; i++)
                {
                    if (!closed && (i == 0 || i == n - 1)) { o.Add(pts[i]); continue; }
                    Vector3 a = pts[(i + n - 1) % n], b = pts[i], c = pts[(i + 1) % n];
                    float r = Mathf.Min(radius, Mathf.Min((a - b).magnitude, (c - b).magnitude) * 0.45f);
                    Vector3 p0 = b + (a - b).normalized * r, p1 = b + (c - b).normalized * r;
                    for (int j = 0; j <= 6; j++)
                    {
                        float t = j / 6f;
                        o.Add(Vector3.Lerp(Vector3.Lerp(p0, b, t), Vector3.Lerp(b, p1, t), t));
                    }
                }
                return o;
            }

            /// <summary>Rows of points joined into a smooth skin (each row the same length).</summary>
            void Skin(Vector3[][] pos, Vector3[][] nrm, int col, bool wrap)
            {
                int rows = pos.Length, cols = pos[0].Length, k0 = v.Count;
                Vector2 cell = Cell(col, Vector3.zero);
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < cols; j++) { v.Add(m.MultiplyPoint3x4(pos[i][j])); n.Add(m.MultiplyVector(nrm[i][j]).normalized); uv.Add(cell); }
                int jn = wrap ? cols : cols - 1;
                for (int i = 0; i + 1 < rows; i++)
                    for (int j = 0; j < jn; j++)
                    {
                        int a = k0 + i * cols + j, b = k0 + i * cols + (j + 1) % cols, c = k0 + (i + 1) * cols + (j + 1) % cols, d = k0 + (i + 1) * cols + j;
                        Vector3 face = Vector3.Cross(v[b] - v[a], v[c] - v[a]) + Vector3.Cross(v[c] - v[a], v[d] - v[a]);
                        if (Vector3.Dot(face, n[a] + n[b] + n[c] + n[d]) >= 0f) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
                        else { t.Add(a); t.Add(c); t.Add(b); t.Add(a); t.Add(d); t.Add(c); }
                    }
            }

            /// <summary>
            /// A box with rounded edges and corners, smoothly shaded. taperX / taperY make it narrower / lower towards
            /// its front (+z) end.
            /// </summary>
            public void Round(Vector3 c, Vector3 size, float r, int col, int steps = 3, float taperX = 0f, float taperY = 0f)
            {
                Vector3 h = size * 0.5f;
                r = Mathf.Min(r, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.999f);
                Vector3 core = h - Vector3.one * r;
                for (int axis = 0; axis < 3; axis++)
                {
                    int ua = (axis + 1) % 3, va = (axis + 2) % 3;
                    float[] us = Steps(h[ua], r, steps), vs = Steps(h[va], r, steps);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        var pos = new Vector3[us.Length][]; var nrm = new Vector3[us.Length][];
                        for (int i = 0; i < us.Length; i++)
                        {
                            pos[i] = new Vector3[vs.Length]; nrm[i] = new Vector3[vs.Length];
                            for (int j = 0; j < vs.Length; j++)
                            {
                                Vector3 q = Vector3.zero; q[axis] = sign * h[axis]; q[ua] = us[i]; q[va] = vs[j];
                                Vector3 inside = new Vector3(Mathf.Clamp(q.x, -core.x, core.x), Mathf.Clamp(q.y, -core.y, core.y), Mathf.Clamp(q.z, -core.z, core.z));
                                Vector3 d = (q - inside).normalized;
                                Vector3 p = inside + d * r;
                                float f = h.z > 1e-5f ? p.z / h.z * 0.5f + 0.5f : 0f;
                                p.x *= 1f - taperX * f;
                                p.y = -h.y + (p.y + h.y) * (1f - taperY * f);
                                pos[i][j] = c + p; nrm[i][j] = d;
                            }
                        }
                        Skin(pos, nrm, col, false);
                    }
                }
            }

            // where to cut a side of a rounded box: evenly round each rounded edge, once across the flat between
            static float[] Steps(float h, float r, int k)
            {
                var a = new float[2 * (k + 1)];
                for (int i = 0; i <= k; i++)
                {
                    float t = Mathf.Tan(Mathf.PI * 0.25f * (k - i) / k);
                    a[i] = -(h - r) - r * t;
                    a[2 * k + 1 - i] = (h - r) + r * t;
                }
                return a;
            }

            /// <summary>A cross-section carried along a path. 'side' is the way the section's x points (kept square to the path).</summary>
            public void Sweep(List<Vector3> path, Vector3 side, P[] section, int col, bool closed)
            {
                int np = path.Count, rows = closed ? np + 1 : np;
                var pos = new Vector3[rows][]; var nrm = new Vector3[rows][];
                for (int i = 0; i < rows; i++)
                {
                    int at = i % np;
                    Vector3 prev = path[closed ? (at + np - 1) % np : Mathf.Max(at - 1, 0)], next = path[closed ? (at + 1) % np : Mathf.Min(at + 1, np - 1)];
                    Vector3 tan = (next - prev).normalized;
                    Vector3 sx = (side - tan * Vector3.Dot(side, tan)).normalized, sy = Vector3.Cross(tan, sx);
                    pos[i] = new Vector3[section.Length]; nrm[i] = new Vector3[section.Length];
                    for (int j = 0; j < section.Length; j++)
                    {
                        pos[i][j] = path[at] + sx * section[j].At.x + sy * section[j].At.y;
                        nrm[i][j] = (sx * section[j].Out.x + sy * section[j].Out.y).normalized;
                    }
                }
                Skin(pos, nrm, col, true);
                if (closed) return;
                for (int end = 0; end < 2; end++)       // flat ends
                {
                    int i = end == 0 ? 0 : rows - 1;
                    Vector3 centre = path[i], outward = (end == 0 ? path[0] - path[1] : path[np - 1] - path[np - 2]).normalized;
                    Fan(centre, pos[i], outward, col);
                }
            }

            void Fan(Vector3 centre, Vector3[] ring, Vector3 normal, int col)
            {
                Vector3 wc = m.MultiplyPoint3x4(centre), wn = m.MultiplyVector(normal).normalized;
                Vector2 cell = Cell(col, wc);
                int k0 = v.Count;
                v.Add(wc); n.Add(wn); uv.Add(cell);
                for (int i = 0; i < ring.Length; i++) { v.Add(m.MultiplyPoint3x4(ring[i])); n.Add(wn); uv.Add(cell); }
                for (int i = 0; i < ring.Length; i++)
                {
                    int i0 = k0 + 1 + i, i1 = k0 + 1 + (i + 1) % ring.Length;
                    if (Vector3.Dot(Vector3.Cross(v[i0] - wc, v[i1] - wc), wn) >= 0f) { t.Add(k0); t.Add(i0); t.Add(i1); }
                    else { t.Add(k0); t.Add(i1); t.Add(i0); }
                }
            }

            /// <summary>
            /// An outline turned round an axis. Each point is (distance along the axis, radius); the surface faces to
            /// the left of the way the outline is drawn, and is shaded smoothly along it.
            /// </summary>
            public void Lathe(Vector2[] outline, int sides, int col, Vector3 origin, Vector3 axis)
            {
                axis.Normalize();
                Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, e2 = Vector3.Cross(axis, e1);
                int np = outline.Length;
                bool loop = (outline[0] - outline[np - 1]).sqrMagnitude < 1e-10f;
                var pos = new Vector3[np][]; var nrm = new Vector3[np][];
                for (int i = 0; i < np; i++)
                {
                    Vector2 prev = outline[loop ? (i + np - 2) % (np - 1) : Mathf.Max(i - 1, 0)], next = outline[loop ? (i + 1) % (np - 1) : Mathf.Min(i + 1, np - 1)];
                    Vector2 tan = (next - prev).normalized, o = new Vector2(-tan.y, tan.x);
                    pos[i] = new Vector3[sides]; nrm[i] = new Vector3[sides];
                    for (int j = 0; j < sides; j++)
                    {
                        float a = j * Mathf.PI * 2f / sides;
                        Vector3 radial = e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a);
                        pos[i][j] = origin + axis * outline[i].x + radial * outline[i].y;
                        nrm[i][j] = (axis * o.x + radial * o.y).normalized;
                    }
                }
                Skin(pos, nrm, col, true);
            }

            /// <summary>A ball, or an egg with different radii along x, y and z.</summary>
            public void Ball(Vector3 c, Vector3 radii, int col, int rings = 8, int sides = 14)
            {
                var pos = new Vector3[rings + 1][]; var nrm = new Vector3[rings + 1][];
                for (int i = 0; i <= rings; i++)
                {
                    float lat = Mathf.PI * i / rings;
                    pos[i] = new Vector3[sides]; nrm[i] = new Vector3[sides];
                    for (int j = 0; j < sides; j++)
                    {
                        float lon = j * Mathf.PI * 2f / sides;
                        Vector3 d = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                        pos[i][j] = c + Vector3.Scale(d, radii);
                        nrm[i][j] = new Vector3(d.x / radii.x, d.y / radii.y, d.z / radii.z).normalized;
                    }
                }
                Skin(pos, nrm, col, true);
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

            /// <summary>The finished mesh is this many times the size it was drawn at.</summary>
            public float Scale = 1f;

            public Mesh Finish(string name)
            {
                if (Scale != 1f) for (int i = 0; i < v.Count; i++) v[i] *= Scale;
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
