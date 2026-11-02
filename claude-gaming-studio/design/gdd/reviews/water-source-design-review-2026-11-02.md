# Design Review: Water Source Model

> **Date**: 2026-11-02
> **Doc**: `design/gdd/water-source-model.md`
> **Mode**: full (systems-designer, gameplay-programmer, qa-lead, level-designer; creative-director synthesis)
> **Verdict**: **NEEDS REVISION**

## Summary

The core design holds: each source loses exactly what is taken and never refills, the caller order (scoops at step 1, one Network withdraw per source at 2b) is right, the split maths and the quoted pacing match the simulator. Six bounded problems, none needing a redesign.

## Blocking (6)

| # | Problem | Raised by | Fix |
|---|---|---|---|
| B1 | The slice stores a start copy and a Drained flag, but the approved Game State Rule 1 and AC-R1 say "volume remaining per source" only and start volumes are "never in state". | Programmer, Systems | Amend Game State Rule 1 and AC-R1: Water Source slice = `{id, start, remaining, drained}` (Decision 1). |
| B2 | The drained snap deletes up to 0.001 L (breaks the 1e-9 ledger tests in Network AC-09 and Hand Tools AC-08), and Rule 8 puts it at step 2d while the formula puts it inside `Withdraw`. | QA, Systems, Programmer | Grant the residue to the caller inside `Withdraw` (if `remaining − granted ≤ ε`, grant all and set remaining 0), ε = 1e-6 L. Step 2d only derives state and raises `SourceDrained`. "Full" checks in Hand Tools and the Network use ≥. Rewrite Rule 4, Rule 8, AC-09, AC-22. |
| B3 | A 0 L request logs a warning; the Network calls `Withdraw(s, 0)` for every source every tick, so up to 20 warnings a second and allocations (breaks Game State AC-P1). | Programmer, Systems | 0 L returns 0 silently; warn only on negative, NaN or infinite, with an explicit NaN guard (C# `Math.Max(NaN, 0)` is NaN). |
| B4 | Basin guidance contradicts the real pond: "≥ 0.25 m per 10% band" needs a 2.5 m deep basin (the pond is 1 m); the 1:25 shelf assumes 20 m² but sloped sides give 25–30 m², where 1% shows only 17 cm (fails Drawdown AC-07). | Level design | State outcomes, not slopes: ≥ 0.8 m of shoreline exposed per 10% band at each viewpoint; ≥ 25 cm of mud at 1% from the pump. Graybox ~6 × 5 m, 1 m deep, bake the curve, derive slopes; pond volume = baked figure within 18,000–25,000 L. |
| B5 | Rejection names `SourceEmpty` / `CannotDeposit` clash with Hand Tools' snake_case `source_dry`, and test rejections this system doesn't own. | QA, Systems, Programmer | Use `source_dry`, owned by Hand Tools. Move Rule 11 and AC-13 to Hand Tools (no deposit API here). |
| B6 | AC-01 never brings the pond to 9,000 L or saves; AC-24 uses an undefined min-spec and the Profiler can't assert. | QA | AC-01: new game, `Withdraw(11,000)`, save, fake table → 25,000, reload, assert start 20,000 / remaining 9,000 / 55.0%. AC-24: EditMode 0-bytes-per-tick test with ProfilerRecorder; timing stays in Game State AC-P1. |

**Downgraded:** no stubs named in ACs (one shared line naming the test fakes fixes it); AC-25 "about 3 minutes" (tighten to first drop ≤ 1:45, dry ≤ 2:30).

## Recommended

- Numbers: Rule 2's 100 m² → 20 m² (~1.5 mm); hand work before the hand pump 0.25%, not 0.1%; leftover "lake" → "farm pond".
- The radio's "6.2 litres" line: the real final scoop is 9.19 L. Add the final grant to `SourceDrained` (in the B1 amendment) and let the line read it; keep the puddle at 40 L.
- AC-06 and AC-23: pump demands real pumps can produce (0.8 / 0.4 L per tick).
- Reject start ≤ 0 or NaN at new game; on load set NaN or negative remaining to 0.
- Mark Dormant, ice all-or-nothing and the shallow-end knob "not in MVP"; derive Low; relax the "Entered from" column.
- Level: name three viewpoints (hand-pump handle on the shelf top, the launch pad, the bucket beach); raise the pad deck 1.5–2 m with a 1:8 lip facing it; check the 20 s pond trip against the ≤ 18 m hose layout rule (estimate 12–14 s, would fail Hand Tools AC-11); pothole shape for the puddle.
- Programmer: list the read API (`Available`, `Withdraw`, `Start`, `DrawdownPct`, `State`), int ids, sources stored in id order.

## Decision for Nour

- **D1. Amend the approved Game State doc, or drop the start copy.** (a) Amend Game State Rule 1 so the slice holds the start copy and drained flag (recommended: tuning the table between playtests won't shift saved drawdown %). (b) Drop the copy; changing the start volume shifts the drawdown % of existing saves.

## Reviewer claims found wrong

- QA 11: AC-07 can fail (shallow-end slowdown or a per-tick cap would grant less than 150).
- Systems 9: "5-minute target vs 2.7 min" is not a contradiction (requirement vs simulator result).
- "10 warnings/s": with the puddle too it is up to 20; conclusion stands.
