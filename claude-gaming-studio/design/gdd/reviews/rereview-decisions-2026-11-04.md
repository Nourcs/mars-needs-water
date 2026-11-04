# Fixes after re-review round 1 (2026-11-04) — decisions are final

Game State Rule 12 is already amended (direct callers now include Pumps SetHands for cable ends; Network Remove for the inlet fitting and swapped-out container, Network SetHands for hose ends; end positions stored by Network/Pumps, Placement only tracks hands).

## Hose (hose-storage-and-flow-network.md)
H1 SwapStorage: accept only if there is a pad tank AND the target is an uninstalled, empty hand-stage container whose capacity is larger than the current pad tank's; else `not_an_upgrade` (check order: `too_far` → `not_on_staging_spot` → `not_an_upgrade`). After the swap the old container is hauled away by the company drone: the Network calls `Placement.Remove(oldId)` (no prop left on the staging spot, so no swap-back and nothing blocks the spot). The IBC is never swapped: it is placed on its own authored spot and linked. Add AC cases.
H2 Live full query: expose `IsFull(poolId)` computed live from contents vs capacity on the current pool graph (sees step-1 pours, links and Takes). Pumps reads `IsFull`; the `PoolFull` event latch is separate and only for edge detection. List it in Interactions for Pumps. Shipping's `Take` happens at step 1 (Shipping rule), so fix AC-31 accordingly (PoolFull tick 124 if that's what the arithmetic gives; verify).
H3 Intake binding only via Use: FPC row 16 sends `Connect(issuer, hoseId, 1, IntakeZone(sourceId))`; a G-drop never binds. Rewrite rule 4 and AC-33.
H4 Hose ends and hands: add a rule. Hoses arrive as coils; picking up a coil puts end 0 in the hands (Placement.PickUp → Network records the end as held by issuer). After end 0 is connected, the Network calls `Placement.SetHands(issuer, end 1)` so the player walks the hose out. Disconnect puts the end in the issuer's hands (`hands_full` if holding something else); Drop (G) of a hose end: Network stores restPose. A carried end is tethered: it cannot be carried farther than the hose length from the other end (presentation clamps; `Connect` still checks `hose_too_short`).
H5 FitInlet: codes `not_holding` (issuer not holding the fitting) → `too_far` → `already_fitted`; on success the Network calls `Placement.Remove(fittingId)`.
H6 Pad tank cannot be picked up: PickUp on the tank on the storage spot is rejected `blocked` (no prompt). Fix "picked-up tank keeps its water" wording (Placement rejects a tank holding water with `not_empty`).
Recommended to apply: say which sources get Withdraw(0) (only those with ≥1 connected pump); Disconnect checks `fixed_end` before `hands_full`; AC-09/10 name pump rate and source; variable-table ranges match Tuning Knobs (hoseCap 8–15, rate_p 0–10) and add d_p, D_G, k_G, cap_G.

## Pumps (pumps-and-machines.md)
P1 Missing part: Network exposes `IntakeBound(pump)` and `OutletPool(pump)` (connected = both). Add `Pumps.MissingPart(pump)` with order `no_intake` → `no_outlet` → `no_power`; FPC reads it for the prompt (FPC owns the text; drop quoted text here). FakeNetwork gets both flags; fix AC-04.
P2 Cable end: the cable end position is stored by Pumps. On `PlugCable` Pumps calls `Placement.SetHands(issuer, null)` and the end sits at the socket pose. On `UnplugCable` the end goes to the issuer's hands (`hands_full` if holding something else; check order `not_plugged` → `too_far` (to the socket) → `hands_full`). Picking up the motor kit/pump cable end works like a hose end.
Recommended: Rule 4 "Movement is locked" → "any move input sends CrankStop (FPC)"; duty range 0.5–0.9 everywhere; AC-22 duty definition = Running time ÷ (first Ready → `Purchased(electric_motor)`) minus 3 s per launch, and note sim3.py needs a `duty=` arg (task); first-2a after new game/load: from-state = derived state, no `PumpStateChanged` on load; "current flag at 2a" wording; "dropped" → "picked-up"; port A/B = inlet 0/1; AC-19 fixed script with expected count; Take at step 1 (Shipping) in rule 11/AC-12.

## Economy (economy-progression-and-shop.md)
E1 Order checks in order: `too_far` (3 m from eye point to the ORDER lever) → `already_owned` → `locked` → `cannot_afford`. AC-13 gets a too-far case; AC-12 consistent.
E2 Unboxing: a tank/pump spawns at the crate spot's authored item pose next to the crate (not overlapping it); the player carries a barrel/drum to the staging spot and uses Swap in; the old container is hauled away by the drone (Hose H1). The IBC goes on its own authored spot. Tanks get no Grant call (they appear only via Spawn). Fix Rule 8 and Rule 12 wording.
Recommended: header notes the 6 cr amendment; AC-21 compares from bucket → barrel onward only (the spoon step is exempt and not compared), and note the sim's table-export reader is a tooling task; fixed crate landing spot rule (lowest free index); line 36 contribution computed from total shipped; AC-20 points to owning docs' knob tables and add a load check cargo ≥ largest pool; Interactions: Placement row adds SetHands, HUD row columns fixed.

## Water Source (water-source-model.md) — APPROVE WITH NITS, apply nits
Line 113 lazy 5:45 passes the 6.5-min lazy limit, not the 5-min one; pond ~30 m² / ~1 mm (also Economy line ~194 says 20 m²: leave Economy to its fixer); shelf ~1:31–1:37 advisory; Low state: no sputter in MVP (Low unused in MVP; Pumps sputters only on Dry); lastGrant and drained-this-tick are per-tick transients outside the slice, cleared after 2d; AC-23 use `Is.Not.AllocatingGCMemory()` (Unity Test Framework); AC-24 evidence (play log + screenshots in production/qa/evidence/) and reference pace = Economy reference player; unknown id: Start/DrawdownPct return 0, State returns Drained; source in save but not in table: kept with saved start copy and logged; saved start ≤ 0 rejects the slice in the load hook; add Pumps row (reads drained flag at 2a).

## Shipping (shipping-and-launch.md) — APPROVE WITH NITS, apply nits
No tank on the storage spot cannot happen (Hose H6: pad tank can't be picked up) but still: treat as empty → `nothing_to_ship` (edge case + AC-02 case); Hose rule ref = Rule 12; timer stored as remaining = fireTick − currentTick (not decremented in the tick it starts); Game Feel claim "except at the range edge"; min_launch value in the Economy table; AC-03/AC-07 refill pad pool to 10 L before N+151; Rule 12: `Take` requests an amount rounded to whole µL so aboard equals shipped exactly; Ready timer field = 0; AC-14 fake ticks at dt 0.1.
Also fix in save-and-load.md: "Shipping rule 9" → Rule 10 (saving) / Rule 4 (atomic payout) (lines ~68, ~100). And hand-tools.md rule 5 wording: poured water enters the pool and fills the pad tank first, then the linked tank (Hose rule 5).

# Round 1b decisions (Radio, Drawdown, HUD) — final

## Radio (corporate-radio.md)
R1 `line_choice`: while the head request is a reveal inside its 1.8 s delay, no other line starts (add state "Idle, reveal pending"); the reveal delay overrides the 0.5 s start target for reveals only.
R2 SourceDrained: not a milestone. It is a one-off (priority 4) per source: the puddle line shows `lastGrantLitres` (Water Source onboarding beat); the pond drain plays the end beat if it has not played, otherwise the pond drained line. The three per-item pump `Purchased` lines: make the first pump purchase the milestone, and keep per-item lines only where reachable (or cut dead ones).
R3 Rule 5 figures: electric stage (7:00 to 15:40) has 28 launches and 5 thresholds; with stinger_min_interval 30 s the split is about 11 full lines, 6 to 8 stingers and 9 to 11 light-only (state as approximate, from sim3 eager). Keep 30 s.
R4 End beat: `litres ≥ capacity − 1e-6` (Hose Full tolerance), AC-16 case 1,199.9999995 L.
Recommended: a priority ≤ 3 request arriving at a queue full of priority 4–5 is dropped; a dropped or evicted launch request still blinks the light; AC-08 precondition "first-launch milestone already played"; pad pool = tank on the storage spot (read via Network/Placement); after a load or rebuild, `padFullTick` = load tick if the pad pool is Full; jingle plays inside the reveal delay (does not change AC-04 / AC-14 ticks); pump-item set and end-beat capacity in the data file.

## Drawdown (drawdown-visuals-and-reveals.md)
D1 Wet shine: wetness = 0 unless `h_lag − h_render ≥ 1e-4 m` and `h_render < worldY ≤ h_lag` (Sub Graph and C# BandMath mirror); AC-15 adds "on load or idle, wetness 0 everywhere" and "after at least 60 s of constant drain" in the GIVEN.
Recommended: band minimum screen width `max(w·√(1−n.y²), k·fwidth(worldY))` as a spike exit criterion; bake raycasts only the basin MeshCollider via layer mask, spill height/bounds authored on the Drawdown Profile, warn outside 18,000–25,000 L; shared shader globals are pond-only in the MVP and band heights set as globals from the profile at runtime; shelf run about 1.2 m for AC-13 and shelf slope about 1:31–1:37; fix the "25 cm apart" claim (~35 cm and ~58 cm at 1:35); decal fallback accepts fixed-height bands; AC-09 "16 L/s combined (8 L/s each)"; Rule 7 Depth Texture only for depth fading; Radio ref AC-03 → AC-04; AC-17 starts at 40% and names a `PadLipProbe` transform; lakebed fade runs on presentation time and freezes on pause.

## HUD (hud.md)
U1 Launch window: from `LaunchStarted` until the payout count-up ends, the shortfall line shows "Launching..." (add Shipping state / `LaunchStarted` to Interactions; add an AC).
U2 Hand Tools items: Rule 4 draws "Spoonful #N" next to the held amount and the three 3 s crosshair quips (Hand Tools Rule 12); add to the Hand Tools Interactions row; F1 hides them like the prompt.
Recommended: payout pop = `floor(Economy.PayoutFor(LaunchCompleted.litres)/1000)` (not the credit delta); tank reading uses `floor((c + 1e-6) × 10) / 10`; HUD triggers Economy's chime cue at the displayed crossing; ring duration counts from the press (finishes at 1.0 s for pry); crank in Hold mode shows a closed ring while running; ring shows 0 when it appears at 0.25 s; grey predicted-rejection prompt style (FPC); AC-13 "font size ≥ 18 px at 80% scale (UI asset) plus screenshot"; add ACs for save icon/toast, PoolFull pulse, count-up restart, subtitle cards; subtitle cards split `display_time` by character count; F1 does not hide the "Save failed" toast; drained % comes from Water Source `DrawdownPct`.

## Save (save-and-load.md) — decisions
S1 Pumps slice adds `cableEndPose|null`; cable ends, like hose ends, are not in Placement.
S2 Timers: 0 s (inactive: Shipping Ready, Landed/Opened crates, an Inbound crate waiting at 0) loads as 0 ticks; `max(1, round(s/dt))` only when saved seconds > 0.
S3 Load chain: a newer-version `.json` stops the chain (no `.bak` fallback; message + offer New Game, which moves it aside per Rule 7 before the first write). On any `.bak` load (corrupt `.json`), the bad `.json` is moved aside to `save_0.corrupt-{utc}.json` before the first write, then `File.Move`/rotation proceeds so the good backup is never overwritten by the corrupt file. Both files corrupt: `.json` → `save_0.corrupt-{utc}.json`, `.bak` → `save_0.bak.corrupt-{utc}.json`. ACs for each.
S4 Retry: at most 3 attempts in total over 200 ms, on all platforms; `IFileSystem` maps errors to `disk_full`, `locked`, `io_error` via HResult/error codes, not message text.
S5 Validation ids: no two records in the same collection share an id, and no id names two different objects.
Recommended: Placement saves item kind with each loose item (`{id, item, pose}`); AC-01 fixture ends with no crank flag set; a failed save does not reset the 120 s autosave timer (next trigger = next purchase or the next 120 s mark); Rule 11 adds: a `cableSocket` whose socket no longer exists loads unplugged with the end at the pump; Placement items whose kind was removed are dropped and logged; removed reveal or radio line ids are ignored; cite rules by name not line; autosave interval range matches Game State's knob (owner: Game State; use its range); HUD drop icon minimum display 0.5 s (HUD); state that the 8 ms budget excludes the real-disk `Flush(true)`, which is measured separately in a build.

## Hand Tools (hand-tools.md) — decisions
T1 AC-27: Nour on a fresh game at their natural pace (no rushing); bucket bought 0:50–1:30 (Economy AC-33), in hand about 12 s later; read from the play log. Drop "first-time tester" from the watch item.
T2 New game: the Hand Tools slice holds the spoon at 0 L, Placement's hands item is the spoon, and an `ownedAtStart` tool has no crate and is usable from the start. AC-16 gets that GIVEN.
T3 Pour time (bucket 0.8 s, spoon 0.3 s) is Hand Tools' own data (a Hand Tools tuning asset), not the Economy table; fix every "from the Economy table" mention.
T4 The HUD doc now draws "Spoonful #N" and the quips (HUD Rule 4, being added in parallel); cite HUD Rule 4.
Recommended: buffered press keeps the type and target it had at press time, sent when the last lock ends; presentation counts accepted scoops from the tool's held amount rising; AC-26 "at least 8 cm, measured along the ground from the storage-spot viewpoint screenshots"; AC-25: keep 15–25 s as the level target and say on failure set the sim's `TRIP_POND` to the measured median and re-run (Water Source estimate 12–14 s noted); line 32: 1.25 s is the scoop cooldown, a real scoop-turn-pour cycle is about 1.6 s; add `StubPumpDraw` (step 2b Withdraw caller) and say the source stub raises `SourceDrained` at 2d; line 56: Pour is sent on a container (Use on a source always sends Scoop); cross-ref Hose rule 10; Water Source AC-24 citation → its visual requirements.

## Round 2 (after verification + FPC re-review), 2026-11-04

### Hose
- AC-26: drop the "barrel holds 0 L on the staging spot / pour returns not_installed" clause; assert the old barrel id no longer exists.
- FitInlet: `not_holding` = "holds no drum inlet fitting" (any), so `already_fitted` is reachable; AC-36 consistent.
- Nits: d_p range 0–2.0; intake end position = "the point aimed at in the zone"; Rule 13 says Remove(old) happens before Move(new); Rule 15 covers a loose end 1 as well as end 0.
### Pumps
- Lines ~31, 43, 76, 157: suction end is "bound with Use on the intake zone", never "dropped".
- PlugCable: add `not_holding` as the first check (issuer must hold that pump's cable end); FPC row 17 gets it too.
- Nits: rate_table range align to 4–10 (Pumps tuning) and say Hose rate_p 0–10 is the general bound; Rule 6 name the Placement calls (Pumps.CanPickUp / CableEndHeld / CableEndDropped(pose), mirroring Network's) that Game State Rule 12 cites; define the hands item `CableEnd(pumpId)`.
- Rule 4 CrankStop: keep (menu or hands change sends CrankStop); FPC gets the matching rule.
### Economy
- Rule 12: delete "moves the old one onto the staging spot" (one Move, old container Removed).
- Duty range 0.5–0.9.
- actionAdded text: "Use the suction end on the pond".
- Confirm in Economy (or add) that a waiting crate lands on the first tick a crate spot is free (Save AC-23 relies on it). If Economy says otherwise, change Save AC-23 to match Economy.
### Shipping
- Launch hover/prompt "pays {N} cr": N = floor(Economy.PayoutFor(min(padPool, cargo))/1000).
### Save
- Pumps slice lists cableEndPose (check it is there); stale "Hose edge case l.144" reference -> cite by name, not line.
### Game State
- Rule 12: remove the duplicate "the Network" in the direct-callers list. Hands item list adds CableEnd(pumpId) if Pumps defines it.
### FPC
- B1: rejection text looked up by (last interaction request type sent, code), fallback to the code's default text. Controller remembers the last interaction request it sent (one in flight in MVP). AC-13 checks request/code pairs plus a per-row case: BoltMotor + already_fitted -> "Motor already fitted"; FitInlet + not_holding -> "Hold the inlet fitting".
- B2: Rules 5 and 10: a hands change (SelectTool or PickUp) or opening any UI-map menu sends CrankStop.
- Launch "pays {N} cr" uses floor(PayoutFor(min(pad pool,cargo))/1000).
- Row 17 adds not_holding first (Pumps PlugCable).
- AC-04 prompt "Hold to crank". AC-18 shelf/lip numbers match Rule 1 (1:35–1:40, lip 1:12). Rule 2 or 9: held hose end clamped to hose length (20 m, suction 6 m) per Hose rule 15. Row 10 add `blocked`. Row 25 codes in Economy order too_far -> already_owned -> locked -> cannot_afford, note the latter two can't occur. Toggle mode: long press on pry/bolt/fit behaves as tap-and-hold-to-completion is NOT needed; in Toggle mode a press starts and a second press cancels for those holds too. no_intake prompt: "Connect the suction hose to the water". AC-14: hands hold HoseEnd(hoseId,0). AC-19 Order hover price 220 (check against Economy). Mud tagging: walkable mud is tagged by Drawdown's exposed-bed mesh (cite Drawdown). Last Updated 2026-11-04.
