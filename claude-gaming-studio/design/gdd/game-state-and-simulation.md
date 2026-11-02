# Game State & Simulation

> **Status**: Approved
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02 (amendment: Placement Spawn/Remove calls, from the Economy review)
> **Last Verified**: 2026-11-01
> **Implements Pillar**: Pillar 3 (The World Shows the Damage); production rule "Co-op Helps, Solo Works" (enabling)
> **Creative Director Review (CD-GDD-ALIGN)**: REVISED 2026-11-01 (4 minor concerns applied)

## Summary

The single honest ledger for a play session: it holds every runtime value (water left, tank contents, held water, credits, machines, drawdown progress, play time) as plain data, advances it on a fixed 10 Hz tick, takes changes only as validated requests, and announces what happened through domain events. The player never sees it, but it is why the waterline always matches the tank, why a drained lake stays drained, and why co-op can be added later without a rewrite. It also writes the play log that the MVP's pacing tests are measured with.

> **Quick reference** — Layer: `Foundation` · Priority: `MVP` · Key deps: `None (Foundation); depended on by every gameplay system`

## Overview

Game State & Simulation is the single source of truth for everything that is *currently true* in a play session: how much water is left in each source, what is in each tank and in the player's hands, how many credits the player has, which machines they own and whether they are running, which drawdown thresholds have passed, and how long the session has run. It holds these as plain data, advances them on a fixed simulation tick that does not depend on frame rate, and announces what happened through a small set of domain events (`LaunchCompleted`, `Purchased`, `DrawdownThreshold`, `Reveal` and others) that presentation systems listen to. It also writes the play log that the MVP's two tests are measured with.

The player never touches this system directly. They feel it through its effects: the waterline always matches the tank, a drained lake stays drained after a reload, and a purchase never double-charges. It exists so that every other system has one honest ledger to read from and so that co-op can be added later without a rewrite (requests in, events out, no authoritative numbers on GameObjects). Implementation choices (tick driver, number representation, event dispatch, save serialiser, statics policy, object IDs) are deliberately left to ADRs listed under Open Questions.

*Framing: data/infrastructure layer (recommended for `Core`). No existing ADR to cite. Fantasy: indirect.*

## Player Fantasy

**"The Ledger Never Lies."** The world keeps the books. Every litre the player takes is counted, it stays gone, and the planet remembers what they did even when the corporation doesn't care. The feeling is *trust*: the player believes the landscape over the HUD, because the two never disagree.

- **Anchor moment:** early on, the tank level rises by exactly what the player carried in their spoon or bucket. The payoff comes at pump scale: the first time the player looks up from a full tank and sees the lake's waterline has visibly moved.
- **Tone:** dry and precise, like an auditor. The numbers are honest; the irony comes from what they are used to celebrate. The radio thanks the player for each counted litre on the same tick that the landscape shows the same total as loss (Pillar 4, Bleak Corporate Satire).
- **Follows from it:** permanence ("the hole stays dug" across saves) and, later, shared truth in co-op ("we both see the same lake").
- **Design test for this system:** if a choice trades *state consistency* for convenience (faking a waterline, rounding away sub-litre amounts the player can see, letting state drift on reload), choose consistency. This is not a call for strict simulation (anti-pillar): physics and presentation stay assisted and readable, as long as the ledger records exactly what moved and the world displays that. When the world and the numbers disagree, this system has failed. This serves Pillar 3: *"Waterlines recede, lakebeds and wrecks appear, glacier faces change: progress is visible in the landscape."*

*Shaped with `creative-director` (three framings offered; "The Ledger Never Lies" chosen, with the satire framing as its tone).*

## Detailed Design

### Core Rules

1. **The container holds runtime values only.** State is split into slices, one per owning system:
   - Water Source: volume remaining per source.
   - Hose/Storage Network: tank contents, connections.
   - Pumps & Machines: owned tier, on/off, running state.
   - Hand Tools: water held in each tool, keyed by `PlayerId` (litres); later, ice held. The MVP has one player, id 0.
   - Economy: credits, items owned, crate records (crate id, item, state Inbound/Landed/Opened, landing timer in whole ticks, landed tick).
   - Drawdown: thresholds passed, reveals triggered.
   - Shipping & Launch: pad state, countdown timer, litres aboard, total shipped (added by the Shipping GDD, 2026-11-02).
   - Placement (owned by this system's Placement module): pose of every placed object (pumps, tanks, crates, litter, hose ends) and, per `PlayerId`, the player's pose and hands item, as plain-data position and rotation. See Rule 12.
   - Session: session id, tick count, accumulated play time in seconds, and the next object id.
   Designed numbers (rates, capacities, prices, source start volumes, payouts) live in the Economy tuning table, never in state.
2. **One owner per slice.** Only the owning system's methods may change its slice. Any system may read any slice. No engine type (GameObject, MonoBehaviour, `Vector3`, `Quaternion`, `EntityId`) is ever authoritative or stored in state; positions are stored as the game's own plain-data `Pose` struct. Objects are referred to by stable IDs. Authored level objects (sources, sockets, reveals, pre-placed props) use ids below 1,000,000, assigned in the level asset. Runtime objects get ids that **only the simulation creates**, from the Session slice's id counter (saved), which starts at 1,000,000. Requests never carry a new id; the event that announces an object (for example `Purchased` for a crate) hands it out.
3. **Fixed simulation tick.** The simulation advances in fixed steps of `dt` (default 0.1 s, 10 Hz), driven by an accumulator fed by unscaled real frame time, independent of frame rate and of the physics step. All rates are defined per second, so for continuous quantities (volumes, credits from continuous flow) changing the tick rate changes results by no more than a relative 1e-6. Timers (countdowns, cycles, delivery delays) are stored as whole ticks, so they quantise to the tick by design and are excluded from that tolerance. Visuals interpolate between ticks: each presenter keeps the previous tick's value itself; it is never saved. Co-op will be host-authoritative, so no lockstep determinism is required. At boot, `maxCatchUp` is clamped to at least `dt` (with a warning), so the simulation can never freeze.
4. **Tick order (every tick, always the same):**
   1. Apply queued requests, first in first out.
   2. Systems step in fixed order, numbered the same in every GDD:
      - **2a** Pumps & Machines (set each pump's state and rate)
      - **2b** Hose/Storage Network (flow solve)
      - **2c** Shipping & Launch (advance countdown and cycle timers; at liftoff, credit the launch)
      - **2d** Water Source (drained check, `SourceDrained`)
      - **2e** Drawdown (thresholds and reveals)
      - **2f** Economy (advance crate delivery timers, derive shop states)
      Hand Tools has no step of its own: every hand action (scoop, pour) is a request applied in step 1.
   3. Flush queued events to listeners.
   4. Perform a pending save, if any, but only when the request queue is empty; otherwise the save waits for the next tick's step 4.
5. **Requests, not writes.** Interaction, UI, physics callbacks and listeners never change state. They submit requests (for example "pour held water into tank T", "buy item X"). Every request carries its issuer's `PlayerId` (always 0 in the MVP) and states an intent, never a quantity ("pour into T", not "pour 7 L"; crank start/stop, not litres). The only exception is a pose, in the Placement requests of Rule 12, which the simulation validates. Each request is validated against the state at the moment it is applied; a rejected request raises `RequestRejected(issuer, reason)` and changes nothing. This is the seam that becomes host-authoritative co-op later.
5b. **Consequences are direct calls, events are notifications.** When one system's step causes a change in another system's slice, it calls that system's method directly during the tick, in tick order: at liftoff Shipping calls `Economy.CreditLaunch(litres)` (step 2c); a purchase grants the item through the owning system's method in the same step 1 that validates it. Events never cause a state change. This keeps every tick self-contained, so a save can never capture half of a consequence.
6. **Event bus.** Events raised during a tick are queued and delivered at step 3, in the order raised, synchronously, at most once. Only a slice's owner raises events; listeners never raise them (the per-flush cap stays as a safety net). Listeners are presentation only (Radio, HUD, Audio, Drawdown visuals, Play Log): they receive a read-only view of state, never change it and never submit requests in response to events. Events raised outside a tick (`SessionStarted`, `SessionEnded`, `Paused`, `Resumed`, a `Saved` while paused, and the `Saved` from step 4) are flushed immediately when raised, stamped with the current tick count. A listener that fails is logged and skipped; the others still run. The bus belongs to the container and is created fresh every time a session boots.
7. **MVP domain events (13):** `SessionStarted`, `SessionEnded`, `Paused`, `Resumed`, `Saved`, `RequestRejected(issuer, reason)`, `Purchased(buyer, item, crateId)` (Economy), `LaunchStarted(litres)` and `LaunchCompleted(litres)` (Shipping), `SourceDrained(source)` (Water Source), `PoolFull(pool)` (Network), `DrawdownThreshold(source, pct)` and `Reveal(id)` (Drawdown). Each carries the tick it happened on and enough ids (who, what) to be presented without reading state. There is no `Milestone` event: the Radio derives milestones (first launch, first pump) from these events.
8. **Session clock.** Play time = seconds saved at load + ticks since load × `dt`. It is saved in seconds, so it accumulates across loads and is never rescaled if `dt` changes in a patch. Timers are converted from seconds with `ticks = max(1, round(seconds / dt))` when they start; a saved timer stores its remaining seconds and is converted again on load. Wall-clock time is recorded only in the play log.
9. **Pause.** Pause is this system's own state: while paused, no ticks run and the session clock stops; rendering and menus continue. No system may pause the simulation with `Time.timeScale`. The accumulator is reset to 0 on every resume (including after the Editor's own pause button or a breakpoint), so resuming never triggers catch-up ticks. Losing window focus pauses the game (focus comes through a replaceable focus source, so tests can fake it; the build enables Run In Background so menus keep rendering); it resumes only when the player clicks Resume or presses the pause key again. Pausing unlocks the cursor. **Presentation freezes too:** entering Paused sets `Time.timeScale = 0` as a presentation side effect, so physics props, animators and particles stop; menus use unscaled time. The simulation never reads `timeScale`. Focus-pause defaults to Off in the Editor and On in builds. Boot unsubscribes and re-subscribes the focus and quit hooks, so they never pile up across Play presses.
10. **Play log (MVP spec).** One CSV file per session, one row per domain event, written and flushed as it happens, numbers written in a culture-invariant format (decimal point, never comma). Location: `PlaytestLogs/session_<id>.csv` in the project folder when running in the Editor; the platform's per-user application data folder in builds. Columns: `session_id, wall_utc, session_time_s, tick, event, detail, value, credits_after, lake_drained_pct, since_prev_same_s`. `since_prev_same_s` is keyed by (event, source): it gives time between purchases and, on `DrawdownThreshold` rows, time since the previous threshold row of the same source whose value is a multiple of 10 (so the 10% row measures 0→10%, not 5→10%); the 1%, 2.5% and 5% rows measure from the previous threshold row. Times are written to 0.1 s with invariant formatting. `lake_drained_pct` is the farm pond's value. The log is write-only: nothing in the game reads it.
11. **Clean start every Play.** Booting clears state, the request queue, the accumulator and all subscribers. Nothing carries over between Play presses in the Editor (Unity 6.6 keeps static values between Play sessions by default).
12. **Placement.** The Placement module owns where things are. It changes only through these requests, each validated at step 1 (the issuer must be within interact range of the target pose, on valid ground, not overlapping): `PlaceItem(issuer, item, pose)`, `Drop(issuer, item, pose)`, `PickUp(issuer, id)`, and the controller's `PlayerPose(issuer, pose)`, sent once per tick. Physics props (crates, litter, dropped items) move in presentation; when one comes to rest, its presenter sends `PropSettled(id, pose)`, recorded at step 1. `PropSettled` is a host-only report (no player, no range check); clients never send it. A save reads only the recorded poses. In co-op, physics runs on the host and these reports come from the host. Simulation systems may also change Placement through four direct calls (Rule 5b), never through requests: `Placement.Spawn(id, item, pose)`, `Placement.Remove(id)`, `Placement.Move(id, pose)` and `Placement.SetHands(player, id)`. In the MVP only Economy (crate landing, unboxing into hands or onto the ground, empty-crate removal; Economy rule 8) and the Network (`SwapStorage` swapping two tanks' poses; Hose rule 12) call them.

### States and Transitions

| State | Ticks run? | Entered when | Exits to |
|---|---|---|---|
| Booting | No | Play starts / game launches | Menu (after state, queue, accumulator and subscribers are cleared) |
| Menu | No | Booting done, quit to menu, or a load failed | Loading (New Game or Continue); Quitting |
| Loading | No | New Game or Continue chosen | Running on success; Menu with an error and a "new game" offer if the save is unreadable |
| Running | Yes | Loading done, or resumed | Paused, Quitting |
| Paused | No | Pause menu opened or window focus lost | Running (Resume); Menu (Quit to menu: raises `SessionEnded`, saves, closes the play log); Quitting (Quit game) |
| Quitting | No | Player quits | Raises `SessionEnded`, saves, closes the play log, exits |

**Saving is a guarded action, not a state.** In Running it is performed at step 4 (the queue is always empty there, because step 1 drains it and nothing inside a tick submits requests); in Paused, on Quit to menu, and in Quitting it is performed immediately, and **any requests still queued are dropped and logged by design** (they are unvalidated intents, so nothing in the ledger is lost); in Booting, Menu and Loading a save request is refused and logged. Invalid state transitions (for example Loading → Paused) are ignored and logged.

### Interactions with Other Systems

| System | Reads | Writes (own slice, through its own methods) | Subscribes to |
|---|---|---|---|
| First-Person Controller & Interaction | State, for prompts | Nothing; submits requests only | `RequestRejected` |
| Economy, Progression & Shop | Credits, items owned; tuning table | Credits, items owned (`CreditLaunch` is called by Shipping at liftoff; purchases validated at step 1) | — |
| Water Source Model | Start volume (tuning table) | Volume remaining (`Withdraw()`); raises `SourceDrained` | — |
| Hose, Storage & Flow Network | Pump states, source `Available` | Tank contents, connections; raises `PoolFull` | — |
| Hand Tools | Source, tanks | Held water | — |
| Pumps & Machines | Items owned | Pump states (items granted through its method at purchase) | — |
| Shipping & Launch | Pad pool contents | Launch state; calls Network `Take` and `Economy.CreditLaunch`; raises `LaunchStarted`, `LaunchCompleted` | — |
| Drawdown Visuals & Reveals | Volume remaining | Thresholds passed; raises `DrawdownThreshold`, `Reveal` | — |
| Corporate Radio | Any slice | Nothing in state; keeps its own played-lines record, which Save & Load stores as a separate presentation block | Domain events |
| Audio (later), Onboarding (later) | Any slice | Nothing | Domain events |
| Save & Load | All slices | All slices, only while Loading | `Saved` |
| HUD | Credits, tank level | Nothing | `Purchased` |
| Play Log (inside this system) | Session, credits, drawdown | The CSV file only | All domain events |

All MVP GDDs now exist and confirm their slices and events (checked at the 2026-11-02 design review).

## Formulas

The **simulation_step** formula is defined as:

`each frame: acc = min(acc + frameDt, maxCatchUp); n = 0; while acc ≥ dt − ε and n < 5 { Tick(); acc = max(acc − dt, 0); n++ }; if n == 5 then acc = min(acc, dt − ε); alpha = acc / dt`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| dt | double | 0.033–0.2 s | data file (tuning knob) | Length of one simulation step |
| frameDt | double | 0 s and up | calculated (unscaled real frame time) | Real time elapsed this frame |
| maxCatchUp | double | 0.2–1.0 s | data file (tuning knob) | Most simulated time made up in one frame |
| acc | double | 0–maxCatchUp | calculated | Time owed to the simulation |
| alpha | double | 0–1 | calculated | Interpolation factor for visuals between the last two ticks |
| ε | double | 1e-9 s | constant | Tolerance so floating-point sums of frame times don't lose a tick |

**Output Range:** 0 to `min(5, floor(maxCatchUp / dt))` ticks per frame (5 at defaults; capped at 5 so a fast tick rate can't run 30 ticks in one frame). `maxCatchUp` is clamped to at least `dt` at boot. At extremes, time beyond `maxCatchUp` is dropped, never queued.
**Example:** at exactly 60 fps (`frameDt` = 1/60 s) and `dt` = 0.1 s, a tick runs on every 6th frame (frames 6, 12 … 60 give 10 ticks). A 2 s hitch with `maxCatchUp` = 0.5 s runs 5 ticks and drops 1.5 s.

The **session_time** formula is defined as:

`SessionTime += dt` (each tick; saved as seconds)

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| SessionTime | double (s) | 0 and up | Session slice, saved | Play time across all loads |
| dt | double | 0.033–0.2 s | data file | Step length |

**Output Range:** 0 s upward. Tick count is kept separately (long) for event stamps.
**Example:** a 20-minute MVP session at 10 Hz is 12,000 ticks = 1,200 s.

The **since_prev_same** formula (play log) is defined as:

`since_prev_same_s = SessionTime_now − SessionTime_prev_same_event`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| SessionTime_now | double | 0 s and up | calculated | Session time of this event |
| SessionTime_prev_same_event | double | 0 s and up, or none | calculated | Session time of the previous row with the same (event, source); for thresholds, the previous multiple-of-10 row when this row is a multiple of 10 |

**Output Range:** 0 s upward; empty for the first event of a type.
**Example:** purchases at 212.4 s and 341.0 s give `since_prev_same_s` = 128.6 for the second.

*Formulas proposed by `systems-designer`; tick rate default chosen at 10 Hz (gameplay-programmer suggested 20 Hz; both fit the 5–30 Hz safe range and the final value is set in the tick ADR).*

## Edge Cases

- **If a frame takes longer than `maxCatchUp`** (hitch, loading spike): at most `maxCatchUp` of simulated time is made up and the rest is dropped. The simulation never spirals into catching up forever.
- **If the window loses focus**: the game pauses and the accumulator is reset to 0, so returning never triggers catch-up ticks.
- **If a save is requested mid-tick**: it waits until step 4, after events flush, so a save never captures a half-finished tick.
- **If a listener raises an event during the flush**: it joins the back of the same flush. If a flush exceeds the per-flush cap (default 256), the excess is dropped and logged as an error (protects against event loops).
- **If an event has no subscribers**: nothing happens except the play log row (every event gets a row, including events raised outside a tick).
- **If the same request arrives twice** (double click on Buy): the second is validated after the first is applied; if credits are now short or the item is unique and owned, it is rejected with `RequestRejected` and no double charge happens.
- **If several drawdown thresholds are crossed in one tick**: one `DrawdownThreshold` event per threshold, in ascending order, all carrying the same tick.
- **If a request arrives while Paused**: it is queued and applied on the first tick after resuming (UI should disable buttons while paused anyway).
- **If the player presses Play again in the Editor**: Booting clears state, the queue, the accumulator and every subscriber, so nothing from the previous run leaks in (domain reload is off by default in Unity 6.6).
- **If a session runs for many hours**: play time is saved seconds plus ticks since load × dt, so it does not drift. Volume precision at very large scales is handled by the number-representation ADR; conservation checks compare with a small tolerance, never exact equality.
- **If the save file is corrupt or from a newer version**: loading fails, the file is kept untouched (never overwritten), and the player is offered a new game.
- **If the play log cannot be written** (locked file, full disk): one warning, logging switches off for the session, gameplay is unaffected.
- **If a knob is outside its safe range**: `dt` is clamped into 0.033–0.2 s, then `maxCatchUp` into [max(dt, 0.2), 1.0] s, at boot, with one warning each.
- **If a save is requested while requests are still queued** (Paused, Quit to menu, Quitting): the queued requests are dropped and logged, then the save is written. In Running the queue is always empty at step 4.
- **If a listener fails while handling an event**: the error is logged, that listener is skipped for that event, and the remaining listeners still receive it.

## Dependencies

**Upstream (this system depends on):** none. It is the Foundation layer.

**Engine:** Unity 6.6 main loop (frame time for the accumulator), application focus and quit callbacks, file system for the play log and saves.

**Downstream (depend on this system):**
| System | Nature | Interface |
|---|---|---|
| First-Person Controller & Interaction | Hard | Submits requests; receives `RequestRejected` |
| Economy, Progression & Shop | Hard | Owns credits/items slice; `CreditLaunch` called by Shipping at 2c; raises `Purchased`; steps at 2f |
| Water Source Model | Hard | Owns volume slice; `Available`, `Withdraw()` |
| Hose, Storage & Flow Network | Hard | Owns tanks/connections slice; flow solve at step 2b; raises `PoolFull` |
| Hand Tools | Hard | Owns held-water slice |
| Pumps & Machines | Hard | Owns pump-state slice; steps at 2a |
| Shipping & Launch | Hard | Owns Shipping slice; steps at 2c; raises `LaunchStarted`, `LaunchCompleted` |
| Drawdown Visuals & Reveals | Hard | Owns thresholds slice; steps at 2e; raises `DrawdownThreshold`, `Reveal`; reads `alpha` for interpolation |
| Corporate Radio | Hard | Subscribes to domain events |
| Save & Load | Hard | Serialises all slices; writes them only while Loading |
| HUD | Soft | Reads slices; subscribes to `Purchased` |
| Audio, Onboarding (VS); Network Spike, Co-op Networking (later) | Hard | Events; requests become network commands |

All MVP downstream GDDs exist (2026-11-02) and use the step labels 2a–2f.

## Tuning Knobs

| Knob | Default | Safe range | Too low | Too high | Interacts with |
|---|---|---|---|---|---|
| Tick rate (`1/dt`) | 10 Hz | 5–30 Hz | Visible stepping in water levels if interpolation is off; coarse event timing | More CPU per frame; no gameplay benefit since rates are per second. Above 10 Hz the 5-tick cap binds first: at 30 Hz, frames slower than about 6 fps run the simulation in slow motion | `maxCatchUp` (ticks per frame = `min(5, maxCatchUp / dt)`) |
| `maxCatchUp` | 0.5 s | 0.2–1.0 s, and ≥ dt (clamped to dt at boot) | More simulated time dropped on hitches | Longer stalls after a hitch | Tick rate |
| Max events per flush | 256 | 64–1024 | Legitimate bursts (many thresholds at once) truncated | Event loops take longer to detect | — |
| Autosave interval | 120 s (plus after every purchase) | 60–600 s | Frequent small hitches | More progress lost on a crash | Save & Load |
| Pause on focus loss | On in builds, Off in the Editor | On / Off | — | — | Turn off for long unattended tests |
| Play log enabled | On in development builds | On / Off | MVP tests (a) and (b) cannot be measured | — | — |

These knobs belong to this system (they are engine-timing values, not game-balance numbers), so they live in this system's settings rather than the Economy tuning table.

## Visual/Audio Requirements

None of its own. This system has no visuals or sounds. It provides `alpha` so that visual systems can interpolate smoothly between ticks, and domain events that audio and VFX react to (requirements for those live in Drawdown Visuals & Reveals, Shipping & Launch, Hand Tools and Pumps & Machines).

## Game Feel

Not player-operated, so no feel targets of its own. Its feel obligation: the ledger commits a player's request on the next tick (up to 0.1 s at defaults), and interpolated visuals of the result can trail by up to about 0.2 s. So **presentation must give instant feedback on input** (sound, splash, tool animation) in the same frame, without waiting for the tick; only the authoritative numbers (tank level, credits) follow the tick. Total input-to-number latency budget: ≤ 0.2 s at defaults.

## UI Requirements

None of its own. It exposes data for the HUD and the pause menu. The only UI it implies is a load-failure message ("Save could not be read; start a new game?") owned by Save & Load.

## Cross-References

Every MVP GDD in `design/gdd/` references this document for its state slice, its events and its tick step (2a–2f); see the Dependencies table. Review record: `design/gdd/reviews/game-state-and-simulation-review-log.md`. The systems index (`design/gdd/systems-index.md`) and the concept's Technical Considerations guardrails are the sources for the ownership rules used here.

## Acceptance Criteria

Tags: **[EditMode]** = pure C# NUnit test with no scene and a fake clock or file system. **[PlayMode]** = automated, but needs the Unity loop. **[Manual]** = a QA tester checks it by hand.

- **AC-R1 Runtime-only state [EditMode]:** GIVEN a fresh container, WHEN each slice's fields are compared with the approved schema list in this GDD (Rule 1), THEN there are exactly 9 slices (Water Source, Network, Pumps, Hand Tools, Economy, Drawdown, Shipping, Placement, Session) and no field outside the list exists.
- **AC-R2 Single owner, no engine types [EditMode]:** GIVEN every slice type, THEN it has no public setters or public mutable fields (mutators are internal to the owner's assembly), and no field is a `UnityEngine.Object`, `Vector3`, `Quaternion` or `EntityId`.
- **AC-R3 Tick rate independence for continuous quantities [EditMode]:** GIVEN a stub system that withdraws R = 3 L/s from a 1,000 L stub source and requests timestamped at multiples of 0.1 s, WHEN 60 s run at 10 Hz and at 20 Hz, THEN the source holds 1,000 − 180 L in both runs within relative 1e-6.
- **AC-R4 Tick order [EditMode]:** GIVEN spy systems that record their calls, WHEN one tick runs, THEN the order is: requests (arrival order) → 2a Pumps → 2b Network → 2c Shipping → 2d Water Source → 2e Drawdown → 2f Economy → event flush → pending save.
- **AC-R5 Rejected request changes nothing [EditMode]:** GIVEN a fake tuning table with an item priced 50 and credits 10, WHEN "buy item" is applied, THEN a field-by-field deep comparison of state before and after finds no difference, and exactly one `RequestRejected(issuer 0, reason)` is delivered.
- **AC-R6 Event bus [EditMode]:** GIVEN events A, B and C raised in one tick and a listener that throws on B, WHEN the flush runs, THEN the other listeners get A, B, C in that order, exactly once each, and one error is logged.
- **AC-R7 Events carry the tick [EditMode]:** GIVEN a test harness that raises each of the 13 MVP event types at tick 42, THEN each is delivered with tick = 42.
- **AC-R8 Session clock survives a reload [EditMode]:** GIVEN 100.0 s of play is saved and loaded, WHEN 200 ticks run at dt = 0.1, THEN session time = 120.0 s ± 0.001.
- **AC-R9 Pause on focus loss [EditMode + Manual]:** GIVEN Running and a fake focus source, WHEN focus is lost and a fake clock advances 5 s, THEN tick count is unchanged, acc = 0, the state is Paused after focus returns, and Running only after Resume. **[Manual]** In a build, alt-tab away and back: the pause menu responds to hover.
- **AC-R10 Play log [EditMode]:** GIVEN a fake file system and the de-DE culture, WHEN a `Purchased` event is flushed, THEN the file holds the header plus 1 row with the 10 columns in order, the value 1234.5 is written as `1234.5`, and the file is flushed before the next tick. The test restores the culture afterwards. **[Manual]** In a build, the file lands in the per-user application data folder.
- **AC-R11 Clean start every Play [EditMode]:** GIVEN `Boot()` has run with queued requests, a non-zero accumulator and subscribers attached, WHEN `Boot()` runs again in the same domain, THEN state, queue, accumulator and subscriber count are at their defaults. **[Manual]** Unity's static-field analysis reports no mutable statics in simulation code.
- **AC-F1 Simulation step [EditMode]:** GIVEN dt = 0.1, maxCatchUp = 0.5 and acc = 0, WHEN frameDt = 2.0, THEN exactly 5 ticks run and acc < dt. WHEN 60 frames of exactly 1/60 s run in doubles, THEN exactly 10 ticks run, on frames 6, 12 … 60, and alpha stays in [0, 1) on every frame.
- **AC-F2 Session time [EditMode]:** GIVEN 12,000 ticks at dt = 0.1, THEN session time = 1,200.000 s ± 0.001.
- **AC-F3 Time since the previous same event [EditMode]:** GIVEN purchases at 212.4 s and 341.0 s, THEN the rows read empty and then `128.6`. GIVEN pond thresholds 5 at 300.0 s, 10 at 420.5 s and 20 at 700.0 s, with a purchase in between, THEN the 5 row reads empty (first threshold row), the 10 row reads `420.5` (the first multiple-of-10 row measures from session start) and the 20 row reads `279.5`.
- **AC-E1 Double-click buy [EditMode]:** GIVEN credits for exactly one unique item, WHEN two buy requests are queued for the same tick, THEN 1 `Purchased` and 1 `RequestRejected`, and credits are charged once.
- **AC-E2 Several thresholds in one tick [EditMode]:** GIVEN a withdrawal that crosses 10%, 20% and 30%, THEN 3 `DrawdownThreshold` events with values 10, 20, 30, in that order, all with the same tick.
- **AC-E3 Event loop cap [EditMode]:** GIVEN a listener that raises a new event on every event, WHEN one seed event is flushed, THEN exactly 256 events are delivered (the seed counts), the rest are dropped without play log rows, and 1 error is logged for the flush.
- **AC-E4 Request while paused [EditMode]:** GIVEN Paused, WHEN a request is submitted, THEN state doesn't change until resume, and the request is applied in the first tick after resume.
- **AC-E5 Save timing [EditMode]:** GIVEN a save requested during tick N, THEN a spy records flush end, then the save, then `Saved` delivered with tick N, and the snapshot's tick is N. GIVEN Paused with one queued request, WHEN a save is requested, THEN it is written immediately, the queued request is dropped with one log entry, and after resume the request is not applied.
- **AC-E6 Unreadable save [EditMode]:** GIVEN fixtures (0-byte file, truncated file, invalid syntax, version = current + 1), WHEN loading, THEN Load returns `Failed(reason)`, the state is Menu, and each file's SHA-256 is unchanged. **[Manual]** Check the message wording.
- **AC-E7 Play log can't be written [EditMode]:** GIVEN a fake file system that throws on write, THEN exactly 1 warning is logged, logging turns off, and later ticks match a run with no log.
- **AC-E8 Invalid transition and refused save [EditMode]:** GIVEN Loading, WHEN a Paused transition is requested, THEN the state stays Loading and one log entry is written. GIVEN Booting, WHEN a save is requested, THEN it is refused and logged.
- **AC-E9 Launch payout is atomic across a save [EditMode, integration]:** GIVEN a fake tuning table at 10 cr/L, the real `Economy.CreditLaunch`, and a stub Shipping that lifts off with 200 L at step 2c of tick N, WHEN saves are taken at the end of ticks N−1 and N, THEN the N−1 snapshot has neither the 200 L shipped nor the 2,000 credits, and the N snapshot has both.
- **AC-E10 Out-of-tick events [EditMode]:** GIVEN Running at tick 500, WHEN the game pauses, THEN `Paused` is delivered immediately with tick 500. WHEN the player quits, THEN `SessionEnded` is delivered, the save is written, and the play log is closed, in that order.
- **AC-E11 Load starts the session [EditMode]:** GIVEN a valid save, WHEN loading succeeds, THEN `SessionStarted` is delivered and the state is Running.
- **AC-E12 Autosave triggers [EditMode]:** GIVEN Running at dt = 0.1, WHEN 1,200 ticks pass after the last save, THEN one save runs on that tick; WHEN a purchase is applied at step 1, THEN a save runs at that tick's step 4 and the 1,200-tick interval restarts.
- **AC-E13 Knob clamps [EditMode]:** GIVEN dt = 0.2 and maxCatchUp = 0.1, WHEN booting, THEN maxCatchUp becomes 0.2, one warning is logged, and a 1 s frame runs exactly 1 tick.
- **AC-E14 Ids come only from the simulation [EditMode]:** GIVEN the id counter at 1,000,041, WHEN a purchase creates a crate, THEN the crate's id is 1,000,041, `Purchased` carries it, the counter is 1,000,042, and the counter survives save and load.
- **AC-E15 Menu and quitting from pause [EditMode]:** GIVEN Paused, WHEN Quit to menu is chosen, THEN `SessionEnded` is delivered, a save is written, the play log is closed, and the state is Menu; WHEN Quit game is chosen from Paused, THEN `SessionEnded` is delivered, the save is written and the state is Quitting.
- **AC-E16 Placement is validated [EditMode]:** GIVEN player 0 at (0,0,0) with interact range 2 m, WHEN `PlaceItem` targets a pose 3 m away, THEN `RequestRejected(too_far)` and Placement is unchanged; WHEN `PropSettled(crate, pose)` arrives, THEN the crate's recorded pose equals it at step 1 of the next tick, and a save stores that pose.
- **AC-E17 Ids [EditMode]:** GIVEN a level with authored ids 1–500, WHEN a new game starts and a crate is created, THEN its id is 1,000,000 and no authored id is reused.
- **AC-E18 Timers survive a dt change [EditMode]:** GIVEN a 3 s timer with 1.0 s left saved at dt = 0.1, WHEN loaded at dt = 0.05, THEN it finishes after 20 ticks.
- **AC-E19 Resume resets the accumulator [EditMode]:** GIVEN Paused with acc = 0.08, WHEN Resumed is delivered, THEN acc = 0 and `Resumed` carries the current tick; GIVEN a new game, WHEN loading finishes, THEN `SessionStarted` is delivered.
- **AC-E20 Purchase grant in the same step [EditMode]:** GIVEN a valid buy request for the bucket, WHEN step 1 applies it, THEN credits, items owned and the Hand Tools slice all show the bucket before step 2a runs.
- **AC-E21 Pause freezes presentation [Manual]:** GIVEN a crate tumbling and the pump animating, WHEN the game pauses, THEN both freeze, the pause menu animates, and both continue exactly on resume. **[EditMode]** No simulation code reads `Time.timeScale` (static search).
- **AC-X1 Pour request across systems [EditMode, integration with stubs]:** GIVEN stub owners for Hand Tools, Network, Water Source and Shipping, a tool holding 20 L and tank T at 30 of 100 L, WHEN "pour into T" from player 0 is applied, THEN in that tick T reads 50 L, held reads 0 L, and held + tanks + sources + litres aboard + shipped is unchanged within relative 1e-6. *(Partial pours: Hose/Storage Network AC-11.)*
- **AC-P1 Performance [PlayMode, Unity Performance Testing]:** GIVEN a synthetic fixture (20 sources, 50 tanks, 30 pumps, 10 events per tick) on Nour's development Mac, THEN p99 tick cost ≤ 1.0 ms over 12,000 ticks and a 5-tick catch-up frame ≤ 5 ms. **[Manual]** The profiler shows no steady per-tick allocation on ticks with no events.

*Drafted with `qa-lead`; revised twice on 2026-11-02 after the design review and re-review (qa-lead, systems-designer, unity-specialist, network-programmer, creative-director).*

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| ADR: tick driver and rate (own accumulator in Update vs FixedUpdate; final rate within 5–30 Hz) | technical-director | Technical Setup (`/architecture-decision`) |
| ADR: number representation and rounding for litres and credits (`double` vs `long` millilitres) | technical-director + economy-designer | Before the Economy GDD is finalised |
| ADR: event dispatch (deferred flush, ordering, re-entrancy cap) | technical-director | Technical Setup |
| ADR: save serialiser and version migration (JsonUtility cannot store dictionaries; System.Text.Json under IL2CPP unverified) | technical-director | Technical Setup |
| ADR: statics policy with domain reload off | technical-director | Technical Setup |
| ADR: stable object IDs and the request/command shape for future co-op | technical-director + network-programmer | Before the Network Spike |
| Should focus loss pause the game in co-op, where one player alt-tabbing can't pause the host? | game-designer | Network Spike |
