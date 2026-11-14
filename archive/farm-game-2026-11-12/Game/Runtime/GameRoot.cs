using System;
using System.IO;
using MarsNeedsWater.Simulation;
using Pose = MarsNeedsWater.Simulation.Pose;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Sim = MarsNeedsWater.Simulation.Simulation;

namespace MarsNeedsWater.Game
{
    /// <summary>Starts the game in the Greybox scene (the prototype keeps SampleScene).</summary>
    public static class GameBootstrap
    {
        public const string SceneName = "Greybox";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // Domain reload is off: unsubscribe before subscribing so hooks never pile up across Play presses.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Spawn(scene);

        static void Spawn(Scene scene)
        {
            if (scene.name != SceneName) return;
            if (UnityEngine.Object.FindAnyObjectByType<GameRoot>() != null) return;
            new GameObject("GameRoot").AddComponent<GameRoot>();
        }
    }

    /// <summary>Routes simulation log lines to the Unity console.</summary>
    public sealed class UnitySimLog : ISimLog
    {
        public void Info(string m) => Debug.Log("[Sim] " + m);
        public void Warn(string m) => Debug.LogWarning("[Sim] " + m);
        public void Error(string m) => Debug.LogError("[Sim] " + m);
    }

    /// <summary>The play log file: PlaytestLogs/ in the project folder in the Editor, per-user app data in builds.</summary>
    public sealed class FilePlayLogSink : IPlayLogSink
    {
        readonly StreamWriter _w;
        public string Path { get; }

        public FilePlayLogSink(string sessionId)
        {
            string dir = Application.isEditor
                ? System.IO.Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PlaytestLogs")
                : System.IO.Path.Combine(Application.persistentDataPath, "PlaytestLogs");
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, $"session_{sessionId}.csv");
            _w = new StreamWriter(Path, false);
        }

        public void WriteLine(string line) => _w.WriteLine(line);
        public void Flush() => _w.Flush();
        public void Dispose() => _w.Dispose();
    }

    /// <summary>
    /// The tick driver and composition root: loads the tuning table, builds the greybox, creates a fresh
    /// Simulation (clean start every Play), the player, the world view and the HUD, and feeds unscaled real
    /// frame time into the fixed 10 Hz accumulator. Pause stops ticks and freezes presentation (timeScale 0).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class GameRoot : MonoBehaviour
    {
        Sim _sim;
        GreyboxLevel _level;
        Planet _planet;
        PlayerController _player;
        WorldView _view;
        Hud _hud;
        PlayLog _playLog;
        bool _focusPause;

        void Start()
        {
            Time.timeScale = 1f;
            Prims.ClearCache();
            var old = Camera.main;
            if (old != null) old.gameObject.SetActive(false);

            var json = Resources.Load<TextAsset>("mvp_tuning");
            if (json == null) { Debug.LogError("mvp_tuning.json not found in a Resources folder."); enabled = false; return; }
            var tuning = JsonUtility.FromJson<TuningData>(json.text);

            // The round Earth first: it puts the farm frame at the world origin, so the level builds as before.
            _planet = new GameObject("Planet").AddComponent<Planet>();
            if (!_planet.Create()) { enabled = false; return; }
            _level = new GreyboxLevel();
            _level.Build();
            _level.Root.SetParent(_planet.Farm, true);
            WorldWaters.Register(_level.Data); // every sea and lake on Earth is a source too

            try { _sim = new Sim(tuning, _level.Data, new UnitySimLog()); }
            catch (TuningException ex) { Debug.LogError("Tuning table rejected: " + ex.Message); enabled = false; return; }

            _focusPause = !Application.isEditor; // On in builds, Off in the Editor (Game State Rule 9)
            Application.runInBackground = true;
            StartSession();
        }

        void StartSession()
        {
            string id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            try { _playLog = new PlayLog(_sim, new FilePlayLogSink(id), _sim.Log); }
            catch (Exception ex) { Debug.LogWarning("Play log disabled: " + ex.Message); _playLog = null; }

            // Presentation first, so SessionStarted reaches every listener.
            var playerGo = new GameObject("Player");
            _player = playerGo.AddComponent<PlayerController>();
            _player.Init(_sim, _level.Data.PlayerStart);
            Look.Apply(_player.Camera);
            _planet.Init(_player.transform);
            gameObject.AddComponent<MapView>().Init(_sim, _player.transform, _planet);
            _view = gameObject.AddComponent<WorldView>();
            _view.Init(_sim, _level, _player.HandsAnchor);
            _hud = gameObject.AddComponent<Hud>();
            _hud.Init(_sim, _player, this);
            if (_playLog != null) _sim.Bus.Subscribe(_playLog);

            _sim.BeforeTick = () => { _player.SendForTick(); _view.BeforeTick(); };
            _sim.NewGame(id);
            _view.AfterTick();
        }

        void Update()
        {
            if (_sim == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (_sim.Run == RunState.Running) Pause();
                else if (_sim.Run == RunState.Paused) Resume();
            }
            if (_sim.Run != RunState.Running) return;
            int n = _sim.Clock.Advance(Time.unscaledDeltaTime);
            for (int i = 0; i < n; i++)
            {
                _sim.Tick();
                _view.AfterTick();
            }
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && _focusPause && _sim != null && _sim.Run == RunState.Running) Pause();
        }

        public void Pause()
        {
            _player.CancelHolds();
            _player.SendForTick(); // flush CrankStop into the queue; applied on the first tick after resume
            _sim.Pause();
            Time.timeScale = 0f; // presentation freezes too; the simulation never reads timeScale
            PlayerController.LockCursor(false);
        }

        public void Resume()
        {
            _sim.Resume();
            Time.timeScale = 1f;
            PlayerController.LockCursor(true);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            _playLog?.Dispose();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void Quit()
        {
            _sim.EndSession(quitGame: true);
            _playLog?.Dispose();
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            _playLog?.Dispose();
            if (_sim != null) _sim.BeforeTick = null;
        }
    }
}
