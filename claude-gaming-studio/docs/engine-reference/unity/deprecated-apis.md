# Unity 6.3 LTS — Deprecated APIs

**Last verified:** 2026-02-13

Quick lookup table for deprecated APIs and their replacements.
Format: **Don't use X** → **Use Y instead**

---

## Input

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| `Input.GetKey()` | `Keyboard.current[Key.X].isPressed` | New Input System |
| `Input.GetKeyDown()` | `Keyboard.current[Key.X].wasPressedThisFrame` | New Input System |
| `Input.GetMouseButton()` | `Mouse.current.leftButton.isPressed` | New Input System |
| `Input.GetAxis()` | `InputAction` callbacks | New Input System |
| `Input.mousePosition` | `Mouse.current.position.ReadValue()` | New Input System |

**Migration:** Install `com.unity.inputsystem` package.

---

## UI

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| `Canvas` (UGUI) | `UIDocument` (UI Toolkit) | UI Toolkit is now production-ready |
| `Text` component | `TextMeshPro` or UI Toolkit `Label` | Better rendering, fewer draw calls |
| `Image` component | UI Toolkit `VisualElement` with background | More flexible styling |

**Migration:** UGUI still works, but UI Toolkit is recommended for new projects.

---

## DOTS/Entities

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| `ComponentSystem` | `ISystem` (unmanaged) | Entities 1.0+ complete rewrite |
| `JobComponentSystem` | `ISystem` with `IJobEntity` | Burst-compatible |
| `GameObjectEntity` | Pure ECS workflow | No GameObject conversion |
| `EntityManager.CreateEntity()` (old signature) | `EntityManager.CreateEntity(EntityArchetype)` | Explicit archetype |
| `ComponentDataFromEntity<T>` | `ComponentLookup<T>` | Entities 1.0+ rename |

**Migration:** See Entities package migration guide. Major refactor required.

---

## Rendering

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| `CommandBuffer.DrawMesh()` | RenderGraph API | URP/HDRP render passes |
| `OnPreRender()` / `OnPostRender()` | `RenderPipelineManager` callbacks | SRP compatibility |
| `Camera.SetReplacementShader()` | Custom render pass | Not supported in SRP |

---

## Physics

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| `Physics.RaycastAll()` | `Physics.RaycastNonAlloc()` | Avoid GC allocations |
| `Rigidbody.velocity` (direct write) | `Rigidbody.AddForce()` | Better physics stability |

---

## Asset Loading

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| `Resources.Load()` | Addressables | Better memory control, async loading |
| Synchronous asset loading | `Addressables.LoadAssetAsync()` | Non-blocking |

---

## Animation

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| Legacy Animation component | Animator Controller | Mecanim system |
| `Animation.Play()` | `Animator.Play()` | State machine control |

---

## Particles

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| Legacy Particle System | Visual Effect Graph | GPU-accelerated, more performant |

---

## Scripting

| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| `WWW` class | `UnityWebRequest` | Modern async networking |
| `Application.LoadLevel()` | `SceneManager.LoadScene()` | Scene management |

---

## Platform-Specific

### WebGL
| Deprecated | Replacement | Notes |
|------------|-------------|-------|
| WebGL 1.0 | WebGL 2.0 or WebGPU | Unity 6+ defaults to WebGPU |

---

## Quick Migration Patterns

### Input Example
```csharp
// ❌ Deprecated
if (Input.GetKeyDown(KeyCode.Space)) {
    Jump();
}

// ✅ New Input System
using UnityEngine.InputSystem;
if (Keyboard.current.spaceKey.wasPressedThisFrame) {
    Jump();
}
```

### Asset Loading Example
```csharp
// ❌ Deprecated
var prefab = Resources.Load<GameObject>("Enemies/Goblin");

// ✅ Addressables
var handle = Addressables.LoadAssetAsync<GameObject>("Enemies/Goblin");
await handle.Task;
var prefab = handle.Result;
```

### UI Example
```csharp
// ❌ Deprecated (UGUI)
GetComponent<Text>().text = "Score: 100";

// ✅ TextMeshPro
GetComponent<TextMeshProUGUI>().text = "Score: 100";

// ✅ UI Toolkit
rootVisualElement.Q<Label>("score-label").text = "Score: 100";
```

---

**Sources:**
- https://docs.unity3d.com/6000.0/Documentation/Manual/deprecated-features.html
- https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Migration.html


---

## 6.3 LTS → 6.6

**Last verified:** 2026-11-01

Format: **Don't use X** → **Use Y instead**. "Status" says whether it is removed /
a compile error, or deprecated but still working, in the version named.

Sources: UG64 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity64 ·
UG65 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity65 ·
UG66 = https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity66 ·
WN64 = https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity64 ·
WN65 = https://docs.unity.com/en-us/engine/6000.5/manual/whats-new/unity65 ·
WN66 = https://docs.unity.com/en-us/engine/6000.6/manual/whats-new/unity66

---

### Object identity

| Don't use | Use instead | Status | Source |
|---|---|---|---|
| `Object.GetInstanceID()` | `GetEntityId()` | Deprecated 6.4; compile error 6.5 | WN64, UG65 |
| `InstanceID` type / `int` IDs | `EntityId` (64-bit struct) | Compile error 6.5 | UG65, WN65 |
| `Resources.InstanceIDToObject`, `Selection.instanceIDs` | `EntityId` equivalents (error message names replacement) | Compile error 6.5 | WN65, UG65 |
| `EntityId.GetHashCode()` as dictionary key | `EntityId` itself as key | — | UG65 |
| `EntityId.FromULong(int.Parse(s))` | parse `s` as `ulong` | Data loss in 6.5 | UG65 |

### GameObject / Component (removed 6.5)

| Don't use | Use instead | Source |
|---|---|---|
| `gameObject.rigidbody` / `component.rigidbody` | `GetComponent<Rigidbody>()` | UG65 |
| `.rigidbody2D` | `GetComponent<Rigidbody2D>()` | UG65 |
| `.camera` | `GetComponent<Camera>()` | UG65 |
| `.light` | `GetComponent<Light>()` | UG65 |
| `.renderer` | `GetComponent<Renderer>()` | UG65 |
| `.collider` / `.collider2d` | `GetComponent<Collider>()` / `GetComponent<Collider2D>()` | UG65 |
| `.audio` | `GetComponent<AudioSource>()` | UG65 |
| `.animation` | `GetComponent<Animation>()` | UG65 |
| `.constantForce`, `.hingeJoint`, `.particleSystem`, `.networkView` | `GetComponent<ConstantForce/HingeJoint/ParticleSystem/NetworkView>()` | UG65 |
| `GameObject.AddComponent(string)` | `AddComponent<T>()` | UG65 |
| `GameObject.SampleAnimation(clip, t)` | `AnimationClip.SampleAnimation(go, t)` | UG65 |
| `GameObject.PlayAnimation(obj)` / `StopAnimation()` | `animation.Play()` / `animation.Stop()` | UG65 |

### Rendering (URP / SRP)

| Don't use | Use instead | Status | Source |
|---|---|---|---|
| URP Compatibility Mode / `URP_COMPATIBILITY_MODE` define / `Execute(ScriptableRenderContext, ref RenderingData)` passes | Render graph (`RecordRenderGraph`) | Removed 6.4 | UG64, WN64 |
| Built-In Render Pipeline | URP (or HDRP) | Deprecated 6.5; supported through 6.7 LTS | WN65, UG65 |
| Dynamic batching | Other draw-call optimizations (SRP Batcher, GPU Resident Drawer, instancing, per linked page) | Deprecated 6.5; obsolete 6.6 | WN65, UG66 |
| `LightShadowCasterMode.NonLightmappedOnly` / `.Everything` | `.ShadowMask` / `.DistanceShadowMask` | Renamed 6.4 (auto-updated) | UG64 |
| `IVolume` | `VolumeGizmoDrawer` | Removed 6.4 | UG64 |
| Shader Graph Reflection Probe Node | Custom Function Node: `Out = SHADERGRAPH_REFLECTION_PROBE(ViewDir, Normal, LOD);` | Deprecated 6.5 | UG65 |
| Legacy Render Graph API (custom SRPs) | Render Graph API with native render pass support | Obsolete 6.5 | UG65 |
| `DebugState`, `DebugState<T>`, `DebugStateBool/Int/Float/Color/Vector`, `DebugStateAttribute`, legacy `DebugUIDrawer`, `LegacyStyles` | `ISerializedDebugDisplaySettings`; override `DebugUI.Widget.Create()` | Compile error 6.6 | UG66 |
| `CoreBuildData.developmentBuild` | `CoreBuildData.useDiagnosticChecks` | Obsolete 6.6 (always `false`) | UG66 |
| Progressive CPU Lightmapper | Unity Compute Light Baker or Progressive GPU Lightmapper | Deprecated 6.6 | UG66, WN66 |
| PVRTC texture format | ASTC or ETC | Removed 6.4 | WN64 |

The sample-name in the dynamic batching row ("SRP Batcher, GPU Resident Drawer,
instancing") is an inference from WN64's Rendering Statistics description, which
lists those as draw-call optimizations; UG66 only links "Choose a method for
optimizing calls".

### UI

| Don't use | Use instead | Status | Source |
|---|---|---|---|
| UXML Factory/Traits (`UxmlFactory`, `UxmlTraits`) | `[UxmlElement]` + `[UxmlAttribute]` | Removed 6.6 | UG66 |
| `UIToolkitInputConfiguration.SetRuntimeInputBackend` | Delete the call (no replacement needed) | Compile error 6.6 | UG66 |
| `DefaultEventSystem.LegacyInputProcessor` | — | Removed 6.6 | UG66 |
| UI Document component (for new scenes) | Panel Renderer component | UI Document "continues to work" (6.5) | WN65 |
| Relying on URP to pull in `com.unity.ugui` | Add `com.unity.ugui` explicitly | Dependency dropped 6.5 | UG65 |

### Scripting / Editor

| Don't use | Use instead | Status | Source |
|---|---|---|---|
| `#if DEVELOPMENT_BUILD` (safety checks) | `#if UNITY_ENABLE_CHECKS` | Deprecated 6.6 (warning `UAC0009`); error in 6.8 | UG66 |
| `#if DEVELOPMENT_BUILD` (logging/profiling) | `UNITY_INCLUDE_INSTRUMENTATION` / `[Conditional("UNITY_INCLUDE_INSTRUMENTATION")]` | Same | UG66 |
| `#if DEVELOPMENT_BUILD` (dev-only runtime behavior) | `if (Debug.isDebugBuild)` | Same | UG66 |
| `#if UNITY_64` | runtime `IntPtr.Size` check | Deprecated 6.6 (warning `UAC0008`); error in 6.8 | UG66 |
| `BuildOptions.ForceEnableAssertions` | `PlayerSettings.SetManagedCodeVariant(target, ManagedCodeVariant.Checked)` | Obsolete 6.6 | UG66 |
| `EditorApplication.playModeStateChanged` | `[OnEnteringPlayMode]` / `[OnExitingPlayMode]` (`UnityEngine`); `[OnEnteringEditMode]` / `[OnExitingEditMode]` (`UnityEditor.Scripting.LifecycleManagement`) | New 6.5 ("replace subscribing to") | WN65 |
| Hard-coded global `Editor.log` path | `Application.consoleLogPath`; `-logFile` in CI | Changed 6.5 | UG65 |
| Custom `[Serializable]` key/value-list dictionary wrappers (new code) | `[SerializeField] Dictionary<TKey,TValue>` | Old way still works (6.6) | UG66, WN66 |
| `serializeInlineMappingsOnOneLine` setter | — (no-op; getter always `true`) | 6.6 | UG66 |
| Obsolete `ModelImporter` members (`optimizeMesh`, `isFileScaleUsed`, `normalImportMode`, `resampleRotations`, `splitTangentsAcrossSeams`, `tangentImportMode`) | `optimizeMeshPolygons`/`optimizeMeshVertices`, `useFileScale`, `importNormals`, `resampleCurves`, `tangentImportMode`, `importTangents` | Compile error 6.5 | UG65 |

### DOTS / Entities (only if used)

| Don't use | Use instead | Status | Source |
|---|---|---|---|
| `Entities.ForEach` | `IJobEntity`, `SystemAPI.Query` | Removed 6.5 | UG65 |
| `Job.WithCode` | `IJob` | Removed 6.5 | UG65 |
| `IAspect` | Component and `EntityQuery` APIs | Removed 6.5 | UG65 |
| Managed components (class `IComponentData`, `AddComponentObject`, `SystemAPI.ManagedAPI`…) | `entityManager.GetCompanion<T>(entity)`, `WithAll<CompanionComponent<T>>()` | Deprecated 6.6 | WN66 |
| Entities Journaling | — | Deprecated 6.5 | WN65 |

### Physics / Input / Netcode

| Area | Finding |
|---|---|
| 3D Physics (PhysX) | NOT SOURCEABLE — no deprecated 3D physics APIs stated at UG64, UG65, UG66, WN65, WN66 |
| 2D Physics | `UnityEngine.LowLevelPhysics2D` → `Unity.U2D.Physics` (`PhysicsCore2D`), 6.5 — UG65 |
| Input System | NOT SOURCEABLE — no deprecated Input System APIs stated at UG64, UG65, UG66 |
| Netcode for GameObjects | NOT SOURCEABLE — not stated at WN65, WN66, UG66 |
