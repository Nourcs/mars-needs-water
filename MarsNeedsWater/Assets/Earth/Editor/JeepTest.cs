using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsNeedsWater.Earth.Editor
{
    /// <summary>
    /// Drives the jeep through a few set pieces in a scratch scene, stepping the physics by hand, and writes what
    /// happened to Logs/jeep-test.txt: settling after a drop, top speed, climbing, going downhill, off a cliff,
    /// a hard turn, floating. Tools > Mars Needs Water > Test Jeep Physics, or create Temp/jeep-test.request.
    /// </summary>
    static class JeepTest
    {
        const string Request = "Temp/jeep-test.request";
        const float Dt = 0.02f;
        static readonly Vector3 O = new Vector3(20000f, -20000f, 20000f);   // far from anything else
        static Scene scene;
        static StringBuilder log;

        [InitializeOnLoadMethod]
        static void OnLoad() { EditorApplication.update += Poll; }

        static double next;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Request)) return;
            File.Delete(Request);
            Run();
        }

        /// <summary>From the command line: -executeMethod MarsNeedsWater.Earth.Editor.JeepTest.Batch (quits when done).</summary>
        public static void Batch()
        {
            // the scratch scene is added beside a saved one: a fresh batch editor starts in an unsaved scene
            string[] scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            if (scenes.Length > 0) EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(scenes[0]));
            Run();
            EditorApplication.Exit(0);
        }

        [MenuItem("Tools/Mars Needs Water/Test Jeep Physics")]
        static void Run()
        {
            log = new StringBuilder();
            SimulationMode mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Scene active = SceneManager.GetActiveScene();
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                Settle(); TopSpeed(); Climb(20f); Climb(30f); Climb(40f); Climb(48f); Downhill(); Cliff(); Turn(); Slalom(); LaneChange(); Circle(); SideSlope(); Float(); Drift(1f, 0f, "road"); Drift(0.72f, 0.8f, "sand"); Drift(0.52f, 0.9f, "snow"); Loose(0.72f, 0.8f, 0.85f, "sand"); Loose(0.62f, 0.85f, 1.1f, "mud"); Loose(0.52f, 0.9f, 0.6f, "snow");
            }
            catch (System.Exception e) { log.AppendLine("FAILED: " + e); }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(active);
                Physics.simulationMode = mode;
            }
            File.WriteAllText("Logs/jeep-test.txt", log.ToString());
            Debug.Log("Jeep test:\n" + log);
        }

        static GameObject Slab(Vector3 centre, Vector3 size, float tiltDeg = 0f, float bankDeg = 0f)
        {
            var go = new GameObject("Slab");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = O + centre;
            go.transform.rotation = Quaternion.Euler(-tiltDeg, 0f, bankDeg);
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        static Jeep Spawn(Vector3 at, float speed = 0f, float pitchDeg = 0f, float bankDeg = 0f)
        {
            Jeep j = Jeep.Build();
            SceneManager.MoveGameObjectToScene(j.gameObject, scene);
            Quaternion r = Quaternion.Euler(-pitchDeg, 0f, bankDeg);
            j.transform.SetPositionAndRotation(O + at, r);
            j.Body.position = O + at; j.Body.rotation = r;
            j.Body.linearVelocity = r * Vector3.forward * speed;
            Physics.SyncTransforms();
            return j;
        }

        static void Step(Jeep j, int n = 1)
        {
            for (int i = 0; i < n; i++) { j.Tick(Dt); Physics.Simulate(Dt); }
        }

        static void Clear()
        {
            foreach (GameObject go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);
        }

        static string L(float[] a) { return $"{a[0]:F0}/{a[1]:F0}/{a[2]:F0}/{a[3]:F0}"; }

        static float Pitch(Jeep j) { return Mathf.Asin(Mathf.Clamp(j.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg; }
        static float Roll(Jeep j) { return Mathf.Asin(Mathf.Clamp(j.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg; }

        /// <summary>How many times the body overshoots its resting height (crossings after the first arrival).</summary>
        static int Overshoots(System.Collections.Generic.List<float> h)
        {
            float rest = h[h.Count - 1];
            int crossings = 0, side = 0;
            foreach (float y in h)
            {
                int s = y > rest + 0.01f ? 1 : y < rest - 0.01f ? -1 : 0;
                if (s != 0 && side != 0 && s != side) crossings++;
                if (s != 0) side = s;
            }
            return crossings;
        }

        static void Settle()
        {
            foreach (float drop in new[] { 0.4f, 1f })
            {
                Slab(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
                Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + drop, 0f));
                var h = new System.Collections.Generic.List<float>();
                float lastMoving = 0f;
                for (int i = 0; i < 250; i++)
                {
                    Step(j);
                    if (j.Grounded > 0 || h.Count > 0) h.Add(j.transform.position.y);
                    if (j.Body.linearVelocity.magnitude > 0.1f || j.Body.angularVelocity.magnitude > 0.1f) lastMoving = (i + 1) * Dt;
                }
                int o = Overshoots(h);
                log.AppendLine($"Drop {drop} m on flat: {o} overshoot(s) {(o <= 1 ? "PASS" : "FAIL")}, still after {lastMoving:F2} s, ride height {j.transform.position.y - O.y:F3} m ({-Jeep.Ground:F3} = at rest)");
                Clear();
            }
            // a kerb 15 cm high at 15 m/s
            Slab(new Vector3(0f, -0.5f, 100f), new Vector3(60f, 1f, 400f));
            Slab(new Vector3(0f, 0.075f - 0.5f, 140f), new Vector3(60f, 1f, 200f));
            Jeep k = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, 0f), 15f);
            k.Throttle = 0.4f;
            var hk = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 400; i++)
            {
                Step(k);
                float z = k.transform.position.z - O.z;
                if (z > 40f + 3.5f && hk.Count < 150) hk.Add(k.transform.position.y);
            }
            int ok = Overshoots(hk);
            log.AppendLine($"Up a 15 cm kerb at 15 m/s: {ok} overshoot(s) {(ok <= 1 ? "PASS" : "FAIL")}, wheels down after {k.Grounded}");
            Clear();
        }

        static void TopSpeed()
        {
            Slab(new Vector3(0f, -0.5f, 1500f), new Vector3(60f, 1f, 3200f));
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, 0f));
            Step(j, 25);
            j.Throttle = 1f;
            string s = "Full throttle on flat:";
            for (int t = 1; t <= 20; t++) { Step(j, 50); if (t == 3 || t == 6 || t == 10 || t == 20) s += $" {t}s {j.Speed:F1} m/s;"; }
            s += $" drifted {j.transform.position.x - O.x:F2} m sideways, heading {j.Heading(0f):F1}°;";
            j.Boosting = true; Step(j, 500);
            s += $" +10 s boost {j.Speed:F1} m/s;";
            j.Boosting = false; j.Throttle = -1f;
            int k = 0; while (Vector3.Dot(j.Body.linearVelocity, j.transform.forward) > 1f && k < 1000) { Step(j); k++; }
            s += $" braking to a stop took {k * Dt:F1} s; wandered {Mathf.Abs(j.transform.position.x - O.x):F2} m sideways";
            log.AppendLine(s);
            Clear();
        }

        static void Climb(float deg)
        {
            float a = deg * Mathf.Deg2Rad, len = 400f;
            Vector3 up = new Vector3(0f, Mathf.Cos(a), -Mathf.Sin(a)), dir = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
            Slab(-up * 0.5f + dir * len * 0.5f, new Vector3(200f, 1f, len), deg);
            Jeep j = Spawn(dir * 20f + up * (-Jeep.Ground + 0.05f), 8f, deg);
            j.Throttle = 1f;
            string s = $"Climbing {deg:F0}° (starting on it at 8 m/s, full throttle):";
            for (int t = 1; t <= 8; t++)
            {
                Step(j, 50);
                float along = Vector3.Dot(j.Body.linearVelocity, dir);
                s += $" {t}s {along:F1} m/s";
                if (t == 2) s += $" [wheels {j.Grounded}, loads {L(j.LastLoad)}, pushes {L(j.LastPush)}, pitch {Pitch(j):F0}°]";
                s += ";";
            }
            log.AppendLine(s + (j.Upturned ? " ended UPTURNED" : ""));
            Clear();
        }

        static void Downhill()
        {
            float deg = 20f, a = deg * Mathf.Deg2Rad, len = 400f;
            Slab(new Vector3(0f, -Mathf.Sin(a) * len * 0.5f - 0.5f, Mathf.Cos(a) * len * 0.5f), new Vector3(40f, 1f, len), -deg);
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, 3f), 15f, -deg);
            j.Throttle = 1f;
            int airborne = 0, partial = 0; float maxUp = 0f;
            Vector3 n = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
            for (int i = 0; i < 400; i++)
            {
                Step(j);
                if (i < 50) continue;
                if (j.Grounded == 0) airborne++; else if (j.Grounded < 4) partial++;
                maxUp = Mathf.Max(maxUp, Vector3.Dot(j.Body.linearVelocity, n));
            }
            log.AppendLine($"Down a 20° slope at full throttle for 8 s: {airborne} steps with all wheels off, {partial} with some off (of 350), fastest motion away from the slope {maxUp:F2} m/s, speed at the end {j.Speed:F1} m/s");
            Clear();
        }

        static void Cliff()
        {
            Slab(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 100f));          // platform, edge at z = 50
            Slab(new Vector3(0f, -25.5f, 200f), new Vector3(200f, 1f, 300f));      // ground 25 m below
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, 20f), 18f);
            j.Throttle = 0.5f;
            string s = "Off a 25 m cliff at 18 m/s:";
            bool off = false; float t0 = 0f, t = 0f; bool landed = false;
            for (int i = 0; i < 400 && !landed; i++)
            {
                Step(j); t += Dt;
                if (!off && j.transform.position.z - O.z > 51f) { off = true; t0 = t; s += $" pitch at the edge {Pitch(j):F0}°;"; }
                if (off && (Mathf.Abs(t - t0 - 0.5f) < Dt * 0.5f || Mathf.Abs(t - t0 - 1.0f) < Dt * 0.5f || Mathf.Abs(t - t0 - 1.5f) < Dt * 0.5f))
                    s += $" {t - t0:F1}s later pitch {Pitch(j):F0}°;";
                if (off && t - t0 > 0.5f && j.Grounded > 0) { landed = true; s += $" landed after {t - t0:F1} s at pitch {Pitch(j):F0}°;"; }
            }
            Step(j, 150);
            log.AppendLine(s + $" 3 s later: pitch {Pitch(j):F0}°, roll {Roll(j):F0}°, upturned {j.Upturned}");
            Clear();
        }

        static void Turn()
        {
            Slab(new Vector3(0f, -0.5f, 0f), new Vector3(800f, 1f, 800f));
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, -300f), 25f);
            j.Throttle = 1f; Step(j, 50);
            j.Steer = 1f;
            float maxRoll = 0f, minUp = 1f;
            for (int i = 0; i < 200; i++) { Step(j); maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Roll(j))); minUp = Mathf.Min(minUp, j.transform.up.y); }
            Vector3 w = j.Body.angularVelocity;
            log.AppendLine($"Full lock at {25f} m/s for 4 s: body roll up to {maxRoll:F0}°, speed now {j.Speed:F1} m/s, turning {w.y * Mathf.Rad2Deg:F0}°/s, rolled over {(minUp < 0.2f)}");
            Clear();
        }

        /// <summary>Angle between where the jeep points and where it is going, degrees.</summary>
        static float Slide(Jeep j)
        {
            Vector3 v = j.Body.linearVelocity; v.y = 0f;
            Vector3 f = j.transform.forward; f.y = 0f;
            return v.magnitude < 1f ? 0f : Vector3.Angle(f, v);
        }

        static void Hold(Jeep j, float speed) { j.Throttle = Mathf.Clamp((speed - Vector3.Dot(j.Body.linearVelocity, j.transform.forward)) * 0.5f, -1f, 1f); }

        static void Ground(Jeep j, float grip, float loose, float drag)
        {
            for (int i = 0; i < 4; i++) { j.SurfGrip[i] = grip; j.SurfLoose[i] = loose; j.SurfDrag[i] = drag; }
        }

        /// <summary>A handbrake turn into a held drift, then letting go: it must slide wide, not spin round, and straighten after.</summary>
        static void Drift(float grip, float loose, string name)
        {
            Slab(new Vector3(0f, -0.5f, 0f), new Vector3(1500f, 1f, 1500f));
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, -500f), 22f);
            Ground(j, grip, loose, 0f);
            j.Throttle = 1f; Step(j, 50);
            j.Steer = 1f; j.Handbraking = true; Step(j, 35);
            float afterBrake = Slide(j);
            j.Handbraking = false;
            float worst = afterBrake, sum = 0f, minSpeed = j.Speed; int n = 0;
            float startYaw = j.transform.eulerAngles.y, turned = 0f, lastYaw = startYaw;
            for (int i = 0; i < 150; i++)       // 3 s holding the drift: power on, steering into the turn
            {
                Step(j);
                float s = Slide(j); worst = Mathf.Max(worst, s); sum += s; n++;
                minSpeed = Mathf.Min(minSpeed, j.Speed);
                float y = j.transform.eulerAngles.y; turned += Mathf.DeltaAngle(lastYaw, y); lastYaw = y;
            }
            float held = sum / n, driftNow = j.Drift, speedHeld = j.Speed;
            j.Steer = 0f;
            int settle = -1;
            for (int i = 0; i < 200; i++) { Step(j); if (settle < 0 && Slide(j) < 4f) settle = i; }
            bool ok = worst < 75f && held > 8f && settle >= 0 && !j.Upturned;
            log.AppendLine($"Handbrake drift on {name} at 22 m/s: {afterBrake:F0}° sideways after the handbrake, held {held:F0}° on average for 3 s (worst {worst:F0}°, drift {driftNow:F1}), " +
                           $"turned {turned:F0}°, speed {speedHeld:F1} m/s (lowest {minSpeed:F1}); straight again {(settle < 0 ? "never" : (settle * Dt).ToString("F2") + " s")} after letting go {(ok ? "PASS" : "FAIL")}");
            Clear();
        }

        /// <summary>Loose ground: how fast it gets, how it turns under power, and whether it can still be driven tidily.</summary>
        static void Loose(float grip, float loose, float drag, string name)
        {
            Slab(new Vector3(0f, -0.5f, 0f), new Vector3(2500f, 1f, 2500f));
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, -1000f), 0f);
            Ground(j, grip, loose, drag);
            j.Throttle = 1f; Step(j, 150); float v3 = j.Speed; Step(j, 500); float top = j.Speed;
            j.Steer = 0.6f;
            float worst = 0f;
            for (int i = 0; i < 200; i++) { Step(j); worst = Mathf.Max(worst, Slide(j)); }
            float power = worst; worst = 0f;
            for (int i = 0; i < 400; i++) { float t = i * Dt; Hold(j, 15f); j.Steer = Mathf.Sin(t * Mathf.PI) * 0.6f; Step(j); if (t > 3f) worst = Mathf.Max(worst, Slide(j)); }
            log.AppendLine($"On {name}: 3s {v3:F1} m/s, top {top:F1} m/s; full throttle with 0.6 steer slides up to {power:F0}°; gentle slalom at 15 m/s slides up to {worst:F0}° {(worst < 30f && !j.Upturned ? "PASS" : "FAIL")}");
            Clear();
        }

        static void Slalom()
        {
            Slab(new Vector3(0f, -0.5f, 0f), new Vector3(1500f, 1f, 1500f));
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, -600f), 20f);
            float maxSlide = 0f;
            for (int i = 0; i < 600; i++)
            {
                float t = i * Dt;
                Hold(j, 20f);
                j.Steer = Mathf.Sin(t * Mathf.PI) * 0.8f;          // left-right every 2 s
                Step(j);
                if (t > 1f) maxSlide = Mathf.Max(maxSlide, Slide(j));
            }
            bool ok = maxSlide < 20f && !j.Upturned;
            log.AppendLine($"Slalom at 20 m/s for 12 s: worst slide angle {maxSlide:F0}° {(ok ? "PASS" : "FAIL")}, speed {j.Speed:F1} m/s");
            Clear();
        }

        static void LaneChange()
        {
            Slab(new Vector3(0f, -0.5f, 0f), new Vector3(1500f, 1f, 1500f));
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, -600f), 30f);
            for (int i = 0; i < 50; i++) { Hold(j, 30f); Step(j); }
            for (int i = 0; i < 30; i++) { Hold(j, 30f); j.Steer = 0.7f; Step(j); }
            for (int i = 0; i < 30; i++) { Hold(j, 30f); j.Steer = -0.7f; Step(j); }
            j.Steer = 0f;
            float settled = -1f, maxSlide = 0f;
            for (int i = 0; i < 200; i++)
            {
                Hold(j, 30f); Step(j);
                float yawRate = Mathf.Abs(j.Body.angularVelocity.y) * Mathf.Rad2Deg, s = Slide(j);
                maxSlide = Mathf.Max(maxSlide, s);
                if (yawRate < 2f && s < 2f) { if (settled < 0f) settled = (i + 1) * Dt; }
                else settled = -1f;
            }
            bool ok = settled >= 0f && settled < 1f && !j.Upturned;
            log.AppendLine($"Lane change at 30 m/s: settled {(settled < 0f ? "never" : settled.ToString("F2") + " s")} after letting go {(ok ? "PASS" : "FAIL")}, worst slide {maxSlide:F0}°");
            Clear();
        }

        static void Circle()
        {
            Slab(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f));
            Jeep j = Spawn(new Vector3(0f, -Jeep.Ground + 0.05f, 0f), 0f);
            j.Steer = 1f;
            for (int i = 0; i < 300; i++) { Hold(j, 5f); Step(j); }
            float w = Mathf.Abs(j.Body.angularVelocity.y), r = w > 0.01f ? j.Speed / w : 999f;
            log.AppendLine($"Full lock at 5 m/s: turning circle radius {r:F1} m {(r < 8f ? "PASS" : "FAIL")}");
            Clear();
        }

        static void SideSlope()
        {
            float bank = 25f;
            Slab(new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 800f), 0f, bank);
            Vector3 up = Quaternion.Euler(0f, 0f, bank) * Vector3.up;
            Jeep j = Spawn(up * (-Jeep.Ground + 0.05f) + new Vector3(0f, 0f, -300f), 15f, 0f, bank);
            float maxRoll = 0f;
            for (int i = 0; i < 400; i++) { Hold(j, 15f); Step(j); maxRoll = Mathf.Max(maxRoll, Mathf.Abs(Roll(j))); }
            bool ok = !j.Upturned && maxRoll < 45f;
            log.AppendLine($"Along a 25° side slope at 15 m/s for 8 s: worst roll {maxRoll:F0}° {(ok ? "PASS" : "FAIL")}, slid {Mathf.Abs(j.transform.position.x - O.x):F1} m down the slope");
            Clear();
        }

        static void Float()
        {
            Slab(new Vector3(0f, -4.5f, 0f), new Vector3(200f, 1f, 200f));     // the bed, 4 m down
            Jeep j = Spawn(new Vector3(0f, 1.5f, 0f));
            float surface = O.y;
            j.WaterFn = (Vector3 p, out float s) => { s = surface; return true; };
            float lastMoving = 0f;
            for (int i = 0; i < 400; i++)
            {
                Step(j);
                if (Mathf.Abs(j.Body.linearVelocity.y) > 0.05f) lastMoving = (i + 1) * Dt;
            }
            string s = $"Dropped into 4 m deep water: afloat {j.Afloat}, centre {j.transform.position.y - surface:F2} m above the surface, bobbing stopped after {lastMoving:F1} s;";
            j.Throttle = 1f; Step(j, 400);
            log.AppendLine(s + $" paddling at full throttle for 8 s: {j.Speed:F1} m/s");
            Clear();
        }
    }
}
