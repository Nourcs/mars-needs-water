# Hose, Storage & Flow Network

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 2 (Hands-On Craft); Pillar 3 (The World Shows Progress)

## Summary

The Hose, Storage & Flow Network is the plumbing. Tanks and pumps have sockets; the player carries hoses and plugs them in by hand; each tick the network works out how much water every running pump actually moves (limited by pump rate, hose capacity, tank space and water left) and puts it in the tanks. It also handles hand pours into tanks. Full tanks stop pumps safely; nothing ever breaks.

> **Quick reference** — Layer: `Core gameplay` · Priority: `MVP` · Key deps: `Game State & Simulation`, `Water Source Model`, `Economy`

## Overview

This system owns three things:

- **Connections:** sockets on pumps, tanks and (later) the rocket feed, and the hoses the player lays between them. Laying a hose is a physical act: pick up a coil, plug one end, walk, plug the other end.
- **Storage:** every tank's contents. Tanks joined by hoses act as one **pool** that fills in order.
- **The flow solve:** at tick step 2b, every running pump's demand is limited by its hose, the space left in its pool and the water left in its source, and the result is moved from source to tanks. This is the only place pumped water moves.

It does not decide whether a pump is running (Pumps & Machines), how much a pump can move (Economy table), how much water a source has (Water Source Model) or when a tank is launched (Shipping & Launch). It is the backbone that connects them, and the source of "blocked" and "full" feedback.

*Framing: data layer plus player-facing hose laying. No existing ADR to cite.*

## Player Fantasy

**"I built this line."** The water moves because you ran the hose: you dragged it from the pond edge across the mud to the drum, clicked the coupling home, and heard the pump note change as water arrived. When you add the IBC, you link it yourself, and watch the drum top up and spill over into the new tank.

- **Anchor moment:** the first hand-pump stroke after laying the hose. A pause, a gurgle, then water drumming into the steel drum.
- **Tone:** practical and tactile (Pillar 2). Couplings clunk, hoses sag, gauges have needles. No glowing pipes, no menus to connect things.
- **Safety, not failure:** a full tank is a satisfying "topped off" moment (the pump's note rises, a float gauge hits the red line), never a spill or a penalty. Pillar 3: the tank level is visible in the world, the HUD only confirms it.

*Shaped by the systems designer under the CD's "Hands-On Craft" pillar; creative-director pass at CD-GDD-ALIGN below.*

## Detailed Design

### Core Rules

1. **Nodes and sockets.** Pumps, tanks and (later) the rocket feed register as nodes with typed sockets: pump `intake` (into a source), pump `outlet`, tank `inlet`, tank `link`. A socket holds at most one hose end.
2. **One MVP hose type:** a 2-inch layflat discharge hose (real-world basis) with a flow cap of 10 L/s and a section length of 20 m, plus a 6 m suction hose for pump intakes. The hand-stage containers (10 L jerrycan, 100 L barrel) take pours only; the 200 L drum is the first tank with a hose inlet, which is why the hand pump requires it (Economy). The drum's inlet fitting comes loose in its crate and is screwed in by hand (`FitInlet(issuer, tank)`, hold Use 2 s; rejected with `too_far` or `already_fitted`); until it is fitted, a connect to the drum is rejected with `no_inlet`. Hoses are not bought in the MVP: each pump crate contains one suction hose and one 20 m discharge section; the IBC crate contains one 5 m link hose. Values live in the Economy table.
3. **Laying a hose is physical.** The player picks up a coil, plugs one end into a socket (request `Connect(end, socket)`), walks (the hose pays out as a sagging spline) and plugs the other end. A connect request is rejected (`RequestRejected`) if the socket is occupied, the socket types don't match, or the distance exceeds the hose length. The player can unplug either end at any time (`Disconnect(end)`); this never spills or loses water.
4. **Intake placement:** a suction hose's free end is dropped into a source. It counts as submerged while that source has water left; in the MVP the visual hose end follows the retreating waterline on its own (decision below).
5. **Pools.** Tanks joined by link hoses form one pool. A pump outlet or a pour feeds the tank it enters (the *entry tank*); water fills the entry tank first, then flows along links in the order they were connected. No communicating-vessel physics in the MVP. A pool's capacity and contents are the sums over its tanks.
6. **Hoses hold no water** in the MVP (a real 20 m, 2-inch hose holds about 40 L; ignored so that water is always conserved exactly and unplugging never spills).
7. **The flow solve** runs at tick step 2b (after Pumps at 2a, before Shipping at 2c and Water Source at 2d). See Formulas. It raises no event per tick; it only changes contents.
8. **Full pool:** when a pool has no space, pumps feeding it move nothing. The pump stays "on" (Pumps decides how it sounds and looks) and resumes by itself as soon as space opens (for example, after a launch). `PoolFull(pool)` is raised once on reaching full; nothing else happens. No spills, no damage, no failure state.
9. **Hand pours** (spoon, bucket) into a tank or a hand-stage container are requests applied in tick step 1: `poured = min(held, pool space)`. The rest stays in the tool. A pour into a full pool is rejected with reason `pool_full`.
10. **Taking water out:** Shipping calls `Take(pool, amount)`, which removes `min(amount, contents)` from the pool, last-filled tank first, and returns what it took.
11. **Conservation:** every litre is in exactly one place (a source, a tool, a tank, or shipped). Every flow-solve and pour path keeps the total exact (relative error at most 1e-9 per tick).
12. **Storage swap:** when a new hand-stage container (barrel or drum) stands on the staging spot beside the storage spot, `SwapStorage(issuer, newTank)` (validated at step 1; rejected with `not_on_staging_spot` or `too_far`) moves all water from the current storage tank into the new one exactly (`min(contents, space)`, always all of it for an upgrade), swaps their poses with `Placement.Move` (new onto the storage spot, old onto the staging spot; Game State Rule 12), and removes the old container's network node, so it can take no pours or hoses and becomes a prop. Any hose on the old container is unplugged and lies where it was (none in the MVP, since the jerrycan and barrel take no hoses). Requested by Economy rule 12.

### States and Transitions

**Hose:**

| State | Condition | Can go to |
|---|---|---|
| Coiled | In a crate, on the ground or held, no end connected | Laying |
| Laying | One end connected, the other held by the player | Connected (second end plugged); Coiled (first end unplugged) |
| Connected | Both ends connected | Laying (one end unplugged) |

A held hose that is dropped with one end connected stays Laying, lying where it fell.

**Pool:**

| State | Condition |
|---|---|
| Empty | Contents = 0 |
| Filling | 0 < contents < capacity |
| Full | Contents ≥ capacity − 1e-6 L |

Pool state is derived each tick; only tank contents and connections are saved.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State & Simulation | The network slice (connections, tank contents); `PoolFull`; `RequestRejected` reasons | Tick step 2b; requests in step 1 |
| Water Source Model | One `Withdraw(source, total)` per source per tick | `Available`, the amount granted |
| Pumps & Machines | Whether each pump is connected (intake submerged, outlet to a pool); actual litres moved (for its sound and gauge) | Pump state (Running or not) and rate |
| Hand Tools | Pour result (litres accepted) | Pour requests |
| Economy | — | Hose cap and length, tank capacities, crate contents; storage swap and inlet fitting (Economy rule 12) |
| Shipping & Launch | `Take(pool, amount)`, pool contents | Take calls |
| HUD, Drawdown, Audio | Pool level, `PoolFull`, per-pump flow | — |
| Save & Load | The slice | Load |

## Formulas

The **flow_solve** formula is defined as (per tick, at step 2b):

```
for each pump p:      d_p = (Running and connected) ? min(rate_p, hoseCap_p) × dt : 0
for each pool G:      D_G = Σ d_p feeding G;  k_G = (D_G > 0) ? min(1, space_G / D_G) : 0
                      d'_p = d_p × k_G
for each source s:    D_s = Σ d'_p drawing from s;  got_s = Withdraw(s, D_s)
                      a_p = (D_s > 0) ? d'_p × got_s / D_s : 0
deposit a_p into p's pool, entry tank first, then along links in connection order
```

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| rate_p | double (L/s) | 0–20 | data file (Economy) | Pump rate while running (hand pump 1.25, electric 8) |
| hoseCap_p | double (L/s) | 1–20 | data file (Economy) | Flow cap of the discharge hose on p (MVP 10) |
| dt | double (s) | 0.033–0.2 | Game State | Tick length (0.1 at 10 Hz) |
| space_G | double (L) | 0 and up | calculated | Capacity minus contents of pool G |
| got_s | double (L) | 0–D_s | Water Source `Withdraw` | Water actually granted |
| a_p | double (L) | 0 to rate_p × dt | calculated | Litres pump p moves this tick |

**Output Range:** each pump moves 0 to `min(rate, hoseCap) × dt` per tick (0.8 L per tick for an electric pump at 10 Hz). Space is applied before the source so that a full pool never takes water out of a source; the source split is proportional (Water Source GDD rule 5).

The **pour** formula is defined as:

`poured = min(max(held, 0), space_G)` · `held -= poured`

**Output Range:** 0 to held. A 10 L bucket into a drum with 4 L of space pours 4 L and keeps 6 L.

The **pool_level** formula is defined as:

`pool_level = contents_G / capacity_G`

**Output Range:** 0–1. Drives the in-world float gauge and the HUD tank bar.

**Example (MVP, electric stage):** electric pump 8 L/s → drum (200 L) linked to IBC (1,000 L). Pool space 1,200 L, so the pump runs at 8 L/s for 150 s until full. With the second electric pump on its own hose into the same drum, both move 8 L/s and the pool fills in 75 s.

*Formula structure by `systems-designer`; numbers from the Economy GDD.*

## Edge Cases

- **If two pumps feed one pool with less space than both want**: both are scaled by the same factor (proportional), so neither starves.
- **If a pump's source runs dry mid-tick**: it gets its proportional share of what was left; `SourceDrained` is raised by Water Source at step 2d; the pump then moves 0 and Pumps shows it running dry.
- **If a pump has an intake but no outlet hose**: it is not connected, so it moves 0 and draws nothing from the source.
- **If a hose is unplugged while water is flowing**: flow stops on the next tick; no water is lost (hoses hold none).
- **If the player links a tank into a pool that is already full**: the pool's space grows by the new tank's space; pumps resume next tick.
- **If the player unlinks a tank from a pool**: each tank keeps its own contents; the pool splits into two pools.
- **If a link hose would create a loop** (tank A → B → A): the connect is rejected (`loop`). Pools are trees.
- **If a pour targets a tank in a full pool**: rejected with `pool_full`; the tool keeps its water.
- **If a pour is larger than the space**: partial pour (pour formula); the rest stays in the tool.
- **If the player tries to connect beyond hose length**: rejected with `too_far`; the hose end snaps back to the player's hand, and a short "taut" animation plays.
- **If the player plugs a suction hose into a tank inlet**: rejected with `wrong_socket`.
- **If a save happens mid-fill**: contents are saved exactly; flow resumes next tick from the same state.
- **If a save references a socket whose object no longer exists** (patch): the hose end is loaded as unplugged and lies at the old position; one warning is logged.
- **If `Take` asks for more than the pool holds**: it returns the contents; the pool is Empty.
- **If floating error leaves a pool at 199.9999999 of 200 L**: it counts as Full (1e-6 L tolerance).

## Dependencies

**Upstream:**
| System | Nature | Interface |
|---|---|---|
| Game State & Simulation (`design/gdd/game-state-and-simulation.md`) | Hard | Slice, tick step 2b, requests, `RequestRejected`, event bus |
| Water Source Model (`design/gdd/water-source-model.md`) | Hard | `Available`, `Withdraw` |
| Economy, Progression & Shop (`design/gdd/economy-progression-and-shop.md`) | Hard | Hose cap and length, tank capacities, crate contents |
| First-Person Controller & Interaction (not yet designed) | Hard | Pick up, carry and plug hose ends (sends requests) |
| Pumps & Machines (not yet designed) | Hard | Pump state and rate |

**Downstream:**
| System | Nature | Interface |
|---|---|---|
| Hand Tools | Hard | Pour requests and results |
| Shipping & Launch | Hard | `Take`, pool contents |
| HUD, Drawdown, Audio | Soft | Pool level, `PoolFull`, per-pump flow |
| Logistics Overlay & Flow Gauges (Alpha) | Soft | Graph and per-hose flow |
| Save & Load | Hard | Slice |

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Discharge hose cap | 10 L/s | 8–15 L/s | Real 2-inch layflat; must stay ≥ the electric pump rate |
| Discharge hose length | 20 m | 15–30 m | Real section lengths; sets how far tanks can be from the pond |
| Suction hose length | 6 m | 4–8 m | Real suction hoses |
| Link hose length | 5 m | 3–8 m | Comes with the IBC |
| Full tolerance | 1e-6 L | — | Not for designers |
| Level layout: pond edge to storage spot | ≤ 18 m | — | Level design must keep the first drum within one discharge section of the pond |

Tank capacities live in the Economy table (10 L jerrycan, 100 L barrel, 200 L drum, 1,000 L IBC).

## Visual/Audio Requirements

- Hoses are sagging splines with chunky orange-banded couplings (safety orange = interactive). Plugging in gives a heavy clunk and a small hose twitch.
- Each tank has a sight tube or float gauge readable from 3 m; the water level inside follows `pool_level` (interpolated with `alpha`).
- Flow in a hose is shown by a gentle pulse along the layflat and a hiss/gurgle at the tank inlet that scales with flow.
- Full: the float gauge needle hits a red band, the inlet gurgle stops, a soft "topped off" clank.
- Rejected connect: a dull thunk and the hose end springs back; no error popup.

## Game Feel

- From click to "plugged in" sound: same frame (presentation responds before the tick, per the Game State latency rule).
- Laying a 20 m hose should take about 5–8 s of walking and feel like dragging something heavy but not tiring.
- First water at the tank after the pump starts: within 0.5 s of the first stroke or switch-on.

## UI Requirements

- Hover prompt on sockets: "Plug in", "Unplug", or the reason it can't be plugged ("Too far", "Socket in use").
- HUD tank bar: pool level and litres ("184 / 200 L"). Only one bar in the MVP (the pool the player last poured or pumped into).

📌 **UX Flag — Hose, Storage & Flow Network:** hover prompts and the HUD tank bar. Cover in `/ux-design` with the HUD.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rule 7, flow_solve | `design/gdd/game-state-and-simulation.md` | Tick step 2b; requests in step 1; AC-X1 partial pours |
| flow_solve, Edge Cases | `design/gdd/water-source-model.md` | `Withdraw`, proportional split, caller order |
| Rule 2, Tuning Knobs | `design/gdd/economy-progression-and-shop.md` | Hose and tank values, crate contents |
| Rules 4, 8 | `design/gdd/pumps-and-machines.md` | Pump state, running-dry and full-pool behaviour |
| Rule 9 | `design/gdd/hand-tools.md` | Pour requests |
| Rules 2, 12 | `design/gdd/economy-progression-and-shop.md` | Storage swap and inlet fitting (Economy rule 12) |

## Acceptance Criteria

Tags: **[EditMode]**, **[PlayMode]**, **[Manual]** as in the Economy GDD.

**Flow solve**
- **AC-01 [EditMode]** GIVEN one electric pump (8 L/s, hose cap 10) Running into an empty 200 L drum from a 20,000 L pond, WHEN 10 ticks run at 10 Hz, THEN the drum holds 8 L and the pond 19,992 L.
- **AC-02 [EditMode]** GIVEN a pump rate of 12 L/s and hose cap 10 L/s, WHEN 10 ticks run, THEN exactly 10 L moved.
- **AC-03 [EditMode]** GIVEN a drum with 0.5 L of space and a pump wanting 0.8 L this tick, WHEN the tick runs, THEN 0.5 L moves, the source loses exactly 0.5 L, and `PoolFull` is raised once.
- **AC-04 [EditMode]** GIVEN a full pool, WHEN 100 ticks run with the pump Running, THEN the source volume is unchanged and `PoolFull` is not raised again.
- **AC-05 [EditMode]** GIVEN two pumps (8 L/s each) into one pool with 1.0 L of space, WHEN the tick runs, THEN each moves 0.5 L.
- **AC-06 [EditMode]** GIVEN two pumps (8 L/s each) on one source with 1.0 L left and empty pools, WHEN the tick runs, THEN each moves 0.5 L and the source records exactly one `Withdraw` for 1.6 L.
- **AC-07 [EditMode]** GIVEN a pump with an intake but no outlet hose, WHEN the tick runs, THEN it moves 0 and no `Withdraw` is made for it.
- **AC-08 [EditMode]** GIVEN a drum (200 L) linked to an IBC (1,000 L), pump into the drum, WHEN 1,000 L is pumped, THEN the drum holds 200 L and the IBC 800 L.
- **AC-09 [EditMode]** GIVEN any scripted 600 s of pumping, pouring and taking, WHEN each tick ends, THEN sources + tools + tanks + shipped total is unchanged within relative 1e-9.
- **AC-10 [EditMode]** GIVEN the same scripted 60 s at 10 Hz and at 20 Hz, THEN tank contents match within relative 1e-6.

**Pours and Take**
- **AC-11 [EditMode]** GIVEN a bucket with 10 L and a drum with 4 L of space, WHEN the pour applies, THEN the drum is full, the bucket keeps 6 L and `PoolFull` is raised once. *(Extends Game State AC-X1.)*
- **AC-12 [EditMode]** GIVEN a full pool, WHEN a pour is requested, THEN `RequestRejected(pool_full)` and nothing changes.
- **AC-13 [EditMode]** GIVEN a pool with drum 200 L and IBC 300 L, WHEN `Take(pool, 350)` is called, THEN it returns 350, the IBC holds 0 and the drum 150.
- **AC-14 [EditMode]** GIVEN a pool with 80 L, WHEN `Take(pool, 100)` is called, THEN it returns 80 and the pool is Empty.

**Connections**
- **AC-15 [EditMode]** GIVEN an occupied socket, WHEN a connect request targets it, THEN `RequestRejected(socket_in_use)`.
- **AC-16 [EditMode]** GIVEN a 20 m hose with one end plugged, WHEN the other end is connected to a socket 21 m away, THEN `RequestRejected(too_far)`.
- **AC-17 [EditMode]** GIVEN tanks A–B linked, WHEN a link B→A is requested, THEN `RequestRejected(loop)`.
- **AC-18 [EditMode]** GIVEN a suction hose, WHEN it is plugged into a tank inlet, THEN `RequestRejected(wrong_socket)`.
- **AC-19 [EditMode]** GIVEN a pool of two linked tanks holding 150 L and 300 L, WHEN the link is unplugged, THEN two pools exist with 150 L and 300 L and total water is unchanged.
- **AC-20 [EditMode]** GIVEN a save taken mid-fill, WHEN it loads, THEN connections and tank contents match exactly and the next tick moves the same litres as it would have.

**Presentation**
- **AC-21 [PlayMode]** GIVEN the player holds a hose end at a free socket in range, WHEN they click, THEN the plug sound plays in the same frame and the hose is Connected by the next tick.
- **AC-22 [PlayMode]** GIVEN the pump is started into an empty drum, WHEN 0.5 s passes, THEN the drum gauge shows a rising level.
- **AC-23 [Manual]** GIVEN a fresh MVP level, WHEN the player lays the first discharge hose from the pond to the drum, THEN it reaches with one section and takes 5–8 s of walking.
- **AC-24 [Manual]** GIVEN a full pool, WHEN the player looks at the tank from 3 m, THEN they can tell it is full without the HUD.
- **AC-25 [EditMode]** GIVEN the 10 L jerrycan or 100 L barrel, WHEN a hose connect targets it, THEN `RequestRejected(wrong_socket)` (no hose inlet). GIVEN a drum whose inlet is not fitted, THEN `RequestRejected(no_inlet)`; after `FitInlet` applies, the same connect succeeds.
- **AC-26 [EditMode]** GIVEN a barrel holding 37 L on the storage spot and an empty 200 L drum on the staging spot, WHEN `SwapStorage(drum)` applies, THEN the drum holds 37 L on the storage spot, the barrel holds 0 L on the staging spot and has no network node (a pour into it is rejected), and total water is unchanged. GIVEN the drum is not on the staging spot, THEN `RequestRejected(not_on_staging_spot)`.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check against the pillars, 2026-11-02).** Pillar 2: every connection is laid by hand, and adding a tank is a physical link. Pillar 3: tank level is visible on the tank itself; the HUD bar only confirms it. No failure states, consistent with "full tanks stop pumps safely" in the systems index. Watch item: intake auto-following the waterline (rule 4) trades a hands-on chore for less friction; revisit if the pump stage feels passive.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Should the player have to move the pump intake as the shoreline retreats (a hands-on chore) instead of it following on its own? MVP: follows on its own | game-designer | First MVP playtest |
| Communicating-vessel levelling between linked tanks (real physics) vs ordered fill. MVP: ordered fill | systems-designer | Vertical Slice |
| Hose water volume (about 2 L per metre for 2-inch hose) for realism at larger scales | economy-designer | Regions / Alpha |
| Buyable extra hose sections and longer runs | economy-designer | When levels need more than 20 m |
