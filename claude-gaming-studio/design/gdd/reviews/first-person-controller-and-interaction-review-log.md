# Review Log: first-person-controller-and-interaction

## Review — 2026-11-02 — Verdict: NEEDS REVISION
Review mode: batch (2 specialists + shared creative-director synthesis)
Specialists: gameplay-programmer, game-designer, creative-director (senior)
Reviewed-Content-Hash: design/gdd/first-person-controller-and-interaction.md d5bf65ee3d222f105cc005361c32fcf3846e588b
Reviewed-Content-Hash: design/registry/entities.yaml fe97a62a1efe88420e4a750fb7677dc0e6e868b0
Reviewed-Content-Hash: .claude/skills/design-review/SKILL.md a63d8fba9b7142eeb43791f061e2e1533c429b43
Full report: design/gdd/reviews/batch-design-review-2026-11-02.md (section for this doc, plus Cross-doc fixes)

Prior verdict resolved: First review

### Revision — 2026-11-02 (same session; Nour chose "Fix all: bucket 6 cr + toggle")
All blocking items in the review report fixed, plus the cheap recommended items; shared cross-doc fixes applied (tick boundaries, timers on disk, pad-pool shortfall line, owner-defined requests with FPC's binding and rejection-text table, no state change from `Purchased`, pad pool and litres aboard in conservation, eye-point ranges, Hold/Toggle setting, pillar names). Supporting amendments: Game State (Rules 1, 3, 4, 7, 12; status stays Approved), Economy (bucket 6 cr, new timings, litresShort, PayoutFor, socket counts), Water Source (basin outcomes, pacing times), registry.
Cross-doc light check (fresh systems reviewer): 3 blocking (FPC rejection-code coverage, two intake mechanisms, stale Save Network slice) and 9 minor mismatches; all fixed the same session.
Revised-Content-Hash: design/gdd/first-person-controller-and-interaction.md 83fc9e235cc000ff871ffc95401f5ef4ccd791e2
Revised-Content-Hash: design/registry/entities.yaml 3699ba8284a65f0f34d18c0aec6dd3f3697fd140

Status after revision: In Review. A fresh /design-review decides whether it is Approved.

## Re-review — 2026-11-04 — Verdict: NEEDS REVISION (then fixed and Approved)
Review mode: full (fresh re-review)
Specialists: 2 fresh reviewers (doc-specific pair)
Reviewed-Content-Hash: design/gdd/first-person-controller-and-interaction.md 83fc9e235cc000ff871ffc95401f5ef4ccd791e2
Prior verdict resolved: Yes (2026-11-02 blockers confirmed fixed)

Fixes from this re-review applied 2026-11-04 (decisions in Nour's review notes, round 1/1b/2), plus a cross-doc pass across Economy, Hose, Pumps, Shipping, Save, HUD, FPC and Game State.
Main fixes: rejection text keyed by (request, code) with a code fallback; hands change or any menu sends CrankStop; Launch payout uses floor(PayoutFor/1000).
Revised-Content-Hash: design/gdd/first-person-controller-and-interaction.md 3bd68db8301952a492456393bdaeb4abec6664b0
Revised-Content-Hash: design/registry/entities.yaml 3699ba8284a65f0f34d18c0aec6dd3f3697fd140

Status after revision: Approved (2026-11-04).
