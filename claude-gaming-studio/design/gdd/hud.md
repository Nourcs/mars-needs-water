# HUD (credits, tank level)

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 3 (The World Shows Progress; the HUD only confirms)

## Summary

The MVP HUD is deliberately small: credits with progress toward the next item, the pad tank's level, a crosshair with context prompts, and radio subtitles. Everything else is shown in the world (gauges on tanks, the waterline, the kiosk, the split-flap Earth board).

> **Quick reference** — Layer: `Presentation` · Priority: `MVP` · Key deps: `Economy`, `Hose/Storage Network`

## Overview

The HUD reads state and listens to events; it never changes anything. It has four elements: a **credits block** (top left), a **tank bar** (top left, under credits), the **crosshair and prompt** (centre), and **subtitles** (bottom). It can be hidden entirely for screenshots.

## Player Fantasy

You shouldn't notice it. It is a quiet clipboard confirming what the world already shows (Pillar 3).

## Detailed Design

### Core Rules

1. **Credits block:** current credits (`floor`), and the next item's progress: "Hand Pump 250 / 380" with a thin bar. When short, a second line gives the gap in litres to ship ("ship 13 L more"). When affordable, the line reads "AFFORDABLE: go to the terminal" with an icon (not colour alone).
2. **Payout pop:** on `LaunchCompleted`, "+2,000 cr" rises from the credits block and the number counts up over 0.8 s.
3. **Tank bar:** the pad pool's level and litres ("184 / 200 L"). Flashes "FULL" with an icon when full.
4. **Crosshair and prompt:** a dot; the context prompt below it (from Interaction); held amount beside it when holding a tool ("7.0 / 10 L").
5. **Subtitles:** radio lines (Corporate Radio), bottom centre, with a radio icon.
6. **Not on the HUD:** the Earth counter (kiosk and board only), pump states, the pond's percentage (optional toggle in settings, off by default), any minimap or objective list.
7. **Scale and readability:** UI scale setting (80–150%); minimum text 18 px at 1080p; high-contrast outline on all text.
8. **Hide HUD:** a key (F1) toggles everything except subtitles.

### States and Transitions

Credits block: **Saving up / Affordable / All bought** (after the last MVP item: "All equipment owned. Keep shipping!").

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Economy | — | Credits, next item, price, prerequisite |
| Hose/Storage Network | — | Pad pool level; `PoolFull` |
| Shipping | — | `LaunchCompleted` |
| Interaction, Hand Tools | — | Prompt text, held amount |
| Corporate Radio | — | Subtitle text |

## Formulas

`litres_short = ceil((price − floor(credits)) / credits_per_litre)` (shown when short).

**Output Range:** 0 and up. Example: 380 − 250 = 130 cr → "ship 13 L more".

## Edge Cases

- **If credits jump past two prices at once** (not possible early by design): the block shows the next unowned item.
- **If no pad tank exists** (impossible in the MVP): the tank bar hides.
- **If a subtitle is longer than two lines**: it is split into consecutive cards.
- **If the HUD is hidden**: subtitles still show unless turned off.

## Dependencies

**Upstream:** Economy, Hose/Storage Network, Shipping, Interaction, Hand Tools, Corporate Radio (all Soft reads). **Downstream:** none.

## Tuning Knobs

| Knob | Default | Safe range |
|---|---|---|
| UI scale | 100% | 80–150% |
| Payout count-up | 0.8 s | 0.4–1.5 s |
| Show pond % | Off | On/Off |

## Visual/Audio Requirements

- Warm, worn company-clipboard style: cream text on a semi-transparent navy panel, safety orange only for the AFFORDABLE line and interactive prompts (concept UI colours).
- A soft tick per credit count-up step; the "AFFORDABLE" chime belongs to Economy.

## Game Feel

- Numbers update on the tick that changes them; the payout count-up starts at liftoff with the roar.

## UI Requirements

This whole GDD is UI. 📌 **UX Flag — HUD:** run `/ux-design` for the HUD, the kiosk screen and prompts before epics.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rule 1 | `design/gdd/economy-progression-and-shop.md` | UI requirements, next-item progress |
| Rule 3 | `design/gdd/hose-storage-and-flow-network.md` | Tank bar |
| Rule 5 | `design/gdd/corporate-radio.md` | Subtitles |

## Acceptance Criteria

- **AC-01 [EditMode]** GIVEN credits 250.7 and the hand pump next at 380, THEN the block reads "Hand Pump 250 / 380" and "ship 13 L more".
- **AC-02 [EditMode]** GIVEN credits ≥ price, THEN the AFFORDABLE line shows with its icon.
- **AC-03 [PlayMode]** GIVEN `LaunchCompleted(200)`, THEN "+2,000 cr" pops and the number reaches its final value within 0.8 s.
- **AC-04 [PlayMode]** GIVEN the pad pool fills, THEN the tank bar reads "200 / 200 L" and shows FULL with an icon.
- **AC-05 [PlayMode]** GIVEN F1 pressed, THEN all HUD elements hide except subtitles.
- **AC-06 [Manual]** GIVEN 1080p at 100% scale, THEN all HUD text is readable from a normal sitting distance and nothing relies on colour alone.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02).** Pillar 3: the HUD is minimal and the Earth counter stays in the world. Watch item: keep the pond percentage off by default so the waterline does the talking.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Does the HUD need the next item at all, or should the kiosk alone carry it? | ux-designer | First MVP playtest |
