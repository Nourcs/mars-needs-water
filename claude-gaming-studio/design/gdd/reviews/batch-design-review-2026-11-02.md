# Design Review: 8 MVP GDDs (batch)

> **Date**: 2026-11-02
> **Docs**: pumps-and-machines, shipping-and-launch, hand-tools, corporate-radio, drawdown-visuals-and-reveals, first-person-controller-and-interaction, save-and-load, hud (all in `design/gdd/`)
> **Mode**: batch (2 specialists per doc, 16 reports; one creative-director synthesis)
> **Verdict**: all 8 **NEEDS REVISION** (none MAJOR)


No files modified. I re-ran sim3.py (eager, lazy and bucket=6) and spot-checked the reviewers against the docs.

**Overall:** all 8 docs NEED REVISION and none needs MAJOR. Each core design holds up. What's missing is shared contract between docs (tick boundaries, who owns what, test specs).

---

## 1. Per-doc verdicts

### Pumps & Machines: NEEDS REVISION
BLOCKING
1. **Second-pump socket:** already decided in the Hose review (drum gets one twin-port inlet that counts as 2 inlets). Pumps cites it and AC-05 names which inlet each pump uses. Nothing new to decide.
2. **Dry/Stalled timing:** "Dry in the same tick" can't happen, because Pumps steps at 2a and the source drains at 2b/2d. Fix: Dry is set at 2a of the tick after the drain, and Stalled the same way after `PoolFull`. Rewrite AC-07 and the edge case at l.91.
3. **State table:** unplugging goes Running/Stalled/Dry → Placed, never Ready. Dry → Placed. Stalled → Ready on release or Off.
4. **Saved fields:** each pump saves `{id, kind, motorFitted, leverOn, cableSocket|null}`. Running is derived and the crank is not saved. This needs a Game State amendment (see Cross-doc).
5. **Purchased:** remove `Purchased` as an input. Economy grants the item by a direct call. A pump or motor is usable once its crate is Opened. The motor can be bought before the pump crate is opened, so the kit unboxes loose and bolting is rejected with `no_pump`.
6. **ACs:** name the fakes (FakeNetwork, FakeSource, FakePowerPost; dt 0.1; tolerance 1e-6). AC-12/13 need play-log rows for crank start/stop and lever events, plus pass thresholds (duty 0.5–0.9; more than 5 m away within 30 s).

RECOMMENDED: request names `CrankStart/CrankStop(issuer,pump)`, `BoltMotor`, `SetSwitch`, `PlugCable`; spin-up is presentation only (AC-04/11); "step 2" → "2b"; Rule 5 should cite "rules 1, 4 and the power part of 3"; picking up a pump disconnects its sockets by direct call; 30.0 m accepted; registry adds `power_cable_m` and `post_sockets`; add `socket_occupied`.

### Shipping & Launch: NEEDS REVISION
BLOCKING
1. **Tick boundaries:** use the shared convention in Cross-doc #1. Liftoff is at 2c of tick N+30 and Ready at 2c of N+150. AC-01: no `LaunchCompleted` through N+29, exactly one at N+30. AC-03: rejected at N+150, accepted at N+151.
2. **Conservation includes litres aboard:** Shipping states it, and Hose Rule 11 and AC-09 add "aboard".
3. **Cargo range:** the floor goes to 1,200 L, plus an AC with cargo 500 against a 1,200 L pool (Take 500, 700 L left).
4. **AC-05 exact values:** 100 L pool, pump at 0.8 L/tick. Assert `LaunchCompleted(100)` and 24.8 L left at liftoff.

RECOMMENDED: total shipped exactness follows the pending number-representation ADR (cite it; it's already tracked in Economy); reject loads under 1 mL as `nothing_to_ship`; check order `too_far → pad_busy → nothing_to_ship`; find the pad tank by spot on every request; AC for a save while Away and for a pad-tank swap; show the countdown on press only when Ready with a non-empty pool (I chose QA's version over Systems'); `launch_cycle` lives in the Economy table, and Economy l.133 is fixed to match; add `pool_capacity` and a `max_ship_rate` variable table.

### Hand Tools: NEEDS REVISION
BLOCKING
1. **No cue to launch the spoon water:** fixed through the shared shortfall line (Cross-doc #3). No separate Hand Tools prompt rule. The LAUNCH lever hover shows "Launch: pays N cr".
2. **Residue epsilon:** ranges become `0 … capacity − held + 1e-6`, plus the 10.0000005 L AC (Full, then `tool_full`, no Withdraw call, no warning).
3. **ACs:** name the doubles. AC-03/04 assert 0 `Withdraw` calls. AC-08: 100 cycles = 1.5 L, relative 1e-9. AC-10: fake-clock press times. State whether a rejected scoop starts the cooldown, and define a pour cooldown.
4. **Interactions table:** a tool is usable when its crate is Opened and arrives via `SetHands`, not via `Purchased`.

RECOMMENDED: "Spoonful #N" counter and 2–3 scoop-count quips; 0.15 s input buffer; check `tool_full` before `source_dry`; a held amount ≤ 1e-6 shows "0.0" with the Scoop prompt; AC-11 is timed by Nour (5 trips, median), noting Water Source's 12–14 s estimate; reword the fantasy to "fill, launch, wait for the next rocket"; level rule: jerrycan within one view of the puddle (no turn). The spoon window itself is Decision 1.

### Corporate Radio: NEEDS REVISION
BLOCKING
1. **Clock:** timers run on an injected simulation-time clock that stops while Paused. Nags work from tick stamps. Add a pause AC.
2. **Milestone overlap:** one event makes at most one request, and the milestone line replaces the base line. AC-01 updated to match.
3. **One-off lines can be lost:** priority ≥4 is never dropped as stale or evicted. Mark a line played when its event arrives.
4. **Electric-stage flood:** retarget Rule 3 at the electric stage. Add `stinger_min_interval` 30 s and a stinger pool of at least 12. A launch with neither line nor stinger blinks the radio light, which still counts as the "every launch gets a reaction" Pillar 4 test.
5. **Nag fires during correct play:** nag only when the pad pool is full and nobody has launched for 60 s.
6. **Thresholds:** add a generic pool for 40–100% and an end-beat pool.

RECOMMENDED: drop the incoming request when it's the lowest priority; start reveal lines 1.5–2 s after the sting; crate nag at 60 s after landing and outside the 90 s gate, once per crate; numbers in subtitles only, voice clips generic; line duration from data, with `max(audio, 1.5 s + 50 ms/char)` on screen; save one-off line IDs only, not shuffle bags; payslip "8 credits" uses Economy's pure payout function; fix AC-06 (13 launches); fix the stale Drawdown cross-reference; the radio's "10%… shopping cart" text should match the dock at 10%.

### Drawdown Visuals & Reveals: NEEDS REVISION
BLOCKING
1. **Basin outcome rule can't be built:** 10 bands × 0.8 m needs 8 m of run in a basin about 2.5–3 m from rim to centre, and the top band drops about 6.7 cm. Fix: 0.8 m of shoreline only for the 10–30% bands at the launch-pad viewpoint; below that, at least 6 cm vertical separation on the walls; slopes derived from the curve. Water Source l.166–168 is amended the same way.
2. **Band thickness:** set width along the surface (vertical half-width = w·√(1−n.y²)). The decal fallback does the same.
3. **Curve key:** `h = curve(remaining/start)`. The bake writes its volume into the Economy row, and the bake method is stated.
4. **Reveal trigger:** baked to a % stored as data and checked at 2e. Never from the rendered plane. Same-tick order: thresholds ascending, then reveals by ascending id, plus an AC.
5. **Data owner:** Drawdown owns the threshold and reveal lists, in a per-source profile asset next to the curve. The Economy reference is removed.
6. **AC-08 vs Economy:** AC-08 copies Economy AC-33's two clauses (reference ≤5 min; lazy ≤6.5 min, IBC by 30:00). Also move the shopping cart to about 15% (my call, see Cross-doc #8).
7. **AC rewrite:** AC-01–09 as Given-When-Then on stubs. AC-09 becomes two screenshots from the pad deck (10%, then after 120 s at 16 L/s).

RECOMMENDED: Vector4 band packing in one Sub Graph; ProBuilder basin; Screen Space Decal setup; wet shine is gloss only, via a lagged height; height-blended lakebed instead of a material swap; h_prev = h on load; epsilon litre comparison (test start 23,456 L); ACs for puddle, swap and shine; one number for band read distance (Water Source says 18 m).

### First-Person Controller & Interaction: NEEDS REVISION
BLOCKING
1. **One input table:** action, request, tap or hold (with duration), KB/M and gamepad bindings, range, rejection text. Every input has one meaning per context.
   - A press under 0.25 s is a tap; holding past it shows a radial ring; releasing early or looking away sends nothing.
   - A Landed crate can only be pried (hold, 1 s). Bolt and FitInlet are 2 s holds.
   - Scroll and bumpers rotate only while a ghost is up; otherwise they switch tools.
2. **Request coverage:** the table covers Disconnect, PickUp, Drop, PlayerPose, CrankStart/Stop, BoltMotor, SetSwitch, PlugCable and SelectTool. Owners define the requests (Cross-doc #4).
3. **Range origin:** eye point = pose + eye height, shared by the raycast and the simulation. Per-target range in data: crank 1.5 m, default 2 m, levers 3 m.
4. **PlacementValidator:** pure C# over authored zones and footprints, called by both the ghost and step 1. Drop the "on a hose" check, since hoses don't block.

RECOMMENDED: add wading 0.6 to `ground_mult` (output about 0.7–6.5); no sprint or jump while carrying a crate or pump; picking something up stows the tool (it keeps its water); fix the Save & Load row (`PlayerPose` per tick); delete the unreachable "walks out of range while cranking" edge case, or allow movement (see Decision 2); the ORDER lever orders the next unlocked item; Input Actions asset spec; ACs for the 0.9 s pry, tap vs hold, and the red ghost; request sequence id deferred to co-op.

### Save & Load: NEEDS REVISION
BLOCKING
1. **Timer format:** on disk, remaining seconds; on load, `max(1, round(s/dt))` (Game State Rule 8).
2. **Doubles:** invariant-culture round-trip format (G17); reject NaN and Infinity; credits stored as an integer millicredit string.
3. **Move files aside:** before the first write of a new session, rename a newer or corrupt file to `save_0.v{N}.json` / `save_0.corrupt-{utc}.json`. Extend AC-04.
4. **Validate and load order:** load `.json`, then `.bak`, and ignore `.tmp`. Validate = each owner's load hook, unique ids, next id above the highest id in the file, `remaining ≤ start`. Unknown ids follow each owner's rule (Hose: the end loads unplugged at its old position; this replaces the "ignored" line).
5. **Write mode and ACs:** the write is synchronous on the main thread for the MVP (about 100 KB), which settles quit-during-write. ACs use an `IFileSystem` fake for disk full, a crash between replace steps, both files corrupt, `.bak` rotation, and a v0→v1 migration fixture.

RECOMMENDED: `Flush(true)`; File.Move on the first save, then File.Replace; Windows retry; absolute budget Save() ≤ 8 ms and file ≤ 100 KB in place of the "1.5× median"; JsonNode migrators; cite immediate saves on pause and quit; list the crate fields; drop the "backups 1–3" knob; the toast and drop icon go into the HUD doc.

### HUD: NEEDS REVISION
BLOCKING
1. **Shortfall line:** Economy computes it and the HUD only displays it (Cross-doc #3).
2. **FULL:** derived from level ≥ capacity on the pad pool. `PoolFull` only triggers the pulse. FULL clears below capacity. Add an AC.
3. **Payout pop:** shows Economy's credit change (after minus before at 2f), not litres × rate. AFFORDABLE flips when the displayed count crosses the price.
4. **States:** map to Economy's names. The plaque counts as the last next item. "All equipment owned" shows after it's bought. Add `Purchased` to the table.
5. **Read-only and testable ACs:** FakeGameState plus a recording request queue gives 0 requests. Every AC gets a WHEN and names its fakes. AC-03 is 0.8 s ± 1 frame. AC-06 is 18 px at 80% scale.

RECOMMENDED: mL below 1 L; the held amount rounds down; thousands separators; hold ring and bound key in prompts; subtitles ≥ 28 px on a backing box, 4.5:1 contrast, pulse ≤ 2 Hz; subtitle on/off and size settings; "drained %"; pillar name fixed to "The World Shows the Damage".

---

## 2. Cross-doc fixes

1. **Tick convention (clarifying amendment to the APPROVED Game State doc, Rules 3/4).**
   - A timer of n ticks started in tick N fires at its owner's step in tick N+n.
   - A change made at any step of tick M is first seen by requests at step 1 of tick M+1.
   - Economy AC-27 (Spawn at T+40) already fits this.
   - Applies to: Shipping AC-01/03/05, Pumps Dry/Stalled, Radio stamps.
2. **Timers on disk:** remaining seconds; in state, whole ticks. Game State Rules 3 and 8 already say this. Save, Shipping Rule 9 and Economy cite it.
3. **Shortfall line:**
   - Economy's `NextItemProgress` exposes `litresShort = max(0, (price − credits)/effective_cpl − padPoolContents)`, with multipliers applied.
   - The HUD shows one decimal below 10 L, and "Enough aboard: LAUNCH to afford" at 0.
   - This one fix covers HUD #1 and Hand Tools #1. It also makes Economy Rule 13's "the shortfall line invites early launches" true.
4. **Requests:** each owning system defines its requests and rejection codes. FPC owns the input/context binding table and the rejection-text column, which also covers Hose's GD2.
   - New: Pumps `CrankStart/CrankStop/BoltMotor/SetSwitch/PlugCable`; Hand Tools `SelectTool`.
5. **`Purchased` never drives state:** grants are direct calls, and items are usable on crate Opened. Fix in Pumps, Hand Tools and HUD.
6. **Pad pool everywhere:** the HUD bar shows the pad pool (Hose l.198 changed, already decided). Conservation includes litres aboard: Hose Rule 11 and AC-09.
7. **AMENDMENT to the APPROVED Game State doc, Rule 1:** Pumps slice = `{id, kind, motorFitted, leverOn, cableSocket|null}`. It replaces "owned tier, on/off, running state". Running is derived.
8. **Drawdown data and pacing:**
   - Drawdown owns threshold and reveal data.
   - Cart moves to about 15%. This splits the lazy 6:15 gap (10% → 20%) into about 3:10 + 3:05, and moves the cart off the 20% threshold tick.
   - Drawdown AC-08 matches Economy AC-33. Water Source basin outcomes are amended (Drawdown #1).
9. **Range:** eye-point origin; crank 1.5 m everywhere (FPC, Pumps).
10. **Pillar names:** use the concept's names in HUD (l.7) and Hose.

## 3. Decisions for Nour

**D1: The spoon stage window.** The concept target is 60–90 s; Economy AC-33 wants the bucket bought at 1:00–1:30. In the sim, the bucket is bought at 1:14 and in hand at 1:26, assuming a perfect 1.25 s cycle. If a real scoop-turn-pour takes about 1.6 s, the bucket is bought around 1:33 and in hand around 1:45.
- **A. Keep 8 cr and 1.25 s.** Rely on the shortfall cue and a no-turn jerrycan layout. Nour plays faster than a first-timer, so they can't confirm the window themselves.
- **B. Bucket 6 cr.** This changes a fixed price. It means 40 scoops; the sim buys the bucket at 0:55, and with 1.6 s cycles about 1:09. The hand pump moves to 4:18, still inside its window. The Shipping anchor becomes 0.6 L / "6 credits".
- **C. Spoon cooldown 1.0 s.** This only helps if the cooldown is the limit. If the turn is the limit, it changes nothing.
- **Recommendation: B.** It's the only option that works whatever the real cycle time turns out to be, and the joke is the same at 40 scoops. If Nour wants the 8 cr price kept, A plus the layout rule is acceptable.

**D2: Hold or toggle for the crank in the MVP.** The player holds the button for about 2.7 min (eager) to 4.8 min (lazy).
- **A. Hold only.** Pure "Hands on the Machinery", but a strain risk.
- **B. Hold by default, plus a Toggle setting.** One press latches the crank; pressing again, moving or looking away releases it. Fixed holds (pry, bolt, fit) complete after a tap when Toggle is on.
- **C. Toggle by default.**
- **Recommendation: B.** It costs one boolean, keeps the effort fantasy as the default, and protects Nour's own hands during repeated test runs.

The lazy-launcher pacing gap is not a decision for Nour. The lazy run already passes Economy AC-33's lazy clause (gaps 6:15 and 5:46 against 6.5 min; IBC at 27:40 against 30:00). Moving the cart to about 15% is a placement change within my remit.

## 4. Reviewer claims checked and found wrong or overstated

- **draw-qa #5** "an MVP session fails… conflicts with Economy AC-33": the gap times are right, but lazy *passes* Economy's 6.5 min clause. The only conflict is Drawdown AC-08's wording, and its Rule 9 still calls the question open when Economy has already closed it.
- **save-eng #1** "timers saved two conflicting ways": overstated. Game State holds ticks in state and seconds on disk, which is consistent, and Economy AC-28 holds at the same dt. The real gap is that Save never says this.
- **ship-qa #7** `ticks = ceil(s/dt)`: wrong. It contradicts Game State Rule 8's `max(1, round(s/dt))`.
- **ship-sys #2:** its convention is internally inconsistent (liftoff at N+30 but Ready hard-coded at N+149). Replaced by Cross-doc #1.
- **tools-gd #2** "cooldown 1.0 s" as an equivalent fix: overstated, because it does nothing if the real cycle is limited by turning. The "1.6 s real cycle" is an estimate, not a measurement.
- **radio-gd #1** "one launch every 15 s from 8:12": partly wrong. The sim gives 25 s gaps until the second pump (10:35) and 15 s after. The jerrycan/puddle stretch also hits 15 s gaps briefly. The conclusion (the electric stage floods the radio) holds; 35 launches is confirmed.
- **save-eng #8** "Radio saves shuffle bags": rejected. Only one-off IDs are saved, as the Save doc already says.
- **hud-qa #10 / radio-gd #10** "paths should be systems/gdd": wrong. `design/gdd/` is the repo destination (the concept lives there). Only the "Drawdown not yet written" note is stale.
- **hud-gd #2** is a fix to the Hose doc, not the HUD; HUD Rule 3 is already correct. It was already decided in the Hose review.
- **ship-sys #3** (exactness of total shipped) is already tracked by the pending number-representation ADR in Economy, so it's RECOMMENDED, not BLOCKING.

Confirmed correct: the sim times (7:25/8:05/9:07/11:19/13:49; bucket at 1:14, in hand at 1:26), the basin arithmetic (6.7 cm per top band; 8 m of run needed against about 2.5–3 m available), the Pumps same-tick Dry error, Hose conservation missing litres aboard, the cargo range against the 1,200 L pool, the HUD `ceil` that ignores the pad pool, and the 1.5 m vs 2 m crank range.