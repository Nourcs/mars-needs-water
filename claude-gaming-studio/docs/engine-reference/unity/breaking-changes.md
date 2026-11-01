# Unity 6.3 LTS — Breaking Changes

**Last verified:** 2026-02-13

This document tracks breaking API changes and behavioral differences between Unity 2022 LTS
(likely in model training) and Unity 6.3 LTS (current version). Organized by risk level.

## HIGH RISK — Will Break Existing Code

### Entities/DOTS API Complete Overhaul
**Versions:** Entities 1.0+ (Unity 6.0+)

```csharp
// ❌ OLD (pre-Unity 6, GameObjectEntity pattern)
public class HealthComponent : ComponentData {
    public float Value;
}

// ✅ NEW (Unity 6+, IComponentData)
public struct HealthComponent : IComponentData {
    public float Value;
}

// ❌ OLD: ComponentSystem
public class DamageSystem : ComponentSystem { }

// ✅ NEW: ISystem (unmanaged, Burst-compatible)
public partial struct DamageSystem : ISystem {
    public void OnCreate(ref SystemState state) { }
    public void OnUpdate(ref SystemState state) { }
}
```

**Migration:** Follow Unity's ECS migration guide. Major architectural changes required.

---

### Input System — Legacy Input Deprecated
**Versions:** Unity 6.0+

```csharp
// ❌ OLD: Input class (deprecated)
if (Input.GetKeyDown(KeyCode.Space)) { }

// ✅ NEW: Input System package
using UnityEngine.InputSystem;
if (Keyboard.current.spaceKey.wasPressedThisFrame) { }
```

**Migration:** Install Input System package, replace all `Input.*` calls with new API.

---

### URP/HDRP Renderer Feature API Changes
**Versions:** Unity 6.0+

```csharp
// ❌ OLD: ScriptableRenderPass.Execute signature
public override void Execute(ScriptableRenderContext context, ref RenderingData data)

// ✅ NEW: Uses RenderGraph API
public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
```

**Migration:** Update custom render passes to use RenderGraph API.

---

## MEDIUM RISK — Behavioral Changes

### Addressables — Asset Loading Returns
**Versions:** Unity 6.2+

Asset loading failures now throw exceptions by default instead of returning null.
Add proper exception handling or use `TryLoad` variants.

```csharp
// ❌ OLD: Silent null on failure
var handle = Addressables.LoadAssetAsync<Sprite>("key");
var sprite = handle.Result; // null if failed

// ✅ NEW: Throws on failure, use try/catch or TryLoad
try {
    var handle = Addressables.LoadAssetAsync<Sprite>("key");
    var sprite = await handle.Task;
} catch (Exception e) {
    Debug.LogError($"Failed to load: {e}");
}
```

---

### Physics — Default Solver Iterations Changed
**Versions:** Unity 6.0+

Default solver iterations increased for better stability.
Check `Physics.defaultSolverIterations` if you rely on old behavior.

---

## LOW RISK — Deprecations (Still Functional)

### UGUI (Legacy UI)
**Status:** Deprecated but supported
**Replacement:** UI Toolkit

UGUI still works but UI Toolkit is recommended for new projects.

---

### Legacy Particle System
**Status:** Deprecated
**Replacement:** Visual Effect Graph (VFX Graph)

---

### Old Animation System
**Status:** Deprecated
**Replacement:** Animator Controller (Mecanim)

---

## Platform-Specific Breaking Changes

### WebGL
- **Unity 6.0+**: WebGPU is now the default (WebGL 2.0 fallback available)
- Update shaders for WebGPU compatibility

### Android
- **Unity 6.0+**: Minimum API level raised to 24 (Android 7.0)

### iOS
- **Unity 6.0+**: Minimum deployment target raised to iOS 13

---

## Migration Checklist

When upgrading from 2022 LTS to Unity 6.3 LTS:

- [ ] Audit all DOTS/ECS code (complete rewrite likely needed)
- [ ] Replace `Input` class with Input System package
- [ ] Update custom render passes to RenderGraph API
- [ ] Add exception handling to Addressables calls
- [ ] Test physics behavior (solver iterations changed)
- [ ] Consider migrating UGUI to UI Toolkit for new UI
- [ ] Update WebGL shaders for WebGPU
- [ ] Verify minimum platform versions (Android/iOS)

---

**Sources:**
- https://docs.unity3d.com/6000.0/Documentation/Manual/upgrade-guides.html
- https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/upgrade-guide.html


---

## 6.3 LTS → 6.6

**Last verified:** 2026-11-01

Changes that affect a project moving from Unity 6.3 LTS (6000.3) to 6.6 (6000.6),
filtered for a 3D URP C# project (Input System, UGUI / UI Toolkit, PhysX, Netcode
for GameObjects later). Every item cites the Unity page it came from, fetched
2026-11-01. Platform-only notes (Android, iOS, XR, QNX) are mostly left out; see
the upgrade guides for those.

Sources used throughout:
- UG64 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity64
- UG65 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity65
- UG66 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity66
- WN64 = https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity64
- WN65 = https://docs.unity.com/en-us/engine/6000.5/manual/whats-new/unity65
- WN66 = https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity66
- DR66 = https://docs.unity.com/en-us/engine/6000.6/manual/programming-environment/code-reload-serialization/code-reload/code-reloading-editor/domain-reloading

---

### 6.4 (6000.4)

#### HIGH RISK — Will Break Existing Code

**URP Compatibility Mode fully removed.** Compatibility Mode is removed for custom
render passes and the `URP_COMPATIBILITY_MODE` scripting define is gone. Custom
passes must use the render graph system (`RecordRenderGraph`). — UG64, WN64

**`LightShadowCasterMode` enum renamed.** `NonLightmappedOnly` → `ShadowMask`,
`Everything` → `DistanceShadowMask`. Unity says scripts are updated automatically. — UG64

**`IVolume` interface removed** (SRP Core). Custom volume types implement
visualization with the new `VolumeGizmoDrawer` class. — UG64

#### MEDIUM RISK — Behavioral Changes

**`Object.Destroy` now calls `OnDisable` on the whole descendant hierarchy**, not
just the GameObject and its direct children (matching `DestroyImmediate`). Remove
any workaround that propagated `OnDisable` manually, or it will run twice. — UG64

**`InstanceID` methods deprecated in favor of `EntityId`.** `EntityId` cannot be
represented by a single `int`: do not cast it to/from `int`, use
`Object.GetHashCode` to get one, round-trip it through `ToString`/`int.Parse`,
sort by it for creation order, or rely on its sign. — WN64
(These become compile errors in 6.5; see below.)

**Asset Database artifact dependencies narrowed.** Dependents reimport only when a
dependency's import *result* changes. `ScriptedImporter`/`AssetPostprocessor` code
that relied on extra reimports must declare `AssetImportContext.DependsOnSourceAsset`
/ `DependsOnArtifact` explicitly. — UG64

**SRP Core's dependency on `com.unity.modules.physics` is now soft.** Physics stays
enabled by default; if you disable it, local Volumes stop working. Packages that use
Physics and depend on SRP Core must declare `com.unity.modules.physics` themselves. — UG64, WN64

**`[SerializeReference]` validation.** Unity now warns if a class assigned to a
`[SerializeReference]` field, or any ancestor, lacks `[Serializable]`. — WN64

#### LOW RISK — Deprecations / Workflow

- Project Auditor is built into the Editor; `CodeModuleInstructionAnalyzer`,
  `InstructionAnalysisContext` and related types are now internal. — UG64
- PVRTC texture compression removed (use ASTC or ETC). — WN64
- New projects hide the classic platform list in Build Profiles
  (**Project Settings > Editor > Build Profiles > Hide Classic Platforms**). — WN64
- New Animator Controllers have **Evaluate Entry Transitions On Start** on by default
  (off for existing ones). — WN64
- HoloLens 2 support deprecated (migrate to OpenXR). — UG64

---

### 6.5 (6000.5)

#### HIGH RISK — Will Break Existing Code

**`InstanceID` APIs removed → `EntityId` (64-bit).** Integer `InstanceID` APIs such
as `Object.GetInstanceID`, `Resources.InstanceIDToObject` and `Selection.instanceIDs`
now produce compile errors. — WN65

```csharp
// ❌ OLD (6.4 and earlier)
int instanceID = go.GetInstanceID();

// ✅ NEW (6.5+)
EntityId entityId = go.GetEntityId();
```
— UG65. `EntityId` is now 8 bytes; truncating to 32 bits loses version data. Change
`int` variables and dictionary keys to `EntityId`; do not key dictionaries on
`EntityId.GetHashCode()`; parse serialized strings as `ulong`, not `int`
(`EntityId.ToString()` stored 32 bits in 6.2–6.4, 64 bits in 6.5); remove
`[StructLayout(LayoutKind.Explicit)]` overlaps or pointer casts between `int` and
`EntityId`. — UG65

**Obsolete GameObject/Component shortcut properties removed.** `GameObject.rigidbody`,
`.camera`, `.light`, `.renderer`, `.collider`, `.audio`, `.animation`, etc. (and the
same on `Component`), plus `GameObject.AddComponent(string)`, are gone. Use
`GetComponent<T>()` / `AddComponent<T>()`. — UG65, WN65

**Obsolete Entities APIs removed** (only if using ECS): `Entities.ForEach` →
`IJobEntity` / `SystemAPI.Query`; `Job.WithCode` → `IJob`; `IAspect` → component and
`EntityQuery` APIs. — UG65

**Obsolete `ModelImporter` APIs are now compile errors** (editor scripts only), e.g.
`optimizeMesh` → `optimizeMeshPolygons`/`optimizeMeshVertices`,
`isFileScaleUsed` → `useFileScale`. — UG65

**Render Pipeline Core no longer pulls in uGUI.** `com.unity.ugui` was removed as a
dependency of `com.unity.render-pipelines.core`. If your UGUI usage only worked
because URP brought it in, add `com.unity.ugui` in Package Manager. — UG65

#### MEDIUM RISK — Behavioral Changes

**Built-In Render Pipeline deprecated.** "Deprecated and will be obsolete in a future
release"; supported with bug fixes through the full Unity 6.7 LTS lifecycle. — WN65, UG65

**Legacy Render Graph API obsolete** — affects only custom SRPs; URP/HDRP projects on
the native-render-pass Render Graph API are unaffected. — UG65

**Project-local `Editor.log`.** Each project now writes `ProjectName/Logs/Editor.log`.
Use `Application.consoleLogPath` in scripts, `-logFile` in CI, or **Use Global Editor
Log** / `-useGlobalLog` for the old location. — UG65

**Base Class Library now ships `System.Text.Json`, `System.Collections.Immutable`,
`System.Reflection.Metadata`, `System.Runtime.CompilerServices.Unsafe`.** Unity's
internal copies replace user-supplied ones in builds, even if versions differ. — UG65

**UI Toolkit: Advanced Text Generator is the default text system.** — WN65

**Read/Write now required** for Particle System Shape and Terrain Paint Detail
textures; the build no longer enables it automatically. — WN65

**Rendering Debugger moved to UI Toolkit**; uGUI-based custom debugger control classes
removed. `DebugUI` code is unaffected. — UG65

#### LOW RISK — Deprecations (Still Functional)

- Dynamic batching deprecated (removed in 6.6, below). — WN65
- Shader Graph Reflection Probe Node deprecated (Custom Function Node with
  `SHADERGRAPH_REFLECTION_PROBE(ViewDir, Normal, LOD)` for URP Forward/Deferred). — UG65
- UI Toolkit: **Panel Renderer** component replaces **UI Document** for adding UI to
  a scene; "The UI Document component continues to work." — WN65
- Edit/Play mode callbacks `OnEnteringPlayModeAttribute` / `OnExitingPlayModeAttribute`
  (`UnityEngine`) and `OnEnteringEditModeAttribute` / `OnExitingEditModeAttribute`
  (`UnityEditor.Scripting.LifecycleManagement`) "replace subscribing to
  `EditorApplication.playModeStateChanged`". — WN65
- 2D only: `LowLevelPhysics2D` renamed `PhysicsCore2D`, namespace
  `UnityEngine.LowLevelPhysics2D` → `Unity.U2D.Physics` (auto-migrated). — UG65
- Android minimum API level 26; x86-64 removed. iOS ReplayKit removed. VR Module and
  Magic Leap removed. — UG65

---

### 6.6 (6000.6)

#### HIGH RISK — Will Break Existing Code

**Domain reload off by default on entering Play mode (new projects).** New projects
default to **Enter Play Mode Settings = Reload Scene only** instead of **Reload Domain
and Scene**. Unity recommends leaving it off to prepare for "the forthcoming removal
of Mono and the domain reload mechanism in future Unity versions." — WN66
With it off: static variables keep their values between Play sessions; static events
keep stale subscribers; non-serialized fields keep Play-mode values back in Edit mode;
static constructors run once per Editor session. — DR66
*Inference:* this hits beginner code hardest (static singletons, static event buses,
static score counters) because bugs appear only on the second Play press.
Fix with `[AutoStaticsCleanup]` / `[NoAutoStaticsCleanup]` (class must be `partial`)
or `[OnEnteringPlayMode]` / `[OnExitingPlayMode]` reset methods — see
best-practices-append.md. — DR66

**Cinemachine 3 is a core package; Cinemachine 2 projects auto-upgrade to 3**, which
has a different API and data format. — UG66

**Rendering Debugger legacy state APIs are compile errors** (`DebugState`,
`DebugState<T>`, `DebugStateBool`…, legacy `DebugUIDrawer`, `DebugStateAttribute`,
`LegacyStyles`). Use `ISerializedDebugDisplaySettings` and override
`DebugUI.Widget.Create()`. — UG66

**UI Toolkit legacy UXML Factory/Traits removed.** Use `[UxmlElement]` /
`[UxmlAttribute]`. `DefaultEventSystem.LegacyInputProcessor` removed;
`UIToolkitInputConfiguration.SetRuntimeInputBackend` is now a compile error — delete
the call (it had no effect). — UG66

#### MEDIUM RISK — Behavioral Changes

**Dynamic batching removed / obsolete.** Use another draw-call optimization method. — UG66, WN66

**`UNITY_64` and `DEVELOPMENT_BUILD` symbols deprecated** (analyzer warnings
`UAC0008`, `UAC0009`; errors under warnings-as-errors; hard errors in Unity 6.8).
New per-platform **Managed Code Variant** Player setting. Replacements:
`UNITY_ENABLE_CHECKS` (safety checks), `UNITY_INCLUDE_INSTRUMENTATION` (profiling /
diagnostic logging), `Debug.isDebugBuild` (development-only runtime behavior),
`IntPtr.Size` runtime check (instead of `UNITY_64`). `BuildOptions.ForceEnableAssertions`
is `[Obsolete]`. — UG66

**URP debug tooling now gated by the code variant.** Debug overlays, Rendering
Debugger resources, Render Graph Viewer, Frame Debugger support and the Volume panel
are gated by `UNITY_ENABLE_CHECKS`; Release builds no longer ship debug-display
shader variants. To get old Development Build diagnostics, set the variant to
**Checked** (or **Debug**). — UG66

**Render Pipeline Core no longer depends on `com.unity.modules.terrain`.** New
projects still include it; add it in Package Manager > Built-in if you relied on it
indirectly. — UG66

**YAML format change.** Word wrapping disabled; **Reduce Version Control Noise**
removed. Expect unrelated YAML diffs; `AssetDatabase.ForceReserializeAssets` once to
settle them. — UG66

**`Dictionary<TKey,TValue>` fields with `[SerializeField]` are now serialized.**
Existing wrapper solutions keep working; migrate by adding a new field, copying data,
then deleting the old one (deleting first loses data). — UG66, WN66

**Surface shader codegen uses the caching preprocessor** (Built-In RP shaders). — UG66

**Hierarchy window (new) enabled by default**; legacy window can be re-enabled. — WN66

#### LOW RISK — Deprecations (Still Functional)

- Progressive CPU Lightmapper deprecated → Unity Compute Light Baker or Progressive
  GPU Lightmapper; bake results may differ. — UG66, WN66
- Intel (x86_64) macOS Editor/Players deprecated. — WN66
- Entities managed components and Entities Hierarchy window deprecated. — WN66
- Android: Legacy/Round icons removed (Adaptive only); minimum GLES 3.1;
  `PlayerSettings.openGLRequireES31` deprecated. — UG66
- WebGPU no longer experimental. — WN66

---

### Areas checked with no sourced change

- PhysX / 3D `Rigidbody` / `Physics` API: NOT SOURCEABLE — no 3D PhysX API or
  behavior changes stated at UG64, UG65, UG66, WN64, WN65, WN66. Only the optional
  physics module (6.4, UG64) and Unity Physics (DOTS) changes (WN65, UG66) are listed.
- Input System package API: NOT SOURCEABLE — no breaking Input System changes stated
  at UG64/UG65/UG66. WN64 lists `OnMouseDown`/`OnMouseDrag`/`OnMouseUp` support with
  the Input System; WN66 lists Android gamepad rumble via `Gamepad.SetMotorSpeeds`.
- Netcode for GameObjects: NOT SOURCEABLE — not stated at WN65, WN66, UG66, or
  https://discussions.unity.com/t/unity-6-6-is-now-available/1735357.
- Exact list of members still `[Obsolete]` but not yet removed in 6.6: NOT
  SOURCEABLE — the What's new pages say they omit the full API change list (WN64).

### Migration Checklist (6.3 LTS → 6.6)

- [ ] Replace every `GetInstanceID()` / `int` instance ID with `GetEntityId()` / `EntityId` (UG65)
- [ ] Replace `go.rigidbody`-style shortcuts with `GetComponent<T>()` (UG65)
- [ ] Port any URP Compatibility Mode custom pass to Render Graph (UG64)
- [ ] Add `com.unity.ugui` explicitly if the project uses UGUI (UG65)
- [ ] Audit statics for domain-reload-off (UAL analyzer warnings, `[AutoStaticsCleanup]`) (WN66, DR66)
- [ ] Replace `DEVELOPMENT_BUILD` / `UNITY_64` checks (UG66)
- [ ] Stop relying on dynamic batching (UG66)
- [ ] Remove `OnDisable` propagation workarounds (UG64)
- [ ] Migrate custom UI Toolkit controls to `[UxmlElement]` (UG66)
