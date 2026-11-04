# Review Log: Water Source Model

## Review — 2026-11-02 — Verdict: NEEDS REVISION
Review mode: full
Workflow: full
Scope signal: S
Specialists: systems-designer, gameplay-programmer, qa-lead, level-designer, creative-director (senior)
Blocking items: 6 | Recommended: 7
Reviewed-Content-Hash: design/gdd/water-source-model.md 7ab84fc723b35bddeaa4d3e37f290df2ad92376c
Reviewed-Content-Hash: design/registry/entities.yaml 484aefbeaef4df610a959d0f733312227c296ea2
Reviewed-Content-Hash: .claude/skills/design-review/SKILL.md a63d8fba9b7142eeb43791f061e2e1533c429b43
Full report: design/gdd/reviews/water-source-design-review-2026-11-02.md

Summary: Core rules, caller order, split maths and pacing are sound. Blockers: slice schema conflicts with approved Game State (needs an amendment, Decision 1); the drained snap deletes water and its step is ambiguous; 0 L requests spam warnings; basin guidance contradicts a 1 m deep real pond; rejection names and ownership clash with Hand Tools; AC-01 and AC-24 can't run as written.

Prior verdict resolved: First review

### Revision — 2026-11-02 (same session, Nour chose "Fix now, amend Game State")
| Blocker | Fix applied |
|---|---|
| B1 Slice schema | Game State Rule 1 amended: Water Source slice `{id, start, remaining, drained}`; start copy is the one exception to "never in state". Ownership table and Rule 7 (`SourceDrained(source, lastGrantLitres)`) updated. |
| B2 Residue snap | `Withdraw` grants any leftover ≤ 1e-6 L to the caller, sets the drained flag at once, and ignores calls on a Drained source; step 2d only raises the event. Hand Tools "Full" uses ≥; Network allows a pool ≤ 1e-6 L over capacity (counts as Full). AC-09, AC-22 rewritten. |
| B3 Zero requests | 0 L returns 0 silently; warnings only for negative, NaN, infinite (explicit guard). AC-13. |
| B4 Basin | Outcome targets (≥ 25 cm mud at 1% from the hand-pump handle; ≥ 0.8 m shoreline per 10% band at each of three named viewpoints; pit for the last 5–10%); graybox ~6 × 5 m, 1 m deep, bake, derive slopes; pond volume = baked figure within 18,000–25,000 L. Mirrored in Drawdown rule 5, AC-07, open question, and Economy's real-scale note. |
| B5 Rejections | `source_dry` and `cannot_deposit` owned by Hand Tools (edge cases, AC-05b); no deposit API here. |
| B6 ACs | AC-01 full steps with fake table and stub save; AC-23 zero-allocation loop test; AC-24 first drop ≤ 1:45, dry ≤ 2:30; test doubles named. |
| Recommended (applied) | 20 m² / 1.5 mm; 0.25% before the hand pump; "farm pond" wording; radio line reads `lastGrantLitres` (Corporate Radio pool); realistic pump demands in ACs; data checks at new game and load; Dormant / ice / shallow-end marked not in MVP; Low derived; read API listed; three viewpoints and raised pad deck; pothole puddle; pond trip vs 18 m layout moved to an open question. |

Light check (fresh systems/QA reviewer): B1–B6 fixed; one new blocker (the withdraw formula let a later call on a drained source overwrite the last grant) and five nits (Network overfill rule, stale basin text in Economy and Drawdown, Game State ownership row, AC-23 method, radio line). All fixed the same session.
Revised-Content-Hash: design/gdd/water-source-model.md d983cc8a3f62f7fc70e2cde4f54220dafcac16d3
Revised-Content-Hash: design/registry/entities.yaml fe97a62a1efe88420e4a750fb7677dc0e6e868b0

Status after revision: In Review. A fresh /design-review decides whether it is Approved.

### Amendment — 2026-11-02 (from the batch design review)
Basin outcomes: 0.8 m shoreline only for the 10–30% bands at the launch-pad viewpoint; below that ≥ 6 cm vertical band separation; lip slopes from the curve. Shopping cart reveal ~15%. Pacing times updated for the 6 cr bucket. Drawdown dependency rows read Available/Start (fraction remaining/start). Pillar name capitalised.
Revised-Content-Hash: design/gdd/water-source-model.md 4c4940325e5796ddbb359049354024be12bf18ee
