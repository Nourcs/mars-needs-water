# Pumps & Machines (hand pump, electric motor)

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 1 (Start Tiny, End Enormous); Pillar 2 (Hands-On Craft)

## Summary

Pumps & Machines covers the hand pump the player cranks, the electric motor that is bolted onto it, and the second electric pump. This system decides whether each pump is running and at what rate; the Hose/Storage Network decides how much water actually moves. Pumps never break or fail: a full tank stalls them quietly and they restart on their own.

> **Quick reference** — Layer: `Core gameplay` · Priority: `MVP` · Key deps: `Hose, Storage & Flow Network`, `Economy`, `Game State`

## Overview

Pumps are the first machines. Each is a placed object with an intake socket (suction hose into a source) and an outlet socket (discharge hose to a tank). The **hand pump** runs only while the player holds Use on its handle. The **electric motor** is a kit bolted onto the hand pump: once fitted and plugged into the power post at the launch pad with a cable, the pump is switched on or off with a lever and runs by itself. The **second electric pump** is a complete powered pump with its own intake and outlet. Each tick, at tick step 2a (just before the network at 2b), this system sets each pump's state and rate; the network's flow solve at step 2 moves the water.

## Player Fantasy

**"From sweat to switch."** The hand pump is the first time water moves without you carrying it, but your arm is still the motor: you stand at the handle, cranking, watching the hose pulse. The electric motor is the moment you let go: you bolt it on, run the cable, throw the lever, and the pump keeps going while you walk away. The second pump doubles the roar.

- **Anchor moment:** the first lever pull on the electric motor: a cough, a rising hum, the hose stiffening as water rushes through, and you step back with your hands free.
- **Tone:** mechanical, greasy, satisfying (Pillar 2). Bolts, levers, cables, gauges; no glowing UI.

## Detailed Design

### Core Rules

1. **Placing a pump:** the player carries the pump from its crate and sets it down on solid ground within 6 m of a source (suction hose length). Then they lay the suction hose into the source and the discharge hose to a tank inlet (Hose/Storage Network rules). A pump is **Ready** when its intake is in a source with water and its outlet is connected to a pool.
2. **Hand pump:** Running only while the player holds Use on the handle and stays within 1.5 m. Rate while Running: 1.25 L/s (Economy table), shown as a stroke every 0.8 s moving 1 L each. Releasing Use, walking away or opening a menu stops it at the end of the current tick.
3. **Electric motor (upgrade kit):** bolted onto an existing hand pump (one interaction: place the kit on the pump, hold Use for 2 s to bolt). The pump becomes electric: the handle is removed (kept as a prop) and the rate becomes 8 L/s. It needs power: the player carries the cable reel from the crate and plugs one end into the motor and the other into the **power post** at the launch pad (cable length 30 m). A connect beyond 30 m is rejected (`too_far`).
4. **Electric switch:** a lever on the motor sets it On or Off. When On, powered and Ready, the pump is Running at 8 L/s without the player present.
5. **Second electric pump:** a complete powered pump (rules 1, 3, 4) with its own suction hose, discharge hose and cable. The power post has two sockets in the MVP.
6. **No failure states:** pumps never break, overheat or need fuel in the MVP. A pump into a full pool is **Stalled**: still On, moving nothing, with a strained-idle sound. It restarts by itself when space opens. A pump whose source is drained is **Dry**: a sputtering sound; it restarts if the intake is moved to another source.
7. **Pumps decide state, the network decides flow.** This system sends `(Running, rate)` per pump to the network each tick. It reads back the litres actually moved, only for sound, gauges and the play log.
8. **Moving a pump:** the player can unplug and pick up any pump (an electric pump must be switched Off first; otherwise the pickup is rejected with `switch_off_first`). Hoses and cables stay plugged at their other ends.

### States and Transitions

| State | Condition | Can go to |
|---|---|---|
| Crated | In its crate | Placed |
| Placed | On the ground; not Ready (missing hose, cable or water) | Ready |
| Ready | Intake in water, outlet to a pool, powered if electric; not running | Running; Placed (unplugged) |
| Running | Hand: Use held at handle. Electric: On and Ready | Ready (released or Off); Stalled; Dry |
| Stalled | Running but its pool is full | Running (space opens); Ready |
| Dry | Running but its source is drained | Ready (Off or released); Running (intake moved to water) |

Only Placed position, connections, the electric On/Off lever and whether the motor is fitted are saved; the rest is derived.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Pumps slice (fitted motor, lever, position) | Tick: pump step before the network |
| Hose/Storage Network | `(Running, rate)` per pump | Ready check (intake, outlet), litres moved, pool full |
| Water Source Model | — | Drained state (for Dry) |
| Economy | — | Pump rates, cable length; `Purchased` |
| Interaction | — | Hold Use on handle, lever, bolt, plug |
| Audio, HUD, Play Log | State changes; flow per pump | — |

## Formulas

The **pump_demand** formula is defined as:

`rate_p = (state == Running) ? rate_table(p) : 0` (the network then applies `min(rate_p, hoseCap) × dt`)

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| rate_table(p) | double (L/s) | 0.5–20 | data file (Economy) | Hand pump 1.25; electric 8 |

**Output Range:** 0 or the table rate.

The **hand_pump_effective** formula is defined as:

`rate_eff = 1.25 × duty` (duty = share of time the player actually cranks; estimate 0.7)

**Output Range:** about 0.875 L/s for a typical player; matches the Economy pacing model.

## Edge Cases

- **If the player holds Use on the handle but the pump isn't Ready**: the handle moves (feedback) but no water flows; a prompt names what is missing ("No hose to tank").
- **If the pool fills while cranking**: Stalled; the handle stiffens (animation) and the strained sound plays; no water moves.
- **If the electric pump is On and the player leaves the area or saves**: it keeps running (the simulation is global); on load it resumes from the saved lever state.
- **If the cable is unplugged while Running**: the pump drops to Ready at the next tick, with a power-down whine.
- **If the motor kit is bought before a hand pump is placed**: impossible in the MVP (linear ladder requires the hand pump); if it ever happens through data, the kit waits in its crate.
- **If the player tries to bolt the motor onto the second electric pump**: rejected (`already_powered`).
- **If both electric pumps feed one pool and it fills**: both Stall; both resume when a launch empties the pool.
- **If both pumps share one source and it drains**: both go Dry in the same tick.
- **If the player tries to pick up a running electric pump**: rejected (`switch_off_first`).

## Dependencies

**Upstream:** Hose/Storage Network (Hard), Economy (Hard), Game State (Hard), Interaction (Hard), Water Source Model (Soft, drained state).
**Downstream:** Audio (Soft), HUD (Soft), Shipping & Launch (Soft: launching pauses nothing here, but pools empty), Save & Load (Hard), Play Log (Soft).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Hand pump rate | 1.25 L/s | 0.6–1.5 L/s | Economy table; real hand pumps |
| Stroke period | 0.8 s | 0.5–1.2 s | Presentation; litres per stroke = rate × period |
| Handle range | 1.5 m | 1–2 m | — |
| Electric rate | 8 L/s | 4–10 L/s | Economy table |
| Bolt-on time | 2 s | 1–4 s | — |
| Power cable length | 30 m | 20–50 m | Level must keep pump spots within reach of the post |
| Power post sockets | 2 | 2–4 | MVP needs 2 |

Rates, cable length and socket count are stored in the Economy table (Economy owns all designed numbers).

## Visual/Audio Requirements

- Hand pump: cast-iron body, long orange-gripped handle, a visible stroke with a gulp of water at the outlet per stroke.
- Electric motor: a squat green motor with an orange lever, a thick cable reel; hum rises with load; strained idle when Stalled; sputter when Dry.
- Power post: a yellow-and-black company post at the launch pad with two sockets and a little "OWNED BY THE COMPANY" plate.

## Game Feel

- Cranking: each stroke must feel weighty (camera bob, resistance animation) but never require button mashing; hold to crank.
- Lever: a satisfying throw with a half-second spin-up before water arrives.

## UI Requirements

- Prompts: "Hold to crank", "Bolt on motor", "Plug in", "Switch on/off", and the missing-part reason when not Ready.
- No pump HUD in the MVP; state reads from sound and the hose pulse. Flow gauges come with the Logistics Overlay (Alpha).

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 1, 6, 7 | `design/gdd/hose-storage-and-flow-network.md` | Ready check, flow solve, full pools |
| Rule 6 | `design/gdd/water-source-model.md` | Drained sources |
| Values | `design/gdd/economy-progression-and-shop.md` | Rates, prices, ladder |
| Tick | `design/gdd/game-state-and-simulation.md` | Pumps step 2a, before the network (2b) |

## Acceptance Criteria

- **AC-01 [EditMode]** GIVEN a Ready hand pump with Use held, WHEN 10 ticks run, THEN the network receives rate 1.25 each tick and 1.25 L moves.
- **AC-02 [EditMode]** GIVEN a Ready hand pump, WHEN Use is released, THEN from the next tick its rate is 0.
- **AC-03 [EditMode]** GIVEN a hand pump without an outlet hose and Use held, THEN no water moves and the prompt reason is "No hose to tank".
- **AC-04 [EditMode]** GIVEN the motor kit bolted on, a cable to the post and the lever On, WHEN 10 ticks run with the player 50 m away, THEN 8 L moves.
- **AC-05 [EditMode]** GIVEN an electric pump Running, WHEN its cable is unplugged, THEN it is Ready at the next tick and moves 0.
- **AC-06 [EditMode]** GIVEN an electric pump into a pool that becomes full, THEN it is Stalled, and after `Take` frees space it is Running again with no player action.
- **AC-07 [EditMode]** GIVEN an electric pump whose source drains, THEN it is Dry in the same tick the source drains.
- **AC-08 [EditMode]** GIVEN a cable connect 31 m away, THEN `RequestRejected(too_far)`.
- **AC-09 [EditMode]** GIVEN a running electric pump, WHEN pickup is requested, THEN `RequestRejected(switch_off_first)`.
- **AC-10 [EditMode]** GIVEN a save with one fitted motor (lever On) and one second pump (lever Off), WHEN loaded, THEN states and positions match and the first pump resumes Running.
- **AC-11 [PlayMode]** GIVEN the lever is thrown On with the pump Ready, THEN the spin-up sound plays in the same frame and the tank level rises within 1 s.
- **AC-12 [Manual]** GIVEN a typical player at the hand-pump stage, WHEN their cranking time is read from the play log over 3 minutes, THEN duty is measured and fed back into the Economy pacing check.
- **AC-13 [Manual]** GIVEN the first electric switch-on, THEN the player walks away from the pump within 30 s (the "hands free" moment reads).

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02).** Pillar 1: from cranking to switching is a clear step up. Pillar 2: every machine is placed, plumbed, bolted and wired by hand. No failure states, matching the systems index. Watch item: the hand pump uses hold-to-crank rather than rhythm input; if it feels passive in playtest, consider a timed stroke.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Hold-to-crank vs rhythmic strokes for the hand pump | game-designer | First MVP playtest |
| Where electricity comes from at later sites (generators, fuel as a chore) | systems-designer | Regions / Alpha |
| Should the second pump be bought as a motor kit for a second hand pump instead (cheaper art)? | producer | Before MVP art |
