# Hand Tools (spoon, bucket)

> **Status**: Approved (2026-11-04)
> **Author**: Nour + agents
> **Last Updated**: 2026-11-04 (revised after the 2026-11-04 re-review)
> **Last Verified**: 2026-11-04
> **Implements Pillar**: Pillar 1 (Start Tiny, End Enormous); Pillar 2 (Hands on the Machinery)

## Summary

Hand Tools are the spoon and the bucket: the player scoops water from a source, carries it, and pours it into the active storage container. Every scoop is a request applied on the next tick, so the water taken from the source always equals the water held and poured. They carry the first 4 minutes or so of the game (hand pump at about 4:18), and the bucket stays useful after pumps arrive.

> **Quick reference** — Layer: `Core gameplay` · Priority: `MVP` · Key deps: `Water Source Model`, `Hose, Storage & Flow Network`, `Economy`

## Overview

The player holds one hand tool at a time. Each tool has a volume and a cycle time from the Economy table (spoon 15 mL with a 1.25 s scoop cycle; bucket 10 L per scoop with a 1 s scoop cooldown, and a full trip on foot of about 5 s at the puddle and 20 s from the pond). Scooping takes water from the source under the tool; pouring puts it into the storage container the player is looking at, through the Hose/Storage Network's pour rule. Held water is part of the Game State ledger. There is no automation here: every litre in the hand stage is moved by the player's own clicks and walking.

## Player Fantasy

**"Start with a spoon."** The joke and the promise of the game in one object: you are sent to drain a planet's water with a tablespoon. Each scoop is tiny and honest (a ripple, a scoop mark in the mud), and 40 of them buy you a bucket. The bucket feels like a huge upgrade: you fill the 10 L jerrycan, launch, and wait for the next rocket, which is exactly why the 100 L barrel then feels like never waiting. When the hand pump arrives you feel how far you have come.

- **Anchor moment:** the first bucket scoop: a heavy slosh, the camera dips with the weight, and the puddle visibly drops.
- **Tone:** comic but sincere effort (Pillars 1 and 2). No auto-scoop, no hold-to-repeat in the MVP.

## Detailed Design

### Core Rules

1. **One tool in hand.** The player switches between usable tools with the tool-switch input (bindings live in the First-Person Controller's binding table). A tool is **owned** from purchase (or from the start, for the spoon) and **usable** only once its crate is Opened (Economy rule 8). **New game:** the Hand Tools slice holds the spoon at 0 L, Placement's hands item is the spoon, and an `ownedAtStart` tool has no crate and is usable from the start. Switching sends `SelectTool` (Requests, below). The 0.3 s switch is a viewmodel animation, not a timer in state; during it the input gate sends no Scoop or Pour for that player. A tool that leaves the hands for any reason (`SelectTool`, an unboxing `Placement.SetHands`, picking up another item) is **stowed**: it has no pose in the world and keeps its own held water.
2. **Scoop:** pressing Use while aiming at a source within 2 m of the eye point (pose + eye height; Game State Rule 12) submits `Scoop(issuer, tool, source)`. At the next tick (step 1), after validation, the tool receives `Withdraw(source, capacity − held)`. The animation and sound play at once on the press (Game State latency rule); the tool's scoop cooldown (spoon 1.25 s, bucket 1 s; Economy table) limits how often a scoop can be sent. Trip times are level-design targets, never timers.
3. **Spoon:** 15 mL capacity, 1.25 s per scoop. The spoon holds one scoop, so the loop is scoop, aim, pour. The bucket costs 6 cr, which is 40 spoon scoops (0.6 L) launched (Economy rule 9). **Level rule:** the 10 L jerrycan stands beside the puddle so that the scooping spot and the jerrycan are inside one view, so the cycle needs no body turn. The 1.25 s is the scoop cooldown; a real scoop-turn-pour cycle takes about 1.6 s (Economy rule 9).
4. **Bucket:** 10 L capacity, one scoop fills it. While the bucket is Full, walking slows to 75% speed (the controller applies it from the Hand Tools state; First-Person Controller AC-01). A trip (scoop, walk to storage, pour, walk back) is about 5 s at the puddle and about 20 s from the farm pond at the MVP layout; these are level-design targets, not timers.
5. **Pour:** pressing Use while aiming at a storage container within 2 m of the eye point submits `Pour(issuer, tool, container)`, applied at tick step 1 with the network's pour rule (`poured = min(held, pool space)`; the rest stays in the tool; poured water enters the pool and fills the pad tank first, then the linked tank; Hose rules 5 and 10). Pouring takes 0.8 s for the bucket and 0.3 s for the spoon; pour time is Hand Tools' own data (the Hand Tools tuning asset), not the Economy table.
6. **Puddle first:** the level places the 40 L starter puddle beside the storage spot so the spoon stage needs no walking; the bucket stage starts at the puddle and moves to the farm pond when the puddle dries (about 1:45 at the reference pace).
7. **Spilling:** none in the MVP. Water in a tool is never lost (no tipping, no evaporation).
8. **Bucket stays useful:** after pumps arrive, the bucket can still top off a tank or be poured into the drum. Nothing removes it.
9. **Ownership and grants.** At purchase Economy calls `HandTools.Grant(player, tool)` directly in the same step 1 (Game State 5b), which adds the tool's held entry at 0 L. That is ownership only. The tool becomes usable when its crate is Opened, and it arrives in the issuer's hands through `Placement.SetHands` (Economy rule 8); the tool previously in hands is stowed with its water. `Purchased` is a notification and never changes this slice.
10. **Input gate (presentation, per player).** One component decides when a Use or switch press becomes a request. All times use the presentation clock.
    - **Scoop cooldown** starts when a Scoop request is sent, whatever its result (a rejected scoop still uses its cooldown). It blocks only further Scoops.
    - **Pour lock** lasts the pour time (bucket 0.8 s, spoon 0.3 s; Hand Tools tuning asset) from when a Pour is sent. It blocks both Scoop and Pour. The scoop cooldown does not block a Pour, so a spoon cycle is scoop at 0 s, pour at about 0.5 s, next scoop at 1.25 s.
    - **Switch lock** lasts 0.3 s from a `SelectTool` press and blocks Scoop and Pour.
    - **Buffer:** a Use press up to 0.15 s before a cooldown or lock ends is kept and sent the moment it ends. It keeps the request type and target it had at press time. When locks overlap, it is sent when the last one ends. Only one press is buffered; earlier presses are dropped with no request and no queueing.
    - In co-op the host will re-check cooldowns; in the MVP the gate is the only check.
11. **Near-empty display.** A held amount at or below 1e-6 L counts as Empty for prompts and display ("0.0 / 10 L", Scoop prompt). The residue is kept in the slice and counted in conservation.
12. **Spoonful counter.** While the spoon is in hand, a small "Spoonful #N" line shows beside the held amount, where N counts accepted spoon scoops since the session was loaded (presentation counts a scoop as accepted when the spoon's held amount rises). The HUD draws the line and the quips (HUD Rule 4). It is presentation only, never saved, and restarts on load (accepted). At N = 10, 25 and 40 a one-off crosshair quip shows for 3 s from the Hand Tools string table (placeholder copy: "Spoonful #10. The shareholders are moved."; "Spoonful #25. Hydration targets: in sight."; "Spoonful #40. That is a bucket. Go launch it.").

### Requests (owned here)

Every request carries its issuer's `PlayerId` (0 in the MVP) and is validated at step 1. Rejections raise `RequestRejected(issuer, reason)` and change nothing. Checks run in the order listed; the first failure is the reason. The player-facing text for each code lives in the First-Person Controller's rejection-text column.

| Request | Sent by | Effect when accepted | Rejection codes (in check order) |
|---|---|---|---|
| `SelectTool(issuer, tool)` | Controller, on the tool-switch input (FPC row 7) | Hand Tools calls `Placement.SetHands(issuer, tool)`; the old hand tool is stowed with its water. Selecting the tool already in hands is accepted with no call and no event | `not_owned` (tool not in the Hand Tools slice), `crate_unopened` (owned, crate not Opened), `hands_full` (hands hold a non-tool item: hose end, cable end, crate, pump; drop it first) |
| `Scoop(issuer, tool, source)` | Controller, Use on a source | `got = Withdraw(source, capacity − held)`; `held += got` | `too_far` (eye point to the source's scoop point over 2 m), `not_in_hand` (tool is not the hands item), `tool_full` (held ≥ capacity), `source_dry` (Water Source `State` is Drained). No `Withdraw` call is made for any rejection |
| `Pour(issuer, tool, container)` | Controller, Use on a container | Network pour rule; `held −= poured` | `too_far`, `not_in_hand`, `tool_empty` (held ≤ 1e-6), `cannot_deposit` (target is a source; defensive, since Use on a source always sends Scoop), then the Network's `not_installed` (container on the staging spot, Network rule 13) and `pool_full` (Network rule 9) |

### States and Transitions

| State | Condition | Can go to |
|---|---|---|
| Empty | held ≤ 1e-6 L | Holding or Full (scoop) |
| Holding | 1e-6 < held < capacity | Full (scoop), Empty (pour all), Holding (partial pour) |
| Full | held ≥ capacity (a grant that empties a source may exceed the request by up to 1e-6 L; Water Source rule 4) | Holding or Empty (pour) |

Separately, each owned tool is In hands or Stowed (Placement hands item).

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Held water per tool (Hand Tools slice); `Grant` method; validation of `SelectTool`, `Scoop`, `Pour` | Tick step 1 |
| Placement (Game State Rule 12) | `Placement.SetHands(issuer, tool)` direct call on an accepted `SelectTool` | Hands item (which tool is in hands; whether hands hold a non-tool item); issuer pose for range |
| Water Source Model | `Withdraw` calls (step 1, before pumps) | Water granted; `State` for `source_dry` |
| Hose/Storage Network | Pour requests | Litres accepted; `pool_full` |
| Economy | — | Volume and scoop cooldown per tool (pour time is Hand Tools' own tuning asset); `Grant` call at purchase; crate state (usable once Opened); unboxed tool arrives via `Placement.SetHands` |
| First-Person Controller & Interaction | Prompt state (Scoop, Pour, Full), Full flag for the 75% speed, rejection codes | Aim target, Use and tool-switch input (binding table), rejection text |
| HUD | Held amount, spoonful counter, quips (drawn by HUD Rule 4) | — |
| Drawdown, Audio | Presentation cues on press (ripple, scoop mark, sounds); not domain events | — |

## Formulas

The **scoop** formula is defined as:

`got = Withdraw(source, capacity − held)` · `held += got`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| capacity | double (L) | 0.01–12 | data file (Economy) | Tool capacity (spoon 0.015, bucket 10) |
| held | double (L) | 0 to capacity + 1e-6 | Game State | Water in the tool |
| got | double (L) | 0 to capacity − held + 1e-6 | Water Source | Water granted (may exceed the request by up to `drainedEpsilon` = 1e-6 L when it empties the source) |

**Output Range:** 0 to capacity − held + 1e-6 per scoop. A bucket at a puddle with 4 L left gets 4 L; at a puddle with 10.0000005 L it gets 10.0000005 L and is Full.

The **hand_rate** formula is defined as:

`hand_rate = capacity / trip_time`

**Output Range:** spoon about 0.012 L/s; bucket about 0.5 L/s from the pond (before launch time; at the puddle the 10 L jerrycan and the 15 s rocket cycle cap it at about 0.67 L/s). Matches the Economy pacing model.

## Edge Cases

- **If the player scoops at a source with less than the tool's space**: the tool takes what is left; the source drains and Water Source raises `SourceDrained(source, lastGrantLitres)` once at step 2d of the same tick.
- **If a scoop and a pump draw from the same source in one tick**: the scoop is served first (step 1), per Water Source rule 5.
- **If the player pours into a source** (puddle or pond): rejected (`cannot_deposit`); Water Source has no way to add water, and the tool keeps its water. Normal input cannot reach this (Use on a source sends Scoop); the check is defensive.
- **If the player scoops at a Drained source**: rejected (`source_dry`), checked by Hand Tools through Water Source `State` before any `Withdraw` call.
- **If a full tool scoops at a Drained source**: rejected `tool_full` (checked before `source_dry`).
- **If the player pours into a full container**: rejected (`pool_full`, Network); the tool keeps its water; a dull "full" clunk plays.
- **If the player pours a full bucket into a jerrycan with 3 L of space**: 3 L goes in, 7 L stays in the bucket.
- **If a pour leaves a sub-microlitre residue** (10.0000005 L into 10 L of space): 5e-7 L stays in the bucket, shown as "0.0 / 10 L" with the Scoop prompt; the residue is kept.
- **If the player switches tools while holding water**: each tool keeps its own held water.
- **If a crate unboxes a tool while another hand tool is in hands**: the old tool is stowed with its water and the new tool is in hands.
- **If the player tries to select a tool while carrying a hose end, crate or pump**: rejected `hands_full`.
- **If the player saves while holding water**: held water is saved and restored exactly.
- **If the player spams Use faster than the cycle**: presses before the buffer window are dropped; one press within 0.15 s of the end is sent when the cooldown ends.
- **If the player aims at nothing valid**: the Use press sends nothing and the crosshair shows no prompt.

## Dependencies

**Upstream:** Game State (Hard, slice, step 1 and Placement `SetHands`), Water Source Model (Hard, `Withdraw` and `State`), Hose/Storage Network (Hard, pour rule), Economy (Hard, tool values, `Grant`, crate state), First-Person Controller & Interaction (Hard, aim, Use, binding table, rejection text).
**Downstream:** HUD (Soft, held amount and counter), Drawdown Visuals (Soft, ripples and scoop marks), Audio (Soft), Onboarding (Soft, the spoon is the tutorial), Save & Load (Hard).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Spoon capacity / cycle | 15 mL / 1.25 s | 10–30 mL / 1–2 s | Economy table |
| Bucket capacity | 10 L | 8–12 L | Economy table |
| Pour time (lock) | bucket 0.8 s / spoon 0.3 s | 0.2–1.2 s | Hand Tools tuning asset |
| Full-bucket walk speed | 75% | 60–90% | Controller carry multiplier; affects trip time |
| Use range (eye point) | 2 m | 1.5–3 m | — |
| Bucket trip at MVP layout | ~5 s puddle / ~20 s pond | 3–8 s / 15–30 s | Level-design targets |
| Tool switch time | 0.3 s | 0.2–0.5 s | Presentation lock |
| Input buffer | 0.15 s | 0–0.25 s | 0 turns buffering off |
| Spoonful quip counts | 10 / 25 / 40 | any, at most 3 | String table |

## Visual/Audio Requirements

- Spoon: small ripple and a scoop mark at the puddle edge; a tiny tink-splash into the jerrycan.
- Bucket: heavy slosh, a dip of the camera when lifted full, water visible inside, a satisfying glug when poured; the puddle visibly drops per trip.
- Placeholder sounds owned here in the MVP (Audio Feedback is Vertical Slice).

## Game Feel

- Animation and sound start on the press, before the tick.
- The bucket should feel heavy but never tiring: slower walk, not slower turning.
- A buffered press feels like a hit, not a miss: the 0.15 s buffer keeps the spoon rhythm from dropping inputs.

## UI Requirements

- Crosshair prompt: "Scoop", "Pour", or "Full". The held amount appears beside the crosshair while holding ("7.0 / 10 L"); at or below 1e-6 L it shows "0.0".
- "Spoonful #N" line and the three quips while the spoon is in hand (Rule 12), drawn by the HUD (HUD Rule 4).
- The cue to launch the spoon water is the shared shortfall line, not a Hand Tools prompt: Economy's `NextItemProgress.litresShort` counts the pad pool, so the HUD reads "Enough aboard: LAUNCH to afford" once 0.6 L is in the jerrycan, and the LAUNCH lever hover reads "Launch: pays N cr" (Shipping).

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 1, 2, 5, 9, Requests | `design/gdd/game-state-and-simulation.md` | Step-1 requests, Rule 3 tick boundaries, Rule 5b direct calls, Rule 12 eye point and `SetHands`; AC-X1, AC-E20 |
| Rule 2, Requests, Edge Cases | `design/gdd/water-source-model.md` | `Withdraw`, `drainedEpsilon`, caller order, `State`, `SourceDrained` at 2d |
| Rule 5 | `design/gdd/hose-storage-and-flow-network.md` | Pour rule, `pool_full`, entry tank (rules 5, 9, 10) |
| Values, Rules 1, 3, 9 | `design/gdd/economy-progression-and-shop.md` | Tool volumes, cycles, bucket 6 cr, crate states, `litresShort` |
| Rules 1, 4, Requests | `design/gdd/first-person-controller-and-interaction.md` | Binding table, rejection text, carry speed |
| Rule 12, UI | `design/gdd/hud.md` | HUD Rule 4 draws "Spoonful #N" and the quips |

## Acceptance Criteria

**Test doubles:** `FakeTuningTable` (Economy table values plus the Hand Tools tuning asset's pour times: spoon 0.015 L, 1.25 s scoop cooldown, 0.3 s pour; bucket 10 L, 1 s, 0.8 s), `StubWaterSource` (settable remaining and State, real grant rule with `drainedEpsilon` 1e-6, records every `Withdraw` call, raises `SourceDrained` at step 2d), `StubPumpDraw` (a step-2b `Withdraw` caller, for AC-08), `StubNetwork` (pour rule `min(held, space)` with settable space, raises `pool_full`), `StubEconomy` (owned tools and crate states), `StubPlacement` (settable hands item and issuer pose, records `SetHands` calls), `EventBusSpy`, `RequestSpy` (requests sent by the input gate), `LogSpy`, `FakeClock`, `FakeInput`, `AudioSpy`, `StubSave` (round-trips the Hand Tools and Placement slices). Player is issuer 0, within range, tool in hands, unless stated.

**Scoop**
- **AC-01 [EditMode]** GIVEN an empty bucket and `StubWaterSource` pond at 20,000 L, WHEN a scoop applies, THEN the bucket holds 10 L, the pond 19,990 L, and exactly 1 `Withdraw(pond, 10)` is recorded.
- **AC-02 [EditMode]** GIVEN an empty bucket and the puddle at 4 L, WHEN a scoop applies at step 1 of tick T, THEN the bucket holds 4 L, the puddle is 0 and Drained, and `EventBusSpy` records `SourceDrained(puddle, 4.0)` exactly once, at step 2d of tick T.
- **AC-03 [EditMode]** GIVEN a bucket holding 10 L and the pond at 20,000 L, WHEN a scoop is requested, THEN `RequestRejected(0, tool_full)`, 0 `Withdraw` calls, and the pond is 20,000 L.
- **AC-04 [EditMode]** GIVEN an empty bucket and a Drained puddle, WHEN a scoop is requested, THEN `RequestRejected(0, source_dry)` and 0 `Withdraw` calls.
- **AC-05 [EditMode]** GIVEN a bucket holding 10 L and a Drained puddle, WHEN a scoop is requested, THEN the reason is `tool_full` (not `source_dry`) and 0 `Withdraw` calls.
- **AC-06 [EditMode]** GIVEN an empty bucket and the puddle at 10.0000005 L, WHEN a scoop applies, THEN the bucket holds 10.0000005 L (±1e-12), the puddle is 0, and the state is Full; WHEN a second scoop is requested, THEN `RequestRejected(0, tool_full)`, no further `Withdraw` call, and `LogSpy` records no warning.
- **AC-07 [EditMode]** GIVEN `StubPlacement` puts the issuer's eye point 2.5 m from the puddle's scoop point, WHEN a scoop is requested, THEN `RequestRejected(0, too_far)` and 0 `Withdraw` calls; at 1.9 m, THEN the scoop applies.
- **AC-08 [EditMode]** GIVEN a spoon scoop and a `StubPumpDraw` pump on one source with 0.01 L left, WHEN the tick runs, THEN the spoon gets 0.01 L and the pump 0.

**Pour**
- **AC-09 [EditMode]** GIVEN a bucket holding 5 L, WHEN a pour into the pond is requested, THEN `RequestRejected(0, cannot_deposit)`, and the pond and the bucket are unchanged.
- **AC-10 [EditMode]** GIVEN a bucket with 10 L and a jerrycan with 3 L of space, WHEN a pour applies, THEN the jerrycan is full and the bucket keeps 7 L.
- **AC-11 [EditMode]** GIVEN a bucket with 10.0000005 L and a jerrycan with 10 L of space, WHEN a pour applies, THEN the jerrycan is full, the bucket keeps 5e-7 L (±1e-12), its state is Empty, the display reads "0.0 / 10 L" and the prompt at a source is "Scoop"; WHEN a pour is requested, THEN `RequestRejected(0, tool_empty)`.
- **AC-12 [EditMode]** GIVEN a bucket with 10 L and a drum on the storage spot holding 150 of 200 L, WHEN a pour applies, THEN the drum holds 160 L and the bucket 0 L. GIVEN a full pool, THEN `RequestRejected(0, pool_full)` (Network AC-12).
- **AC-13 [EditMode]** GIVEN a spoon, the puddle at 40 L and an empty jerrycan, WHEN 100 scoop-and-pour cycles apply, THEN puddle loss = jerrycan gain = 1.5 L (relative 1e-9) and the spoon holds 0 L.

**Tool selection and grants**
- **AC-14 [EditMode]** GIVEN the spoon in hands holding 0.015 L and the bucket owned with its crate Opened, WHEN `SelectTool(0, bucket)` applies, THEN `StubPlacement` records `SetHands(0, bucket)` and the spoon still holds 0.015 L. GIVEN the bucket crate Landed, THEN `RequestRejected(0, crate_unopened)` and no `SetHands`. GIVEN the bucket not owned, THEN `not_owned`. GIVEN the hands item is a hose end, THEN `hands_full`. GIVEN `SelectTool(0, spoon)` with the spoon in hands, THEN no `SetHands` call, no rejection and no event.
- **AC-15 [EditMode]** GIVEN the bucket in hands, WHEN `SelectTool(0, spoon)` applies, THEN `SetHands(0, spoon)` is recorded and the stowed bucket keeps its water.
- **AC-16 [EditMode]** GIVEN a new game, THEN the slice holds the spoon at 0 L, the hands item is the spoon, and a spoon scoop at the puddle applies (no `crate_unopened`, since an `ownedAtStart` tool has no crate). GIVEN a new game, WHEN Economy calls `Grant(0, bucket)` at step 1, THEN the slice holds a bucket entry at 0 L before step 2a, and a scoop with the bucket is rejected (`not_in_hand`) until its crate is Opened and `SetHands(0, bucket)` runs. GIVEN `EventBusSpy` replays `Purchased(0, bucket, crate)` with no `Grant` call, THEN the slice is unchanged.
- **AC-17 [EditMode]** GIVEN the spoon holds 0.015 L and the bucket 6 L, bucket in hands, WHEN `StubSave` saves and loads, THEN both values are unchanged and the hands item is the bucket.
- **AC-18 [EditMode]** GIVEN a bucket holding 10 L, THEN Hand Tools reports Full (the controller applies 0.75); GIVEN 7 L, THEN Holding (no slow).

**Input gate** (EditMode, `FakeClock`, `FakeInput`, `RequestSpy`, spoon aimed at the puddle unless stated)
- **AC-19 [EditMode]** GIVEN Use presses at 0, 0.2, 0.4, 0.6 and 1.0 s, THEN exactly 1 Scoop is sent (at 0 s); WHEN a further press comes at 1.15 s, THEN a second Scoop is sent at 1.25 s, not before.
- **AC-20 [EditMode]** GIVEN a full spoon, WHEN Use is pressed at 0 s, THEN a Scoop is sent (rejected later as `tool_full`) and a press at 0.5 s sends nothing (the rejected scoop used its cooldown).
- **AC-21 [EditMode]** GIVEN a spoon scoop sent at 0 s and the jerrycan aimed at, WHEN Use is pressed at 0.5 s, THEN a Pour is sent at 0.5 s (the scoop cooldown does not block pours). GIVEN a bucket Pour sent at 0 s, WHEN Use presses at 0.3 and 0.6 s, THEN nothing is sent; WHEN a press comes at 0.8 s, THEN a request is sent.
- **AC-22 [EditMode]** GIVEN a tool-switch press at 0 s, WHEN Use is pressed at 0.1 s, THEN nothing is sent and the press is dropped (outside the 0.15 s buffer); WHEN Use is pressed at 0.2 s, THEN it is buffered and the request is sent at 0.3 s.

**Presentation and playtest**
- **AC-23 [PlayMode]** GIVEN the player presses Use at a source, THEN `AudioSpy` records the scoop sound in the same frame, and the held amount updates within 0.2 s (2 ticks).
- **AC-24 [PlayMode]** GIVEN the spoon in hands, WHEN 40 spoon scoops are accepted, THEN the crosshair reads "Spoonful #40" and the quips showed once each at 10, 25 and 40.
- **AC-25 [Manual]** GIVEN the MVP layout, WHEN Nour times 5 bucket trips from the pond to the storage spot with a stopwatch, THEN the median is 15–25 s (the level target). Water Source estimates 12–14 s on flat ground. On failure, set the simulator's `TRIP_POND` to the measured median and re-run it.
- **AC-26 [Manual]** GIVEN the first bucket scoop at the puddle, WHEN screenshots are taken from the storage spot before and after, THEN the puddle edge recedes by at least 8 cm, measured along the ground from the storage-spot viewpoint screenshots (Water Source visual requirements).
- **AC-27 [Manual]** GIVEN Nour on a fresh game, playing at their natural pace (no rushing), WHEN the play log is read, THEN the bucket is bought at 0:50–1:30 (Economy AC-33) and is in hand about 12 s later.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: approved after the 2026-11-04 re-review, blockers and nits fixed.** Pillar 1 starts literally with a spoon; Pillar 2 ("Hands on the Machinery") keeps every hand-stage litre physical. The spoon window is now set by Nour's D1: the bucket costs 6 cr (40 scoops, 0.6 L), bought at about 0:55 in the simulator (in hand about 1:07) and about 70 s at a real 1.6 s cycle, inside the concept's 60–90 s. The no-turn jerrycan layout and the shortfall line carry the rest. Watch item: AC-27, Nour's natural-pace run on a fresh game.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Hold-to-repeat scooping for the spoon (accessibility); the "Hold actions: Hold / Toggle" setting (D2) does not cover scooping, which has no hold | ux-designer | `/ux-design` |
| Spilling when running with a full bucket (more craft, more friction) | game-designer | First MVP playtest |
