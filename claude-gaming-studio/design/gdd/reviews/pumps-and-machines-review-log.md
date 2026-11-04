# Review Log: pumps-and-machines

## Review — 2026-11-02 — Verdict: NEEDS REVISION
Review mode: batch (2 specialists + shared creative-director synthesis)
Specialists: qa-lead, systems-designer, creative-director (senior)
Reviewed-Content-Hash: design/gdd/pumps-and-machines.md 913f0d4c9df0d49902ad7ec60db615c34f318a17
Reviewed-Content-Hash: design/registry/entities.yaml fe97a62a1efe88420e4a750fb7677dc0e6e868b0
Reviewed-Content-Hash: .claude/skills/design-review/SKILL.md a63d8fba9b7142eeb43791f061e2e1533c429b43
Full report: design/gdd/reviews/batch-design-review-2026-11-02.md (section for this doc, plus Cross-doc fixes)

Prior verdict resolved: First review

### Revision — 2026-11-02 (same session; Nour chose "Fix all: bucket 6 cr + toggle")
All blocking items in the review report fixed, plus the cheap recommended items; shared cross-doc fixes applied (tick boundaries, timers on disk, pad-pool shortfall line, owner-defined requests with FPC's binding and rejection-text table, no state change from `Purchased`, pad pool and litres aboard in conservation, eye-point ranges, Hold/Toggle setting, pillar names). Supporting amendments: Game State (Rules 1, 3, 4, 7, 12; status stays Approved), Economy (bucket 6 cr, new timings, litresShort, PayoutFor, socket counts), Water Source (basin outcomes, pacing times), registry.
Cross-doc light check (fresh systems reviewer): 3 blocking (FPC rejection-code coverage, two intake mechanisms, stale Save Network slice) and 9 minor mismatches; all fixed the same session.
Revised-Content-Hash: design/gdd/pumps-and-machines.md a29ccb50701fbbbb4bea7086e628f80b1784332a
Revised-Content-Hash: design/registry/entities.yaml 3699ba8284a65f0f34d18c0aec6dd3f3697fd140

Status after revision: In Review. A fresh /design-review decides whether it is Approved.

## Re-review — 2026-11-04 — Verdict: NEEDS REVISION (then fixed and Approved)
Review mode: full (fresh re-review)
Specialists: 2 fresh reviewers (doc-specific pair)
Reviewed-Content-Hash: design/gdd/pumps-and-machines.md a29ccb50701fbbbb4bea7086e628f80b1784332a
Prior verdict resolved: Yes (2026-11-02 blockers confirmed fixed)

Fixes from this re-review applied 2026-11-04 (decisions in Nour's review notes, round 1/1b/2), plus a cross-doc pass across Economy, Hose, Pumps, Shipping, Save, HUD, FPC and Game State.
A separate verification pass checked the fixes and found a few small follow-ups (round 2); all applied the same day.
Revised-Content-Hash: design/gdd/pumps-and-machines.md 540ccb9ab53739a8def14e27a91aa11795a8f741
Revised-Content-Hash: design/registry/entities.yaml 3699ba8284a65f0f34d18c0aec6dd3f3697fd140

Status after revision: Approved (2026-11-04).
