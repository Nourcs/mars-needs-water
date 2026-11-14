# ADR-0003: Event dispatch

## Status
Accepted (implemented in Sprint 1, 2026-11-06; Nour may veto)

## Date
2026-11-06

## Decision Makers
Nour (direction: "start building"), technical-director role (Claude)

## Summary
One deferred, synchronous `EventBus` per session; events are a single flat `SimEvent` struct queued during a tick and flushed at step 3; listeners are presentation only.

## Engine Compatibility
Unity 6.6 (6000.6.7f1), C# 9. The decision lives in the pure-C# `MarsNeedsWater.Simulation` assembly (`noEngineReferences: true`), so it does not depend on engine APIs.

## Context
Game State Rules 5b, 6, 7: events are notifications, never consequences; at most once, in order; a failing listener is skipped; a per-flush cap stops loops.

## Decision
- `SimEvent {Type, Tick, Player, Id, Value, Text, Text2}` covers all 14 MVP event types without allocation.
- `Simulation.Raise` stamps the current tick; out-of-tick events (`SessionStarted`, `Paused`, `Resumed`, `Saved`, `SessionEnded`) flush immediately.
- Cap 256 per flush; the excess is dropped with one error.
- Cross-system consequences are direct method calls inside the tick (`Economy.CreditLaunch`, `HandTools.Grant`, `Pumps.Grant`, `Placement.*`).

## Alternatives Considered
- **C# events / UnityEvents:** immediate dispatch would let listeners observe half-finished ticks. Rejected.
- **One class per event type:** cleaner typing, but allocates per event. Revisit if the event set grows past the MVP.

## Consequences
See Decision. The simulation stays testable outside Unity (`dotnet test claude-gaming-studio/tests/unit/simulation`).

## GDD Requirements Addressed
Game State Rules 6, 7; AC-R6, AC-R7, AC-E3.

## Validation Criteria
`EventBus_ThrowingListenerIsSkipped`, `EventBus_LoopIsCappedAt256` pass.

## Related
`docs/architecture/architecture.md`, `design/gdd/game-state-and-simulation.md`
