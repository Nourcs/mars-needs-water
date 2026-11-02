# Water Source Model

> **Status**: In Design
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 3 (The World Shows the Damage); Pillar 1 (Start Tiny, End Enormous)
> **Creative Director Review (CD-GDD-ALIGN)**: REVISED 2026-11-02 (3 concerns applied)

## Summary

The rule for how much water is left in each source and how it leaves. In the MVP there are two sources, a 40 L starter puddle for the spoon-and-bucket opening and a 20,000 L farm pond for the pumps. Every source uses its real-world volume (real-scale rule); real lakes and reservoirs come later. Each loses exactly what the player takes, never refills, and announces when it runs dry. Other systems take water only through `Available` and `Withdraw`; the waterline, stain bands and reveals are drawn from its remaining volume by Drawdown Visuals & Reveals.

> **Quick reference** — Layer: `Core` · Priority: `MVP` · Key deps: `Game State & Simulation, Economy tuning table`

## Overview

The Water Source Model is the rule for how much water is left in each body of water the player works, and how it leaves. Each source (in the MVP, a small **starter puddle** by the spawn and the **lake**) starts with a designed volume and loses exactly what the player takes, by spoon, bucket or pump, until it is empty. It never refills. Other systems ask it for water through two operations: how much is `Available`, and `Withdraw` an amount, which returns what was actually taken. It knows nothing about pipes, tanks or pumps; the Hose/Storage Network decides how much pumps pull each tick, and Drawdown Visuals & Reveals turns the remaining volume into a visible waterline, stain bands and reveals.

The player meets this system directly: it is the thing they are taking apart. Without it, nothing in the world could shrink, and Pillar 3 ("the world shows the damage") would have nothing to show.

*Framing: both the data layer and its player effect (recommended for `Gameplay`). No existing ADR to cite. Fantasy: direct.*

## Player Fantasy

**"The hole is yours."** The lake is something you can take apart with your hands, and every trip leaves a mark you made. You never feel the lake as a number; you see it in where the water no longer is.

- **Anchor moments:** at hand scale, the starter puddle is gone and you made the mud. At pump scale, you stand at the intake where you first lowered the hose, and the shoreline has stepped back past your boots.
- **Tone:** quiet pride with a cold edge (Pillars 2 and 3).
- **Onboarding beat, "Spoon versus lake":** your spoon leaves little scoop marks in the puddle's edge, the bucket visibly lowers it, and about 2 minutes in your bucket takes the last drop; the radio thanks you for exactly the litres of that final scoop ("6.2 litres of progress"). Deadpan absurdity (Pillars 4 and 1). It works as an opening joke, not as the whole session's feeling.

*Shaped with `creative-director` (three framings; "The hole is yours" chosen, "Spoon versus lake" kept as the onboarding beat).*

## Detailed Design

### Core Rules

1. **A source** has an id, a type (`Puddle`, `Lake`; `GlacierFace` in the Vertical Slice), a start volume, and a remaining volume, all in litres. The start volume comes from the Economy tuning table at new game and is **copied into this system's state slice**, so a later change to the table never changes the drawdown percentage of an existing save.
2. **MVP sources:** one starter puddle next to the spawn and one **farm pond** (type `Lake`). **Real-scale rule (Nour, 2026-11-02):** every source holds its real-world volume for what it is; 20,000 L is a true farm pond (about 5 × 4 m, 1 m deep). Real lakes (tens of millions of litres and up), reservoirs and glaciers come in later regions with real volumes, and the machines grow to match. The puddle exists because hand work cannot visibly move a lake: 30 L over a 100 m² surface is about 0.3 mm. The puddle gives the first visible change within 2 minutes and the "last drop" beat. Its volume does not count toward the lake's drawdown.
3. **`Available(source)`** returns the remaining volume. It is never negative and, in the MVP, has no rate cap.
4. **`Withdraw(source, amount)`** grants `min(max(amount, 0), remaining)` (an amount that is not a number counts as 0), reduces the remaining volume by exactly that, and returns what was granted. Partial withdrawals are allowed. Only two callers exist in the MVP: Hand Tools (scoop requests, applied in tick step 1) and the Hose/Storage Network (pump flow, tick step 2b). Withdrawals take effect immediately when called; this system's own step (tick step 2d) then applies the drained check, updates states and raises `SourceDrained`.
5. **Who gets water first:** in one tick, hand scoops (step 1) are served before pumps (step 2b). When several pumps want more than is left, the Network splits it proportionally and makes one `Withdraw` per source. The source never arbitrates between callers.
6. **No "rate limited by what remains" penalty in the MVP.** The lake is sized so it is never nearly empty during the MVP session (see Formulas), so a shallow-end slowdown would add code without a felt effect. A disabled knob is kept for later regions (Tuning Knobs).
7. **No regeneration.** Water taken stays gone. A later river is modelled as a very large source behind a hose or pump cap, never as a refill.
8. **Drained:** when the remaining volume falls to or below a tiny epsilon (0.001 L), it snaps to exactly 0, the source becomes Drained, and it raises `SourceDrained(source, tick)` once. The last drop is reachable with any tool, including the spoon.
9. **Drawdown percentage** = (start − remaining) ÷ start × 100, per source. Drawdown Visuals & Reveals and the play log's `lake_drained_pct` use the lake's value.
10. **Events:** this system raises only `SourceDrained`. It does not raise a per-tick volume event; Drawdown steps right after it in the tick (Game State tick order) and reads the remaining volume directly. `DrawdownThreshold` and `Reveal` belong to Drawdown.
11. **No putting water back in the MVP.** Pouring a carried load into a source is rejected (`RequestRejected(CannotDeposit)`), so the volume only ever goes down.
12. **Height is not this system's job.** Turning volume into a waterline height (the basin's volume-to-height curve) belongs to Drawdown Visuals & Reveals and the level asset.

### States and Transitions

| State | Condition | Entered from | Effect |
|---|---|---|---|
| Full | remaining = start | New game | — |
| Draining | low threshold < remaining < start | Full | — |
| Low | 0 < remaining ≤ low threshold (10% of start) | Draining | Presentation hint only (pump sputter); no event |
| Drained | remaining = 0 | Any | Raises `SourceDrained` once; `Withdraw` returns 0 |
| Dormant (later, Regions) | Region locked | — | `Available` = 0 |

States are derived from the remaining volume at this system's step. The Drained flag is saved, so loading a save never raises `SourceDrained` again.

### Interactions with Other Systems

| System | Uses from this system | Gives to this system |
|---|---|---|
| Game State & Simulation | Owns the slice storage; runs this system at tick step 2d | Tick, request queue, event bus |
| Economy, Progression & Shop | — | Start volume per source id (copied at new game) |
| Hand Tools | `Withdraw` for scoops; `RequestRejected(SourceEmpty)` when drained | Scoop requests |
| Hose, Storage & Flow Network | `Available`, `Withdraw` | Pump demand already split across pumps |
| Drawdown Visuals & Reveals | Remaining volume, drawdown %, `SourceDrained` | — |
| Corporate Radio | `SourceDrained` (the "last drop" line) | — |
| Save & Load | The slice (start copy, remaining, drained flag) | — |
| Ice Harvesting (Vertical Slice) | `Withdraw` all-or-nothing for a block | Block size in water-equivalent litres |

All except Game State are provisional until their GDDs exist.

## Formulas

The **withdraw** formula is defined as:

`granted = min(max(amount, 0), remaining); remaining' = remaining − granted; if remaining' ≤ drainedEpsilon then remaining' = 0`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| amount | double (L) | 0 and up | calculated (caller) | Litres requested this call |
| remaining | double (L) | 0–start | calculated (Game State slice) | Litres left in the source before the call |
| granted | double (L) | 0–remaining | calculated | Litres actually taken |
| drainedEpsilon | double (L) | 0.0001–0.01 | data file (tuning knob) | Below this, the source snaps to empty |

**Output Range:** 0 to `remaining`. Never negative; never more than what is left. Performance budget: this system's work costs ≤ 0.05 ms per tick with 64 sources and no garbage allocation.
**Example:** a 10 L bucket scoop from a puddle with 6.2 L left grants 6.2 L, and the puddle becomes Drained that tick.

The **drawdown_pct** formula is defined as:

`drawdown_pct = (start − remaining) / start × 100`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| start | double (L) | 1–1e12 | data file (Economy table, copied at new game) | Designed volume of the source |
| remaining | double (L) | 0–start | calculated | Litres left |

**Output Range:** 0 to 100.
**Example:** a 20,000 L lake with 9,000 L left is 55% drained.

**MVP sizes: puddle 40 L, farm pond 20,000 L** (both real-world sizes under the real-scale rule; the Economy GDD may tune them only within what such a pond really holds). Check against the MVP ladder from the Economy GDD's pacing simulator (spoon 15 mL scoops into a 10 L jerrycan to ~1:14, bucket with 10 L → 100 L → 200 L storage to ~4:38, hand pump 1.25 L/s to ~7:20, electric 8 L/s, second electric pump at ~10:35, IBC at ~16:00):
- Typical session: the pond is about 30% drained at about 16 minutes, when the IBC arrives (model includes lever, rocket-cycle and delivery time). The pond outlasts the ladder, unlike the prototype's 6,000 L lake.
- Hand work (bucket trips from the pond) moves only about 0.1% of the pond before the hand pump, and the hand pump about another 0.5%, which is why the puddle exists. A real spoon moves about 0.72 L in a minute, too little to lower even the puddle visibly, so the spoon shows local feedback (ripple, scoop mark); the puddle visibly drops from the bucket on and is dry at about 2:04.
- **Requirement on Drawdown (not optional):** the real hand pump drains only about 0.5% of the pond in its whole stage (about a 5 mm drop on a 20 m² pond). The pond must have drawdown thresholds at **1%, 2.5% and 5%** (about 7:25, 8:05 and 9:07), then every 10%, and the basin needs a **1:25 shallow shelf at the pump-intake viewpoint** so 1 cm reads as about 25 cm of exposed mud. Gaps between visible changes stay at or under about 2.7 minutes for the reference player (about 6.3 minutes for a lazy launcher; Economy GDD).

*Formulas and sizing from `systems-designer`; puddle and basin reasoning from `level-designer`.*

## Edge Cases

- **If `amount` is 0, negative or not a number**: nothing is granted and a warning is logged.
- **If `amount` is more than what remains**: everything left is granted and the source becomes Drained in the same tick.
- **If a scoop and pumps reach a nearly empty source in the same tick**: the scoop (step 1) is served first; pumps get what is left.
- **If total pump demand exceeds what is available**: the Network splits it proportionally; the source hands over all of it and becomes Drained.
- **If a withdrawal leaves `drainedEpsilon` or less**: the remainder snaps to 0 and the tiny residue is logged as rounding, not lost water.
- **If anything withdraws from a Drained source**: it gets 0 and no event fires. A scoop gets `RequestRejected(SourceEmpty)`.
- **If the source id is unknown**: 0 is returned and an error is logged.
- **If the player tries to pour carried water back into a source**: the request is rejected (`CannotDeposit`). The water stays in the tool.
- **If the Economy start volume changes after a save was made**: the saved copy of the start volume is used. A remaining volume larger than start (corrupt data) is clamped to start.
- **If a save is loaded with a Drained source**: it stays Drained and `SourceDrained` does not fire again.
- **If a glacier block needs more litres than remain** (Vertical Slice): the all-or-nothing withdrawal returns 0 and the face is shown as exhausted.
- **If `SourceDrained` and drawdown thresholds happen in the same tick**: `SourceDrained` is raised at this system's step, thresholds at Drawdown's step right after, so they flush in that order.
- **If the starter puddle is drained**: the lake's drawdown is unchanged; the puddle is never refilled.

## Dependencies

**Upstream:**
| System | Nature | Interface |
|---|---|---|
| Game State & Simulation (`design/gdd/game-state-and-simulation.md`) | Hard | Slice storage, tick step 2d, request queue, `RequestRejected`, event bus |
| Economy, Progression & Shop (not yet designed) | Hard | Start volume per source id, read once at new game. Provisional |

**Downstream:**
| System | Nature | Interface |
|---|---|---|
| Hose, Storage & Flow Network | Hard | `Available`, `Withdraw` |
| Hand Tools | Hard | `Withdraw` via scoop requests |
| Drawdown Visuals & Reveals | Hard | Remaining volume, drawdown %, `SourceDrained` |
| Corporate Radio | Soft | `SourceDrained` |
| Save & Load | Hard | Slice |
| Ice Harvesting (VS), Regions (Alpha), Network Spike | Hard | Glacier faces as sources; dormant sources; synced volume |

## Tuning Knobs

| Knob | Default | Safe range | Home | Too low | Too high |
|---|---|---|---|---|---|
| Farm pond start volume | 20,000 L | 18,000–25,000 L (real farm-pond range) | Economy table | Runs out before the ladder ends | 10% bands arrive too slowly for the 5-minute target |
| Puddle start volume | 40 L (provisional) | 25–60 L | Economy table | Spoon stage ends before the joke lands | No visible change within 2 minutes |
| `drainedEpsilon` | 0.001 L | 0.0001–0.01 L | This system | Rounding residue never reaches empty | Visible water vanishes early |
| Low threshold | 10% of start | 5–20% | This system | Sputter warning too late | Pumps sound weak for too long |
| Shallow-end slowdown | Off | Start 3–10% of volume, floor 0.25–1.0 | This system (later regions) | — | Last stretch drags (chore risk) |

## Visual/Audio Requirements

- The water plane's height must always follow the remaining volume through a curve baked from the basin (owned by Drawdown and the level asset), and must be interpolated between ticks with Game State's `alpha`, so it never steps.
- **Basin guidance for the level:** a 1:25 shallow shelf at the pump-intake viewpoint so the first 1% (about 1 cm) reads as about 25 cm of exposed mud and the first 5% as over 1 m; then near-even height per 10% band (at least 0.25 m drop per band; bathtub-like walls, not a saucer), gentle 1:8 beach lips at the 2–3 main viewpoints (a 0.25 m drop reads as about 2 m of shoreline retreat), and a deep central pit holding the last 5–10% so the finale is a puddle again.
- **Scooping:** a splash, ripple and a scoop mark in the mud per scoop. A spoon cannot visibly lower the puddle (real 15 mL scoops); the puddle visibly drops per bucket trip. On the pond, hand tools move nothing, which is the joke.
- **Pumping:** continuous; the waterline creeps visibly within a minute at electric scale; the intake hums, then sputters when the source is Low.
- **Drained:** a wet-mud surface, a final drip sound, and a beat of silence before the radio line.
- Reveal placement per band is owned by Drawdown Visuals & Reveals (level designer suggestions recorded there: dock and "Swim Safe" sign at 10%, cart and rowboat at 20–30%, a car at 50%, a wreck and a corporate pipe intake at 70–80%, the pit, last puddle and a company time capsule at 90–100%).

*`art-director` not consulted for this section; visual requirements come from the concept's Visual Identity Anchor and the `level-designer`. Review at `/art-bible`.*

## Game Feel

- **Every scoop registers immediately:** the splash, sound and tool weight appear the same frame as the input; the litres commit on the next tick (Game State latency budget, ≤ 0.2 s).
- **The puddle is the first "I did that" moment:** spoon scoops leave marks and ripples; each bucket trip visibly lowers it (first visible drop within about 90 seconds); the bucket takes the last drop at about 2 minutes.
- **The lake should feel too big for your hands and just right for your pumps:** no visible change from hand work, then a waterline that creeps where you can see it once the electric motor runs.

## UI Requirements

No UI of its own. The HUD may show the lake's drawdown percentage as confirmation only (Pillar 3: the landscape shows progress first). The pump sputter at Low is audio, not UI.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 4, 5, 10 | `design/gdd/game-state-and-simulation.md` (tick order, requests, events) | Water Source runs at tick step 2d; scoops are step-1 requests; `RequestRejected` and the event bus |
| Rule 1, Tuning Knobs | Economy, Progression & Shop GDD (not yet written) | Start volumes live in the Economy table |
| Rules 5, Interactions | Hose, Storage & Flow Network GDD (not yet written) | Owns the flow solve and proportional split |
| Rules 9, 10, 12 | Drawdown Visuals & Reveals GDD (not yet written) | Owns thresholds, reveals and the volume-to-height curve |

## Acceptance Criteria

Tags: **[EditMode]** = pure C# NUnit test, no scene. **[PlayMode]** = automated in the Unity loop. **[Manual]** = checked by hand.

- **AC-01 Start copied** [EditMode]: Given the Economy table sets lake = 20,000 L at new game, when the table is then changed to 25,000 L and the save is reloaded, then the lake's start is still 20,000 L and remaining 9,000 L reports drawdown_pct 55.
- **AC-02 MVP sources** [EditMode]: Given a new game, when sources are listed, then there are exactly two: one `Puddle` (40 L) and one `Lake` (20,000 L), both Full.
- **AC-03 Puddle separate from lake** [EditMode]: Given a new game, when the puddle is withdrawn to 0, then the lake's remaining is 20,000 L and its drawdown_pct is 0.
- **AC-04 Available** [EditMode]: Given a source with 6.2 L, when `Available` is called, then it returns 6.2. After draining it returns 0, never a negative value.
- **AC-05 Withdraw grant** [EditMode]: Given remaining 100 L, when `Withdraw(30)` is called, then it returns 30 and remaining is 70. When `Withdraw(500)` is called next, it returns 70 and remaining is 0.
- **AC-06 Caller order** [EditMode]: Given remaining 12 L, a queued 10 L scoop and pump demand of 10 L in the same tick, when the tick runs, then the scoop gets 10 L, the pumps get 2 L, and the source records exactly one `Withdraw` from the Network.
- **AC-07 No rate cap** [EditMode]: Given a lake with 1% left (200 L), when `Withdraw(150)` is called, then it returns 150. The shallow-end knob defaults to Off.
- **AC-08 No regeneration** [EditMode]: Given remaining 9,000 L, when 10,000 ticks run with no withdrawals, then remaining is still exactly 9,000 L.
- **AC-09 Drained snap and event** [EditMode]: Given remaining 1.0005 L and epsilon 0.001, when `Withdraw(1.0)` is called and the tick reaches this system's step, then remaining is exactly 0.0, the state is Drained, and `SourceDrained(source, tick)` fires once with the current tick.
- **AC-10 Spoon reaches the last drop** [PlayMode]: Given the puddle at 0.3 L, when the player scoops with the spoon until nothing is left, then the puddle reaches Drained.
- **AC-11 drawdown_pct formula** [EditMode]: start 20,000 with remaining 9,000 gives 55.0; remaining 20,000 gives 0; remaining 0 gives 100. The output always stays within 0–100.
- **AC-12 Only SourceDrained** [EditMode]: Given a bus spy, when 100 partial withdrawals and one drain happen, then the only event this system raises is a single `SourceDrained`; there is no per-tick volume event, `DrawdownThreshold` or `Reveal`.
- **AC-13 No deposit** [EditMode]: Given the player carries 5 L, when a pour into the lake is requested, then `RequestRejected(CannotDeposit)` is raised, the lake volume is unchanged and the tool still holds 5 L.
- **AC-14 No height API** [Manual]: A code review confirms the source API exposes no waterline height or basin curve.
- **AC-15 withdraw formula** [EditMode]: A 10 L request on 6.2 L returns 6.2 and the source is Drained that tick. A request of 0, −5 or NaN returns 0, leaves remaining unchanged, and logs one warning.
- **AC-16 States** [EditMode]: With start 100 L, remaining 100, 50, 10, 5 and 0 map to Full, Draining, Low, Low and Drained. Entering Low raises no event.
- **AC-17 Drained source** [EditMode]: Given a Drained source, when `Withdraw(10)` is called, then it returns 0 and no event fires; a scoop gets `RequestRejected(SourceEmpty)`.
- **AC-18 Unknown id** [EditMode]: `Available` and `Withdraw` on an unknown id return 0 and log one error.
- **AC-19 Corrupt save** [EditMode]: Given a save with remaining 25,000 and saved start 20,000, when it loads, then remaining is 20,000.
- **AC-20 Drained survives load** [EditMode]: Given a save with a Drained puddle, when it loads and 60 ticks run, then the puddle is Drained, remaining is 0 and `SourceDrained` is never raised.
- **AC-21 Event order** [EditMode]: Given a withdrawal that drains the lake and crosses a drawdown threshold in one tick, when the bus flushes, then `SourceDrained` comes before the threshold event.
- **AC-22 Residue logged** [EditMode]: Given a snap below epsilon, then the residue is logged as rounding and the totals in AC-23 still balance.
- **AC-23 Cross-system: scoop + pumps on a nearly empty source** [EditMode, headless tick]: Given the lake at 12.0 L, a 10 L scoop queued for step 1, and two pumps demanding 3 L and 1 L for step 2, when one tick runs, then the scoop gets 10.0 L, the pumps get 1.5 L and 0.5 L (proportional split), the lake ends Drained with `SourceDrained` raised once, and |(scoop + pumps + remaining + logged residue) − 12.0| ≤ 1e-6 × 12.0.
- **AC-24 Performance** [PlayMode]: Given 64 sources and 1,000 `Withdraw`/`Available` calls per tick, when 600 ticks run on the minimum-spec target, then this system's work averages ≤ 0.05 ms per tick and allocates 0 bytes of garbage per tick (Unity Profiler).
- **AC-25 Puddle timing** [Manual playtest]: Given a new game, when a first-time player uses the spoon and then the bucket, then each spoon scoop leaves a visible mark or ripple, each bucket trip visibly lowers the puddle, and the puddle is dry within about 3 minutes (rates from the Economy GDD).

*Drafted with `qa-lead`. Ambiguities it found were fixed in the rules: when withdrawals happen vs this system's step, "at or below" epsilon, NaN treated as 0, and a performance budget.*

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Final puddle and lake volumes against the full MVP ladder and prices | economy-designer | Economy GDD |
| Should pouring water back be allowed later as a joke (it spills and is lost, with a radio line)? | game-designer | Hand Tools GDD |
| Rivers and regenerating sources for later regions: very large source behind a cap, or a real refill rule? | game-designer + systems-designer | Regions GDD |
| Earth counter: Drawdown/HUD show "Earth's water remaining" from 1.386 × 10²¹ L, minus everything shipped. Who owns it (Economy or Radio/UI)? | game-designer | Economy GDD |
| Number representation for very large sources and the Earth counter (up to 1.4 × 10²¹ L; `long` millilitres cannot hold this, `double` loses sub-litre precision above about 10¹⁵ L) | technical-director | Number-representation ADR |
