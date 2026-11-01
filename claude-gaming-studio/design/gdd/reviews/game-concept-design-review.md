# Design Review: game-concept.md

> Date: 2026-11-01 · Reviewers: game-designer, systems-designer, economy-designer, creative-director (senior)

## Verdict (creative director): NEEDS REVISION

The game's identity, pillars and scope tiers are sound. The doc lacks the prototype's lesson: it still opens with a bucket, its MVP hypothesis can't be proven wrong, and it gives no pacing targets for later GDDs to aim at.

**Scope signal:** Right as tiered. The solo 3–5 h release in 12–18 months is believable only if co-op stays deferred and the release keeps to 2 regions.

## Must-fix before /gate-check

1. **Make the opening tool consistent.** Replace "starting with a bucket", "one bucket at a time", "from a bucket", "carry and pour" and "nine pump tiers from bucket" with a short hand-tool stage (spoon to bucket) before the pump tiers. Cap the spoon at ~60–90 s as a satire beat. Cut the cup unless it gets its own physical action.
2. **Extend Pillar 2's design test to hand tools and launching.** Each hand tool adds a new action or reach, not just more litres. Automated launching is built and maintained by hand.
3. **Rewrite the MVP hypothesis as two failable tests:** (a) a timed bucket-only stretch holds attention; (b) the ladder keeps a player 15+ min, first pump in ≤4 min. 2–3 external testers. Add to MVP: one corporate radio line per launch, and visible water drop or a reveal within the first 2 minutes.
4. **Add a "Pacing Targets" block.** Minutes per tier rise, not shrink; reveal spacing; hours per region for the 3–5 h release. "Prices tuned by playtest against these targets." Make the 1-trillion-litre final contract an open question.
5. **Consistency nits.** Session length 30–90 vs 30–120 min; Fantasy and Submission both ranked 3 in MDA; the 2× footprint rule applies to machines only.

## Deferred to later docs

- Extraction formula `min(rate·dt, remaining, pipe, storage)` → Water Model GDD
- `double` vs `long` litres, credit rounding → architecture ADR
- Price/payout curve, credit sinks, per-region multiplier → Economy/Progression GDD
- Verbs per tier, hidden chores (key holding, launch every 10 s) → Extraction Tools GDD
- Either/or autonomy purchases → Shop/Progression GDD
- "≤3 bottlenecks" enforcement → Logistics GDD
- Litres per ice weight class → Ice Harvesting GDD
- Source volumes per region → /map-systems, Region GDDs
- Lake size vs ladder total (lake runs out) → v2 prototype tuning before the next playtest

## Where the creative director overruled specialists

- Lake running out, endgame math and number precision are real but not concept blockers (prototype tuning, later GDDs, architecture).
- No per-tier verb list or siphon tier in the concept: state the rule, cut the cup rather than add tiers.
- Upgraded: "nothing visible changes for ~12 minutes" is must-fix (Pillar 3 failing in the riskiest stretch).

---

## Appendix: specialist findings

### Game designer
[game-designer]
BLOCKING
1 Core Loop/Pillars 1-2: spoon/cup/bucket same verb, rate upgrades. Fix: distinct verb + reach per tier: spoon scoops shoreline puddles into a jar; bucket = wade in, weight slows you; siphon = suck-start hose (new tier before pumps); hand pump = crank rhythm; electric = run cable + refuel generator. Merge/cut cup.
2 Chore math: v2 ~11 min before pump (spoon->cup ~3m, cup->bucket ~5m, bucket->pump ~3.5m); v1 tester quit ~6m. Fix: cap spoon phase 60-90 s (tutorial beat or 1-2 cr cup); time-to-first-pump target <=4 min tracked.
3 Pillar 2 vs auto-launcher: build by hand (fill rail, snap tank hose to rocket socket, set timer valve), needs upkeep (clogged intakes).
4 MVP hypothesis unfalsifiable (says no upgrades doing work but MVP has shop; prototype shows upgrades carry). Split: (a) timed bucket-only stretch holds attention; (b) ladder pacing hits 15+ min. Update "one bucket at a time" (Core Fantasy), "carry and pour" first (Flow), pump tiers "from bucket" (Long-Term).
RECOMMENDED
5 v2 economy: lake 6000 L pays ~6000 cr, ladder costs 5135 -> auto-launcher bought with last few hundred L; industrial drains rest in ~1 min. Fix: 3-5x lake or second source.
6 Autonomy: ladder is linear; add an either/or purchase (tank vs industrial pump).
7 Pillar 2 post-automation: hands move intake hose to follow receding shoreline (ties to pillar 3).
8 MVP has no satire (pillar 4 untested): one corporate radio line per launch paired with a reveal.
9 Discovery ranked #1 but 2 reveal objects: make "≥1 readable object per 10% drained" an MVP requirement.
NICE
10 MDA Fantasy & Submission both rank 3. 11 Session length 30-90 vs 30-120 mismatch. 12 2x footprint rule can't apply to hand tools; separate scale rule.

### Systems designer
[systems-designer]
BLOCKING
1 v2 ladder consumes lake: ladder 5135 cr vs 6000 L lake -> ≤865 L left at auto-launcher; degenerate last purchase. Lake ≥3x ladder total or carry economy to other sources.
2 long litres breaks small amounts (5 L/s at 60fps = 0.083 L/frame -> 0). Litres in double (or long millilitres); credits long with rounding rule. float fails at 1e12.
3 Endgame scale: 1e12 L contract at 1e6 L/s = 278 h vs 15-25h campaign; need many extractors or cap contracts ~1e10-1e11; source volumes must scale with rate; none given per region.
4 "Rate limited by what remains" no formula: extracted = min(rate*dt, remaining, pipe cap, free storage) + floor.
RECOMMENDED
5 "≤3 bottlenecks" unenforceable (only 3 categories named; 4 limits in Core Mechanics); redefine as "region introduces ≤N constraint types".
6 Baseline conflict: bucket "0.5 L/s effective" vs spoon ~0.02-0.03 L/s; span 4e7x; fix "nine pump tiers from bucket", "one bucket at a time".
7 No visible drawdown first 10-15 min: spoon/cup/bucket ~35 trips remove 135 L = 2.25%; first band at 10%. No reveal in riskiest stretch.
8 Pacing collapses after pumps: hand pump->motor 80s, motor->tank 30s, industrial->auto 25s; rate/tank forces manual launch every 10 s, making auto-launcher feel necessary.
9 Ice conversion undefined: litres per weight class, losses.
10 Need a per-tier price/payout curve across 9 orders of magnitude before content work.
NICE 11 session length mismatch; 12 MVP hypothesis vs shop; 13 tier ratio target (~6x avg); 14 map content counts to tiers per release.

### Economy designer
**Economy review: game-concept.md, v2 ladder, and the GDD cost table**

**Time per tier, solo, v2 ladder.** Time per tier = cost ÷ effective rate, with a carry trip of 15–25 s (20 s used). This leaves out setup, walking to the launcher and the 200 L tank cap, so real times are longer, mostly in the pump tiers.

| Tier → next buy | Effective rate | Cost | Time | Cumulative |
|---|---|---|---|---|
| Spoon → cup | 0.025 L/s | 5 | 3.3 min (2.5–4.2) | 3.3 min |
| Cup → bucket | 0.1 L/s | 30 | 5 min (3.75–6.25) | 8.3 min |
| Bucket → hand pump | 0.5 L/s | 100 | 3.3 min | 11.6 min |
| Hand pump → electric | 5 L/s (held) | 400 | 80 s of holding | 13 min |
| Electric → tank | 20 L/s | 600 | 30 s | 13.5 min |
| Tank → industrial | 20 L/s | 1,500 | 75 s | 14.8 min |
| Industrial → auto-launcher | 100 L/s | 2,500 | 25 s | ~15 min |

**BLOCKING**
1. **The time per tier shrinks as you go up (Core Loop, Short-Term; v2 ladder).** About 75% of playtime is spoon, cup and bucket work. Every tier after that takes 25–80 s. Costs grow about 2.5–6× per tier while rate grows 4–10×, so the payoff tiers rush past and the chore stretch drags. The concept's "5–15 minutes per upgrade" holds only in the spoon/cup/bucket stage.
2. **The lake runs out (Long-Term; v2).** All purchases add up to 5,135 credits. At 1 credit/L that is 5,135 L out of a 6,000 L lake, leaving 865 L, which is about 9 s of industrial pumping. The auto-launcher is bought with almost nothing left to launch.
3. **Nothing visible happens in the hand stage (Pillar 3 vs v2).** The first stain band or reveal comes at 10% drained (600 L). By the end of the bucket stage only 135 L has been taken. So roughly the first 12 minutes, the riskiest chore stretch, show no world change. Fix this with steep shallow edges or an early first threshold.
4. **The 15–25 h target doesn't match the cost table (Long-Term; GDD costs).** Each GDD tier costs about 120–150 s of the previous tier's output: 2,500/20, 12,000/100, 60,000/500, 350k/2,500, 2M/15k, 15M/100k. The full nine-tier ladder is about 18 minutes of pumping, which is 50–80× short of the target. The other way round, the 1-trillion-L final contract needs about 278 h even at a constant 1,000,000 L/s. Hours have to come from source volumes, region gates and contract sizes, and none of these are specified yet.

**RECOMMENDED**
5. **Single currency at 1 credit/L (Anti-Pillars; Core Mechanics).** Income equals litres, so rate is the only lever. There is no sink after the last purchase: about 3.6 billion credits/h at the planetary extractor with nothing to buy. Contract payouts may also count the same litres twice. Keep one currency, but add a credits-per-litre multiplier per region or contract as a tuning knob, and give late credits a sink (maintenance, redeploying machines).
6. **Numbers don't agree.** The electric pump is 400 credits in the prototype and 500 in the GDD. Session length is "30–90 min" in Core Identity and "30–120" in Session-Level. The 30-minute onboarding has no spoon or cup, and v2 pushes the hand pump from 6–10 min to about 12 min.
7. **Hidden chores.** The hand pump means 80 s of holding a key. With a 200 L tank, the electric pump needs a manual launch about every 10 s until the tank upgrade.
8. **"Set by playtesting, not formula" (Open Questions).** This is fine for prices, but the doc still needs a target structure: minutes per tier, reveal spacing, and hours per chapter. Without that, playtest data has nothing to calibrate against.

**NICE-TO-HAVE**
9. A spoon-only lake would take 50–83 h. Use it as a satire beat on the radio, not as a pacing value.
10. Log time between buys and time to each 10% drained in the v2 CSV to check this table.
