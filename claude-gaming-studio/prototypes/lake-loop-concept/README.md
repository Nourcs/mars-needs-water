<!-- PROTOTYPE - NOT FOR PRODUCTION -->
<!-- Question: Does the bucket → tank → pump → rocket loop feel satisfying while the lake visibly drops? -->
<!-- Date: 2026-11-01 -->

# Lake loop concept prototype

**Hypothesis:** If the player fills a bucket at the lake, pours it into a tank, then works a pump and launches rockets, the loop will feel satisfying as the lake visibly drops. We know it's true if the player keeps playing **10+ minutes unprompted** and **reaches the electric pump upgrade**.

**Riskiest assumption:** hand-carrying water feels good rather than like a chore (pillar 2, Hands on the Machinery).

**Path:** Engine (Unity 6.6). **Code:** `MarsNeedsWater/Assets/Prototypes/LakeLoopConcept/` (also visible here via the `Assets` link). Own assembly (`Prototypes.LakeLoop`), so production code can't reference it.

## How to play
1. Open `MarsNeedsWater` in Unity Hub, open `Assets/Scenes/SampleScene`, press Play. The script builds the grey-box level itself.
2. WASD move, mouse look, Shift sprint, Space jump. E: pick up/put down bucket, launch, buy, hold E on the pump. Left mouse (hold): fill bucket at the water / pour into the tank. Esc frees the cursor.

## Tuning (hardcoded at the top of the script)
v1: lake 2,000 L · bucket 10 L · tank 200 L · hand pump 5 L/s (100) · electric motor 20 L/s (400) · 1 credit/L.

v2 (current): lake 6,000 L · spoon 0.5 L → cup 2 L (5) → bucket 10 L (30) → hand pump 5 L/s (100) → electric motor 20 L/s (400) → 1,000 L tank (600) → industrial pump 100 L/s (1,500) → auto-launcher (2,500). Play log: `MarsNeedsWater/PlaytestLogs/lakeloop-<time>.csv`.

Verdict: PROCEED (see REPORT.md).

## Cut on purpose
Saving, menus, glaciers, co-op, art, sound, contracts, hose placement (hoses are straight lines).

## What to note while playing
- Time until you first felt bored or annoyed, and what you were doing.
- Did you want to watch the water drop / go look at the sunken car?
- Did buying the pump feel like a real jump? Did the electric motor make you stop playing (nothing left to do)?
