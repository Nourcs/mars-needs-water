# Economy, Progression & Shop

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02 (revised after design review 1)
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 1 (Start Tiny, End Enormous); Pillar 4 (Bleak Corporate Satire)

## Summary

Economy, Progression & Shop owns every designed number in the game (one tuning table, each value tied to a real-world object), the single currency (credits, paid only when water is launched), and the company terminal where the player orders the next tool. It sets the MVP ladder from spoon to two electric pumps and an IBC tank, so each purchase after the bucket takes longer to earn than the last.

> **Quick reference** — Layer: `Foundation` · Priority: `MVP` · Key deps: `Game State & Simulation`

## Overview

Economy, Progression & Shop decides what everything costs, what everything pays, and in what order the player climbs from a spoon and a 10 L jerrycan to a pair of electric pumps. It has three parts:

- **The tuning table:** one data file holding every designed number in the game (tool volumes, pump rates, tank sizes, source volumes, prices, payouts, global values), each with a real-world reference under the real-scale rule. Each system owns the *rules* that use these numbers; this system owns the numbers.
- **Credits:** the single currency. Credits are paid only when water leaves the planet on a launch, so income is always water shipped.
- **The shop:** an in-world company terminal on the launch pad where the player orders the next tool or machine. The order arrives as a crate the player pries open and installs by hand.

It also defines the corporate **Earth counter**, which starts at all of Earth's water and ticks down with every shipment but is never emptied.

The player meets the shop and the prices directly; the tuning table is infrastructure that every other system reads. Without this system there is no reason to ship, nothing to buy and no ladder, so Pillar 1 ("Start Tiny, End Enormous") would have no steps.

*Framing: both the data layer and its player effect (recommended for `Economy`). No existing ADR to cite. Fantasy: yes.*

## Player Fantasy

**"The Company Store."** You get paid in company credits and spend them at the company's terminal, on tools the company sells you so you can take more water for the company. Each purchase is a promotion you paid for yourself.

- **Anchor moment:** the launch payout counts up past the hand pump's price, you pull the ORDER lever, and a branded crate lands beside the dried mud where the puddle used to be.
- **Tone:** deadpan, cheerful, quietly owned (Pillar 4). The irony lives in the transaction itself, in print rather than speech: catalogue copy ("Hand Pump: 10× the gratitude!"), a payslip on every payout with your job title and a "Corporate Hydration Levy: −0 cr (waived, for now!)" line, an "Employee of the Month: YOU" sticker on the kiosk. The Corporate Radio adds voice on top; the store works without it.
- **The unboxing:** prying the lid off the crate, lifting out a real machine and its spec sheet is the craftsman's pleasure inside the satire (Pillar 2).
- **The Earth counter is a background prop, not the point:** a split-flap board by the launch pad where only the last digits ever flip, with "Your contribution: 0.0000000000000000029%" and confetti. It stays wry, never preachy.

*Shaped with `creative-director` ("The Company Store" chosen; the unboxing kept as the delivery beat; "Rounding Error" kept only as a prop).*

## Detailed Design

### Core Rules

1. **One currency: credits.** Water and, later, ice both count as water-equivalent litres. There is no second currency (anti-pillar).
2. **Credits are paid only on a launch.** At liftoff Shipping calls `CreditLaunch(litres)` directly (tick step 2c, same tick as the shipped total), and the player receives `litres × credits_per_litre × region_multiplier × contract_multiplier`. Water in a tank or a bucket pays nothing until it is launched. In the MVP, `credits_per_litre` = 10 and both multipliers = 1. A contract multiplies the payout for the litres it covers; it never pays for the same litres twice. `CreditLaunch` ignores and logs any `litres` that is not finite or is ≤ 0, so credits can never become NaN or fall.
3. **Credits are whole numbers underneath.** Credits are stored as a whole number of millicredits (64-bit integer; 1 cr = 1,000 mc). Each payout is rounded to the nearest millicredit once, when it is credited. `floor(credits)` is integer division, so a run of tiny launches that adds up to exactly 5 cr is exactly 5 cr (no 4.9999 that floors to 4). The 64-bit range (about 9 × 10¹⁵ cr) is far beyond any real-scale region. This is the Economy's requirement on the number-representation ADR, which confirms the type before implementation.
4. **The tuning table** is one data file, read as a whole only by this system; others read their own rows through it.
   - **Row kinds:** `tool`, `machine`, `tank`, `source`, `contract`, `region`, `global`.
   - **Columns:** `id, kind, displayName, price_cr, prereq_id, exclusiveGroup, unique, ownedAtStart, value, unit, rate_Lps, capacity_L, scoopCooldown_s, tripTarget_s, startVolume_L, payoutMult, crateContents, actionAdded, realWorldRef`. `global` rows use `value` and `unit` only. `crateContents` lists what comes in the crate besides the item (hoses, cable, inlet fitting, spec sheet). `realWorldRef` names the real object a number comes from (the audit trail for the real-scale rule). `actionAdded` names the new physical action each item gives (Pillar 2).
   - **Load checks** (the game refuses to start and names the bad row): every `prereq_id` exists; prerequisites form no cycle; `price_cr ≥ 0`; every multiplier > 0; no item has a prerequisite that belongs to an `exclusiveGroup`; every physical value lies inside its safe range (Tuning Knobs).
5. **Real-scale rule:** every volume and rate must be plausible for the real object it names (a tablespoon, a 10 L jerrycan, a 10 L pail, a 100 L water barrel, a diaphragm hand pump, a 2-inch transfer pump, a 200 L drum, a 1,000 L IBC tote, a farm pond). Prices and the payout rate are free to tune; physical numbers may only move within real-world ranges.
6. **Shop order is linear in the MVP**, encoded as a chain of `prereq_id`: bucket → 100 L barrel → 200 L drum → hand pump → electric motor → second electric pump → IBC tank. An item is **Locked** until its prerequisite is owned, then **Visible**, and **Affordable** when `floor(credits) ≥ price`. Because the chain is linear, at most one item is ever Visible or Affordable. The second pump comes before the IBC so both purchases are felt: the second pump lifts shipped flow from 8 to 13.3 L/s (the drum's cap), and the IBC then removes that bottleneck (16 L/s).
7. **Buying:** the player pulls the ORDER lever on the terminal, which submits `Order(issuer, item)`. At the next tick (step 1) it is validated: rejected with `locked`, `cannot_afford` or `already_owned` (for `unique` items), with no state change. On success, credits are taken, the item is marked owned and granted to its owning system in the same step (Game State AC-E20), a crate record is created, and the system raises `Purchased(buyer, item, crateId)`. **No refunds**, which rules out buy-sell exploits. The table supports either/or groups (`exclusiveGroup`: buying one excludes the others) and non-unique items (`unique = false`: never Owned, each order just sends another crate), but the MVP uses neither.
8. **Crates (lifecycle owned here):** each crate record in the Economy slice holds `crateId, item, state, ticksLeft, landedTick`, with state **Inbound → Landed → Opened**.
   - **Inbound:** landing timer `delivery_delay` (4 s = 40 ticks). Economy's step (2f) counts it down. At 0, if a pad spot is free, Economy calls `Placement.Spawn(crateId, crate, spotPose)` (a direct call under Game State Rule 5b) and the crate is **Landed**, shown as a company drone dropping it. The pad has 4 crate spots. A spot is free unless it holds an unopened crate; empty crates on a spot are carried off by the landing drone (`Placement.Remove`) with a payslip line "Packaging recycled at your expense: −0 cr (waived)". If all 4 spots hold unopened crates, the crate waits at 0 and lands on the first tick a spot frees.
   - **Opening:** the player holds Use on a Landed crate for 1 s (prying the lid; presentation), which submits `OpenCrate(issuer, crateId)`. It is validated at step 1: rejected with `too_far` (outside interact range) or `already_open`. On success the crate is **Opened** (it stays as an empty crate until a drone takes it), and the item comes out: a hand tool goes straight into the issuer's hands (`Placement.SetHands`); a placeable item (barrel, drum, pump, motor kit, IBC) is created by `Placement.Spawn` at the crate's pose, with its `crateContents`. A printed spec sheet (one real-world stat and the catalogue line) flutters out and stays as a prop.
   - **Usable only once opened:** owning systems treat an owned item as usable only after its crate is Opened (they read the crate state from the Economy slice). Ownership itself (shop states, AC-E20) changes at purchase.
   - **Saving:** crate records are part of the slice, so a save during delivery resumes the same tick count on load. Time from ORDER to holding the item, with the player at the pad: about 12 s (2 s to the lever, 4 s delivery, 6 s to pry and lift out).
9. **Pacing rules for prices:** from the bucket onward, the time to afford each item must be longer than the time for the item before it, measured on the reference player of the pacing simulator (Formulas). The spoon → bucket step is exempt: it is set to the concept's 60–90 s spoon joke. The hand pump requires the 200 L drum (Nour, 2026-11-02).
10. **Nothing removes income:** the bucket stays usable after the pump; the electric motor upgrades the hand pump rather than replacing a working tool with nothing.
11. **The Earth counter is derived, never stored:** `Earth remaining = max(1 L, 1.386 × 10²¹ L − total shipped)`, computed as an exact decimal from Shipping's total shipped whenever it is displayed. It is not part of any slice and never saved. It is shown on the terminal header and a split-flap board at the launch pad; the HUD does not show it.
12. **Storage upgrades replace the old container.** The new barrel or drum comes out of its crate and is set down on the marked staging spot next to the storage spot. One interaction on it, "Swap in" (the Network's `SwapStorage` request, Hose/Storage Network GDD), pours all the water from the old container into the new one (exact, no loss), moves the new one onto the storage spot and the old one onto the staging spot. The old container stops being a tank and stays as a prop. Only one hand-stage container is active at a time. The drum also has a hose inlet fitting in its crate that must be screwed in (hold Use 2 s) before any hose can connect, so the drum adds its own new action and is the natural lead-in to the hand pump.
13. **What each storage tier is for** (Nour's ladder, 2026-11-02):
    - **10 L jerrycan:** one bucket fills it, so every trip ends at the LAUNCH lever. At the puddle (5 s trips) the 15 s rocket cycle makes the player wait for the next rocket; the stage is busy and pays little, on purpose.
    - **100 L barrel:** "never wait for the rocket". Pour without checking, launch when you choose; fewer lever walks.
    - **200 L drum:** the first container with a hose inlet; fitting it is the step before the hand pump.
    Players are expected to launch early (whenever a launch affords the next item); the HUD shortfall line ("ship 13 L more") invites it. Filling a barrel to the brim is allowed but slower, and never breaks a rule.
14. **Credit sinks** (redeploy and maintenance fees, contract bids) are post-MVP. The MVP's end beat plays after the first launch of a full drum + IBC pool. One joke item stays on sale after that: the **Employee of the Year Plaque** (100,000 cr), which does nothing but hang on the kiosk. It is exempt from the pacing rule and can be cut freely.
15. **Job titles:** the payslip prints a job title that rises with the latest purchase: Probationary Hydration Intern (start), Junior Hydration Associate (bucket), Hydration Associate (barrel), Senior Hydration Associate (drum), Extraction Technician (hand pump), Extraction Supervisor (motor), Regional Extraction Lead (second pump), Vice President of Water, Acting (IBC). The kiosk also prints a commendation slip when the puddle runs dry ("Puddle liquidated. Outstanding!") and at each drawdown threshold. Text lives in a string table; placeholder copy is fine for the MVP.

### States and Transitions

**Shop item states:**

| State | Condition | Can go to |
|---|---|---|
| Locked | Prerequisite not owned | Visible |
| Visible | Prerequisite owned, `floor(credits) < price` | Affordable; Excluded (later) |
| Affordable | Prerequisite owned, `floor(credits) ≥ price` | Visible (credits spent elsewhere, post-MVP); Owned; Excluded (later) |
| Owned | Bought, or `ownedAtStart` (spoon, 10 L jerrycan) | — (final) |
| Excluded (post-MVP) | Another item in its either/or group is owned | — (final) |

States are derived each tick from credits and items owned; only Owned (and later Excluded) is saved.

**Crate states:** Inbound (timer) → Landed (on a pad spot) → Opened (empty crate, removed by the next drone). Saved in the slice.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State & Simulation | Economy slice (credits in millicredits, items owned, crate records); raises `Purchased`, `RequestRejected` | Tick (Economy step 2f); `Order` and `OpenCrate` requests at step 1 |
| Shipping & Launch | `credits_per_litre`, multipliers, launch values (table) | `CreditLaunch(litres)` call at liftoff; total shipped (for the Earth counter) |
| Placement (Game State) | `Spawn` and `Remove` calls (crates, unboxed items); hands item for unboxed tools | Pad spot poses; crate poses |
| Water Source Model | Start volume per source (copied at new game) | — |
| Hose, Storage & Flow Network | Tank capacities, hose caps and lengths, crate contents | — |
| Hand Tools | Scoop volume, scoop cooldown, trip target; grant at purchase | Reads crate state (usable once opened) |
| Pumps & Machines | Pump rates, cable length, post sockets; grant at purchase | Reads crate state |
| HUD | Credits, next item and its progress | `Purchased` |
| Corporate Radio | Item names, payout amounts, Earth counter value | `Purchased`, `LaunchCompleted` |
| Save & Load | The slice (credits, items owned, crate records) | Load |

## Formulas

The **payout** formula is defined as:

`payout_mc = round(litres × credits_per_litre × region_multiplier × contract_multiplier × 1000)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| litres | double (L) | > 0, finite | calculated (Shipping, passed to `CreditLaunch` at liftoff) | Litres launched; anything else is ignored and logged |
| credits_per_litre | double | 5–20 | data file | Base credits per litre (MVP 10) |
| region_multiplier | double | 0.5–5 | data file | Per region (MVP 1) |
| contract_multiplier | double | 0.5–5 | data file | Per active contract (MVP 1) |

**Output Range:** millicredits, 1 and up. A full 10 L jerrycan pays 100 cr, a full 200 L drum 2,000 cr, a full drum + IBC pool 12,000 cr. A 0.8 L spoon launch pays 8 cr, exactly the bucket's price.

The **effective_rate** formula (an estimate for reading the table; the pacing simulator below is the reference) is defined as:

`hand tool: rate_eff = min(volume_per_trip / (trip_time + lever_time / trips_per_launch), storage / launch_cycle)` · `pumps: rate_eff = min(Σ pump_rate × duty, pool / launch_cycle)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| volume_per_trip | double (L) | 0.01–12 | data file | Litres per scoop or bucket trip |
| trip_time | double (s) | 1–30 | data file (`tripTarget_s`, a level-design target, not a timer) | Spoon 1.25 s; bucket 5 s at the puddle, 20 s at the pond |
| trips_per_launch | int | ≥ 1 | calculated | `max(1, floor(storage / volume_per_trip))`; early launches make it smaller |
| storage | double (L) | 10–200 | data file | Active hand-stage container |
| pump_rate | double (L/s) | 0.5–20 | data file | Pump flow while running (each pump also capped by the 10 L/s hose) |
| duty | double | 0.5–1 | playtest estimate | Share of time a hand pump is actually cranked (electric = 1) |
| pool | double (L) | 200–1,200 | data file | Pad pool capacity |
| lever_time | double (s) | 2–5 | playtest estimate | Player time to walk to and pull the LAUNCH lever (3 s), charged once per launch |
| launch_cycle | double (s) | 8–25 | data file (Shipping) | Lever to next rocket (15 s); pumping continues meanwhile, so it only caps small pools |

**Output Range:** about 0.012 L/s (spoon) to 16 L/s (two electric pumps with drum + IBC) in the MVP. Two pumps into the drum alone are capped at 200 / 15 = 13.3 L/s; the IBC lifts that to 16.

The **time_to_afford** formula is defined as:

`time_to_afford = max(0, price − floor(credits)) / (rate_eff × credits_per_litre × region_multiplier × contract_multiplier)`; infinite when `rate_eff = 0`.

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| price | int (cr) | 0 and up | data file | Next item's price |
| credits | cr | 0 and up | calculated | Credits after the last purchase |
| rate_eff | double (L/s) | see above | calculated | Effective rate at the current tier |
| credits_per_litre, multipliers | double | as above | data file | — |

**Output Range:** 0 s and up (∞ when nothing can be earned). A quick estimate only; Rule 9 is checked on the simulator.

**MVP tuning table** (credits_per_litre = 10):

| id | Prereq | Real-world basis | Value | Price | Action added |
|---|---|---|---|---|---|
| spoon | — (owned at start) | Tablespoon | 15 mL per 1.25 s scoop | — | Scoop |
| jerrycan_10l | — (owned at start) | 10 L jerrycan | 10 L storage | — | Pour into storage, launch every trip |
| bucket | — | 10 L pail | 10 L per scoop; 1 s scoop cooldown; trip target 5 s (puddle) / 20 s (pond) | 8 | Wade in and carry |
| barrel_100l | bucket | 100 L water barrel | 100 L storage (replaces the jerrycan) | 220 | Place and swap in |
| drum_200l | barrel_100l | 200 L steel drum | 200 L storage (replaces the barrel) with a screw-in hose inlet | 260 | Swap in, screw in the hose inlet |
| hand_pump | drum_200l | Double-acting diaphragm hand pump (~75 L/min) | 1.25 L/s while cranked | 380 | Place intake, lay hose, crank |
| electric_motor | hand_pump | 2-inch 1.5 kW transfer pump (~480 L/min) | 8 L/s | 1,100 | Fit motor, run cable, start/stop |
| electric_pump_2 | electric_motor | Second 2-inch transfer pump | +8 L/s | 15,000 | Run a second intake and cable |
| ibc_tank | electric_pump_2 | 1,000 L IBC tote | 1,000 L storage, linked to the drum | 40,000 | Place and plumb a second tank |
| plaque (joke) | ibc_tank | Engraved office plaque | — | 100,000 | Hang it on the kiosk |
| puddle / farm pond | — | Small rain puddle / farm pond | 40 L / 20,000 L | — | — |

Global rows include: `credits_per_litre` 10, `delivery_delay` 4 s, `pad_crate_spots` 4, discharge hose cap 10 L/s and section 20 m, suction hose 6 m, link hose 5 m, power cable 30 m, power post sockets 2, rocket cargo 1,500 L, `earth_total_water` 1.386 × 10²¹ L. Shipping owns the launch cycle (15 s) and countdown (3 s) rules; their values sit in this table.

**Pacing simulator (reference player).** A 10 Hz script (`tools/pacing/sim3.py` in the design folder) that plays the ladder: buys as soon as affordable; launches when the pool is full or when the launch affords the next item and a rocket is on the pad; 3 s at the lever; 15 s rocket cycle with pumping continuing; ORDER walk 2 s + 4 s delivery + 6 s to pry and lift out; install times bucket 0, barrel 8 s, drum 10 s, hand pump 25 s, motor 15 s, second pump 25 s, IBC 25 s; spoon 15 mL per 1.25 s at the puddle; bucket trips 5 s at the puddle and 20 s at the pond; hand-pump duty 0.7; the 40 L puddle is used before the pond.

| Minute | Event | Pond drained | Beat |
|---|---|---|---|
| ~1:14 | Bucket bought (in hand ~1:26) | 0% | Spoon stage ends (about 75 s, inside the concept's 60–90 s) |
| ~2:04 | Puddle dry | 0% | Commendation slip; bucket moves to the pond |
| ~2:10 | 100 L barrel bought | 0% | No more waiting for the rocket |
| ~3:13 | 200 L drum bought | ~0.1% | Screw in the hose inlet |
| ~4:38 | Hand pump bought | ~0.25% | First pump; crate lands by the dried puddle |
| ~7:20 | Electric motor bought | ~0.8% | Hands free |
| ~7:25 / ~8:05 / ~9:07 | Electric | 1% / 2.5% / 5% | Thresholds |
| ~10:35 | Second electric pump bought | ~8.5% | The drum fills in 12.5 s; launch every rocket |
| ~11:19 / ~13:49 | Two pumps | 10% / 20% | Bands |
| ~16:00 | IBC tank bought | ~28.6% | Relief: a 1,200 L pool; end beat after its first full launch |
| ~16:17 | IBC | 30% | Band |

**Time to afford each step (reference player):** spoon → bucket 75 s (exempt) · bucket → barrel 55 s · barrel → drum 63 s · drum → hand pump 85 s · hand pump → motor 162 s · motor → second pump 195 s · second pump → IBC 325 s. Rises at every step from the bucket on. The longest gap between visible changes is about 2:42 (hand pump stage).

**Other players (same simulator):**
- **Fills before launching** (launches only when the pool is full from the hand pump on): hand pump at the same ~4:38, IBC at ~18:00, longest gap about 3:28. The hand stage is slower than the next step for this player, which is allowed: Rule 9 is a pricing rule for the reference player.
- **Lazy launcher** (launches 20 s after the pool fills): IBC at ~27:40, longest gap about 6:15 (between the 20% band and the IBC). Two pumps into one drum need attention; the strained-idle sound of a Stalled pump (Pumps GDD) is the cue.

The pond outlasts the ladder (about 70% left at the IBC). The spoon moves about 0.72 L a minute.

**What the real-scale rule costs, and how it is handled:**
- A real hand pump (1.25 L/s) moves only about 0.5% of the pond during its whole stage (about a 5 mm drop on a 20 m² pond). **Drawdown keeps the 1% threshold and a shallow intake shelf** shaped from the baked basin so that 1% shows at least 25 cm of exposed mud from the hand-pump handle (Water Source basin outcomes); it arrives around the motor purchase.
- A real tablespoon cannot visibly lower even the puddle. The spoon gives honest local feedback instead (ripple, a scoop mark in the mud at the puddle edge); the puddle visibly drops from the bucket on.
- A real 4 L/s electric pump would leave 9–11 minutes between 10% bands. 8 L/s (a real 2-inch pump) keeps them inside the 5-minute target.

*Formulas, table and simulation by `economy-designer` with `systems-designer`; revised after design review 1.*

## Edge Cases

- **If the player clicks ORDER twice**: the second request is validated after the first; it is rejected (`already_owned`) and credits are taken once.
- **If credits exactly equal the price**: the item is affordable.
- **If credits are fractional**: affordability and display both use `floor(credits)` on whole millicredits, so the screen and the rule always agree.
- **If water is in a tank or bucket but not launched**: it pays nothing yet; a purchase never waits on unlaunched water.
- **If a launch carries a tiny amount (one spoonful)**: it is valid and pays (15 mL = 0.15 cr); the radio can joke about it.
- **If `CreditLaunch` receives NaN, infinity, 0 or a negative number**: ignored and logged once per call; credits unchanged.
- **If the game is saved during a launch**: the payout happens in the same tick step (2c) as the shipped total, through `CreditLaunch`, so a save has either both or neither.
- **If the tuning table changes after a save** (patch): prices are read live from the table; source start volumes keep their saved copy (Water Source rule).
- **If a save owns an item id that no longer exists in the table**: it is kept in the save, ignored in play, and logged.
- **If the pond runs dry**: at the reference pace the pond is about 30% drained when the IBC arrives and lasts about another 15 minutes at 16 L/s, so it can only run dry after the last real purchase. Water already in tanks still pays, the radio plays the end beat if it has not played, and the game never softlocks.
- **If a purchase would leave the player with no way to earn**: impossible by design; no item removes or disables an owned income source.
- **If an either/or item is bought** (post-MVP): the others in its group become Excluded permanently; no refund.
- **If ORDER is pulled while paused**: the request is queued and applied after resuming. If a save happens while it is queued, the request is dropped and logged (Game State save rule), and the player simply pulls the lever again.
- **If the Earth counter's total would reach zero**: it shows 1 L (the clamp); real total shipping is many orders of magnitude smaller.
- **If a crate lands while the player stands on the free spot**: the drone hovers until the spot is clear (presentation only; the crate counts as Landed on schedule and the drop plays when clear).
- **If all 4 pad spots hold unopened crates**: the next crate waits at 0 and lands the tick a crate is opened (only possible if the player buys several items without opening any).
- **If the player never opens a crate**: the item stays in the crate (owned, not usable); the radio nags 60 s after landing, once.
- **If the player fills the barrel before launching**: a 100 L launch pays 1,000 cr, enough for the drum and then the hand pump in quick succession. Allowed: only one item is Visible at a time, so it is two clean purchases, not a menu choice.

## Dependencies

**Upstream:**
| System | Nature | Interface |
|---|---|---|
| Game State & Simulation (`design/gdd/game-state-and-simulation.md`) | Hard | Slice, tick (Economy step 2f), requests, `Purchased`, `RequestRejected`, Placement `Spawn`/`Remove` |
| Shipping & Launch (`design/gdd/shipping-and-launch.md`) | Hard | `CreditLaunch(litres)` at liftoff; total shipped |

**Downstream:**
| System | Nature | Interface |
|---|---|---|
| Water Source Model (`design/gdd/water-source-model.md`) | Hard | Start volumes (40 L puddle, 20,000 L farm pond) |
| Hose, Storage & Flow Network (`design/gdd/hose-storage-and-flow-network.md`) | Hard | Tank capacities (10 L jerrycan, 100 L barrel, 200 L drum, 1,000 L IBC), hose caps and lengths, crate contents, `SwapStorage` |
| Hand Tools (`design/gdd/hand-tools.md`) | Hard | Spoon and bucket volumes, scoop cooldowns, trip targets; crate state |
| Pumps & Machines (`design/gdd/pumps-and-machines.md`) | Hard | Pump rates (1.25 L/s hand, 8 L/s electric), cable length, post sockets; crate state |
| Shipping & Launch | Hard | Payout rate, cargo capacity, cycle values |
| HUD, Corporate Radio | Soft | Credits, next item, Earth counter, `Purchased` |
| Save & Load (`design/gdd/save-and-load.md`) | Hard | Slice |
| Contracts, Regions (Alpha) | Hard | Multipliers, region rows |

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| `credits_per_litre` | 10 | 5–20 | Scale all prices with it |
| Spoon volume / scoop time | 15 mL / 1.25 s | 10–30 mL / 1–2 s | Real spoon range |
| Bucket volume | 10 L | 8–12 L | Real pail range |
| Bucket scoop cooldown | 1 s | 0.5–2 s | Hand Tools rule 2 throttle; not the trip time |
| Bucket trip target | 5 s puddle / 20 s pond | 3–8 / 15–30 s | Level-design targets, measured in playtest |
| Hand pump rate | 1.25 L/s | 0.6–1.5 L/s | Real hand pumps |
| Hand pump duty (estimate) | 0.7 | 0.5–0.9 | Measure in playtest |
| Electric pump rate | 8 L/s | 4–10 L/s | Below 8, add 5% thresholds |
| Storage ladder | 10 L → 100 L → 200 L | 5–20 / 60–120 / 150–220 L | Real jerrycans, barrels, drums |
| IBC tank | 1,000 L | 600–1,250 L | Real IBC sizes |
| Launch cycle | 15 s | 8–25 s | Owned by Shipping; makes the player wait at the puddle in the jerrycan stage |
| Rocket cargo capacity | 1,500 L | ≥ largest pool | Shipping |
| Prices | As in the table | ±20% each | Re-run the simulator after any change; the bucket → barrel → drum margins are small (55 / 63 s) |
| Farm pond volume | 20,000 L | 18,000–25,000 L | Real farm-pond range; Water Source GDD |
| Crate delivery delay (`delivery_delay`) | 4 s | 3–5 s | Order to crate landing; fixed per item, no randomness |
| Pad crate spots | 4 | 3–6 | Level layout |
| Crate pry time | 1 s | 0.5–2 s | Presentation; the request is sent when the hold completes |

## Visual/Audio Requirements

- **Terminal:** a weathered navy-and-cream company kiosk on the launch pad, with a chunky world-space screen readable from 2 m (not a full-screen menu, so the pond stays visible behind it), a big safety-orange ORDER lever, and the whole MVP catalogue as item cards (Locked cards greyed with "Requires: …"). An "Employee of the Month: YOU" sticker. A slot that prints payslips and commendation slips.
- **Payout:** credits count up in a chunk at each launch, with ticks and an "AFFORDABLE" chime when a price is crossed, plus the payslip (job title, litres × rate, levy line).
- **Purchase:** an "APPROVED" stamp, a pneumatic thunk, drone audio, a branded crate landing with a small camera shake; empty crates lifted away by the drone; first use of a new tool gets its own sound and a radio line.
- **Unboxing:** a prying creak, the lid popping off with simple physics, a spec sheet fluttering out.
- **Earth counter:** split-flap board by the launch pad; only the last digits flip on each shipment; confetti on the contribution line.

*`art-director` not consulted (optional for `Economy`); review at `/art-bible`.*

## Game Feel

- Credits arrive in chunks at launch, never as a trickle, so crossing a price is one clear moment.
- From ORDER to holding the new item: about 12 s, never more than 15 s.
- Only one item is ever on offer, so each purchase is a clean beat rather than a menu decision (MVP; see Open Questions).
- The second pump should feel frantic (launch on every rocket) and the IBC like relief.

## UI Requirements

- HUD: credits and the next item's progress ("Hand Pump 250 / 380") at all times; when short, also the gap in litres ("ship 13 L more").
- Terminal card per item: name, icon, one real-world stat, price, the new action it adds, and its state (Locked shows "Requires: …"; Visible shows the shortfall; Affordable is marked by text and icon, never by colour alone).
- Terminal header: the Earth counter.

📌 **UX Flag — Economy, Progression & Shop:** this system has UI requirements (terminal screen and HUD credits line). Run `/ux-design` for the terminal screen and the HUD before writing epics.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 2, 7, 8, Interactions | `design/gdd/game-state-and-simulation.md` | `CreditLaunch` direct call, `Purchased`, request validation, tick order (2f), Placement `Spawn`/`Remove`, AC-E20 grant |
| Rule 3 | Number-representation ADR (pending) | Millicredits; exact total shipped |
| Rules 8, 12 (swap tested by Hose AC-26) | `design/gdd/hose-storage-and-flow-network.md`, `design/gdd/pumps-and-machines.md`, `design/gdd/hand-tools.md` | Unboxed items, `SwapStorage`, inlet fitting, usable-once-opened |
| Rule 11 | `design/gdd/shipping-and-launch.md` | Total shipped |
| MVP tuning table, pacing | `design/gdd/water-source-model.md`, `design/gdd/drawdown-visuals-and-reveals.md` | Puddle and farm-pond volumes; threshold times |
| Real-scale rule, Earth counter, spoon length | `design/gdd/game-concept.md` Core Mechanics #6 and the spoon joke (60–90 s) | Source of the rules |

## Acceptance Criteria

Tags: **[EditMode]** = Unity Test Framework EditMode test, **[PlayMode]** = PlayMode test, **[Script]** = design-tool check run outside Unity (the pacing simulator), **[Manual]** = playtest check by Nour. Systems that are not built yet are replaced by stubs named in each AC (Game State convention).

**Payout and credits**
- **AC-01 [EditMode]** GIVEN credits_per_litre = 10 and both multipliers = 1, WHEN `CreditLaunch(200)` is called, THEN credits rise by exactly 2,000,000 mc (2,000 cr).
- **AC-02 [EditMode]** GIVEN multipliers = 1, WHEN `CreditLaunch(0.8)` and then `CreditLaunch(0.015)` are called, THEN credits rise by exactly 8,000 mc and then 150 mc.
- **AC-03 [EditMode]** GIVEN water in the jerrycan and a bucket and a stub Shipping that makes no call, WHEN 600 ticks run, THEN credits do not change.
- **AC-04 [EditMode]** GIVEN region_multiplier = 2 and contract_multiplier = 1.5, WHEN `CreditLaunch(100)` is called, THEN credits rise by exactly 3,000 cr, applied once.
- **AC-05 [EditMode]** GIVEN a stub Shipping that calls `CreditLaunch(50)` and adds 50 L to its total in step 2c, WHEN a save is taken at the end of that tick and loaded, THEN it has both the shipped litres and the payout (Game State AC-E9).
- **AC-06 [EditMode]** GIVEN credits = 10, WHEN `CreditLaunch` is called with NaN, +∞, 0 and −5, THEN credits stay exactly 10 and one warning is logged per call.
- **AC-07 [EditMode]** GIVEN credits = 0, WHEN `CreditLaunch(0.05)` is called 10 times, THEN `floor(credits)` = 5 exactly and an item priced 5 is Affordable.

**Shop rules**
- **AC-08 [EditMode]** GIVEN the hand pump is Visible and credits = 379.9, WHEN states are derived, THEN it is Visible, not Affordable, and Economy's `NextItemProgress` = (379, 380).
- **AC-09 [EditMode]** GIVEN credits = 380 exactly and the 200 L drum owned, WHEN states are derived, THEN the hand pump is Affordable.
- **AC-10 [EditMode]** GIVEN the 200 L drum is not owned, WHEN states are derived, THEN the hand pump is Locked whatever the credits, and its card text is "Requires: 200 L drum".
- **AC-11 [EditMode]** GIVEN the MVP table, WHEN it loads, THEN the prerequisites form exactly the chain bucket → barrel → drum → hand pump → motor → second pump → IBC → plaque; GIVEN a fake table with a prerequisite cycle or a missing prerequisite id, WHEN it loads, THEN loading fails with an error naming the row.
- **AC-12 [EditMode]** GIVEN credits = 500, the hand pump Affordable and `unique`, WHEN two `Order(hand_pump)` requests arrive in the same tick, THEN one `Purchased(hand_pump)` and one `RequestRejected(already_owned)` are raised and credits end at exactly 120.
- **AC-13 [EditMode]** GIVEN credits = 379.9 and the hand pump Visible, WHEN `Order(hand_pump)` applies, THEN `RequestRejected(cannot_afford)` and the slice is unchanged; GIVEN the motor Locked, WHEN `Order(electric_motor)` applies, THEN `RequestRejected(locked)` and the slice is unchanged.
- **AC-14 [EditMode]** GIVEN an item is Owned, THEN no request type exists to sell or refund it (static check) and credits are unchanged by any request other than `Order`.
- **AC-15 [EditMode]** GIVEN a new game, WHEN it starts, THEN the spoon and the 10 L jerrycan are Owned (from `ownedAtStart`) and credits = 0.
- **AC-16 [EditMode]** GIVEN the game is paused, WHEN ORDER is pulled, THEN the request is applied on the first tick after resuming; GIVEN a save while it is queued, THEN the request is dropped and logged.
- **AC-17 [EditMode]** GIVEN any owned set, WHEN any purchase completes, THEN the owned set afterwards contains the set before (buying the electric motor leaves the hand pump Owned).
- **AC-18 [EditMode]** GIVEN any tier from spoon to plaque, WHEN states are derived at every 1-credit step, THEN at most one item is Visible or Affordable.

**Tuning table and pacing**
- **AC-19 [EditMode]** GIVEN the MVP table, WHEN it loads, THEN every row with a volume, rate or capacity has a `realWorldRef` that names a physical object (not a document), and every purchasable row has a non-empty `actionAdded`.
- **AC-20 [EditMode]** GIVEN the MVP table, WHEN it loads, THEN every physical value lies inside its Tuning Knobs safe range; GIVEN a fake row with a 50 L bucket, THEN loading fails naming the row.
- **AC-21 [Script]** GIVEN the pacing simulator reading its prices and physical values from an export of the MVP tuning table (not hard-coded), and the reference-player fixture (Formulas), WHEN it runs, THEN the spoon → bucket step is 60–90 s and each later step, from bucket → barrel to second pump → IBC, is strictly longer than the one before.
- **AC-22 [EditMode]** GIVEN the tuning table changes after a save, WHEN the save loads, THEN prices come from the new table and source volumes keep their saved values.
- **AC-23 [EditMode]** GIVEN a save owns an item id missing from the table, WHEN it loads, THEN the id is kept in the save, ignored in play, and one warning is logged.

**Earth counter**
- **AC-24 [EditMode]** GIVEN a stub Shipping total of 0 L, WHEN the counter is read, THEN it equals 1,386,000,000,000,000,000,000 exactly.
- **AC-25 [EditMode]** GIVEN a stub Shipping total of 1 L, WHEN the counter is read, THEN it is exactly 1 lower than at 0 L.
- **AC-26 [EditMode]** GIVEN a stub Shipping total of 1.386e21 L or more, WHEN the counter is read, THEN it equals 1 L exactly; and a saved file contains no Earth counter field.

**Crates and delivery**
- **AC-27 [EditMode]** GIVEN an Affordable item and a stub Placement, WHEN `Order` applies at tick T, THEN `Purchased` is raised at T, a crate record is Inbound with 40 ticks, and the stub receives exactly one `Spawn(crateId, crate, spot)` at tick T + 40.
- **AC-28 [EditMode]** GIVEN a crate Inbound with 25 ticks left, WHEN saved and loaded, THEN it lands exactly 25 ticks after load.
- **AC-29 [EditMode]** GIVEN 4 Landed unopened crates and a stub Placement, WHEN a fifth item's timer reaches 0, THEN it stays Inbound; WHEN one crate is opened at step 1 of tick T, THEN the new crate lands on that spot at step 2f of the same tick T and the empty crate receives `Remove`.
- **AC-30 [EditMode]** GIVEN a Landed bucket crate and player 0 within range, WHEN `OpenCrate` applies, THEN the crate is Opened, the bucket is player 0's hands item (stub Placement), and Hand Tools reports the bucket usable; GIVEN the player 3 m away, THEN `RequestRejected(too_far)`; GIVEN an Opened crate, THEN `RequestRejected(already_open)`.
- **AC-31 [EditMode]** GIVEN a crate that lands at tick T, THEN its record holds `landedTick` = T and state Landed until opened (the Corporate Radio's 60 s nag reads this; the nag itself is a Radio AC).

**Feel and playtest**
- **AC-32 [Manual]** GIVEN the player waiting at the pad, WHEN ORDER is pulled, THEN the new item is in hand within 15 s.
- **AC-33 [Manual]** GIVEN a fresh MVP run by Nour playing as the reference player, WHEN purchase times are read from the play log, THEN the bucket is bought at 1:00–1:30, the hand pump at 4:00–5:15, the IBC at 14:00–18:00, and no gap between visible changes (a purchase, the puddle running dry, or a `DrawdownThreshold`) exceeds 5 minutes. GIVEN a run launching only about 20 s after the pool fills, THEN the IBC arrives by 30:00 and no gap exceeds 6.5 minutes.
- **AC-34 [Manual]** GIVEN the terminal from 2 m away, WHEN the player reads it, THEN item names, prices and the Affordable mark are legible and Affordable is shown by text and icon, not colour alone.
- **AC-35 [Manual]** GIVEN a full MVP run, THEN the payslip job title changes with each purchase, the puddle commendation slip prints, and Nour has read every catalogue line and payslip line once for tone.

*Written in the qa-lead format (Given-When-Then, test tags). AC-21 and AC-33 cover the pacing; the storage swap is tested in the Hose/Storage Network GDD (AC-26); AC-01 to AC-07 and AC-24 to AC-26 depend on the number-representation ADR confirming millicredits and an exact total shipped.*

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check against the pillars, 2026-11-02; design review 1 found NEEDS REVISION, revised the same day).** Pillar 1: the ladder runs spoon → two electric pumps and an IBC with rising time-to-afford. Pillar 2: every purchase adds a physical action (the drum now adds the inlet fitting) and arrives as a crate pried open and installed by hand. Pillar 4: satire lives in the transaction (catalogue copy, payslip and job titles, commendation slips, Earth counter, the plaque) without preaching. Anti-pillars respected: one currency, no refunds, no menu-only installs. Watch item: the Earth counter must stay a prop, not a HUD guilt meter.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Add one "which first?" choice in the MVP (for example IBC vs second pump in either order)? Kept linear for now (Nour, 2026-11-02) | game-designer | First MVP playtest |
| Number type for credits (millicredits proposed) and the exact total shipped | technical-director | Number-representation ADR |
| Credit sinks after the last purchase (maintenance, redeploy fees) | economy-designer | Regions / Alpha |
| Measure hand-pump duty, bucket trip times and launch habits in playtest, then re-run the simulator | economy-designer | First MVP playtest |
| Is the lazy launcher's 6-minute gap acceptable, or add a 25% reveal? | game-designer | First MVP playtest |
