# Shipping & Launch

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 1 (Start Tiny, End Enormous); Pillar 4 (Bleak Corporate Satire)

## Summary

Shipping & Launch is the end of the pipeline: the player pulls the LAUNCH lever on the pad, the rocket loads everything in the storage on the pad, lifts off, and the company pays credits for every litre (`LaunchCompleted`). A new rocket lands 15 s later. Launching is the only way to earn, so it is the heartbeat of the loop.

> **Quick reference** — Layer: `Core gameplay` · Priority: `MVP` · Key deps: `Hose, Storage & Flow Network`, `Economy`, `Interaction`

## Overview

The launch pad holds one small cargo rocket, a **storage spot** where the active container sits (jerrycan, barrel, then the 200 L drum, with the IBC linked to it), and a console with a big LAUNCH lever. Pulling the lever submits a launch request. The rocket takes all the water in the storage spot's pool (up to its cargo capacity) through the pad's built-in feed, counts down 3 s, lifts off, Shipping credits Economy directly and raises `LaunchCompleted(litres)` for the radio, HUD and log. The pad then stays busy until a replacement rocket lands. Pumps keep filling the pool during the turnaround. Automatic launching is post-MVP and will be built by hand (a feed line and timer), never bought.

## Player Fantasy

**"Send it."** Every launch is a small celebration of a small amount: a whole rocket for 10 litres of pond water, a countdown, a roar, a payslip. Early on you launch after every bucket, so the rocket becomes your rhythm. Later you launch 1,200 litres at a time, and the same rocket feels like it's working for you.

- **Anchor moment:** the first launch: you pull the lever on a jerrycan holding 0.8 L of spoon water, the rocket roars into the sky, and the radio thanks you warmly for your 8 credits, exactly the price of a bucket.
- **Tone:** absurd scale mismatch played straight (Pillar 4): a full rocket launch for a puddle's worth of water.

## Detailed Design

### Core Rules

1. **The pad tank:** the tank on the storage spot is the *pad tank*. Its pool (the pad tank plus any tanks linked to it) is the *pad pool*. Only the pad pool can be launched.
2. **Launch request:** pulling the LAUNCH lever submits `Launch(issuer)`. It is validated at tick step 1: rejected with `pad_busy` if a rocket is not on the pad, or `nothing_to_ship` if the pad pool is empty.
3. **Loading:** on a valid launch, Shipping calls `Take(pad pool, min(contents, cargo_capacity))` (Hose/Storage Network) and holds those litres in the rocket. Loading is instant (the pad feed is built in).
4. **Countdown and liftoff:** on a valid launch Shipping raises `LaunchStarted(litres)`. 3 s countdown (30 ticks; timers are whole ticks), then liftoff at step 2c: Shipping adds the litres to the total shipped, calls `Economy.CreditLaunch(litres)` directly in the same step, and raises `LaunchCompleted(litres)` as a notification. Payout and shipped total change in the same tick, so a save can never hold one without the other.
5. **Turnaround:** after liftoff the pad is empty until a replacement rocket lands. The whole cycle from lever to next rocket is `launch_cycle` (15 s at default: 3 s countdown + 12 s until the new rocket lands). During the cycle, pours and pumping into the pad pool continue normally.
6. **Cargo capacity:** 1,500 L in the MVP, more than the largest MVP pool (1,200 L), so in practice a launch always empties the pad pool.
7. **No cost, no minimum:** a launch costs nothing and any amount can go. The cycle time is the only limit on spamming.
8. **The lever is the only trigger** in the MVP. Auto-launch (Alpha) will be a machine the player builds and plumbs, consistent with "no menu buttons".
9. **Saving:** the countdown and the litres aboard are saved as they are (in the Shipping slice), and the countdown resumes on load. Litres aboard count in every water-conservation check. No water or credits are ever lost or doubled.

### States and Transitions

| State | Condition | Can go to |
|---|---|---|
| Ready | Rocket on pad | Countdown (valid launch) |
| Countdown | Loaded, 3 s timer | Away (liftoff; `LaunchCompleted` raised) |
| Away | No rocket on pad | Ready (after the rest of `launch_cycle`) |

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Shipping slice (pad state, timer, litres aboard, total shipped); `LaunchCompleted` | Tick step 1 requests; step 2c (timers, liftoff) |
| Hose/Storage Network | `Take` calls | Litres taken; pad pool contents |
| Economy | `CreditLaunch(litres)` call at liftoff; total shipped (Earth counter) | Cargo capacity, launch cycle (tuning table) |
| Interaction | — | LAUNCH lever |
| Corporate Radio, HUD, Audio, Play Log | `LaunchCompleted`, countdown start | — |
| Save & Load | The slice | Load |

## Formulas

The **launch_load** formula is defined as:

`litres = min(pad_pool_contents, cargo_capacity)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| pad_pool_contents | double (L) | 0–1,200 (MVP) | Hose/Storage Network | Water in the pad pool |
| cargo_capacity | double (L) | 500–5,000 | data file (Economy) | Rocket cargo (MVP 1,500) |

**Output Range:** 0 to cargo_capacity; 0 is rejected as `nothing_to_ship`.

The **max_ship_rate** formula is defined as:

`max_ship_rate = min(cargo_capacity, pool_capacity) / launch_cycle`

**Output Range:** the most litres per second shipping can carry. MVP: 10 L jerrycan → 0.67 L/s; 200 L drum → 13.3 L/s; drum + IBC → 80 L/s. At the puddle (5 s bucket trips) the 10 L jerrycan's cap binds and the player waits for the next rocket, which is what the 100 L barrel removes. Two electric pumps (16 L/s) into the drum alone are capped at 13.3 L/s until the IBC arrives (Economy ladder: second pump before the IBC).

## Edge Cases

- **If the player pulls the lever with an empty pad pool**: rejected (`nothing_to_ship`); the lever clunks and the console reads "NO CARGO".
- **If the player pulls the lever while the rocket is away**: rejected (`pad_busy`); the console shows the seconds until the next rocket.
- **If the lever is pulled twice in one tick**: the second request is rejected (`pad_busy`); one launch, one payout.
- **If a pump keeps filling during the countdown**: the rocket only carries what was loaded at the lever pull; new water waits for the next rocket.
- **If the pad tank is swapped during a cycle** (storage upgrade): allowed; the new pad pool is launched next time.
- **If the save happens during the countdown**: rule 9 (the countdown resumes on load).
- **If the game is paused during the countdown**: the timer pauses (it runs on ticks).
- **If the pad pool holds more than cargo capacity** (not possible in the MVP): the rest stays for the next rocket.

## Dependencies

**Upstream:** Hose/Storage Network (Hard, `Take`), Economy (Hard, cargo and cycle values), Interaction (Hard, lever), Game State (Hard).
**Downstream:** Economy (Hard, `CreditLaunch` at liftoff), Corporate Radio (Hard, one line per launch), HUD, Audio, Play Log (Soft), Contracts (Alpha), Save & Load (Hard).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Launch cycle | 15 s | 8–25 s | Caps the jerrycan stage; above 20 s the 10 L stage drags |
| Countdown | 3 s | 2–5 s | Part of the cycle |
| Cargo capacity | 1,500 L | ≥ largest pool | Stored in the Economy table |

## Visual/Audio Requirements

- A stubby, company-branded cargo rocket, absurdly large next to a jerrycan. A countdown on the console's split-flap display, a roar, a smoke plume, and a payslip printing from the console.
- The replacement rocket descends on retro thrusters and lands with a thump.
- Placeholder launch sounds are owned here in the MVP.

## Game Feel

- Lever to roar: the countdown starts on the press (same frame), liftoff 3 s later.
- The payout counts up as the rocket clears the pad, so credits and spectacle land together.

## UI Requirements

- Console: "READY" / countdown / "NEXT ROCKET 9 s" / "NO CARGO", plus litres in the pad pool.
- Payslip pop-up in world space at liftoff: litres × rate = credits, plus the satirical levy line (Economy).

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 3, 6 | `design/gdd/hose-storage-and-flow-network.md` | `Take`, pools |
| Rule 4 | `design/gdd/economy-progression-and-shop.md` | Payout, Earth counter, launch values |
| Rule 2, 9 | `design/gdd/game-state-and-simulation.md` | Requests, atomic save |
| Rule 4 | `design/gdd/corporate-radio.md` | One line per launch |

## Acceptance Criteria

- **AC-01 [EditMode]** GIVEN a pad pool with 200 L and a ready rocket, WHEN a launch applies, THEN the pool is empty, and after 3 s of ticks `LaunchCompleted(200)` is raised once.
- **AC-02 [EditMode]** GIVEN an empty pad pool, WHEN a launch is requested, THEN `RequestRejected(nothing_to_ship)`.
- **AC-03 [EditMode]** GIVEN a launch at t = 0, WHEN another launch is requested at t = 10 s, THEN `RequestRejected(pad_busy)`; at t = 15 s it is accepted.
- **AC-04 [EditMode]** GIVEN two launch requests in one tick, THEN exactly one `LaunchCompleted` follows.
- **AC-05 [EditMode]** GIVEN an electric pump filling the pad pool during the countdown, THEN `LaunchCompleted` carries only the litres loaded at the lever pull, and the new water remains in the pool.
- **AC-06 [EditMode]** GIVEN a save taken during the countdown with 200 L aboard and 10 ticks left, WHEN it loads, THEN 200 L is aboard, liftoff happens 10 ticks later and pays once, and total water (including litres aboard) is unchanged.
- **AC-07 [EditMode]** GIVEN 1 L launched, THEN total shipped rises by exactly 1 and the Earth counter falls by exactly 1.
- **AC-08 [PlayMode]** GIVEN a lever pull, THEN the countdown display starts in the same frame.
- **AC-09 [Manual]** GIVEN the first launch of a session, THEN a playtester laughs or comments on the size mismatch (satire check, MVP test b).

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02).** Pillar 4: a full rocket for a few litres is the satire in its purest form. Pillar 1: the same rocket carries 0.8 L at the start and 1,200 L at the end. Hands-on: the lever is a physical act; auto-launch will be built, not bought.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Hand-built auto-launch machine (float switch + timer) | systems-designer | Alpha |
| Should the player physically carry the container into the rocket in the hand stage (more work) instead of the built-in feed? | game-designer | First MVP playtest |
