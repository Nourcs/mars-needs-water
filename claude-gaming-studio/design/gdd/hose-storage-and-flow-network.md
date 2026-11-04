# Hose, Storage & Flow Network

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 2 (Hands on the Machinery); Pillar 3 (The World Shows the Damage)

## Summary

The Hose, Storage & Flow Network is the plumbing. Tanks and pumps have sockets; the player carries hoses and plugs them in by hand; each tick the network works out how much water every running pump actually moves (limited by pump rate, hose capacity, tank space and water left) and puts it in the tanks. It also handles hand pours into tanks. Full tanks stop pumps safely; nothing ever breaks.

> **Quick reference** — Layer: `Core gameplay` · Priority: `MVP` · Key deps: `Game State & Simulation`, `Water Source Model`, `Economy`

## Overview

This system owns three things:

- **Connections:** typed sockets on pumps and tanks, intake zones on sources, and the hoses the player lays between them. Laying a hose is a physical act: pick up a coil, plug one end, walk, plug the other end.
- **Storage:** every installed tank's contents. In the MVP at most two tanks (the drum on the storage spot and the IBC) can be linked into one **pool**, which fills in a fixed order: the pad tank first.
- **The flow solve:** at tick step 2b, every running pump's demand is limited by its hose, the space left in its pool and the water left in its source, and the result is moved from source to tanks. This is the only place pumped water moves.

It does not decide whether a pump is running (Pumps & Machines), how much a pump can move (Economy table), how much water a source has (Water Source Model), where things stand (Placement, Game State Rule 12) or when a tank is launched (Shipping & Launch). It is the backbone that connects them, and the source of "blocked" and "full" feedback.

*Framing: data layer plus player-facing hose laying. No existing ADR to cite.*

## Player Fantasy

**"I built this line."** The water moves because you ran the hose: you dragged it from the pond edge across the mud to the drum, clicked the coupling home, and heard the pump note change as water arrived. When you add the IBC, you link it yourself, and watch the drum top up and spill over into the new tank.

- **Anchor moment:** the first hand-pump stroke after laying the hose. A pause, a gurgle, then water drumming into the steel drum.
- **Tone:** practical and tactile (Hands on the Machinery). Couplings clunk, hoses sag, gauges have needles. No glowing pipes, no menus to connect things.
- **Safety, not failure:** a full tank is a satisfying "topped off" moment (the pump's note rises, a float gauge hits the red line), never a spill or a penalty. The World Shows the Damage: the tank level is visible in the world, the HUD only confirms it.

*Shaped by the systems designer under the "Hands on the Machinery" pillar; creative-director pass at CD-GDD-ALIGN below.*

## Detailed Design

### Core Rules

1. **Nodes and typed sockets.** Pumps and installed tanks register as nodes. A socket is addressed as `SocketRef(nodeId, index)` and holds at most one hose end. Socket counts per node live in the Economy table:

   | Node | Sockets |
   |---|---|
   | Hand pump, electric pump | `intake` ×1 (the suction hose, fixed at the factory), `outlet` ×1 |
   | 200 L drum | `inlet` ×2 (one twin-port fitting: one `FitInlet` adds both), `link` ×1 |
   | 1,000 L IBC | `inlet` ×1, `link` ×1 |
   | 10 L jerrycan, 100 L barrel | none (pours only) |

   Each source has one hand-placed **intake zone** (a volume over its water near the shore, authored in the level). Hose kinds fit only these pairs: **suction**: pump `intake` → a source's intake zone; **discharge**: pump `outlet` ↔ tank `inlet`; **link**: tank `link` ↔ tank `link`. Two discharge hoses into the drum (one per pump, from the second pump's purchase onward) use its two inlets.
2. **MVP hoses:** a 2-inch layflat discharge hose (real-world basis) with a flow cap of 10 L/s and a section length of 20 m; a 6 m suction hose; a 5 m link hose. The hand-stage containers take pours only; the 200 L drum is the first tank with a hose inlet, which is why the hand pump requires it (Economy). The drum's twin-port inlet fitting comes loose in its crate and is screwed in by hand (`FitInlet(issuer, tank)`, hold Use 2 s, or a tap with the "Hold actions: Toggle" setting, FPC). Until it is fitted, a connect to either drum inlet is rejected with `no_inlet`. Hoses are not bought in the MVP: each pump crate contains its pump with the suction hose already plugged into the intake, plus one 20 m discharge section; the IBC crate contains one 5 m link hose. Values live in the Economy table.
3. **Laying a hose is physical.** The player picks up a coil or a free hose end, plugs one end in (request `Connect(issuer, hoseId, endIdx, target)`, where `target` is a `SocketRef` or a source's intake zone), walks (the hose pays out as a sagging spline drawn by presentation) and plugs the other end. **Hose length** is the straight line between the two ends' positions (socket positions = node pose + socket offset from data; an intake end's position is its drop point); a connect is accepted when that length is ≤ the hose length (20.0 m accepted, 20.01 m rejected). The player can unplug either end at any time with `Disconnect(issuer, hoseId, endIdx)` (the end goes to their hands); this never spills or loses water. The pump end of a suction hose is fixed and cannot be unplugged.
4. **Intake binding.** The suction hose comes pre-plugged into its pump. When the player drops its free end (or presses Use on the water) inside a source's intake zone, the controller sends `Connect(issuer, hoseId, 1, IntakeZone(sourceId))`; when accepted, the hose end stores `sourceId`. A drop outside every intake zone just leaves the end lying there, unbound. A pump is **connected** when its suction end is bound to a source and its outlet hose is plugged at both ends into a tank inlet. Water left is not part of "connected": a drained source simply grants 0 through `Withdraw`, so Pumps can show Dry. In the MVP the visual hose end follows the retreating waterline on its own (decision below).
5. **Pools and fill order.** Tanks joined by a link hose form one pool; an unlinked tank is a pool of one. A pool's capacity and contents are the sums over its tanks. Water entering a pool (pumped or poured, whichever tank it enters) **fills the pad tank first** (the tank on the storage spot, found by spot through Placement), then the linked tank. A pool without the pad tank (an unlinked IBC) has one tank. `Take` removes water in the reverse order: the linked tank first, then the pad tank. No communicating-vessel physics in the MVP. With one link socket per tank and one link hose, loops cannot exist; the pool builder asserts this in debug builds.
6. **Pool ids and rebuilds.** A pool's id is the pad tank's id when it holds the pad tank, otherwise its lowest tank id. Pools are rebuilt only when connections change (`Connect`, `Disconnect`, `DetachAll`, `SwapStorage`, load), not every tick.
7. **Hoses hold no water** in the MVP (a real 20 m, 2-inch hose holds about 40 L; ignored so that water is always conserved exactly and unplugging never spills).
8. **The flow solve** runs at tick step 2b (after Pumps at 2a, before Shipping at 2c and Water Source at 2d). See Formulas. It reads connections as they stand after this tick's step 1, so a hose plugged or unplugged at step 1 of tick N changes flow at step 2b of tick N. It raises no event per tick; it only changes contents.
9. **Full pool and `PoolFull`.** A pool is Full when contents ≥ capacity − 1e-6 L. Pumps feeding a Full pool move nothing and draw nothing from their source. The pump stays "on" (Pumps decides how it sounds and looks) and resumes by itself as soon as space opens (after a `Take`, or a link to a tank with space). One check runs at the end of step 2b, covering this tick's pours and pumping: `PoolFull(poolId)` is raised for each pool that is Full now and was not Full at the previous check. The flag clears when the pool drops below Full, so the event can fire again after a `Take`. A rebuild (rule 6) or a load sets the flags to the current state silently: hose changes never raise `PoolFull`. No spills, no damage, no failure state.
10. **Hand pours.** `Pour(tool, tank)` is a Hand Tools request applied in tick step 1. Hand Tools calls the Network's pour rule, which returns the litres accepted (`poured = min(held, pool space)`, the rest stays in the tool) or a reason: `pool_full` (pool Full by the 1e-6 L tolerance) or `not_installed` (the target is not a network node). Hand Tools raises the rejection.
11. **Taking water out:** Shipping calls `Take(poolId, amount)`, which removes `min(amount, contents)` from the pool in Take order (rule 5) and returns what it took.
12. **Conservation:** every litre is in exactly one place: a source, a tool, a tank, aboard the rocket, or shipped. Sources + tools + tanks + litres aboard + total shipped stays exact across every flow-solve, pour, Take and swap path (relative error at most 1e-9 per tick).
13. **Installed tanks and the storage swap.** A hand-stage container (barrel or drum) unboxed onto the staging spot is **not installed**: it is not a network node, so pours and hose connects to it are rejected with `not_installed` (fitting the drum's inlet there is allowed). `SwapStorage(issuer, newTank)` (validated at step 1; rejected with `too_far` or `not_on_staging_spot`) moves all water from the current storage tank into the new one, swaps their poses with `Placement.Move` (new onto the storage spot, old onto the staging spot; Game State Rule 12), installs the new tank as a node and removes the old container's node, so it becomes a prop. Because the new container is always empty and larger (100 L barrel, then 200 L drum), the move is always all of the water. The IBC is installed when placed (`PlaceItem`), not swapped. Requested by Economy rule 12.
14. **Picking up a node.** When Placement's `PickUp` takes a pump or a tank, Placement calls `Network.DetachAll(nodeId)` directly in the same step (Game State Rule 12): every hose end in that node's sockets is unplugged and lies at its socket position; the other ends stay where they are. A pump's fixed suction hose goes with the pump and its intake binding is cleared. A tank keeps its contents. Pools are rebuilt.

### Requests and rejection codes

Every request carries its issuer's `PlayerId` (0 in the MVP; Game State Rule 5). Reach is measured from the issuer's eye point, default 2 m (Game State Rule 12; ranges in data). Codes are checked in the order listed and the first failure is returned. One read-only function, `CanConnect(issuer, hoseId, endIdx, target) → reason | ok`, is used by step 1, the hover prompt and the presenter, so the hover reason always matches the result. Player-facing text for each code lives in FPC's binding table (rejection-text column).

| Request | Codes, in check order |
|---|---|
| `Connect(issuer, hoseId, endIdx, target)` | `not_holding` (issuer does not hold that end) → `too_far` (target beyond reach) → `wrong_socket` (pair not allowed by rule 1, or the container has no sockets) → `not_installed` (target tank not installed) → `no_inlet` (drum inlet not fitted) → `socket_in_use` → `hose_too_short` (length rule 3) |
| `Disconnect(issuer, hoseId, endIdx)` | `too_far` → `hands_full` → `fixed_end` (pump end of a suction hose) |
| `FitInlet(issuer, tank)` | `too_far` → `already_fitted` |
| `SwapStorage(issuer, newTank)` | `too_far` → `not_on_staging_spot` |
| Pour rule (called by Hand Tools' `Pour`) | `not_installed` → `pool_full` |

Direct calls (no request, no rejection): `Network.DetachAll(nodeId)` from Placement; `Take(poolId, amount)` from Shipping.

### States and Transitions

**Hose:**

| State | Condition | Can go to |
|---|---|---|
| Coiled | In a crate, on the ground or held, no end connected (link and discharge hoses only) | Laying |
| Laying | One end connected, the other held or lying free | Connected (second end plugged); Coiled (first end unplugged) |
| Connected | Both ends connected (a suction hose: free end bound to a source) | Laying (one end unplugged or `DetachAll`) |

A suction hose is always at least Laying, since its pump end is fixed. A held hose that is dropped with one end connected stays Laying, lying where it fell.

**Pool:**

| State | Condition |
|---|---|
| Empty | Contents = 0 |
| Filling | 0 < contents < capacity − 1e-6 L |
| Full | Contents ≥ capacity − 1e-6 L |

Pool state is derived. Saved slice: `tanks{id, contents, inletFitted, installed}`, `hoses{id, kind, ends[2]{target: SocketRef | IntakeZone(sourceId) | null, restPose}}`. Pools, pool ids, the pad tank and the Full flags are derived on load.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State & Simulation | The network slice; `PoolFull`; `RequestRejected` reasons | Tick step 2b; requests in step 1 |
| Placement (Game State Rule 12) | `SwapStorage` calls `Placement.Move` | Node poses, the storage and staging spots, eye point for reach; `DetachAll(nodeId)` on `PickUp` |
| Water Source Model | One `Withdraw(source, total)` per source per tick | The amount granted |
| Pumps & Machines | Whether each pump is connected (rule 4); actual litres moved (for its sound and gauge) | Pump state (Running or not) and rate |
| Hand Tools | Pour rule result (litres accepted or reason) | Pour requests |
| Economy | — | Hose caps and lengths, socket counts, tank capacities, crate contents; storage swap and inlet fitting (Economy rule 12) |
| Shipping & Launch | `Take(poolId, amount)`, pad pool contents | Take calls |
| HUD, Drawdown, Audio | Pad pool level, `PoolFull`, per-pump flow | — |
| FPC & Interaction | `CanConnect` reasons for hover prompts | `Connect`, `Disconnect`, `FitInlet`, `SwapStorage` requests |
| Save & Load | The slice | Load |

## Formulas

The **flow_solve** formula is defined as (per tick, at step 2b; pumps and sources processed in id order):

```
for each pump p:      d_p = (Running and connected) ? min(rate_p, hoseCap_p) × dt : 0
for each pool G:      space_G = Full(G) ? 0 : max(0, cap_G − contents_G)
                      D_G = Σ d_p feeding G;  k_G = (D_G > 0) ? min(1, space_G / D_G) : 0
                      d'_p = d_p × k_G
for each source s:    D_s = Σ d'_p drawing from s;  got_s = Withdraw(s, D_s)
                      a_p = (D_s > 0) ? d'_p × got_s / D_s : 0   (the last pump on s gets got_s − Σ others)
deposit Σ a_p into each pool in fill order (pad tank first); any overshoot ≤ 1e-6 L goes to the last tank in fill order
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| rate_p | double (L/s) | 0–20 | data file (Economy) | Pump rate while running (hand pump 1.25, electric 8) |
| hoseCap_p | double (L/s) | 1–20 | data file (Economy) | Flow cap of the discharge hose on p (MVP 10) |
| dt | double (s) | 0.033–0.2 | Game State | Tick length (0.1 at 10 Hz) |
| space_G | double (L) | 0 and up | calculated | 0 if G is Full, else capacity minus contents, never negative |
| got_s | double (L) | 0 to D_s + 1e-6 | Water Source `Withdraw` | Water actually granted (may exceed D_s by at most 1e-6 L when it empties the source; Water Source rule 4). A drained source grants 0 |
| a_p | double (L) | 0 to rate_p × dt (+ its share of ≤ 1e-6 L) | calculated | Litres pump p moves this tick. A pool may end up to 1e-6 L over capacity; it counts as Full and `pool_level` is clamped to 1 for display |

**Output Range:** each pump moves 0 to `min(rate, hoseCap) × dt` per tick (0.8 L per tick for an electric pump at 10 Hz). Space is applied before the source so that a Full pool never takes water out of a source; the source split is proportional (Water Source GDD rule 5). The "last pump gets the remainder" rule keeps conservation exact.

The **pour** formula is defined as:

`poured = Full(G) ? 0 (rejected pool_full) : min(max(held, 0), space_G)` · `held -= poured`, deposited in fill order.

**Output Range:** 0 to held. A 10 L bucket into a drum with 4 L of space pours 4 L and keeps 6 L.

The **pool_level** formula is defined as:

`pool_level = min(1, contents_G / capacity_G)`

**Output Range:** 0–1. Drives the in-world float gauge of each tank (per tank: `min(1, contents/capacity)`) and the HUD tank bar, which always shows the pad pool.

**Example (MVP, electric stage):** electric pump 8 L/s → drum (200 L) linked to IBC (1,000 L). Pool space 1,200 L, so the pump runs at 8 L/s for 150 s until full; the drum fills in the first 25 s. With the second electric pump on its own hose into the drum's second inlet, both move 8 L/s and the pool fills in 75 s.

*Formula structure by `systems-designer`; numbers from the Economy GDD.*

## Edge Cases

- **If two pumps feed one pool with less space than both want**: both are scaled by the same factor (proportional), so neither starves.
- **If a pool is Full by tolerance or slightly over capacity**: `space_G` is 0, never negative; pumps feeding it request 0 and the source is unchanged.
- **If a pump's source runs dry mid-tick**: it gets its proportional share of what was left; `SourceDrained` is raised by Water Source at step 2d. From the next tick the pump is still connected, `Withdraw` grants 0, it moves 0 and Pumps shows it Dry. No warnings are logged.
- **If a pump has a bound intake but no outlet hose**: it is not connected, so `d_p` = 0 and it adds nothing to its source's `Withdraw`.
- **If the suction end is dropped outside every intake zone**: it lies unbound; the pump is not connected.
- **If a hose is unplugged while water is flowing**: flow stops at step 2b of the same tick; no water is lost (hoses hold none).
- **If the player links a tank into a pool that is already full**: the pool's space grows by the new tank's space, the rebuild clears the Full flag silently, and pumps move water at step 2b of the same tick.
- **If the player unlinks a tank from a pool**: each tank keeps its own contents; the pool splits into two pools; no `PoolFull` is raised.
- **If a second link hose targets a tank whose link socket is taken**: rejected `socket_in_use`. Loops are impossible in the MVP (rule 5).
- **If a discharge hose end targets an occupied link socket**: `wrong_socket`, since the type check comes before the occupancy check.
- **If a pour or connect targets a container on the staging spot**: rejected `not_installed`; the container takes nothing until it is swapped in, so a swap can never strand water.
- **If a pour targets a tank in a full pool**: rejected with `pool_full`; the tool keeps its water.
- **If a pour is larger than the space**: partial pour (pour formula); the rest stays in the tool.
- **If the player tries to connect beyond hose length**: rejected with `hose_too_short`; the hose end snaps back to the player's hand, and a short "taut" animation plays.
- **If the player plugs a suction hose into a tank inlet**: rejected with `wrong_socket`.
- **If a pump or the IBC is picked up**: `DetachAll` unplugs its sockets; a tank keeps its water and becomes a pool of one; total water is unchanged.
- **If water sits in an unlinked IBC**: it is safe but cannot be shipped; the IBC's hover prompt says it is not linked to the pad (text in FPC's table).
- **If a save happens mid-fill**: contents are saved exactly; flow resumes next tick from the same state.
- **If a save is loaded with a pool Full**: the Full flag is set silently; no `PoolFull` is raised.
- **If a save references a socket or source whose object no longer exists** (patch): that hose end is loaded as unplugged at its saved `restPose`; one warning is logged.
- **If `Take` asks for more than the pool holds**: it returns the contents; the pool is Empty.
- **If floating error leaves a pool at 199.9999999 of 200 L**: it counts as Full (1e-6 L tolerance).

## Dependencies

**Upstream:**
| System | Nature | Interface |
|---|---|---|
| Game State & Simulation (`design/gdd/game-state-and-simulation.md`) | Hard | Slice, tick step 2b, requests, `RequestRejected`, event bus, Placement and `DetachAll` (Rule 12) |
| Water Source Model (`design/gdd/water-source-model.md`) | Hard | `Withdraw` |
| Economy, Progression & Shop (`design/gdd/economy-progression-and-shop.md`) | Hard | Hose caps and lengths, socket counts, tank capacities, crate contents |
| First-Person Controller & Interaction (`design/gdd/first-person-controller-and-interaction.md`) | Hard | Pick up, carry and plug hose ends (sends requests); rejection text |
| Pumps & Machines (`design/gdd/pumps-and-machines.md`) | Hard | Pump state and rate |

**Downstream:**
| System | Nature | Interface |
|---|---|---|
| Hand Tools | Hard | Pour rule |
| Shipping & Launch | Hard | `Take`, pad pool contents |
| HUD, Drawdown, Audio | Soft | Pad pool level, `PoolFull`, per-pump flow |
| Logistics Overlay & Flow Gauges (Alpha) | Soft | Graph and per-hose flow |
| Save & Load | Hard | Slice |

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Discharge hose cap | 10 L/s | 8–15 L/s | Real 2-inch layflat; must stay ≥ the electric pump rate |
| Discharge hose length | 20 m | 15–30 m | Real section lengths; sets how far tanks can be from the pumps |
| Suction hose length | 6 m | 4–8 m | Real suction hoses |
| Link hose length | 5 m | 3–8 m | Comes with the IBC |
| Drum inlets / IBC inlets | 2 / 1 | — | Economy table; drum needs 2 for the second pump |
| Full tolerance | 1e-6 L | — | Not for designers |
| Level layout: farthest valid pump spot to the drum inlet on the storage spot | ≤ 18 m | — | Level design keeps every valid pump spot (up to 6 m from water) within one discharge section of the drum, with 2 m slack |

Tank capacities live in the Economy table (10 L jerrycan, 100 L barrel, 200 L drum, 1,000 L IBC).

## Visual/Audio Requirements

- Hoses are sagging splines with chunky orange-banded couplings (safety orange = interactive). The spline runs through points along the walked path, snapped to the ground; no hose physics. Plugging in gives a heavy clunk and a small hose twitch.
- Each tank has a sight tube or float gauge readable from 3 m; the water level inside follows that tank's level (interpolated with `alpha`).
- Flow in a hose is shown by a gentle pulse along the layflat and a hiss/gurgle at the tank inlet that scales with flow.
- Full: the float gauge needle hits a red band, the inlet gurgle stops, a soft "topped off" clank.
- Rejected connect: a dull thunk and the hose end springs back; no error popup.

## Game Feel

- From click to "plugged in" sound: same frame (presentation responds before the tick, per the Game State latency rule).
- Laying a 20 m hose should take about 5–8 s of walking and feel like dragging something heavy but not tiring: carrying a hose end slows walking to 90% (FPC rule 2).
- First water at the tank after the pump starts: within 0.5 s of the first stroke or switch-on.

## UI Requirements

- Hover prompt on sockets and intake zones: "Plug in", "Unplug", or the reason from `CanConnect`, shown before the click. Text for every code is in FPC's binding table.
- HUD tank bar: the pad pool's level and litres ("184 / 200 L"). Only one bar in the MVP, always the pad pool (HUD Rule 3).

📌 **UX Flag — Hose, Storage & Flow Network:** hover prompts and the HUD tank bar. Cover in `/ux-design` with the HUD.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 8, 14, flow_solve | `design/gdd/game-state-and-simulation.md` | Tick step 2b; requests in step 1; Rule 12 Placement, eye-point reach, `DetachAll`; AC-X1 partial pours |
| Rule 4, flow_solve, Edge Cases | `design/gdd/water-source-model.md` | `Withdraw`, proportional split, residue grant |
| Rules 1, 2, Tuning Knobs | `design/gdd/economy-progression-and-shop.md` | Hose, socket and tank values, crate contents |
| Rules 4, 9 | `design/gdd/pumps-and-machines.md` | Pump state, connected check, Dry and Stalled behaviour |
| Rule 10 | `design/gdd/hand-tools.md` | Pour requests and their rejections |
| Requests table, UI | `design/gdd/first-person-controller-and-interaction.md` | Binding table, rejection text, hose-end carry speed |
| Rules 2, 13 | `design/gdd/economy-progression-and-shop.md` | Storage swap and inlet fitting (Economy rule 12) |
| Rule 11, Rule 12 | `design/gdd/shipping-and-launch.md` | `Take`, litres aboard in conservation |

## Acceptance Criteria

Tags: **[EditMode]**, **[PlayMode]**, **[Manual]** as in the Economy GDD. Unless stated, dt = 0.1 s (10 Hz) and litre comparisons use ±1e-9 L.

Test doubles used below: a **stub Pumps** (fixed `(Running, rate)` per pump at step 2a), a **spy Water Source** (records every `Withdraw(source, amount)` and grants `min(amount, remaining)`, granting the leftover when ≤ 1e-6 L would remain), a **stub Hand Tools** (held litres per tool), a **stub Shipping** (litres aboard and total shipped; calls `Take`), a **fake Placement** (poses, eye point, storage and staging spots), a **fake tuning table** (Economy rows for hoses, sockets and tanks), a **stub save** (serialise and reload the slice), a **bus spy** and a **log spy** (warnings).

**Flow solve**
- **AC-01 [EditMode]** GIVEN stub Pumps with one electric pump (8 L/s) Running, hose cap 10 from the fake tuning table, into an empty 200 L drum, and a spy Water Source pond of 20,000 L, WHEN 10 ticks run, THEN the drum holds 8.0 L and the pond 19,992.0 L.
- **AC-02 [EditMode]** GIVEN the fake tuning table with a pump rate of 12 L/s and hose cap 10 L/s, WHEN 10 ticks run, THEN exactly 10.0 L moved.
- **AC-03 [EditMode]** GIVEN a drum with 0.5 L of space and a pump wanting 0.8 L this tick, WHEN the tick runs, THEN 0.5 L moves, the spy Water Source records one `Withdraw` of 0.5 L, and the bus spy records `PoolFull(drumId)` once.
- **AC-04 [EditMode]** GIVEN a Full pool, WHEN 100 ticks run with the stub pump Running, THEN the spy Water Source records only `Withdraw` amounts of 0, the pond is unchanged, and the bus spy records no `PoolFull`.
- **AC-05 [EditMode]** GIVEN pump 1 on drum inlet 0 and pump 2 on drum inlet 1 (8 L/s each), pool space 1.0 L, WHEN the tick runs, THEN each moves 0.5 L.
- **AC-06 [EditMode]** GIVEN two pumps (8 L/s each) on one source with 1.0 L left and empty pools, WHEN the tick runs, THEN each moves 0.5 L and the spy Water Source records exactly one `Withdraw` for 1.6 L.
- **AC-07 [EditMode]** GIVEN a pump with a bound intake but no outlet hose as the only pump on its source, WHEN the tick runs, THEN it moves 0, the `Withdraw` for that source is 0 L and the source's remaining is unchanged.
- **AC-08 [EditMode]** GIVEN a drum (200 L, pad tank) linked to an IBC (1,000 L), pump into the drum, WHEN 1,000 L is pumped, THEN the drum holds 200 L and the IBC 800 L.
- **AC-09 [EditMode]** GIVEN a fixed 600 s script (actions at multiples of 0.1 s: pump on at 0.0 s, bucket pour of 10 L at 12.3 s, link the IBC at 100.0 s, `Take(pad pool, 150)` into stub Shipping aboard at 200.0 s, shipped at 215.0 s, unlink at 400.0 s, pump off at 590.0 s), WHEN each tick ends, THEN sources + tools + tanks + litres aboard + total shipped is unchanged within relative 1e-9.
- **AC-10 [EditMode]** GIVEN the first 60 s of the AC-09 script run at 10 Hz and at 20 Hz, THEN tank contents match within relative 1e-6.
- **AC-27 [EditMode]** GIVEN space 0.8 L, a spy Water Source holding 0.8000005 L and one pump wanting 0.8 L, WHEN the tick runs, THEN the pool gets 0.8000005 L (the overshoot in the last tank in fill order), is Full, `pool_level` is 1, and over the next 10 ticks the spy records only 0 L withdrawals and the log spy records no warnings.
- **AC-28 [EditMode]** GIVEN a 200 L pool at 199.9999995 L (Full by tolerance) and a pump Running on a 20,000 L pond, WHEN 10 ticks run, THEN the pond is unchanged. GIVEN the pool at 200.0000008 L, WHEN a tick runs, THEN `space_G` is 0 (not negative), the pond is unchanged and the log spy records no warnings.
- **AC-29 [EditMode]** GIVEN an empty IBC linked to the empty pad drum and a pump into the IBC inlet, WHEN 10 ticks at 8 L/s run, THEN the drum holds 8.0 L and the IBC 0 L (pad tank fills first, whichever tank the water enters).
- **AC-30 [EditMode]** GIVEN 600 ticks of steady pumping into a pool that never fills, THEN the bus spy records no Network events.

**Pours and Take**
- **AC-11 [EditMode]** GIVEN stub Hand Tools with a bucket holding 10 L and a drum with 4 L of space, WHEN the pour applies, THEN the drum is Full, the bucket keeps 6 L and the bus spy records `PoolFull` once. *(Extends Game State AC-X1.)*
- **AC-12 [EditMode]** GIVEN a Full pool, WHEN a pour is requested, THEN the pour rule returns `pool_full` and nothing changes. GIVEN a 200 L pool at 199.9999995 L, THEN the same.
- **AC-13 [EditMode]** GIVEN a pool with the pad drum at 200 L and the IBC at 300 L, WHEN `Take(pool, 350)` is called, THEN it returns 350, the IBC holds 0 and the drum 150.
- **AC-14 [EditMode]** GIVEN a pool with 80 L, WHEN `Take(pool, 100)` is called, THEN it returns 80 and the pool is Empty.
- **AC-31 [EditMode]** GIVEN a Full 200 L pool fed by one electric pump, with `PoolFull` already raised once, WHEN stub Shipping calls `Take(pool, 100)` at step 2c of tick 0 and ticks 1 to 125 run, THEN the bus spy records `PoolFull(drumId)` exactly once more, on tick 125.

**Connections**
- **AC-15 [EditMode]** GIVEN an occupied inlet, WHEN a discharge connect targets it, THEN `RequestRejected(0, socket_in_use)`.
- **AC-16 [EditMode]** GIVEN a 20 m discharge hose with one end plugged, WHEN the other end is connected to an inlet 20.0 m away (socket to socket), THEN it is accepted; at 20.01 m, THEN `RequestRejected(0, hose_too_short)`.
- **AC-17 [EditMode]** GIVEN the drum and IBC joined by the one link hose, WHEN a second link hose from the fake tuning table targets the drum's link socket, THEN `RequestRejected(0, socket_in_use)`, and the pool builder's debug loop assert never fires across AC-01 to AC-40.
- **AC-18 [EditMode]** GIVEN a suction hose end, WHEN it is plugged into a tank inlet, THEN `RequestRejected(0, wrong_socket)`.
- **AC-19 [EditMode]** GIVEN a pool of two linked tanks holding 150 L and 300 L, WHEN the link is unplugged, THEN two pools exist with 150 L and 300 L, total water is unchanged and the bus spy records no `PoolFull`.
- **AC-20 [EditMode]** GIVEN a stub save taken mid-fill, WHEN it loads, THEN connections and tank contents match exactly and the next tick moves the same litres as it would have.
- **AC-25 [EditMode]** GIVEN the 10 L jerrycan or 100 L barrel installed, WHEN a hose connect targets it, THEN `RequestRejected(0, wrong_socket)`. GIVEN an installed drum whose inlet is not fitted, THEN `RequestRejected(0, no_inlet)`; after `FitInlet` applies, the same connect succeeds on inlet 0 and a second discharge connect succeeds on inlet 1.
- **AC-26 [EditMode]** GIVEN a barrel holding 37 L on the storage spot and an empty, not installed 200 L drum on the staging spot, WHEN `SwapStorage(0, drum)` applies, THEN the drum holds 37 L on the storage spot and is installed, the barrel holds 0 L on the staging spot and has no network node (a pour into it returns `not_installed`), and total water is unchanged. GIVEN the drum is not on the staging spot, THEN `RequestRejected(0, not_on_staging_spot)`; GIVEN the issuer's eye point 2.1 m from the drum, THEN `RequestRejected(0, too_far)`.
- **AC-32 [EditMode]** GIVEN an IBC with its inlet occupied, WHEN a discharge end targets its occupied link socket, THEN `RequestRejected(0, wrong_socket)` (type before occupancy). GIVEN the order table in Requests, WHEN `CanConnect` and step 1 are run on one case per code, THEN both return the same code.
- **AC-33 [EditMode]** GIVEN a pump whose suction end is held 4.0 m from its intake socket, WHEN it is dropped inside the pond's intake zone, THEN the end stores the pond's id and the pump is connected once its outlet hose is plugged; WHEN dropped outside every intake zone, THEN the end is unbound and the pump is not connected.
- **AC-34 [EditMode]** GIVEN a connected pump on a source drained to 0, WHEN 10 ticks run, THEN the pump is still connected, each `Withdraw` grants 0, it moves 0 and the log spy records no warnings.
- **AC-35 [EditMode]** GIVEN an empty, not installed drum on the staging spot, WHEN a bucket pour or a discharge connect targets it, THEN `not_installed` and nothing changes; WHEN `FitInlet` targets it within 2 m, THEN it applies.
- **AC-36 [EditMode]** GIVEN `FitInlet` from an eye point 2.1 m away, THEN `RequestRejected(0, too_far)`; GIVEN a drum already fitted, THEN `RequestRejected(0, already_fitted)`.
- **AC-37 [EditMode]** GIVEN an electric pump with its suction end bound and a discharge hose plugged into the drum, WHEN fake Placement calls `Network.DetachAll(pumpId)`, THEN the outlet end is unplugged at the outlet's position, the hose's drum end stays plugged, the intake binding is cleared, tank contents are unchanged and the bus spy records no `PoolFull`.
- **AC-38 [EditMode]** GIVEN a Full 200 L drum pool with a pump Running, WHEN an empty IBC is linked at step 1 of tick N, THEN at step 2b of tick N the IBC gains 0.8 L, and the bus spy records no `PoolFull` for the link.
- **AC-39 [EditMode]** GIVEN a pump moving 0.8 L per tick, WHEN its outlet hose is disconnected at step 1 of tick N, THEN tick N moves 0 for that pump and total water is unchanged.
- **AC-40 [EditMode]** GIVEN a stub save of a Full pool, WHEN it loads, THEN the bus spy records no `PoolFull`. GIVEN a stub save whose hose end references a missing node, WHEN it loads, THEN the end is unplugged at its saved `restPose` and the log spy records exactly one warning.

**Presentation**
- **AC-21 [PlayMode]** GIVEN the player holds a hose end at a free socket 1.8 m from the eye point, WHEN they click, THEN the plug sound plays in the same frame and the hose is Connected after the next tick.
- **AC-22 [PlayMode]** GIVEN the hand pump cranked, or the electric pump switched on, into an empty drum, WHEN 0.5 s passes, THEN the drum's gauge level is above 0 (for each pump).
- **AC-23 [Manual]** GIVEN a fresh MVP level, WHEN the player lays the first discharge hose from the farthest valid pump spot to the drum, THEN it reaches with one section and takes 5–8 s of walking.
- **AC-24 [Manual]** GIVEN a Full pool, WHEN a 1080p screenshot is taken from 3 m in front of the tank, THEN the gauge needle sits inside the red band and the red band is at least 20 px tall.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check against the pillars, 2026-11-02; revised after the 2026-11-02 design review, which found NEEDS REVISION).** Hands on the Machinery: every connection is laid by hand, the drum's inlet is screwed in, and adding a tank is a physical link. The World Shows the Damage: tank level is visible on the tank itself; the HUD bar only confirms it. No failure states, consistent with "full tanks stop pumps safely" in the systems index. Watch item: intake auto-following the waterline (rule 4) trades a hands-on chore for less friction; revisit if the pump stage feels passive. Scope cuts accepted in review: two tanks per pool at most, no loop rule, suction hose pre-plugged at the pump.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Should the player have to move the pump intake as the shoreline retreats (a hands-on chore) instead of it following on its own? MVP: follows on its own | game-designer | First MVP playtest |
| Communicating-vessel levelling between linked tanks (real physics) vs ordered fill. MVP: ordered fill | systems-designer | Vertical Slice |
| General tank trees (more than two tanks per pool) | systems-designer | Alpha |
| Hose water volume (about 2 L per metre for 2-inch hose) for realism at larger scales | economy-designer | Regions / Alpha |
| Buyable extra hose sections and longer runs | economy-designer | When levels need more than 20 m |
