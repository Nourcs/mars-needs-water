# ADR-0001: Tick driver and rate

## Status
Accepted (implemented in Sprint 1, 2026-11-06; Nour may veto)

## Date
2026-11-06

## Decision Makers
Nour (direction: "start building"), technical-director role (Claude)

## Summary
Run the simulation on its own fixed-step accumulator at 10 Hz, fed by unscaled frame time from one MonoBehaviour (`GameRoot.Update`), not on `FixedUpdate`.

## Engine Compatibility
Unity 6.6 (6000.6.7f1), C# 9. The decision lives in the pure-C# `MarsNeedsWater.Simulation` assembly (`noEngineReferences: true`), so it does not depend on engine APIs.

## Context
Game State Rules 3, 4, 9 and the `simulation_step` formula need a tick that is independent of frame rate, of the physics step and of `Time.timeScale` (pause sets timeScale 0 for presentation only).

## Decision
- `SimClock` (pure C#) implements `simulation_step` exactly: `acc = min(acc + frameDt, maxCatchUp)`, at most 5 ticks per frame, ε = 1e-9, `alpha = acc / dt`.
- `GameRoot` (execution order 100, after the controller) calls `Clock.Advance(Time.unscaledDeltaTime)` and runs `Simulation.Tick()` n times.
- `dt` = 0.1 s (10 Hz), `maxCatchUp` = 0.5 s; clamps from Game State Edge Cases applied at boot with one warning each.
- `Simulation.BeforeTick` lets the controller submit `PlayerPose` and then its buffered requests right before step 1 (FPC Rule 6).

## Alternatives Considered
- **FixedUpdate:** tied to the physics step and to `timeScale`; pause would need special cases. Rejected.
- **20 Hz:** no gameplay benefit since every rate is per second; 10 Hz keeps the 0.2 s input-to-number budget. Kept as a knob.

## Consequences
See Decision. The simulation stays testable outside Unity (`dotnet test claude-gaming-studio/tests/unit/simulation`).

## GDD Requirements Addressed
Game State Rules 3, 4, 9; AC-F1, AC-F2, AC-E13, AC-E19 (covered by `ClockTests`, `ContainerTests`).

## Validation Criteria
`ClockTests` pass (5 ticks on a 2 s hitch, ticks on frames 6, 12 … 60 at 60 fps, clamp warning).

## Related
`docs/architecture/architecture.md`, `design/gdd/game-state-and-simulation.md`
