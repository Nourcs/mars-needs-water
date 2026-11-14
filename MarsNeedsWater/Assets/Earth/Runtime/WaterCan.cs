using UnityEngine;
using UnityEngine.InputSystem;

namespace MarsNeedsWater.Earth
{
    /// <summary>
    /// The first step of the game's goal: take water from Earth. On foot, hold E at any sea, lake or river to fill
    /// the 10 L jerrycan; hold R to pour it out. Counts what has been taken from Earth's water so far.
    /// </summary>
    public sealed class WaterCan : MonoBehaviour
    {
        public const float Size = 10f;          // litres: the starter jerrycan
        const float Rate = 4f;                  // litres a second, scooping or pouring

        public float Litres { get; private set; }
        public double Taken { get; private set; }

        EarthPlayer player;
        float fxIn;
        bool canReach, scooping, pouring;
        GUIStyle big, small;

        void Update()
        {
            EarthWater w = EarthWater.I;
            if (player == null) player = FindAnyObjectByType<EarthPlayer>();
            Keyboard kb = Keyboard.current;
            scooping = pouring = false;
            if (player == null || w == null || kb == null) return;
            float dt = Time.deltaTime;

            Vector3 spot; bool lake;
            canReach = player.ReachWater(out spot, out lake);
            bool walking = player.Current == EarthPlayer.Mode.Walk && !EarthPlayer.InputBlocked;
            fxIn -= dt;

            if (walking && canReach && kb.eKey.isPressed && Litres < Size)
            {
                scooping = true;
                float got = Mathf.Min(Rate * dt, Size - Litres);
                Litres += got; Taken += got;
                if (fxIn <= 0f)
                {
                    w.Ripple(spot, 0.35f);
                    w.Spray(spot, Vector3.up * 1.2f, 3, 0.6f, 0.08f);
                    if (WaterSounds.I != null && Random.value < 0.3f) WaterSounds.I.Splash(0.5f);
                    fxIn = 0.15f;
                }
            }
            else if (walking && kb.rKey.isPressed && Litres > 0f)
            {
                pouring = true;
                float out_ = Mathf.Min(Rate * dt, Litres);
                Litres -= out_;
                Transform t = player.transform;
                Vector3 hand = t.position + t.forward * 0.55f + Vector3.up * 0.2f;
                if (fxIn <= 0f)
                {
                    w.Spray(hand, t.forward * 1.1f + Vector3.down * 0.4f, 5, 0.15f, 0.07f);
                    fxIn = 0.05f;
                }
                // poured back into water: that water was not taken after all
                if (canReach) { Taken -= out_; if (Random.value < dt * 6f) { w.Ripple(spot, 0.3f); if (WaterSounds.I != null) WaterSounds.I.Splash(0.35f); } }
            }
        }

        void OnGUI()
        {
            if (player == null || player.Current != EarthPlayer.Mode.Walk) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
                small = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperRight };
            }
            float w = 280f, x = Screen.width - w - 16f, y = Screen.height - 128f;
            Label(new Rect(x, y, w, 22), "Jerrycan  " + Litres.ToString("F1") + " / " + Size.ToString("F0") + " L", big);
            // a bar for the can
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(x + w - 160f, y + 24f, 160f, 8f), Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.75f, 0.95f, 0.95f);
            GUI.DrawTexture(new Rect(x + w - 160f, y + 24f, 160f * Litres / Size, 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Label(new Rect(x, y + 36f, w, 20), "Taken from Earth: " + Taken.ToString("F1") + " L", small);
            string hint = scooping ? "Scooping..." : pouring ? "Pouring..." :
                canReach && Litres < Size ? "Hold E to scoop water" : Litres >= Size && canReach ? "Can full. Hold R to pour" : Litres > 0f ? "Hold R to pour" : "Walk to water to scoop it";
            Label(new Rect(x, y + 56f, w, 20), hint, small);
        }

        static void Label(Rect r, string text, GUIStyle style)
        {
            Color c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            GUI.color = c;
            GUI.Label(r, text, style);
        }
    }
}
