## Gate Check: Concept → Systems Design

**Date**: 2026-11-01
**Checked by**: gate-check skill (workflow: full, review_mode: full, team.size: studio)

### Required Artifacts: 3/3 present
- [x] `design/gdd/game-concept.md`: exists, 449 lines, real content
- [x] Game pillars: 4 pillars, each with a design test, plus anti-pillars (in the concept doc)
- [x] Visual Identity Anchor: present, with a one-line rule and 3 principles

**Recommended**
- [x] Concept prototype: `prototypes/lake-loop-concept/REPORT.md`, verdict PROCEED (CD-PLAYTEST concerns accepted)

**Not on this gate's checklist (workflow catalog lists them as Concept-phase steps)**
- [ ] `design/art/art-bible.md`: absent (catalog: "before systems design")
- [ ] `design/gdd/systems-index.md`: absent (`/map-systems` is the first Systems Design step)

### Quality Checks: 4/4 passing
- [x] Concept reviewed: `/design-review` verdict NEEDS REVISION (not MAJOR); all 5 must-fixes applied the same day. The revised doc has not been re-reviewed.
- [x] Core loop described: four time scales plus Pacing Targets
- [x] Target audience identified: Target Player Profile, Bartle, motivation
- [x] Visual Identity Anchor: one-line rule ("Every surface remembers where the water was") and 3 principles with tests

### Director Panel Assessment (4 of 4, workflow: full)

Creative Director: CONCERNS
  The artifacts are good and the fixes are visible. The core fantasy is only half shown: Pillar 4 satire is untested. The evidence is thin (one developer playtest, v2 unplayed). The v2 prototype drifts from the pillars (a bought auto-launcher, a cup that is just a rate upgrade) and must not set a precedent for the GDDs. The revised doc needs a quick re-review or Nour's sign-off on the diff.

Technical Director: READY
  Feasible on Unity 6.6 URP; the prototype compiled clean; the engine config is consistent; the risks have mitigations. Carry forward: separate state from visuals per system; hoses as a socket graph; settle the number scale and rounding in the economy GDD; with domain reload off, avoid static game state; 6.6 is not an LTS; Win64-from-Mac build and v2 compile are unchecked.

Producer: CONCERNS
  Thin evidence; the economy blockers were deferred into design (the first critical-path problem); full/studio rigor is heavy for a solo first-time developer. Design only the MVP systems: 3–4 GDDs (water model, economy/pacing, hand tools + pump/hose, optionally shipping), timeboxed to 2–3 weeks, with v2 played by 2–3 external testers before the economy GDD is finalised.

Art Director: CONCERNS
  The Visual Identity Anchor is enough to enter Systems Design. Colour conflict: brand teal (signage/UI) vs "water is the only fully saturated cool hue"; the interactive accent colour is unnamed and could clash with safety-orange-only-on-player-machinery. Visual rules (stain bands, lakebed swap, a reveal per drawdown, 2× footprint) are GDD requirements. Run `/art-bible` early in Systems Design.

### Blockers
None.

### Concerns
1. The revised concept doc has not been re-reviewed (re-run `/design-review design/gdd/game-concept.md`, or Nour signs off on the diff).
2. Thin play evidence: one internal session, v2 not compiled or played, and neither MVP test run.
3. Pillar 4 (satire) is untested.
4. Colour conflict in the Visual Identity Anchor (teal UI vs water hue; accent colour unnamed).
5. The economy pacing problems (shrinking tier times, lake runs out, no early visible change) carry into Systems Design as the first problem to solve.
6. The art bible is not written yet.
7. Process weight: rigor full/studio for a solo first-time developer (consider `modes.rigor: standard`).

### Recommendations
- Fix the teal/accent colour wording in the concept (about 10 minutes).
- `/map-systems`: tag each system with its pillar, treat radio/satire as a real system, and design only the MVP systems.
- Short `/art-bible` alongside the first GDDs (palette, colour meanings, lakebed staging, tier scale).
- Compile and play v2 with 2–3 external testers, including a timed bucket-only stretch.

### Follow-up the same day (Nour: "fix what can be fixed")
- Concern 1 RESOLVED: Nour signed off on the revised concept doc.
- Concern 2 PARTLY RESOLVED: Nour played v2 (so it compiles and runs) and judged it good enough. External testers are not available for now, so the evidence is still from the developer only.
- Concern 4 RESOLVED: colour wording fixed (warm brand colours for UI and signage; safety orange marks interactive parts, which are only on the player's tools and machinery).
- Still open: 3 (satire untested), 5 (economy pacing for the first GDDs), 6 (art bible not written), 7 (rigor setting), plus external playtests when testers are available.

### Verdict: CONCERNS
All required artifacts and quality checks pass and there are no blockers, but three of four directors returned CONCERNS. The stage advances only if Nour explicitly accepts the concerns above.

Chain-of-Verification: 5 questions checked (2 by tool: re-read the concept's section list; confirmed the teal/accent lines at the Visual Identity Anchor). Verdict unchanged. No concern rises to a blocker: each is resolvable inside Systems Design and none stops GDD work from starting. Together they argue for a short, MVP-only Systems Design phase, not for holding at Concept.

### Accepted Risks
- Pillar 4 (satire) untested — accepted by Nour, 2026-11-01
- Economy pacing (tier times, source volume vs ladder, early visible change) to be solved in the first GDDs — accepted by Nour, 2026-11-01
- Art bible not yet written — accepted by Nour, 2026-11-01
- Process rigor (full/studio) heavy for a solo developer — accepted by Nour, 2026-11-01
- Play evidence from the developer only (no external testers for now) — accepted by Nour, 2026-11-01

Stage advanced: Concept → Systems Design (2026-11-01).
