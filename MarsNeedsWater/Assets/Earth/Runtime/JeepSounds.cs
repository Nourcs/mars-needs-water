using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The sound of the jeep: an engine that follows the revs (through gears it changes by itself), tyres rolling and
    /// sliding on whatever each wheel stands on, brakes, landings and the wind at speed. It only listens to the jeep
    /// (Jeep.cs is not touched): its speed, pedals, Drift, and what each wheel is doing.
    ///
    /// Recordings are read from Resources/Earth/Audio/Jeep:
    ///   engine_N       loops of a steady engine; N is how many firings a second that recording has (its pitch),
    ///                  so the nearest ones are blended and bent to the revs wanted
    ///   roll_KIND      tyres rolling: road, gravel, sand, grass, snow, mud (a missing kind borrows a near one)
    ///   skid_KIND(_n)  tyres sliding or spinning; with several, each slide picks another
    ///   brake          brakes rubbing, a loop;  brake_stop(_n)  the squeak as it comes to rest
    ///   land(_n)       a landing;  start(_n)  the engine starting;  rush  wind at speed
    /// Anything missing is simply silent.
    /// </summary>
    public sealed class JeepSounds : MonoBehaviour
    {
        public static JeepSounds I;
        const string Folder = "Earth/Audio/Jeep";

        [Header("Mix")]
        [Range(0f, 1f)] public float Volume = 1f;
        [Range(0f, 1f)] public float EngineVolume = 0.5f;
        [Range(0f, 1f)] public float TyreVolume = 0.6f;
        [Range(0f, 1f)] public float SkidVolume = 0.7f;
        [Range(0f, 1f)] public float BrakeVolume = 0.4f;

        [Header("Engine")]
        [Tooltip("Firings a second at idle and flat out (a four-cylinder: revs / 30).")] public float IdleHz = 27f;
        public float RedlineHz = 175f;
        [Tooltip("Road speed (m/s) at which each gear runs out of revs.")] public float[] GearTop = { 8f, 15f, 23f, 32f, 47f, 76f };
        [Tooltip("Change up above this share of the revs, down below the other.")] [Range(0.5f, 1f)] public float ShiftUp = 0.86f;
        [Range(0.1f, 0.6f)] public float ShiftDown = 0.42f;

        /// <summary>How loud the engine is right now, 0 to 1: other sounds can make room for it.</summary>
        public float Loudness { get; private set; }
        /// <summary>Revs 0 (idle) to 1 (flat out), and the gear, for the HUD or tests.</summary>
        public float Revs { get; private set; }
        public int Gear { get; private set; }

        enum Kind { Road, Gravel, Sand, Grass, Snow, Mud, Count }
        static readonly string[] KindName = { "road", "gravel", "sand", "grass", "snow", "mud" };
        // what a kind borrows when it has no recording of its own
        static readonly Kind[][] Borrow =
        {
            new Kind[0],
            new Kind[0],
            new[] { Kind.Gravel },
            new[] { Kind.Gravel },
            new[] { Kind.Sand, Kind.Gravel },
            new[] { Kind.Sand, Kind.Gravel },
        };

        sealed class Layer { public AudioSource Src; public AudioLowPassFilter Filter; public float Hz; }

        SoundSets sets;
        EarthWorld world;
        EarthPlayer player;
        Jeep jeep;
        Layer[] engine = new Layer[0];
        readonly AudioSource[] roll = new AudioSource[(int)Kind.Count], skid = new AudioSource[(int)Kind.Count];
        readonly string[] skidSet = new string[(int)Kind.Count];
        readonly float[] rollWant = new float[(int)Kind.Count], skidWant = new float[(int)Kind.Count];
        readonly Kind[] under = new Kind[4];
        AudioSource brake, rush;
        AudioSource[] voices;
        int nextVoice, probeWheel;
        float probeIn, on, load, shiftIn, air, lastVy, brakePeak, wobble;
        bool wasDriving, wasBraking;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("Sounds");
            go.AddComponent<JeepSounds>();
            go.AddComponent<AmbientSounds>();
        }

        void Awake()
        {
            I = this;
            sets = new SoundSets(Folder, 31);

            var loops = sets.All("engine");
            if (loops != null)
            {
                engine = new Layer[loops.Length];
                for (int i = 0; i < loops.Length; i++)
                {
                    var s = sets.Loop(transform, "Engine " + loops[i].Number, loops[i].Audio);
                    engine[i] = new Layer { Src = s, Filter = s.gameObject.AddComponent<AudioLowPassFilter>(), Hz = Mathf.Max(1, loops[i].Number) };
                }
            }

            for (int k = 0; k < (int)Kind.Count; k++)
            {
                string r = Find("roll_", (Kind)k), sk = Find("skid_", (Kind)k);
                if (r != null) roll[k] = Shared(roll, r, k) ?? sets.Loop(transform, r, sets.Pick(r));
                if (sk != null) { skid[k] = Shared(skid, sk, k) ?? sets.Loop(transform, sk, sets.Pick(sk)); skidSet[k] = sk; }
            }
            if (sets.Has("brake")) brake = sets.Loop(transform, "Brakes", sets.Pick("brake"));
            if (sets.Has("rush")) rush = sets.Loop(transform, "Wind rush", sets.Pick("rush"));

            voices = new AudioSource[4];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false; voices[i].spatialBlend = 0f;
            }
        }

        void OnDestroy() { if (I == this) I = null; }

        /// <summary>The set a kind of ground uses: its own, or the first it can borrow.</summary>
        string Find(string prefix, Kind k)
        {
            if (sets.Has(prefix + KindName[(int)k])) return prefix + KindName[(int)k];
            foreach (Kind b in Borrow[(int)k]) if (sets.Has(prefix + KindName[(int)b])) return prefix + KindName[(int)b];
            return null;
        }

        /// <summary>Two kinds that use the same recording share one source.</summary>
        static AudioSource Shared(AudioSource[] made, string name, int upTo)
        {
            for (int i = 0; i < upTo; i++) if (made[i] != null && made[i].name == name) return made[i];
            return null;
        }

        void Shot(string key, float volume, float pitchSpread)
        {
            AudioClip c = sets.Pick(key);
            if (c == null) return;
            AudioSource v = voices[nextVoice]; nextVoice = (nextVoice + 1) % voices.Length;
            v.Stop();
            v.clip = c;
            v.pitch = 1f + (sets.Random01() * 2f - 1f) * pitchSpread;
            v.volume = Mathf.Clamp01(volume * Volume);
            v.Play();
        }

        static Kind KindOf(byte ground)
        {
            switch (ground)
            {
                case GroundKind.Paved: case GroundKind.Rock: return Kind.Road;
                case GroundKind.Sand: return Kind.Sand;
                case GroundKind.Snow: return Kind.Snow;
                case GroundKind.Mud: return Kind.Mud;
                case GroundKind.Grass: case GroundKind.DryGrass: return Kind.Grass;
                default: return Kind.Gravel;         // bare earth, farmland, forest floor
            }
        }

        void Update()
        {
            if (world == null) world = EarthWorld.I;
            if (player == null) player = FindAnyObjectByType<EarthPlayer>();
            if (player != null && jeep == null) jeep = player.Jeep;
            if (jeep == null || jeep.Body == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bool driving = player.Current == EarthPlayer.Mode.Drive;
            bool live = driving && !jeep.Body.isKinematic;
            if (driving && !wasDriving) { Shot("start", 0.6f, 0.03f); Gear = 0; Revs = 0f; }
            wasDriving = driving;
            on = Mathf.MoveTowards(on, driving ? 1f : 0f, dt * (driving ? 1.6f : 3f));

            Vector3 vel = live ? jeep.Body.linearVelocity : Vector3.zero;
            float fwd = Vector3.Dot(vel, jeep.transform.forward);
            float speed = new Vector2(vel.x, vel.z).magnitude;
            float throttle = live ? jeep.Throttle : 0f;
            bool braking = (throttle < 0f && fwd > 0.5f) || (throttle > 0f && fwd < -0.5f);
            int down = 0; float spin = 0f;
            for (int i = 0; i < 4; i++) if (live && jeep.WheelDown(i)) { down++; spin += jeep.WheelSpin[i]; }
            spin = down > 0 ? spin / down : 0f;

            Engine(dt, live, fwd, throttle, braking, down, spin);
            Tyres(dt, live, speed, down);
            Brakes(dt, live, speed, braking, down);

            // landings
            if (live && down == 0 && !jeep.Afloat) { air += dt; lastVy = vel.y; }
            else
            {
                if (live && air > 0.3f && !jeep.Afloat) Shot("land", Mathf.Clamp(0.25f + Mathf.Max(0f, -lastVy) * 0.07f, 0.25f, 1f), 0.08f);
                air = 0f;
            }

            if (rush != null)
            {
                float r = Mathf.Clamp01((speed - 12f) / 45f);
                rush.volume = Mathf.Lerp(rush.volume, r * Mathf.Sqrt(r) * 0.5f * on * Volume, dt * 4f);
                rush.pitch = 0.85f + r * 0.35f;
            }
        }

        // ------------------------------------------------------------ engine

        void Engine(float dt, bool live, float fwd, float throttle, bool braking, int down, float spin)
        {
            // how fast the driven wheels turn, as road speed: spinning wheels and wheels in the air run ahead of the ground
            float wheel = Mathf.Abs(fwd);
            float push = braking ? 0f : Mathf.Abs(throttle);
            float boost = live && jeep.Boosting ? 1f : 0f;
            if (push > 0f) wheel += spin * 6f;
            int top = Mathf.Max(0, GearTop.Length - 1);
            Gear = Mathf.Clamp(Gear, 0, top);
            if (fwd < -0.5f) Gear = 0;

            float want;
            if (!live) want = 0f;
            else if (jeep.Afloat) want = 0.3f + 0.35f * Mathf.Abs(throttle);           // paddling
            else if (down == 0) want = push > 0f ? 0.92f : Mathf.Max(0f, Revs - dt * 0.5f) ; // wheels in the air rev free
            else
            {
                float x = wheel / Mathf.Max(1f, GearTop[Gear]);
                if (shiftIn <= 0f)
                {
                    // hold a gear longer with the foot down, change early when cruising
                    float up = Mathf.Lerp(ShiftUp * 0.72f, ShiftUp, Mathf.Max(push, boost));
                    if (x > up && Gear < top) { Gear++; shiftIn = 0.22f; }
                    else if (x < ShiftDown && Gear > 0) { Gear--; shiftIn = 0.16f; }
                    x = wheel / Mathf.Max(1f, GearTop[Gear]);
                }
                want = Mathf.Clamp01(x);
                if (Gear == 0 && push > 0f) want = Mathf.Max(want, 0.22f + 0.12f * boost);    // the clutch slipping as it pulls away
            }
            shiftIn -= dt;
            Revs = Mathf.Lerp(Revs, Mathf.Clamp01(want), 1f - Mathf.Exp(-(want > Revs ? 9f : 6f) * dt));

            // load: foot down (not while changing gear); the engine is duller and quieter off the throttle
            float wantLoad = live && shiftIn <= 0f ? Mathf.Max(push * (0.8f + 0.2f * boost), down == 0 ? 0f : 0.15f * Mathf.Clamp01(Revs * 2f)) : 0f;
            load = Mathf.Lerp(load, wantLoad, 1f - Mathf.Exp(-(wantLoad > load ? 10f : 6f) * dt));

            wobble += dt;
            float hz = Mathf.Lerp(IdleHz, RedlineHz, Revs) * (1f + 0.012f * (Mathf.PerlinNoise(wobble * 1.7f, 3.1f) - 0.5f) * (1f - Revs));
            float level = on * Volume * EngineVolume * (0.42f + 0.58f * load) * (0.75f + 0.25f * Revs);
            Loudness = on * (0.3f + 0.7f * load) * (0.5f + 0.5f * Revs);
            float cutoff = Mathf.Lerp(1400f + 4200f * Revs, 22000f, Mathf.Sqrt(load));

            // blend the two recordings either side of this pitch (equal power, by octaves), each bent onto it
            int n = engine.Length;
            int hi = 0;
            while (hi < n && engine[hi].Hz < hz) hi++;
            int lo = Mathf.Max(0, hi - 1); hi = Mathf.Min(hi, n - 1);
            float t = n == 0 || lo == hi ? 0f : Mathf.Clamp01(Mathf.Log(hz / engine[lo].Hz) / Mathf.Log(engine[hi].Hz / engine[lo].Hz));
            for (int i = 0; i < n; i++)
            {
                Layer l = engine[i];
                float share = i == lo && i == hi ? 1f : i == lo ? Mathf.Cos(t * Mathf.PI * 0.5f) : i == hi ? Mathf.Sin(t * Mathf.PI * 0.5f) : 0f;
                l.Src.volume = Mathf.Lerp(l.Src.volume, level * share, 1f - Mathf.Exp(-25f * dt));
                l.Src.pitch = Mathf.Clamp(hz / l.Hz, 0.35f, 2.8f);
                l.Filter.cutoffFrequency = cutoff;
            }
        }

        // ------------------------------------------------------------ tyres

        void Tyres(float dt, bool live, float speed, int down)
        {
            // what each wheel stands on: one wheel looked up every few hundredths of a second
            probeIn -= dt;
            if (live && probeIn <= 0f)
            {
                probeIn = 0.04f;
                int i = probeWheel = (probeWheel + 1) % 4;
                if (jeep.WheelDown(i))
                {
                    float y; byte kind; Color32 tint;
                    if (!jeep.WheelOnTerrain[i]) under[i] = Kind.Road;
                    else if (world != null && world.GroundInfo(jeep.WheelPoint[i], out y, out kind, out tint)) under[i] = KindOf(kind);
                }
            }

            for (int k = 0; k < (int)Kind.Count; k++) rollWant[k] = skidWant[k] = 0f;
            float slideMax = 0f;
            if (live && !jeep.Afloat)
                for (int i = 0; i < 4; i++)
                {
                    if (!jeep.WheelDown(i)) continue;
                    int k = (int)under[i];
                    float slide = Mathf.Abs(jeep.WheelSlide[i]), rolling = Mathf.Abs(jeep.WheelRoll[i]);
                    // the same measure the skid marks use, so what is heard is what is drawn
                    float s = Mathf.Clamp01(Mathf.Max((slide - 2.5f) / 5f, jeep.WheelSpin[i] * Mathf.Clamp01((rolling + slide) / 4f + 0.4f)));
                    s = Mathf.Max(s, jeep.Drift * Mathf.Clamp01((slide - 1f) / 4f) * (i >= 2 ? 1f : 0.5f));
                    skidWant[k] += s * 0.5f;                                  // two wheels sliding is a full skid
                    rollWant[k] += 0.25f * Mathf.Pow(Mathf.Clamp01(rolling / 26f), 0.6f);
                    slideMax = Mathf.Max(slideMax, slide);
                }

            for (int k = 0; k < (int)Kind.Count; k++)
            {
                // kinds sharing a source: the first of them carries the sum
                if (roll[k] != null) for (int j = 0; j < k; j++) if (roll[j] == roll[k]) { rollWant[j] += rollWant[k]; rollWant[k] = -1f; break; }
                if (skid[k] != null) for (int j = 0; j < k; j++) if (skid[j] == skid[k]) { skidWant[j] += skidWant[k]; skidWant[k] = -1f; break; }
            }
            for (int k = 0; k < (int)Kind.Count; k++)
            {
                if (roll[k] != null && rollWant[k] >= 0f)
                {
                    roll[k].volume = Mathf.Lerp(roll[k].volume, Mathf.Clamp01(rollWant[k]) * TyreVolume * Volume * on, 1f - Mathf.Exp(-6f * dt));
                    roll[k].pitch = 0.8f + 0.45f * Mathf.Clamp01(speed / 35f);
                }
                if (skid[k] != null && skidWant[k] >= 0f)
                {
                    AudioSource s = skid[k];
                    float want = Mathf.Clamp01(skidWant[k]);
                    // from silence, begin on another recording and somewhere else in it
                    if (want > 0.02f && s.volume < 0.004f)
                    {
                        AudioClip c = sets.Pick(skidSet[k]);
                        if (c != null && c != s.clip) { s.clip = c; s.Play(); }
                        s.time = sets.Random01() * s.clip.length * 0.8f;
                    }
                    s.volume = Mathf.Lerp(s.volume, want * SkidVolume * Volume * on, 1f - Mathf.Exp(-(want > s.volume ? 14f : 7f) * dt));
                    s.pitch = (Kind)k == Kind.Road ? 0.92f + 0.2f * Mathf.Clamp01(slideMax / 14f) : 0.9f + 0.25f * Mathf.Clamp01(speed / 30f);
                }
            }
        }

        // ------------------------------------------------------------ brakes

        void Brakes(float dt, bool live, float speed, bool braking, int down)
        {
            bool held = live && down >= 2 && (braking || jeep.Handbraking);
            if (held) brakePeak = Mathf.Max(brakePeak, speed);
            // the squeak as it comes to rest, after a real stop from speed
            if (held && wasBraking && speed < 1.0f && brakePeak > 6f) { Shot("brake_stop", Mathf.Clamp(0.25f + brakePeak * 0.012f, 0.25f, 0.6f) * BrakeVolume * 2f, 0.06f); brakePeak = 0f; }
            if (!held) brakePeak = 0f;
            wasBraking = held;
            if (brake != null)
            {
                float want = held ? (braking ? 1f : 0.6f) * Mathf.Clamp01(speed / 14f) * Mathf.Clamp01(speed / 2f) : 0f;
                brake.volume = Mathf.Lerp(brake.volume, want * BrakeVolume * Volume * on, 1f - Mathf.Exp(-(want > brake.volume ? 12f : 8f) * dt));
                brake.pitch = 0.85f + 0.3f * Mathf.Clamp01(speed / 30f);
            }
        }
    }
}
