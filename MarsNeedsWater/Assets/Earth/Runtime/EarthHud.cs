using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MarsNeedsWater.Earth
{
    /// <summary>On-screen text: where you are, how you are moving, the keys, the map credit, and "go to a place".</summary>
    public sealed class EarthHud : MonoBehaviour
    {
        public const string Credit = "Map data © OpenStreetMap contributors, Overture Maps Foundation (ODbL) · Elevation: Terrain Tiles, hosted by AWS";

        EarthWorld world;
        EarthPlayer player;
        bool searching, focus;
        string query = "";
        List<Places.Place> results = new List<Places.Place>();
        string placeLine = "";
        float nextPlace;
        GUIStyle label, small, box, field;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<EarthHud>() == null) new GameObject("Earth HUD").AddComponent<EarthHud>();
        }

        void Update()
        {
            if (world == null) world = EarthWorld.I;
            if (player == null) player = FindFirstObjectByType<EarthPlayer>();
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (!searching && kb.gKey.wasPressedThisFrame) { searching = true; focus = true; query = ""; results.Clear(); EarthPlayer.InputBlocked = true; }
            else if (searching && kb.escapeKey.wasPressedThisFrame) Close();

            if (world != null && Time.unscaledTime >= nextPlace)
            {
                nextPlace = Time.unscaledTime + 1f;
                Places.Place p; double km;
                placeLine = Places.Nearest(world.Lat, world.Lon, out p, out km)
                    ? (km < 3 || p.Landmark ? p.Name : km.ToString("F0") + " km from " + p.Name) + (p.Region.Length > 0 ? ", " + p.Region : "")
                    : "Open country";
            }
        }

        void Close() { searching = false; EarthPlayer.InputBlocked = false; }

        void Go(double lat, double lon, float facing = float.NaN) { world.Teleport(lat, lon, facing); Close(); }

        void Styles()
        {
            if (label != null) return;
            // place names come in every script: a system font with fallbacks for Arabic, Chinese, Japanese, Korean, Indic, Thai...
            Font f = null;
            try
            {
                f = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Arial Unicode MS", "Geeza Pro", "PingFang SC", "Hiragino Sans",
                    "Apple SD Gothic Neo", "Kohinoor Devanagari", "Thonburi", "Noto Sans", "Segoe UI", "Arial" }, 18);
            }
            catch (System.Exception) { f = null; }
            if (f != null) GUI.skin.font = f;
            label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            label.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            small.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
            box = new GUIStyle(GUI.skin.box);
            field = new GUIStyle(GUI.skin.textField) { fontSize = 20 };
        }

        static void Shadow(Rect r, string text, GUIStyle style)
        {
            Color c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        void OnGUI()
        {
            if (world == null || player == null) return;
            Styles();
            float w = Screen.width, h = Screen.height;

            Shadow(new Rect(16, 12, w - 32, 26), placeLine, label);
            string ns = world.Lat >= 0 ? "N" : "S", ew = world.Lon >= 0 ? "E" : "W";
            string mode = player.Current == EarthPlayer.Mode.Walk ? "On foot" : player.Current == EarthPlayer.Mode.Drive ? "Driving" : "Flying";
            float realKmh = player.Speed * (float)Geo.Scale * 3.6f;
            Shadow(new Rect(16, 38, w - 32, 20),
                System.Math.Abs(world.Lat).ToString("F5") + "° " + ns + "   " + System.Math.Abs(world.Lon).ToString("F5") + "° " + ew +
                "   ·   " + mode + "   ·   " + player.Speed.ToString("F0") + " m/s (like " + realKmh.ToString("F0") + " km/h on the real Earth)" +
                (player.Current == EarthPlayer.Mode.Fly ? "   ·   height " + world.Alt.ToString("F0") + " m" : ""), small);

            string status = null;
            if (world.Error != null) status = "Cannot reach the map data. Check the internet connection. (" + world.Error + ")";
            else if (player.Waiting) status = "Loading this part of the world...";
            else if (world.Loading > 0) status = "Loading " + world.Loading + " more squares";
            if (status != null) Shadow(new Rect(16, 60, w - 32, 20), status, small);

            Shadow(new Rect(16, h - 46, w - 32, 20),
                "Click to look around   WASD move   Shift faster   Space jump / up   C car   F fly (wheel or Q/E changes flying speed)   V view   G go to a place   Esc free the mouse", small);
            Shadow(new Rect(16, h - 24, w - 32, 20), Credit, small);

            if (!searching) return;
            float bw = Mathf.Min(560f, w - 40f), bx = (w - bw) / 2f, by = h * 0.18f;
            GUI.Box(new Rect(bx - 12, by - 40, bw + 24, 96 + results.Count * 30), "Go to a place: type a landmark, a town or city, or a latitude and longitude", box);
            GUI.SetNextControlName("q");
            string before = query;
            query = GUI.TextField(new Rect(bx, by, bw, 34), query, 60, field);
            if (focus) { GUI.FocusControl("q"); focus = false; }
            if (query != before) results = Places.Find(query, 8);

            Event e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            double lat, lon;
            if (Places.ParseLatLon(query, out lat, out lon))
            {
                if (GUI.Button(new Rect(bx, by + 42, bw, 28), "Go to " + lat.ToString("F4") + ", " + lon.ToString("F4")) || enter) { Go(lat, lon); return; }
            }
            else
            {
                for (int i = 0; i < results.Count; i++)
                {
                    Places.Place p = results[i];
                    string text = p.Landmark ? p.Name + "   ·   landmark, " + p.Region
                        : p.Name + (p.Region.Length > 0 ? "  (" + p.Region + ")" : "") + (p.Pop > 0 ? "   " + p.Pop.ToString("N0") + " people" : "");
                    if (GUI.Button(new Rect(bx, by + 42 + i * 30, bw, 28), text) || (enter && i == 0)) { Go(p.GoLat, p.GoLon, p.Facing); return; }
                }
            }
        }
    }
}
