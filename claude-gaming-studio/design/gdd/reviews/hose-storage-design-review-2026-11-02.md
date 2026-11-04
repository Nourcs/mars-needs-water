# Design Review: Hose, Storage & Flow Network

> **Date**: 2026-11-02
> **Doc**: `design/gdd/hose-storage-and-flow-network.md`
> **Mode**: full (systems-designer, gameplay-programmer, qa-lead, game-designer; creative-director synthesis)
> **Verdict**: **NEEDS REVISION**

## Summary

The core design holds: hoses laid by hand, space checked before a source is drawn, a proportional split, exact water bookkeeping and no failure states. The problems are bounded contract gaps; none needs a redesign.

## Blocking (7)

| # | Problem | Raised by | Fix |
|---|---|---|---|
| B1 | The second pump has nowhere to plug in. From 10:35 to 16:00 Economy runs two discharge hoses into the drum, but Rule 1 gives the drum one inlet. | Game design, Programmer, QA, Systems | Keep typed sockets; per-tank socket counts in the Economy table. Drum: inlet ×2 (one twin-port fitting, still one FitInlet), link ×1. IBC: inlet ×1, link ×1. Pairs: suction intake→source, discharge outlet→inlet, link↔link. Rejection order: wrong_socket, no_inlet, socket_in_use, too_far. AC-05 names which inlet each pump uses. |
| B2 | Fill and Take order are not deterministic (connection order is never saved). | Programmer, Systems, QA | Water fills the pad tank first, then the linked tank, whichever tank it enters. Take works in reverse. |
| B3 | The flow solve does not clamp space: an overshooting pool gets a negative scale factor; a pool Full by tolerance still draws water (AC-04 fails). | QA | `space_G = Full ? 0 : max(0, cap − contents)`, plus an AC with 0.8000005 L. |
| B4 | Intake-to-source binding is undefined. | Programmer, Systems | Dropping the suction end inside a source's hand-placed intake zone sets `intake.sourceId`. "Connected" means both ends plugged. A drained source yields 0 through Withdraw so Pumps can reach Dry. |
| B5 | SwapStorage can strand water (e.g. 50 L when a 150 L staged container swaps for a 100 L barrel). | Systems | The container on the staging spot is not a network node: no pours or hoses until swapped in (`not_installed`). |
| B6 | Request contract gaps. | Programmer | Issuer on Connect/Disconnect; hose length = straight line between sockets; Placement.PickUp of a node calls `Network.DetachAll(nodeId)` directly. |
| B7 | Tests: no test doubles named; missing ACs. | QA | Test-doubles line as in Water Source; ACs for B1–B5, PoolFull re-arming after Take, pumps resuming after a link into a full pool. |

## Recommended

- Player-facing text for every rejection reason, at least `no_inlet` → "No inlet: screw in the fitting" (route through /ux-design).
- Registry: step 2b, add Pumps; register Take, pool_level, SwapStorage.
- PoolFull pool id = the pad tank's id; on load set the "already full" flag silently.
- Measure the ≤ 18 m layout limit from the farthest valid pump spot.
- Hose-end carry speed 90% in FPC rule 2.
- Tolerance ±1e-9 on summed floats; fixed scripts for AC-09/10; 20.0 m accepted, 20.01 m rejected; objective AC-22 and AC-24; reword AC-07 to "requests 0, remaining unchanged".
- Rebuild pools only when connections change. The HUD always shows the pad pool.
- Use the concept's pillar names: "Hands on the Machinery" and "The World Shows the Damage".

## MVP scope cuts accepted

- Two tanks only (drum plus IBC); general tank trees deferred to Alpha.
- No loop rule: one link hose makes a loop impossible. Delete the `loop` reason; AC-17 becomes an assert.
- The suction hose comes pre-plugged into the pump intake; the player drops the free end in the pond.

## Decisions for Nour

None. Everything is within creative and systems authority and respects the fixed constraints.

## Reviewer claims found wrong or overstated

- "Economy has two pumps into drum (two inlets)": Economy implies two pumps into the drum but never says two inlets. That gap is the bug.
- "Pumps Dry unreachable": overstated; ambiguous rather than impossible. Fix stands.
- "AC-07 conflicts": Withdraw is called once per source and Withdraw(0) is quiet. Ambiguous, moved to Recommended.
- "Requests lack issuer": a notation gap; FPC rule 5 already sends a PlayerId.
