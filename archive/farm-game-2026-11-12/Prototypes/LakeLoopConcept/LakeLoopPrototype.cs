// PROTOTYPE - NOT FOR PRODUCTION
// Question: If the player fills a bucket, pours it into a tank, pumps with a hose and launches
//           a rocket while the lake visibly drops, does the loop feel satisfying for 10+ minutes?
// Date: 2026-11-01
//
// How to run: open Assets/Scenes/SampleScene and press Play. This script builds the whole
// grey-box level at runtime (it only runs in a scene named "SampleScene" or "LakeLoopPrototype").
// Controls: WASD move, mouse look, Space jump, E pick up / put down / use, hold E to work the pump,
// left mouse fill or pour the carrier (spoon / cup / bucket), Esc frees the cursor (click to recapture).
// v2 (2026-11-01, after first playtest): longer ladder starting from a spoon; CSV play log written to
// <project>/PlaytestLogs/ (outside Assets).
//
// Standards are deliberately relaxed: hardcoded values, no architecture, one file.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;

namespace Prototypes.LakeLoop
{
    public static class LakeLoopBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Build()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene != "SampleScene" && scene != "LakeLoopPrototype") return;
            if (Object.FindAnyObjectByType<LakeLoopGame>() != null) return;
            new GameObject("LakeLoopGame").AddComponent<LakeLoopGame>();
        }
    }

    public class LakeLoopGame : MonoBehaviour
    {
        // ---- Tuning (hardcoded on purpose) ----
        const float LakeMaxLitres = 6000f;
        const float CreditsPerLitre = 1f;

        // The upgrade ladder, bought in order at the shop. Spoon is what you start with.
        static readonly string[] UpgradeNames = { "Cup (2 L)", "Bucket (10 L)", "Hand pump (5 L/s, hold E)", "Electric motor (20 L/s, automatic)", "Bigger tank (1,000 L)", "Industrial pump (100 L/s)", "Auto-launcher" };
        static readonly int[] UpgradeCosts =   { 5, 30, 100, 400, 600, 1500, 2500 };
        int nextUpgrade;

        // Hand carrier: spoon -> cup -> bucket.
        float BucketLitres = 0.5f;
        string carrierName = "Spoon";
        float FillSeconds = 0.3f, PourSeconds = 0.25f;
        float TankCapacity = 200f;
        float pumpRate;            // 0 until a pump is bought
        bool hasAutoLaunch;

        StreamWriter log;

        const float LakeSize = 30f;   // square basin, metres
        const float LakeDepth = 4f;
        const float ReachDistance = 3.5f;

        // ---- State ----
        float lakeLitres = LakeMaxLitres;
        float tankLitres;
        float bucketLitres;
        int credits;
        bool hasHandPump, hasElectric;
        bool holdingBucket;
        float actionTimer;
        string actionLabel;
        int bandsDrawn;
        int launches;
        float playSeconds;
        string toast; float toastTimer;

        // ---- Scene refs ----
        Transform player, cam, water, bucket, tank, tankFill, rocket, pump, pumpHandle, shop, launchPad;
        CharacterController cc;
        LineRenderer hoseIn, hoseOut;
        GameObject car;
        float pitch, yVel;
        bool rocketFlying; float rocketT;
        readonly List<Renderer> bucketWater = new List<Renderer>();

        Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        Transform Box(string name, Vector3 pos, Vector3 scale, Color c, Transform parent = null, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = Mat(c);
            if (!collider) Destroy(g.GetComponent<Collider>());
            return g.transform;
        }

        void Start()
        {
            var oldCam = Camera.main;
            if (oldCam != null) oldCam.gameObject.SetActive(false);

            Color grass = new Color(0.45f, 0.5f, 0.35f);
            Color mud = new Color(0.55f, 0.45f, 0.3f);      // bone / ochre lakebed
            Color orange = new Color(1f, 0.45f, 0.05f);    // employer safety orange = player machinery
            Color grey = new Color(0.55f, 0.55f, 0.58f);

            // Ground around a square basin (four slabs leave a hole in the middle).
            float half = LakeSize / 2f, outer = 40f;
            float slab = (outer * 2 - LakeSize) / 2f;
            Box("GroundN", new Vector3(0, -0.5f, half + slab / 2), new Vector3(outer * 2, 1, slab), grass);
            Box("GroundS", new Vector3(0, -0.5f, -half - slab / 2), new Vector3(outer * 2, 1, slab), grass);
            Box("GroundE", new Vector3(half + slab / 2, -0.5f, 0), new Vector3(slab, 1, LakeSize), grass);
            Box("GroundW", new Vector3(-half - slab / 2, -0.5f, 0), new Vector3(slab, 1, LakeSize), grass);
            // Basin walls and bed.
            Box("WallN", new Vector3(0, -LakeDepth / 2, half + 0.25f), new Vector3(LakeSize, LakeDepth, 0.5f), mud);
            Box("WallS", new Vector3(0, -LakeDepth / 2, -half - 0.25f), new Vector3(LakeSize, LakeDepth, 0.5f), mud);
            Box("WallE", new Vector3(half + 0.25f, -LakeDepth / 2, 0), new Vector3(0.5f, LakeDepth, LakeSize), mud);
            Box("WallW", new Vector3(-half - 0.25f, -LakeDepth / 2, 0), new Vector3(0.5f, LakeDepth, LakeSize), mud);
            // Ramp down the west side so the player can walk onto the exposed bed and back out.
            var ramp = Box("Ramp", new Vector3(-half + 3f, -LakeDepth / 2f, 0), new Vector3(7.4f, 0.3f, 3f), mud);
            ramp.rotation = Quaternion.Euler(0, 0, -33.7f);
            Box("Lakebed", new Vector3(0, -LakeDepth - 0.25f, 0), new Vector3(LakeSize, 0.5f, LakeSize), mud);

            // Something to reveal: a sunken car, roof ~1.2 m above the bed.
            car = Box("SunkenCar", new Vector3(-6, -LakeDepth + 0.6f, 4), new Vector3(2, 1.2f, 4), new Color(0.6f, 0.15f, 0.15f)).gameObject;
            Box("SunkenSign", new Vector3(7, -LakeDepth + 1.6f, -6), new Vector3(0.2f, 3.2f, 0.2f), grey);
            Box("SunkenSignBoard", new Vector3(7, -LakeDepth + 3.0f, -6), new Vector3(2f, 1f, 0.1f), new Color(0.9f, 0.85f, 0.2f));

            // Water: the only saturated blue in the world. No collider: you can't stand on it.
            water = Box("Water", Vector3.zero, new Vector3(LakeSize, 0.1f, LakeSize), new Color(0.1f, 0.45f, 0.95f), null, false);
            UpdateWater();

            // Station on the south shore: tank, launch pad, shop.
            float shoreZ = -half - 4f;
            tank = Box("Tank", new Vector3(4, 1.25f, shoreZ - 2), new Vector3(2, 2.5f, 2), grey);
            tankFill = Box("TankGauge", new Vector3(4, 0.05f, shoreZ - 0.95f), new Vector3(1.6f, 0.1f, 0.1f), new Color(0.1f, 0.45f, 0.95f), null, false);
            launchPad = Box("LaunchButton", new Vector3(8, 0.6f, shoreZ - 2), new Vector3(1, 1.2f, 1), orange);
            Box("Pad", new Vector3(14, 0.1f, shoreZ - 4), new Vector3(4, 0.2f, 4), grey);
            rocket = Box("Rocket", new Vector3(14, 2.2f, shoreZ - 4), new Vector3(1, 4f, 1), Color.white);
            shop = Box("ContractorShop", new Vector3(-4, 0.75f, shoreZ - 2), new Vector3(1.5f, 1.5f, 1f), new Color(0.2f, 0.75f, 0.7f));

            // Pump (hidden until bought) at the shore edge, hoses as straight lines (no physics).
            pump = Box("Pump", new Vector3(0, 0.5f, -half - 1.2f), new Vector3(1.2f, 1f, 1.2f), orange);
            pumpHandle = Box("PumpHandle", new Vector3(0, 0.9f, 0), new Vector3(0.15f, 0.8f, 0.15f), Color.black, pump, false);
            pump.gameObject.SetActive(false);
            hoseIn = MakeHose(); hoseOut = MakeHose();

            // Bucket starts on the ground near the tank.
            bucket = Box("Bucket", new Vector3(1.5f, 0.3f, shoreZ - 1), new Vector3(0.5f, 0.6f, 0.5f), orange);
            var bw = Box("BucketWater", new Vector3(0, 0.45f, 0), new Vector3(0.85f, 0.1f, 0.85f), new Color(0.1f, 0.45f, 0.95f), bucket, false);
            bucketWater.Add(bw.GetComponent<Renderer>());

            // Light (the URP template scene has one; add one only if missing).
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var l = new GameObject("Sun").AddComponent<Light>();
                l.type = LightType.Directional;
                l.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            // Player.
            var p = new GameObject("Player");
            p.transform.position = new Vector3(0, 1.1f, shoreZ - 5);
            cc = p.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f;
            player = p.transform;
            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam = camGo.transform;
            cam.SetParent(player, false);
            cam.localPosition = new Vector3(0, 0.7f, 0);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            bucket.localScale = new Vector3(0.12f, 0.06f, 0.35f); // it's a spoon
            Toast("Your employer has issued you a spoon. Empty the lake.", 6f);
            OpenLog();
        }

        LineRenderer MakeHose()
        {
            var g = new GameObject("Hose");
            var lr = g.AddComponent<LineRenderer>();
            lr.widthMultiplier = 0.12f;
            lr.positionCount = 2;
            lr.material = Mat(new Color(0.1f, 0.1f, 0.1f));
            lr.enabled = false;
            return lr;
        }

        void Toast(string msg, float t = 3f) { toast = msg; toastTimer = t; }

        float WaterY => -LakeDepth + LakeDepth * (lakeLitres / LakeMaxLitres);

        void UpdateWater()
        {
            water.gameObject.SetActive(lakeLitres > 0.5f);
            water.position = new Vector3(0, WaterY, 0);
            // Stain band each time another 10% has gone ("height is history").
            int bandsDue = Mathf.FloorToInt((1f - lakeLitres / LakeMaxLitres) * 10f);
            while (bandsDrawn < bandsDue && bandsDrawn < 10)
            {
                bandsDrawn++;
                Log("lake_band", $"{bandsDrawn * 10}% drained");
                float y = -LakeDepth * (bandsDrawn / 10f) + 0.02f;
                float h = LakeSize / 2f;
                Color stain = new Color(0.35f, 0.28f, 0.2f);
                Box("Stain", new Vector3(0, y, h - 0.01f), new Vector3(LakeSize, 0.08f, 0.05f), stain, null, false);
                Box("Stain", new Vector3(0, y, -h + 0.01f), new Vector3(LakeSize, 0.08f, 0.05f), stain, null, false);
                Box("Stain", new Vector3(h - 0.01f, y, 0), new Vector3(0.05f, 0.08f, LakeSize), stain, null, false);
                Box("Stain", new Vector3(-h + 0.01f, y, 0), new Vector3(0.05f, 0.08f, LakeSize), stain, null, false);
                if (bandsDrawn == 3) Toast("RADIO: \"Record quarter for Crystal Lake Holdings! Thank you, contractor.\"", 5f);
                if (bandsDrawn == 7) Toast("Something is surfacing out there...", 4f);
            }
        }

        void Update()
        {
            playSeconds += Time.deltaTime;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; return; }

            Look(mouse); Move(kb);
            Interact(kb, mouse);
            RunPump(kb);
            AnimateRocket();
            if (hasAutoLaunch && !rocketFlying && tankLitres >= TankCapacity * 0.95f) Launch();
            if (toastTimer > 0) toastTimer -= Time.deltaTime;

            tankFill.localScale = new Vector3(1.6f, Mathf.Max(0.05f, (tank.localScale.y - 0.1f) * tankLitres / TankCapacity), 0.1f);
            tankFill.position = new Vector3(tank.position.x, tankFill.localScale.y / 2f, tank.position.z + tank.localScale.z / 2f + 0.05f);
            foreach (var r in bucketWater) r.enabled = bucketLitres > 0.1f;
        }

        void Look(Mouse mouse)
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            Vector2 d = mouse.delta.ReadValue() * 0.1f;
            player.Rotate(0, d.x, 0);
            pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
            cam.localEulerAngles = new Vector3(pitch, 0, 0);
        }

        void Move(Keyboard kb)
        {
            Vector3 dir = Vector3.zero;
            if (kb.wKey.isPressed) dir += player.forward;
            if (kb.sKey.isPressed) dir -= player.forward;
            if (kb.dKey.isPressed) dir += player.right;
            if (kb.aKey.isPressed) dir -= player.right;
            float speed = kb.leftShiftKey.isPressed ? 7f : 4.5f;
            if (cc.isGrounded) { yVel = -1f; if (kb.spaceKey.wasPressedThisFrame) yVel = 5f; }
            yVel += Physics.gravity.y * Time.deltaTime;
            cc.Move((dir.normalized * speed + Vector3.up * yVel) * Time.deltaTime);
            if (player.position.y < -20f) { cc.enabled = false; player.position = new Vector3(0, 1.1f, -LakeSize / 2f - 9f); cc.enabled = true; }
        }

        Transform LookTarget(out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;
            if (Physics.Raycast(cam.position, cam.forward, out var hit, ReachDistance))
            { hitPoint = hit.point; return hit.transform; }
            return null;
        }

        bool LookingAtWater()
        {
            // Water has no collider: check the ray against the water plane instead.
            var plane = new Plane(Vector3.up, new Vector3(0, WaterY, 0));
            var ray = new Ray(cam.position, cam.forward);
            if (lakeLitres <= 0.5f || !plane.Raycast(ray, out float dist) || dist > ReachDistance + LakeDepth) return false;
            Vector3 p = ray.GetPoint(dist);
            if (Physics.Raycast(ray, out var hit, dist)) return false; // something solid in the way
            return Mathf.Abs(p.x) < LakeSize / 2f && Mathf.Abs(p.z) < LakeSize / 2f;
        }

        void Interact(Keyboard kb, Mouse mouse)
        {
            var target = LookTarget(out _);

            if (kb.eKey.wasPressedThisFrame)
            {
                if (holdingBucket) { DropBucket(); }
                else if (target == bucket) { holdingBucket = true; bucket.SetParent(cam, false); bucket.localPosition = new Vector3(0.45f, -0.4f, 0.8f); bucket.localRotation = Quaternion.identity; bucket.GetComponent<Collider>().enabled = false; }
                else if (target == launchPad) Launch();
                else if (target == shop) Buy();
            }

            // Bucket fill / pour, over a short hold so it has a little weight to it.
            if (holdingBucket && mouse.leftButton.isPressed)
            {
                bool canFill = bucketLitres < BucketLitres && LookingAtWater();
                bool canPour = bucketLitres > 0 && target == tank && tankLitres < TankCapacity;
                if (canFill || canPour)
                {
                    actionLabel = canFill ? "Filling..." : "Pouring...";
                    actionTimer += Time.deltaTime;
                    float need = canFill ? FillSeconds : PourSeconds;
                    bucket.localRotation = Quaternion.Euler(canPour ? 40f * actionTimer / need : 0, 0, 0);
                    if (actionTimer >= need)
                    {
                        if (canFill) { float take = Mathf.Min(BucketLitres, lakeLitres); lakeLitres -= take; bucketLitres = take; UpdateWater(); Log("fill"); }
                        else { float put = Mathf.Min(bucketLitres, TankCapacity - tankLitres); tankLitres += put; bucketLitres -= put; Log("pour"); }
                        actionTimer = 0; actionLabel = null; bucket.localRotation = Quaternion.identity;
                    }
                    return;
                }
            }
            actionTimer = 0; actionLabel = null;
            if (holdingBucket) bucket.localRotation = Quaternion.identity;
        }

        void DropBucket()
        {
            holdingBucket = false;
            bucket.SetParent(null, true);
            bucket.position = player.position + player.forward * 1.2f + Vector3.down * 0.7f;
            bucket.rotation = Quaternion.identity;
            bucket.GetComponent<Collider>().enabled = true;
        }

        void Buy()
        {
            if (nextUpgrade >= UpgradeNames.Length) { Toast("Nothing left to buy in this prototype."); return; }
            int cost = UpgradeCosts[nextUpgrade];
            if (credits < cost) { Toast($"{UpgradeNames[nextUpgrade]} costs {cost} credits."); return; }
            credits -= cost;
            string name = UpgradeNames[nextUpgrade];
            switch (nextUpgrade)
            {
                case 0: BucketLitres = 2f; carrierName = "Cup"; FillSeconds = 0.5f; PourSeconds = 0.4f;
                        bucket.localScale = new Vector3(0.25f, 0.3f, 0.25f); bucketLitres = 0;
                        Toast("Upgraded to a cup. Management is impressed.", 4f); break;
                case 1: BucketLitres = 10f; carrierName = "Bucket"; FillSeconds = 0.8f; PourSeconds = 0.6f;
                        bucket.localScale = new Vector3(0.5f, 0.6f, 0.5f); bucketLitres = 0;
                        Toast("A real bucket. You are going places.", 4f); break;
                case 2: hasHandPump = true; pumpRate = 5f; pump.gameObject.SetActive(true); hoseIn.enabled = hoseOut.enabled = true;
                        Toast("Hand pump installed at the shore. Hold E on it to pump into the tank.", 5f); break;
                case 3: hasElectric = true; pumpRate = 20f;
                        Toast("Electric motor fitted. The pump now runs by itself while the tank has room.", 5f); break;
                case 4: TankCapacity = 1000f; tank.localScale = new Vector3(3f, 3.5f, 3f); tank.position = new Vector3(tank.position.x, 1.75f, tank.position.z - 0.5f);
                        Toast("Tank expanded to 1,000 L.", 4f); break;
                case 5: pumpRate = 100f; pump.localScale = new Vector3(2.4f, 2f, 2.4f);
                        Toast("Industrial pump online. Listen to that lake go.", 5f); break;
                case 6: hasAutoLaunch = true;
                        Toast("Auto-launcher installed. Shipments go out the moment the tank is full.", 5f); break;
            }
            nextUpgrade++;
            Log("buy", name);
        }

        void OpenLog()
        {
            try
            {
                string dir = Path.Combine(Application.dataPath, "..", "PlaytestLogs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"lakeloop-{System.DateTime.Now:yyyyMMdd-HHmmss}.csv");
                log = new StreamWriter(path) { AutoFlush = true };
                log.WriteLine("seconds,event,detail,credits,tank_litres,lake_percent,carrier");
                Log("start", "v2");
                Debug.Log($"[LakeLoop] Play log: {Path.GetFullPath(path)}");
            }
            catch (System.Exception e) { Debug.LogWarning($"[LakeLoop] Could not open play log: {e.Message}"); }
        }

        void Log(string ev, string detail = "")
        {
            if (log == null) return;
            log.WriteLine($"{playSeconds:0.0},{ev},{detail.Replace(',', ';')},{credits},{tankLitres:0.0},{100f * lakeLitres / LakeMaxLitres:0.0},{carrierName}");
        }

        void OnDestroy()
        {
            if (log == null) return;
            Log("end", $"launches={launches}; upgrades={nextUpgrade}");
            log.Dispose(); log = null;
        }

        void RunPump(Keyboard kb)
        {
            if (!hasHandPump) return;
            hoseIn.SetPosition(0, pump.position);
            hoseIn.SetPosition(1, new Vector3(0, WaterY, -LakeSize / 2f + 1.5f));
            hoseOut.SetPosition(0, pump.position);
            hoseOut.SetPosition(1, tank.position + Vector3.up * 1.2f);

            bool working = hasElectric || (kb.eKey.isPressed && !holdingBucket && LookTarget(out _) == pump);
            float rate = pumpRate;
            if (working && lakeLitres > 0 && tankLitres < TankCapacity)
            {
                float amount = Mathf.Min(rate * Time.deltaTime, lakeLitres, TankCapacity - tankLitres);
                lakeLitres -= amount; tankLitres += amount; UpdateWater();
                pumpHandle.localPosition = new Vector3(0, 0.9f + Mathf.Sin(Time.time * (hasElectric ? 4f + pumpRate * 0.2f : 8f)) * 0.15f, 0);
            }
            else if (working && tankLitres >= TankCapacity && !hasElectric) Toast("Tank full. Launch a shipment.", 1f);
        }

        void Launch()
        {
            if (rocketFlying) return;
            if (tankLitres < 1f) { Toast("The tank is empty."); return; }
            int earned = Mathf.FloorToInt(tankLitres * CreditsPerLitre);
            credits += earned; tankLitres = 0; launches++;
            Log("launch", $"+{earned}");
            rocketFlying = true; rocketT = 0;
            Toast($"Shipment launched: +{earned} credits. Mars thanks you.", 4f);
        }

        void AnimateRocket()
        {
            if (!rocketFlying) return;
            rocketT += Time.deltaTime;
            var basePos = new Vector3(14, 2.2f, -LakeSize / 2f - 8f);
            rocket.position = basePos + Vector3.up * (rocketT * rocketT * 6f);
            if (rocketT > 4f) { rocketFlying = false; rocket.position = basePos; }
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            float pct = 100f * lakeLitres / LakeMaxLitres;
            GUI.Label(new Rect(16, 12, 600, 28), $"Credits: {credits}    Tank: {tankLitres:0.0}/{TankCapacity:0} L    Lake: {pct:0}%    Launches: {launches}", style);
            GUI.Label(new Rect(16, 38, 600, 28), holdingBucket ? $"{carrierName}: {bucketLitres:0.0}/{BucketLitres:0.0} L" : "", style);
            GUI.Label(new Rect(16, 64, 600, 28), $"Time played: {Mathf.FloorToInt(playSeconds / 60)}:{Mathf.FloorToInt(playSeconds % 60):00}", style);
            // Crosshair and contextual prompt.
            GUI.Label(new Rect(Screen.width / 2 - 5, Screen.height / 2 - 12, 20, 24), "+", style);
            string prompt = Prompt();
            if (!string.IsNullOrEmpty(prompt))
                GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height / 2 + 20, 400, 28), prompt, new GUIStyle(style) { alignment = TextAnchor.MiddleCenter });
            if (toastTimer > 0)
                GUI.Label(new Rect(Screen.width / 2 - 350, 90, 700, 30), toast, new GUIStyle(style) { alignment = TextAnchor.MiddleCenter });
        }

        string Prompt()
        {
            if (actionLabel != null) return actionLabel;
            if (cam == null) return null;
            var t = LookTarget(out _);
            if (!holdingBucket && t == bucket) return $"E: pick up {carrierName.ToLower()}";
            if (holdingBucket && bucketLitres < BucketLitres && LookingAtWater()) return "Hold left mouse: fill";
            if (holdingBucket && bucketLitres > 0 && t == tank) return "Hold left mouse: pour into tank";
            if (t == launchPad) return "E: launch shipment";
            if (t == shop) return nextUpgrade < UpgradeNames.Length ? $"E: buy {UpgradeNames[nextUpgrade]} for {UpgradeCosts[nextUpgrade]}" : "Shop: sold out";
            if (t == pump && !hasElectric) return "Hold E: pump";
            if (holdingBucket) return $"E: put down {carrierName.ToLower()}";
            return null;
        }
    }
}
