# ADR-0002: Number representation for litres, credits and shipped water

## Status
Accepted (implemented in Sprint 1, 2026-11-06; Nour may veto)

## Date
2026-11-06

## Decision Makers
Nour (direction: "start building"), technical-director role (Claude)

## Summary
Litres are `double` everywhere water moves; credits are a 64-bit integer of millicredits; litres aboard and total shipped are 64-bit integers of microlitres; the Earth counter is a derived `decimal`.

## Engine Compatibility
Unity 6.6 (6000.6.7f1), C# 9. The decision lives in the pure-C# `MarsNeedsWater.Simulation` assembly (`noEngineReferences: true`), so it does not depend on engine APIs.

## Context
Economy Rule 3 and Shipping Rule 12 need exact credits and an exact shipped total; Water Source and Hose rules use a 1e-6 L tolerance on doubles; the Earth counter starts at 1.386 × 10²¹ L.

## Decision
- Sources, tools, tanks: `double` litres with the GDDs' epsilons (drainedEpsilon 1e-6, Full within 1e-6, Empty ≤ 1e-6).
- Credits: `long` millicredits, payout rounded once per launch (`PayoutFor`), `floor(credits)` = integer division.
- Shipping: `Take` requests whole µL (floor), litres aboard and total shipped are `long` µL, and the same value is passed to `CreditLaunch`.
- Earth remaining = `max(1, 1.386e21 − shipped)` computed as `decimal` when displayed; never stored.
- Range: `long` µL holds 9.2 × 10¹² L shipped. That is enough for every MVP and Vertical Slice source; a reservoir-scale region (> 10¹² L) must move total shipped to `decimal` before it ships. Logged as a known limit.

## Alternatives Considered
- **`long` millilitres everywhere:** exact, but every rate × dt would need rounding rules; the GDDs are written against doubles with tolerances. Rejected for the MVP.
- **`decimal` everywhere:** slow in the flow solve, no benefit. Rejected.

## Consequences
See Decision. The simulation stays testable outside Unity (`dotnet test claude-gaming-studio/tests/unit/simulation`).

## GDD Requirements Addressed
Economy Rules 2, 3, 11; Shipping Rule 12; Water Source Rule 4; Hose Rule 12 (conservation).

## Validation Criteria
Conservation asserted in `HandToolTests`, `PumpTests`, `LadderTests` (relative ≤ 1e-9); `SpoonLaunch_PaysExactlyTheBucketPrice`; `EarthCounter_IsExactDecimal`.

## Related
`docs/architecture/architecture.md`, `design/gdd/game-state-and-simulation.md`
