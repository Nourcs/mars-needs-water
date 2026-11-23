using UnityEngine;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The air of the place you are in: wind over open ground, leaves and birdsong in woods, insects in meadows,
    /// the hum of a town, dry wind in the desert, cold wind on snow, gulls by the sea, and the high wind when flying.
    /// It listens to the ground around the camera (its GroundKind at a ring of spots), how built-up the map square
    /// is, the sea nearby and the height above the ground, and mixes long loops to match.
    ///
    /// Recordings are read from Resources/Earth/Audio/Ambient: wind, trees, birds, meadow, city, desert, snow,
    /// gulls, high. With several of one name ("birds_1", "birds_2") one is picked each time that sound comes
    /// back from silence. Anything missing is simply silent.
    /// </summary>
    public sealed class AmbientSounds : MonoBehaviour
    {
        public static AmbientSounds I;
        const string Folder = "Earth/Audio/Ambient";

        [Range(0f, 1f)] public float Volume = 1f;
        [Tooltip("How much the jeep's engine pushes the ambience down while it is working.")] [Range(0f, 1f)] public float EngineDuck = 0.45f;

        enum Bed { Wind, Trees, Birds, Meadow, City, Desert, Snow, Gulls, High, Count }
        static readonly string[] BedName = { "wind", "trees", "birds", "meadow", "city", "desert", "snow", "gulls", "high" };
        static readonly float[] BedLevel = { 0.30f, 0.40f, 0.45f, 0.40f, 0.45f, 0.40f, 0.45f, 0.35f, 0.50f };

        /// <summary>What it hears, for tests and tuning: one number 0-1 for each bed, in the order of the names.</summary>
        public readonly float[] Mix = new float[(int)Bed.Count];

        const int Spots = 24;
        SoundSets sets;
        EarthWorld world;
        readonly AudioSource[] beds = new AudioSource[(int)Bed.Count];
        readonly byte[] seen = new byte[Spots];
        readonly float[] share = new float[GroundKind.Count];
        int spot;
        float built, sea, above = 0f, gust;
        bool any;

        void Awake()
        {
            I = this;
            sets = new SoundSets(Folder, 53);
            for (int b = 0; b < (int)Bed.Count; b++)
                if (sets.Has(BedName[b])) beds[b] = sets.Loop(transform, BedName[b], sets.Pick(BedName[b]));
            for (int i = 0; i < Spots; i++) seen[i] = 255;
        }

        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            if (world == null) world = EarthWorld.I;
            Camera cam = Camera.main;
            if (world == null || cam == null) return;
            float dt = Time.deltaTime;
            Vector3 c = cam.transform.position;

            // look around: two spots a frame on rings 12 to 190 m out; 255 = nothing known (water, or not loaded)
            for (int k = 0; k < 2; k++)
            {
                spot = (spot + 1) % Spots;
                float a = spot * 2.39996f, r = 12f + (spot % 4) * 59f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                float y; byte kind; Color32 tint;
                bool ok = world.GroundInfo(p, out y, out kind, out tint) && kind < GroundKind.Count;
                seen[spot] = ok ? kind : (byte)255;
                if (ok && r < 20f) above = Mathf.Lerp(above, Mathf.Max(0f, c.y - y), 0.5f);
            }
            for (int k = 0; k < GroundKind.Count; k++) share[k] = 0f;
            int known = 0;
            for (int i = 0; i < Spots; i++) if (seen[i] != 255) { share[seen[i]] += 1f; known++; }
            float water = 1f - known / (float)Spots;
            if (known > 0) for (int k = 0; k < GroundKind.Count; k++) share[k] /= Spots;
            any |= known > 0;

            float perSqKm;
            if (world.BuildingsNear(c, out perSqKm)) built = Mathf.Lerp(built, Mathf.Clamp01(perSqKm / 900f), dt * 0.7f);
            sea = Mathf.Lerp(sea, WaterSounds.I != null ? WaterSounds.I.NearSea : 0f, dt * 1.5f);

            float low = Mathf.Clamp01(1f - (above - 25f) / 180f);            // ground sounds fade as you climb
            float high = Mathf.Clamp01((above - 60f) / 400f);
            float paved = share[GroundKind.Paved];
            float town = Mathf.Clamp01(Mathf.Max(built, paved * 1.2f) * (0.4f + 0.6f * built) * 1.6f);
            float woods = Mathf.Clamp01(share[GroundKind.Forest] * 2.2f);
            float green = Mathf.Clamp01((share[GroundKind.Grass] + share[GroundKind.Farm] + share[GroundKind.DryGrass] * 0.7f) * 1.6f);
            float dry = Mathf.Clamp01((share[GroundKind.Sand] * (1f - sea) + share[GroundKind.Rock] + share[GroundKind.DryGrass] * 0.5f + share[GroundKind.Dirt] * 0.5f) * 1.6f);
            float cold = Mathf.Clamp01(share[GroundKind.Snow] * 2f);

            // a slow swell so the wind is never flat
            gust += dt;
            float swell = 0.75f + 0.5f * Mathf.PerlinNoise(gust * 0.07f, 7.3f);

            float[] want = Mix;
            want[(int)Bed.Wind] = (0.35f + 0.45f * (1f - woods) * (1f - town)) * (1f - cold) * (1f - 0.6f * dry) * swell * Mathf.Lerp(1f, 0.5f, high);
            want[(int)Bed.Trees] = woods * low * swell;
            want[(int)Bed.Birds] = Mathf.Clamp01(woods + green * 0.45f) * (1f - 0.6f * town) * (1f - cold) * low;
            want[(int)Bed.Meadow] = green * (1f - woods * 0.5f) * (1f - town) * (1f - cold) * low;
            want[(int)Bed.City] = town * Mathf.Clamp01(1f - (above - 60f) / 500f);
            want[(int)Bed.Desert] = dry * (1f - town * 0.7f) * low * swell;
            want[(int)Bed.Snow] = cold * swell;
            want[(int)Bed.Gulls] = Mathf.Clamp01(sea * 1.3f) * Mathf.Clamp01(0.3f + water) * low;
            want[(int)Bed.High] = high * swell;

            bool under = EarthWater.I != null && EarthWater.I.Under;
            float duck = 1f - EngineDuck * (JeepSounds.I != null ? JeepSounds.I.Loudness : 0f);
            float master = Volume * duck * (any ? 1f : 0f) * (under ? 0.25f : 1f);
            for (int b = 0; b < (int)Bed.Count; b++)
            {
                AudioSource s = beds[b];
                if (s == null) continue;
                float w = Mathf.Clamp01(want[b]) * BedLevel[b] * master;
                if (w > 0.01f && s.volume < 0.002f)                      // back from silence: another take, if there are several
                {
                    AudioClip clip = sets.Pick(BedName[b]);
                    if (clip != null && clip != s.clip) { s.clip = clip; s.time = sets.Random01() * clip.length * 0.9f; s.Play(); }
                }
                s.volume = Mathf.Lerp(s.volume, w, 1f - Mathf.Exp(-0.8f * dt));
            }
        }
    }
}
