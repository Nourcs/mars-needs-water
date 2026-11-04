# Drawdown Visuals & Reveals

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02 (revised after the 2026-11-02 batch design review)
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Pillar 3 (The World Shows the Damage)

## Summary

Drawdown Visuals & Reveals turns the water the player has taken into what they see: the water plane drops with the remaining volume, each threshold leaves a stain band on the basin walls, and at set depths the falling water uncovers authored objects (a dock with a "Swim Safe" sign, a shopping cart). It owns each source's threshold and reveal data, raises `DrawdownThreshold` and `Reveal`, which the radio celebrates, and is the progress bar of the game, drawn on the landscape.

> **Quick reference** — Layer: `Presentation + gameplay events` · Priority: `MVP` · Key deps: `Water Source Model`, `Game State`

## Overview

Each tick, at step 2e (right after the Water Source step), this system reads every source's remaining litres from the Water Source slice, compares them with that source's threshold and reveal lists, and raises events for anything newly passed. It never reads the rendered water plane. Every frame, it sets each water plane's height from a fraction-to-height curve baked from the basin mesh, interpolated with Game State's `alpha`. A stain band is drawn at the baked waterline height of each passed threshold (a world-height shader; URP decals as fallback). Reveals are authored objects whose reveal height is baked into a drawdown percentage; when that percentage is passed, the reveal fires once with a short beat (radio line, sting, a wet shine on the object). All of this data (curve, thresholds, reveals, band heights) lives in one per-source **Drawdown Profile** asset.

## Player Fantasy

**"I did that."** You look up from the pump and the shoreline has stepped back past your boots. Rings mark every stage of your work like a bathtub ring. Then the water lets go of something it was hiding, a rotting dock with a sign that says "Swim Safe", and the radio calls it a heritage asset.

- **Anchor moment:** the 10% band: a clear ring on the wall, the dock's posts breaking the surface, and a jingle.
- **Tone:** quiet and uncanny (the Bathtub Ring direction); the satire is supplied by the radio, never by this system.

## Detailed Design

### Core Rules

1. **Water plane:** each source has a flat water plane. Its height is `h = curve(remaining / start)`, where `start` is the copy in the Water Source slice and the curve maps fraction remaining (0 to 1) to metres. Because the key is a fraction, the plane sits at the spill height at 0% drained even if the saved start volume differs from the bake. Rendered height = lerp(previous tick height, current tick height, `alpha`), so it never steps. On a new game or a load, `h_prev = h`.
2. **Curve bake (editor tool):** the basin is a mesh (ProBuilder, not Terrain). The bake casts a downward raycast grid at 5 cm spacing over the basin, finds the volume below each height level, and stores at least 200 samples evenly spaced in height from the basin bottom to the spill height as `(fraction remaining, height)` in the source's Drawdown Profile (a ScriptableObject). At runtime the curve is read by binary search on the fraction, linear between samples. The bake writes the basin's total volume, rounded to the litre, into the Economy tuning table's start volume for that source, so new games use the baked figure. A curve that is not strictly increasing fails the bake.
3. **Drawdown Profile (data owner):** this system owns, per source, one Drawdown Profile asset next to its curve: the threshold list (percent), the reveal list (`id`, authored reveal height in metres, baked `revealPct`) and the baked band heights. Farm pond (MVP): thresholds 1%, 2.5%, 5%, then every 10% to 100% (13 thresholds). The puddle's profile has empty lists (a hand-scale source; it just drops and dries).
4. **Threshold and reveal check (tick step 2e):** for each source, a threshold `t` is passed when `remaining ≤ start × (1 − t/100) + ε`, with `ε = 0.001 L`. Litres are compared, never percentages, so start volumes other than 20,000 L cannot miss a threshold to rounding. In one tick the system raises `DrawdownThreshold(source, pct)` for each newly passed threshold in ascending order, then `Reveal(id)` for each newly passed reveal (same litre rule with `revealPct`) in ascending id order, and records each as passed. Nothing un-passes (water never returns). Following Game State Rule 3, events raised at 2e are flushed at step 3 of the same tick, and the new slice values are first seen by requests at step 1 of the next tick.
5. **Stain bands:** each passed threshold shows a band at its baked waterline height: a darker, slightly greenish line plus a dry-mud tint above the current waterline. The band's **width is set along the surface** (default 4 cm, so half-width `w = 2 cm`). In the shader, a fragment is in a band when `|worldY − h_band| ≤ w × √(1 − n.y²)`, where `n` is the world normal, so a band is 4 cm wide whether it lies on a 1:35 shelf or a vertical wall. Build: one Shader Graph Sub Graph used by the basin and reveal materials, with the 13 band heights packed into 4 Vector4 properties set by the bake (unused slots −1000). A band is drawn only where its height is above the current water height, so no list changes at runtime and a load draws every passed band at once. The 1%, 2.5% and 5% bands sit about 7, 17 and 33 mm below the start level; with along-surface width they stay separate (about 25 cm apart on the 1:35 shelf) instead of merging. Fallback (if the spike fails): one URP Decal Projector per band, pointing down, at the baked height, with the strip 4 cm wide along the surface; this needs the URP Decal renderer feature with the Screen Space technique.
6. **Basin requirements (level asset), as outcomes:** graybox the pond (about 6 × 5 m at the surface, 1 m deep, about 25–30 m²), bake the curve, then shape slopes from the curve, never from a fixed ratio:
   - **1% shelf:** at 1% drained, at least 25 cm of exposed mud on the intake shelf seen from the hand-pump handle. Shelf slope = 25 cm ÷ the height drop at 1% (about 1:35–1:40).
   - **10%, 20% and 30% bands at the launch-pad viewpoint:** each band exposes at least 0.8 m of shoreline on the lip facing the raised pad deck. Lip slope = band drop ÷ 0.8 m, read from the curve. The top band drops about 6.7 cm (2,000 L over about 30 m²), so the lip is about 1:12 there and steepens as the surface shrinks. Three bands use about 2.4 m of run on that one side only.
   - **40% to 100%:** adjacent bands are at least 6 cm apart vertically on the walls, so they read as separate rings from the pad deck (about 18 m).
   - **Pit:** a deep central pit holds the last 5–10%.
   - The Water Source GDD (Visual/Audio Requirements) states the same outcomes.
7. **Reveals:** authored objects in the basin, each with an int id (authored, below 1,000,000, Game State Rule 2) and a reveal height: the height of the object's top, the moment it first breaks the surface. The bake converts that height into `revealPct = 100 × (1 − curve⁻¹(revealHeight))` and stores it in the profile; Rule 4 checks it at 2e. MVP: 2 reveals, the **dock with a "Swim Safe" sign** (id 201) at about 10% and the **shopping cart** (id 202) at about 15%. The cart sits off the 10% and 20% threshold ticks so it adds a visible change of its own (about 12:15 for the reference player, 18:39 for a lazy launcher). Objects exist from the start under the water. Seeing them through the water is optional; it needs transparent water and the Depth Texture turned on in the URP asset.
8. **Lakebed blend:** wet mud blends to cracked, drying mud by height in the same Sub Graph. When `DrawdownThreshold(pond, 10)` is flushed, a global cracked-mud weight rises from 0 to 1 over 5 s, applied only to surfaces above the 10% band height. It happens once; on a load with 10% passed, the weight is 1 at once. No material swap (a swap pops).
9. **Wet shine (gloss only):** each tick the system pushes the current height into a 600-tick ring buffer (60 s) and sets a global `h_lag` = the height 60 s ago. Surfaces between the waterline and `h_lag` get `wetness = inverseLerp(h_lag, h_render, worldY)`, clamped 0 to 1, which raises gloss only. There is no colour change: 60 s at 16 L/s is only about 3 cm of height, and a colour change there would read as an extra stain band. The buffer is presentation only and not saved; on a new game or load it is filled with the current height (no shine).
10. **Visible change target:** something visible must change within the first 2 minutes (the puddle dropping with the bucket counts) and then at least every 5 minutes (purchases, the puddle running dry, thresholds, reveals). The Economy pacing simulator (sim3.py, bucket 6 cr) gives, for the reference player, a longest gap of 2:42 (hand pump 4:18 to motor 7:00). For a lazy launcher (launches 20 s after the pool fills), the cart at about 15% splits the old 6:15 gap from 10% (15:20) to 20% (21:35) into 3:19 and 2:56; the longest lazy gap is then 5:45 (20% band to the IBC at 27:20). Both pass Economy AC-33 (5 minutes reference; 6.5 minutes lazy, IBC by 30:00).

### States and Transitions

Per threshold and reveal: **Pending → Passed** (once, never back). Saved (Drawdown slice): passed thresholds and triggered reveals. Not saved: rendered height, `h_prev`, the wet-shine ring buffer and the lakebed fade (all rebuilt from the slice and the curve on load).

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Drawdown slice (passed thresholds, triggered reveals); `DrawdownThreshold`, `Reveal` | Tick step 2e (after Water Source at 2d); `alpha` |
| Water Source Model | — | `remaining` and `start` from its slice (read at 2e); `SourceDrained` ordering only |
| Economy | Baked start volume written into the tuning table by the editor bake tool | Nothing (threshold data no longer lives in Economy) |
| Corporate Radio | Events | — |
| Hose/Storage Network | Waterline height (for the intake hose end to follow) | — |
| Save & Load | Slice | Load |

## Formulas

The **water_height** formula is defined as:

`f = remaining / start` · `h = curve(f)` · `h_render = lerp(h_prev, h, alpha)`

**Variables:**
| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| remaining | double (L) | 0–start | Water Source slice | Litres left |
| start | double (L) | 18,000–25,000 (pond) | Water Source slice | Start volume copied at new game |
| curve | baked table | strictly increasing | Drawdown Profile | Fraction remaining → metres, ≥ 200 samples, binary search, linear between |
| alpha | float | 0–1 | Game State | Tick interpolation |

**Output Range:** basin bottom (f = 0) to spill height (f = 1).

The **threshold_check** formula is defined as:

`for t in thresholds ascending where !passed[t] and remaining ≤ start × (1 − t/100) + ε: raise DrawdownThreshold(source, t); passed[t] = true` then the same for reveals by ascending id with `revealPct`. `ε = 0.001 L`.

**Example:** start 23,456 L, 1% passes at remaining ≤ 23,221.441 L.

**Output Range:** 0 or more events per tick: thresholds ascending, then reveals by ascending id.

The **reveal_pct** bake formula is `revealPct = clamp(100 × (1 − curve⁻¹(revealHeight)), 0, 100)`. A reveal height above the spill height gives 0 (see Edge Cases).

The **band_halfwidth** formula is `vertical half-width = w × √(1 − n.y²)`, `w = 2 cm`. **Example:** on a 1:35 shelf (n.y ≈ 0.99959) the vertical half-width is about 0.57 mm; on a vertical wall it is 20 mm.

The **wetness** formula is `wetness = saturate(inverseLerp(h_lag, h_render, worldY))`, gloss only.

## Edge Cases

- **If one tick crosses several thresholds** (a save from an old version, a debug jump): all are raised in ascending order in the same tick (Game State AC-E2), then any reveals passed in that tick by ascending id.
- **If a threshold and a reveal pass in the same tick**: thresholds first (ascending), then reveals (ascending id). The radio then reorders for playback by its own priority (reveal line first, Corporate Radio AC-03).
- **If a threshold and `SourceDrained` happen in the same tick**: `SourceDrained` first (Water Source step 2d), then thresholds and reveals at 2e.
- **If the stain-band shader spike fails**: use the URP decal fallback (Rule 5); bands must still read from the pad deck at about 18 m.
- **If a reveal object's top is above the starting waterline** (authoring error): the bake clamps `revealPct` to 0 and logs a warning. It fires on the first tick of a new game, and never on a save load (it is already recorded as triggered).
- **If the game loads with thresholds passed**: bands for all passed thresholds are drawn at once (they are above the water height); no events re-fire; `h_prev = h`, so the plane does not slide.
- **If the saved start volume differs from the latest bake** (a patch after the save): the curve is keyed by fraction, so 0% drained is still the spill height; band heights stay correct.
- **If the curve is not strictly increasing** (bake error): the bake fails with an error and writes no asset; it never ships.
- **If more catch-up ticks run in one frame** (up to 5, Game State Rule 4): each tick runs its own 2e check; the rendered plane interpolates only between the last two ticks.

## Dependencies

**Upstream:** Water Source Model (Hard), Game State (Hard), level art (Hard, basin mesh and curve bake).
**Downstream:** Corporate Radio (Hard), Audio (Soft), Hose/Storage Network (Soft, intake follows waterline), Save & Load (Hard), Play Log (threshold and reveal rows), Economy (Soft, receives the baked start volume through the bake tool).

## Tuning Knobs

| Knob | Default | Safe range | Notes |
|---|---|---|---|
| Pond thresholds | 1, 2.5, 5, then every 10% | — | Drawdown Profile; early ones needed for real scale |
| Band width along the surface | 4 cm (w = 2 cm) | 3–6 cm | Readability; same on shelf and wall |
| Band read distance | 18 m | 18 m fixed | From the raised pad deck; the one figure used in this doc and Water Source |
| Threshold epsilon | 0.001 L | 1e-6–0.01 L | Litre comparison |
| Wet-shine dry time | 60 s | 30–120 s | Ring-buffer length (600 ticks at 10 Hz) |
| Lakebed blend fade | 5 s | 2–10 s | After the 10% event |
| Reveal positions | dock ~10%, cart ~15% | Off any threshold tick | Authored heights, baked to %; keep off threshold ticks |
| Curve samples | 200 | ≥ 100 | Bake quality |
| Bake grid spacing | 5 cm | 2–10 cm | Bake accuracy vs bake time |

## Visual/Audio Requirements

- Bathtub Ring look: pale dry mud above, a crisp dark band per threshold, gloss-only wet shine on fresh exposure, cracked mud above the 10% band after it passes.
- Reveal beat: a low sting, the object's wet shine, a short camera-unforced pause (no cutscene, no camera grab).
- Threshold beat: a soft chime and the band appears at the waterline as it passes.

## Game Feel

- The waterline must visibly creep at electric pump scale (8–16 L/s) when watched from the intake shelf for 30 s (16 L/s for 30 s is about 1.6 cm of height, about 19 cm of shoreline on the 1:12 lip).
- No camera takeovers; the player discovers reveals by looking.

## UI Requirements

None. The HUD may show the pond's drained percentage as confirmation only.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 4, 6 | `design/gdd/water-source-model.md` | `remaining`/`start` slice; basin outcomes stated the same way |
| Rule 2 | `design/gdd/economy-progression-and-shop.md` | Bake writes the start volume into the tuning table |
| Rule 10, AC-08 | `design/gdd/economy-progression-and-shop.md` | Pacing (sim3.py) and AC-33 |
| Rules 1, 4 | `design/gdd/game-state-and-simulation.md` | Tick order and boundaries (Rule 3), catch-up (Rule 4), ids (Rule 2), AC-E2 |
| Rule 7 | `design/gdd/corporate-radio.md` | Lines for thresholds and reveals |

## Acceptance Criteria

Test doubles: `StubWaterSource` (pond start 20,000 L unless stated, `remaining` set per tick, drives Drawdown's 2e step), `BusSpy` (records raised events with tick), `FakeCurveTable` (linear, `h = f × 1.0 m`), `FakeClock` (fixed frame time, no catch-up), `FakeSaveStore` (in-memory slice round trip), `TestProfile` (a Drawdown Profile built in code).

- **AC-01 [EditMode] Threshold once:** GIVEN `StubWaterSource` at remaining 19,820 L (0.9%), a `BusSpy` and the pond profile, WHEN one tick sets remaining to 19,800 L (1.0%) and the next tick to 19,790 L, THEN the `BusSpy` records exactly one `DrawdownThreshold(pond, 1)`, on the first tick, and nothing on the second.
- **AC-02 [EditMode] Several in one tick:** GIVEN thresholds 1 and 2.5 already passed and remaining 19,020 L (4.9%), WHEN one tick sets remaining to 17,980 L (10.1%), THEN the `BusSpy` records `DrawdownThreshold(pond, 5)` then `DrawdownThreshold(pond, 10)`, both with the same tick, and no 1 or 2.5 event.
- **AC-03 [EditMode] Save and load:** GIVEN thresholds 1, 2.5 and 5 passed, reveal 201 not triggered, remaining 18,900 L (5.5%) and `FakeCurveTable`, WHEN the slice goes through `FakeSaveStore` and one tick runs at the same remaining, THEN the `BusSpy` records no event, the bands above the water height are exactly [0.99, 0.975, 0.95] m (± 1e-6 m), and `h_prev` equals `h` (0.945 m).
- **AC-04 [EditMode] Curve bake:** GIVEN the test mesh `FixtureBasin6x5` (6 × 5 m, 1 m deep), WHEN it is baked, THEN the curve has ≥ 200 samples, height is strictly increasing with fraction remaining, `curve(1)` equals the spill height within 5 mm, and the exported Economy table's pond start volume equals the baked volume rounded to the litre. GIVEN a sample table with one repeated height, WHEN it is validated, THEN the bake fails with an error and writes no asset.
- **AC-05 [EditMode] Reveal from the tick only:** GIVEN `TestProfile` with reveal 201 at `revealPct` 10.0 and remaining 18,020 L (9.9%), WHEN the rendered plane is forced below the dock's reveal height and a tick runs at 18,020 L, THEN no `Reveal` is recorded; WHEN the next tick sets 18,000 L (10.0%), THEN exactly one `Reveal(201)` is recorded; WHEN later ticks go to 17,000 L and the slice is saved and loaded, THEN no further `Reveal(201)`.
- **AC-06 [PlayMode] Smooth plane:** GIVEN `FakeClock` at 1/60 s per frame with no catch-up, `StubWaterSource` draining 1.6 L per tick (16 L/s) and `FakeCurveTable`, WHEN the rendered height is sampled every frame for 600 frames, THEN it never increases and no frame-to-frame change exceeds one tick's change (8e-5 m) + 1e-6 m.
- **AC-07 [PlayMode + Visual] 1% shelf:** GIVEN the graybox basin with its baked curve and the 1% threshold passed, WHEN a ray from the hand-pump handle eye point measures the horizontal distance along the intake shelf from the start shoreline to the waterline, THEN it is at least 20 cm (design target 25 cm), and a screenshot is saved.
- **AC-08 [Manual] Pacing (copies Economy AC-33):** GIVEN a fresh MVP run by Nour as the reference player, WHEN times are read from the play log, THEN no gap between visible changes (a purchase, the puddle running dry, a `DrawdownThreshold` or a `Reveal`) exceeds 5 minutes. GIVEN a run launching only about 20 s after the pool fills, THEN the IBC arrives by 30:00 and no gap exceeds 6.5 minutes.
- **AC-09 [Visual] Readable drop:** GIVEN the 10% threshold just passed, WHEN a screenshot is taken from the pad deck eye point (about 18 m from the lip) and a second from the same pose after 120 s of two pumps at 16 L/s (1,920 L, about 19.6%), THEN in the second image the waterline sits visibly below the 10% band, with at least 0.7 m of shoreline between them on the pad-facing lip, and the band reads as one crisp line. Both images are kept as evidence.
- **AC-10 [EditMode] Same-tick order:** GIVEN `TestProfile` with threshold 10 pending, reveal 202 at 9.5% and reveal 201 at 9.8%, and remaining 18,200 L (9.0%), WHEN one tick sets remaining to 17,900 L (10.5%), THEN the `BusSpy` order is `DrawdownThreshold(pond, 10)`, `Reveal(201)`, `Reveal(202)`, all with the same tick.
- **AC-11 [EditMode] Litre epsilon:** GIVEN `StubWaterSource` with start 23,456 L, WHEN remaining falls by 0.23456 L per tick (accumulated in double) for 1,000 ticks, THEN `DrawdownThreshold(pond, 1)` is recorded exactly once, on tick 1,000.
- **AC-12 [EditMode] Puddle has no thresholds:** GIVEN a puddle `StubWaterSource` (start 40 L) with its empty profile, WHEN remaining goes 40 → 30 → 20 → 10 → 0 L over 4 ticks, THEN the `BusSpy` records no `DrawdownThreshold` and no `Reveal`.
- **AC-13 [EditMode + Visual] Band width:** GIVEN `BandMath.VerticalHalfWidth` (the C# mirror of the Sub Graph function) with w = 0.02 m, WHEN evaluated for a 1:35 shelf normal and a vertical wall normal, THEN it returns 0.000571 m (± 1e-5) and 0.020 m (± 1e-6); a screenshot shows the 1%, 2.5% and 5% bands as three separate lines on the shelf.
- **AC-14 [PlayMode] Lakebed blend:** GIVEN 10% not passed and the cracked-mud weight at 0, WHEN `DrawdownThreshold(pond, 10)` is flushed, THEN the weight reaches 1 after 5 s (± 0.1 s) and stays 1; GIVEN a load with 10% passed, THEN the weight is 1 on the first frame.
- **AC-15 [PlayMode] Wet shine:** GIVEN `FakeCurveTable`, `FakeClock` and a constant drain of 16 L/s, WHEN a world height is exposed at t0, THEN its wetness is 1.0 at t0, 0.5 (± 0.02) at t0 + 30 s and 0 at t0 + 60 s, and the material's albedo is unchanged throughout; GIVEN a load, THEN `h_lag` equals `h`.
- **AC-16 [EditMode] Reveal above the waterline:** GIVEN a reveal 203 whose top is above the spill height, WHEN the profile is baked, THEN `revealPct` is 0 and one warning is logged; WHEN a new game's first tick runs, THEN `Reveal(203)` is recorded once on that tick; GIVEN a save with 203 triggered, WHEN it is loaded and a tick runs, THEN no `Reveal(203)`.
- **AC-17 [EditMode] Basin band outcomes:** GIVEN the graybox basin's baked curve, WHEN the pad-facing lip is measured, THEN the horizontal distance between the waterlines at 0%, 10%, 20% and 30% is at least 0.8 m per band; and the vertical drop between each adjacent pair of bands from 30%/40% to 90%/100% is at least 0.06 m.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02; revised after the 2026-11-02 batch design review: 7 blocking items fixed).** This is Pillar 3 as a system: height is history (bands), reveal don't remove (authored objects). Watch item: the stain-band Sub Graph is a 1–2 day spike, with the URP decal fallback ready.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Stain-band Sub Graph vs URP decals | technical-artist | 1–2 day spike |
| Does the pad-facing lip fit three 0.8 m bands (about 2.4 m of run) in the graybox, and do the 40–100% rings read at 6 cm from 18 m? | level-designer | Graybox |
| More reveals between 30% and 100% (Water Source list) | level-designer | After the MVP |
