# Mars Needs Water — Architecture (MVP)

> **Status**: Living document · **Last Updated**: 2026-11-06 · **Engine**: Unity 6.6 (6000.6.7f1), URP, C# 9
> **Source of truth for rules**: the 12 approved MVP GDDs in `design/gdd/`. This doc only says where each rule lives in code.

## Layers

| Layer | Assembly / folder | Engine? | Owns |
|---|---|---|---|
| Simulation (Foundation + Core) | `MarsNeedsWater/Assets/Game/Simulation` → `MarsNeedsWater.Simulation.asmdef` | **No** (`noEngineReferences`) | All runtime state, the 10 Hz tick, requests, events, every MVP rule |
| Presentation | `Assets/Game/Runtime` → `MarsNeedsWater.Game.asmdef` | Yes | Greybox level, first-person controller, interaction ray, world view, HUD, play-log file, tick driver |
| Editor | `Assets/Game/Editor` → `MarsNeedsWater.Editor.asmdef` | Editor only | Creates `Assets/Game/Scenes/Greybox.unity` |
| Data | `Assets/Game/Data/Resources/mvp_tuning.json` | — | The Economy tuning table (prices, capacities, rates, hose lengths, item socket points) |
| Tests | `claude-gaming-studio/tests/unit/simulation` (`dotnet test`) | No | NUnit tests of the simulation, compiled straight from `Assets/Game/Simulation` |
| Prototype (frozen) | `Assets/Prototypes/LakeLoopConcept` | Yes | The concept prototype; runs only in `SampleScene` |

Presentation may read any simulation state and may only change it by `Simulation.Submit(request)`. The simulation never references presentation.

## Simulation map (GDD → code)

| GDD | Code | Tick step |
|---|---|---|
| Game State & Simulation | `Core/Simulation.cs` (container, tick order, run states, saves hook), `Core/SimClock.cs`, `Core/Events.cs` (bus), `Core/SimState.cs` (9 slices), `Core/Requests.cs`, `Core/PlayLog.cs`, `Systems/PlacementSystem.cs` (Placement module + `PlacementValidator`) | 1, 3, 4 |
| Pumps & Machines | `Systems/PumpsSystem.cs` | 2a |
| Hose, Storage & Flow Network | `Systems/NetworkSystem.cs` (pools, pour, Take, swap, connect rules, flow solve) | 2b |
| Shipping & Launch | `Systems/ShippingSystem.cs` | 2c |
| Water Source Model | `Systems/WaterSourceSystem.cs` | 2d |
| Drawdown (sim half) | `Systems/DrawdownSystem.cs` (thresholds, reveals) | 2e |
| Economy, Progression & Shop | `Systems/EconomySystem.cs` (credits in mc, shop chain, ORDER, crates) | 2f |
| Hand Tools | `Systems/HandToolsSystem.cs` (SelectTool, Scoop, Pour) | 1 |
| First-Person Controller & Interaction | `Runtime/PlayerController.cs`, `Runtime/RejectionText.cs`, `Runtime/SimTarget.cs` | — |
| Drawdown (visual half), HUD, Radio (placeholder) | `Runtime/WorldView.cs`, `Runtime/Hud.cs` | — |
| Save & Load | not built (ADR-0006 pending) | 4 |

## Key decisions (ADRs)

- ADR-0001 Tick driver: own accumulator in `GameRoot.Update`, 10 Hz, unscaled time.
- ADR-0002 Numbers: litres `double`; credits `long` mc; aboard/shipped `long` µL; Earth counter derived `decimal`.
- ADR-0003 Events: one deferred bus, flat `SimEvent` struct, flush at step 3, cap 256.
- ADR-0004 Statics: none in the simulation; fresh `Simulation` per Play.
- ADR-0005 Ids and requests: authored < 1,000,000; runtime from the session counter; requests carry ids, not quantities.
- ADR-0006 Save serialiser: proposed, decided with the Save & Load stories.

## Greybox deviations (accepted for the vertical-slice prototype; art bible deferred)

- Pond is a 5 × 4 × 1 m box pit (exactly 20,000 L), so the volume→height curve is linear; the basin bake, intake shelf and along-surface stain-band shader are later stories. Bands are drawn as thin boxes on the pit walls.
- Input uses `Keyboard.current` / `Mouse.current` directly; the `MarsInput.inputactions` asset, gamepad and the Hold/Toggle setting (Hold only for now) are later stories.
- HUD and radio are IMGUI placeholders; the Corporate Radio system (pools, priorities, queue of 3) is not built yet.
- Prompt ranges in the controller are measured to the ray hit; the simulation measures to each object's interact point, so a rare "Too far" can show on an edge.
- Hoses are straight sagging lines; no tether clamp while carrying an end (the simulation still rejects `hose_too_short`).
- Physics props (crates tumbling, `PropSettled`) are not simulated; items sit where they are placed.
