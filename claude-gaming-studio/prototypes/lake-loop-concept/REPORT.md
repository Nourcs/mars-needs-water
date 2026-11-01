# Concept Prototype Report: Lake Loop

> **Date**: 2026-11-01
> **Prototype Path**: Engine (Unity 6.6, editor 6000.6.7f1)
> **Concept File**: design/gdd/game-concept.md

---

## Hypothesis

"If the player fills a bucket at the lake, pours it into a tank, then works a pump and launches
rockets, the loop will feel satisfying as the lake visibly drops — evidenced by the player playing
**10+ minutes unprompted** and **reaching the electric pump upgrade**."

---

## Riskiest Assumption Tested

That hand-carrying water feels good rather than like a chore (pillar 2, Hands on the Machinery).
**Proved out:** the manual start was not reported as a chore. The playtester's ask was the
opposite: start *even smaller* than a bucket.

---

## Approach

One throwaway C# script that builds a grey-box level at runtime when SampleScene is played:
square basin with a dropping water plane, stain bands every 10% drained, a sunken car and sign
revealed as the water falls, a bucket (fill/pour on a short hold), a 200 L tank, a launch button
that pays 1 credit per litre, and a shop selling a hand pump (100) then an electric motor (400).
Written and compile-checked in one session (batchmode, exit 0, no warnings).

**Path chosen:** Engine
**Reason for path:** the question is about feel (carrying, pouring, watching water drop), which a
browser or paper build can't answer.

**Shortcuts taken (intentional):**
- Hardcoded tuning, one file, own assembly (`Prototypes.LakeLoop`) so production can't reference it
- Primitive cubes only; OnGUI text HUD; no audio, menus, saving, contracts, glaciers or co-op
- Hoses are straight lines between fixed points (no placement, no physics)
- No telemetry in v1 (added in v2 after the playtest, see below)

---

## Result

Playtester: Nour (developer), one session, ~5–6 minutes, Unity editor on macOS.

- First reaction: **"LOVE IT"**.
- Best moment, in their words: **"It's satisfying going from filling a bucket to buying a pump and
  then automate it."** They reached the electric motor (automation) within the session.
- Feedback: **"I feel we should start even smaller than a bucket. Like a spoon or something"** and
  **"Add more steps."**
- No bugs or control problems were reported. Worst moment: not reported.
- Session ended at ~5–6 minutes. Most likely cause (inference, not confirmed): the v1 ladder had only
  two purchases, so after the electric motor there was nothing left to do.

---

## Metrics

| Metric | Value |
|--------|-------|
| Path used | Engine |
| Iterations to playable | 1 (compiled and ran first time) |
| Prototype duration | ~1 hour build (same day) |
| Playtesters | 1 internal (developer), 0 external |
| Feel assessment | The bucket → pump → automation progression felt satisfying; the start felt too big rather than too slow ("start even smaller") |
| Hypothesis verdict | PARTIALLY CONFIRMED — reached the electric motor (signal met), but played ~5–6 min, under the 10-minute target |

---

## Recommendation: PROCEED

The riskiest assumption held: manual water work wasn't a chore, and the jump from hand work to a
pump to automation is the moment the playtester singled out — exactly pillar 1 (Start Tiny, End
Enormous) and pillar 2. The missed 10-minute signal is most plausibly a content limit (two
upgrades), not a loop failure, and the player's own requests ("start smaller", "add more steps")
ask for *more* of the same loop. Caveat: one internal tester who is also the designer; this needs
fresh external testers before it counts as strong evidence.

---

## If Proceeding

- **Core tuning values discovered:**
  - Start smaller than a bucket. v2 ladder: spoon 0.5 L → cup 2 L (5 cr) → bucket 10 L (30) →
    hand pump 5 L/s (100) → electric motor 20 L/s (400) → 1,000 L tank (600) → industrial pump
    100 L/s (1,500) → auto-launcher (2,500); lake 6,000 L.
  - v1 values that worked: 1 credit/L, hand pump at 100, electric at 400, 200 L tank.
- **Assumptions confirmed:** manual extraction is satisfying as a start; the hand-work → automation
  jump is the emotional payoff; a numeric lake driving a moving water plane reads clearly.
- **Assumptions disproved:** the GDD's bucket is not small enough as the first tool — the game
  should open even tinier (spoon/cup) to make the scale jump bigger.
- **Emergent mechanics:** none observed beyond the designed loop (single short session).

**Next steps:**
1. Play v2 (spoon ladder + CSV play log) and, ideally, watch 2–3 people who haven't seen it
2. `/design-review design/gdd/game-concept.md` (add the spoon/cup tier to the concept)
3. `/gate-check`
4. `/art-bible`, then `/map-systems`
5. `/design-system [mechanic]` (use these values in Tuning Knobs and Formulas)

---

## Lessons Learned

- **What assumptions were broken by actually building this?**
  The concept's starting tool was too big. "Start tiny" needs to be literal: a spoon against a lake.

- **What surprised us that didn't show up in the brainstorm?**
  How quickly the player wanted the *next* step: the loop is pulled forward by the upgrade ladder,
  so the ladder's length and spacing is the main pacing lever.

- **What would we test differently next time?**
  Build the play log in from the start (v1 had none, so session length came from memory), and
  test with someone other than the designer.

---

## v2 (built after the playtest, not yet played)

Longer ladder starting from a spoon (values above), 6,000 L lake, auto-launch, bigger tank, and a
CSV play log written to `MarsNeedsWater/PlaytestLogs/` (fill, pour, launch, buy, every 10% drained).
Not yet compile-confirmed: the editor was open when it was written.

---

> *Prototype code location: `MarsNeedsWater/Assets/Prototypes/LakeLoopConcept/` (notes in
> `prototypes/lake-loop-concept/`).*
> *This code is throwaway. Never refactor into production.*

---

## Creative Director Review (CD-PLAYTEST): CONCERNS

- **Core fantasy:** half present. The pleasure half (hand work → pump → automation, visible drawdown) lands; the "uneasy knowledge of the cost" half (pillar 4) is untested.
- **Thin evidence:** one internal tester (the designer), 5–6 minutes, under the 10-minute target. A signal, not validation.
- **Upgrades may be carrying the loop:** the best moment named was the ladder, and the session ended when it ran out; the trip on its own isn't shown to be fun yet.
- **Spoon tension:** helps pillar 1 and the satire, but spoon/cup/bucket are rate upgrades (same action, more litres), which pillars 1 and 2 warn against; a 0.5 L trip is the strongest chore risk and is unplayed. The concept doc still says "one bucket at a time".
- **Auto-launcher (v2) drifts from pillar 2:** automation bought as a button instead of set up by hand.
- **Before proceeding:** test v2 with 2–3 fresh players, time a spoon-only stretch, give each early tier its own physical action, make the auto-launcher something built by hand.

**Decision (Nour, 2026-11-01):** PROCEED — accepted with the noted concerns, to be addressed during design.
