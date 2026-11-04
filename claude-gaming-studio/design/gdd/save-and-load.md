# Save & Load (minimal JSON)

> **Status**: In Review
> **Author**: Nour + agents
> **Last Updated**: 2026-11-02 (revised after the 2026-11-02 batch design review)
> **Last Verified**: 2026-11-02
> **Implements Pillar**: Supports all pillars, mainly "The World Shows the Damage" (a drained pond stays drained)

## Summary

Save & Load writes the Game State snapshot to one JSON file and reads it back, so a drained pond stays drained and nothing the player owns or has paid for is lost or doubled. The MVP has one save slot, autosave, and immediate saves on pause, quit to menu and quit.

> **Quick reference** — Layer: `Foundation` · Priority: `MVP` · Key deps: `Game State`

## Overview

Game State already holds every runtime value as plain data in 9 slices (Water Source, Network, Pumps, Hand Tools, Economy, Drawdown, Shipping, Placement, Session) and decides *when* a save is safe (step 4, after the flush, with an empty request queue; immediately in Paused, Quit to menu and Quitting). This system serialises that snapshot plus the Radio's presentation block to a versioned JSON file, writes it safely (temp file, then replace), and loads it back into a fresh container. The serialiser choice is an ADR; this GDD sets the rules. All file access goes through an `IFileSystem` interface so every disk failure can be tested with a fake.

## Player Fantasy

Not felt directly. Its job is trust: "I can quit any time and my pond, my pumps and my credits will be exactly where I left them."

## Detailed Design

### Core Rules

1. **One slot** in the MVP: `save_0.json` in Unity's `Application.persistentDataPath`. Player settings (the "Hold actions: Hold / Toggle" setting, sensitivity, invert, subtitles, volume) are **not** in the save file. They live in a separate prefs file owned by the settings code, so a New Game or a corrupt save never resets them.
2. **When:** autosave every 120 s of play and after every purchase, both at step 4 of the tick after the event flush (Game State Rule 4, AC-E12). On pause, quit to menu and quit, the save is immediate and any queued requests are dropped and logged by design (Game State "Saving is a guarded action", AC-E5, AC-E15). Saves are refused in Booting, Menu and Loading.
3. **What:** every state slice as Game State Rule 1 defines it:
   - Water Source: per source `{id, start, remaining, drained}`.
   - Network: tanks `{id, contents, inletFitted, installed}` and hoses `{id, kind, ends[2]{target, restPose}}` (Game State Rule 1; Hose/Storage Network). Hose-end positions live here, not in Placement.
   - Pumps: per pump `{id, kind, motorFitted, leverOn, cableSocket|null}`. Running, Stalled and Dry are derived, and the crank flag is per-player input, so none of them is saved.
   - Hand Tools: litres held in each tool, keyed by `PlayerId`.
   - Economy: credits, items owned, and each crate record `{crateId, item, state (Inbound/Landed/Opened), remaining seconds of the landing timer, landedTick}`.
   - Drawdown: thresholds passed, reveals triggered.
   - Shipping: pad state, remaining seconds of the countdown or turnaround timer (one timer, Shipping rule 10), litres aboard, total shipped.
   - Placement: player pose and hands item per `PlayerId`, every placed object's id and pose (hose ends are saved in the Network slice).
   - Session: session id, tick count, play time in seconds, next object id.

   Plus the Radio's played one-off line ids as a separate presentation block (not its shuffle bags). The serialised snapshot is a versioned blob produced separately from file writing, so it can later serve as the co-op join payload. Nothing from the tuning table is saved except the source start volumes (copied at new game).
4. **Timers on disk:** every timer (crate landing timer, Shipping countdown or turnaround) is held in state as whole ticks and written to disk as remaining seconds = ticks × dt. On load it becomes `ticks = max(1, round(seconds / dt))` (Game State Rule 8, AC-E18). Never `ceil`. Play time is saved in seconds and never rescaled.
5. **Format and numbers:** JSON with a top-level `version` (int), `gameVersion` (string), `savedUtc` (ISO 8601), then one object per slice.
   - Every double is written in invariant culture with round-trip formatting (G17), so it reads back bit-exact.
   - NaN and Infinity cannot be stored in JSON: a save that finds one fails (no file is written, `SaveFailed(non_finite)` is raised) and names the slice and field in the log.
   - Credits are written as the Economy's whole number of millicredits, as an integer string (for example `"6000"` for 6 cr).
   - The shipped total is a decimal string until the number-representation ADR settles its type.
6. **Safe write (synchronous):** in the MVP the write runs on the main thread inside the save call (about 100 KB). Steps:
   1. Serialise the snapshot to bytes.
   2. Write `save_0.tmp` in the same folder and call `FileStream.Flush(true)`.
   3. If `save_0.json` does not exist, `File.Move(tmp, json)`. Otherwise `File.Replace(tmp, json, bak)`, which keeps the previous file as `save_0.bak`.
   4. On Windows, if a step fails because another process holds the file (antivirus, indexer, OneDrive), retry it up to 3 times over 200 ms in total, then fail.

   A failure at any step leaves `save_0.json` and `save_0.bak` as they were. Because the write is synchronous, a quit can never interrupt it.
7. **Move unreadable files aside:** when the player starts a New Game over a file that is newer or corrupt, the files are not touched until the new session's first write. Just before that write, a newer file is renamed to `save_0.v{N}.json` (N = its `version`) and a corrupt one to `save_0.corrupt-{utc}.json` (UTC as `yyyyMMddTHHmmssZ`). A corrupt `.bak` is renamed the same way. Nothing is ever deleted except a leftover `.tmp`.
8. **Load order:** try `save_0.json`, then `save_0.bak`. A leftover `save_0.tmp` is never loaded; it is deleted on load. For each candidate file: read, parse, check `version`, migrate, validate. The first file that passes is loaded into a fresh container. If `.json` failed and `.bak` loaded, the player is told the last few minutes may be lost.
9. **Migrate:** a file with an older `version` is migrated one version at a time by pure migrator functions on an untyped JSON tree (`JsonNode` to `JsonNode`), one per version step, each with a fixture file under tests. A file with a newer `version` fails without being modified.
10. **Validate:** a file that parses but fails any of these checks counts as corrupt, so the next candidate is tried:
    - every object id is unique across the file;
    - the Session slice's next object id is above the highest runtime id (≥ 1,000,000) in the file;
    - after the owner hooks run, every source has `remaining ≤ start` and both are finite (Water Source rule 13 clamps first, so this check catches a missing or broken hook);
    - credits are 0 or more;
    - each slice owner's load hook accepts its slice. The owners' repair rules are not failures: Water Source rule 13 clamps bad volumes, and the unknown-id rules below apply.
11. **Unknown ids** (a patch removed an object or item) follow each owner's rule, never one shared "ignore" rule:
    - Economy: an owned item id missing from the table is kept in the save, ignored in play, and logged once (Economy AC-23).
    - Network: a hose end plugged into a socket whose object no longer exists loads unplugged, lying at its old position, with one warning (Hose edge case l.144).
    - Water Source: a source in the table but missing from the save is created from the table and logged (Water Source rule 13).
12. **Stable ids:** every saveable object has a stable id (Game State Rule 2), never Unity's runtime ids, so references (hose to socket, pump to cable socket) survive a reload.
13. **No mid-launch payouts lost:** a save holds either both or neither of a launch's shipped litres and payout (Shipping rule 9, Game State AC-E9, Economy AC-05).
14. **Save results:** after every attempt the Save & Load service exposes `LastSaveResult` (`Ok` or `Failed(reason)`) and raises a C# notification `SaveFailed(reason)` on failure, with reasons `io_error`, `disk_full`, `locked`, `non_finite`. These are presentation notifications for the HUD, not Game State domain events. A successful save raises Game State's `Saved` event as before.

### States and Transitions

Uses Game State's Saving action and Loading state; no states of its own.

### Interactions with Other Systems

| System | This system gives | This system takes |
|---|---|---|
| Game State | Loaded container; `Saved` event | Snapshot and the save trigger |
| All slice owners | Their slice to their load hook | Their slice data |
| Corporate Radio | Played one-off line ids on load | Played one-off line ids |
| Placement (Game State) | Player pose and hands item on load | Player pose and hands item |
| HUD | `LastSaveResult`, `SaveFailed(reason)`, "saving" flag | — |
| Main menu | Continue available or not; load failure message | — |
| Settings (prefs file) | Nothing; settings are a separate file | — |

## Formulas

- `next_autosave = last_save + 120 s of play time` (1,200 ticks at dt = 0.1; any save resets it).
- Timer to disk: `seconds = ticks × dt`. Timer from disk: `ticks = max(1, round(seconds / dt))`.

## Edge Cases

- **Disk full or write error**: keep playing; `SaveFailed(reason)` is raised for the HUD toast; retry at the next trigger; `.json` and `.bak` are untouched.
- **Crash between replace steps** (`.json` missing, `.bak` and `.tmp` present): load `.bak`, delete `.tmp` (Rule 8).
- **Corrupt main file, good `.bak`**: load `.bak` and tell the player the last few minutes may be lost.
- **Both corrupt, or main file newer**: offer a new game; the files' bytes stay unchanged until the player confirms, then they are moved aside before the first write (Rule 7).
- **Unknown item or object id** (patch removed it): each owner's rule (Rule 11).
- **NaN or Infinity in state**: the save fails with `non_finite`; the last good file stays (Rule 5).
- **Save during a countdown**: Shipping rule 9; the countdown is stored as remaining seconds (Rule 4).
- **Quit during autosave**: cannot happen, because the write is synchronous on the main thread (Rule 6).

## Dependencies

**Upstream:** Game State (Hard), every slice owner (Hard), Corporate Radio (Soft, presentation block). **Downstream:** Main menu, HUD (toast and drop icon), Co-op (later: the snapshot becomes the join-in-progress payload).

## Tuning Knobs

| Knob | Default | Safe range |
|---|---|---|
| Autosave interval | 120 s | 60–300 s |
| Windows lock retries | 3 tries over 200 ms | 1–5 tries |

One backup (`save_0.bak`) in the MVP; more backups are out of scope.

## Visual/Audio Requirements

- The HUD shows a small spinning water drop in the corner while saving and a small "Save failed" toast on `SaveFailed`. Both are drawn by the HUD (see `design/gdd/hud.md`); this system only exposes the flag and the result. No sound.

## Game Feel

- Saving never hitches the game noticeably: `Save()` takes at most 8 ms and the file is at most 100 KB.

## UI Requirements

- Main menu: Continue (if a loadable save exists), New Game (confirm if it would replace a save), Quit.
- Load failure message: "Save could not be read; start a new game?" When `.bak` loaded instead: "Your last save was damaged; the backup loaded, so the last few minutes may be lost."

## Cross-References

| This document | References | Why |
|---|---|---|
| Rules 2, 3, 4 | `design/gdd/game-state-and-simulation.md` | Save timing, slices (Rule 1), timers (Rules 3, 8), AC-E5/E6/E12/E15/E18 |
| Rule 5 | `design/gdd/economy-progression-and-shop.md` Rule 3 | Credits in millicredits |
| Rule 5 | Number-representation ADR (pending) | Shipped total type |
| Rule 9 | Save serialiser ADR (pending) | Serialiser and `JsonNode` migrators |
| Rule 11 | Economy AC-23; Hose edge case l.144; Water Source rule 13 | Unknown ids |
| Rule 13 | `design/gdd/shipping-and-launch.md` | Countdown saves |
| Rule 14 | `design/gdd/hud.md` | Toast and saving icon |

## Acceptance Criteria

Test doubles: **`FakeFileSystem`** (in-memory `IFileSystem` that records every call), **`ThrowingFileSystem(after, exception)`** (a `FakeFileSystem` that throws after a given number of bytes or on a named call), **`FakeTuningTable`**, **`ScriptedInput("save-10min")`** (a recorded 10-minute input fixture), **`LogSpy`**, **`SaveResultSpy`** (counts `SaveFailed` and save attempts). Corrupt fixtures are Game State AC-E6's: 0-byte, truncated, invalid syntax.

- **AC-01 Round trip [EditMode]** GIVEN `ScriptedInput("save-10min")` run at dt = 0.1 on `FakeFileSystem`, WHEN saved and loaded into a fresh container, THEN every slice compares equal field by field with doubles bit-exact, and the next 600 ticks match a run that was never saved.
- **AC-02 Disk full [EditMode]** GIVEN a good `save_0.json` and `save_0.bak` and `ThrowingFileSystem(after: 512 bytes, IOException "disk full")`, WHEN an autosave runs, THEN the SHA-256 of `.json` and `.bak` is unchanged, `SaveResultSpy` records one `SaveFailed(disk_full)`, the state is Running, and at the next trigger a second attempt is made (attempt count = 2).
- **AC-03 Corrupt main, good backup [EditMode]** GIVEN each corrupt fixture as `save_0.json` and a valid `save_0.bak`, WHEN Continue, THEN the `.bak` state loads, the "backup loaded" message is requested once, and the SHA-256 of `save_0.json` is unchanged.
- **AC-04 Newer version moved aside [EditMode]** GIVEN `save_0.json` with `version` = current + 1, WHEN Continue, THEN Load fails, its bytes are unchanged and a new game is offered; WHEN New Game is confirmed and one autosave runs, THEN `save_0.v{current+1}.json` exists with the original bytes and `save_0.json` holds the new game.
- **AC-05 Exact numbers [EditMode]** GIVEN shipped total 123,456,789.0001 L, credits 6,000 mc, and a tank holding 0.015 L, WHEN saved and loaded, THEN the shipped total is exactly equal (decimal string), credits are the string `"6000"` on disk and 6,000 mc after load, and the tank double is bit-exact.
- **AC-06 Socket by stable id [EditMode]** GIVEN a hose plugged into the IBC's link socket, WHEN saved and loaded, THEN it is plugged into the same socket by stable id.
- **AC-07 Save budget [EditMode]** GIVEN the state at the end of `ScriptedInput("save-10min")` on `FakeFileSystem`, WHEN `Save()` runs 20 times, THEN the median time measured with a Stopwatch is at most 8 ms and the file is at most 100 KB.
- **AC-08 Look the same [Manual]** GIVEN Nour quits mid-session and continues, THEN the pond level, bands, tanks, crates and credits look exactly as before.
- **AC-09 Both corrupt [EditMode]** GIVEN corrupt fixtures as both `save_0.json` and `save_0.bak`, WHEN Continue, THEN the load failure message is requested, both SHA-256 values stay unchanged until New Game is confirmed, and after the first autosave both originals exist as `save_0.corrupt-{utc}.json` files.
- **AC-10 Crash between replace steps [EditMode]** GIVEN `FakeFileSystem` with no `save_0.json`, a valid `save_0.bak` and a `save_0.tmp` holding a different valid state, WHEN Continue, THEN the `.bak` state loads and `save_0.tmp` is deleted.
- **AC-11 Backup rotation [EditMode]** GIVEN an empty `FakeFileSystem`, WHEN two saves run with different states, THEN the first save used `File.Move`, the second used `File.Replace`, and `save_0.bak` equals the first file byte for byte.
- **AC-12 Crash mid-replace [EditMode]** GIVEN a good `save_0.json` and `ThrowingFileSystem` that throws on `Replace`, WHEN a save runs, THEN `save_0.json` is unchanged and one `SaveFailed(io_error)` is recorded.
- **AC-13 Migration [EditMode]** GIVEN the v0 fixture file, WHEN loaded, THEN the v0 to v1 migrator runs once and the resulting state equals the v1 fixture's expected state.
- **AC-14 Validation [EditMode]** GIVEN a valid `.bak` and a `.json` that parses but has (a) two objects with id 1,000,005, or (b) next object id 1,000,010 with an object id 1,000,020 in the file, or (c) credits `"-1"`, WHEN Continue, THEN for each case the `.json` is treated as corrupt and the `.bak` loads.
- **AC-15 Unknown item kept [EditMode]** GIVEN a fixture that owns item id 9999, which is missing from `FakeTuningTable`, WHEN it is loaded and saved again, THEN the new JSON still lists 9999 as owned and `LogSpy` records exactly one warning.
- **AC-16 Missing socket [EditMode]** GIVEN a fixture with a hose end plugged into socket 42, whose object no longer exists, WHEN loaded, THEN the hose end is unplugged at its saved pose and `LogSpy` records one warning.
- **AC-17 Timer on disk [EditMode]** GIVEN a crate Inbound with 25 ticks left at dt = 0.1, WHEN saved, THEN the file stores 2.5 s; WHEN loaded at dt = 0.1, THEN it lands 25 ticks after load; WHEN loaded at dt = 0.05, THEN it lands `max(1, round(2.5/0.05))` = 50 ticks after load.
- **AC-18 Non-finite refused [EditMode]** GIVEN a good `save_0.json` and a tank whose contents are set to NaN through a test hook, WHEN a save runs, THEN no file is written, the SHA-256 of `save_0.json` is unchanged, and one `SaveFailed(non_finite)` is recorded with the slice and field in the log.
- **AC-19 Windows lock retry [EditMode]** GIVEN `ThrowingFileSystem` that throws `IOException` "sharing violation" on the first 2 `Replace` calls, WHEN a save runs, THEN it succeeds on the third try and no `SaveFailed` is raised; GIVEN it throws on 3 calls, THEN one `SaveFailed(locked)` is recorded.
- **AC-20 Settings not in the save [EditMode]** GIVEN "Hold actions" set to Toggle, WHEN a save runs, THEN the save JSON has no settings field; WHEN New Game starts, THEN the setting is still Toggle.
- **AC-21 Menu [Manual]** Screenshots: Continue shown when a loadable save exists and hidden when none exists; the New Game confirm shows when a save would be replaced.

## Creative Alignment (CD-GDD-ALIGN)

**Verdict: APPROVED (self-check, 2026-11-02; revised after the 2026-11-02 batch design review).** Supports "The World Shows the Damage" (a drained pond stays drained). No design risk.

## Open Questions

| Question | Owner | Resolve by |
|---|---|---|
| Serialiser choice (JsonUtility vs Newtonsoft vs System.Text.Json); `System.Text.Json` with `JsonNode` migrators is the lead option | technical-director | Save serialiser ADR |
| `File.Replace` behaviour on macOS under Mono and IL2CPP | technical-director | Save serialiser ADR |
| Multiple slots and cloud saves (Steam) | producer | Before Steam release |
