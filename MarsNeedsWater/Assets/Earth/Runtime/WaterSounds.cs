using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The sound of water: surf rolling in near the sea, gentle lapping by lakes and rivers, splashes, wading steps,
    /// swim strokes, filling and pouring the can, the wash of a boat's bow, and everything muffled with your head under.
    /// Recordings are read from Resources/Earth/Audio/Water, grouped by name ("step_1", "step_2"... are one set, and
    /// a set never plays the same clip twice in a row). Any set with no recording falls back to noise made here.
    /// </summary>
    public sealed class WaterSounds : MonoBehaviour
    {
        public static WaterSounds I;
        /// <summary>How close and how much sea there is around the listener, 0 to 1 (for the other sounds of the shore).</summary>
        public float NearSea { get { return nearSea; } }

        const int Rate = 44100;
        const string Folder = "Earth/Audio/Water";

        EarthWorld world;
        EarthPlayer player;
        AudioSource surf, lap, hiss, pour, pourWater, fill;
        AudioSource[] voices;
        int nextVoice;
        AudioClip[] synthSplashes;
        readonly Dictionary<string, AudioClip[]> sets = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, int> last = new Dictionary<string, int>();
        AudioLowPassFilter muffle;
        float nextShot, nextStep, nextScoop, nearSea, nearLake, pourUntil, fillUntil;
        bool pourInWater, recordedBow;
        int probe;
        readonly float[] seaHits = new float[16], lakeHits = new float[16];
        System.Random rnd = new System.Random(77);

        public void Init(EarthWorld w)
        {
            I = this;
            world = w;

            // gather the recordings into sets by name, dropping a trailing _number
            var found = new Dictionary<string, List<AudioClip>>();
            foreach (AudioClip c in Resources.LoadAll<AudioClip>(Folder))
            {
                string key = c.name;
                int u = key.LastIndexOf('_');
                if (u > 0 && int.TryParse(key.Substring(u + 1), out _)) key = key.Substring(0, u);
                if (!found.TryGetValue(key, out var list)) found[key] = list = new List<AudioClip>();
                list.Add(c);
            }
            foreach (var kv in found) sets[kv.Key] = kv.Value.ToArray();

            recordedBow = sets.ContainsKey("bow");
            surf = Loop("Surf", One("surf") ?? MakeSurf());
            lap = Loop("Lapping", One("lap") ?? MakeLapping());
            hiss = Loop("Bow wash", One("bow") ?? MakeHiss());
            AudioClip p = One("pour"), pw = One("pour_water");     // with only one of the two, it serves for both
            if (p != null || pw != null) pour = Loop("Pour", p ?? pw);
            if (p != null && pw != null) pourWater = Loop("Pour into water", pw);

            if (One("fill") != null) fill = Loop("Can filling", One("fill"));

            voices = new AudioSource[8];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false; voices[i].spatialBlend = 0f;
            }
            if (!sets.ContainsKey("splash") && !sets.ContainsKey("splash_small"))
            {
                synthSplashes = new AudioClip[4];
                for (int i = 0; i < synthSplashes.Length; i++) synthSplashes[i] = MakeSplash(i);
            }
        }

        void OnDestroy() { if (I == this) I = null; }

        AudioClip One(string key) { return sets.TryGetValue(key, out var a) && a.Length > 0 ? a[0] : null; }

        AudioSource Loop(string name, AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0f; s.spatialBlend = 0f; s.playOnAwake = false;
            s.time = (float)rnd.NextDouble() * clip.length * 0.9f;      // not the same opening every time
            s.Play();
            return s;
        }

        /// <summary>
        /// Play one clip from the first of these sets that has recordings, never the one it played last.
        /// Each shot has its own voice, so its pitch does not bend the ones still ringing. False if none exist.
        /// </summary>
        bool Shot(float volume, float pitchSpread, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!sets.TryGetValue(key, out var set) || set.Length == 0) continue;
                int i = rnd.Next(set.Length);
                if (set.Length > 1 && last.TryGetValue(key, out int was) && i == was) i = (i + 1 + rnd.Next(set.Length - 1)) % set.Length;
                last[key] = i;
                AudioSource v = voices[nextVoice]; nextVoice = (nextVoice + 1) % voices.Length;
                v.Stop();
                v.clip = set[i];
                v.pitch = 1f + ((float)rnd.NextDouble() * 2f - 1f) * pitchSpread;
                v.volume = Mathf.Clamp01(volume * (0.85f + (float)rnd.NextDouble() * 0.3f));
                v.Play();
                return true;
            }
            return false;
        }

        void SynthSplash(float power)
        {
            if (synthSplashes == null) return;
            AudioSource v = voices[nextVoice]; nextVoice = (nextVoice + 1) % voices.Length;
            v.Stop();
            v.clip = synthSplashes[rnd.Next(synthSplashes.Length)];
            v.pitch = Mathf.Clamp(1.35f - power * 0.06f, 0.7f, 1.4f) * (0.92f + (float)rnd.NextDouble() * 0.16f);
            v.volume = Mathf.Clamp(0.12f + power * 0.09f, 0.1f, 1f);
            v.Play();
        }

        /// <summary>A splash, louder for bigger ones. power about 1 (a hand) to 10 (a dive from high up).</summary>
        public void Splash(float power)
        {
            if (Time.time < nextShot) return;
            nextShot = Time.time + 0.08f;
            bool ok = power >= 3.5f ? Shot(Mathf.Clamp(0.55f + power * 0.05f, 0.5f, 1f), 0.06f, "splash_big", "splash", "splash_small")
                    : power >= 1.2f ? Shot(Mathf.Clamp(0.3f + power * 0.1f, 0.3f, 0.7f), 0.08f, "splash", "splash_small", "splash_big")
                    : Shot(Mathf.Clamp(0.18f + power * 0.15f, 0.15f, 0.4f), 0.1f, "splash_small", "splash");
            if (!ok) SynthSplash(power);
        }

        /// <summary>A step wading through shallow water. speed in metres a second.</summary>
        public void Step(float speed)
        {
            if (Time.time < nextStep) return;
            nextStep = Time.time + 0.2f;
            if (!Shot(Mathf.Clamp(0.28f + speed * 0.04f, 0.25f, 0.6f), 0.08f, "step", "splash_small", "splash")) SynthSplash(0.4f + speed * 0.05f);
        }

        /// <summary>An arm coming over and pulling through the water, swimming.</summary>
        public void Stroke(bool fast)
        {
            if (Time.time < nextStep) return;
            nextStep = Time.time + 0.2f;
            if (!Shot(fast ? 0.4f : 0.3f, 0.07f, "stroke", "splash_small", "splash")) SynthSplash(1.1f);
        }

        /// <summary>The can dipped in and filling. Call it every frame while filling; it paces itself.</summary>
        public void Scoop()
        {
            bool starting = Time.time > fillUntil + 0.3f;
            fillUntil = Time.time + 0.12f;
            if (fill != null && !starting) return;             // with a filling sound, one dip at the start is enough
            if (Time.time < nextScoop) return;
            nextScoop = Time.time + 0.55f + (float)rnd.NextDouble() * 0.3f;
            if (!Shot(0.45f, 0.06f, "scoop", "splash_small", "splash")) SynthSplash(0.5f);
        }

        /// <summary>Water running out of the can. Call it every frame while pouring; it stops by itself.</summary>
        public void Pour(bool intoWater)
        {
            pourUntil = Time.time + 0.12f;
            pourInWater = intoWater;
        }

        void Update()
        {
            Camera cam = Camera.main;
            if (cam == null || world == null) return;
            if (muffle == null && cam.GetComponent<AudioListener>() != null)
                muffle = cam.GetComponent<AudioLowPassFilter>() ?? cam.gameObject.AddComponent<AudioLowPassFilter>();

            // listen around: a few spots a frame on rings 10 to 120 m out; the sea is heard further than a lake
            Vector3 c = cam.transform.position;
            for (int k = 0; k < 2; k++)
            {
                probe = (probe + 1) % 16;
                float a = probe * 2.39996f, r = 10f + (probe % 4) * 36f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                float level, bed; bool lake;
                bool wet = world.WaterAt(p, out level, out bed, out lake) && level > bed;
                float near = 1f - r / 140f;
                seaHits[probe] = wet && !lake ? near : 0f;
                lakeHits[probe] = wet && lake ? near : 0f;
            }
            float sea = 0, lk = 0;
            for (int i = 0; i < 16; i++) { sea += seaHits[i]; lk += lakeHits[i]; }
            float alt = Mathf.Max(0f, c.y - 0f);
            float high = Mathf.Clamp01(1f - (alt - 20f) / 300f);          // fades as you climb away
            nearSea = Mathf.Lerp(nearSea, Mathf.Clamp01(sea / 5f) * high, Time.deltaTime * 1.5f);
            nearLake = Mathf.Lerp(nearLake, Mathf.Clamp01(lk / 6f) * high, Time.deltaTime * 1.5f);
            surf.volume = nearSea * 0.55f;
            lap.volume = nearLake * 0.35f;

            // the boat's bow
            if (player == null) player = FindAnyObjectByType<EarthPlayer>();
            EarthPlayer pl = player;
            float boatSpeed = pl != null && pl.Afloat ? pl.Speed : 0f;
            // a recorded wash keeps its own pitch, more or less; the noise one needs bending to sound like speed
            hiss.volume = Mathf.Lerp(hiss.volume, Mathf.Clamp01(boatSpeed / 20f) * (recordedBow ? 0.6f : 0.5f), Time.deltaTime * 4f);
            hiss.pitch = recordedBow ? 0.9f + Mathf.Clamp01(boatSpeed / 25f) * 0.2f : 0.8f + Mathf.Clamp01(boatSpeed / 25f) * 0.5f;

            // the can pouring: quick in, quick out
            bool pouring = Time.time < pourUntil;
            if (fill != null) fill.volume = Mathf.MoveTowards(fill.volume, Time.time < fillUntil ? 0.4f : 0f, Time.deltaTime * 4f);
            if (pour != null) pour.volume = Mathf.MoveTowards(pour.volume, pouring && !(pourInWater && pourWater != null) ? 0.5f : 0f, Time.deltaTime * 5f);
            if (pourWater != null) pourWater.volume = Mathf.MoveTowards(pourWater.volume, pouring && pourInWater ? 0.5f : 0f, Time.deltaTime * 5f);

            if (muffle != null)
            {
                bool under = EarthWater.I != null && EarthWater.I.Under;
                muffle.cutoffFrequency = Mathf.Lerp(muffle.cutoffFrequency, under ? 600f : 22000f, Time.deltaTime * 8f);
            }
        }

        // ------------------------------------------------------------ making the sounds

        /// <summary>Waves rolling in: deep rumbling noise that swells and crashes, a whole number of waves per loop.</summary>
        AudioClip MakeSurf()
        {
            const float Len = 16f;
            int n = (int)(Rate * Len);
            var d = new float[n];
            float brown = 0, lp = 0, hp = 0, prevHp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = t / Len;
                float white = (float)rnd.NextDouble() * 2f - 1f;
                brown = brown * 0.995f + white * 0.06f;                       // low roar
                lp += (white - lp) * 0.08f;                                  // mid hiss
                hp = 0.97f * (prevHp + white - lp); prevHp = white - lp;
                // three waves in each loop, uneven, each a rise and a long fizzing fall
                float env = 0;
                float[] at = { 0.05f, 0.36f, 0.71f }, size = { 1f, 0.75f, 0.9f };
                for (int k = 0; k < 3; k++)
                {
                    float x = u - at[k]; if (x < 0) x += 1f;
                    float s = x * Len;
                    env += size[k] * (s < 1.2f ? s / 1.2f : Mathf.Exp(-(s - 1.2f) * 0.55f));
                }
                d[i] = brown * (0.35f + 0.25f * env) + lp * 0.5f * env + hp * 0.12f * env * env;
            }
            Normalise(d, 0.8f);
            d = Loopable(d, Rate / 4);
            var c = AudioClip.Create("Surf", d.Length, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Small water slapping a bank: soft clicks and gurgles over quiet noise.</summary>
        AudioClip MakeLapping()
        {
            const float Len = 9f;
            int n = (int)(Rate * Len);
            var d = new float[n];
            float lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                float white = (float)rnd.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.05f; lp2 += (lp - lp2) * 0.2f;
                d[i] = lp2 * 0.25f;
            }
            for (int k = 0; k < 26; k++)                                       // little slaps
            {
                int start = rnd.Next(n - Rate / 2);
                float f = 180f + (float)rnd.NextDouble() * 320f, amp = 0.2f + (float)rnd.NextDouble() * 0.4f;
                for (int j = 0; j < Rate / 6; j++)
                {
                    float t = j / (float)Rate;
                    float env = Mathf.Exp(-t * 28f) * Mathf.Min(1f, t * 400f);
                    d[start + j] += Mathf.Sin(2f * Mathf.PI * f * t * (1f + t * 3f)) * env * amp * 0.5f + ((float)rnd.NextDouble() * 2f - 1f) * env * amp * 0.25f;
                }
            }
            Normalise(d, 0.6f);
            d = Loopable(d, Rate / 4);
            var c = AudioClip.Create("Lapping", d.Length, 1, Rate, false); c.SetData(d, 0); return c;
        }

        AudioClip MakeHiss()
        {
            int n = Rate * 3;
            var d = new float[n];
            float lp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float white = (float)rnd.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.3f;
                float wob = 0.75f + 0.25f * Mathf.Sin(i * 2f * Mathf.PI * 3f / n) * Mathf.Sin(i * 2f * Mathf.PI * 7f / n);
                d[i] = (lp - prev * 0.5f) * wob; prev = lp;
            }
            Normalise(d, 0.6f);
            d = Loopable(d, Rate / 8);
            var c = AudioClip.Create("Spray hiss", d.Length, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>A splash: a sharp slap, a burst of spray, then bubbles popping as it settles.</summary>
        AudioClip MakeSplash(int variant)
        {
            int n = (int)(Rate * 0.9f);
            var d = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)rnd.NextDouble() * 2f - 1f;
                lp += (white - lp) * (0.25f + 0.1f * variant);
                float body = Mathf.Exp(-t * 9f) * Mathf.Min(1f, t * 300f);
                float slap = Mathf.Exp(-t * 60f);
                d[i] = lp * body * 0.9f + white * slap * 0.5f + Mathf.Sin(t * 2f * Mathf.PI * 90f) * slap * 0.6f;
            }
            int bubbles = 6 + variant * 3;
            for (int k = 0; k < bubbles; k++)
            {
                int start = (int)(Rate * (0.04f + (float)rnd.NextDouble() * 0.5f));
                float f = 600f + (float)rnd.NextDouble() * 1400f, amp = 0.15f + (float)rnd.NextDouble() * 0.2f;
                for (int j = 0; j < Rate / 20 && start + j < n; j++)
                {
                    float t = j / (float)Rate;
                    d[start + j] += Mathf.Sin(2f * Mathf.PI * f * t * (1f + t * 25f)) * Mathf.Exp(-t * 90f) * amp;
                }
            }
            Normalise(d, 0.9f);
            var c = AudioClip.Create("Splash " + variant, n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        static void Normalise(float[] d, float peak)
        {
            float m = 1e-6f;
            foreach (float v in d) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        /// <summary>Blend the tail into the start and cut it off, so the loop has no click.</summary>
        static float[] Loopable(float[] d, int k)
        {
            int n = d.Length;
            var o = new float[n - k];
            System.Array.Copy(d, o, n - k);
            for (int i = 0; i < k; i++)
            {
                float a = i / (float)k;
                o[i] = d[i] * a + d[n - k + i] * (1f - a);
            }
            return o;
        }
    }
}
