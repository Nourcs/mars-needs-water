# Water Source Model

> **Status**: Approved (2026-11-04)
> **Author**: Nour + agents
> **Last Updated**: 2026-11-04 (nits from the 2026-11-04 re-review applied)
> **Last Verified**: 2026-11-04
> **Implements Pillar**: Pillar 3 (The World Shows the Damage); Pillar 1 (Start Tiny, End Enormous)
> **Creative Director Review (CD-GDD-ALIGN)**: REVISED 2026-11-02 (3 concerns applied)
> **Design Review Verdict**: APPROVED after the 2026-11-04 re-review, with nits applied

## Summary

The rule for how much water is left in each source and how it leaves. In the MVP there are two sources, a 40 L starter puddle for the spoon-and-bucket opening and a 20,000 L farm pond for the pumps. Every source uses its real-world volume (real-scale rule); real lakes and reservoirs come later. Each loses exactly what the player takes, never refills, and announces when it runs dry. Other systems take water only through `Withdraw` and read it through a small read API; the waterline, stain bands and reveals are drawn from its remaining volume by Drawdown Visuals & Reveals.

> **Quick reference** — Layer: `Core` · Priority: `MVP` · Key deps: `Game State & Simulation, Economy tuning table`

## Overview

The Water Source Model is the rule for how much water is left in each body of water the player works, and how it leaves. Each source (in the MVP, a small **starter puddle** on the launch-pad apron and the **farm pond**) starts with a designed volume and loses exactly what the player takes, by spoon, bucket or pump, until it is empty. It never refills. Other systems take water through one operation, `Withdraw`, which returns what was actually taken, and read it through `Available`, `Start`, `DrawdownPct` and `State`. It knows nothing about pipes, tanks or pumps; the Hose/Storage Network decides how much pumps pull each tick, and Drawdown Visuals & Reveals turns the remaining volume into a visible waterline, stain bands and reveals.

The player meets this system directly: it is the thing they are taking apart. Without it, nothing in the world could shrink, and Pillar 3 ("The World Shows the Damage") would have nothing to show.

*Framing: both the data layer and its player effect (recommended for `Gameplay`). No existing ADR to cite. Fantasy: direct.*

## Player Fantasy

**"The hole is yours."** The pond is something you can take apart with your hands, and every trip leaves a mark you made. You never feel the pond as a number; you see it in where the water no longer is.

- **Anchor moments:** at hand scale, the starter puddle is gone and you made the mud. At pump scale, you stand at the intake where you first lowered the hose, and the shoreline has stepped back past your boots.
- **Tone:** quiet pride with a cold edge (Pillars 2 and 3).
- **Onboarding beat, "Spoon versus lake":** your spoon leaves little scoop marks in the puddle's edge, the bucket visibly lowers it, and about 2 minutes in your bucket takes the last drop; the radio thanks you for exactly the litres of that final scoop (for example "9.2 litres of progress", read from the event). Deadpan absurdity (Pillars 4 and 1). It works as an opening joke, not as the whole session's feeling.

*Shaped with `creative-director` (three framings; "The hole is yours" chosen, "Spoon versus lake" kept as the onboarding beat).*

## Detailed Design

### Core Rules

1. **A source** has an id (an authored int below 1,000,000, Game State Rule 2), a type (`Puddle`, `Lake`; `GlacierFace` in the Vertical Slice), a start volume, a remaining volume (litres) and a drained flag. The start volume comes from the Economy tuning table at new game and is **copied into this system's state slice** (`{id, start, remaining, drained}`, Game State Rule 1, amended 2026-11-02), so a later change to the table never changes the drawdown percentage of an existing save. Sources are stored in an array in id order.
2. **MVP sources:** one starter puddle on the launch-pad apron beside the storage spot, and one **farm pond** (type `Lake`). **Real-scale rule (Nour, 2026-11-02):** every source holds its real-world volume for what it is; 20,000 L is a true farm pond (about 6 × 5 m at the surface, 1 m deep; the exact volume is set from the baked basin, within 18,000–25,000 L). Real lakes (tens of millions of litres and up), reservoirs and glaciers come in later regions with real volumes, and the machines grow to match. The puddle exists because hand work cannot visibly move the pond: 30 L over its roughly 30 m² surface is about 1 mm. The puddle gives the first visible change within 2 minutes and the "last drop" beat. Its volume does not count toward the pond's drawdown.
3. **Read API:** `Available(id)` returns the remaining volume (never negative; no rate cap in the MVP). `Start(id)`, `DrawdownPct(id)` and `State(id)` return the start copy, the drawdown percentage and the derived state. None of these allocates memory.
4. **`Withdraw(id, amount)`** is the only way water leaves a source.
   - **Guard:** an amount that is 0 returns 0 silently. An amount that is negative, NaN or infinite returns 0 and logs a warning (an explicit check, not `max(amount, 0)`, which keeps NaN in C#). A source that is already Drained returns 0 without touching its flag or last grant, so a later caller in the same tick cannot overwrite the last-drop amount.
   - **Grant:** `granted = min(amount, remaining)`. If what would be left is at or below `drainedEpsilon` (1e-6 L), the caller is granted everything (`granted = remaining`), so no water is ever deleted. Remaining drops by exactly `granted`, and when it reaches 0 the drained flag is set at once, inside the call. A second caller in the same tick sees exactly 0.
   - Partial withdrawals are allowed. Only two callers exist in the MVP: Hand Tools (scoop requests, tick step 1) and the Hose/Storage Network (pump flow, tick step 2b). Callers check "full" with ≥, since a grant may exceed their request by at most 1e-6 L.
5. **Who gets water first:** in one tick, hand scoops (step 1) are served before pumps (step 2b). When several pumps want more than is left, the Network splits it proportionally and makes one `Withdraw` per source. The source never arbitrates between callers.
6. **No "rate limited by what remains" penalty in the MVP.** The pond is sized so it is never nearly empty during the MVP session (see Formulas), so a shallow-end slowdown would add code without a felt effect. The knob is listed for later regions and is not built in the MVP (Tuning Knobs).
7. **No regeneration.** Water taken stays gone. A later river is modelled as a very large source behind a hose or pump cap, never as a refill.
8. **Drained event:** at this system's own step (tick step 2d), each source whose drained flag was set during this tick raises `SourceDrained(source, lastGrantLitres)` once, in id order, where `lastGrantLitres` is the grant that emptied it (for the radio's "last drop" line). `lastGrant` and the "drained this tick" marker are per-tick transients held by the system outside the slice: they are cleared after step 2d and never saved (saves happen only after the event flush). The last drop is reachable with any tool, including the spoon.
9. **Drawdown percentage** = (start − remaining) ÷ start × 100, per source. Drawdown Visuals & Reveals and the play log's `lake_drained_pct` use the pond's value.
10. **Events:** this system raises only `SourceDrained`. It does not raise a per-tick volume event; Drawdown steps right after it in the tick (Game State tick order) and reads the remaining volume directly. `DrawdownThreshold` and `Reveal` belong to Drawdown.
11. **No deposit API.** This system has no way to add water, so the volume only ever goes down. Pouring a carried load into a source is a Hand Tools request that Hand Tools rejects (`cannot_deposit`); scooping a drained source is rejected by Hand Tools (`source_dry`).
12. **Height is not this system's job.** Turning volume into a waterline height (the basin's volume-to-height curve) belongs to Drawdown Visuals & Reveals and the level asset.
13. **Data checks:** at new game, a start volume that is ≤ 0 or NaN is refused with an error naming the source. On load, a remaining volume that is NaN or negative becomes 0, one above start is clamped to start, and one at or below `drainedEpsilon` becomes 0 and Drained without raising `SourceDrained`. A save with `drained = true` but remaining above 0 is treated as Drained with remaining set to 0 (logged). A source in the table but missing from the save is created from the table and logged. A source in the save but no longer in the table is kept with its saved start copy and logged. A saved start ≤ 0 makes the load hook reject the slice.

### States and Transitions

| State | Condition | Effect |
|---|---|---|
| Full | remaining = start | — |
| Draining | low threshold < remaining < start | — |
| Low | 0 < remaining ≤ low threshold (10% of start) | Unused in the MVP (no cue; Pumps sputters only on Dry); no event |
| Drained | drained flag set (remaining = 0) | `SourceDrained` raised once; `Withdraw` returns 0 |
| Dormant (Regions; not built in MVP) | Region locked | `Available` = 0 |

States are derived from `remaining` and the drained flag whenever they are read, so one withdrawal may jump straight from Full to Low or Drained. Only the drained flag is stored. It is saved, so loading a save never raises `SourceDrained` again.

### Interactions with Other Systems

| System | Uses from this system | Gives to this system |
|---|---|---|
| Game State & Simulation | Owns the slice storage; runs this system at tick step 2d | Tick, event bus |
| Economy, Progression & Shop | — | Start volume per source id (copied at new game) |
| Pumps & Machines | Drained flag, read at tick step 2a (Pumps Rule 10) | — |
| Hand Tools | `Withdraw` for scoops; `State` for its own `source_dry` rejection | Scoop withdrawals at step 1 |
| Hose, Storage & Flow Network | `Available`, `Withdraw` | Pump demand already split across pumps (step 2b) |
| Drawdown Visuals & Reveals | `Available`, `Start` (Drawdown works from remaining/start in litres), `SourceDrained` | — |
| Corporate Radio | `SourceDrained(source, lastGrantLitres)` (the "last drop" line) | — |
| Save & Load | The slice (`id, start, remaining, drained`) | — |
| Ice Harvesting (Vertical Slice) | `Withdraw` all-or-nothing for a block (not built in MVP) | Block size in water-equivalent litres |

## Formulas

The **withdraw** formula is defined as:

`if amount == 0: return 0` · `if amount < 0 or NaN or infinite: warn; return 0` · `if drained: return 0` · `granted = min(amount, remaining); if remaining − granted ≤ drainedEpsilon then granted = remaining` · `remaining −= granted; if remaining == 0 then drained = true, lastGrant = granted`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| amount | double (L) | > 0, finite | calculated (caller) | Litres requested this call |
| remaining | double (L) | 0–start | calculated (Game State slice) | Litres left in the source before the call |
| granted | double (L) | 0–remaining | calculated | Litres actually taken (may exceed `amount` by at most `drainedEpsilon` when it empties the source) |
| drainedEpsilon | double (L) | 1e-9–1e-6 | data file (tuning knob) | A leftover this small is granted to the caller |

**Output Range:** 0 to `remaining`. Never negative; never more than what is left; water is conserved exactly (granted + new remaining = old remaining). No memory allocation.
**Example:** a 10 L bucket scoop from a puddle with 9.19 L left grants 9.19 L, and the puddle raises `SourceDrained(puddle, 9.19)` at step 2d that tick.

The **drawdown_pct** formula is defined as:

`drawdown_pct = (start − remaining) / start × 100`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| start | double (L) | > 0 (1–1e12) | slice (copied from the Economy table at new game) | Designed volume of the source |
| remaining | double (L) | 0–start | calculated | Litres left |

**Output Range:** 0 to 100.
**Example:** a 20,000 L pond with 9,000 L left is 55% drained.

**MVP sizes: puddle 40 L, farm pond 20,000 L** (both real-world sizes under the real-scale rule; the pond's final volume is the baked basin figure within 18,000–25,000 L). Check against the Economy GDD's pacing simulator (spoon 15 mL scoops into a 10 L jerrycan to ~0:55, bucket with 10 L → 100 L → 200 L storage to ~4:18, hand pump 1.25 L/s to ~7:00, electric 8 L/s, second electric pump at ~10:15, IBC at ~15:40):
- Typical session: the pond is about 30% drained at about 16 minutes, when the IBC arrives (model includes lever, rocket-cycle and delivery time). The pond outlasts the ladder, unlike the prototype's 6,000 L lake.
- Hand work moves about 0.25% of the pond by the time the hand pump is bought, and the hand pump about another 0.5%, which is why the puddle exists. A real spoon moves about 0.72 L in a minute, too little to lower even the puddle visibly, so the spoon shows local feedback (ripple, scoop mark); the puddle visibly drops from the first bucket trip (~1:07) and is dry at about 1:45.
- **Requirement on Drawdown (not optional):** the real hand pump drains only about 0.5% of the pond in its whole stage (a few millimetres). The pond must have drawdown thresholds at **1%, 2.5% and 5%** (about 7:06, 7:45 and 8:48), then every 10%. The basin must meet the visibility outcomes in Visual/Audio Requirements. Gaps between visible changes stay at or under about 2.7 minutes for the reference player, inside the 5-minute requirement (Drawdown Rule 10). A lazy launcher's longest gap is about 5:45, counting the shopping-cart reveal at about 15% (Economy GDD); that passes the 6.5-minute lazy limit (Economy AC-33, Drawdown AC-08), not the 5-minute one.

*Formulas and sizing from `systems-designer`; puddle and basin reasoning from `level-designer`; revised after design review 1.*

## Edge Cases

- **If `amount` is 0**: 0 is returned silently (the Network calls every source every tick, often with 0).
- **If `amount` is negative, NaN or infinite**: nothing is granted and one warning is logged per call.
- **If `amount` is more than what remains**: everything left is granted and the source is Drained at once; the event follows at step 2d.
- **If a withdrawal would leave `drainedEpsilon` or less**: the caller is granted that leftover too, so the source reaches exactly 0 and no water disappears.
- **If a scoop and pumps reach a nearly empty source in the same tick**: the scoop (step 1) is served first; pumps get what is left.
- **If total pump demand exceeds what is available**: the Network splits it proportionally; the source hands over all of it and is Drained.
- **If anything withdraws from a Drained source**: it gets 0 and no event fires. Hand Tools rejects the scoop itself (`source_dry`).
- **If the source id is unknown**: `Available`, `Withdraw`, `Start` and `DrawdownPct` return 0 and `State` returns Drained (so Hand Tools rejects a scoop with `source_dry`); each call logs one error.
- **If the Economy start volume changes after a save was made**: the saved copy of the start volume is used.
- **If a save holds a NaN, negative or too-large remaining volume**: Rule 13 (0, 0, clamp to start).
- **If a save holds a source that is no longer in the table**: it is kept with its saved start copy and logged; a saved start ≤ 0 rejects the slice in the load hook (Rule 13).
- **If a save is loaded with a Drained source**: it stays Drained and `SourceDrained` does not fire again.
- **If a glacier block needs more litres than remain** (Vertical Slice, not built in MVP): the all-or-nothing withdrawal returns 0 and the face is shown as exhausted.
- **If `SourceDrained` and drawdown thresholds happen in the same tick**: `SourceDrained` is raised at this system's step, thresholds at Drawdown's step right after, so they flush in that order.
- **If two sources drain in the same tick**: their events are raised in id order.
- **If the starter puddle is drained**: the pond's drawdown is unchanged; the puddle is never refilled.

## Dependencies

**Upstream:**
| System | Nature | Interface |
|---|---|---|
| Game State & Simulation (`design/gdd/game-state-and-simulation.md`) | Hard | Slice storage, tick step 2d, event bus |
| Economy, Progression & Shop (`design/gdd/economy-progression-and-shop.md`) | Hard | Start volume per source id, read once at new game |

**Downstream:**
| System | Nature | Interface |
|---|---|---|
| Hose, Storage & Flow Network (`design/gdd/hose-storage-and-flow-network.md`) | Hard | `Available`, `Withdraw` |
| Pumps & Machines (`design/gdd/pumps-and-machines.md`) | Hard | Drained flag at step 2a (Pumps Rule 10) |
| Hand Tools (`design/gdd/hand-tools.md`) | Hard | `Withdraw` for scoops; `State` for `source_dry` |
| Drawdown Visuals & Reveals (`design/gdd/drawdown-visuals-and-reveals.md`) | Hard | `Available`, `Start` (fraction remaining/start), `SourceDrained` |
| Corporate Radio (`design/gdd/corporate-radio.md`) | Soft | `SourceDrained(source, lastGrantLitres)` |
| Save & Load (`design/gdd/save-and-load.md`) | Hard | Slice |
| Ice Harvesting (VS), Regions (Alpha), Network Spike | Hard | Glacier faces as sources; dormant sources; synced volume |

## Tuning Knobs

| Knob | Default | Safe range | Home | Too low | Too high |
|---|---|---|---|---|---|
| Farm pond start volume | 20,000 L (final = baked basin) | 18,000–25,000 L (real farm-pond range) | Economy table | Runs out before the ladder ends | 10% bands arrive too slowly for the 5-minute requirement |
| Puddle start volume | 40 L | 25–60 L | Economy table | Spoon stage ends before the joke lands | No visible change within 2 minutes |
| `drainedEpsilon` | 1e-6 L | 1e-9–1e-6 L | This system | Floating residue never reaches empty | A caller's grant exceeds its request by a visible amount |
| Low threshold (unused in the MVP) | 10% of start | 5–20% | This system | Sputter warning too late | Pumps sound weak for too long |
| Shallow-end slowdown (not built in MVP) | Off | Start 3–10% of volume, floor 0.25–1.0 | This system (later regions) | — | Last stretch drags (chore risk) |

## Visual/Audio Requirements

- The water plane's height must always follow the remaining volume through a curve baked from the basin (owned by Drawdown and the level asset), and must be interpolated between ticks with Game State's `alpha`, so it never steps.
- **Basin outcomes for the level (targets, not slopes):** graybox the pond at about 6 × 5 m at the surface and 1 m deep, bake the volume-to-height curve, then shape slopes from the curve to meet these at the named viewpoints:
  - at 1% drained, at least 25 cm of exposed mud on the intake shelf seen from the hand-pump handle (Drawdown AC-07 tests ≥ 20 cm); shelf slope = 25 cm ÷ the height drop at 1% from the baked curve (about 1:31–1:37 on a 25–30 m² surface; advisory, the baked curve sets it);
  - at the launch-pad viewpoint only, the 10%, 20% and 30% bands each expose at least 0.8 m of shoreline on the lip facing the pad deck; lip slope = band drop ÷ 0.8 m from the baked curve (about 1:12 for the 10% band, which drops about 6.7 cm), not a fixed ratio;
  - from 40% to 100%, adjacent bands are at least 6 cm apart vertically on the walls, so they read as separate rings from the pad deck (Drawdown AC-17);
  - a deep central pit holds the last 5–10%, so the finale is a puddle again.
- **Named viewpoints:** (1) the hand-pump handle on the shelf top, within 3 m of the original shoreline so the 6 m suction hose reaches the pit; (2) the launch pad, where the player stands through the electric stage, with the pad deck raised 1.5–2 m and a lip on the shore facing it, sloped from the curve (Drawdown Rule 6), so stain bands read from about 18 m; (3) the bucket beach.
- **Starter puddle:** a pothole puddle on the pad apron beside the storage spot: a shallow skirt about 1.6 × 1.2 m with a 1:15 rim holding about 30 L, so each bucket pulls the edge back about 10 cm, and a central pothole about 15 cm deep holding the last 7–10 L, so the final bucket dip is believable.
- **Scooping:** a splash, ripple and a scoop mark in the mud per scoop. A spoon cannot visibly lower the puddle (real 15 mL scoops); the puddle visibly drops per bucket trip. On the pond, hand tools move nothing visible, which is the joke.
- **Pumping:** continuous; the waterline creeps visibly within a minute at electric scale; the intake hums; the pump sputters when it runs Dry (Pumps). Low has no cue in the MVP.
- **Drained:** a wet-mud surface, a final drip sound, and a beat of silence before the radio line.
- Reveal placement per band is owned by Drawdown Visuals & Reveals (level designer suggestions recorded there: dock and "Swim Safe" sign at 10%, shopping cart at about 15%, rowboat at 20–30%, a car at 50%, a wreck and a corporate pipe intake at 70–80%, the pit, last puddle and a company time capsule at 90–100%).

*`art-director` not consulted for this section; visual requirements come from the concept's Visual Identity Anchor and the `level-designer`. Review at `/art-bible`.*

## Game Feel

- **Every scoop registers immediately:** the splash, sound and tool weight appear the same frame as the input; the litres commit on the next tick (Game State latency budget, ≤ 0.2 s).
- **The puddle is the first "I did that" moment:** spoon scoops leave marks and ripples; each bucket trip visibly lowers it (first visible drop at about 1:07); the bucket takes the last drop at about 1:45.
- **The pond should feel too big for your hands and just right for your pumps:** no visible change from hand work, then a waterline that creeps where you can see it once the electric motor runs.

## UI Requirements

No UI of its own. The HUD may show the pond's drawdown percentage as confirmation only (Pillar 3: the landscape shows progress first). The pump sputter on Dry is audio (Pumps), not UI.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 1, 4, 8, 10 | `design/gdd/game-state-and-simulation.md` | Slice schema (Rule 1, amended), tick order (2b, 2d), event bus, `SourceDrained` payload |
| Rule 1, Tuning Knobs, pacing | `design/gdd/economy-progression-and-shop.md` | Start volumes; pacing simulator times |
| Rules 4, 5 | `design/gdd/hose-storage-and-flow-network.md` | Flow solve, proportional split, one `Withdraw` per source |
| Rules 4, 11 | `design/gdd/hand-tools.md` | Scoops at step 1; owns `source_dry` and `cannot_deposit` |
| Rules 9, 10, 12, Visual/Audio | `design/gdd/drawdown-visuals-and-reveals.md` | Thresholds, reveals, the volume-to-height curve, basin outcomes |

## Acceptance Criteria

Tags: **[EditMode]** = pure C# NUnit test, no scene. **[PlayMode]** = automated in the Unity loop. **[Manual]** = checked by hand. Test doubles used below: a **fake tuning table** (source rows only), a **stub Network** that calls `Withdraw` once per source with given demands and splits proportionally, a **stub save** (serialise and reload the slice), and a **bus spy**.

- **AC-01 Start copied** [EditMode]: Given a fake table with the pond at 20,000 L, a new game, then `Withdraw(pond, 11,000)` and a stub save, when the fake table changes to 25,000 L and the save reloads, then `Start(pond)` = 20,000, remaining = 9,000 and `DrawdownPct(pond)` = 55.0.
- **AC-02 MVP sources** [EditMode]: Given the MVP table and a new game, when sources are listed, then there are exactly two: one `Puddle` (40 L) and one `Lake` (20,000 L), both Full, in id order.
- **AC-03 Puddle separate from pond** [EditMode]: Given a new game, when the puddle is withdrawn to 0, then the pond's remaining is 20,000 L and its drawdown_pct is 0.
- **AC-04 Available** [EditMode]: Given a source with 6.2 L, when `Available` is called, then it returns 6.2. After draining it returns 0, never a negative value.
- **AC-05 Withdraw grant** [EditMode]: Given remaining 100 L, when `Withdraw(30)` is called, then it returns 30 and remaining is 70. When `Withdraw(500)` is called next, it returns 70, remaining is 0 and the drained flag is set at once.
- **AC-06 Caller order** [EditMode]: Given remaining 2.0 L, a 1.5 L scoop withdrawn at step 1 and a stub Network with one pump demanding 0.8 L at step 2b, when the tick runs, then the scoop gets 1.5 L and the pump 0.5 L.
- **AC-07 No rate cap** [EditMode]: Given a full pond (20,000 L), when `Withdraw(20,000)` is called, then it returns 20,000. Given 1% left (200 L), `Withdraw(150)` returns 150.
- **AC-08 No regeneration** [EditMode]: Given remaining 9,000 L, when 10,000 ticks run with no withdrawals, then remaining is still exactly 9,000 L.
- **AC-09 Residue granted, flag and event** [EditMode]: Given remaining 1.0000005 L and epsilon 1e-6, when `Withdraw(1.0)` is called, then it returns 1.0000005, remaining is exactly 0.0 and the drained flag is set immediately; a second `Withdraw(1.0)` in the same tick returns 0 and leaves the last grant unchanged; when the tick reaches step 2d, `SourceDrained(source, 1.0000005)` is raised exactly once.
- **AC-10 Spoon reaches the last drop** [EditMode]: Given the puddle at 0.3 L, when `Withdraw(0.015)` is called 20 times, then the puddle is Drained and the 20 grants sum to exactly 0.3 within 1e-12.
- **AC-11 drawdown_pct formula** [EditMode]: start 20,000 with remaining 9,000 gives 55.0; remaining 20,000 gives 0; remaining 0 gives 100. The output always stays within 0–100.
- **AC-12 Only SourceDrained** [EditMode]: Given a bus spy, when 100 partial withdrawals and one drain happen, then the only event this system raises is a single `SourceDrained`; there is no per-tick volume event, `DrawdownThreshold` or `Reveal`.
- **AC-13 Zero and bad amounts** [EditMode]: Given remaining 50 L, when `Withdraw(0)` is called 100 times, then each returns 0 and no warning is logged; when `Withdraw(−5)`, `Withdraw(NaN)` and `Withdraw(+∞)` are called, then each returns 0, remaining stays 50, and exactly three warnings are logged.
- **AC-14 No height or deposit API** [Manual]: A code review confirms the source API exposes no waterline height, basin curve or way to add water.
- **AC-15 Over-request** [EditMode]: A 10 L request on 9.19 L returns 9.19, the source is Drained, and at step 2d `SourceDrained(source, 9.19)` is raised.
- **AC-16 States** [EditMode]: With start 100 L, remaining 100, 50, 10, 5 and 0 map to Full, Draining, Low, Low and Drained; one `Withdraw(95)` from 100 goes straight to Low. Entering Low raises no event.
- **AC-17 Drained source** [EditMode]: Given a Drained source, when `Withdraw(10)` is called, then it returns 0 and no event fires.
- **AC-18 Unknown id** [EditMode]: `Available`, `Withdraw`, `Start` and `DrawdownPct` on an unknown id return 0, `State` returns Drained, and each call logs one error.
- **AC-19 Bad data** [EditMode]: Given a fake table row with start 0 or NaN, when a new game starts, then it is refused with an error naming the source. Given a stub save with remaining 25,000 (start 20,000), −3 or NaN, when it loads, then remaining is 20,000, 0 or 0. Given a stub save with remaining 5e-7, when it loads, then the source is Drained and no `SourceDrained` is raised. Given a stub save with a saved start of 0, when it loads, then the load hook rejects the slice. Given a stub save holding a source id not in the table, when it loads, then the source keeps its saved start and one log line is written.
- **AC-20 Drained survives load** [EditMode]: Given a stub save with a Drained puddle, when it loads and 60 ticks run, then the puddle is Drained, remaining is 0 and `SourceDrained` is never raised.
- **AC-21 Event order** [EditMode]: Given a bus spy and a stub Drawdown that raises a threshold at step 2e, when one tick drains the pond, then `SourceDrained` is flushed before the threshold event; given two sources drained in one tick, their events come in id order.
- **AC-22 Conservation** [EditMode]: Given the pond at 12.0 L, a 10 L scoop at step 1 and a stub Network with two pumps demanding 0.8 L and 0.4 L at step 2b, when one tick runs, then the scoop gets 10.0 L, the pumps 0.8 L and 0.4 L, remaining is 0.8 L, and scoop + pumps + remaining = 12.0 exactly (relative 1e-12). Then with demands 0.8 and 0.4 on 0.8 L left, the pumps get 0.5333… and 0.2666…, the pond is Drained, `SourceDrained` is raised once, and the total still equals 12.0 within relative 1e-12.
- **AC-23 No garbage** [EditMode]: Given 2 sources and one warm-up call of each method, when a loop makes 6,000 calls of only this system's `Available`, `Withdraw`, `DrawdownPct` and `State`, then the loop passes `Is.Not.AllocatingGCMemory()` (Unity Test Framework). (Timing is covered by Game State AC-P1 on Nour's development Mac.)
- **AC-24 Puddle timing** [Manual playtest]: Given a new game played at the pace of the Economy reference player, then each spoon scoop leaves a visible mark or ripple, each bucket trip visibly lowers the puddle, the first visible drop comes by 1:45 and the puddle is dry by 2:30 of play time. Evidence: a timestamped play log plus screenshots in `production/qa/evidence/`.

*Drafted with `qa-lead`; revised after design review 1 (residue granted to the caller, silent zero requests, ownership of rejections moved to Hand Tools, test doubles named, garbage test replaces the profiler budget).*

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Should pouring water back be allowed later as a joke (it spills and is lost, with a radio line)? | game-designer | Vertical Slice |
| Rivers and regenerating sources for later regions: very large source behind a cap, or a real refill rule? | game-designer + systems-designer | Regions GDD |
| Number representation for very large sources and the Earth counter (up to 1.4 × 10²¹ L; `long` millilitres cannot hold this, `double` loses sub-litre precision above about 10¹⁵ L) | technical-director | Number-representation ADR |
| Final pond volume from the baked graybox basin (within 18,000–25,000 L); re-run the pacing simulator after | level-designer + economy-designer | MVP graybox |
| Does the 20 s pond bucket trip hold with the pond edge within 18 m of the drum (Hose layout rule)? Estimate 12–14 s on flat ground; the raised pad deck's ramp lengthens it. Measure, then set `TRIP_POND` in the simulator | level-designer | MVP graybox |
