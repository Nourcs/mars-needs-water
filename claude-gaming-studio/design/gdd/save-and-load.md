# Save & Load (minimal JSON)

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Supports all pillars (a drained pond stays drained)

## Summary

Save & Load writes the Game State snapshot to one JSON file and reads it back, so a drained pond stays drained and nothing the player owns or has paid for is lost or doubled. The MVP has one save slot, autosave and save on quit.

> **Quick reference** — Layer: `Foundation` · Priority: `MVP` · Key deps: `Game State`

## Overview

Game State already holds every runtime value as plain data in 9 slices (Water Source, Network, Pumps, Hand Tools, Economy, Drawdown, Shipping, Placement, Session) and decides *when* a save is safe (step 4, after the flush, with an empty request queue). This system serialises that snapshot plus the Radio's presentation block to a versioned JSON file, writes it safely (temp file, then replace), and loads it back into a fresh container. The serialiser choice is an ADR; this GDD sets the rules.

## Player Fantasy

Not felt directly. Its job is trust: "I can quit any time and my pond, my pumps and my credits will be exactly where I left them."

## Detailed Design

### Core Rules

1. **One slot** in the MVP: `save_0.json` in the per-user application data folder.
2. **When:** autosave every 120 s of play, on every purchase, on quit and on returning to the menu. Game State performs the save after the current tick's event flush (Game State rule on save timing).
3. **What:** every state slice (including Placement: player pose and hands item, every placed object's id and pose, every hose end's connection or resting position; and Session: clock, tick count, next object id), plus the Radio's played one-off lines as a separate presentation block. The serialised snapshot is a versioned blob produced separately from file writing, so it can later serve as the co-op join payload. Nothing from the tuning table except source start volumes (copied at new game).
4. **Format:** JSON with a top-level `version` (int), `gameVersion` (string), `savedUtc`, then one object per slice. Numbers that need exactness (the shipped total for the Earth counter, credits) are stored as decimal strings (number-representation ADR).
5. **Safe write:** write to `save_0.tmp`, flush, then replace `save_0.json`; keep the previous file as `save_0.bak`. A crash mid-write never destroys the last good save.
6. **Load:** read, check version, migrate older versions step by step, validate, then build a fresh state container. A newer or corrupt file is never modified; the player is offered a new game, and `.bak` is tried first if the main file is corrupt.
7. **Stable ids:** every saveable object has a stable id (not Unity's runtime ids), so references (hose → socket) survive a reload.
8. **No mid-launch payouts lost:** follows Shipping rule 9 and Economy's atomic payout.

### States and Transitions

Uses Game State's Saving and Loading states; no states of its own.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Loaded container; `Saved` event | Snapshot and the save trigger |
| All slice owners | — | Their slice data |
| Interaction | Player pose on load | Player pose |
| Main menu | Continue available or not | — |

## Formulas

None beyond the autosave timer: `next_autosave = last_save + 120 s of play time`.

## Edge Cases

- **Disk full or write error**: keep playing; show a small "Save failed" toast; retry at the next trigger; the previous save is untouched.
- **Corrupt main file, good `.bak`**: load `.bak` and tell the player the last few minutes may be lost.
- **Both corrupt**: offer a new game; never overwrite them until the player confirms.
- **Unknown item or object id** (patch removed it): keep it in the save, ignore in play, log once (Economy and Network rules).
- **Save during a countdown**: Shipping rule 9.
- **Quit during autosave**: the quit waits for the write to finish (under 100 ms expected).

## Dependencies

**Upstream:** Game State (Hard), every slice owner (Hard). **Downstream:** Main menu, Co-op (later: the snapshot becomes the join-in-progress payload).

## Tuning Knobs

| Knob | Default | Safe range |
|---|---|---|
| Autosave interval | 120 s | 60–300 s |
| Backups kept | 1 | 1–3 |

## Visual/Audio Requirements

- A small spinning-water-drop icon in the corner while saving. No sound.

## Game Feel

- Saving never hitches the game for more than one frame (expected file under 100 KB).

## UI Requirements

- Main menu: Continue (if a save exists), New Game (confirm if it would overwrite), Quit.

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 2, 3 | `design/gdd/game-state-and-simulation.md` | Save timing, slices, AC-E5/E6 |
| Rule 4 | Number-representation ADR (pending) | Exact decimals |
| Rule 8 | `design/gdd/shipping-and-launch.md` | Countdown saves |

## Acceptance Criteria

- **AC-01 [EditMode]** GIVEN a state after 10 minutes of scripted play, WHEN saved and loaded, THEN every slice compares equal and the next 600 ticks match an unsaved run.
- **AC-02 [EditMode]** GIVEN a write that throws halfway, THEN `save_0.json` is unchanged and a toast request is raised.
- **AC-03 [EditMode]** GIVEN a corrupt main file and a good `.bak`, WHEN Continue, THEN the `.bak` loads.
- **AC-04 [EditMode]** GIVEN a file with a newer `version`, WHEN Continue, THEN the file's bytes are unchanged and a new game is offered.
- **AC-05 [EditMode]** GIVEN shipped total 123,456,789.0001 L, WHEN saved and loaded, THEN it is exactly equal (decimal string).
- **AC-06 [EditMode]** GIVEN a hose plugged into the IBC's link socket, WHEN saved and loaded, THEN it is plugged into the same socket by stable id.
- **AC-07 [PlayMode]** GIVEN an autosave during electric pumping, THEN no frame takes longer than 1.5 × the median frame during the save.
- **AC-08 [Manual]** GIVEN Nour quits mid-session and continues, THEN the pond level, bands, tanks, crates and credits look exactly as before.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02).** Supports Pillar 3 ("a drained lake stays drained"). No design risk.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Serialiser choice (JsonUtility vs Newtonsoft vs custom) | technical-director | Save serialiser ADR |
| Multiple slots and cloud saves (Steam) | producer | Before Steam release |
