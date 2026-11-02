# Design Review: Economy, Progression & Shop

> **Date**: 2026-11-02
> **Doc**: `design/gdd/economy-progression-and-shop.md`
> **Mode**: full (economy-designer, game-designer, systems-designer, qa-lead; creative-director synthesis)
> **Verdict**: **NEEDS REVISION**

## Summary

The vision, the pillar fit and the doc structure hold up. The numbers do not: the pacing model contradicts itself, and the storage-ladder beat the doc promises (fill the barrel, launch every 10 trips) never happens under its own launch rule. Most fixes are mechanical. The one real design question is how Nour's 10 / 100 / 200 L ladder pays off (D1). The ladder itself stays.

## Blocking (7)

| # | Problem | Raised by | Fix |
|---|---|---|---|
| B1 | Pacing model wrong; AC-15 fails on the doc's own formula. The hand-tool formula charges the lever on every trip (spoon 0.0035 L/s, not 0.012). The pond % column ignores the 40 L puddle (hand pump arrives at ~0.2% drained, not 0.4%). Puddle trips are ~5 s (Hand Tools Rule 6), not 20 s. Bucket `cycle_s = 20` would throttle scoops to one per 20 s. | QA, Systems, Economy | One simulator: lever once per launch, ~5 s trips at the puddle and 20 s at the pond, puddle counted. Separate scoop cooldown from trip estimate. AC-15 checks simulator output against a fixture. Update table, % column, and times quoted in the Water Source and Drawdown GDDs. |
| B2 | Storage beat is false. Under "launch when full or when it affords the next item" the barrel never holds more than ~28 L, the drum ~38 L. A player who fills the barrel gets 1,000 cr and buys drum and hand pump back to back. | Economy, Game design | Take D1, rewrite the beat text to match, add an AC for the chosen behaviour. |
| B3 | Linear shop order not encoded. Only hand_pump has a prereq (doc and registry): the drum can skip the barrel, the motor can precede the hand pump. | Economy, QA, Systems | Chain prereqs bucket → barrel → drum → hand_pump → motor → next. `ownedAtStart` column instead of price 0. Load checks (prereqs exist, no cycles, price ≥ 0, multipliers > 0). AC. |
| B4 | Earth counter and save slice break one-owner-per-slice. Rule 10 stores the counter; the save row holds Shipping's total and omits pending deliveries. | Systems, QA | Derive the counter from Shipping's total, never save it, clamp floor 1 L. Save: credits, owned items, pending deliveries. |
| B5 | Number-representation ADR still open; approved Game State says it must precede finalising Economy. Float sums can floor 4.9999 to 4. | Systems | Run the ADR (whole millilitres and millicredits is simplest), or keep status "Approved pending ADR". |
| B6 | Crates have no lifecycle. Nothing owns crate state after landing; no OpenCrate request; 7 purchases of litter vs "at least 3" pad spots. | Systems, QA, Game design | Economy slice records crates as inbound / landed / opened. Add OpenCrate to Game State Rule 12. Next drone collects empties ("packaging recycled at your expense"). Define full-pad behaviour. |
| B7 | Several ACs untestable. | QA | AC-22 as an EditMode tick test (ORDER T → Purchased T+1 → crate at T+1+40), "in hand within 10 s" to Manual. Name stubs in AC-03/05/06/13/23/24/27. Add ACs for save during delivery and rejected purchases. AC-21 names its floor. |

**Downgraded:** no trade-offs (Game-1) is Nour's call (D3); puddle anchor (Game-3) is a text fix, and "beside the dried mud where the puddle was" is better satire; output range (Systems-2) is a text fix; NaN and negative guards (Systems-3/4) are one-line hygiene.

## Recommended

- R1 Exempt spoon → bucket from the rising-time rule (D2).
- R2 Output range: two pumps give 16 L/s; 13.3 is the drum cap. With one pump the IBC adds nothing, say so or reorder (D3).
- R3 Shortfall text "ship 13 L", not 26. Spoon moves 0.72 L/min, not 0.8.
- R4 Registry payout note: paid by the direct `CreditLaunch` call, not on `LaunchCompleted`.
- R5 Storage swap order: move old → place new → pour across; remove the old node once empty.
- R6 AC-25: lazy-launcher case and tolerances (bucket 0:45 ±15 s; hand pump by 4:30; second pump 18–22 min).
- R7 AC-26 vs UI: "text and icon".
- R8 Satire copy pass: payslip job title per tier; end-of-store joke item (Employee of the Year Plaque).
- Also: show the full catalogue with prices; hold-to-pry crates with a spec sheet; Cross-References no longer "not yet written"; hose cap and length stated in the doc since the registry sources them here.

## Decisions for Nour

- **D1. How the 10 / 100 / 200 L ladder pays off** (ladder unchanged).
  - **A (recommended): reframe the beats.** Barrel = "never wait for the rocket" (at the puddle a full jerrycan stalls you through the 15 s turnaround). Drum = fitting the hose inlet is its own action, the hand pump's prerequisite. No new rules; players launch early.
  - B: the Hydration Levy starts at the barrel, a flat 30–50 cr per launch, so full launches pay best. Strong satire; hand stage ~20–30% longer.
  - C: minimum load from the barrel on (half full). Conflicts with Shipping Rule 7; first pump toward 5:30.
- **D2. Spoon stage length.** Hit the concept's 60–90 s with a bucket at ~8 cr, exempt from the rising rule (recommended), or keep ~45 s.
- **D3. Trade-offs in the MVP.** Stay linear but put pump 2 before the IBC so both are felt: 8 → 13.3 L/s, then the IBC lifts to 16 (recommended). Or add one either-order pair now. Revisit after the first playtest.

## Reviewer claims found wrong

- Game-2: the spoon's 60–90 s does have a concept source (game-concept.md line 126).
- Economy-3: the double buy is real but doesn't break AC-18 or Rule 8; the hand pump stays Hidden until the drum is owned. It hurts the beat, not the rule.
- Economy-5: the 15 s cap doesn't bind at the pond, but does at the puddle with ~5 s trips.
- QA-1 / Systems-1: "41.7 vs 36.8 s" mixes two formulas. Either formula applied consistently still fails AC-15.

## Reviewer sims

Economy reviewer's 10 Hz sim (launch when next item affordable): purchases at 0:47 / 1:42 / 2:45 / 4:08 / 7:23 / 12:07 / 20:42. Prices match the registry: 5 / 160 / 280 / 380 / 1,600 / 18,000 / 40,000.
