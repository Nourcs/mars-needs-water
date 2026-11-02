# Design Review: Game State & Simulation

- **Date:** 2026-11-02
- **Review mode:** full · **Workflow:** full
- **Specialists:** systems-designer, qa-lead, unity-specialist, network-programmer
- **Senior reviewer:** creative-director
- **Re-review:** No, this is the first review. All specialists were fresh agents, not the author.

## Completeness: 8/8 required sections present

## Dependency Graph
- ✓ Its upstream dependencies: none.
- ✓ Downstream GDDs that exist: water-source-model, hose-storage-and-flow-network, economy-progression-and-shop, hand-tools, pumps-and-machines, shipping-and-launch, drawdown-visuals-and-reveals, corporate-radio, save-and-load, first-person-controller-and-interaction, hud.
- ✗ Not yet written (later tiers): Audio, Onboarding, Network Spike, Co-op.

## Registry Values
Checked: sim_tick_rate (10 Hz), sim_max_catch_up (0.5 s), input_to_number_latency_budget (0.2 s), simulation_step. No conflicts.

## Required Before Implementation
1. **Credits can be lost between ticks** [systems-designer, network-programmer].
   - Problem: the payout is made through a listener request that lands one tick later, and the request queue isn't saved. A save in between drops the credits.
   - Fix: when one system causes a change in another, it calls that system directly, in tick order (Shipping → Economy.CreditLaunch). Events are notifications only. Save only when the queue is empty.
2. **Tick order is missing Shipping** [systems-designer].
   - Fix: add Shipping after Network and before Economy, and number the system steps 2a–2f in every GDD. Update AC-R4.
3. **Saving while paused contradicts itself** [systems-designer, qa-lead, unity-specialist].
   - Problem: AC-E5 and AC-E8 disagree.
   - Fix: make Save a guarded action, not a state, and rewrite AC-E8.
4. **Some events never reach listeners** [systems-designer].
   - Problem: Paused, Saved-while-paused and SessionEnded are raised when no tick runs, so they never flush.
   - Fix: add an out-of-tick flush.
5. **Tick-rate independence is over-promised** [systems-designer, qa-lead, unity-specialist].
   - Problem: timers quantise to the tick.
   - Fix: store timers as whole ticks or integer ms, and limit the 1e-6 tolerance to continuous rates.
6. **The event list is stale** [systems-designer, qa-lead].
   - Fix: add SourceDrained, PoolFull and LaunchStarted. Give Milestone an owner or cut it, then fix AC-R7.
7. **Cheap co-op seams** [network-programmer].
   - Fix: every request carries a PlayerId, and held water is keyed by PlayerId. Only the simulation creates object IDs, from a counter saved in the Session slice.
8. **Acceptance criteria fixes** [qa-lead, unity-specialist, systems-designer].
   - AC-X1: drop the play-log clause, and add litres aboard the rocket to the conservation sum.
   - AC-P1: use a synthetic fixture instead of the undefined "min-spec".

## Recommended Revisions
- Validate maxCatchUp ≥ dt at boot.
- Pause is a simulation state and never uses timeScale.
- Allow a plain-data pose struct in state: ban engine types, not positions.
- Save session_time in seconds.
- Key the play log's since_prev_same by event and source.
- Write saves atomically: temp file, then File.Replace.
- AC-R11: make it an EditMode test that calls Boot() twice.
- Focus tests use a fake focus source; AC-R9's alt-tab check stays Manual.
- Clean up AC-F3, AC-E6 and AC-R5.
- Add the missing ACs: quit, load, autosave, and out-of-range knobs.
- Add one line saying there is no lockstep determinism.
- Fix the stale "no other GDD exists" text.

## Deferred or Downgraded (creative-director)
- **Zero-GC on every tick:** changed to "no steady per-tick allocation in the profiler" (Manual).
- **Moved to the ADRs:** pooled event bus, asmdef setters, tick-driver mechanics (Update vs PlayerLoop), the requestId echo, SetPaused vs menu, snapshot as byte[].
- **Moved to implementation:** play log CSV quoting and GUID session ids.
- **Post-MVP:** 50-hour drift test, latency PlayMode test.

## Specialist Disagreements
- **Payout timing:** systems-designer wants a direct call in the tick; network-programmer wants sim-side listener requests. The creative-director backs the direct call: it is simpler and correct for saves.
- **Performance criteria:** qa-lead and unity-specialist tightened them; the creative-director loosened them for a solo MVP.

## Senior Verdict [creative-director]
The architecture is right: requests in, events out, slices, a fixed tick. The blockers are local contradictions, not structural flaws. The most important fix is #1, because it is the only finding where the game breaks its own promise ("The Ledger Never Lies").

**Rough scope signal:** M. The producer should verify this before sprint planning.

## Verdict: NEEDS REVISION
