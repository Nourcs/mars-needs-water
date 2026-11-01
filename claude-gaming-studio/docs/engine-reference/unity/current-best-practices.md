# Unity 6.3 LTS — Current Best Practices

**Last verified:** 2026-02-13

Modern Unity 6 patterns that may not be in the LLM's training data.
These are production-ready recommendations as of Unity 6.3 LTS.

---

## Project Setup

### Use Unity 6.3 LTS for Production
- **Supported Update releases** (6.4+): Latest features, supported only until the next one ships
- **LTS** (6.3): Production-ready, 2-year support (until Dec 2027)

### Choose the Right Render Pipeline
- **URP (Universal)**: Mobile, cross-platform, good performance ✅ Recommended for most games
- **HDRP (High Definition)**: High-end PC/console, photorealistic
- **Built-in**: Deprecated, avoid for new projects

---

## Scripting

### Use C# 9+ Features (Unity 6 Supports C# 9)

```csharp
// ✅ Record types for data
public record PlayerData(string Name, int Level, float Health);

// ✅ Init-only properties
public class Config {
    public string GameMode { get; init; }
}

// ✅ Pattern matching
var result = enemy switch {
    Boss boss => boss.Enrage(),
    Minion minion => minion.Flee(),
    _ => null
};
```

### Async/Await for Asset Loading

```csharp
// ✅ Modern async pattern
public async Task<GameObject> LoadEnemyAsync(string key) {
    var handle = Addressables.LoadAssetAsync<GameObject>(key);
    return await handle.Task;
}
```

### Use Source Generators for Serialization (Unity 6+)

```csharp
// ✅ Source-generated serialization (faster, less reflection)
[GenerateSerializer]
public partial struct PlayerStats : IComponentData {
    public int Health;
    public int Mana;
}
```

---

## DOTS/ECS (Production-Ready in Unity 6.3 LTS)

### Use ISystem (Not ComponentSystem)

```csharp
// ✅ Modern unmanaged ISystem (Burst-compatible)
public partial struct MovementSystem : ISystem {
    public void OnCreate(ref SystemState state) { }

    public void OnUpdate(ref SystemState state) {
        foreach (var (transform, speed) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRO<MoveSpeed>>()) {
            transform.ValueRW.Position += speed.ValueRO.Value * SystemAPI.Time.DeltaTime;
        }
    }
}
```

### Use IJobEntity for Parallel Jobs

```csharp
// ✅ IJobEntity (replaces IJobForEach)
[BurstCompile]
public partial struct DamageJob : IJobEntity {
    public float DeltaTime;

    void Execute(ref Health health, in DamageOverTime dot) {
        health.Value -= dot.DamagePerSecond * DeltaTime;
    }
}

// Schedule it
var job = new DamageJob { DeltaTime = SystemAPI.Time.DeltaTime };
job.ScheduleParallel();
```

---

## Input

### Use Input System Package (Not Legacy Input)

```csharp
// ✅ Input Actions (rebindable, cross-platform)
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour {
    private PlayerControls controls;

    void Awake() {
        controls = new PlayerControls();
        controls.Gameplay.Jump.performed += ctx => Jump();
    }

    void OnEnable() => controls.Enable();
    void OnDisable() => controls.Disable();
}
```

Create Input Actions asset in editor, generate C# class via inspector.

---

## UI

### Use UI Toolkit for Runtime UI (Production-Ready in Unity 6)

```csharp
// ✅ UI Toolkit (replaces UGUI for new projects)
using UnityEngine.UIElements;

public class MainMenu : MonoBehaviour {
    void OnEnable() {
        var root = GetComponent<UIDocument>().rootVisualElement;

        var playButton = root.Q<Button>("play-button");
        playButton.clicked += StartGame;

        var scoreLabel = root.Q<Label>("score");
        scoreLabel.text = $"High Score: {PlayerPrefs.GetInt("HighScore")}";
    }
}
```

**UXML** (UI structure) + **USS** (styling) = HTML/CSS-like workflow.

---

## Asset Management

### Use Addressables (Not Resources)

```csharp
// ✅ Addressables (async, memory-efficient)
using UnityEngine.AddressableAssets;

public async Task SpawnEnemyAsync(string enemyKey) {
    var handle = Addressables.InstantiateAsync(enemyKey);
    var enemy = await handle.Task;

    // Cleanup: release when destroyed
    Addressables.ReleaseInstance(enemy);
}
```

**Benefits:** Async loading, remote content delivery, better memory control.

---

## Rendering

### Use RenderGraph API for Custom Passes (URP/HDRP)

```csharp
// ✅ RenderGraph API (Unity 6+)
public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
    using (var builder = renderGraph.AddRasterRenderPass<PassData>("My Pass", out var passData)) {
        // Setup pass
        builder.SetRenderFunc((PassData data, RasterGraphContext context) => {
            // Execute commands
        });
    }
}
```

**Replaces:** Old `CommandBuffer.Execute()` pattern.

**This is URP's API.** `RecordRenderGraph` overrides URP's `ScriptableRenderPass`.
HDRP custom passes are a separate API: a `CustomPass` subclass, run from a Custom
Pass Volume, overrides `Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)`,
`Execute(CustomPassContext ctx)` and `Cleanup()` (HDRP 17.3, *Create a Custom
Pass in a C# script*:
https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/Custom-Pass-Scripting.html).

---

## Performance

### Use Burst Compiler + Jobs System

```csharp
// ✅ Burst-compiled job (massive performance gain)
[BurstCompile]
struct ParticleUpdateJob : IJobParallelFor {
    public NativeArray<float3> Positions;
    public NativeArray<float3> Velocities;
    public float DeltaTime;

    public void Execute(int index) {
        Positions[index] += Velocities[index] * DeltaTime;
    }
}

// Schedule
var job = new ParticleUpdateJob {
    Positions = positions,
    Velocities = velocities,
    DeltaTime = Time.deltaTime
};
job.Schedule(positions.Length, 64).Complete();
```

**20-100x faster** than equivalent C# code.

---

### Use GPU Instancing for Repeated Objects

```csharp
// ✅ GPU Instancing (thousands of objects, minimal draw calls)
Graphics.RenderMeshInstanced(
    new RenderParams(material),
    mesh,
    0,
    matrices // NativeArray<Matrix4x4>
);
```

---

## Memory Management

### Use NativeContainers (Not Managed Arrays in Jobs)

```csharp
// ✅ NativeArray (no GC, Burst-compatible)
NativeArray<int> data = new NativeArray<int>(1000, Allocator.TempJob);
// ... use in job
data.Dispose(); // Manual cleanup required

// ✅ Or use using statement
using var data = new NativeArray<int>(1000, Allocator.TempJob);
// Auto-disposed
```

---

## Multiplayer

### Use Netcode for GameObjects (Official)

```csharp
// ✅ Unity's official netcode
using Unity.Netcode;

public class Player : NetworkBehaviour {
    private NetworkVariable<int> health = new NetworkVariable<int>(100);

    [ServerRpc]
    public void TakeDamageServerRpc(int damage) {
        health.Value -= damage;
    }
}
```

**Replaces:** UNet (deprecated), MLAPI (renamed to Netcode for GameObjects).

---

## Testing

### Use Unity Test Framework (NUnit-based)

```csharp
// ✅ Play Mode Test
[UnityTest]
public IEnumerator Player_TakesDamage_HealthDecreases() {
    var player = new GameObject().AddComponent<Player>();
    player.Health = 100;

    player.TakeDamage(25);
    yield return null; // Wait one frame

    Assert.AreEqual(75, player.Health);
}
```

---

## Command Line — Tests

Documented (Unity Manual, *Run tests from the command line*; Test Framework
command-line reference):
`<Unity editor> -runTests -batchmode -projectPath <path> -testPlatform <platform> -testResults <file>`.
`-batchmode` removes the need for user input. `-testPlatform` takes `EditMode`,
`PlayMode` (Play Mode tests in the Editor) or a player platform; with no value,
tests run in Edit Mode. `-testResults` defaults to the project root, and the
file follows NUnit's XML format: the `<test-run>` element carries
`testcasecount`, `result`, `passed` and `failed`, and each `<test-case>` a
`fullname` and a `result` of `Passed`, `Failed`, `Inconclusive` or `Skipped`.
Unity defines no common exit codes for test runs.

Observed on Unity 6000.3.23f1 — not in the docs:
- `-runTests` exits 0 when every test passes and 2 when one fails; 1 is a
  compile error (no results file is written), 3 a failed run, 4 an unknown
  `-testPlatform`. It exits by itself — no `-quit`.
- Tests outside `Assets/` are never compiled: the run reports
  `testcasecount="0"`, `result="Passed"` and exits 0 — a false pass.
- Without the `com.unity.test-framework` package, `-runTests` hangs rather
  than failing.
- `Unity` on `PATH` may be Unity's separate CLI, not the editor; it rejects
  `-batchmode` and `-version` with exit 2. Name the editor executable by its
  full path.
- `-batchmode -quit -projectPath . -logFile -` exits 1 on a compile error and
  0 when clean.

Sources:
- Run tests from the command line: https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/run-tests-from-command-line.html
- Test Framework command-line arguments: https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html
- NUnit test result XML format: https://docs.nunit.org/articles/nunit/technical-notes/usage/Test-Result-XML-Format.html

---

## Debugging

### Use Logging Best Practices

```csharp
// ✅ Structured logging (Unity 6+)
using UnityEngine;

Debug.Log($"Player {playerName} scored {score} points");

// ✅ Conditional compilation for debug code
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    Debug.DrawRay(transform.position, direction, Color.red);
#endif
```

---

## Summary: Unity 6 Tech Stack

| Feature | Use This (2026) | Avoid This (Legacy) |
|---------|------------------|----------------------|
| **Input** | Input System package | `Input` class |
| **UI** | UI Toolkit | UGUI (Canvas) |
| **ECS** | ISystem + IJobEntity | ComponentSystem |
| **Rendering** | URP + RenderGraph | Built-in pipeline |
| **Assets** | Addressables | Resources |
| **Jobs** | Burst + IJobParallelFor | Coroutines for heavy work |
| **Multiplayer** | Netcode for GameObjects | UNet |

---

**Sources:**
- https://docs.unity3d.com/6000.0/Documentation/Manual/BestPracticeGuides.html
- https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/index.html
- https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/index.html


---

## 6.3 LTS → 6.6

**Last verified:** 2026-11-01

New or changed practices introduced in Unity 6.4–6.6. These apply only if the
project runs on 6.4+; the pin is still 6.3 LTS (see VERSION.md), so check the
installed editor before using a 6.4+ API.

Sources: UG64 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity64 ·
UG65 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity65 ·
UG66 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity66 ·
WN64 = https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity64 ·
WN65 = https://docs.unity.com/en-us/engine/6000.5/manual/whats-new/unity65 ·
WN66 = https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity66 ·
DR66 = https://docs.unity.com/en-us/engine/6000.6/manual/programming-environment/code-reload-serialization/code-reload/code-reloading-editor/domain-reloading

---

### Project Setup

**Pick URP, not Built-In.** Built-In RP is deprecated as of 6.5 (supported through
6.7 LTS). — WN65. Unity's migration page:
https://docs.unity.com/en-us/engine/6000.5/manual/render-pipelines/universal-render-pipeline/introduction/installing-and-configuring-urp/upgrading-from-birp (linked from WN65, not fetched).

**Declare packages you use directly.** Since 6.5 URP/SRP Core no longer brings in
`com.unity.ugui` (UG65), since 6.6 not `com.unity.modules.terrain` (UG66), and since
6.4 packages using Physics must declare `com.unity.modules.physics` (UG64).

### Scripting — write code that survives no domain reload (6.6 default)

New 6.6 projects enter Play mode with **Reload Scene only**; Unity recommends keeping
domain reload off (WN66). Statics and static events then persist between Play
sessions (DR66). Pattern from DR66:

```csharp
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

public partial class ScoreKeeper : MonoBehaviour   // must be partial
{
    [AutoStaticsCleanup]   public static int score = 0;          // reset on entering Play mode
    [NoAutoStaticsCleanup] public static int nextId = 0;         // only reset on domain reload
}
```
— DR66 (adapted names). DR66's table: events/delegates, collections of user objects
and references to Unity objects → `[AutoStaticsCleanup]`; ID counters and cached
immutable GUI resources → `[NoAutoStaticsCleanup]`. Non-`readonly` field initializers
are re-evaluated each Play transition; `readonly` collections get `Clear()`. Setup
that isn't a field initializer goes in an `[OnEnteringPlayMode]` method. — DR66

Alternative: manual reset in `[OnExitingPlayMode]` (DR66 recommends exit over entry
unless Edit-mode code also changes the static). — DR66

Turn on the analyzer to find missed statics (UAL0010/UAL0013 missing attribute,
UAL0012 missing `partial`) via a `.globalconfig` next to the asmdef — DR66:
```
is_global = true
build_property.UnityEnableAutoStaticsCleanupCodeGen = true
build_property.UnityEnableAutoStaticsCleanupAnalysis = true
```
(DR66 shows the analysis line set to `false`, its default; `true` here is our
choice.) Project Auditor also emits `UAL` warnings in 6.6. — WN66

`RuntimeInitializeOnLoadMethod` / `SubsystemRegistration` with domain reload off:
NOT SOURCEABLE — not stated at DR66.

### Scripting — other

- **Use `EntityId`, never `int`, for object identity** (`GetEntityId()`); don't cast,
  hash, sort or sign-test it. — WN64, UG65
- **Use `GetComponent<T>()` / `AddComponent<T>()`**; the property shortcuts are gone. — UG65
- **Serialize dictionaries directly** with `[SerializeField] Dictionary<TKey,TValue>`
  on MonoBehaviour / ScriptableObject / `[Serializable]` types (6.6). — WN66
- **Mark `[SerializeReference]` classes and all ancestors `[Serializable]`.** — WN64
- **Use lifecycle attributes instead of `EditorApplication.playModeStateChanged`**
  (`[OnEnteringPlayMode]`, `[OnExitingPlayMode]`). `[OnCodeInitializing]` runs before
  assets are fully loaded, earlier than `InitializeOnLoad` / `RuntimeInitializeOnLoad`. — WN65
- **Gate debug code with the new symbols:** `UNITY_ENABLE_CHECKS`,
  `UNITY_INCLUDE_INSTRUMENTATION`, `Debug.isDebugBuild`; choose Release for shipping
  and Checked/Debug during development in **Player > Managed Code Variant**. — UG66
- **Burst is a built-in module** (no package install) in 6.6; Unity Mathematics is
  built in from 6.5 with implicit conversions to/from `UnityEngine` types. — WN66, WN65

### Rendering (URP)

- **Custom passes: Render Graph only** (Compatibility Mode removed in 6.4). — UG64
- **Don't rely on dynamic batching** (obsolete 6.6). — UG66
- **Mobile:** URP on-tile post-processing and **Tile-Only Mode** on the URP Renderer
  (6.5). — WN65
- **Lightmaps:** prefer Unity Compute Light Baker or Progressive GPU over the
  deprecated Progressive CPU Lightmapper; pick the default in **Graphics > Light
  Baker** (6.6). — WN66, UG66
- **To get Rendering Debugger / Frame Debugger support in a player build** set Managed
  Code Variant to Checked (6.6). — UG66

### Input

- Input System projects can use `OnMouseDown` / `OnMouseDrag` / `OnMouseUp` on
  MonoBehaviours (6.4). — WN64
- Gamepad rumble on Android: `Gamepad.SetMotorSpeeds` (6.6). — WN66
- NOT SOURCEABLE — any change to recommended Input System workflow, not stated at
  WN64/WN65/WN66.

### UI

- **UI Toolkit:** use **Panel Renderer** for new in-scene UI (UI Document still
  works) (6.5); custom controls use `[UxmlElement]`/`[UxmlAttribute]` (Factory/Traits
  removed 6.6). — WN65, UG66
- **UI Toolkit 6.6 styling:** `backdrop-filter` and `drop-shadow` USS filters. — WN66
- **UGUI 6.6:** **SafeArea** component for `Screen.safeArea`; `LayoutElement`
  **Max Width/Max Height**; Content Size Fitter **Clamped** mode; TMP `SetText()`
  accepts `ReadOnlySpan`. — WN66
- **UGUI 6.5:** **Raycast Receiver** component for invisible clickable areas. — WN65

### Assets / Builds

- **Content Directories** (6.6) replace the AssetBundle content build system: define
  root assets, load with the `Loadable` API, build with
  `BuildPipeline.BuildContentDirectory`; Addressables supports it via a **Content
  Directory** schema. — WN66
- **Build Analysis window** (6.6) reads reports from `Library/BuildHistory`
  (`BuildHistory` API). — WN66
- **Project Auditor** is built into the Editor from 6.4. — WN64
- **Editor log** is `ProjectName/Logs/Editor.log` from 6.5. — UG65

### Physics / Multiplayer

- 3D PhysX: NOT SOURCEABLE — no new 3D physics practices stated at WN64, WN65, WN66.
  (WN65's Direct Solver and parallel scheduling are for the DOTS **Unity Physics**
  package, not PhysX.)
- Netcode for GameObjects: NOT SOURCEABLE — not stated at WN65, WN66, UG66.
