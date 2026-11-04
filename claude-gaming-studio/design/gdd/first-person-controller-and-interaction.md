# First-Person Controller & Interaction

> **Status**: Approved (2026-11-04)
> **Author**: Nour + agents
> **Last Updated**: 2026-11-04 (revised after the 2026-11-04 FPC re-review: rejection text by request and code, CrankStop on hands change or menu, Launch payout in whole credits)
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 2 (Hands on the Machinery)

## Summary

The first-person controller moves the player around the pond and pad, and the interaction layer turns "look at a thing and press Use" into requests (scoop, pour, plug a hose, crank, pull a lever, pry a crate). It never changes game state itself: it only sends requests and shows prompts, which keeps the door open for co-op. This doc owns the one input and context table (Rule 4): every action, its request, its owner, tap or hold, its bindings, its range and the player-facing text for every rejection code in the MVP.

> **Quick reference**: Layer `Core` · Priority `MVP` · Key deps `Game State`; every interactive system defines its own requests and codes, this doc binds them to inputs and words.

## Overview

Two parts. The **controller**: walk, sprint, crouch, jump, look and carry, on keyboard/mouse and gamepad, with a carry-weight speed modifier. The **interaction layer**: a short ray from the player's eye point finds the interactable under the crosshair, shows its prompt, and on input sends the matching request (with the issuer and the target's stable id) to Game State. Carrying uses a single hands slot: a tool, or one carried item (hose end, cable end, inlet fitting, motor kit, crate, tank or pump).

## Player Fantasy

**"My hands do the work."** Every action is something you aim at and do: you see the bucket in front of you, the coupling click, the lever go down. Nothing happens from a menu ("Hands on the Machinery").

## Detailed Design

### Core Rules

1. **Movement (defaults):** walk 4 m/s, sprint 6.5 m/s, crouch 2 m/s, jump 1 m. Mouse look and gamepad look with sensitivity settings. Character controller (not a rigidbody), with slopes up to 40°, so the beach lips (sloped from the basin curve, about 1:12) and the intake shelf (about 1:31 to 1:37; Drawdown rule 6, Water Source) are walkable when exposed. Mud slows walking to 85%: walkable mud is the exposed basin bed, that is, ground whose hit collider is a source's basin mesh (Drawdown rule 2) at a point above that source's current water plane (Drawdown rule 1); the controller reads this from its ground hit, so no separate tagging is authored. Below the water plane the ground is water, not mud; wading (water deeper than 0.3 m) slows it to 60%. No sprint and no jump while carrying a crate, tank or pump.
2. **Hands slot:** the player holds either a tool (spoon or bucket) or one carried item. A tool is usable once its crate is Opened and it arrives through `Placement.SetHands` (Economy rule 8); `Purchased` never changes what is in the hands. Carry speeds: full bucket 75% (Hand Tools), hose end or cable end 90%, crate or tank 70%, pump 60%. **Picking something up stows the tool**: it goes back on the belt and keeps its water (held water is per tool, Hand Tools rule 1). While a carried item is in the hands, the tool-switch inputs do nothing. G drops the carried item; a tool is never dropped. A held hose end is clamped to the hose length from the hose's other end (discharge 20 m, suction 6 m from its pump's intake socket, link 5 m; Hose rule 15); this is presentation only and `Connect` still checks `hose_too_short`. During a hold action (crank, pry, bolt, fit) a held tool is lowered out of view and comes back afterwards; this is presentation only and sends no request.
3. **Eye point and range:** the interaction ray starts at the **eye point** = recorded pose + eye height (1.6 m). The simulation measures range from the same point (Game State Rule 12), so a prompt that shows is a request in range. Each target type has its own range in data: **hand-pump handle 1.5 m, levers 3 m (LAUNCH, ORDER, motor switch), everything else 2 m**. The ray hits the Interactable layer; the nearest hit gives the prompt, and a tie (within 0.05 m) goes to the target that matches the hands item. Only the prompt for the current hands item is shown (holding a hose end at a socket shows "Plug in").
4. **One input and context table.** Each input has one meaning per context (hands item × target). This table is the binding contract; the owning system defines each request and its codes, and this doc defines the words.
   - **Use** = E or left mouse / right trigger. **Drop** = G / Y. **Tools** = 1 (spoon), 2 (bucket) or the scroll wheel / LB, RB. **Rotate ghost** = scroll wheel / LB, RB. Scroll and bumpers rotate only while a placement ghost is up; a ghost is only up while a placeable item is in the hands, when tool switching is off anyway (Rule 2), so the two never clash.
   - **Tap or hold:** a tap-action's request is sent on press. On a hold target, a press released before 0.25 s is a tap; past 0.25 s a radial ring appears and fills to the hold duration, counted from the press. Releasing early, looking off the target or moving cancels the hold and sends nothing. A Landed crate has no tap action: it can only be pried.
   - **Rejection text** is shown as the prompt for 1.5 s with a dull thunk (Rule 6). The text is looked up by (request type, code): the controller remembers the type of the last interaction request it sent (one is in flight in the MVP; `PlayerPose` does not count), and when `RequestRejected(issuer, code)` arrives it shows that row's text for the code. If that pair has no entry, it falls back to the code's default text, which is the code's text in the lowest-numbered row that lists it. Five codes have row-specific text: `blocked` (rows 10, 11, 13), `too_far` (row 17), `not_holding` (rows 14, 17, 22), `already_fitted` (rows 20, 22) and `no_motor` (rows 17, 21). Where read-only state already predicts the rejection (pool full, tool full, no cargo, rocket away, short of credits, no inlet, socket in use, pump switched on), the prompt shows that text in grey before the press; a press still sends the request and the simulation decides.

| # | Action | Context (hands → target) | Request (owner) | Tap / hold | KB/M | Gamepad | Range | Rejection codes → player text |
|---|---|---|---|---|---|---|---|---|
| 1 | Move | any | `PlayerPose(issuer, pose)`, once per tick (Game State, Placement) | continuous | WASD | left stick | none | none in the MVP |
| 2 | Look | any | none | continuous | mouse | right stick | none | none |
| 3 | Sprint | not carrying crate, tank or pump | none | hold | Left Shift | left stick click | none | none |
| 4 | Crouch | any | none | hold | C or Left Ctrl | B | none | none |
| 5 | Jump | not carrying crate, tank or pump | none | tap | Space | A | none | none |
| 6 | Pause | any | none (Game State Paused) | tap | Esc | Start | none | none |
| 7 | Switch tool | empty or tool; tool's crate Opened; no ghost | `SelectTool(issuer, tool)` (Hand Tools) | tap | 1, 2, scroll | LB / RB | none | `not_owned` → "You don't own that yet"; `crate_unopened` → "Not unboxed yet: pry the crate"; `hands_full` → "Hands full: drop it first (G)" |
| 8 | Scoop | tool → source | `Scoop(issuer, tool, source)` (Hand Tools) | tap | Use | RT | 2 m | `tool_full` → "Full: go pour it"; `source_dry` → "Dry. Nothing left here"; `too_far` → "Too far: step closer"; `not_in_hand` → "Take it in hand first" |
| 9 | Pour | tool → container | `Pour(issuer, tool, container)` (Hand Tools; pour rule in Hose/Storage Network) | tap | Use | RT | 2 m | `tool_empty` → "Nothing to pour"; `not_in_hand` → "Take it in hand first"; `pool_full` → "Tank full: LAUNCH it"; `cannot_deposit` → "Company water only flows one way"; `not_installed` → "Swap it in first"; `too_far` → "Too far: step closer" |
| 10 | Pick up | empty or tool (tool is stowed) → loose item: hose coil or end, cable end, inlet fitting, motor kit, Opened crate, empty tank (not the tank on the storage spot, which shows no prompt), pump body | `PickUp(issuer, id)` (Game State, Placement) | tap | Use | RT | 2 m | `too_far` → "Too far: step closer"; `blocked` → "Can't pick that up" (pad tank, Hose rule 14; no prompt shows there, so this is for completeness); `hands_full` → "Hands full: drop it first (G)"; `switch_off_first` → "Switch it off first"; `not_empty` → "Empty it first" |
| 11 | Place | placeable item (crate, tank, pump) → ground, ghost up | `PlaceItem(issuer, item, pose)` (Game State, Placement) | tap | Use | RT | 2 m | `too_far` → "Too far: step closer"; `blocked` → "Can't put it there" (the ghost names why first: "On water", "Not level", "Something's in the way", "Off the work area") |
| 12 | Rotate ghost | placeable item, ghost up | none (local ghost yaw, 15° per step) | tap | scroll | LB / RB | none | none |
| 13 | Drop | carried item (not a tool) | `Drop(issuer, item, pose)`, pose 0.8 m ahead on the ground (Game State, Placement) | tap | G | Y | none | `blocked` → "No room to drop it here" |
| 14 | Plug hose | hose end → hose socket | `Connect(issuer, hoseId, endIdx, target)` (Hose/Storage Network) | tap | Use | RT | 2 m; hose reach 20 m socket to socket | `not_holding` → "Pick up the hose end first"; `too_far` → "Too far: step closer"; `wrong_socket` → "Wrong fitting"; `not_installed` → "Swap it in first"; `no_inlet` → "No inlet: screw in the fitting"; `socket_in_use` → "Socket in use"; `hose_too_short` → "Hose won't reach" |
| 15 | Unplug hose | empty or tool → plugged hose end | `Disconnect(issuer, hoseId, endIdx)` (Hose/Storage Network); the end goes into the player's hands (Hose rule 15) | tap | Use | RT | 2 m | `too_far` → "Too far: step closer"; `fixed_end` → "That end is fixed to the pump"; `hands_full` → "Hands full: drop it first (G)" |
| 16 | Lay suction end | suction hose free end → a source's intake zone | `Connect(issuer, hoseId, 1, IntakeZone(sourceId))` (Hose/Storage Network rule 4) | tap | Use | RT | 2 m | the Connect codes of row 14 |
| 17 | Plug cable | pump's cable end → power-post socket | `PlugCable(issuer, pump, socket)` (Pumps) | tap | Use | RT | 2 m; cable reach 30 m | `not_holding` → "Pick up the cable end first"; `not_placed` → "Set the pump down first"; `no_motor` → "No motor to power"; `socket_in_use` → "Socket in use"; `too_far` → "Too far, or the cable won't reach" |
| 18 | Unplug cable | empty or tool → plugged cable end | `UnplugCable(issuer, pump)` (Pumps) | tap | Use | RT | 2 m | `not_plugged` → "Not plugged in"; `too_far` → "Too far: step closer"; `hands_full` → "Hands full: drop it first (G)" |
| 19 | Crank | empty or tool → hand-pump handle | `CrankStart(issuer, pump)` on press; `CrankStop(issuer, pump)` on release, move, look-away, a hands change, or pause or any UI-map menu (Pumps; Rules 5, 10) | hold (setting: Toggle, Rule 5) | Use | RT | 1.5 m | `not_placed` → "Set the pump down first"; `no_handle` → "No handle: it's electric now"; `too_far` → "Too far: step closer" |
| 20 | Bolt motor | motor kit → hand pump | `BoltMotor(issuer, pump)` when the hold completes (Pumps) | hold 2 s | Use | RT | 2 m | `not_holding_kit` → "Hold the motor kit"; `no_pump` → "Needs a hand pump"; `already_powered` → "Already electric"; `already_fitted` → "Motor already fitted"; `too_far` → "Too far: step closer" |
| 21 | Motor switch | any → motor On/Off lever | `SetSwitch(issuer, pump, on)`, `on` = not the current lever state (Pumps) | tap | Use | RT | 3 m | `not_placed` → "Set the pump down first"; `no_motor` → "No motor to switch"; `too_far` → "Too far: step closer" |
| 22 | Fit inlet | inlet fitting → drum inlet boss | `FitInlet(issuer, tank)` when the hold completes (Hose/Storage Network) | hold 2 s | Use | RT | 2 m | `not_holding` → "Hold the inlet fitting"; `too_far` → "Too far: step closer"; `already_fitted` → "Already fitted" |
| 23 | Swap in | empty or tool → container on the staging spot | `SwapStorage(issuer, tank)` (Hose/Storage Network) | tap | Use | RT | 2 m | `too_far` → "Too far: step closer"; `not_on_staging_spot` → "Set it on the staging spot first"; `not_an_upgrade` → "Only a bigger, empty container" |
| 24 | Pry crate | empty or tool → Landed crate | `OpenCrate(issuer, crate)` when the hold completes (Economy) | hold 1 s | Use | RT | 2 m | `already_open` → "Already open"; `too_far` → "Too far: step closer"; a tap shows "Hold to pry" and sends nothing |
| 25 | Order | any → ORDER lever | `Order(issuer, item)`, `item` = the first not-owned item in the ladder chain (Economy rule 6) | tap | Use | RT | 3 m | In Economy's check order: `too_far` → "Too far: step closer"; `already_owned` → "Already bought"; `locked` → "Requires: {prereq}"; `cannot_afford` → "Short {n} cr". `locked` cannot occur, because `item` is always the first not-owned item, whose prerequisite is owned; `already_owned` occurs only when two `Order` presses apply in the same tick (Economy AC-12) |
| 26 | Launch | any → LAUNCH lever | `Launch(issuer)` (Shipping) | tap | Use | RT | 3 m | `too_far` → "Too far: step closer"; `pad_busy` → "Next rocket in {s} s"; `nothing_to_ship` → "No cargo: fill the tank" |

   - Hover prompts (no rejection): Scoop, Pour, "Pick up", "Place", "Plug in", "Unplug", "Drop in" (suction end over an intake zone), "Hold to crank" (Hold mode) or "Press to crank" (Toggle mode), with the missing part when a cranked pump can't pump (Pumps `no_intake` → "No intake: connect the suction hose to the water", `no_outlet` → "No outlet: run a hose to the tank", `no_power` → "No power: plug in the cable"), "Bolt on", "Switch on" / "Switch off", "Fit inlet", "Swap in", "Pry open", "Order: {item}, {price} cr" ("Catalogue complete" when nothing is left, and Use sends nothing), "Launch: pays {N} cr" (N = `floor(Economy.PayoutFor(min(pad pool, cargo_capacity)) / 1000)`, since `PayoutFor` returns millicredits, Economy rule 2; Shipping UI).
   - `{n}` in "Short {n} cr" = price − `floor(credits)`; `{s}` = `ceil(remaining ticks × dt)`; `{prereq}` = the Locked card's "Requires" name. All are read from read-only state when the rejection arrives.
   - A code with no text here shows "Can't do that", and a warning is logged naming the code.
5. **Hold actions setting (D2, Nour 2026-11-02).** Options → Controls → "Hold actions: Hold / Toggle", default **Hold**. Stored in player settings, not in the save file.
   - **Hold:** the crank runs while Use is held; pry 1 s, bolt 2 s and fit inlet 2 s complete only if Use stays held for the full time.
   - **Toggle:** a press on the handle latches the crank (`CrankStart`); the next Use press, any move input past the stick dead zone, the eye ray leaving the handle, or pausing releases it (`CrankStop`). A press on a pry, bolt or fit target starts the hold, whether it is a tap or a long press (releasing Use does nothing in Toggle mode), and it completes by itself after the same duration unless the player presses Use a second time (which cancels it), moves or looks off the target. The ring shows in both modes.
   - **Both modes:** a hands change (a `SelectTool` or `PickUp` sent) or opening any `UI`-map menu (the pause menu or the terminal readout) sends `CrankStop` and cancels any fixed hold in progress (Pumps rule 4).
   - The hold durations are read from their owners' data: pry 1 s (Economy, crate pry time), bolt 2 s (Pumps), fit inlet 2 s (Hose/Storage Network). The tap threshold (0.25 s) is this doc's.
6. **Requests only, and send order.** Every request carries the issuer's `PlayerId` (0 in the MVP) and stable ids, never a quantity (Game State Rule 5). The result arrives through state and `RequestRejected(issuer, reason)`. In the frame in which the next tick will run (the controller's script runs before the tick driver), the controller submits `PlayerPose` first, then any interaction requests buffered since the last tick, so the range check and the pose come from the same frame. This adds no latency, because requests are only applied at step 1. If a frame runs several ticks (catch-up), one `PlayerPose` is sent per tick. Per Game State Rule 3, a request applied in tick M sees every change made in tick M−1 and earlier; its effect is first visible to the next request at step 1 of tick M+1.
7. **Instant feedback:** the animation and sound for an action start on the press, before the tick (Game State latency rule). On a rejection, the animation of the action in progress is cut short and the thunk plays. `RequestRejected` carries no request id in the MVP, which is safe with one player and one action at a time: the controller pairs a rejection with the last interaction request it sent (Rule 4) for both the cut animation and the text. A request sequence id is deferred to co-op (Open Questions).
8. **Placing items and the PlacementValidator.** Holding a placeable item and aiming at ground within 2 m shows a ghost: green outline = valid, red = blocked, with the reason as text. The ghost and the simulation's step 1 both call the same pure-C# `PlacementValidator` over authored placement zones and item footprints (Game State Rule 12), so a green ghost is always accepted. Hoses and cables never block placement; they are visual splines.
9. **Physics props:** crates, litter and dropped items are physics props moved by presentation. When one comes to rest, its presenter (not the interaction layer) sends `PropSettled(id, pose)` (Game State Rule 12). A suction end binds to a source only through `Connect` on its intake zone (row 16, Hose/Storage Network rule 4); a suction end dropped with G just lies where it lands.
10. **Pause:** opening the pause menu stops the simulation (Game State Paused), cancels any hold in progress, releases a latched or held crank by sending `CrankStop` (applied on the first tick after resume), disables the `Player` action map and enables `UI`. Opening any other `UI`-map menu (the terminal readout) and any hands change (`SelectTool` or `PickUp`) also send `CrankStop` (Rule 5).
11. **Input System spec:** one Input Actions asset, `MarsInput.inputactions`, with two action maps: `Player` (every row of the table) and `UI` (pause menu, terminal readout). Bindings for keyboard/mouse and gamepad as in the table; rebinding is a later Settings feature. Tunable values (tap threshold, eye height, ranges, rotate step) live in an `InteractionTuning` data asset.

### States and Transitions

Hands: **Empty ↔ Tool(t) ↔ Carrying(item)**. Picking up from Tool(t) goes to Carrying and stows t; dropping, placing or plugging the last end goes back to Tool(t) if one was stowed, else Empty.

Controller: **Walking / Sprinting / Crouching / Airborne / Wading / UsingHeld**. UsingHeld covers the crank and the fixed holds (pry, bolt, fit): look is allowed, a move input leaves UsingHeld and cancels the hold or sends `CrankStop`, so the player can never walk out of crank range while cranking. Wading is entered when the water under the player is deeper than 0.3 m.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Requests, including `PlayerPose` once per tick; the eye height for range checks | `RequestRejected`; read-only state for prompts |
| Hand Tools, Hose/Storage Network, Pumps, Shipping, Economy | Requests on their objects (table, Rule 4) | Request definitions and rejection codes; hold durations; prompt values (payout, shortfall, prereq names) |
| Placement (`PlacementValidator`) | Ghost queries | Valid or blocked, with the reason |
| HUD | Crosshair prompt, hold ring, rejection text | None |
| Save & Load | Nothing directly: Save reads the Placement slice (pose and hands item, Game State Rule 12) | On load, the controller is placed at the recorded pose with the recorded hands item |
| Settings | None | Sensitivity, invert, Hold actions (Hold / Toggle); bindings later |

## Formulas

The **move_speed** formula is defined as:

`speed = base(mode) × carry_mult × ground_mult`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| base(mode) | float (m/s) | 2–6.5 | data file | Walk 4, sprint 6.5, crouch 2 (sprint is not available while carrying a crate, tank or pump) |
| carry_mult | float | 0.6–1 | data file | Empty or tool 1, hose or cable end 0.9, full bucket 0.75, crate or tank 0.7, pump 0.6 |
| ground_mult | float | 0.6–1 | data file | Dry ground 1, mud 0.85, wading 0.6; the lowest surface under the player applies |

**Output Range:** about 0.72–6.5 m/s (crouch × pump × wading = 2 × 0.6 × 0.6 = 0.72).

## Edge Cases

- **If two interactables overlap under the crosshair**: the nearest wins; a tie within 0.05 m goes to the one matching the hands item.
- **If the player moves or looks away while cranking**: the crank stops (`CrankStop`), in both Hold and Toggle modes. Pumps step 2a also stops it if the recorded pose is ever beyond 1.5 m.
- **If the player releases Use at 0.9 s of a 1 s pry**: nothing is sent; the ring empties.
- **If the player taps a Landed crate**: in Hold mode, "Hold to pry" shows and nothing is sent; in Toggle mode, the pry starts and completes after 1 s.
- **If the player drops a hose end in the pond**: it floats at the surface and can be picked up; it binds as the intake only when the player uses it on a source's intake zone (row 16).
- **If a crate falls into the pond**: it floats to the nearest shore point within 2 s (never lost), then its presenter sends `PropSettled`.
- **If the player falls into the pond**: it's 1 m deep; they wade at 60% speed. No drowning.
- **If the player tries to place an item in water or outside the placement zones**: the ghost goes red with the reason, and Use sends nothing. A hose or cable under the ghost does not block it.
- **If the player picks up a pump with hoses plugged**: Placement detaches them (`Network.DetachAll`, Game State Rule 12); an electric pump must be switched off first (`switch_off_first`).
- **If all ladder items are owned**: the ORDER lever shows "Catalogue complete" and sends nothing.
- **If input is pressed during a pause**: ignored (the `Player` map is disabled).

## Dependencies

**Upstream:** Game State (Hard: requests, Placement, `PlacementValidator`, read-only state). Request definitions and codes from Hand Tools, Hose/Storage Network, Pumps, Shipping and Economy (Hard).
**Downstream:** every interactive system (Hard), HUD (Soft), Save & Load (Hard, through the Placement slice), Onboarding (VS), Co-op (later: requests become network commands).

## Tuning Knobs

| Knob | Default | Safe range |
|---|---|---|
| Walk / sprint / crouch | 4 / 6.5 / 2 m/s | 3–5 / 5–8 / 1.5–3 |
| Eye height | 1.6 m | 1.5–1.75 m |
| Interact range | 2 m default; hand-pump handle 1.5 m; levers 3 m | 1.5–3 m |
| Tap threshold | 0.25 s | 0.15–0.35 s |
| Ghost rotate step | 15° | 5–45° |
| Rejection text duration | 1.5 s | 1–3 s |
| Mouse sensitivity | 1.0 | 0.1–5 |
| Carry multipliers | hose/cable end 0.9, bucket 0.75, crate/tank 0.7, pump 0.6 | 0.5–0.95 |
| Mud / wading multiplier | 0.85 / 0.6 | 0.7–1 / 0.5–0.8 |
| Hold actions | Hold | Hold / Toggle |

## Visual/Audio Requirements

- Viewmodel hands holding the current item; heavy items show a lean and slight camera bob.
- Footsteps vary by surface (grass, gravel pad, wet mud, shallow water). Placeholder in the MVP.
- Rejection: one dull thunk, shared by all codes; Hose and Hand Tools may add their own spring-back or "full" clunk on top.

## Game Feel

- Input to animation: same frame. Movement should feel grounded, a little heavy, never floaty.
- Head bob subtle and toggleable.
- The hold ring is small, around the crosshair, and fills smoothly; it never hides the target.

## UI Requirements

- Crosshair dot with a context prompt below it; held amount when holding a tool (Hand Tools).
- Radial hold ring around the crosshair from 0.25 s.
- Ghost outline (green or red) with the blocked reason as text.
- Options → Controls: sensitivity, invert, Hold actions (Hold / Toggle).

📌 **UX Flag: First-Person Controller & Interaction:** prompt wording and the rejection text column. Cover in `/ux-design`; the table in Rule 4 is the starting copy.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 3, 6, 8, 9 | `design/gdd/game-state-and-simulation.md` | Requests, Rule 3 tick boundaries, Rule 12 eye-point range, `PlacementValidator`, `PropSettled`, `DetachAll` |
| Rule 4 rows 7–9; Rule 2 | `design/gdd/hand-tools.md` | `SelectTool`, `Scoop`, `Pour`, carry speed, held water per tool |
| Rule 4 rows 9, 14–16, 22–23 | `design/gdd/hose-storage-and-flow-network.md` | `Connect`, `Disconnect`, `FitInlet`, `SwapStorage`, intake zones, socket codes |
| Rule 4 rows 17–21; Rules 5, 10 | `design/gdd/pumps-and-machines.md` | `CrankStart/CrankStop` (rule 4: menu or hands change sends `CrankStop`), `BoltMotor`, `SetSwitch`, `PlugCable`, crank range |
| Rule 4 rows 24–25 | `design/gdd/economy-progression-and-shop.md` | `OpenCrate`, `Order`, ladder chain, pry time, `PayoutFor` |
| Rule 4 row 26 | `design/gdd/shipping-and-launch.md` | `Launch`, console text, lever hover |
| Rule 1 | `design/gdd/drawdown-visuals-and-reveals.md` | Walkable shelf and beach lips (rule 6); mud = exposed basin bed above the water plane (rules 1, 2) |

## Acceptance Criteria

Test doubles: `SpyRequestQueue` (records submitted requests in order), `FakeStateView` (read-only state), `FakeClock` (frame times), `FakeTickDriver` (runs a tick every 6th frame at 60 fps, dt 0.1 s), `FakePlacementZones` (authored zones and footprints for the real `PlacementValidator`), Unity `InputTestFixture` virtual keyboard, mouse and gamepad.

- **AC-01 [EditMode]** GIVEN a full bucket on mud, THEN walk speed = 4 × 0.75 × 0.85 = 2.55 m/s.
- **AC-02 [EditMode]** GIVEN crouching, carrying a pump and wading, THEN speed = 2 × 0.6 × 0.6 = 0.72 m/s; GIVEN carrying a pump, WHEN Left Shift and W are held, THEN speed = 2.4 m/s, and WHEN Space is pressed, THEN the controller stays grounded.
- **AC-03 [EditMode]** GIVEN the Interaction assembly definition, THEN it references only the simulation's read-only view and request types, never a slice-write API (asmdef reference check).
- **AC-04 [PlayMode]** GIVEN pose y = 0 (eye at 1.6 m) and holding a hose end, WHEN the player looks at a socket at eye height 1.8 m away, THEN the prompt "Plug in" shows; at 2.2 m it doesn't. GIVEN empty hands, in Hold mode a hand-pump handle 1.4 m from the eye point shows "Hold to crank"; at 1.6 m it doesn't.
- **AC-05 [PlayMode]** GIVEN `FakeTickDriver` and `SpyRequestQueue`, WHEN Use is pressed on a socket at frame 8 and frames 1–12 run, THEN the queue holds, in order, `PlayerPose` (frame 6), `PlayerPose` and `Connect` (frame 12), every one with issuer 0, and no `Connect` before frame 12.
- **AC-06 [PlayMode]** GIVEN Hold mode, a Landed crate in range and `FakeClock`, WHEN Use is held for 0.9 s and released, THEN 0 requests are sent and the ring empties; WHEN Use is held for 1.0 s, THEN exactly 1 `OpenCrate` is sent, at 1.0 s; WHEN Use is tapped for 0.2 s, THEN 0 requests are sent and "Hold to pry" shows.
- **AC-07 [PlayMode]** GIVEN Hold mode and Use held on a Landed crate, WHEN the eye ray leaves the crate at 0.6 s, THEN 0 requests are sent and the ring hides.
- **AC-08 [PlayMode]** GIVEN Toggle mode, WHEN Use is tapped for 0.1 s on a Landed crate, THEN exactly 1 `OpenCrate` is sent 1.0 s after the press; WHEN instead Use is pressed again at 0.5 s, THEN 0 requests are sent.
- **AC-09 [PlayMode]** GIVEN Hold mode and a hand pump in range, WHEN Use is pressed on the handle, THEN 1 `CrankStart` is sent; WHEN Use is released 30 s later, THEN 1 `CrankStop` is sent; GIVEN cranking, WHEN the left stick moves to 0.5, THEN 1 `CrankStop` is sent and the controller is Walking.
- **AC-10 [PlayMode]** GIVEN Toggle mode, WHEN Use is tapped for 0.1 s on the handle, THEN 1 `CrankStart` and no `CrankStop` are sent; WHEN Use is pressed again 20 s later, THEN 1 `CrankStop` is sent. In separate runs, W, the eye ray leaving the handle, opening the pause menu, opening the terminal readout, and pressing 2 (a `SelectTool(bucket)` sent) each send exactly 1 `CrankStop`.
- **AC-11 [PlayMode]** GIVEN the spoon and bucket Opened and the bucket in hand, WHEN the scroll wheel moves one notch, THEN 1 `SelectTool(spoon)` is sent; GIVEN a pump in the hands with its ghost up, WHEN the scroll wheel moves one notch, THEN the ghost yaw changes by 15° and 0 `SelectTool` requests are sent.
- **AC-12 [EditMode, integration]** GIVEN `FakePlacementZones` with a water area and a valid gravel area, WHEN the ghost pose is over water, THEN the ghost is red with "On water" and Use sends nothing; WHEN the pose is on gravel across a hose spline, THEN the ghost is green and the `PlaceItem` applied at step 1 by stub Placement with the real `PlacementValidator` is accepted.
- **AC-13 [EditMode]** GIVEN the rejection text asset, THEN it has a non-empty entry for every (request, code) pair in the Rule 4 table, and a non-empty default entry for each of the 36 MVP codes: `too_far, blocked, hands_full, not_empty, switch_off_first, not_owned, crate_unopened, not_in_hand, tool_full, tool_empty, source_dry, cannot_deposit, not_holding, wrong_socket, not_installed, no_inlet, socket_in_use, hose_too_short, fixed_end, already_fitted, not_on_staging_spot, not_an_upgrade, pool_full, not_placed, no_handle, not_holding_kit, no_pump, already_powered, no_motor, not_plugged, pad_busy, nothing_to_ship, locked, cannot_afford, already_owned, already_open`. **[PlayMode]** GIVEN a `Connect` sent and then `RequestRejected(0, no_inlet)`, THEN the prompt reads "No inlet: screw in the fitting" for 1.5 s and the thunk plays once. Per-row cases: GIVEN a `BoltMotor` sent and then `RequestRejected(0, already_fitted)`, THEN the prompt reads "Motor already fitted"; GIVEN a `FitInlet` sent and then `RequestRejected(0, not_holding)`, THEN it reads "Hold the inlet fitting" (not row 14's "Pick up the hose end first"). GIVEN an unknown code, THEN "Can't do that" shows and 1 warning is logged.
- **AC-14 [PlayMode]** GIVEN the bucket in hand holding 6 L, WHEN Use is pressed on a hose coil and the `PickUp` is accepted, THEN the hands hold `HoseEnd(hoseId, 0)` and `FakeStateView` still shows 6 L in the bucket; WHEN 1, 2 or the scroll wheel are then pressed, THEN 0 requests are sent.
- **AC-15 [PlayMode]** GIVEN a hose end in the hands and a socket and an Opened crate both 1.50 m away under the crosshair, THEN the prompt is "Plug in".
- **AC-16 [PlayMode]** GIVEN Paused, WHEN E, G and 1 are pressed, THEN 0 requests are sent and the `Player` map is disabled.
- **AC-17 [PlayMode]** GIVEN a crate dropped in the pond, THEN within 2 s it rests on the shore and exactly 1 `PropSettled` is sent.
- **AC-18 [PlayMode]** GIVEN a 1:31 shelf (the steepest of Rule 1's range) and a 1:12 beach lip exposed, WHEN the player stands still on each for 5 s, THEN downhill drift is under 0.01 m; and the player can walk down both.
- **AC-19 [PlayMode]** GIVEN `FakeStateView` with the bucket owned and the barrel not owned, WHEN the ORDER lever is pulled, THEN the hover prompt shows the barrel's price, 220 cr (Economy table), and 1 `Order(0, barrel_100l)` is sent; GIVEN every ladder item owned, THEN the prompt reads "Catalogue complete" and 0 requests are sent.
- **AC-20 [Manual]** GIVEN keyboard/mouse and a gamepad, THEN every row of the Rule 4 table can be done with each, in both Hold and Toggle modes.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, revised after the 2026-11-02 batch design review).** "Hands on the Machinery": every action is aimed and physical; no menus for building. Hold stays the default, so the crank is effort; Toggle protects hands without removing the act. Co-op seam respected (requests only, issuer on every request).

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Request sequence id on `RequestRejected`, so the right animation is cut when several requests are in flight | technical-director (Game State request ADR) | Before the Network Spike |
| Final wording of the rejection text column | ux-designer | `/ux-design` |
| Should hoses be physical (can be stepped on and kinked)? MVP: no | game-designer | Vertical Slice |
| Rebindable controls | ux-designer | Vertical Slice |
