using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth.Editor
{
    /// <summary>
    /// Takes pictures of the game at a list of places, to judge the art style: Logs/style-shots/&lt;name&gt;.png.
    /// Create Temp/style-shots.request with lines "name lat lon height yaw pitch [jeep]" (height in game metres
    /// above the ground, yaw clockwise from north, pitch down; "jeep" takes the game's own chase view from the
    /// jeep instead). Enters Play if needed, goes to each place, waits for it to load, and leaves Play again.
    /// From the command line: -executeMethod MarsNeedsWater.Earth.Editor.StyleShots.Batch (quits when done).
    /// </summary>
    [InitializeOnLoad]
    static class StyleShots
    {
        const string Request = "Temp/style-shots.request";
        const string Out = "Logs/style-shots";
        const int W = 1920, H = 1080;

        static List<string[]> shots;
        static int index;
        static double next, arrived, quietSince;
        static bool startedPlay, going;

        static StyleShots() { EditorApplication.update += Poll; }

        public static void Batch()
        {
            SessionState.SetBool("StyleShotsQuit", true);
            // Unity empties Temp when it starts: a list left beside the project is picked up instead
            Directory.CreateDirectory("Temp");
            if (File.Exists("style-shots.txt")) File.Copy("style-shots.txt", Request, true);
            else if (!File.Exists(Request)) File.WriteAllText(Request, "start 37.79535 -122.39370 30 225 12\n");
        }

        static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (shots == null)
            {
                if (now < next) return;
                next = now + 0.5;
                if (!File.Exists(Request) || EditorApplication.isCompiling) return;
                if (!EditorApplication.isPlaying)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) { startedPlay = true; EditorApplication.EnterPlaymode(); }
                    return;
                }
                if (EarthWorld.I == null) return;
                shots = new List<string[]>();
                foreach (string line in File.ReadAllLines(Request))
                {
                    string[] p = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 6 && !p[0].StartsWith("#")) shots.Add(p);
                }
                File.Delete(Request);
                Directory.CreateDirectory(Out);
                index = 0; going = false;
                return;
            }
            if (!EditorApplication.isPlaying || EarthWorld.I == null) { shots = null; return; }
            if (index >= shots.Count) { Finish(); return; }

            string[] s = shots[index];
            EarthWorld world = EarthWorld.I;
            if (!going)
            {
                world.Teleport(F(s[1]), F(s[2]), (float)F(s[4]));
                arrived = now; quietSince = -1; going = true;
                return;
            }
            // loaded: nothing left in the queue for a few seconds in a row (or it is taking far too long)
            if (now - arrived < 5) return;
            if (world.Loading > 0 && now - arrived < 150) { quietSince = -1; return; }
            if (quietSince < 0) quietSince = now;
            if (now - quietSince < 4) return;

            try { Shoot(world, s); }
            catch (System.Exception e) { Debug.LogError("Style shot " + s[0] + ": " + e); File.WriteAllText(Out + "/" + s[0] + ".error.txt", e.ToString()); }
            index++; going = false;
        }

        static double F(string v) { return double.Parse(v, CultureInfo.InvariantCulture); }

        static void Shoot(EarthWorld world, string[] s)
        {
            Camera cam = Camera.main;
            bool jeep = s.Length > 6 && s[6] == "jeep";
            Vector3 oldPos = cam.transform.position; Quaternion oldRot = cam.transform.rotation;
            if (s.Length > 6 && s[6] == "player")
            {
                // the player's own figure, seen from in front and a little above
                Transform pl = world.PlayerTransform;
                foreach (Transform c in pl) if (c.name == "Figure") c.gameObject.SetActive(true);
                Quaternion face = Quaternion.Euler(0f, (float)F(s[4]), 0f);
                Vector3 at = pl.position + face * new Vector3(0.9f, 0.5f, 2.6f);
                cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(pl.position + Vector3.up * 0.1f - at));
            }
            else if (!jeep)
            {
                float ground;
                world.Ground(Vector3.zero, out ground);
                cam.transform.SetPositionAndRotation(new Vector3(0f, ground + (float)F(s[3]), 0f), Quaternion.Euler((float)F(s[5]), (float)F(s[4]), 0f));
            }
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            RenderTexture.active = null;
            tex.Apply();
            File.WriteAllBytes(Out + "/" + s[0] + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
        }

        static void Finish()
        {
            shots = null;
            File.WriteAllText(Out + "/done.txt", System.DateTime.Now.ToString("o"));
            bool quit = SessionState.GetBool("StyleShotsQuit", false);
            if (startedPlay || quit) EditorApplication.ExitPlaymode();
            startedPlay = false;
            if (quit) EditorApplication.Exit(0);
        }
    }
}
