# Assets/Game — production code (Mars Needs Water)

- `Simulation/` — pure C# simulation (no UnityEngine). All rules from the MVP GDDs live here.
- `Runtime/` — Unity presentation: greybox level, player controller, world view, HUD, tick driver (`GameRoot`).
- `Editor/` — creates `Scenes/Greybox.unity` on first open.
- `Data/Resources/mvp_tuning.json` — the Economy tuning table.

Play: open `Scenes/Greybox.unity`, press Play. Architecture: `claude-gaming-studio/docs/architecture/architecture.md`.
Tests: `dotnet test claude-gaming-studio/tests/unit/simulation`.
