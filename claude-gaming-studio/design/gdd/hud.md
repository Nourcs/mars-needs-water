# HUD (credits, tank level)

> **Status**: Approved (2026-11-04)
> **Author**: Nour + agents
> **Last Updated**: 2026-11-04
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
   - **Launching line:** while the Shipping slice is in Countdown (from `LaunchStarted`) and until the payout count-up ends, "Launching..." replaces the shortfall line. The pad water is already aboard and the credits only arrive at liftoff, so `litresShort` would give the wrong cue in that window. The HUD reads the Shipping state from the slice, so the line also shows after a load during a countdown.
   - **Affordable line:** "AFFORDABLE: go to the terminal" with an icon (never colour alone).
2. **Payout pop:** on a tick that raised `LaunchCompleted(litres)`, the HUD pops `Economy.PayoutFor(litres)` (millicredits), Economy's own pure function, so the HUD still never multiplies litres by a rate and a purchase in the same tick does not change the pop. Display: whole credits `floor(PayoutFor(litres) / 1000)` ("+2,000 cr"; a 0.6 L spoon launch shows "+6 cr"), the same rule as FPC's "Launch: pays {N} cr" prompt, so the prompt before a launch and the pop after it always show the same number. The displayed number then counts up from the old to the new credit value in 20 equal steps over 0.8 s, starting on the liftoff tick. The AFFORDABLE line and its icon flip when the **displayed** count crosses the price, not when the state changes underneath, and on that frame the HUD triggers Economy's AFFORDABLE chime cue (Economy owns the asset; only the HUD knows the displayed crossing).
3. **Tank bar:** the pad pool (the Network pool that contains the tank on the storage spot; Hose Rule 5) with its level and litres ("184 / 200 L"; contents rounded down, `floor(contents + 1e-6)`; one decimal while capacity ≤ 10 L, `floor((contents + 1e-6) × 10) / 10`, so the jerrycan reads "0.4 / 10 L" at 0.45 L and "10.0 / 10 L" at 9.9999995 L). The pad pool is looked up every frame, so a storage swap or a load needs no event.
   - **FULL** is derived, not event-driven: it shows (text "FULL" plus an icon) whenever the pad pool is Full in Hose terms (contents ≥ capacity − 1e-6 L) and clears as soon as it is below that. `PoolFull` for the pad pool only triggers a pulse (2 pulses at 1 Hz); `PoolFull` for any other pool is ignored.
4. **Crosshair and prompt:** a dot; the context prompt below it, with the bound key or gamepad button glyph ("[E] Scoop"). Prompt text, rejection text, hold durations and bindings come from First-Person Controller & Interaction (FPC owns the binding table and the rejection-text column).
   - **Prompt styles:** the normal prompt (safety orange); a **predicted rejection** in grey, shown before the press when FPC reports that read-only state already predicts the rejection (FPC Rule 4); and the rejection text after a rejected press.
   - **Hold ring:** for a fixed hold (pry 1 s, bolt 2 s, FitInlet 2 s) a radial ring around the dot appears when the press passes the 0.25 s tap threshold, showing 0 on that frame, and fills to full at the hold duration counted from the press (so a pry ring finishes at 1.0 s and visibly fills over 0.75 s). The crank has no duration: in Hold mode it shows a closed ring while the crank runs, with no lock glyph. With the "Hold actions: Toggle" setting on, a fixed hold fills after one tap, and a latched crank shows a closed ring with a lock glyph until it is released.
   - **Rejection:** a rejected request shows FPC's rejection text in place of the prompt for 1.5 s.
   - **Held amount** beside the dot while holding a tool (from Hand Tools): below 1 L capacity in whole mL ("15 / 15 mL", spoon); otherwise one decimal rounded down, `floor((held + 1e-6) × 10) / 10` ("9.9 / 10 L" at 9.96 L; "10.0 / 10 L" at 10.0000005 L). A held amount ≤ 1e-6 L shows "0" mL or "0.0" L.
   - **Spoonful counter and quips (Hand Tools Rule 12):** while the spoon is in hand, a "Spoonful #N" line beside the held amount, N from Hand Tools. At N = 10, 25 and 40 Hand Tools' one-off quip text shows below the crosshair for 3 s (text from the Hand Tools string table). The HUD only draws them.
5. **Subtitles:** radio lines (Corporate Radio), bottom centre, with a radio icon, on a semi-opaque backing box. Default size 28 px at 1080p. A line that fits on one card stays up for the `display_time` Corporate Radio gives with it. A line split into several cards (longer than two lines) splits `display_time` between the cards in proportion to each card's character count, so the cards together last exactly `display_time`.
6. **Not on the HUD:** the Earth counter (kiosk and board only), pump states, the pond's "drained %" (optional settings toggle, off by default; value from Water Source `DrawdownPct(pond)`), any minimap or objective list.
7. **Scale and readability:** UI scale setting (80–150%). Every HUD text element is at least 18 px at 1080p measured at the lowest scale (80%), so 22.5 px at 100%. All text has a high-contrast outline and meets a 4.5:1 contrast ratio against its panel (including safety orange on navy). Nothing flashes faster than 2 Hz.
8. **Hide HUD:** a key (F1) toggles every element except subtitles and the "Save failed" toast (prompts, the hold ring, the spoonful line and quips hide too).
9. **Save indicator (for Save & Load):** a small spinning-water-drop icon in the bottom right corner while Save & Load's saving flag is set, shown for at least 0.5 s from the frame the flag rises (a synchronous save takes under one frame, so without the minimum the icon would never be seen); a "Save failed" toast for 3 s on `SaveFailed(reason)`. No sound.

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
| Economy | Triggers the AFFORDABLE chime cue at the displayed crossing (presentation only) | Credits (millicredits), next item, price, state, `NextItemProgress.litresShort`, `PayoutFor(litres)`; `Purchased` (refresh only) |
| Hose/Storage Network | none | Pad pool contents and capacity, Full state; `PoolFull` (pulse only) |
| Shipping | none | Shipping state (Countdown drives the "Launching..." line); `LaunchStarted`; `LaunchCompleted(litres)` (pop trigger; the amount comes from `Economy.PayoutFor`) |
| First-Person Controller & Interaction | none | Prompt text, predicted-rejection flag (grey prompt), rejection text, bindings, hold progress, Hold/Toggle setting |
| Hand Tools | none | Held amount and capacity of the tool in hand; spoonful count N and quip text (Rule 12) |
| Corporate Radio | none | Subtitle text and `display_time` |
| Water Source | none | `DrawdownPct(pond)` (only when the drained % toggle is on) |
| Save & Load | none | Saving flag, `SaveFailed(reason)` |

## Formulas

The HUD owns no game formula. The shortfall is Economy's:

`litresShort = max(0, (price − floor(credits)) / (credits_per_litre × region_multiplier × contract_multiplier) − padPoolContents)` (Economy, UI Requirements; tested by Economy AC-36).

Display formulas (HUD only):

- Shortfall text: x ≥ 10 → `ceil(x − 1e-6)` L; 1e-6 < x < 10 → `ceil(x × 10 − 1e-6) / 10` L; x ≤ 1e-6 → "Enough aboard".
- Held amount: `floor((held + 1e-6) × 10) / 10` L, or `floor(held × 1000 + 1e-3)` mL when capacity < 1 L.
- Tank litres: `floor(contents + 1e-6)`, or `floor((contents + 1e-6) × 10) / 10` when capacity ≤ 10 L.
- Payout pop: `floor(Economy.PayoutFor(litres) / 1000)` cr (the same rule as FPC's Launch prompt).
- Split subtitle card time: `display_time × card_chars / line_chars`.

**Output Range:** shortfall 0 and up. Examples: hand pump 380, credits 250.7, empty pad pool → litresShort 13.0 → "ship 13 L more". Bucket 6 cr, credits 0, pad pool 0.45 L → 0.15 → "ship 0.2 L more"; pad pool 0.6 L → "Enough aboard: LAUNCH to afford".

## Edge Cases

- **If credits jump past two prices at once** (not possible early by design): the block shows the next item Economy reports.
- **If no pad tank exists** (impossible in the MVP): the tank bar hides.
- **If the pad tank is swapped or a save is loaded:** the bar and FULL are recomputed from state on the next frame; no event is needed.
- **If a subtitle is longer than two lines**: it is split into consecutive cards that share the line's `display_time` by character count (Rule 5).
- **If a save finishes within one frame:** the drop icon still shows for 0.5 s (Rule 9).
- **If the HUD is hidden**: subtitles still show unless the player turned subtitles off in settings.
- **If a launch lands while the count-up from the previous launch is still running:** the count-up restarts from the currently displayed value to the new total over 0.8 s.

## Dependencies

**Upstream:** Economy, Hose/Storage Network, Shipping, First-Person Controller & Interaction, Hand Tools, Corporate Radio, Water Source, Save & Load (all Soft reads). **Downstream:** none.

## Tuning Knobs

| Knob | Default | Safe range |
|---|---|---|
| UI scale | 100% | 80–150% |
| Payout count-up | 0.8 s, 20 steps | 0.4–1.5 s |
| Rejection text time | 1.5 s (FPC value) | 1–3 s |
| Subtitles | On | On/Off |
| Subtitle size | 28 px | 24–48 px |
| Show drained % | Off | On/Off |
| Save icon minimum time | 0.5 s | 0.3–1.5 s |

## Visual/Audio Requirements

- Warm, worn company-clipboard style: cream text on a semi-transparent navy panel, safety orange only for the AFFORDABLE line and interactive prompts (concept UI colours). Orange on navy must pass 4.5:1.
- A soft tick per count-up step (20 per payout); the "AFFORDABLE" chime asset belongs to Economy, and the HUD triggers it at the displayed crossing.

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
| Rule 4 | `design/gdd/hand-tools.md` | Held amount, spoonful counter and quips (Rule 12) |
| Rule 1 | `design/gdd/shipping-and-launch.md` | Countdown state, `LaunchStarted`, `LaunchCompleted` |
| Rule 6 | `design/gdd/water-source-model.md` | `DrawdownPct` |
| Rule 5 | `design/gdd/corporate-radio.md` | Subtitles and durations |
| Rule 9 | `design/gdd/save-and-load.md` | Save indicator and toast |
| Overview | `design/gdd/game-state-and-simulation.md` | Read-only presentation (Rule 5b) |

## Acceptance Criteria

Test doubles: `FakeEconomy`, `FakeNetwork`, `FakeHandTools`, `FakeInteraction`, `FakeShipping`, `FakeSaveLoad`, `FakeRadio`, `StubEventBus`, `FakeGameState` (a fixed slice snapshot), `RecordingRequestQueue` (records every submitted request), `FakeInput`. Frame time 1/60 s unless stated.

- **AC-01 [EditMode]** GIVEN `FakeEconomy` with credits 250,700 mc, the hand pump Visible at 380 and `litresShort` 13.0, WHEN the credits block updates, THEN it reads "Hand Pump 250 / 380" and "ship 13 L more".
- **AC-02 [EditMode]** GIVEN `FakeEconomy` with the bucket Visible at 6 cr, WHEN `litresShort` is 0.6, then 0.15, then 0, THEN the line reads "ship 0.6 L more", then "ship 0.2 L more", then "Enough aboard: LAUNCH to afford".
- **AC-03 [EditMode]** GIVEN `FakeEconomy` with the next item Affordable and no count-up running, WHEN the block updates, THEN the AFFORDABLE line shows with its icon and no shortfall line shows.
- **AC-04 [PlayMode]** GIVEN `FakeEconomy` credits 300 cr with the hand pump at 380 and `PayoutFor(200)` = 2,000,000 mc, WHEN `StubEventBus` raises `LaunchCompleted(200)` and the credits become 2,300 cr in the same tick, THEN "+2,000 cr" pops, the displayed count reaches 2,300 at 0.8 s ± 1 frame after the liftoff tick, and the AFFORDABLE line appears and the chime cue is triggered once, both on the first frame the displayed count is ≥ 380, not before.
- **AC-05 [PlayMode]** GIVEN `FakeEconomy` with a contract multiplier of 1.5 so `PayoutFor(100)` = 1,500,000 mc, and an `Order` for 380 cr applied in the same tick that raised `LaunchCompleted(100)`, WHEN the pop shows, THEN it reads "+1,500 cr" (`PayoutFor`, not litres × 10 and not the credit change of +1,120 cr).
- **AC-06 [EditMode]** GIVEN `FakeNetwork` with the pad pool at 199.9999995 / 200 L and no `PoolFull` event raised, WHEN the bar updates, THEN it reads "200 / 200 L" with FULL and its icon; WHEN the contents drop to 100 L, THEN FULL clears on the next frame; WHEN `PoolFull` is raised for a different pool, THEN no pulse plays; WHEN `PoolFull` is raised for the pad pool, THEN exactly 2 pulses play at 1 Hz (ending 2.0 s ± 1 frame later).
- **AC-07 [EditMode]** GIVEN `FakeNetwork` with the jerrycan (10 L) on the storage spot holding 0.45 L, WHEN the storage tank is swapped for the barrel holding 0.45 L, THEN the bar reads "0.4 / 10 L" before and "0 / 100 L" after, with no event raised; GIVEN the jerrycan holding 9.9999995 L, THEN it reads "10.0 / 10 L" with FULL.
- **AC-08 [EditMode]** GIVEN `FakeHandTools`, WHEN the tool in hand is the spoon holding 0.015 L, then the bucket holding 9.96 L, then the bucket holding 10.0000005 L, THEN the held amount reads "15 / 15 mL", "9.9 / 10 L", "10.0 / 10 L".
- **AC-09 [EditMode]** GIVEN `FakeEconomy` after the IBC is owned with credits 3,456 cr, WHEN the block updates, THEN it reads "Employee of the Year Plaque 3,456 / 100,000"; WHEN `Purchased(plaque)` is raised and no item is Visible or Affordable, THEN it reads "All equipment owned. Keep shipping!".
- **AC-10 [EditMode]** GIVEN `FakeGameState` and a `RecordingRequestQueue`, WHEN the HUD updates for 100 frames including one `LaunchCompleted`, one `PoolFull` and one `Purchased` on `StubEventBus`, THEN the queue holds 0 requests and the slice hash is unchanged.
- **AC-11 [PlayMode]** GIVEN the HUD visible and a subtitle playing, WHEN `FakeInput` presses F1, THEN the credits block, tank bar, crosshair, prompt, hold ring and spoonful line hide and the subtitle and any "Save failed" toast stay; WHEN subtitles are turned off in settings, THEN the subtitle hides too.
- **AC-12 [PlayMode]** GIVEN `FakeInteraction` reporting a 1 s pry hold, WHEN the press passes 0.25 s and is held to 1.0 s, THEN the ring appears at 0.25 s showing 0 and fills to full at 1.0 s ± 1 frame after the press (over 0.75 s); GIVEN the crank in Hold mode, WHEN it runs, THEN a closed ring without a lock glyph shows; GIVEN a rejection with text "Too far", THEN it replaces the prompt for 1.5 s ± 1 frame; GIVEN `FakeInteraction` reporting a predicted rejection, THEN the prompt shows in grey before any press.
- **AC-13 [Manual]** GIVEN 1080p at 80% UI scale, WHEN Nour checks every HUD text element's font size in the UI asset and captures a screenshot as evidence, THEN each font size is ≥ 18 px at 80% scale, subtitles are ≥ 28 px at 100%, every text and panel pair passes 4.5:1 in a contrast checker, and Affordable and FULL each carry an icon.
- **AC-14 [PlayMode]** GIVEN `FakeEconomy` with the bucket Visible at 6 cr, credits 0 and `litresShort` 0.6, and `FakeShipping` in Ready, WHEN `LaunchStarted(0.6)` is raised and Shipping enters Countdown (the pad pool now 0), THEN the shortfall line reads "Launching..." through the countdown; WHEN `LaunchCompleted(0.6)` follows and the count-up runs, THEN "Launching..." stays until the count-up ends and no "Enough aboard" or "ship N L more" text shows at any frame in between; GIVEN a save loaded with Shipping in Countdown, THEN "Launching..." shows on the first frame.
- **AC-15 [PlayMode]** GIVEN `FakeHandTools` with the spoon in hand and N = 9, WHEN N becomes 10, THEN "Spoonful #10" shows beside the held amount and the Hand Tools quip shows below the crosshair for 3 s ± 1 frame; WHEN F1 is pressed, THEN both hide.
- **AC-16 [PlayMode]** GIVEN `FakeSaveLoad`, WHEN the saving flag rises and falls within one frame, THEN the drop icon shows for 0.5 s ± 1 frame; WHEN `SaveFailed(disk_full)` is raised, THEN "Save failed" shows for 3 s ± 1 frame, no sound plays, and pressing F1 does not hide it.
- **AC-17 [PlayMode]** GIVEN a count-up from 300 to 2,300 cr at 0.4 s with displayed value 1,300, WHEN a second `LaunchCompleted` raises credits to 3,300 cr, THEN the count-up restarts from 1,300 and reaches 3,300 at 0.8 s ± 1 frame after the second liftoff tick, with no backward step in the displayed value.
- **AC-18 [EditMode]** GIVEN `FakeRadio` with a line of `display_time` 9.0 s split into two cards of 120 and 60 characters, WHEN it plays, THEN card 1 shows for 6.0 s and card 2 for 3.0 s (± 1 frame), 9.0 s in total.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02; design review 2026-11-02 found NEEDS REVISION, revised the same day).** Pillar 3 ("The World Shows the Damage"): the HUD is minimal, reads only, and the Earth counter stays in the world. Watch item: keep "drained %" off by default so the waterline does the talking.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Does the HUD need the next item at all, or should the kiosk alone carry it? | ux-designer | First MVP playtest |
