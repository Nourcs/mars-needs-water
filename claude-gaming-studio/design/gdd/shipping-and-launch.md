# Shipping & Launch

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 1 (Start Tiny, End Enormous); Pillar 4 (Bleak Corporate Satire)

## Summary

Shipping & Launch is the end of the pipeline: the player pulls the LAUNCH lever on the pad, the rocket loads everything in the storage on the pad, lifts off, and the company pays credits for every litre (`LaunchCompleted`). A new rocket lands 15 s after the lever pull. Launching is the only way to earn, so it is the heartbeat of the loop.

> **Quick reference** — Layer: `Core gameplay` · Priority: `MVP` · Key deps: `Hose, Storage & Flow Network`, `Economy`, `Interaction`

## Overview

The launch pad holds one small cargo rocket, a **storage spot** where the active container sits (jerrycan, barrel, then the 200 L drum, with the IBC linked to it), and a console with a big LAUNCH lever. Pulling the lever submits a launch request. The rocket takes all the water in the storage spot's pool (up to its cargo capacity) through the pad's built-in feed, counts down 3 s, lifts off, Shipping credits Economy directly and raises `LaunchCompleted(litres)` for the radio, HUD and log. The pad then stays busy until a replacement rocket lands. Pumps keep filling the pool during the turnaround. Automatic launching is post-MVP and will be built by hand (a feed line and timer), never bought.

## Player Fantasy

**"Send it."** Every launch is a small celebration of a small amount: a whole rocket for 10 litres of pond water, a countdown, a roar, a payslip. Early on you launch after every bucket, so the rocket becomes your rhythm. Later you launch 1,200 litres at a time, and the same rocket feels like it's working for you.

- **Anchor moment:** the first launch: you pull the lever on a jerrycan holding 0.6 L of spoon water (40 scoops), the rocket roars into the sky, and the radio thanks you warmly for your 6 credits, exactly the price of a bucket.
- **Tone:** absurd scale mismatch played straight (Pillar 4): a full rocket launch for a puddle's worth of water.

## Detailed Design

### Core Rules

1. **The pad tank:** the tank on the storage spot is the *pad tank*. Its pool (the pad tank plus any tanks linked to it) is the *pad pool*. Only the pad pool can be launched. Shipping finds the pad tank from the storage spot (Placement) on every request and never keeps a stored tank id, so a storage swap needs no special handling.
2. **Launch request:** pulling the LAUNCH lever submits `Launch(issuer)`. It is validated at tick step 1, checks in this order:
   1. `too_far`: the issuer's eye point (Game State Rule 12) is more than the lever range (3 m, FPC interact range for levers) from the lever.
   2. `pad_busy`: the state is not Ready (Countdown or Away).
   3. `nothing_to_ship`: the pad pool holds less than `min_launch` (1 mL = 0.001 L). This stops a floating-point residue from being launched for a 0 mc payout; a single spoon scoop (15 mL) is always enough.
   Requests in one step 1 apply in arrival order, and each sees the ones applied before it (as for purchases, Game State AC-E1), so a second pull in the same tick is `pad_busy`.
3. **Loading:** on a valid launch, Shipping calls `Take(pad pool, min(contents, cargo_capacity))` (Hose/Storage Network) and holds the returned litres in the rocket (*litres aboard*). Loading is instant (the pad feed is built in).
4. **Countdown and liftoff:** on a valid launch in tick N, Shipping raises `LaunchStarted(litres)` and starts the countdown timer of `countdown_ticks` (30 at defaults). Following the tick convention (Game State Rule 3: a timer of n ticks started in tick N fires at its owner's step in tick N+n), liftoff happens at step 2c of tick N+30. At liftoff Shipping adds the litres aboard to the total shipped, calls `Economy.CreditLaunch(litres)` directly in the same step, sets litres aboard to 0, and raises `LaunchCompleted(litres)` as a notification. Payout and shipped total change in the same tick, so a save can never hold one without the other.
5. **Turnaround:** at liftoff the state becomes Away and Shipping starts the turnaround timer of `turnaround_ticks = cycle_ticks − countdown_ticks` (120 at defaults). It fires at step 2c of tick N+150, which sets the state to Ready (the replacement rocket has landed). By the tick convention, a request at step 1 of tick N+150 is still rejected with `pad_busy`, and the first request that can be accepted is at step 1 of tick N+151. The whole cycle from lever to next rocket is `launch_cycle` (15 s at default: 3 s countdown + 12 s until the new rocket lands). During the cycle, pours and pumping into the pad pool continue normally.
6. **Timer conversion:** `countdown_ticks` and `cycle_ticks` are converted from seconds once, when the timer starts, with Game State Rule 8: `ticks = max(1, round(seconds / dt))`. The safe ranges (countdown ≤ 5 s, cycle ≥ 8 s) keep `turnaround_ticks` at 1 or more.
7. **Cargo capacity:** 1,500 L in the MVP, more than the largest MVP pool (1,200 L), so in practice a launch always empties the pad pool. If a pool ever holds more than the cargo capacity, the rest stays for the next rocket.
8. **No cost:** a launch costs nothing and any amount from 1 mL up can go. The cycle time is the only limit on spamming.
9. **The lever is the only trigger** in the MVP. Auto-launch (Alpha) will be a machine the player builds and plumbs, consistent with "no menu buttons".
10. **Saving:** the Shipping slice saves `{state (Ready/Countdown/Away), timer remaining in seconds, litres aboard, total shipped}`. Following Game State Rule 8, the timer is held as whole ticks in state and saved as remaining seconds, then converted again on load, so the countdown or turnaround resumes where it stopped. No water or credits are ever lost or doubled.
11. **Conservation:** every litre is in exactly one place: a source, a tool, a tank, **litres aboard**, or total shipped. Every conservation check sums all five (Game State AC-X1). Hose Rule 11 and AC-09 must include litres aboard too.
12. **Total shipped exactness:** total shipped is stored exactly, in the type set by the pending number-representation ADR (proposed: integer microlitres, rounded once at liftoff), the same ADR that confirms Economy's millicredits. The same value that is added to the total is passed to `CreditLaunch`, so the payslip and the Earth counter always agree.

### States and Transitions

| State | Condition | Can go to |
|---|---|---|
| Ready | Rocket on pad | Countdown (valid `Launch` at step 1 of tick N) |
| Countdown | Loaded, `countdown_ticks` timer | Away (liftoff at 2c of tick N+30; `LaunchCompleted` raised) |
| Away | No rocket on pad, `turnaround_ticks` timer | Ready (at 2c of tick N+150; first accepted request at step 1 of N+151) |

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Shipping slice (state, timer, litres aboard, total shipped); `LaunchStarted`, `LaunchCompleted`, `RequestRejected(issuer, too_far / pad_busy / nothing_to_ship)` | Tick step 1 requests; step 2c (timers, liftoff) |
| Placement | — | Which tank is on the storage spot (read on every request); issuer eye point for the range check |
| Hose/Storage Network | `Take` calls | Litres taken; pad pool contents |
| Economy | `CreditLaunch(litres)` call at liftoff; total shipped (Earth counter) | `launch_cycle`, countdown and cargo capacity (Economy tuning table, globals row; Shipping owns the rules) |
| Interaction (FPC) | `Launch` request and its rejection codes | LAUNCH lever (3 m range); FPC owns the binding and the player-facing rejection text |
| Corporate Radio, HUD, Audio, Play Log | `LaunchStarted`, `LaunchCompleted` | — |
| Save & Load | The slice | Load |

## Formulas

The **launch_load** formula is defined as:

`litres = min(pad_pool_contents, cargo_capacity)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| pad_pool_contents | double (L) | 0–1,200 (MVP) | Hose/Storage Network | Water in the pad pool |
| cargo_capacity | double (L) | 1,200–5,000 | Economy tuning table | Rocket cargo (MVP 1,500); never below the largest pool |

**Output Range:** 0 to cargo_capacity; anything below `min_launch` (0.001 L) is rejected as `nothing_to_ship`.

The **launch_timers** formula is defined as:

`countdown_ticks = max(1, round(countdown / dt))` · `cycle_ticks = max(1, round(launch_cycle / dt))` · `turnaround_ticks = cycle_ticks − countdown_ticks`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| countdown | double (s) | 2–5 | Economy tuning table | Lever to liftoff (3 s) |
| launch_cycle | double (s) | 8–25 | Economy tuning table | Lever to next rocket (15 s) |
| dt | double (s) | 1/30–0.2 | Game State | Tick length (0.1 s) |

**Output Range:** at defaults 30, 150 and 120 ticks; at 20 Hz 60, 300 and 240. Always rounded, never `ceil` (Game State Rule 8).

The **max_ship_rate** formula is defined as:

`max_ship_rate = min(cargo_capacity, pool_capacity) / launch_cycle`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| cargo_capacity | double (L) | 1,200–5,000 | Economy tuning table | Rocket cargo (1,500) |
| pool_capacity | double (L) | 10–1,200 (MVP) | Hose/Storage Network | Total capacity of the pad pool (pad tank plus linked tanks) |
| launch_cycle | double (s) | 8–25 | Economy tuning table | Lever to next rocket (15 s) |

**Output Range:** the most litres per second shipping can carry. MVP: 10 L jerrycan → 0.67 L/s; 200 L drum → 13.3 L/s; drum + IBC → 80 L/s. At the puddle (5 s bucket trips) the 10 L jerrycan's cap binds and the player waits for the next rocket, which is what the 100 L barrel removes. Two electric pumps (16 L/s) into the drum alone are capped at 13.3 L/s until the IBC arrives (Economy ladder: second pump before the IBC).

## Edge Cases

- **If the player pulls the lever with an empty pad pool** (or less than 1 mL): rejected (`nothing_to_ship`); the lever clunks and the console reads "NO CARGO". A residue under 1 mL stays in the pool.
- **If the player pulls the lever while the rocket is away**: rejected (`pad_busy`); the console shows the seconds until the next rocket.
- **If the player pulls the lever from too far away** (more than 3 m from the eye point): rejected (`too_far`), checked before `pad_busy`.
- **If the lever is pulled twice in one tick**: the second request is rejected (`pad_busy`); one launch, one `Take`, one payout.
- **If a pump keeps filling during the countdown**: the rocket only carries what was loaded at the lever pull; new water waits for the next rocket.
- **If the pad tank is swapped during a cycle** (storage upgrade): allowed; Shipping reads the storage spot on the next request, so the new pad pool is launched next time. If `SwapStorage` and `Launch` apply in the same step 1, they apply in arrival order: a launch after the swap takes the new tank's contents, which are the same litres (the swap moves all of them).
- **If the save happens during the countdown or while Away**: rule 10 (the timer resumes on load as remaining seconds).
- **If the game is paused during the countdown**: the timer pauses (it runs on ticks).
- **If the pad pool holds more than cargo capacity** (not possible in the MVP): the rest stays for the next rocket.

## Dependencies

**Upstream:** Hose/Storage Network (Hard, `Take`, pool contents), Economy (Hard, cargo and timer values), Interaction (Hard, lever), Game State (Hard, ticks, Placement), number-representation ADR (total shipped type).
**Downstream:** Economy (Hard, `CreditLaunch` at liftoff), Corporate Radio (Hard, one line per launch), HUD, Audio, Play Log (Soft), Contracts (Alpha), Save & Load (Hard).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Launch cycle | 15 s | 8–25 s | Caps the jerrycan stage; above 20 s the 10 L stage drags. Value in the Economy table |
| Countdown | 3 s | 2–5 s | Part of the cycle. Value in the Economy table |
| Cargo capacity | 1,500 L | 1,200–5,000 L (≥ largest pool) | Value in the Economy table |
| Minimum launch | 0.001 L | 0.0001–0.01 L | Must stay below one spoon scoop (0.015 L) |

## Visual/Audio Requirements

- A stubby, company-branded cargo rocket, absurdly large next to a jerrycan. A countdown on the console's split-flap display, a roar, a smoke plume, and a payslip printing from the console.
- The replacement rocket descends on retro thrusters and lands with a thump.
- Placeholder launch sounds are owned here in the MVP.

## Game Feel

- Lever to roar: the lever clunk plays on the press (same frame) every time. The split-flap countdown also starts in the same frame, but only when the read-only state shows Ready, the pad pool holds at least 1 mL and the player is in range; otherwise the console waits for the tick and shows "NO CARGO" or "NEXT ROCKET" from the rejection. A countdown is never shown and then taken back in single player. Liftoff follows 3 s later.
- The payout counts up as the rocket clears the pad, so credits and spectacle land together.

## UI Requirements

- Console: "READY" / countdown / "NEXT ROCKET 9 s" / "NO CARGO", plus litres in the pad pool. "NEXT ROCKET" shows `ceil(remaining ticks × dt)` seconds (display only).
- Lever hover prompt (text owned by FPC): "Launch: pays N cr", where N is `floor` of Economy's pure payout function for `min(pad pool, cargo_capacity)`. This is the cue that a spoon's worth of water is worth launching.
- Payslip pop-up in world space at liftoff: litres × rate = credits, plus the satirical levy line (Economy).

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 3, 7, 11 | `design/gdd/hose-storage-and-flow-network.md` | `Take`, pools, conservation (Rule 11 and AC-09 must include litres aboard) |
| Rules 4, 12, Interactions | `design/gdd/economy-progression-and-shop.md` | Payout, Earth counter, `launch_cycle`, countdown and cargo values (tuning table) |
| Rules 1, 2, 4–6, 10 | `design/gdd/game-state-and-simulation.md` | Requests, tick convention (Rule 3), timer conversion (Rule 8), Placement and eye point (Rule 12), atomic save |
| Rule 2, UI | `design/gdd/first-person-controller-and-interaction.md` | Lever range (3 m), binding and rejection text |
| Rule 12 | Number-representation ADR (pending) | Exact total shipped |
| Rule 4 | `design/gdd/corporate-radio.md` | One line per launch |

## Acceptance Criteria

Test doubles used below: **FakeNetwork** (a pad pool with settable contents; records `Take` calls), **FakePlacement** (which tank is on the storage spot; issuer eye point), **FakeTuningTable** (credits_per_litre 10, countdown 3 s, launch_cycle 15 s, cargo 1,500 L unless stated), **SpyEconomy** (records `CreditLaunch` calls), **SpyEventBus** (records events with their tick), **FakePump** (adds a fixed amount to the pad pool at step 2b). dt = 0.1 s unless stated; volume tolerance 1e-9 L. The player's eye point is 1 m from the lever unless stated. A launch is "requested at N" when it arrives at step 1 of tick N.

- **AC-01 [EditMode]** GIVEN a FakeNetwork pad pool with 200 L and state Ready, WHEN a launch is requested at N, THEN at tick N: one `Take(200)`, the pool holds 0, litres aboard = 200 and one `LaunchStarted(200)` stamped N; through tick N+29: no `LaunchCompleted`, no `CreditLaunch` and total shipped unchanged; at step 2c of tick N+30: exactly one `LaunchCompleted(200)`, one `CreditLaunch(200)`, total shipped +200, litres aboard 0 and state Away.
- **AC-02 [EditMode]** GIVEN state Ready and a pad pool of 0 L, WHEN a launch is requested, THEN `RequestRejected(nothing_to_ship)`, no `LaunchStarted`, no `Take`, and every Shipping slice field equals its value before the request. GIVEN 0.0009 L, THEN the same, and 0.0009 L stays in the pool. GIVEN 0.001 L, THEN the launch is accepted.
- **AC-03 [EditMode]** GIVEN a launch accepted at N, WHEN another launch is requested at N+150, THEN `RequestRejected(pad_busy)`, and the state becomes Ready at step 2c of tick N+150; WHEN a launch is requested at N+151, THEN it is accepted.
- **AC-04 [EditMode]** GIVEN a 200 L pad pool, state Ready and the real `Economy.CreditLaunch` with FakeTuningTable at 10 cr/L, WHEN two launch requests arrive in tick N, THEN exactly one `LaunchStarted`, one `RequestRejected(pad_busy)` and one `Take`; and at tick N+30 exactly one `LaunchCompleted(200)`, with credits up by exactly 2,000,000 mc (2,000 cr).
- **AC-05 [EditMode]** GIVEN a FakeNetwork pad pool of 100 L and a FakePump adding 0.8 L at step 2b of every tick, WHEN a launch is requested at N, THEN `LaunchCompleted(100)` is raised at tick N+30 and, after step 2c of tick N+30, the pool holds 24.8 L (31 pump steps, ticks N to N+30).
- **AC-06 [EditMode]** GIVEN a save taken at the end of tick S during the countdown with 200 L aboard and 10 ticks left, THEN the saved timer is 1.0 s; WHEN it loads, THEN 200 L is aboard, liftoff happens at step 2c of tick S+10 and pays exactly once, and the conservation sum (sources + tools + tanks + litres aboard + total shipped) is unchanged.
- **AC-07 [EditMode]** GIVEN a launch at N and a save taken at the end of tick N+80 (Away, 70 ticks left), THEN the saved timer is 7.0 s; WHEN it loads, THEN state is Away, a request at N+150 is rejected with `pad_busy`, the state is Ready after step 2c of N+150, and a request at N+151 is accepted.
- **AC-08 [EditMode]** GIVEN FakeTuningTable cargo 500 L and a pad pool of 1,200 L, WHEN a launch is requested, THEN `Take` is called with 500, 700 L stays in the pool, and at liftoff `LaunchCompleted(500)` and `CreditLaunch(500)`.
- **AC-09 [EditMode]** GIVEN 1 L launched at N, THEN total shipped is unchanged through tick N+29, rises by exactly 1 L at tick N+30, and the Earth counter falls by exactly 1 L at the same tick.
- **AC-10 [EditMode]** GIVEN FakePlacement with tank A (50 L) on the storage spot and a launch at N, WHEN tank B (80 L) replaces A on the storage spot during Away and a launch is requested at N+151, THEN `Take` is called on B's pool with 80 and A is not touched.
- **AC-11 [EditMode]** GIVEN state Away and the issuer's eye point 3.5 m from the lever, WHEN a launch is requested, THEN `RequestRejected(too_far)` (not `pad_busy`); GIVEN state Ready, a 10 L pool and the eye point 2.9 m away, THEN it is accepted.
- **AC-12 [EditMode]** GIVEN dt = 0.05 (20 Hz), WHEN a launch is accepted, THEN `countdown_ticks` = 60 and `turnaround_ticks` = 240 (Ready at 2c of N+300); GIVEN dt = 0.1 and countdown 2.04 s, THEN `countdown_ticks` = 20 (rounded, not 21).
- **AC-13 [EditMode]** GIVEN a stub ledger that sums sources + tools + tanks + litres aboard + total shipped, WHEN a 200 L launch runs from tick N to N+30, THEN the sum at the end of every tick equals its starting value within relative 1e-9.
- **AC-14 [PlayMode]** GIVEN a fake GameState in Ready with a 10 L pad pool and the player in range, WHEN the lever is pulled, THEN the clunk and the countdown display start in the same frame. GIVEN an empty pad pool, WHEN the lever is pulled, THEN the clunk plays in the same frame, no countdown digit is ever shown, and "NO CARGO" appears within 0.2 s.
- **AC-15 [Manual]** GIVEN the first launch of a session, THEN a playtester laughs or comments on the size mismatch (satire check, MVP test b).

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02; revised after the 2026-11-02 batch design review: tick boundaries, conservation with litres aboard, cargo range, exact ACs).** Pillar 4: a full rocket for a few litres is the satire in its purest form. Pillar 1: the same rocket carries 0.6 L at the start and 1,200 L at the end. Hands-on: the lever is a physical act; auto-launch will be built, not bought.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Hand-built auto-launch machine (float switch + timer) | systems-designer | Alpha |
| Should the player physically carry the container into the rocket in the hand stage (more work) instead of the built-in feed? | game-designer | First MVP playtest |
| Total shipped number type (integer microlitres proposed) | technical-director | Number-representation ADR |
