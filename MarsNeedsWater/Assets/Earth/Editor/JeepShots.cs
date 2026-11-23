using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Earth.Editor
{
    /// <summary>
    /// Drives the jeep on autopilot at a list of places (a weave, then a handbrake drift) and takes pictures of the
    /// tyre tracks and dust it leaves: Logs/jeep-shots/&lt;name&gt;-chase.png (the game's own view), -tracks.png (looking
    /// back down at the tracks) and -side.png, plus log.txt saying what ground the wheels found.
    /// Create Temp/jeep-shots.request with lines "name lat lon heading". Enters Play if needed and leaves it again.
    /// From the command line: -executeMethod MarsNeedsWater.Earth.Editor.JeepShots.Batch (reads jeep-shots.txt beside
    /// the project, quits when done).
    /// </summary>
    [InitializeOnLoad]
    static class JeepShots
    {
        const string Request = "Temp/jeep-shots.request";
        const string Out = "Logs/jeep-shots";
        const int W = 1600, H = 900;

        static List<string[]> shots;
        static int index, stage;
        static double next, arrived, driveFrom;
        static bool startedPlay;
        static System.Text.StringBuilder log;

        static JeepShots() { EditorApplication.update += Poll; }

        public static void Batch()
        {
            SessionState.SetBool("JeepShotsQuit", true);
            Directory.CreateDirectory("Temp");
            if (File.Exists("jeep-shots.txt")) File.Copy("jeep-shots.txt", Request, true);
            else if (!File.Exists(Request)) File.WriteAllText(Request, "beach 37.7594 -122.5107 0\n");
        }

        static double F(string v) { return double.Parse(v, CultureInfo.InvariantCulture); }

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
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) { startedPlay = true; Application.runInBackground = true; EditorApplication.EnterPlaymode(); }
                    return;
                }
                if (EarthWorld.I == null) return;
                shots = new List<string[]>();
                foreach (string line in File.ReadAllLines(Request))
                {
                    string[] p = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4 && !p[0].StartsWith("#")) shots.Add(p);
                }
                File.Delete(Request);
                Directory.CreateDirectory(Out);
                log = new System.Text.StringBuilder();
                index = 0; stage = 0;
                return;
            }
            if (!EditorApplication.isPlaying || EarthWorld.I == null) { shots = null; return; }
            if (index >= shots.Count) { Finish(); return; }

            string[] s = shots[index];
            EarthWorld world = EarthWorld.I;
            EarthPlayer player = Object.FindFirstObjectByType<EarthPlayer>();
            if (player == null || player.Jeep == null) return;
            Jeep j = player.Jeep;
            float heading = (float)F(s[3]);
            try
            {
                switch (stage)
                {
                    case 0:
                        player.Autopilot = true; player.AutoThrottle = 0f; player.AutoSteer = 0f; player.AutoBoost = false;
                        player.EnterJeep();
                        world.Teleport(F(s[1]), F(s[2]), heading);
                        arrived = now; stage = 1;
                        break;
                    case 1:
                        if (now - arrived < 4) return;
                        bool ready = !player.Waiting && j.Grounded > 0 && (world.Loading == 0 || now - arrived > 60);
                        if (!ready && now - arrived < 120) return;
                        driveFrom = now; stage = 2;
                        break;
                    case 2:
                    {
                        float t = (float)(now - driveFrom);
                        // straight, a weave, then a handbrake drift to the right
                        float want = heading + (t < 2.5f ? 0f : t < 7f ? Mathf.Sin((t - 2.5f) * 1.6f) * 35f : 0f);
                        float err = Mathf.DeltaAngle(j.Heading(heading), want);
                        player.AutoSteer = t > 7.6f ? 1f : Mathf.Clamp(err / 15f, -1f, 1f);
                        player.AutoThrottle = Mathf.Clamp((15f - j.Speed) * 0.5f, -1f, 1f);
                        if (t > 7.6f) player.AutoThrottle = 1f;
                        j.Handbraking = t > 7.6f && t < 8.1f;
                        if (t > 9.3f)
                        {
                            var trails = j.GetComponent<JeepTrails>();
                            log.AppendLine($"{s[0]}: speed {j.Speed:F1} m/s, wheels down {j.Grounded}, drift {j.Drift:F2}, slide {j.SlideAngle:F0}°, ground {(trails != null ? trails.Describe() : "no trails")}");
                            Shoot(s[0] + "-chase", Camera.main, false, Vector3.zero, Vector3.zero);
                            Vector3 p = j.transform.position, f = j.transform.forward; f.y = 0f; f.Normalize();
                            Vector3 r = new Vector3(f.z, 0f, -f.x);
                            Shoot(s[0] + "-tracks", Camera.main, true, p + Vector3.up * 3.2f + f * 4.5f - r * 1.5f, p - f * 5f - Vector3.up * 0.8f);
                            Shoot(s[0] + "-side", Camera.main, true, p + Vector3.up * 1.3f - r * 6f - f * 2f, p - f * 2.5f - Vector3.up * 0.5f);
                            Shoot(s[0] + "-close", Camera.main, true, p + Vector3.up * 1.6f - f * 7f + r * 0.8f, p - f * 9.5f + r * 0.8f - Vector3.up * 0.85f);
                            player.AutoThrottle = 0f; player.AutoSteer = 0f;
                            index++; stage = 0;
                        }
                        break;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogError("Jeep shot " + s[0] + ": " + e); log.AppendLine(s[0] + " FAILED: " + e); index++; stage = 0; }
        }

        static void Shoot(string name, Camera cam, bool move, Vector3 from, Vector3 at)
        {
            Vector3 oldPos = cam.transform.position; Quaternion oldRot = cam.transform.rotation;
            if (move) cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            RenderTexture.active = null;
            tex.Apply();
            File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            cam.transform.SetPositionAndRotation(oldPos, oldRot);
        }

        static void Finish()
        {
            shots = null;
            File.WriteAllText(Out + "/log.txt", log.ToString());
            File.WriteAllText(Out + "/done.txt", System.DateTime.Now.ToString("o"));
            EarthPlayer player = Object.FindFirstObjectByType<EarthPlayer>();
            if (player != null) player.Autopilot = false;
            bool quit = SessionState.GetBool("JeepShotsQuit", false);
            if (startedPlay || quit) EditorApplication.ExitPlaymode();
            startedPlay = false;
            if (quit) EditorApplication.Exit(0);
        }
    }
}
