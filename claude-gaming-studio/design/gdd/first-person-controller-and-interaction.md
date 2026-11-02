# First-Person Controller & Interaction

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 2 (Hands-On Craft)

## Summary

The first-person controller moves the player around the pond and pad, and the interaction layer turns "look at a thing and press Use" into requests (scoop, pour, plug a hose, crank, pull a lever, open a crate). It never changes game state itself: it only sends requests and shows prompts, which keeps the door open for co-op.

> **Quick reference** — Layer: `Core` · Priority: `MVP` · Key deps: `Game State`

## Overview

Two parts. The **controller**: walk, sprint, crouch, jump, look, carry, on keyboard/mouse and gamepad, with a carry-weight speed modifier. The **interaction layer**: a short raycast from the camera finds the interactable under the crosshair, shows its prompt, and on input sends the matching request (with the target's stable id) to Game State. Carrying uses a single "hands" slot: a tool, a hose end, a crate or a pump.

## Player Fantasy

**"My hands do the work."** Every action is something you aim at and do: you see the bucket in front of you, the coupling click, the lever go down. Nothing happens from a menu (Pillar 2).

## Detailed Design

### Core Rules

1. **Movement (defaults):** walk 4 m/s, sprint 6.5 m/s, crouch 2 m/s, jump 1 m. Mouse look and gamepad look with sensitivity settings. Character controller (not a rigidbody), with slopes up to 40°, so the 1:8 beach lips and 1:25 shelf are walkable when exposed. Mud slows walking to 85%.
2. **Hands slot:** the player carries one thing: a hand tool, a hose end, a cable end, a crate (both hands, 70% speed), or a pump (both hands, 60% speed). A full bucket slows to 75% (Hand Tools). Drop with G; pick up with Use.
3. **Interaction raycast:** from the camera, range 2 m (3 m for levers and the terminal), against an Interactable layer. The nearest hit gives a prompt; only the prompt for the current hands item is shown (e.g. holding a hose end at a socket shows "Plug in").
4. **Inputs:** Use (E / right trigger) for the main action; Hold Use for crank and bolt-on; G to drop; 1–3 / bumpers to switch hand tools; Esc / Start to pause.
5. **Requests only:** each action sends a request carrying the issuer's `PlayerId` (0 in the MVP) and stable ids (`Scoop(tool, source)`, `Pour(tool, container)`, `Connect(end, socket)`, `Crank(pump, held)`, `Launch()`, `Order(item)`, `OpenCrate(crate)` (sent when a 1 s hold-to-pry completes), `SwapStorage(tank)`, `FitInlet(tank)`, `PlaceItem(item, pose)`). The result arrives via state and `RequestRejected`; rejection plays a dull thunk and shows the reason as the prompt for 1.5 s.
6. **Instant feedback:** the animation and sound for an action start on the press, before the tick (Game State latency rule). If the request is rejected, the animation is cut short.
7. **Placing items:** pumps, tanks and crates are placed with a ghost preview on valid ground (green outline = valid, red = blocked, with text), rotated with the scroll wheel or bumpers.
8. **Physics props:** crates and litter are physics props; hoses are visual splines that can't snag or trip the player.
9. **Pause:** opening the pause menu stops the simulation (Game State Paused).

### States and Transitions

Hands: **Empty ↔ Holding(item)**. Controller: **Walking / Sprinting / Crouching / Airborne / UsingHeld** (crank, bolt: movement locked while held).

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Requests | `RequestRejected`; state for prompts |
| Hand Tools, Network, Pumps, Shipping, Economy | Requests on their objects | Prompt text and validity |
| HUD | Crosshair prompt | — |
| Save & Load | Player position, facing, hands item | Load |
| Settings (later) | — | Sensitivity, invert, bindings |

## Formulas

The **move_speed** formula is defined as:

`speed = base(mode) × carry_mult × ground_mult`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| base(mode) | float (m/s) | 2–6.5 | data file | Walk 4, sprint 6.5, crouch 2 |
| carry_mult | float | 0.6–1 | data file | Full bucket 0.75, crate 0.7, pump 0.6 |
| ground_mult | float | 0.8–1 | data file | Mud 0.85 |

**Output Range:** about 1.0–6.5 m/s.

## Edge Cases

- **If two interactables overlap under the crosshair**: the nearest wins; ties go to the one matching the hands item.
- **If the player walks out of range while holding Use on a crank**: the crank stops (Pumps rule 2).
- **If the player drops a hose end in the pond**: it floats at the surface and can be picked up; if it is a suction hose, it counts as the intake.
- **If a crate falls into the pond**: it floats to the nearest shore point within 2 s (never lost).
- **If the player falls into the pond**: it's 1 m deep; they wade at 60% speed. No drowning.
- **If the player tries to place an item on a hose or in water**: the ghost goes red with "Blocked".
- **If input is pressed during a pause**: ignored.

## Dependencies

**Upstream:** Game State (Hard). **Downstream:** every interactive system (Hard), HUD (Soft), Save & Load (Hard), Onboarding (VS), Co-op (later: requests become network commands).

## Tuning Knobs

| Knob | Default | Safe range |
|---|---|---|
| Walk / sprint / crouch | 4 / 6.5 / 2 m/s | 3–5 / 5–8 / 1.5–3 |
| Interact range | 2 m (3 m levers) | 1.5–3 m |
| Mouse sensitivity | 1.0 | 0.1–5 |
| Carry multipliers | bucket 0.75, crate 0.7, pump 0.6 | 0.5–0.9 |
| Mud multiplier | 0.85 | 0.7–1 |

## Visual/Audio Requirements

- Viewmodel hands holding the current item; heavy items show a lean and slight camera bob.
- Footsteps vary by surface (grass, gravel pad, wet mud, shallow water). Placeholder in the MVP.

## Game Feel

- Input to animation: same frame. Movement should feel grounded, a little heavy, never floaty.
- Head bob subtle and toggleable.

## UI Requirements

- Crosshair dot with a context prompt below it; held amount when holding a tool (Hand Tools).

📌 **UX Flag — First-Person Controller & Interaction:** prompts and bindings. Cover in `/ux-design`.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 5, 6 | `design/gdd/game-state-and-simulation.md` | Requests, latency rule |
| Rule 2 | `design/gdd/hand-tools.md`, `design/gdd/pumps-and-machines.md` | Carry speeds, crank |
| Rule 1 | `design/gdd/drawdown-visuals-and-reveals.md` | Walkable shelf and beach lips |

## Acceptance Criteria

- **AC-01 [EditMode]** GIVEN a full bucket on mud, THEN walk speed = 4 × 0.75 × 0.85 = 2.55 m/s.
- **AC-02 [EditMode]** GIVEN any interaction, THEN the interaction layer sends a request and writes no state (static check: no writes to state slices from interaction code).
- **AC-03 [PlayMode]** GIVEN the player looks at a socket 1.8 m away holding a hose end, THEN the prompt "Plug in" shows; at 2.2 m it doesn't.
- **AC-04 [PlayMode]** GIVEN a rejected request, THEN the reason shows as the prompt for 1.5 s and a thunk plays.
- **AC-05 [PlayMode]** GIVEN a crate dropped in the pond, THEN within 2 s it rests on the shore.
- **AC-06 [PlayMode]** GIVEN the 1:25 shelf exposed, THEN the player can walk down it without sliding.
- **AC-07 [Manual]** GIVEN keyboard/mouse and a gamepad, THEN every MVP action is doable with each.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02).** Pillar 2: every action is aimed and physical; no menus for building. Co-op seam respected (requests only).

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Hold-to-repeat and toggle options for accessibility (crank, scoop) | ux-designer | `/ux-design` |
| Should hoses be physical (can be stepped on and kinked)? MVP: no | game-designer | Vertical Slice |
