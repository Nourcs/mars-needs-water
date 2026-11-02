# Design Re-Review: Game State & Simulation

- **Date:** 2026-11-02
- **Review mode:** full · **Workflow:** full
- **Specialists:** systems-designer, qa-lead, unity-specialist, network-programmer
- **Senior reviewer:** creative-director
- **Re-review:** Yes. The prior verdict was NEEDS REVISION, earlier on 2026-11-02.
- **Completeness:** 8/8 sections.

## Fixed since the last review
- **Prior #1, credit loss:** fixed. Rule 5b makes the payout a direct call.
- **Prior #2, #4, #5, #6, #8:** fixed (tick order, out-of-tick flush, timers in whole ticks, event list, ACs).
- **Prior #3, save while paused:** mostly fixed.
- **Prior #7, co-op seams:** mostly fixed.

## Required Before Implementation
1. **Tick formula ε** [systems, qa, unity].
   - Problem: computed in doubles, 60 frames of 1/60 s give 9 ticks, so AC-F1 fails.
   - Fix: use `acc ≥ dt − ε` and `acc = max(acc − dt, 0)`, and add ε to the variables table.
2. **Complete the state machine** [qa, systems].
   - Add a Menu state: Booting → Menu → Loading.
   - Add the transitions Paused → Quitting and Paused → Menu (with a save).
3. **Queued requests in a Paused or Quitting save** [network, qa, systems].
   - Decision: they are dropped and logged, by design. They are intents that have not been validated yet.
   - Rewrite AC-E5 to match.
4. **Placement slice ownership** [network, unity, systems].
   - Name the slice's owner.
   - Add a host-validated "place/drop at pose" request, as the one allowed exception to the no-quantities rule.
   - Say at which step physics-settled poses are recorded.
   - Put crate delivery in a slice as a whole-tick timer, advanced at a named step.
   - Freeze presentation while paused. The simulation never reads timeScale; presentation may set it. Add a Manual AC.

## Cross-doc fix (not blocking this GDD)
- **Economy GDD:** AC-01, the edge case at line 182 and the variable source at line 94 still say the payout happens on LaunchCompleted. Rule 6 `Purchased` still lacks crateId.

## Recommended
- Add PlayerId to Purchased and Launch(); presentation filters RequestRejected by local player.
- Reserve an id range for authored objects.
- Use one source for the session clock, and trigger autosave by tick count.
- Add the 5-tick cap to the knob table, with its slow-motion trade-off.
- Convert timers with `max(1, round(s/dt))`.
- Deliver `Saved` on the tick it happens.
- Ban listeners from raising events; keep the event cap as a safety net.
- AC-E9: make it deterministic.
- AC-F3: give it an explicit empty first row.
- Add missing ACs: accumulator reset on resume, purchase granted in step 1, no timeScale.
- Turn focus-pause off by default in the Editor.
- Unsubscribe the quit and focus hooks in Boot.

## Deferred
| Item | Moved to |
|---|---|
| Off-main-thread saves | ADR |
| AC-P1 IL2CPP split | ADR |
| Reflection-test tooling | ADR |
| Field-level schema | Save & Load GDD and its ADR |
| Scoop-cycle timing | Hand Tools GDD |
| Co-op pose per player | One-line note |
| Play-log build flag | Implementation |

## Disagreements
- **timeScale:** settled. The simulation never reads it; presentation may set it.
- **Queued requests at save:** the creative-director chose "drop by design" over qa-lead's "drain first" or "refuse".

## Senior Verdict [creative-director]
The doc is converging. The prior blockers were contradictions and are now resolved. The new blockers are edge gaps: one formula, two states, one policy and one slice with no owner. One focused pass should reach APPROVED, and a single specialist check on Placement would be enough.

**Scope signal:** S.

## Verdict: NEEDS REVISION
