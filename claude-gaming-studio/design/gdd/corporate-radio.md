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

The radio listens to domain events and plays one line at a time from authored line pools, with subtitles. A small scheduler picks which event gets a line when several happen together, avoids repeats, and keeps the radio from talking over itself or nagging. It never changes game state. In the MVP, lines are text plus placeholder voice; final voice comes later (and may tie into the Voice Sound Packs idea).

## Player Fantasy

**"Your employer is so proud of you."** A warm, tireless voice congratulates you for every litre, never notices the mud, and treats the dying pond as a growth story. The joke lands because the radio and the landscape disagree: when the water drops 10% and a rusted shopping cart appears, the radio announces "a new quarterly record."

- **Anchor moment:** the first reveal: the waterline uncovers something sad, and the radio, right on cue, plays celebratory jingle music.
- **Tone:** deadpan, upbeat corporate (Pillar 4). Never winks, never lectures, never says "climate". The player draws the conclusion.

## Detailed Design

### Core Rules

1. **Events and pools.** The radio subscribes to these events, each with its own line pool and priority (higher first):

| Event | Priority | Pool size (MVP) | Example line (placeholder) |
|---|---|---|---|
| `Reveal` | 5 | 1 per reveal (2 reveals) + 3 generic | "Look at that! The pond is giving back. Heritage assets recovered." |
| `DrawdownThreshold` | 4 | 1 per threshold (1%, 2.5%, 5%, 10%, 20%, 30%) | "Ten percent! Accounting has asked me to say: wow." |
| Milestone (derived by the radio) | 4 | 1 per milestone: first `LaunchCompleted`, first `Purchased` of each pump, first `SourceDrained` | "First launch! Mars thanks you. Mars is very far away, but it thanks you." |
| `Purchased` | 3 | 1 per item + 3 generic | "The Hand Pump. Ten times the gratitude." |
| `SourceDrained` | 3 | 2 (puddle), 2 generic, plus 1 that reads `lastGrantLitres` | "The puddle has been fully optimised." / "{lastGrantLitres} litres of progress. Thank you." |
| `LaunchCompleted` | 2 | 12 full lines + 8 short stingers | "Another shipment! Your contribution is noted. Somewhere." |
| Idle / nag | 1 | 6 (unopened crate after 60 s, no launch for 3 min) | "Your crate misses you." |

2. **One line at a time.** If a line is playing, new requests queue. The queue holds 3; when full, the lowest-priority, oldest request is dropped. Requests older than 10 s are dropped (stale news).
3. **Launch lines and stingers.** Every launch gets a reaction (Pillar 4 test), but if the previous launch line was less than 45 s ago, a short stinger (1–3 words: "Received!", "Thank you!", "Noted.") plays instead of a full line. This keeps the 10 L jerrycan stage, with a launch every ~20 s, from becoming noise.
4. **No repeats.** Within a pool, a line is not reused until all others have played (shuffle bag), and never twice in a row.
5. **Figures in lines.** Lines may insert the litres, credits, item name or the Earth counter ("…1,385,999,999,999,999,998,800 litres to go!"). Values come from the event payload or a read of state.
6. **Gaps.** After any line, 2 s of silence before the next. Idle and nag lines need 90 s of no other line.
7. **Ducking.** While the radio speaks, other audio ducks by 4 dB; subtitles are always shown.
8. **Never blocks play.** The radio never pauses the game, never needs input, and can be turned down or off in settings (subtitles stay available).
9. **Satire rule for writing:** the radio only ever celebrates. It never mentions harm directly; the irony comes from what the player sees at the same moment.

### States and Transitions

| State | Condition | Can go to |
|---|---|---|
| Idle | No line playing, queue empty | Speaking (request arrives, gap elapsed) |
| Speaking | A line is playing | Gap (line ends) |
| Gap | 2 s silence | Speaking (queue not empty); Idle |

Not saved, except the shuffle bags' used lines and which one-off lines (milestones, thresholds, reveals) have played, so a reload doesn't replay them.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Played-lines record (for save) | Events: `LaunchCompleted`, `Purchased`, `DrawdownThreshold`, `Reveal`, `SourceDrained`; a read of credits and totals. The radio derives milestones itself (there is no Milestone event) and keeps its played-lines record outside simulation state; Save & Load stores it as a presentation block. |
| Economy | — | Item names; Earth counter |
| Audio | Ducking signal | — |
| HUD | Subtitle text | — |
| Settings (later) | — | Radio volume, subtitles size |

## Formulas

The **line_choice** rule is defined as:

`next = highest priority in queue (ties: oldest); line = shuffle_bag(pool).draw(); if event == LaunchCompleted and now − last_launch_line < 45 s then pool = stingers`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| priority | int | 1–5 | this GDD | Per event type |
| last_launch_line | double (s) | — | runtime | Time of the last full launch line |
| stinger_window | double (s) | 20–90 | data file | Default 45 s |
| queue_max | int | 2–5 | data file | Default 3 |

**Output Range:** one line or none per scheduling step.

## Edge Cases

- **If a threshold and a reveal happen in the same tick**: the reveal plays first (priority 5), the threshold line queues.
- **If three launches happen during a long reveal line**: the queue keeps the newest; stale launch requests (over 10 s old) are dropped.
- **If the player buys and launches at once**: the purchase line plays first.
- **If a one-off line was played before a save**: it is not replayed after loading.
- **If the radio is turned off**: events are still consumed (so one-off lines are marked as played) and subtitles still show, unless the player turns subtitles off.
- **If a line's figure is huge (Earth counter)**: it is read as digit groups in subtitles; voice says "one point three eight six sextillion litres, approximately".
- **If the pool for an event is empty** (missing content): nothing plays; one warning is logged.

## Dependencies

**Upstream:** Game State (Hard, events), Economy (Soft, names and Earth counter), Drawdown Visuals & Reveals (Hard, raises `DrawdownThreshold` and `Reveal`), Shipping (Hard, `LaunchCompleted`).
**Downstream:** Audio (Soft, ducking), HUD (Soft, subtitles), Chapter Story & Milestones (Alpha, builds on it), Voice Sound Packs (later).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Stinger window | 45 s | 20–90 s | Shorter means more full launch lines |
| Queue size | 3 | 2–5 | — |
| Stale request age | 10 s | 5–20 s | — |
| Gap between lines | 2 s | 1–4 s | — |
| Idle / nag delay | 90 s | 60–180 s | — |
| Ducking | −4 dB | −2 to −8 dB | — |

## Visual/Audio Requirements

- A chunky company radio on a post by the launch pad with a small light that glows while it speaks; the voice is positional near the pad and also mixed lightly into the player's ears so it's always audible.
- A three-note company jingle before one-off lines (milestones, thresholds, reveals).
- MVP: placeholder voice (Nour's recording or text-to-speech); final voice cast later.

## Game Feel

- A line should start within 0.5 s of its event when the radio is free.
- Lines are short: full lines 2–6 s, stingers under 1 s.

## UI Requirements

- Subtitles at the bottom of the screen with a small radio icon; readable size; on by default.

📌 **UX Flag — Corporate Radio:** subtitles. Cover in `/ux-design` with the HUD.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rule 1 | `design/gdd/game-state-and-simulation.md` | Event list |
| Rule 1, 5 | `design/gdd/economy-progression-and-shop.md` | Item names, Earth counter, crate nag |
| Rule 3 | `design/gdd/shipping-and-launch.md` | Launch frequency in the jerrycan stage |
| Rule 1 | Drawdown Visuals & Reveals GDD (not yet written) | Threshold and reveal events |

## Acceptance Criteria

- **AC-01 [EditMode]** GIVEN an idle radio, WHEN `LaunchCompleted` fires, THEN one full launch line is chosen.
- **AC-02 [EditMode]** GIVEN a full launch line 20 s ago, WHEN `LaunchCompleted` fires, THEN a stinger is chosen.
- **AC-03 [EditMode]** GIVEN `Reveal` and `DrawdownThreshold` in the same tick, THEN the reveal line plays first and the threshold line next.
- **AC-04 [EditMode]** GIVEN a queue of 3 and a 4th request of higher priority, THEN the oldest lowest-priority request is dropped.
- **AC-05 [EditMode]** GIVEN a request queued for 11 s, THEN it is dropped unplayed.
- **AC-06 [EditMode]** GIVEN a pool of 12 lines, WHEN 12 launches each get a full line, THEN all 12 lines are used once, and no line plays twice in a row across the reshuffle.
- **AC-07 [EditMode]** GIVEN the 10% threshold line has played, WHEN the game is saved and loaded, THEN it does not play again.
- **AC-08 [EditMode]** GIVEN the radio volume is 0, WHEN a milestone fires, THEN its subtitle shows and the line is marked played.
- **AC-09 [PlayMode]** GIVEN an idle radio, WHEN an event fires, THEN audio starts within 0.5 s and other audio ducks.
- **AC-10 [Manual]** GIVEN a 15–20 minute MVP session, WHEN Nour plays it, THEN the radio never feels like noise in the jerrycan stage and at least one reveal line lands as a joke (MVP test b, satire check).

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02).** Pillar 4 is carried entirely by the gap between the radio's cheer and the drained landscape; rule 9 keeps it from preaching. Pillar 3: thresholds and reveals are the radio's biggest moments. Watch item: launch-line fatigue in the jerrycan stage (rule 3 stingers).

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Placeholder voice: Nour's own recording or text-to-speech? | Nour | Before MVP build |
| Final voice and number of lines for the solo release (estimate 300–500) | narrative-director | Vertical Slice |
| Should players be able to record their own radio voice (Voice Sound Packs)? | game-designer | Co-op update |
