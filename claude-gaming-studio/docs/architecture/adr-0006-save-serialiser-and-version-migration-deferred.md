# ADR-0006: Save serialiser and version migration (deferred)

## Status
Proposed

## Date
2026-11-06

## Decision Makers
Nour (direction: "start building"), technical-director role (Claude)

## Summary
Not decided yet. Save & Load is the next sprint; the state is already plain data (no engine types, dictionaries only in the Hand Tools, Drawdown and Placement slices).

## Engine Compatibility
Unity 6.6 (6000.6.7f1), C# 9. The decision lives in the pure-C# `MarsNeedsWater.Simulation` assembly (`noEngineReferences: true`), so it does not depend on engine APIs.

## Context
Game State lists this ADR as open: `JsonUtility` cannot store dictionaries; `System.Text.Json` under IL2CPP is unverified on Unity 6.6.

## Decision
- Proposal to evaluate in Sprint 2: a hand-written DTO layer (arrays instead of dictionaries) serialised with `JsonUtility`, with a `version` field and per-slice load hooks (Water Source Rule 13 clamps).
- Until then `Simulation.SaveWriter` is null; autosave and purchase saves run the step-4 path with no writer.

## Alternatives Considered
- **Newtonsoft Json (com.unity.nuget.newtonsoft-json):** handles dictionaries; adds a package. Candidate.
- **Binary:** not human-readable for playtest debugging. Rejected for the MVP.

## Consequences
See Decision. The simulation stays testable outside Unity (`dotnet test claude-gaming-studio/tests/unit/simulation`).

## GDD Requirements Addressed
Game State Rule 4 step 4; Save & Load GDD.

## Validation Criteria
To be written with the Save & Load stories.

## Related
`docs/architecture/architecture.md`, `design/gdd/game-state-and-simulation.md`
