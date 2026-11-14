# ADR-0005: Stable ids and the request shape for future co-op

## Status
Accepted (implemented in Sprint 1, 2026-11-06; Nour may veto)

## Date
2026-11-06

## Decision Makers
Nour (direction: "start building"), technical-director role (Claude)

## Summary
Authored ids below 1,000,000 come from the level; runtime ids come only from the Session slice counter (from 1,000,000). Every change enters as a `Request` subclass carrying the issuer's `PlayerId` and ids, never quantities (poses excepted).

## Engine Compatibility
Unity 6.6 (6000.6.7f1), C# 9. The decision lives in the pure-C# `MarsNeedsWater.Simulation` assembly (`noEngineReferences: true`), so it does not depend on engine APIs.

## Context
Game State Rules 2 and 5; host-authoritative co-op later turns requests into network commands.

## Decision
- `Simulation.NewId()` is internal; requests never carry new ids; `Purchased` hands out the crate id.
- Requests are small classes (`ScoopRequest`, `ConnectRequest`, …) validated at step 1; a rejection raises `RequestRejected(issuer, code)` with the request name in `Text2` so the controller can pick the (request, code) text.
- `ScoopRequest` carries the aim point on the water (a pose, validated: inside the source's scoop area and within reach), and an intake `Connect` carries the aimed point in the zone (Hose Rule 3).

## Alternatives Considered
- **Engine `EntityId` as ids:** forbidden in state (Game State Rule 2; 64-bit, changes per session). Rejected.

## Consequences
See Decision. The simulation stays testable outside Unity (`dotnet test claude-gaming-studio/tests/unit/simulation`).

## GDD Requirements Addressed
Game State Rules 2, 5, 12; AC-E14, AC-E17.

## Validation Criteria
`Purchase_GrantsInSameStep_AndIdsComeFromTheCounter` passes.

## Related
`docs/architecture/architecture.md`, `design/gdd/game-state-and-simulation.md`
