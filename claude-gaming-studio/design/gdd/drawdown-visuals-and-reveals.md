# Drawdown Visuals & Reveals

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 3 (The World Shows the Damage)

## Summary

Drawdown Visuals & Reveals turns the water the player has taken into what they see: the water plane drops with the remaining volume, each threshold leaves a stain band on the basin walls, and at set depths the falling water uncovers authored objects (a dock, a "Swim Safe" sign, a shopping cart). It raises `DrawdownThreshold` and `Reveal`, which the radio celebrates. It is the progress bar of the game, drawn on the landscape.

> **Quick reference** — Layer: `Presentation + gameplay events` · Priority: `MVP` · Key deps: `Water Source Model`, `Game State`

## Overview

Each tick, right after the Water Source step, this system reads every source's drawdown percentage, checks it against that source's threshold list, and raises events for thresholds crossed. Every frame, it sets each water plane's height from a volume-to-height curve baked from the basin mesh, interpolated with Game State's `alpha`. A stain band is painted at the waterline height of each passed threshold (a world-height shader; flat decals as fallback). Reveals are authored objects placed in the basin at chosen depths; a reveal "happens" when the water drops below the object's reveal height, with a one-time beat (radio line, sting, a short wet-shine on the object).

## Player Fantasy

**"I did that."** You look up from the pump and the shoreline has stepped back past your boots. Rings mark every stage of your work like a bathtub ring. Then the water lets go of something it was hiding, a rotting dock with a sign that says "Swim Safe", and the radio calls it a heritage asset.

- **Anchor moment:** the 10% band: a clear ring on the wall, the dock's posts breaking the surface, and a jingle.
- **Tone:** quiet and uncanny (the Bathtub Ring direction); the satire is supplied by the radio, never by this system.

## Detailed Design

### Core Rules

1. **Water plane:** each source has a flat water plane. Its height is `height = curve(remaining)`, a monotonic curve baked from the basin mesh at edit time (litres → metres). Rendered height = lerp(previous tick height, current tick height, `alpha`), so it never steps.
2. **Thresholds (farm pond, MVP):** 1%, 2.5%, 5%, then every 10% to 100%. The puddle has none (it is a hand-scale source; it just drops and dries). Threshold lists are data (Economy table, `source` rows).
3. **Threshold events:** at tick step 2e, for each threshold newly passed, this system raises `DrawdownThreshold(source, pct)`, in ascending order, and records it as passed. Thresholds never un-pass (water never returns).
4. **Stain bands:** each passed threshold paints a band at that threshold's waterline height: a darker, slightly greenish line 3–6 cm tall on all wet surfaces, plus a dry-mud tint above the current waterline. Implementation: one world-height stain-band shader reading a list of band heights (3-day spike; fallback: flat decal strips placed at the baked heights).
5. **Basin requirements (level asset), as outcomes:** graybox the pond (about 6 × 5 m, 1 m deep), bake the curve, then shape slopes so that at 1% at least 25 cm of mud shows on the intake shelf from the hand-pump handle (slope = 25 cm ÷ the height drop at 1%, about 1:35–1:40), every 10% band exposes at least 0.8 m of shoreline at each named viewpoint (hand-pump handle, raised launch-pad deck, bucket beach; 1:8 beach lips), and a deep central pit holds the last 5–10%. (From the Water Source GDD, Visual/Audio Requirements.)
6. **Reveals:** authored objects in the basin, each with a reveal height. When the water plane drops below it, the system raises `Reveal(id)` once and plays the reveal beat. MVP: 2 reveals, the **dock with a "Swim Safe" sign** at about 10% and a **shopping cart** at about 20%. Objects exist from the start under the water (visible through it if the water is clear enough to tease them).
7. **Lakebed swap:** at 10%, the exposed shelf swaps from a wet-mud material to a cracked, drying mud (one material swap in the MVP).
8. **Wet shine:** newly exposed surfaces look wet and dry out over 60 s (shader parameter by exposure time).
9. **Visible change target:** something visible must change within the first 2 minutes (the puddle dropping with the bucket counts) and then at least every 5 minutes (thresholds, bands, reveals, purchases). The Economy pacing simulator gives a longest gap of about 2.7 minutes for the reference player (about 6.3 minutes for a lazy launcher, flagged as an open question there).

### States and Transitions

Per threshold and reveal: **Pending → Passed** (once, never back). Saved: passed thresholds and triggered reveals.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Drawdown slice (passed thresholds, reveals); `DrawdownThreshold`, `Reveal` | Tick step 2e (after Water Source at 2d); `alpha` |
| Water Source Model | — | `Available`, `Start`, `DrawdownPct`, `SourceDrained` |
| Economy | — | Threshold lists per source |
| Corporate Radio | Events | — |
| Hose/Storage Network | Waterline height (for the intake hose end to follow) | — |
| Save & Load | Slice | Load |

## Formulas

The **water_height** formula is defined as:

`h = curve(remaining)` · `h_render = lerp(h_prev, h, alpha)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| remaining | double (L) | 0–start | Water Source | Litres left |
| curve | baked table | monotonic | level asset | Litres → metres, sampled at ≥ 200 points, linear between |
| alpha | float | 0–1 | Game State | Tick interpolation |

**Output Range:** basin bottom to start height.

The **threshold_check** formula is defined as:

`for t in thresholds where !passed[t] and drawdown_pct ≥ t: raise DrawdownThreshold(t); passed[t] = true`

**Output Range:** 0 or more events per tick, ascending.

## Edge Cases

- **If one tick crosses several thresholds** (a save from an old version, a debug jump): all are raised in ascending order in the same tick (Game State AC-E2).
- **If a threshold and `SourceDrained` happen in the same tick**: `SourceDrained` first (Water Source step), then thresholds.
- **If the stain-band shader spike fails**: use decal strips at baked heights; bands must still read from 10 m.
- **If a reveal object's height is above the starting waterline** (authoring error): it triggers at load; a warning is logged in the editor.
- **If the game loads with thresholds passed**: bands are drawn at once for all passed thresholds; no events re-fire.
- **If the curve is not monotonic** (bake error): the editor bake fails with an error; it never ships.

## Dependencies

**Upstream:** Water Source Model (Hard), Game State (Hard), Economy (Hard, threshold data), level art (Hard, basin and curve bake).
**Downstream:** Corporate Radio (Hard), Audio (Soft), Hose/Storage Network (Soft, intake follows waterline), Save & Load (Hard), Play Log (threshold rows).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Pond thresholds | 1, 2.5, 5, then every 10% | — | Data; early ones needed for real scale |
| Band height | 4 cm | 3–6 cm | Readability |
| Wet-shine dry time | 60 s | 30–120 s | — |
| Reveal heights | ~10% and ~20% | — | Authored per level |
| Curve samples | 200 | ≥ 100 | Bake quality |

## Visual/Audio Requirements

- Bathtub Ring look: pale dry mud above, a crisp dark band per threshold, wet shine on fresh exposure.
- Reveal beat: a low sting, the object's wet shine, a short camera-unforced pause (no cutscene, no camera grab).
- Threshold beat: a soft chime and the band appears at the waterline as it passes.

## Game Feel

- The waterline must visibly creep at electric pump scale (8–16 L/s) when watched from the intake shelf for 30 s.
- No camera takeovers; the player discovers reveals by looking.

## UI Requirements

None. The HUD may show the pond's drained percentage as confirmation only.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 2, 5 | `design/gdd/water-source-model.md` | Threshold and basin requirements |
| Rule 9 | `design/gdd/economy-progression-and-shop.md` | Pacing check and visible-change gaps |
| Rule 3 | `design/gdd/game-state-and-simulation.md` | Tick order, AC-E2 |
| Rule 6 | `design/gdd/corporate-radio.md` | Lines for thresholds and reveals |

## Acceptance Criteria

- **AC-01 [EditMode]** GIVEN the pond at 0.9% drained, WHEN a withdrawal brings it to 1.0%, THEN `DrawdownThreshold(pond, 1)` is raised once.
- **AC-02 [EditMode]** GIVEN 4.9%, WHEN a withdrawal brings it to 10.1%, THEN events 5 and 10 are raised in that order in one tick.
- **AC-03 [EditMode]** GIVEN thresholds 1, 2.5 and 5 passed, WHEN the game is saved and loaded, THEN no threshold event is raised and three bands are drawn.
- **AC-04 [EditMode]** GIVEN a baked curve, THEN it is strictly increasing with remaining volume; a non-monotonic bake fails.
- **AC-05 [EditMode]** GIVEN the water plane drops below the dock's reveal height, THEN `Reveal(dock)` is raised once and never again.
- **AC-06 [PlayMode]** GIVEN an electric pump running, WHEN rendered heights are sampled every frame for 10 s, THEN height never increases and never jumps by more than one tick's change.
- **AC-07 [PlayMode]** GIVEN the 1% threshold passed, THEN at least 20 cm of exposed mud shows on the intake shelf seen from the hand-pump handle viewpoint (design target 25 cm).
- **AC-08 [Manual]** GIVEN an MVP session, THEN no gap between visible changes exceeds 5 minutes (from the play log and Nour's notes).
- **AC-09 [Manual]** GIVEN a player who stepped away for 2 minutes at the 10% band, THEN they can tell the pond dropped from the stain band alone.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02).** This is Pillar 3 as a system: height is history (bands), reveal don't remove (authored objects). Watch item: the stain-band shader is the MVP's largest art-tech risk; the decal fallback must be ready.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Stain-band shader vs decals | technical-artist | 3-day spike |
| Real 1 m-deep pond gives about 0.1 m height per 10% band; confirm the outcome rule (≥ 0.8 m of shoreline exposed per band at each viewpoint) reads in the graybox | level-designer | Graybox |
| More reveals between 30% and 100% (Water Source list) | level-designer | After the MVP |
