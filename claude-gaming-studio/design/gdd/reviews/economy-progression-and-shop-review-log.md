# Review Log: Economy, Progression & Shop

## Review — 2026-11-02 — Verdict: NEEDS REVISION
Review mode: full
Workflow: full
Scope signal: M
Specialists: economy-designer, game-designer, systems-designer, qa-lead, creative-director (senior)
Blocking items: 7 | Recommended: 8
Reviewed-Content-Hash: design/gdd/economy-progression-and-shop.md ecd68ae2bdc7f5ff083400b4087af54e86b4e225
Reviewed-Content-Hash: design/registry/entities.yaml b6e756025e54c29ec911da8025751414a5d8c80f
Reviewed-Content-Hash: .claude/skills/design-review/SKILL.md a63d8fba9b7142eeb43791f061e2e1533c429b43
Full report: design/gdd/reviews/economy-design-review-2026-11-02.md

Summary: Vision and pillar fit hold. The pacing model contradicts itself (lever charged per trip, puddle ignored, AC-15 fails), the storage-ladder beat never happens under the doc's own launch rule, the linear order isn't encoded in prereqs, the Earth counter and save row break slice ownership, the number ADR is still open, crates have no lifecycle, and several ACs are untestable. Open decisions for Nour: D1 ladder payoff, D2 spoon length, D3 trade-offs.

Prior verdict resolved: First review

### Revision — 2026-11-02 (same session, Nour chose "Revise now, my picks": D1 option A, D2 bucket ~8 cr, D3 second pump before the IBC)
| Blocker | Fix applied |
|---|---|
| B1 Pacing model | New reference simulator (`tools/pacing/sim3.py`): lever once per launch, 5 s puddle / 20 s pond trips, puddle counted, install times stated. Bucket scoop cooldown 1 s separate from trip targets. AC-21 checks simulator output (now a [Script] check reading the table). Pacing table, % column and quoted times updated in Water Source, Hand Tools, Shipping and Drawdown. |
| B2 Storage beat | Rule 13: jerrycan = launch every trip and wait for the rocket at the puddle; barrel = never wait for the rocket; drum = screw-in hose inlet (`FitInlet`, Hose rule 2). Early launching is expected; filling first is allowed (edge case). |
| B3 Linear order | Prereq chain bucket → barrel → drum → hand pump → motor → second pump → IBC → plaque in table and registry; `ownedAtStart`; load checks; AC-11. |
| B4 Earth counter / save | Rule 11: derived from Shipping's total, never saved, clamped at 1 L; slice = credits, items owned, crate records; AC-26. |
| B5 Number type | Rule 3: whole millicredits (64-bit), rounded once per payout; stated as Economy's requirement on the pending ADR. |
| B6 Crates | Rule 8: Inbound → Landed → Opened in the Economy slice; `OpenCrate` (1 s pry); 4 pad spots; drone removes empty crates; full-pad wait. Game State Rule 12 amended with `Placement.Spawn/Remove/Move/SetHands` direct calls. |
| B7 ACs | AC-27 tick-exact delivery with stub Placement; AC-28 save during delivery; AC-13 rejections; AC-06/07 guards and exact sums; stubs named throughout. |
| Recommended (applied) | Spoon stage 60–90 s (bucket 8 cr, exempt from the rising rule); second pump before IBC; output range 16 L/s; "ship 13 L"; 0.72 L/min; registry payout note; `SwapStorage` (Hose rule 12, AC-26); lazy-launcher and tolerances in AC-33; "text and icon"; job titles, commendation slips, full catalogue, hold-to-pry with spec sheet, joke plaque. |

New prices: bucket 8, barrel 220, drum 260, hand pump 380, motor 1,100, second pump 15,000, IBC 40,000, plaque 100,000. Reference player: hand pump ~4:38, IBC ~16:00, longest gap ~2:42.

Light check (fresh systems/economy reviewer): all 7 blockers fixed; three new text-level issues (no sanctioned Placement move for the swap, AC-21 not runnable as EditMode, AC-29 tick timing) plus nits (stale numbers in Water Source and Drawdown, 10 s vs 12 s delivery promise, AC-31/32 in the wrong doc, "nearly doubles", `FitInlet` rejections, registry naming). All fixed the same session.
Revised-Content-Hash: design/gdd/economy-progression-and-shop.md 8fb954b971fff7a8bab7e873925b9f4496702b98
Revised-Content-Hash: design/registry/entities.yaml 484aefbeaef4df610a959d0f733312227c296ea2

Status after revision: In Review. A fresh /design-review decides whether it is Approved.

### Amendment — 2026-11-02 (from the batch design review; Nour chose "bucket 6 cr + toggle")
Bucket price 8 → 6 cr (40 scoops; Rule 9 note). Simulator re-run: bucket 0:55, puddle dry 1:45, barrel 1:50, drum 2:53, hand pump 4:18, motor 7:00, pump2 10:15, IBC 15:40; longest gap 2:42; full mode IBC 17:37 (gap 3:29); lazy IBC 27:20 (gaps 6:15, 5:45). AC-21 spoon step 45–90 s in the sim; AC-33 bucket window 0:50–1:30. `NextItemProgress.litresShort` subtracts the pad pool (shortfall owned by Economy, displayed by HUD); new AC-36. `launch_cycle` lives in the Economy table (globals). Status stays In Review.

## Re-review — 2026-11-04 — Verdict: NEEDS REVISION (then fixed and Approved)
Review mode: full (fresh re-review)
Specialists: 2 fresh reviewers (doc-specific pair)
Reviewed-Content-Hash: design/gdd/economy-progression-and-shop.md 92a7b9366293523d3d15e9177a1186a43cb938ff
Prior verdict resolved: Yes (2026-11-02 blockers confirmed fixed)

Fixes from this re-review applied 2026-11-04 (decisions in Nour's review notes, round 1/1b/2), plus a cross-doc pass across Economy, Hose, Pumps, Shipping, Save, HUD, FPC and Game State.
A separate verification pass checked the fixes and found a few small follow-ups (round 2); all applied the same day.
Revised-Content-Hash: design/gdd/economy-progression-and-shop.md 6033f3c89dfbaaad0b2f3263f175e7bdd8f5b93e
Revised-Content-Hash: design/registry/entities.yaml 3699ba8284a65f0f34d18c0aec6dd3f3697fd140

Status after revision: Approved (2026-11-04).
