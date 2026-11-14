# Sprint 1 — Simulation core and first playable greybox

> **Dates**: 2026-11-06 → 2026-11-20 (prototype week 1–2 of 6–10) · **Owner**: Nour (solo) · **Status**: In progress

**Goal:** the real game (not the concept prototype) is playable in the Greybox scene from the spoon to the hand pump, on the production simulation, with the electric stage working in the simulation.

## Stories

| # | Story | Type | Status | Evidence |
|---|---|---|---|---|
| S1-01 | Simulation container: 10 Hz clock, request queue, tick order 1→2a…2f→3→4, event bus, run states, pause | Logic | Done | `ClockTests`, `ContainerTests` (dotnet) |
| S1-02 | Water Source: Withdraw, drained epsilon, SourceDrained at 2d | Logic | Done | `WaterSourceTests` |
| S1-03 | Hand Tools: spoon, bucket, scoop/pour/select with rejection order | Logic | Done | `HandToolTests` |
| S1-04 | Network: pools, fill/take order, pour rule, swap storage, connect rules, flow solve, PoolFull | Logic | Done | `StorageTests`, `PumpTests`, `LadderTests` |
| S1-05 | Pumps: crank, motor kit, cable and post, switch, derived states, PumpStateChanged | Logic | Done (sim) | `PumpTests` |
| S1-06 | Shipping: launch, countdown 30 ticks, turnaround 120, payout atomic with shipped total | Logic | Done | `ShippingEconomyTests` |
| S1-07 | Economy: millicredits, linear shop, ORDER, crates Inbound→Landed→Opened, unboxing | Logic | Done | `ShippingEconomyTests`, `LadderTests` |
| S1-08 | Drawdown thresholds (1, 2.5, 5, then every 10%) and reveals (dock 10%, cart 15%) | Logic | Done (sim) | `PumpTests.Drawdown_SeveralThresholdsInOneTick` |
| S1-09 | Play log CSV (invariant culture, since_prev_same) | Logic | Done | `PlayLogTests` |
| S1-10 | Greybox level + world view (water planes, tanks, gauges, hoses, cables, bands, rocket) | Visual | Needs a run in Unity | Screenshot to `production/qa/evidence/` |
| S1-11 | First-person controller + interaction table + holds + tool gate + placement ghost | Integration | Needs a run in Unity | Manual playthrough |
| S1-12 | Placeholder HUD + radio lines + pause menu | UI | Needs a run in Unity | Screenshot |
| S1-13 | Playthrough: spoon → bucket → barrel → drum → hand pump → electric motor | Integration | To do (Nour) | Play log in `MarsNeedsWater/PlaytestLogs/` |

## Next (Sprint 2 candidates)

Save & Load (ADR-0006) · Corporate Radio system · Input Actions asset + gamepad + Hold/Toggle setting · second electric pump and IBC playthrough · drawdown basin bake + stain-band shader spike · Unity Test Framework package so the same tests run in EditMode.

## How to play the greybox

1. Open `MarsNeedsWater` in Unity 6000.6.7f1. On first open the editor creates `Assets/Game/Scenes/Greybox.unity` (or use **Mars Needs Water → Open Greybox Scene**).
2. Open `Greybox` and press Play. (`SampleScene` still runs the old concept prototype.)
3. Controls: WASD, mouse, Shift sprint, C crouch, Space jump. **E / left mouse** = use (hold for pry 1 s, bolt 2 s, fit inlet 2 s, crank). **G** drop. **1 / 2 / scroll** spoon / bucket (scroll rotates a placement ghost). **Esc** pause.
4. Loop: scoop the puddle with the spoon, pour into the jerrycan, pull LAUNCH, then ORDER at the kiosk. Pry crates open on the crate spots. Set a bigger container on the yellow staging square and "Swap in". Screw the inlet fitting into the drum. Carry the hand pump to the pond, drop the suction hose end in at the yellow stakes, run the discharge hose from the pump's yellow outlet to the drum's inlet, and hold E on the crank.

## Run the tests

`dotnet test claude-gaming-studio/tests/unit/simulation` (needs the .NET 8 SDK). 36 tests, all passing on 2026-11-06.
