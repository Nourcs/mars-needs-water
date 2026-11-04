# HUD (credits, tank level)

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 3 (The World Shows the Damage; the HUD only confirms)

## Summary

The MVP HUD is deliberately small: credits with progress toward the next item, the pad tank's level, a crosshair with context prompts, radio subtitles and a save indicator. Everything else is shown in the world (gauges on tanks, the waterline, the kiosk, the split-flap Earth board).

> **Quick reference** — Layer: `Presentation` · Priority: `MVP` · Key deps: `Economy`, `Hose/Storage Network`

## Overview

The HUD reads state and listens to events; it never changes anything and never submits a request. It computes no game numbers: every value it shows comes from its owning system and the HUD only formats it. It has five elements: a **credits block** (top left), a **tank bar** (top left, under credits), the **crosshair and prompt** (centre), **subtitles** (bottom) and a **save indicator** (bottom right corner). It can be hidden for screenshots.

## Player Fantasy

You shouldn't notice it. It is a quiet clipboard confirming what the world already shows (Pillar 3).

## Detailed Design

### Core Rules

1. **Credits block:** current credits (`floor`, with thousands separators: "2,380"), and the next item's progress from Economy's `NextItemProgress`: "Hand Pump 250 / 380" with a thin bar. The next item is the one Economy reports as Visible or Affordable (at most one, Economy Rule 6).
   - **Shortfall line (Visible only):** shows Economy's `NextItemProgress.litresShort` (economy-progression-and-shop.md, UI Requirements and AC-36). The HUD does no credit or litre maths of its own; the value already includes the multipliers and the water in the pad pool. Display, from `litresShort` = x:
     - x ≥ 10 L: "ship N L more", N = x rounded up to a whole litre (display rounding only).
     - 0 < x < 10 L: "ship N L more" to one decimal, rounded up (so 0.15 shows "0.2 L"; a 6 cr bucket at 0 cr and an empty pool shows "ship 0.6 L more").
     - x = 0 (≤ 1e-6): "Enough aboard: LAUNCH to afford". This is the cue that sends a new player to launch the spoon water.
   - **Affordable line:** "AFFORDABLE: go to the terminal" with an icon (never colour alone).
2. **Payout pop:** on a tick that raised `LaunchCompleted`, the HUD pops the change in Economy's credits across that tick (credits at the end of the tick minus credits at the end of the previous tick, read from the Economy slice). It never multiplies litres by a rate. The pop reads "+2,000 cr" (whole credits; one decimal below 10 cr, so a 0.6 L spoon launch shows "+6.0 cr"). The displayed number then counts up from the old to the new value in 20 equal steps over 0.8 s, starting on the liftoff tick. The AFFORDABLE line, its icon and Economy's chime cue flip when the **displayed** count crosses the price, not when the state changes underneath.
3. **Tank bar:** the pad pool (the Network pool that contains the tank on the storage spot; Hose Rule 5) with its level and litres ("184 / 200 L"; contents rounded down, `floor(contents + 1e-6)`; one decimal while capacity ≤ 10 L, so the jerrycan reads "0.4 / 10 L"). The pad pool is looked up every frame, so a storage swap or a load needs no event.
   - **FULL** is derived, not event-driven: it shows (text "FULL" plus an icon) whenever the pad pool is Full in Hose terms (contents ≥ capacity − 1e-6 L) and clears as soon as it is below that. `PoolFull` for the pad pool only triggers a pulse (2 pulses at 1 Hz); `PoolFull` for any other pool is ignored.
4. **Crosshair and prompt:** a dot; the context prompt below it, with the bound key or gamepad button glyph ("[E] Scoop"). Prompt text, rejection text, hold durations and bindings come from First-Person Controller & Interaction (FPC owns the binding table and the rejection-text column).
   - **Hold ring:** for a hold action (crank, pry 1 s, bolt 2 s, FitInlet 2 s) a radial ring around the dot fills over the hold duration once the press passes the 0.25 s tap threshold. With the "Hold actions: Toggle" setting on, a fixed hold fills after one tap, and a latched crank shows a closed ring with a lock glyph until it is released.
   - **Rejection:** a rejected request shows FPC's rejection text in place of the prompt for 1.5 s.
   - **Held amount** beside the dot while holding a tool (from Hand Tools): below 1 L capacity in whole mL ("15 / 15 mL", spoon); otherwise one decimal rounded down, `floor((held + 1e-6) × 10) / 10` ("9.9 / 10 L" at 9.96 L; "10.0 / 10 L" at 10.0000005 L). A held amount ≤ 1e-6 L shows "0" mL or "0.0" L.
5. **Subtitles:** radio lines (Corporate Radio), bottom centre, with a radio icon, on a semi-opaque backing box. Default size 28 px at 1080p. Each card stays up for the duration Corporate Radio gives with the line.
6. **Not on the HUD:** the Earth counter (kiosk and board only), pump states, the pond's "drained %" (optional settings toggle, off by default; value from Drawdown), any minimap or objective list.
7. **Scale and readability:** UI scale setting (80–150%). Every HUD text element is at least 18 px at 1080p measured at the lowest scale (80%), so 22.5 px at 100%. All text has a high-contrast outline and meets a 4.5:1 contrast ratio against its panel (including safety orange on navy). Nothing flashes faster than 2 Hz.
8. **Hide HUD:** a key (F1) toggles every element except subtitles (prompts and the hold ring hide too).
9. **Save indicator (for Save & Load):** a small spinning-water-drop icon in the bottom right corner while a save is written; a "Save failed" toast for 3 s when Save & Load reports a failed write. No sound.

### States and Transitions

The credits block follows Economy's shop state for the next item:

| HUD state | Economy condition | Shows | Goes to |
|---|---|---|---|
| Saving up | Next item Visible | Name, `floor(credits)` / price, bar, shortfall line | Affordable (displayed count crosses the price) |
| Affordable | Next item Affordable | Name, credits / price, AFFORDABLE line and icon | Saving up (post-MVP spending); next item (on `Purchased`) |
| All bought | No item Visible or Affordable (the plaque is owned, or cut from the table and the IBC is owned) | "All equipment owned. Keep shipping!" | none |

The Employee of the Year Plaque (100,000 cr) counts as the last next item: after the IBC the block shows "Employee of the Year Plaque 3,456 / 100,000". On `Purchased` the block re-reads `NextItemProgress` and slides to the next item.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Economy | none | Credits (millicredits), next item, price, state, `NextItemProgress.litresShort`; `Purchased` (refresh only) |
| Hose/Storage Network | none | Pad pool contents and capacity, Full state; `PoolFull` (pulse only) |
| Shipping | none | `LaunchCompleted` (pop trigger only; the amount comes from Economy) |
| First-Person Controller & Interaction | none | Prompt text, rejection text, bindings, hold progress, Hold/Toggle setting |
| Hand Tools | none | Held amount and capacity of the tool in hand |
| Corporate Radio | none | Subtitle text and display duration |
| Drawdown | none | Drained % (only when the toggle is on) |
| Save & Load | none | Saving and save-failed signals |

## Formulas

The HUD owns no game formula. The shortfall is Economy's:

`litresShort = max(0, (price − floor(credits)) / (credits_per_litre × region_multiplier × contract_multiplier) − padPoolContents)` (Economy, UI Requirements; tested by Economy AC-36).

Display formulas (HUD only):

- Shortfall text: x ≥ 10 → `ceil(x − 1e-6)` L; 1e-6 < x < 10 → `ceil(x × 10 − 1e-6) / 10` L; x ≤ 1e-6 → "Enough aboard".
- Held amount: `floor((held + 1e-6) × 10) / 10` L, or `floor(held × 1000 + 1e-3)` mL when capacity < 1 L.
- Tank litres: `floor(contents + 1e-6)`, or one decimal rounded down when capacity ≤ 10 L.

**Output Range:** shortfall 0 and up. Examples: hand pump 380, credits 250.7, empty pad pool → litresShort 13.0 → "ship 13 L more". Bucket 6 cr, credits 0, pad pool 0.45 L → 0.15 → "ship 0.2 L more"; pad pool 0.6 L → "Enough aboard: LAUNCH to afford".

## Edge Cases

- **If credits jump past two prices at once** (not possible early by design): the block shows the next item Economy reports.
- **If no pad tank exists** (impossible in the MVP): the tank bar hides.
- **If the pad tank is swapped or a save is loaded:** the bar and FULL are recomputed from state on the next frame; no event is needed.
- **If a subtitle is longer than two lines**: it is split into consecutive cards.
- **If the HUD is hidden**: subtitles still show unless the player turned subtitles off in settings.
- **If a launch lands while the count-up from the previous launch is still running:** the count-up restarts from the currently displayed value to the new total over 0.8 s.

## Dependencies

**Upstream:** Economy, Hose/Storage Network, Shipping, First-Person Controller & Interaction, Hand Tools, Corporate Radio, Drawdown, Save & Load (all Soft reads). **Downstream:** none.

## Tuning Knobs

| Knob | Default | Safe range |
|---|---|---|
| UI scale | 100% | 80–150% |
| Payout count-up | 0.8 s, 20 steps | 0.4–1.5 s |
| Rejection text time | 1.5 s (FPC value) | 1–3 s |
| Subtitles | On | On/Off |
| Subtitle size | 28 px | 24–48 px |
| Show drained % | Off | On/Off |

## Visual/Audio Requirements

- Warm, worn company-clipboard style: cream text on a semi-transparent navy panel, safety orange only for the AFFORDABLE line and interactive prompts (concept UI colours). Orange on navy must pass 4.5:1.
- A soft tick per count-up step (20 per payout); the "AFFORDABLE" chime belongs to Economy, timed to the displayed crossing.

## Game Feel

- Numbers update on the frame after the tick that changes them; the payout count-up starts at liftoff with the roar.

## UI Requirements

This whole GDD is UI. 📌 **UX Flag — HUD:** run `/ux-design` for the HUD, the kiosk screen and prompts before epics.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 1, 2, States | `design/gdd/economy-progression-and-shop.md` | `NextItemProgress`, `litresShort` (UI Requirements, AC-36), shop states, plaque |
| Rule 3 | `design/gdd/hose-storage-and-flow-network.md` | Pad pool, Full state, `PoolFull` |
| Rule 4 | `design/gdd/first-person-controller-and-interaction.md` | Binding table, rejection text, hold and Toggle |
| Rule 4 | `design/gdd/hand-tools.md` | Held amount |
| Rule 5 | `design/gdd/corporate-radio.md` | Subtitles and durations |
| Rule 9 | `design/gdd/save-and-load.md` | Save indicator and toast |
| Overview | `design/gdd/game-state-and-simulation.md` | Read-only presentation (Rule 5b) |

## Acceptance Criteria

Test doubles: `FakeEconomy`, `FakeNetwork`, `FakeHandTools`, `FakeInteraction`, `StubEventBus`, `FakeGameState` (a fixed slice snapshot), `RecordingRequestQueue` (records every submitted request), `FakeInput`. Frame time 1/60 s unless stated.

- **AC-01 [EditMode]** GIVEN `FakeEconomy` with credits 250,700 mc, the hand pump Visible at 380 and `litresShort` 13.0, WHEN the credits block updates, THEN it reads "Hand Pump 250 / 380" and "ship 13 L more".
- **AC-02 [EditMode]** GIVEN `FakeEconomy` with the bucket Visible at 6 cr, WHEN `litresShort` is 0.6, then 0.15, then 0, THEN the line reads "ship 0.6 L more", then "ship 0.2 L more", then "Enough aboard: LAUNCH to afford".
- **AC-03 [EditMode]** GIVEN `FakeEconomy` with the next item Affordable and no count-up running, WHEN the block updates, THEN the AFFORDABLE line shows with its icon and no shortfall line shows.
- **AC-04 [PlayMode]** GIVEN `FakeEconomy` credits 300 cr with the hand pump at 380, WHEN `StubEventBus` raises `LaunchCompleted(200)` and the credits become 2,300 cr in the same tick, THEN "+2,000 cr" pops, the displayed count reaches 2,300 at 0.8 s ± 1 frame after the liftoff tick, and the AFFORDABLE line appears on the first frame the displayed count is ≥ 380, not before.
- **AC-05 [PlayMode]** GIVEN `FakeEconomy` with a contract multiplier of 1.5 and credits rising by 1,500 cr in a tick that raised `LaunchCompleted(100)`, WHEN the pop shows, THEN it reads "+1,500 cr" (the credit change, not litres × 10).
- **AC-06 [EditMode]** GIVEN `FakeNetwork` with the pad pool at 199.9999995 / 200 L and no `PoolFull` event raised, WHEN the bar updates, THEN it reads "200 / 200 L" with FULL and its icon; WHEN the contents drop to 100 L, THEN FULL clears on the next frame; WHEN `PoolFull` is raised for a different pool, THEN no pulse plays.
- **AC-07 [EditMode]** GIVEN `FakeNetwork` with the jerrycan (10 L) on the storage spot holding 0.45 L, WHEN the storage tank is swapped for the barrel holding 0.45 L, THEN the bar reads "0.4 / 10 L" before and "0 / 100 L" after, with no event raised.
- **AC-08 [EditMode]** GIVEN `FakeHandTools`, WHEN the tool in hand is the spoon holding 0.015 L, then the bucket holding 9.96 L, then the bucket holding 10.0000005 L, THEN the held amount reads "15 / 15 mL", "9.9 / 10 L", "10.0 / 10 L".
- **AC-09 [EditMode]** GIVEN `FakeEconomy` after the IBC is owned with credits 3,456 cr, WHEN the block updates, THEN it reads "Employee of the Year Plaque 3,456 / 100,000"; WHEN `Purchased(plaque)` is raised and no item is Visible or Affordable, THEN it reads "All equipment owned. Keep shipping!".
- **AC-10 [EditMode]** GIVEN `FakeGameState` and a `RecordingRequestQueue`, WHEN the HUD updates for 100 frames including one `LaunchCompleted`, one `PoolFull` and one `Purchased` on `StubEventBus`, THEN the queue holds 0 requests and the slice hash is unchanged.
- **AC-11 [PlayMode]** GIVEN the HUD visible and a subtitle playing, WHEN `FakeInput` presses F1, THEN the credits block, tank bar, crosshair, prompt and hold ring hide and the subtitle stays; WHEN subtitles are turned off in settings, THEN the subtitle hides too.
- **AC-12 [PlayMode]** GIVEN `FakeInteraction` reporting a 1 s pry hold, WHEN the press passes 0.25 s and is held to 1.0 s, THEN the ring fills from 0 to full over 0.75 s ± 1 frame; GIVEN a rejection with text "Too far", THEN it replaces the prompt for 1.5 s ± 1 frame.
- **AC-13 [Manual]** GIVEN 1080p at 80% UI scale, WHEN Nour measures every HUD text element in a screenshot, THEN each is ≥ 18 px tall, subtitles are ≥ 28 px at 100%, every text and panel pair passes 4.5:1 in a contrast checker, and Affordable and FULL each carry an icon.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02; design review 2026-11-02 found NEEDS REVISION, revised the same day).** Pillar 3 ("The World Shows the Damage"): the HUD is minimal, reads only, and the Earth counter stays in the world. Watch item: keep "drained %" off by default so the waterline does the talking.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Does the HUD need the next item at all, or should the kiosk alone carry it? | ux-designer | First MVP playtest |
