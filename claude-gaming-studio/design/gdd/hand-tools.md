# Hand Tools (spoon, bucket)

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 1 (Start Tiny, End Enormous); Pillar 2 (Hands-On Craft)

## Summary

Hand Tools are the spoon and the bucket: the player scoops water from a source, carries it, and pours it into the active storage container. Every scoop is a request applied on the next tick, so the water taken from the source always equals the water held and poured. They are the first 4–5 minutes of the game, and the bucket stays useful after pumps arrive.

> **Quick reference** — Layer: `Core gameplay` · Priority: `MVP` · Key deps: `Water Source Model`, `Hose, Storage & Flow Network`, `Economy`

## Overview

The player holds one hand tool at a time. Each tool has a volume and a cycle time from the Economy table (spoon 15 mL with a 1.25 s scoop cycle; bucket 10 L per scoop with a 1 s scoop cooldown, and a full trip on foot of about 5 s at the puddle and 20 s from the pond). Scooping takes water from the source under the tool; pouring puts it into the storage container the player is looking at, through the Hose/Storage Network's pour rule. Held water is part of the Game State ledger. There is no automation here: every litre in the hand stage is moved by the player's own clicks and walking.

## Player Fantasy

**"Start with a spoon."** The joke and the promise of the game in one object: you are sent to drain a planet's water with a tablespoon. Each scoop is tiny and honest (a ripple, a scoop mark in the mud), the bucket feels like a huge upgrade, and the 10 L jerrycan fills on every trip so you are always running back to launch. When the hand pump arrives you feel how far you have come.

- **Anchor moment:** the first bucket scoop: a heavy slosh, the camera dips with the weight, and the puddle visibly drops.
- **Tone:** comic but sincere effort (Pillars 1 and 2). No auto-scoop, no hold-to-repeat in the MVP.

## Detailed Design

### Core Rules

1. **One tool in hand.** The player switches between owned tools whose crate has been opened (Economy rule 8) with number keys or the scroll wheel. Switching takes 0.3 s. A tool that holds water keeps it while put away (held water is per tool).
2. **Scoop:** pressing Use while aiming at a source within 2 m submits `Scoop(tool, source)`. At the next tick (step 1), the tool receives `Withdraw(source, capacity − held)`. A scoop with a full tool, or at a drained source, is rejected (`tool_full`, `source_dry`). The animation plays at once (Game State latency rule); the tool's scoop cooldown (spoon 1.25 s, bucket 1 s; Economy table) limits how often a scoop can be submitted. Trip times are level-design targets, never timers.
3. **Spoon:** 15 mL capacity, 1.25 s per scoop. The spoon holds one scoop, so the loop is scoop, turn, pour. The spoon stage is set up with the 10 L jerrycan beside the puddle so a scoop-pour cycle is about 1.25 s.
4. **Bucket:** 10 L capacity, one scoop fills it. Carrying a full bucket slows walking to 75% speed. A trip (scoop, walk to storage, pour, walk back) is about 5 s at the puddle and about 20 s from the farm pond at the MVP layout; these are level-design targets, not timers.
5. **Pour:** pressing Use while aiming at a storage container within 2 m submits `Pour(tool, container)`, applied at tick step 1 with the network's pour rule (`poured = min(held, space)`; the rest stays in the tool). Pouring takes 0.8 s for the bucket, 0.3 s for the spoon.
6. **Puddle first:** the level places the 40 L starter puddle beside the storage spot so the spoon stage needs no walking; the bucket stage starts at the puddle and moves to the farm pond when the puddle dries (about 2 minutes in).
7. **Spilling:** none in the MVP. Water in a tool is never lost (no tipping, no evaporation).
8. **Bucket stays useful:** after pumps arrive, the bucket can still top off a tank or be poured into the drum. Nothing removes it.

### States and Transitions

| State | Condition | Can go to |
|---|---|---|
| Empty | held = 0 | Holding (scoop) |
| Holding | 0 < held < capacity | Full (scoop), Empty (pour all), Holding (partial pour) |
| Full | held ≥ capacity (a grant that empties a source may exceed the request by up to 1e-6 L; Water Source rule 4) | Holding or Empty (pour) |

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Held water per tool (Hand Tools slice); scoop and pour requests | Tick step 1 |
| Water Source Model | `Withdraw` calls (step 1, before pumps) | Water granted |
| Hose/Storage Network | Pour requests | Litres accepted |
| Economy | — | Volume and cycle per tool; `Purchased(bucket)` |
| Interaction | — | Aim target and Use input |
| Drawdown, Audio | Scoop and pour events for ripples, scoop marks, sounds | — |

## Formulas

The **scoop** formula is defined as:

`got = Withdraw(source, capacity − held)` · `held += got`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| capacity | double (L) | 0.01–12 | data file (Economy) | Tool capacity (spoon 0.015, bucket 10) |
| held | double (L) | 0–capacity | Game State | Water in the tool |
| got | double (L) | 0–capacity | Water Source | Water granted |

**Output Range:** 0 to capacity per scoop. A bucket at a puddle with 4 L left gets 4 L.

The **hand_rate** formula is defined as:

`hand_rate = capacity / trip_time`

**Output Range:** spoon about 0.012 L/s; bucket about 0.5 L/s from the pond (before launch time; at the puddle the 10 L jerrycan and the 15 s rocket cycle cap it at about 0.67 L/s). Matches the Economy pacing model.

## Edge Cases

- **If the player scoops at a source with less than the tool's space**: the tool takes what is left; the source drains (`SourceDrained`).
- **If a scoop and a pump draw from the same source in one tick**: the scoop is served first (step 1), per Water Source rule 5.
- **If the player pours into a source** (puddle or pond): rejected (`cannot_deposit`); Water Source has no way to add water, and the tool keeps its water.
- **If the player scoops at a Drained source**: rejected (`source_dry`), checked by Hand Tools through Water Source `State` (Hand Tools owns this rejection).
- **If the player pours into a full container**: rejected (`pool_full`); the tool keeps its water; a dull "full" clunk plays.
- **If the player pours a full bucket into a jerrycan with 3 L of space**: 3 L goes in, 7 L stays in the bucket.
- **If the player switches tools while holding water**: each tool keeps its own held water.
- **If the player saves while holding water**: held water is saved and restored exactly.
- **If the player spams Use faster than the cycle**: extra presses are ignored (no queueing); no request is sent.
- **If the player aims at nothing valid**: the Use press does nothing and the crosshair shows no prompt.

## Dependencies

**Upstream:** Game State (Hard, slice and step 1), Water Source Model (Hard, `Withdraw`), Hose/Storage Network (Hard, pour rule), Economy (Hard, tool values), First-Person Controller & Interaction (Hard, aim and Use).
**Downstream:** Drawdown Visuals (Soft, ripples and scoop marks), Audio (Soft), Onboarding (Soft, the spoon is the tutorial), Save & Load (Hard).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Spoon capacity / cycle | 15 mL / 1.25 s | 10–30 mL / 1–2 s | Economy table |
| Bucket capacity | 10 L | 8–12 L | Economy table |
| Full-bucket walk speed | 75% | 60–90% | Weight feel; affects trip time |
| Use range | 2 m | 1.5–3 m | — |
| Bucket trip at MVP layout | ~5 s puddle / ~20 s pond | 3–8 s / 15–30 s | Level-design targets |
| Tool switch time | 0.3 s | 0.2–0.5 s | — |

## Visual/Audio Requirements

- Spoon: small ripple and a scoop mark at the puddle edge; a tiny tink-splash into the jerrycan.
- Bucket: heavy slosh, a dip of the camera when lifted full, water visible inside, a satisfying glug when poured; the puddle visibly drops per trip.
- Placeholder sounds owned here in the MVP (Audio Feedback is Vertical Slice).

## Game Feel

- Animation and sound start on the press, before the tick.
- The bucket should feel heavy but never tiring: slower walk, not slower turning.

## UI Requirements

- Crosshair prompt: "Scoop", "Pour", or "Full". The held amount appears beside the crosshair while holding ("7.0 / 10 L").

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 2, 5 | `design/gdd/game-state-and-simulation.md` | Step-1 requests; AC-X1 |
| Rule 2, Edge Cases | `design/gdd/water-source-model.md` | `Withdraw`, caller order, spoon feedback |
| Rule 5 | `design/gdd/hose-storage-and-flow-network.md` | Pour rule |
| Values | `design/gdd/economy-progression-and-shop.md` | Tool volumes, cycles, storage ladder |

## Acceptance Criteria

- **AC-01 [EditMode]** GIVEN an empty bucket at a pond with 20,000 L, WHEN a scoop applies, THEN the bucket holds 10 L and the pond 19,990 L.
- **AC-02 [EditMode]** GIVEN an empty bucket at a puddle with 4 L, WHEN a scoop applies, THEN the bucket holds 4 L and `SourceDrained` is raised.
- **AC-03 [EditMode]** GIVEN a full bucket, WHEN a scoop is requested, THEN `RequestRejected(tool_full)`.
- **AC-04 [EditMode]** GIVEN a drained source, WHEN a scoop is requested, THEN `RequestRejected(source_dry)`.
- **AC-05b [EditMode]** GIVEN a bucket holding 5 L, WHEN a pour into the pond is requested, THEN `RequestRejected(cannot_deposit)`, and the pond and the bucket are unchanged.
- **AC-05 [EditMode]** GIVEN a bucket with 10 L and a jerrycan with 3 L of space, WHEN a pour applies, THEN the jerrycan is full and the bucket keeps 7 L.
- **AC-06 [EditMode]** GIVEN a spoon scoop and a pump on one source with 0.01 L left, WHEN the tick runs, THEN the spoon gets 0.01 L and the pump 0.
- **AC-07 [EditMode]** GIVEN the spoon holds 15 mL and the bucket 6 L, WHEN the player switches tools and saves and loads, THEN both values are unchanged.
- **AC-08 [EditMode]** GIVEN 100 scoop-and-pour cycles, THEN source loss equals storage gain exactly (relative 1e-9).
- **AC-09 [PlayMode]** GIVEN the player presses Use at a source, THEN the scoop sound plays in the same frame and held water updates by the next tick.
- **AC-10 [PlayMode]** GIVEN the player presses Use 5 times within one spoon cycle, THEN exactly one scoop request is sent.
- **AC-11 [Manual]** GIVEN the MVP layout, WHEN a typical player does bucket trips from the pond to the storage spot, THEN a trip takes 15–25 s.
- **AC-12 [Manual]** GIVEN the first bucket scoop at the puddle, THEN the player can see the puddle drop.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02).** Pillar 1 starts literally with a spoon; Pillar 2 keeps every hand-stage litre physical. Watch item: the spoon stage is set to the concept's 60–90 s joke (about 75 s in the Economy pacing simulator, bucket at 8 cr); if it drags in playtest, lower the bucket price.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Hold-to-repeat scooping for the spoon (accessibility) | ux-designer | `/ux-design` |
| Spilling when running with a full bucket (more craft, more friction) | game-designer | First MVP playtest |
