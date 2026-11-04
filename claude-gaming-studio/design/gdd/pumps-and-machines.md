# Pumps & Machines (hand pump, electric motor)

> **Status**: Approved (2026-11-04)
> **Author**: Nour + agents
> **Last Updated**: 2026-11-04 (revised after the 2026-11-04 re-review, rr1 and round 2)
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 1 (Start Tiny, End Enormous); Pillar 2 (Hands on the Machinery)

## Summary

Pumps & Machines covers the hand pump the player cranks, the electric motor that is bolted onto it, and the second electric pump. This system decides whether each pump is running and at what rate; the Hose/Storage Network decides how much water actually moves. Pumps never break or fail: a full tank stalls them quietly and they restart on their own.

> **Quick reference**: Layer `Core gameplay` · Priority `MVP` · Key deps `Hose, Storage & Flow Network`, `Economy`, `Game State`

## Overview

Pumps are the first machines. Each is a placed object with an intake socket (a suction hose that comes pre-plugged at the pump; the player binds its free end with Use on a source's intake zone) and an outlet socket (a discharge hose to a tank inlet). The **hand pump** runs only while a player cranks its handle. The **electric motor** is a kit bolted onto the hand pump: once fitted and plugged into the power post at the launch pad with its cable, the pump is switched on or off with a lever and runs by itself. The **second electric pump** is a complete powered pump with its own intake, outlet and cable. Each tick, at tick step 2a (just before the network at 2b), this system derives each pump's state and sends its rate; the network's flow solve at step 2b moves the water.

## Player Fantasy

**"From sweat to switch."** The hand pump is the first time water moves without you carrying it, but your arm is still the motor: you stand at the handle, cranking, watching the hose pulse. The electric motor is the moment you let go: you bolt it on, run the cable, throw the lever, and the pump keeps going while you walk away. The second pump doubles the roar.

- **Anchor moment:** the first lever pull on the electric motor: a cough, a rising hum, the hose stiffening as water rushes through, and you step back with your hands free.
- **Tone:** mechanical, greasy, satisfying (Pillar 2). Bolts, levers, cables, gauges; no glowing UI.

## Detailed Design

### Core Rules

1. **Getting a pump:** a purchase grants the pump through a direct call, `Pumps.Grant(item, crateId)`, made by Economy in the same step 1 that validates the purchase (Game State Rule 5b). Grant creates the pump's slice record (`motorFitted = false`, `leverOn = false`, `cableSocket = null`). `Purchased` is a notification only and drives nothing here. A pump is usable only once its crate is **Opened** (Pumps reads crate state from the Economy slice); it then comes out at the crate pose through `Placement.Spawn` (Economy rule 8). The motor kit is granted the same way but adds no pump record: it is a loose placeable item until it is bolted on (Rule 4).
2. **Placing and plumbing:** the player carries the pump and sets it down with Placement's `PlaceItem` (Game State Rule 12; ground and overlap checked by `PlacementValidator`). They bind the suction hose's free end with Use on a source's intake zone (6 m hose; a G-drop never binds, Hose rule 4) and connect the discharge hose to a tank inlet (Hose/Storage Network rules 1 to 3). A pump is **connected** when the Network reports both: `IntakeBound(pump)` (suction end bound to a source) and `OutletPool(pump)` not null (outlet hose joined to a pool).
3. **Hand pump (crank):** the pump is cranked by the requests `CrankStart(issuer, pump)` and `CrankStop(issuer, pump)` (see Requests). An accepted `CrankStart` sets the issuer's crank flag to that pump. The crank flag is per player, held in memory and never saved (Game State Rule 1). At step 2a, a hand pump is **demanded** if some player's crank flag names it and that player's recorded eye point (pose + eye height) is within 1.5 m of the handle; if the player is beyond 1.5 m, step 2a clears the flag. Rate while Running: 1.25 L/s (Economy table), shown as a stroke every 0.8 s moving 1 L each.
4. **Hold actions setting (Nour, D2):** the controller has a setting **"Hold actions: Hold / Toggle"**, default **Hold**. The controller (FPC) owns the setting and turns input into requests; Pumps only ever sees `CrankStart` and `CrankStop`.
   - **Hold:** pressing Use on the handle sends `CrankStart`; releasing Use sends `CrankStop`. Any move input sends `CrankStop` (FPC state UsingHeld).
   - **Toggle:** one press sends `CrankStart` and latches the crank. Pressing Use again, moving, or looking away from the handle sends `CrankStop`.
   - **Both:** opening the pause menu or any menu, or changing the hands item, sends `CrankStop`.
   - **Fixed holds** (pry a crate 1 s, bolt the motor 2 s, `FitInlet` 2 s): with Toggle on, a press (a tap or a long press) starts the hold and it completes by itself after its time unless the player presses again, moves or looks away. The request is sent when the hold completes, in both modes.
5. **Electric motor (upgrade kit):** the player carries the kit to a placed hand pump and holds Use for 2 s; when the hold completes the controller sends `BoltMotor(issuer, pump)`. On success the pump's `motorFitted` becomes true, the handle is removed (visual only), the rate becomes 8 L/s, and Pumps calls `Placement.Remove(kitId)` to consume the kit (direct call, Game State Rule 5b). The kit's cable comes pre-wired to the motor; its free end is handled as in Rule 6.
6. **Power:** the player carries the cable's free end to the **power post** at the launch pad and plugs it into a free socket: `PlugCable(issuer, pump, socket)`. The cable is 30 m, measured as the straight line from the pump's cable port to the socket; 30.0 m is accepted and anything longer is rejected with `too_far`. The post has 2 sockets in the MVP (`post_sockets`). `UnplugCable(issuer, pump)` frees the socket. A pump is **powered** when `cableSocket` is not null.
   - **The cable end:** Pumps stores where each cable's free end is (like the Network does for hose ends; Game State Rule 12). Placement tracks only whether it is in a player's hands, as the hands item `CableEnd(pumpId)` (one cable per pump, so the pump id names the end). On an accepted `PlugCable`, Pumps calls `Placement.SetHands(issuer, null)` and the end sits at the socket pose. On an accepted `UnplugCable`, Pumps calls `Placement.SetHands(issuer, CableEnd(pumpId))` and the end goes to the issuer's hands (a held tool is stowed, as on `PickUp`; a carried item rejects the unplug with `hands_full`). Picking up or dropping a loose cable end works like a hose end (Placement `PickUp` and `Drop`), with the same direct calls the Network has for hose ends (Game State Rule 12): while validating `PickUp`, Placement asks `Pumps.CanPickUp(id)`; when the end enters the hands, Placement calls `Pumps.CableEndHeld(pumpId, issuer)`; on `Drop`, Placement calls `Pumps.CableEndDropped(pumpId, pose)` and Pumps records `pose` as the rest pose (`cableEndPose`). The motor kit's cable end travels with the kit until the kit is bolted on.
7. **Electric switch:** a lever on the motor sets `leverOn` through `SetSwitch(issuer, pump, on)`. An electric pump is demanded when `leverOn` is true; the player need not be present.
8. **Second electric pump:** a complete powered pump that follows rules 1, 2, 6 and 7, with its own suction hose, discharge hose and pre-wired cable in its crate. Until the IBC arrives the only tank is the drum, so its discharge hose goes into the drum's second inlet port: the drum's one twin-port inlet fitting counts as 2 inlets and is screwed in by one `FitInlet` (Hose/Storage Network rule 1). By convention the first pump uses drum inlet port A (inlet index 0) and the second pump uses port B (inlet index 1). The Economy table's `electric_pump_2` row has `rate_Lps` = 8 (the table prints it as "+8").
9. **No failure states:** pumps never break, overheat or need fuel in the MVP. A demanded pump whose pool is full is **Stalled**: still on, moving nothing, with a strained-idle sound. It restarts by itself when space opens. A demanded pump whose source is drained is **Dry**: a sputtering sound; it restarts when its suction end is bound with Use on the intake zone of a source with water.
10. **Pumps decide state, the network decides flow.** At step 2a this system derives each pump's state (Formulas, `pump_state`) from data already in state: the Network's `IntakeBound(pump)`, `OutletPool(pump)` and `IsFull(poolId)`, the source's drained flag, `cableSocket`, `leverOn` and the crank flags. It sends `(Running, rate)` per pump to the network. It reads back the litres actually moved, only for sound, the stroke animation and gauges.
11. **Tick timing (Game State Rule 3):** a request applied at step 1 of tick N is used by step 2a of the same tick N. A source drained or a pool filled at step 2b of tick M (flagged at 2b, `SourceDrained` raised at 2d) is first seen by Pumps at step 2a of tick M+1, so **Dry and Stalled are set at 2a of the tick after** the drain or the fill. In the draining or filling tick itself the network has already moved only what was there. Shipping's `Take` applies at step 1 (the launch request), so space it frees in tick M lets a Stalled pump run again at 2a of the same tick M.
12. **Moving a pump:** the player picks up a pump with Placement's `PickUp(issuer, id)`. Placement asks `Pumps.CanPickUp(id)` while validating; an electric pump with `leverOn` true is rejected with `switch_off_first`. On success Placement calls `Network.DetachAll(nodeId)` in the same step (Game State Rule 12), so no hose stays connected to a carried pump. At step 2a Pumps sees the pump is Carried and clears its `cableSocket` (the cable end is left lying at the socket pose, which becomes its `cableEndPose`). A carried pump is never demanded, and a picked-up hand pump's crank flags are cleared.
13. **State changes are announced:** each time a pump's derived state changes at step 2a, Pumps raises `PumpStateChanged(pump, from, to)` once. Presenters (audio, the stroke animation, the play log) use it instead of polling. At the first 2a after a new game or a load, each pump's from-state is set to its derived state with no event, so no `PumpStateChanged` is raised on load. This event is Game State Rule 7 event 14 (see Dependencies).
14. **Missing part:** `Pumps.MissingPart(pump)` is a read-only query that returns the first missing part, in this order: `no_intake` (not `IntakeBound(pump)`), then `no_outlet` (`OutletPool(pump)` is null), then `no_power` (electric and `cableSocket` is null), else none. FPC reads it for the pump's prompt and owns the text.

### Requests

Every request carries the issuer's `PlayerId` (0 in the MVP), is submitted by the First-Person Controller & Interaction layer, and is validated at step 1 against the state at that moment (Game State Rule 5). A rejection raises `RequestRejected(issuer, reason)` and changes nothing. Checks run in the order listed; the first failure is the reason. Ranges are measured from the issuer's eye point (Game State Rule 12): crank 1.5 m, default 2 m, levers 3 m. FPC owns the binding table and the player-facing text for each reason.

| Request | Sent by FPC when | On success | Rejected with (in order) |
|---|---|---|---|
| `CrankStart(issuer, pump)` | Hold: Use pressed on the handle. Toggle: the latching press | Issuer's crank flag = pump (replacing any earlier flag) | `not_placed` (pump Crated or Carried); `no_handle` (pump is electric: motor fitted or second pump); `too_far` (eye point more than 1.5 m from the handle) |
| `CrankStop(issuer, pump)` | Hold: Use released. Toggle: second press, move or look away. Both: any move input, the eye ray leaving the handle, a hands change (`SelectTool` or `PickUp`), or any menu or pause opened (FPC row 19, rules 5 and 10) | Issuer's crank flag cleared | Never rejected; if the flag does not name this pump it does nothing |
| `BoltMotor(issuer, pump)` | The 2 s hold on a pump completes while the issuer holds the motor kit | `motorFitted = true`; `Placement.Remove(kitId)` | `not_holding_kit`; `no_pump` (target is not a Placed pump, for example the kit was unboxed while the hand pump crate is still shut); `already_powered` (target is the second electric pump); `already_fitted`; `too_far` (more than 2 m) |
| `SetSwitch(issuer, pump, on)` | Use on the motor lever | `leverOn = on` (setting the current value is accepted and changes nothing) | `not_placed`; `no_motor` (hand pump without motor); `too_far` (more than 3 m) |
| `PlugCable(issuer, pump, socket)` | Use on a post socket while holding that pump's cable end | `cableSocket = socket`; `Placement.SetHands(issuer, null)`; the cable end sits at the socket pose (Rule 6) | `not_holding` (issuer's hands item is not `CableEnd(pump)`); `not_placed`; `no_motor`; `socket_in_use` (socket already holds a cable; same code as Hose); `too_far` (issuer more than 2 m from the socket, or pump cable port to socket more than 30.0 m) |
| `UnplugCable(issuer, pump)` | Use on a plugged socket | `cableSocket = null`; `Placement.SetHands(issuer, CableEnd(pump))` (Rule 6) | `not_plugged`; `too_far` (eye point more than 2 m from the socket); `hands_full` (issuer carries an item; a tool is stowed instead) |
| `PickUp(issuer, id)` (Placement's request) | Use on a placed pump with empty hands | Placement takes it; `DetachAll`; cable cleared at 2a (Rule 12) | Placement's own reasons, then `switch_off_first` from `Pumps.CanPickUp` |

### States and Transitions

States are derived at step 2a every tick (Formulas, `pump_state`); none is saved.

| State | Condition | Can go to |
|---|---|---|
| Crated | Granted; its crate is not Opened | Placed (crate opened; pump spawns at the crate pose) |
| Carried | In a player's hands | Placed (set down) |
| Placed | On the ground but not connected (no intake source or no outlet pool), or electric and not powered, or not demanded and its source is drained | Ready; Carried (picked up) |
| Ready | Connected, powered if electric, source has water, not demanded | Running (crank started or lever On); Placed (hose or cable removed, source drained); Carried |
| Running | Demanded, connected, powered, source has water, pool has space | Ready (crank stopped or lever Off); Stalled; Dry; Placed (hose or cable removed) |
| Stalled | Demanded, connected, powered, pool Full | Running (space opens); Ready (crank stopped or lever Off); Placed (hose or cable removed) |
| Dry | Demanded, connected, powered, source drained | Running (suction end bound with Use on the intake zone of a source with water); Placed (crank stopped, lever Off, hose or cable removed) |

**Saved (Game State Rule 1, amended 2026-11-02):** per pump `{id, kind, motorFitted, leverOn, cableSocket|null, cableEndPose|null}`, where `kind` is `hand_pump` or `electric_pump_2` and `cableEndPose` is the rest pose of a loose cable end (null while plugged, in a hand, or with no cable). Running, Stalled and Dry are derived; the crank flag is never saved. Pump and kit positions belong to Placement and hose connections to the Network.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Pumps slice; `PumpStateChanged` (Game State Rule 7); `RequestRejected` reasons | Tick step 2a; requests at step 1 |
| Hose/Storage Network | `(Running, rate)` per pump at 2a | `IntakeBound(pump)`, `OutletPool(pump)`, `IsFull(poolId)`, litres moved per pump |
| Water Source Model | — | Drained flag of the pump's intake source |
| Economy | — | `Pumps.Grant(item, crateId)` call at purchase; crate state (usable once Opened); pump rates, cable length, post sockets, bolt time |
| Placement (Game State) | `Pumps.CanPickUp(id)` answer; `Placement.Remove(kitId)` call on bolt; `Placement.SetHands` calls on plug and unplug | Pump and kit poses, hands items (including `CableEnd(pumpId)`), recorded `PlayerPose`; `CableEndHeld(pumpId, issuer)` and `CableEndDropped(pumpId, pose)` calls (Rule 6) |
| First-Person Controller & Interaction | Request definitions and reason codes; `Pumps.MissingPart(pump)` (`no_intake`, `no_outlet`, `no_power`) for prompts | `CrankStart/CrankStop/BoltMotor/SetSwitch/PlugCable/UnplugCable`; the Hold actions setting lives there |
| Audio, HUD, Play Log | `PumpStateChanged`; litres moved per pump | — |

## Formulas

The **pump_state** formula (evaluated at step 2a for each pump, in id order):

```
if crate not Opened              -> Crated
else if carried                  -> Carried
else if !connected or !powered   -> Placed
else if !demanded                -> drained ? Placed : Ready
else if drained                  -> Dry
else if poolFull                 -> Stalled
else                             -> Running
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| connected | bool | — | Network | `IntakeBound(pump)` and `OutletPool(pump)` not null |
| powered | bool | — | Pumps slice | `true` for an unmotored hand pump; otherwise `cableSocket != null` |
| demanded | bool | — | Pumps | Hand pump: a valid crank flag within 1.5 m. Electric: `leverOn` |
| drained | bool | — | Water Source | Drained flag of the intake source, the current flag at 2a |
| poolFull | bool | — | Network | `IsFull(OutletPool(pump))` (Full within 1e-6 L), the current flag at 2a |

**Output Range:** exactly one of the 7 states.

The **pump_demand** formula:

`rate_p = (state == Running) ? rate_table(p) : 0` (the network then applies `min(rate_p, hoseCap) × dt`)

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| rate_table(p) | double (L/s) | 0.6–10 | Economy table | Hand pump 1.25 (tuning 0.6–1.5); electric (fitted motor or `electric_pump_2`) 8 (tuning 4–10). Hose's `rate_p` range 0–10 is the general bound (0 when not Running) |
| hoseCap | double (L/s) | 8–15 | Economy table | 10; applied by the Network |
| dt | double (s) | 0.1 | Game State | Tick length |

**Output Range:** 0, 1.25 or 8 L/s; at most 0.8 L per pump per tick at 10 Hz.

The **hand_pump_effective** formula (pacing only, not run in game):

`rate_eff = rate_table(hand_pump) × duty`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| rate_table(hand_pump) | double (L/s) | 0.6–1.5 | Economy table | 1.25 |
| duty | double | 0.5–0.9 | playtest estimate (Economy) | Share of the hand stage the pump is Running; estimate 0.7 |

**Output Range:** 0.625 to 1.125 L/s; 0.875 L/s at duty 0.7, which matches the Economy pacing model.

The **cable_reach** check:

`accepted = distance(cablePort(pump), socketPose) ≤ power_cable_m` (straight line, metres; `power_cable_m` = 30)

## Edge Cases

- **If the player cranks a pump that is not connected**: `CrankStart` is accepted, the handle moves (feedback), the state stays Placed and no water flows; the prompt names the missing part from `Pumps.MissingPart` (here `no_outlet`; FPC owns the text).
- **If the pool fills while cranking**: Stalled from the next tick's 2a; the handle stiffens (animation) and the strained sound plays.
- **If the player walks beyond 1.5 m while cranking in Hold mode**: any move input already sends `CrankStop` (FPC row 19), so this only happens if the player is pushed; step 2a clears the crank flag and the pump drops to Ready.
- **If the electric pump is On and the player leaves the area or saves**: it keeps running (the simulation is global); on load it is Running again at the first 2a from the saved `leverOn` and `cableSocket`.
- **If the cable is unplugged while Running, Stalled or Dry**: the pump is Placed at 2a of the same tick the unplug applies, with a power-down whine.
- **If the motor kit is bought before the hand pump crate is opened**: the kit unboxes as a loose item at its crate pose; a bolt attempt on anything that is not a Placed hand pump is rejected with `no_pump`.
- **If the player tries to bolt the motor onto the second electric pump**: rejected (`already_powered`).
- **If both electric pumps feed one pool and it fills**: both are Stalled at the same 2a (the tick after the fill); both are Running again at 2a of the launch tick (Shipping's `Take` applies at step 1).
- **If both pumps share one source and it drains**: both are Dry at 2a of the tick after the drain.
- **If a Dry pump's suction end is bound with Use on the other source's intake zone**: the Network rebinds the intake at step 1; the pump is Running at 2a of that tick.
- **If the player tries to pick up a running electric pump**: rejected (`switch_off_first`). With the lever Off the pickup succeeds; hoses detach and the cable drops at the post.
- **If both post sockets are full**: a third cable is rejected with `socket_in_use`. The MVP has exactly two cables, so this happens only if a cable is moved onto an occupied socket.
- **If Toggle is on and the player looks away from the handle**: FPC sends `CrankStop`; the pump drops to Ready at the next tick's 2a.

## Dependencies

**Upstream:** Hose/Storage Network (Hard), Economy (Hard), Game State incl. Placement (Hard), First-Person Controller & Interaction (Hard), Water Source Model (Soft, drained flag).
**Downstream:** Audio (Soft), HUD (Soft), Shipping & Launch (Soft: launching changes nothing here, but `Take` frees pool space), Save & Load (Hard), Play Log (Soft).

**Amendments made in other docs (2026-11-02):** Game State Rule 7 has `PumpStateChanged(pump, from, to)` (event 14); Game State Rule 12 lists Pumps as a caller of `Placement.Remove` (motor kit) and `Placement.SetHands` (cable end plug and unplug), says cable-end positions are stored by Pumps (hands item `CableEnd(pumpId)`; Placement calls `Pumps.CableEndHeld` and `Pumps.CableEndDropped`), and has the `Pumps.CanPickUp` check (`switch_off_first`) on `PickUp`.

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Hand pump rate | 1.25 L/s | 0.6–1.5 L/s | Economy table; real hand pumps |
| Stroke period | 0.8 s | 0.5–1.2 s | Presentation; litres per stroke = rate × period |
| Crank range | 1.5 m | 1–2 m | From the eye point (Game State Rule 12) |
| Lever range | 3 m | 2–3 m | FPC interact range for levers |
| Bolt and plug range | 2 m | 1.5–3 m | FPC default interact range |
| Electric rate | 8 L/s | 4–10 L/s | Economy table; must stay ≤ hose cap |
| Bolt-on time | 2 s | 1–4 s | Presentation hold; request sent when it completes |
| Power cable length (`power_cable_m`) | 30 m | 20–50 m | Level must keep pump spots within reach of the post |
| Power post sockets (`post_sockets`) | 2 | 2–4 | MVP needs 2 |
| Hold actions setting | Hold | Hold / Toggle | Owned by FPC; listed here because the crank depends on it |

Rates, cable length, socket count and bolt time are stored in the Economy table (Economy owns all designed numbers).

## Visual/Audio Requirements

- Hand pump: cast-iron body, long orange-gripped handle, a visible stroke with a gulp of water at the outlet per stroke.
- Electric motor: a squat green motor with an orange lever, a thick cable reel; hum rises with load; strained idle when Stalled; sputter when Dry.
- Power post: a yellow-and-black company post at the launch pad with two sockets and a little "OWNED BY THE COMPANY" plate.

## Game Feel

- Cranking: each stroke must feel weighty (camera bob, resistance animation) but never require button mashing; hold to crank by default, with the Toggle setting for players who cannot hold a button for minutes (the hand stage lasts about 2:42 for the reference player, 4:18 to 7:00).
- Lever: a satisfying throw with a half-second spin-up sound. The spin-up is presentation only: water moves from the first 2a after the lever applies.

## UI Requirements

- Prompts: "Hold to crank" (Hold) or "Press to crank" (Toggle), "Bolt on motor", "Plug in", "Unplug", "Switch on/off", and the missing-part reason from `Pumps.MissingPart` when not Ready.
- Rejection text for each reason lives in FPC's binding table.
- No pump HUD in the MVP; state reads from sound and the hose pulse. Flow gauges come with the Logistics Overlay (Alpha).

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 2, 8, 9, 10 | `design/gdd/hose-storage-and-flow-network.md` | Typed sockets, twin-port drum inlet, intake zones, flow solve, Full pools, `DetachAll` |
| Rule 9 | `design/gdd/water-source-model.md` | Drained flag |
| Rules 1, 6, values | `design/gdd/economy-progression-and-shop.md` | Grant call, crate Opened, rates, cable, sockets |
| Rules 1, 3, 11, 12, 13 | `design/gdd/game-state-and-simulation.md` | Pumps slice, step 2a, tick boundaries, Placement, events |
| Rule 4, Requests | `design/gdd/first-person-controller-and-interaction.md` | Hold actions setting, binding table, rejection text |

## Acceptance Criteria

**Test doubles (EditMode):** `FakeNetwork` (per pump: `IntakeBound` and `OutletPool`, connected = both; per pool: `IsFull`; records each `(pump, Running, rate)` it receives; returns litres moved as `rate × dt` unless told otherwise), `FakeSource` (drained flag, `Available`), `FakePowerPost` (2 sockets with poses), `FakePlacement` (poses, hands items, recorded `PlayerPose`; records `Remove` and `SetHands` calls), `FakeCrates` (crate state per item). dt = 0.1 s; tolerance 1e-6 unless stated. Player 0 eye height 1.6 m.

- **AC-01 [EditMode]** GIVEN a connected hand pump, player 0's eye point 1.0 m from the handle and `CrankStart` applied at step 1 of tick 1, WHEN 10 ticks run, THEN `FakeNetwork` receives `(Running, 1.25)` on each of ticks 1 to 10 and 1.25 L moves in total.
- **AC-02 [EditMode]** GIVEN AC-01 after tick 10, WHEN `CrankStop` applies at step 1 of tick 11, THEN the state is Ready at 2a of tick 11 and the rate sent is 0.
- **AC-03 [EditMode]** GIVEN a connected hand pump, WHEN `CrankStart` is sent with the eye point 1.50 m from the handle, THEN it is accepted; WITH 1.51 m, THEN `RequestRejected(too_far)`. GIVEN a Running hand pump, WHEN the recorded eye point moves to 1.6 m, THEN at the next 2a the crank flag is cleared and the state is Ready.
- **AC-04 [EditMode]** GIVEN a hand pump with `IntakeBound` true, `OutletPool` null and `CrankStart` accepted, WHEN a tick runs, THEN the state is Placed, the rate sent is 0, and `Pumps.MissingPart` returns `no_outlet`. GIVEN `IntakeBound` false and `OutletPool` null, THEN it returns `no_intake`. GIVEN a motored hand pump with both set and `cableSocket` null, THEN it returns `no_power`; with the cable plugged, none.
- **AC-05 [EditMode, FPC input mapper with `FakeInput`]** GIVEN Hold actions = Toggle and the crosshair on a handle, WHEN Use is pressed and released once, THEN exactly one `CrankStart` is sent and none follows in the next 30 ticks; WHEN the player then moves, presses Use again, looks away, or opens the pause menu (each case from a fresh latch), THEN exactly one `CrankStop` is sent. GIVEN Hold actions = Hold, WHEN Use is pressed and released, THEN one `CrankStart` on press and one `CrankStop` on release.
- **AC-06 [EditMode, FPC input mapper with `FakeInput`]** GIVEN Hold actions = Toggle and the motor kit held at a hand pump, WHEN Use is tapped, THEN one `BoltMotor` is sent 2.0 s later; WHEN the player looks away at 1.0 s instead, THEN none is sent.
- **AC-07 [EditMode]** GIVEN player 0 holding the motor kit 1.5 m from a Placed hand pump, WHEN `BoltMotor` applies, THEN `motorFitted` is true, `FakePlacement` records one `Remove(kitId)`, and a later `CrankStart` on that pump is rejected with `no_handle`.
- **AC-08 [EditMode]** GIVEN the motor kit held and the hand pump crate Landed but not Opened (`FakeCrates`), WHEN `BoltMotor` targets the crate's pump id, THEN `RequestRejected(no_pump)`. GIVEN the kit held at the second electric pump, THEN `RequestRejected(already_powered)`. GIVEN the hand pump already fitted, THEN `RequestRejected(already_fitted)`.
- **AC-09 [EditMode]** GIVEN a fitted motor, a cable in post socket 0, `leverOn` true and the pump connected, WHEN 10 ticks run with player 0 50 m away, THEN `FakeNetwork` receives `(Running, 8)` each tick and 8 L moves.
- **AC-10 [EditMode]** GIVEN the motored hand pump on drum inlet port A (inlet 0) and the second electric pump on drum inlet port B (inlet 1), both Running into an empty drum, WHEN one tick runs, THEN `FakeNetwork` receives `(Running, 8)` for each pump.
- **AC-11 [EditMode]** GIVEN an electric pump Running, WHEN `UnplugCable` applies at step 1 of tick N, THEN `cableSocket` is null, the state is Placed at 2a of tick N, and the rate sent in tick N is 0.
- **AC-12 [EditMode]** GIVEN an electric pump Running into a 200 L pool holding 199.6 L, WHEN tick 1 runs and `FakeNetwork` moves 0.4 L and sets the pool Full, THEN at 2a of tick 2 the state is Stalled and the rate sent is 0; WHEN `FakeNetwork` clears Full at step 1 of tick 5 (Shipping's `Take` of 100 L), THEN at 2a of tick 5 the state is Running and the rate sent is 8, with no player request.
- **AC-13 [EditMode]** GIVEN an electric pump Running and `FakeSource` setting drained during step 2b of tick 1, WHEN tick 2 runs, THEN at 2a of tick 2 (not tick 1) the state is Dry and the rate sent is 0; WHEN the intake is rebound to a source with 1,000 L at step 1 of tick 5, THEN the state is Running at 2a of tick 5.
- **AC-14 [EditMode]** GIVEN two electric pumps Running into one pool that becomes Full in tick 1, THEN both are Stalled at 2a of tick 2; WHEN Full clears at step 1 of tick 3 (a `Take`), THEN both are Running at 2a of tick 3.
- **AC-15 [EditMode]** GIVEN a fitted motor whose cable port is 30.0 m from free socket 1 and player 0 1.0 m from the socket, WHEN `PlugCable` applies, THEN it is accepted; AT 30.01 m, THEN `RequestRejected(too_far)`; GIVEN socket 1 already holds a cable, THEN `RequestRejected(socket_in_use)`; GIVEN a hand pump without motor, THEN `RequestRejected(no_motor)`.
- **AC-16 [EditMode]** GIVEN an electric pump with `leverOn` true, WHEN `PickUp` is requested, THEN `RequestRejected(switch_off_first)`. GIVEN `leverOn` false and the cable in socket 0, WHEN `PickUp` applies, THEN Placement calls `Network.DetachAll` once, and at 2a the state is Carried and `cableSocket` is null.
- **AC-17 [EditMode]** GIVEN the hand pump granted with its crate Landed, WHEN 2a runs, THEN the state is Crated and `CrankStart` on its id is rejected with `not_placed`; WHEN `OpenCrate` applies, THEN at the next 2a the state is Placed at the crate pose.
- **AC-18 [EditMode]** GIVEN a save of one motored hand pump (`leverOn` true, socket 0, connected) and the second pump (`leverOn` false, socket 1, connected), WHEN it is written, THEN each pump record has exactly the fields `id, kind, motorFitted, leverOn, cableSocket, cableEndPose` and no crank flag; WHEN it is loaded, THEN at the first 2a the first pump is Running and the second is Ready, and no `PumpStateChanged` is raised.
- **AC-19 [EditMode]** GIVEN a connected electric pump E (cable in socket 0, `leverOn` false) and a connected hand pump H, both Ready with no event at the first 2a, and this fixed 600-tick script: tick 10 `SetSwitch(E, on)`; `FakeNetwork` sets E's pool Full at 2b of tick 20; Full cleared at step 1 of tick 30; tick 40 `UnplugCable(E)`; tick 50 `PlugCable(E, 0)`; tick 60 `SetSwitch(E, off)`; tick 70 `SetSwitch(E, off)` again; tick 100 `CrankStart(H)` with the eye point 1.0 m from the handle; tick 200 `CrankStop(H)`; tick 300 `CrankStart(H)`; tick 310 the recorded eye point moves to 1.6 m. WHEN all 600 ticks run, THEN exactly 10 `PumpStateChanged` events are raised: E Ready→Running (10), Running→Stalled (21), Stalled→Running (30), Running→Placed (40), Placed→Running (50), Running→Ready (60); H Ready→Running (100), Running→Ready (200), Ready→Running (300), Running→Ready (310); none at tick 70.
- **AC-20 [PlayMode]** GIVEN an electric pump Ready, WHEN the lever is thrown On, THEN the spin-up sound plays in the same frame as the press, and the pool contents are 0.8 L higher after the next tick.
- **AC-21 [PlayMode]** GIVEN a hand pump that is not connected, WHEN the player cranks it for 1 s, THEN the handle animates and the pool contents do not change.
- **AC-22 [Manual]** GIVEN a playtester at the hand-pump stage, WHEN duty is read from the play log (time the hand pump is Running, from its `PumpStateChanged` rows, ÷ the time from its first Ready to `Purchased(electric_motor)` minus 3 s per launch in that span), THEN duty is between 0.5 and 0.9; if not, re-run `sim3.py` with the measured duty and re-check Economy AC-33. Task: `sim3.py` needs a `duty=` argument for this re-run (it has none yet).
- **AC-23 [Manual]** GIVEN the first electric switch-on in a session, WHEN the debug distance readout is watched, THEN the player is more than 5 m from the pump within 30 s of the lever going On (the "hands free" moment reads).
- **AC-24 [EditMode]** GIVEN player 0 holding a fitted motor's cable end 1.0 m from free socket 1, WHEN `PlugCable` applies, THEN `cableSocket` = 1, `FakePlacement` records `SetHands(0, null)` and the cable end's pose is socket 1's pose. GIVEN that plugged cable and player 0 1.0 m from the socket with empty hands, WHEN `UnplugCable` applies, THEN `cableSocket` is null and `FakePlacement` records `SetHands(0, CableEnd(pumpId))`; WITH player 0 carrying a hose end, THEN `RequestRejected(hands_full)` and nothing changes; WITH the eye point 2.01 m from the socket, THEN `RequestRejected(too_far)`; GIVEN no cable plugged, THEN `RequestRejected(not_plugged)` (checked first). GIVEN player 0 1.0 m from free socket 1 with empty hands, or holding the other pump's cable end, WHEN `PlugCable(0, pump, 1)` is sent, THEN `RequestRejected(not_holding)` and nothing changes; with the hand pump unmotored as well, still `not_holding` (checked first).

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02; revised after the 2026-11-02 batch design review).** Pillar 1 (Start Tiny, End Enormous): from cranking to switching is a clear step up. Pillar 2 (Hands on the Machinery): every machine is placed, plumbed, bolted and wired by hand, and hold-to-crank stays the default. The Toggle setting (Nour, D2) protects players' hands without changing the default fantasy. No failure states, matching the systems index. Watch item: the hand pump uses hold-to-crank rather than rhythm input; if it feels passive in playtest, consider a timed stroke.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Hold-to-crank vs rhythmic strokes for the hand pump | game-designer | First MVP playtest |
| Where electricity comes from at later sites (generators, fuel as a chore) | systems-designer | Regions / Alpha |
| Should the second pump be bought as a motor kit for a second hand pump instead (cheaper art)? | producer | Before MVP art |
