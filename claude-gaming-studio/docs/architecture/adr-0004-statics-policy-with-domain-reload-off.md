# ADR-0004: Statics policy with domain reload off

## Status
Accepted (implemented in Sprint 1, 2026-11-06; Nour may veto)

## Date
2026-11-06

## Decision Makers
Nour (direction: "start building"), technical-director role (Claude)

## Summary
No mutable statics in simulation code; one `Simulation` instance is created fresh on every Play by `GameRoot`; the only presentation statics are a material cache (cleared at start) and the bootstrap's scene hook (unsubscribed before subscribing).

## Engine Compatibility
Unity 6.6 (6000.6.7f1), C# 9. The decision lives in the pure-C# `MarsNeedsWater.Simulation` assembly (`noEngineReferences: true`), so it does not depend on engine APIs.

## Context
Unity 6.6 keeps static values between Play presses when domain reload is off (Game State Rule 11).

## Decision
- `MarsNeedsWater.Simulation` is a `noEngineReferences` assembly with no static mutable fields (constants and pure static functions only).
- `Simulation.Boot()` clears state, the request queue, the accumulator and all subscribers.
- `Prims.ClearCache()` runs in `GameRoot.Start`; `GameBootstrap` does `sceneLoaded -= ; sceneLoaded +=`.

## Alternatives Considered
- **Turn domain reload back on:** slower Play presses for every iteration. Rejected.

## Consequences
See Decision. The simulation stays testable outside Unity (`dotnet test claude-gaming-studio/tests/unit/simulation`).

## GDD Requirements Addressed
Game State Rule 11; AC-R11.

## Validation Criteria
`Boot_ClearsEverything` passes; a static search of `Assets/Game/Simulation` finds no `static` non-readonly fields.

## Related
`docs/architecture/architecture.md`, `design/gdd/game-state-and-simulation.md`
