# Review Log: Game State & Simulation

## Review — 2026-11-02 — Verdict: NEEDS REVISION
Review mode: full
Workflow: full
Scope signal: M
Specialists: systems-designer, qa-lead, unity-specialist, network-programmer, creative-director (senior)
Blocking items: 8 | Recommended: 12
Reviewed-Content-Hash: design/gdd/game-state-and-simulation.md c2b97eb54fb38fdcf07d2f0622ebd8856c344a10
Reviewed-Content-Hash: design/registry/entities.yaml b6e756025e54c29ec911da8025751414a5d8c80f
Reviewed-Content-Hash: .claude/skills/design-review/SKILL.md a63d8fba9b7142eeb43791f061e2e1533c429b43
Full report: design/gdd/reviews/game-state-design-review-2026-11-02.md

Summary: The architecture is sound. The doc had local contradictions: a launch payout delivered through a listener request could be lost by a save; Shipping was missing from the tick order; save-while-paused rules contradicted each other; out-of-tick events never flushed; tick-rate independence was over-promised for timers; the event list was stale (Milestone had no owner); there were no PlayerId or ID-ownership seams; and some ACs were untestable.

Prior verdict resolved: First review

### Revision — 2026-11-02 (same session, Nour chose "Revise now")
| Blocker | Fix applied |
|---|---|
| 1 Credits lost between ticks | Rule 5b: consequences are direct calls (Shipping → Economy.CreditLaunch at 2c); events are notifications only; listeners are presentation-only; save only with an empty queue. AC-E9 added. Shipping, Economy and Radio GDDs updated. |
| 2 Tick order / numbering | Steps 2a–2f (Pumps, Network, Shipping, Water Source, Drawdown, Economy); AC-R4 updated; Water Source, Network, Pumps and Drawdown GDDs relabelled. |
| 3 Save while paused | Saving is a guarded action, not a state; Saving row removed; AC-E5 and AC-E8 rewritten. |
| 4 Out-of-tick events | Immediate flush for events raised outside a tick; AC-E10 and AC-E11 added. |
| 5 Tick-rate promise | 1e-6 applies to continuous quantities only; timers are whole ticks; AC-R3 rewritten with a stub. Shipping countdown is now saved and resumed. |
| 6 Event list | 13 events (adds LaunchStarted, SourceDrained, PoolFull); Milestone cut (the Radio derives milestones); AC-R7 rewritten. |
| 7 Co-op seams | PlayerId on every request and held water keyed by PlayerId; only the simulation creates ids (counter in Session, saved); AC-E14 added. Interaction GDD updated. |
| 8 AC fixes | AC-X1 uses stubs and includes litres aboard; AC-P1 uses a synthetic fixture and drops the 0-GC claim (Manual profiler check instead). |
| Recommended (applied) | maxCatchUp clamp and 5-tick cap; pause never via timeScale; plain-data Pose and Placement slice; session time saved in seconds; since_prev_same keyed by (event, source); AC-R1/R2/R5/R9/R10/R11/F1/F3/E3/E6 rewritten; no-lockstep line; stale text fixed. |

Status after revision: In Review. A fresh /design-review decides whether it is Approved.

## Re-review — 2026-11-02 — Verdict: NEEDS REVISION (converging)
Review mode: full
Workflow: full
Scope signal: S
Specialists: systems-designer, qa-lead, unity-specialist, network-programmer, creative-director (senior)
Blocking items: 4 | Recommended: 12
Full report: design/gdd/reviews/game-state-design-rereview-2026-11-02.md
Prior verdict resolved: Partly. Prior blockers 1, 2, 4, 5, 6 and 8 are fixed; 3 and 7 are mostly fixed.
New blockers: the tick formula ε, the Menu state and Paused exits, the queued-request policy at save, and the Placement slice owner (with crate delivery timer and presentation freeze).

### Revision 2 and focused check — 2026-11-02 — Verdict: APPROVED
Review mode: full (focused verification, as the creative director advised for a converging doc)
Workflow: full
Specialist: systems-designer (with network check)
Reviewed-Content-Hash: design/gdd/game-state-and-simulation.md 4f25bf1c3a725b3c21635fd35b96fbd00be09e73
Reviewed-Content-Hash: design/registry/entities.yaml b6e756025e54c29ec911da8025751414a5d8c80f
Reviewed-Content-Hash: .claude/skills/design-review/SKILL.md a63d8fba9b7142eeb43791f061e2e1533c429b43
Fixes: tick formula ε, with acc clamped at 0 (verified in doubles: 10 ticks on frames 6..60); Menu state and Paused exits; queued requests dropped and logged on Paused/Quitting saves; Placement module owns the Placement slice through validated requests and a host-only PropSettled report; crate delivery is a saved whole-tick timer in the Economy slice (step 2f); presentation freezes on pause (timeScale is set by presentation only); authored ids below 1,000,000; session clock is saved seconds plus ticks; Saved is delivered on its own tick; listeners don't raise events; ACs E15–E21 added. Economy and Shipping wording aligned.
Remaining non-blocking: AC-R1 field-level schema (Save & Load ADR); off-main-thread saves and IL2CPP perf split (ADRs); scoop cycle timer (Hand Tools GDD).

### Amendment — 2026-11-02 (from the Economy design review, no re-review)
Rule 12 gains one sentence: simulation systems may change Placement through four direct calls (Rule 5b): `Placement.Spawn`, `Remove`, `Move` and `SetHands`, used only by Economy (crates, unboxing) and the Network (storage swap) in the MVP. Rule 1's Economy slice line now lists crate records (Inbound/Landed/Opened, landed tick). No event or AC changed; status stays Approved.

### Amendment — 2026-11-02 (from the Water Source design review; Nour chose "Fix now, amend Game State")
Rule 1: the Water Source slice is `{id, start, remaining, drained}` per source; the start copy is the one exception to "start volumes never in state", so tuning the pond never shifts a saved drawdown %. Rule 7: `SourceDrained(source, lastGrantLitres)` (the radio's last-drop line reads the final grant). AC-R1 is unchanged in wording and checks against the amended Rule 1 list. Status stays Approved.
