using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MarsNeedsWater.Earth.Editor
{
    /// <summary>
    /// Drives the jeep across the real streamed hills near Sausalito at 15, 30 and 60 m/s on autopilot and writes
    /// how it rode (wheel contact, air, bottom-outs, body accelerations) to Logs/jeep-terrain.txt. It enters Play
    /// mode by itself and leaves it when done. Tools > Mars Needs Water > Test Jeep On Real Terrain, or create
    /// Temp/jeep-terrain.request.
    /// </summary>
    static class JeepTerrainTest
    {
        const string Request = "Temp/jeep-terrain.request";
        const double Lat = 37.8390, Lon = -122.4950;
        static readonly float[] Speeds = { 15f, 30f, 60f };
        static readonly float[] Headings = { 270f, 200f };

        enum Phase { Idle, Starting, Teleport, Waiting, Accelerating, Measuring, Done }
        static Phase phase = Phase.Idle;
        static int run;
        static double until, nextTrace;
        static StringBuilder log;
        static bool background;

        [InitializeOnLoadMethod]
        static void Hook() { EditorApplication.update += Update; }

        [MenuItem("Tools/Mars Needs Water/Test Jeep On Real Terrain")]
        static void Start()
        {
            if (EditorApplication.isPlaying || phase != Phase.Idle) return;
            log = new StringBuilder();
            log.AppendLine($"Real terrain near {Lat}, {Lon}");
            run = 0;
            background = Application.runInBackground;
            Application.runInBackground = true;      // keep the game running while the editor window is in the background
            phase = Phase.Starting;
            EditorApplication.isPlaying = true;
        }

        static double Now { get { return Time.realtimeSinceStartupAsDouble; } }

        static void Update()
        {
            if (phase == Phase.Idle)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists(Request))
                {
                    File.Delete(Request);
                    Start();
                }
                return;
            }
            if (!EditorApplication.isPlaying)
            {
                if (phase == Phase.Done || phase == Phase.Starting) { if (phase == Phase.Done) phase = Phase.Idle; return; }
                Finish("Play mode ended early");
                return;
            }
            EarthWorld world = EarthWorld.I;
            EarthPlayer player = Object.FindFirstObjectByType<EarthPlayer>();
            if (world == null || player == null || player.Jeep == null) return;
            Jeep j = player.Jeep;
            float target = Speeds[run % Speeds.Length], heading = Headings[run / Speeds.Length];

            switch (phase)
            {
                case Phase.Starting:
                    phase = Phase.Teleport;
                    break;
                case Phase.Teleport:
                    player.Autopilot = true; player.AutoThrottle = 0f; player.AutoSteer = 0f; player.AutoBoost = false;
                    player.EnterJeep();
                    world.Teleport(Lat, Lon, heading);
                    until = Now + 60.0;
                    log.AppendLine($"  run {run}: teleported at {Now:F1}");
                    phase = Phase.Waiting;
                    break;
                case Phase.Waiting:
                    bool ready = !player.Waiting && player.Current == EarthPlayer.Mode.Drive && j.Grounded > 0 && (world.Loading == 0 || Now > until - 40.0);
                    if (ready) { phase = Phase.Accelerating; until = Now + 12.0; }
                    else if (Now > until) { log.AppendLine($"{target} m/s heading {heading}: ground never loaded (waiting {player.Waiting}, mode {player.Current}, loading {world.Loading}, wheels {j.Grounded})"); Next(); }
                    break;
                case Phase.Accelerating:
                    Drive(player, j, target, heading);
                    if (j.Speed > target * 0.85f || Now > until) { j.Ride.Reset(); phase = Phase.Measuring; until = Now + 10.0; }
                    break;
                case Phase.Measuring:
                    Drive(player, j, target, heading);
                    if (run == 1 && Now >= nextTrace)
                    {
                        nextTrace = Now + 0.25;
                        Vector3 p = j.transform.position, f = j.transform.forward; f.y = 0f; f.Normalize();
                        Vector3 r = new Vector3(f.z, 0f, -f.x);
                        float g0, gf, gb, gr, gl;
                        world.Ground(p, out g0, true, true); world.Ground(p + f * 3f, out gf, true, true); world.Ground(p - f * 3f, out gb, true, true);
                        world.Ground(p + r * 3f, out gr, true, true); world.Ground(p - r * 3f, out gl, true, true);
                        float roll = Mathf.Asin(Mathf.Clamp(j.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg, pitch = Mathf.Asin(Mathf.Clamp(j.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                        log.AppendLine($"    t {until - Now:F2} speed {j.Speed:F1} wheels {j.Grounded} up.y {j.transform.up.y:F2} roll {roll:F0} pitch {pitch:F0} height {p.y - g0 - 0.85f:F2} slope along {Mathf.Atan2(gf - gb, 6f) * Mathf.Rad2Deg:F0} side {Mathf.Atan2(gr - gl, 6f) * Mathf.Rad2Deg:F0} steer {player.AutoSteer:F2} throttle {player.AutoThrottle:F2}");
                    }
                    if (Now > until || j.Upturned)
                    {
                        log.AppendLine($"{target} m/s heading {heading}° (step {Time.fixedDeltaTime * 1000f:F0} ms): {j.Ride}{(j.Upturned ? "  -- ended UPTURNED" : "")}");
                        Next();
                    }
                    break;
            }
        }

        static void Drive(EarthPlayer player, Jeep j, float target, float heading)
        {
            float err = Mathf.DeltaAngle(j.Heading(heading), heading);
            player.AutoSteer = Mathf.Clamp(err / 15f, -1f, 1f);
            player.AutoBoost = target > 32f;
            player.AutoThrottle = Mathf.Clamp((target - j.Speed) * 0.5f, -1f, 1f);
        }

        static void Next()
        {
            run++;
            if (run >= Speeds.Length * Headings.Length) { Finish(null); return; }
            phase = Phase.Teleport;
        }

        static void Finish(string why)
        {
            if (why != null) log.AppendLine(why);
            File.WriteAllText("Logs/jeep-terrain.txt", log.ToString());
            Debug.Log("Jeep terrain test:\n" + log);
            EarthPlayer player = Object.FindFirstObjectByType<EarthPlayer>();
            if (player != null) player.Autopilot = false;
            phase = Phase.Done;
            Application.runInBackground = background;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
