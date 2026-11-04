# Corporate Radio

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 4 (Bleak Corporate Satire); Pillar 3 (The World Shows the Damage)

## Summary

The Corporate Radio is a cheerful company announcer on a radio by the launch pad. It reacts to what the player does (every launch, every purchase, every drawdown threshold and reveal) with short, upbeat lines that celebrate exactly what the landscape is losing. It is the MVP's only test of the satire pillar, so it is a real system, not polish.

> **Quick reference** — Layer: `Presentation` · Priority: `MVP` · Key deps: `Game State` (events)

## Overview

The radio listens to domain events and plays one line at a time from authored line pools, with subtitles. A small scheduler picks which event gets a line when several happen together, avoids repeats, and keeps the radio from talking over itself or nagging. It never changes game state and never submits requests. All its timers run on the simulation clock, so they stop while the game is paused. In the MVP, lines are text plus placeholder voice; final voice comes later (and may tie into the Voice Sound Packs idea).

## Player Fantasy

**"Your employer is so proud of you."** A warm, tireless voice congratulates you for every litre, never notices the mud, and treats the dying pond as a growth story. The joke lands because the radio and the landscape disagree: when the pond drops 10% and a rotting dock with a "Swim Safe" sign breaks the surface, the radio announces "a new quarterly record"; at about 15% a rusted shopping cart appears and the radio calls it a heritage asset.

- **Anchor moment:** the first reveal: the waterline uncovers something sad, a low sting plays, and a beat later the radio plays its celebratory jingle.
- **Tone:** deadpan, upbeat corporate (Pillar 4). Never winks, never lectures, never says "climate". The player draws the conclusion.

## Detailed Design

### Core Rules

1. **Events and pools.** The radio subscribes to these events, each with its own line pool and priority (higher first). Every line has a stable string ID, its subtitle text and its audio duration in the pool data file.

| Request kind | Priority | Pool size (MVP) | Example line (placeholder) |
|---|---|---|---|
| `Reveal` | 5 | 1 per reveal (dock ~10%, shopping cart ~15%) + 3 generic | "Look at that! The pond is giving back. Heritage assets recovered." |
| End beat (milestone, derived) | 5 | 2 (one after the first full drum + IBC launch, one if the farm pond runs dry first) | "The quarter is closed. The pond has been fully monetised. Please continue." |
| `DrawdownThreshold` | 4 | 1 per threshold for 1%, 2.5%, 5%, 10%, 20%, 30%; 3 generic for 40% to 100% | "Ten percent! Accounting has asked me to say: wow." |
| Milestone (derived by the radio) | 4 | 1 per milestone: first `LaunchCompleted`, first `Purchased` of each pump item (hand pump, electric motor, second electric pump), first `SourceDrained` of each source | "First launch! Mars thanks you for your 6 credits. Mars is very far away, but it thanks you." |
| `Purchased` | 3 | 1 per item + 3 generic | "The Hand Pump. Ten times the gratitude." |
| `SourceDrained` | 3 | 2 (puddle), 2 generic, plus 1 that shows `lastGrantLitres` | "The puddle has been fully optimised." / "{lastGrantLitres} litres of progress. Thank you." |
| `LaunchCompleted` | 2 | 12 full lines + 12 stingers | "Another shipment! Your contribution is noted. Somewhere." |
| Nag | 1 | 3 crate nags + 3 full-pad nags | "Your crate misses you." / "The rocket is ready when you are. It is always ready." |

2. **One event, at most one request.** Each event the radio receives makes at most one request. When an event is also a milestone, the milestone line replaces the base line (the first launch plays only the first-launch line, not a launch line as well). A pump milestone is simply a `Purchased` whose item is a pump item that has no played milestone yet, so the radio needs no memory beyond its played-lines record. The first-launch line shows the payout, computed from `LaunchCompleted.litres` with Economy's pure payout formula (`payout_mc` divided by 1,000, rounded down to whole credits); a 0.6 L spoon launch shows "6 credits", the price of the bucket (Shipping anchor moment).
3. **One line at a time, and a protected queue.** If a line is playing, new requests queue, stamped with their event's tick. The queue holds 3 (`queue_max`). When it is full and a request arrives:
   - if the incoming request has a priority below or equal to the lowest queued priority of 3 or less, the incoming request is dropped;
   - otherwise the oldest request with the lowest priority of 3 or less is evicted;
   - requests of priority 4 and 5 (thresholds, milestones, reveals, end beat) are never evicted and never go stale; if the queue holds only such requests, it grows past `queue_max` for them.
   A request of priority 3 or less that has waited 100 ticks (10 s) is dropped as stale news.
4. **Played means received.** A one-off line (milestone, threshold, reveal, end beat) is marked played when its event arrives at the step 3 flush of tick N, not when its audio ends. A save at step 4 of the same tick therefore always agrees with what the radio will play, and a protected request is never lost.
5. **Launch lines, stingers and the light (electric stage).** Every launch gets a reaction (the Pillar 4 test). The flood is the electric stage: from the motor (~7:00) to the IBC (~15:40) there are about 25 launches, one every 15 to 25 s, overlapping four thresholds, both reveals and two purchases. The early hand stages have only about 6 launches in the first 5 minutes. When a launch request reaches the front of the queue (decided at play time, not at queue time):
   - if the last full launch line was 45 s or more ago (`stinger_window`), a full line plays;
   - else if the last spoken launch reaction (full line or stinger) was 30 s or more ago (`stinger_min_interval`), a stinger plays (1 to 3 words: "Received!", "Thank you!", "Noted.");
   - else the radio's light blinks once with a click and nothing is spoken. The blink counts as the launch's reaction.
   In the reference run this gives about 15 full lines, 12 stingers and 8 light-only launches.
6. **No repeats.** Within a pool, a line is not reused until all others have played (shuffle bag), and never twice in a row across a reshuffle. Shuffles use a `System.Random` owned by the radio, never shared with the simulation.
7. **Figures in subtitles only.** Lines may show litres, credits, item names or the Earth counter ("…1,385,999,999,999,999,998,800 litres to go!"). Values come from the event payload or a read of state. The number appears only in the subtitle; the voice clip says something generic ("that many litres", "your credits"), so every clip can be pre-recorded.
8. **Simulation clock.** All radio timers (stale age, stinger window and interval, gap, nag delays, reveal delay, line duration) run on an injected radio clock that reads Game State's tick count, so time advances only while Running. Durations in seconds become whole ticks with `max(1, round(s/dt))` (Game State Rule 8). A timer of n ticks started in tick N fires in tick N+n (Game State Rule 3). On `Paused` the radio pauses its audio source; on `Resumed` it resumes it. The radio state is not changed by a pause.
9. **Line duration and subtitles.** A line occupies the radio for `display_time = max(audio_duration, 1.5 s + 50 ms per character)`, with `audio_duration` read from the pool data, not from the audio source. This works at volume 0 and in EditMode tests. Subtitles show for the same time. They are on by default and can be turned off in settings.
10. **Gaps.** After any line, 2 s of silence before the next.
11. **Reveal delay.** A reveal line starts 1.8 s (`reveal_delay`, 1.5 to 2 s) after its `Reveal` event, so the player hears Drawdown's quiet sting and sees the object first, and the cheer comes second. The reveal request waits at the front of the queue during the delay.
12. **Nags.** Nags are checked only when the radio is Idle with an empty queue, so they never queue or go stale.
    - **Crate nag:** fires once per crate, when the crate is still unopened 600 ticks (60 s) after its `landedTick` (Economy AC-31). It is exempt from the 90 s silence rule.
    - **Full-pad nag:** fires when the pad pool is full (read from the Network slice) and 600 ticks (60 s) have passed since that pool's latest `PoolFull` with no `LaunchCompleted` since. It also needs 90 s with no other line, and fires at most once until the next launch. It never fires while the player is filling the pad, however long that takes (the hand-pump stage can run 160 to 290 s between launches).
13. **End beat.** The radio derives the end beat itself: the first `LaunchCompleted` whose litres are at least the drum + IBC pool capacity (1,200 L, read from the tuning table), or, if that has not happened, the farm pond's `SourceDrained` (Economy Rule 14 and its pond-dry edge case). It plays once.
14. **Ducking.** While the radio speaks, other audio ducks by 4 dB.
15. **Never blocks play.** The radio never pauses the game, never needs input, and can be turned down or off in settings.
16. **Satire rule for writing:** the radio only ever celebrates. It never mentions harm directly; the irony comes from what the player sees at the same moment.

### States and Transitions

| State | Condition | Can go to |
|---|---|---|
| Idle | No line playing, queue empty | Speaking (request arrives and is due, or a nag fires) |
| Speaking | A line is playing, for its `display_time` | Gap (line ends) |
| Gap | 2 s silence | Speaking (a queued request is due); Idle |

The light-only launch reaction does not change state. A pause freezes the current state and its timers (Rule 8).

Saved: only the stable string IDs of one-off lines that have played (milestones, thresholds, reveals, end beat), so a reload doesn't replay them. Save & Load stores them as the Radio's presentation block. Shuffle bags, the queue and timers are not saved; they start fresh on load.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Played-lines record (for save) | Events: `LaunchCompleted`, `Purchased`, `DrawdownThreshold`, `Reveal`, `SourceDrained`, `PoolFull`, `Paused`, `Resumed`; the tick count (radio clock); a read of credits and totals. The radio derives milestones and the end beat itself (there is no Milestone event) and keeps its played-lines record outside simulation state. |
| Economy | — | Item names and kinds; the pure payout formula; crate records (`landedTick`, opened or not); Earth counter; drum + IBC capacity |
| Hose, Storage & Flow Network | — | Pad pool contents and capacity; `PoolFull` |
| Drawdown Visuals & Reveals | — | `DrawdownThreshold`, `Reveal` (threshold and reveal data are Drawdown's) |
| Water Source | — | `SourceDrained(source, lastGrantLitres)` |
| Save & Load | Played one-off line IDs (presentation block) | The same on load |
| Audio | Ducking signal | — |
| HUD | Subtitle text and display time | — |
| Settings (later) | — | Radio volume, subtitles on/off and size |

## Formulas

The **line_choice** rule is defined as:

`next = highest priority due request (ties: oldest); if next is LaunchCompleted: if now − last_full_launch_line ≥ stinger_window then pool = full launch lines; else if now − last_launch_reaction ≥ stinger_min_interval then pool = stingers; else blink light, no line; line = shuffle_bag(pool).draw()`

The **display_time** rule is defined as:

`display_time = max(audio_duration, subtitle_min + per_char × characters)`, converted to ticks with `max(1, round(s/dt))`

The **full_pad_nag** condition is defined as:

`padPoolFull and now − padFullTick ≥ full_pad_nag_delay and no LaunchCompleted after padFullTick and now − last_line_end ≥ idle_gate and not nagged since padFullTick`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| priority | int | 1–5 | this GDD | Per request kind |
| now | long (ticks) | 0 and up | radio clock (Game State tick count) | Advances only while Running |
| last_full_launch_line | long (ticks) | — | runtime | Tick the last full launch line started |
| last_launch_reaction | long (ticks) | — | runtime | Tick the last full launch line or stinger started |
| stinger_window | double (s) | 20–90 | data file | Default 45 s |
| stinger_min_interval | double (s) | 15–60 | data file | Default 30 s |
| queue_max | int | 2–5 | data file | Default 3 (priority 4–5 may exceed it) |
| audio_duration | double (s) | 0.3–8 | pool data file | Per line |
| subtitle_min, per_char | double (s) | 1.0–2.5, 0.03–0.08 | data file | Defaults 1.5 s and 0.05 s |
| padFullTick | long (ticks) | — | runtime | Tick of the latest `PoolFull` for the pad pool |
| full_pad_nag_delay | double (s) | 30–180 | data file | Default 60 s |
| idle_gate | double (s) | 60–180 | data file | Default 90 s |

**Output Range:** one line, one light blink, or nothing per scheduling step.

## Edge Cases

- **If a threshold and a reveal happen in the same tick**: the reveal plays first (priority 5) after its 1.8 s delay; the threshold line queues and is never dropped.
- **If three launches happen during a long reveal line**: queue limits apply to the launch requests only; any launch request that waits 100 ticks is dropped. Thresholds and milestones queued behind the reveal always play.
- **If the player buys and launches at once**: the purchase line plays first.
- **If a one-off line's event arrives just before a save**: it is already marked played, so it is not replayed after loading, even if its audio had not started.
- **If the game is paused mid-line**: the audio pauses and the radio clock stops; on resume the line continues and no queued request has aged.
- **If the radio is turned off**: events are still consumed (so one-off lines are marked played) and subtitles still show for their display time, unless the player turns subtitles off.
- **If a line's figure is huge (Earth counter)**: the subtitle shows digit groups; the voice clip is generic ("a great many litres to go").
- **If a threshold above 30% passes**: it draws from the 3 generic threshold lines (shuffle bag); it is still a one-off by its own ID (for example `threshold_40`).
- **If several thresholds pass in one tick** (Game State edge case): each makes its own protected request, in ascending order.
- **If the pond runs dry before the first full drum + IBC launch**: the pond-dry end-beat line plays; the other end-beat line never plays.
- **If the pool for an event is empty** (missing content): nothing plays; one warning is logged per pool; a one-off is still marked played.

## Dependencies

**Upstream:** Game State (Hard, events and tick count), Drawdown Visuals & Reveals (Hard, raises `DrawdownThreshold` and `Reveal`), Shipping (Hard, `LaunchCompleted`), Water Source (Hard, `SourceDrained`), Economy (Soft, names, payout formula, crate records, Earth counter), Hose, Storage & Flow Network (Soft, pad pool and `PoolFull`).
**Downstream:** Save & Load (Hard, presentation block), Audio (Soft, ducking), HUD (Soft, subtitles), Chapter Story & Milestones (Alpha, builds on it), Voice Sound Packs (later).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Stinger window | 45 s | 20–90 s | Shorter means more full launch lines |
| Stinger min interval | 30 s | 15–60 s | Shorter means fewer light-only launches |
| Queue size | 3 | 2–5 | Applies to priority 1–3 only |
| Stale request age | 10 s | 5–20 s | Priority 1–3 only |
| Gap between lines | 2 s | 1–4 s | — |
| Reveal delay | 1.8 s | 1.5–2 s | After Drawdown's sting |
| Crate nag delay | 60 s | 30–120 s | After `landedTick`; once per crate |
| Full-pad nag delay | 60 s | 30–180 s | After the pad's `PoolFull` |
| Idle gate (full-pad nag) | 90 s | 60–180 s | Silence needed before the full-pad nag |
| Subtitle minimum | 1.5 s + 50 ms per character | 1.0–2.5 s, 30–80 ms | Also the minimum line time |
| Ducking | −4 dB | −2 to −8 dB | — |

## Visual/Audio Requirements

- A chunky company radio on a post by the launch pad with a small light that glows while it speaks and blinks once (with a soft click) for a light-only launch reaction; the voice is positional near the pad and also mixed lightly into the player's ears so it's always audible.
- A three-note company jingle before one-off lines (milestones, thresholds, reveals, end beat).
- MVP: placeholder voice (Nour's recording or text-to-speech) for about 70 lines, all with generic spoken figures; final voice cast later.

## Game Feel

- A line should start within 0.5 s of its event when the radio is free, except reveal lines, which start 1.8 s after the reveal (Rule 11).
- Lines are short: full lines 2–6 s of audio, stingers under 1 s of audio (on screen for at least the subtitle minimum).

## UI Requirements

- Subtitles at the bottom of the screen with a small radio icon; readable size; on by default; shown for `display_time`.

📌 **UX Flag — Corporate Radio:** subtitles. Cover in `/ux-design` with the HUD.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 1, 8 | `design/gdd/game-state-and-simulation.md` | Event list (Rule 7), tick boundaries (Rule 3), ticks from seconds (Rule 8), pause (Rule 9) |
| Rules 1, 2, 12, 13 | `design/gdd/economy-progression-and-shop.md` | Item names, payout formula, crate `landedTick` (AC-31), end beat (Rule 14), Earth counter |
| Rule 5 | `design/gdd/shipping-and-launch.md` | Launch cadence; first-launch anchor (0.6 L, 6 credits) |
| Rules 1, 11 | `design/gdd/drawdown-visuals-and-reveals.md` | Threshold list (Rule 2), reveals (Rule 6), reveal sting |
| Rule 1 | `design/gdd/water-source-model.md` | `SourceDrained(source, lastGrantLitres)` |
| Rule 12 | `design/gdd/hose-storage-and-flow-network.md` | `PoolFull`, pad pool |
| States | `design/gdd/save-and-load.md` | Radio presentation block |

## Acceptance Criteria

Tests use `FakeRadioClock` (settable tick count, dt 0.1 s), `StubLinePools` (fixed IDs, 2.0 s audio, 20 characters unless stated), `SpyRadioAudio` (records play, pause, resume and duck calls), `SpySubtitleSink` and `StubStateView` (pad pool, crate records, tuning reads), with a seeded `System.Random`.

- **AC-01 [EditMode]** GIVEN an idle radio and no launch milestone played, WHEN the first `LaunchCompleted(0.6)` is delivered at tick 0, THEN exactly one request is made, it is the first-launch milestone line, no launch-pool line plays, and its subtitle reads "6 credits".
- **AC-02 [EditMode]** GIVEN an idle radio and the first-launch milestone played, WHEN `LaunchCompleted` is delivered, THEN one full launch line is chosen.
- **AC-03 [EditMode]** GIVEN a full launch line started at tick 0, WHEN a launch is due at tick 300, THEN a stinger plays; WHEN another launch is due at tick 400, THEN only the light blinks and `SpyRadioAudio` records no play; WHEN another is due at tick 450, THEN a full line plays.
- **AC-04 [EditMode]** GIVEN `Reveal(dock)` and `DrawdownThreshold(pond, 10)` delivered in tick 0, THEN the reveal line starts at tick 18 (display time 25 ticks) and the threshold line starts at tick 63, after the 20-tick gap.
- **AC-05 [EditMode]** GIVEN a queue holding priorities 2, 2, 3, WHEN a priority 3 request arrives, THEN the older priority 2 request is evicted; GIVEN a queue holding priorities 3, 3, 3, WHEN a priority 2 request arrives, THEN the incoming request is dropped.
- **AC-06 [EditMode]** GIVEN a queue holding 3 priority 4 requests, WHEN a priority 4 request arrives, THEN the queue holds 4 and none is dropped.
- **AC-07 [EditMode]** GIVEN a priority 2 request queued at tick 0 behind a long line, THEN it is still queued at tick 99 and dropped unplayed at tick 100; GIVEN a priority 4 request queued at tick 0, THEN it is still queued at tick 1,000.
- **AC-08 [EditMode]** GIVEN a pool of 12 full launch lines, WHEN 13 launches arrive 500 ticks apart, THEN launches 1 to 12 use each line exactly once and launch 13's line differs from launch 12's.
- **AC-09 [EditMode]** GIVEN `DrawdownThreshold(pond, 10)` delivered at step 3 of tick N, WHEN a save is taken at step 4 of tick N and loaded, THEN `threshold_10` is in the played block, no 10% line plays after load, and the save holds no shuffle-bag data.
- **AC-10 [EditMode]** GIVEN the radio volume is 0, WHEN a milestone fires, THEN its subtitle shows for its display time and the line is marked played.
- **AC-11 [EditMode]** GIVEN a line playing from tick 0 and a priority 2 request queued at tick 10, WHEN `Paused` is delivered, 300 s of wall time pass with `FakeRadioClock` at tick 20, and `Resumed` is delivered, THEN `SpyRadioAudio` records pause then resume, and the request is still queued.
- **AC-12 [EditMode]** GIVEN a crate with `landedTick` 0 still unopened and the radio idle with an empty queue, THEN a crate nag plays at tick 600 even though a line ended at tick 550, and no second nag for that crate plays by tick 5,000.
- **AC-13 [EditMode]** GIVEN `StubStateView` with the pad pool filling (not full) and no launch for 3,000 ticks, THEN no nag plays; GIVEN `PoolFull(pad)` at tick 0, no launch and no line since tick -1,000, THEN a full-pad nag plays at tick 600, and none at tick 601 to 5,000.
- **AC-14 [EditMode]** GIVEN a line of 60 characters with 1.0 s audio, THEN the radio stays Speaking for 45 ticks and `SpySubtitleSink` shows it for 45 ticks; GIVEN a 4.0 s line of 20 characters, THEN 40 ticks.
- **AC-15 [EditMode]** GIVEN `DrawdownThreshold(pond, 40)`, THEN a line from the 3 generic threshold lines plays and `threshold_40` is marked played.
- **AC-16 [EditMode]** GIVEN the IBC owned and drum + IBC capacity 1,200 L, WHEN `LaunchCompleted(1200)` is delivered, THEN the end-beat line plays once; WHEN the farm pond's `SourceDrained` arrives later, THEN no end-beat line plays.
- **AC-17 [EditMode]** GIVEN an empty pool for `Purchased`, WHEN `Purchased` fires twice, THEN nothing plays and exactly one warning is logged.
- **AC-18 [PlayMode]** GIVEN an idle radio, WHEN `Purchased` fires, THEN audio starts within 0.5 s and `SpyRadioAudio` records a −4 dB duck.
- **AC-19 [Manual]** GIVEN a 15–20 minute MVP session, WHEN Nour plays it, THEN the radio never feels like noise in the electric stage (7:00 to 15:40), the dock and shopping-cart reveals each land as a joke after the sting, and no nag plays while they are filling the pad (MVP test b, satire check).

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02; batch design review 2026-11-02 found NEEDS REVISION, revised the same day).** Pillar 4 is carried entirely by the gap between the radio's cheer and the drained landscape; Rule 16 keeps it from preaching, and every launch still gets a reaction (line, stinger or light). Pillar 3: thresholds and reveals are the radio's biggest moments, protected from being dropped, and reveal lines wait for the sting. Watch item: launch-line fatigue in the electric stage (Rule 5).

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Placeholder voice: Nour's own recording or text-to-speech? | Nour | Before MVP build |
| Final voice and number of lines for the solo release (estimate 300–500) | narrative-director | Vertical Slice |
| Should players be able to record their own radio voice (Voice Sound Packs)? | game-designer | Co-op update |
