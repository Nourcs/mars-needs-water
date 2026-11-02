# Systems Index: Mars Needs Water

> **Status**: Draft
> **Created**: 2026-11-01
> **Last Updated**: 2026-11-01
> **Source Concept**: design/gdd/game-concept.md

---

## Overview

Mars Needs Water is a first-person incremental game about physically draining Earth's water into rocket cargo. Mechanically it is a **volume pipeline**: water leaves a source (lake, later glacier), passes through the player's hands or machines, sits in storage, and leaves the planet on a launch that pays credits, which buy the next jump in scale. Almost every system is a stage of that pipeline or a way of *showing* it. The four pillars sort them. **Start Tiny, End Enormous** governs the tool and machine ladder and the economy's pacing. **Hands on the Machinery** governs interaction, hand tools, pumps, hoses and hand-built automation: no system may be a menu button. **The World Shows the Damage** makes drawdown visuals and reveals a gameplay system, not polish. **Bleak Corporate Satire** makes the corporate radio a real system from the MVP on. Co-op is a production rule: every system keeps its state in plain data (Game State) so networking can be added later, but no networking is designed before the Network Test.

**Ownership rules (from the technical director's boundary check):** the **Economy tuning table** owns every designed number (per-tier rate, capacity, price, source volume, payout); each system owns the *rules* that use them; **Game State** holds only runtime values and is a container + tick + event bus, with each system defining its own state slice and the methods that change it. The **Hose & Connection Network** owns the per-tick flow solve: min(source available, pump rate, hose cap, tank space). Sources, pumps and tanks register as nodes through the network's interface; the Water Source Model only exposes `Available` and `Withdraw()`. Listeners (Radio, Audio, Onboarding, Play Log) subscribe to domain events (`LaunchCompleted`, `Purchased`, `Milestone`, `DrawdownThreshold`) instead of holding system references. Interaction only sends requests to system methods (future co-op RPCs) and never writes state.

**Scope (producer check, OPTIMISTIC):** the MVP is about 65–70 dev-days for a first-time solo developer. To keep it near 6–10 weeks of building, the MVP is narrowed: storage merged into the hose network, the shop merged into the economy, the play log folded into Game State, hand tools = spoon + bucket, pumps = hand pump + electric motor, drawdown = water plane + one lakebed swap + 1–2 reveals (stain-band shader gets a 3-day spike with a fallback), HUD = credits + tank level. Realistic restatement: MVP in 10–14 weeks including the 2–3 week design timebox. **Target MVP session: about 15–20 minutes** (matches MVP test b). Drawdown must hit the 5-minute visible-change target over that session with stain bands alone (or the flat-decal fallback), with the 1–2 reveals as extra beats.

**Real-scale rule (Nour, 2026-11-02):** every source holds its real-world volume (MVP: 40 L puddle and a 20,000 L farm pond); a corporate Earth counter starts at about 1.386 × 10²¹ L and is never emptied (satire). This affects the Economy GDD and the number-representation ADR.

**Per-system notes (thorough):**

- **Game State & Simulation (inferred)** is the container, simulation tick and event bus for every system's runtime state (volumes, tank contents, credits, machine states, hand-held water). It is the co-op seam and the save source. It also emits the play-log events (the Play Log is a short spec inside it, not its own GDD).
- **Water Source Model** holds a source's volume (start volume read from the Economy table) and exposes `Available` and `Withdraw()`. It does not know about pipes or tanks. It is the most depended-on gameplay system.
- **First-Person Controller & Interaction** is an asset-store controller plus our own grab/hold/use/aim layer. Every physical verb in pillar 2 plugs into it.
- **Hand Tools** (spoon, then bucket; a cup only if it earns its own action) cover the opening minutes. Each tool must add an action or reach, not just litres.
- **Pumps & Machines** cover the hand pump, the electric motor and later tiers: placing, starting, cranking, maintaining and redeploying. Each tier is at least 2× the footprint of the last.
- **Hose, Storage & Flow Network** handles sockets, spline hoses, tanks as graph nodes, and the per-tick flow solve. It is the logistics backbone and the source of "blocked connection" feedback. Full tanks stop pumps safely; there is no failure state.
- **Shipping & Launch** moves stored water into a rocket, launches it and pays credits. Automated launching is built by hand (a feed line to the pad), not bought.
- **Economy, Progression & Shop** owns the tuning table (credits per litre, prices, rates, capacities, source volumes) against the **Pacing Targets** (first pump within 4 min, time per tier rises), and the in-world shop terminal that is a screen over that table. It solves the review's pacing problems.
- **Drawdown Visuals & Reveals** covers the water plane following volume, stain bands, lakebed swaps at thresholds and authored reveal objects. Something visible must change within 2 minutes, then at least every 5.
- **Corporate Radio** voices one line per launch, milestone barks, and one celebratory line per drawdown threshold and reveal, so the reward celebrates exactly what the landscape mourns. It is the MVP's only test of pillar 4.
- **Save & Load** writes versioned JSON of the Game State, never BinaryFormatter.
- **HUD & Gauges** shows credits, tank level, flow gauges on hoses and the shop screen. Pillar 3 says the world shows progress first, and the HUD only confirms it.
- **Audio Feedback (inferred)** covers flow, machinery, pour and launch sounds. Sensation ranks second among aesthetics and the bucket trip is the MVP's first fun test, so placeholder pour, pump and launch sounds are MVP scope inside Hand Tools, Pumps and Shipping; the audio system proper comes in the slice.
- **Ice Harvesting** and **Ice Handling & Processing** make up the glacier chain (each glacier face registers as a Water Source node, so Drawdown shows the face being cut back): cut marked faces into weight-class blocks, then lift, stack, crush or melt them on host-only physics with snap slots.
- **Logistics Overlay** shows the bottleneck (extraction, storage or shipping), with at most three at once.
- **Onboarding** teaches one verb at a time through world signage and radio, with no tutorial screens.
- **Contracts & Colony Orders**, **Regions & World Progression** and **Chapter Story & Milestones** make up the solo release's campaign layer: contract sizes, region gates and milestone celebrations paired with losses.
- **Settings & Accessibility** covers input rebinding, audio and text size.
- **Network Spike** (Network Test tier) syncs lake volume and one machine between 2 players to decide co-op timing. **Co-op Networking**, **Vehicles** and **Prestige/Endless** are full-vision features. Vehicles and Prestige each need a pillar case before a GDD: Vehicles must add a physical action (pillar 2); Prestige must keep the landscape scars (pillar 3) or be cut.

---

## Systems Enumeration

| # | System Name | Category | Priority | Status | Design Doc | Depends On |
|---|-------------|----------|----------|--------|------------|------------|
| 1 | Game State & Simulation (inferred; includes Play Log events) | Core | MVP | Approved | design/gdd/game-state-and-simulation.md | — |
| 2 | First-Person Controller & Interaction | Core | MVP | In Review | design/gdd/first-person-controller-and-interaction.md | Game State |
| 3 | Economy, Progression & Shop | Economy | MVP | In Review | design/gdd/economy-progression-and-shop.md | Game State |
| 4 | Water Source Model | Gameplay | MVP | In Review | design/gdd/water-source-model.md | Game State, Economy |
| 5 | Hose, Storage & Flow Network | Gameplay | MVP | In Review | design/gdd/hose-storage-and-flow-network.md | Game State, Interaction, Water Source Model, Economy |
| 6 | Hand Tools (spoon, bucket) | Gameplay | MVP | In Review | design/gdd/hand-tools.md | Interaction, Water Source Model, Hose/Storage Network, Economy |
| 7 | Pumps & Machines (hand pump, electric motor) | Gameplay | MVP | In Review | design/gdd/pumps-and-machines.md | Hose/Storage Network, Interaction, Game State, Economy |
| 8 | Shipping & Launch | Gameplay | MVP | In Review | design/gdd/shipping-and-launch.md | Hose/Storage Network, Economy, Interaction |
| 9 | Drawdown Visuals & Reveals | Gameplay | MVP | In Review | design/gdd/drawdown-visuals-and-reveals.md | Water Source Model (events); glacier faces as Water Source nodes in VS |
| 10 | Corporate Radio | Narrative | MVP | In Review | design/gdd/corporate-radio.md | Game State (events, incl. drawdown and reveals) |
| 11 | Save & Load (minimal JSON) | Persistence | MVP | In Review | design/gdd/save-and-load.md | Game State |
| 12 | HUD (credits, tank level) | UI | MVP | In Review | design/gdd/hud.md | Hose/Storage Network, Economy |
| 13 | Audio Feedback (inferred; MVP placeholder pour/pump/launch sounds owned by Hand Tools, Pumps, Shipping) | Audio | Vertical Slice | Not Started | — | Game State (events) |
| 14 | Ice Harvesting | Gameplay | Vertical Slice | Not Started | — | Interaction, Game State, Economy, Water Source Model (glacier face = source node) |
| 15 | Ice Handling & Processing | Gameplay | Vertical Slice | Not Started | — | Ice Harvesting, Hose/Storage Network |
| 16 | Onboarding (inferred) | Meta | Vertical Slice | Not Started | — | Game State (events), Hand Tools, Pumps |
| 17 | Network Spike | Core | Full Vision (Network Test) | Not Started | — | Game State, Water Source Model, Pumps |
| 18 | Logistics Overlay & Flow Gauges | UI | Alpha | Not Started | — | Hose/Storage Network, Shipping |
| 19 | Contracts & Colony Orders | Progression | Alpha | Not Started | — | Economy, Shipping |
| 20 | Regions & World Progression | Progression | Alpha | Not Started | — | Water Source Model, Contracts, Game State |
| 21 | Chapter Story & Milestones | Narrative | Alpha | Not Started | — | Game State (events), Contracts, Regions |
| 22 | Settings & Accessibility (inferred; own settings file) | Persistence | Alpha | Not Started | — | — |
| 23 | Co-op Networking | Core | Full Vision | Not Started | — | Game State (all systems), Network Spike |
| 24 | Vehicles | Gameplay | Full Vision | Not Started | — | Interaction, Regions |
| 25 | Prestige / Endless Mode | Progression | Full Vision | Not Started | — | Economy, Regions |
| 26 | Voice Sound Packs (player-recorded SFX/music; Nour's idea 2026-11-02) | Audio | Full Vision (with the co-op update) | Not Started | — | Audio Feedback, Settings, Co-op Networking |


---

## Categories

| Category | Description | Typical Systems |
|----------|-------------|-----------------|
| **Core** | Foundation systems everything depends on | Game State, Controller & Interaction, Network Spike, Co-op Networking |
| **Gameplay** | The systems that make the game fun | Water Source, Hand Tools, Pumps, Hose/Storage Network, Shipping, Drawdown, Ice |
| **Progression** | How the player grows over time | Contracts, Regions, Prestige |
| **Economy** | Resource creation and consumption | Economy, Progression & Shop |
| **Persistence** | Save state and continuity | Save & Load, Settings |
| **UI** | Player-facing information displays | HUD, Logistics Overlay & Flow Gauges |
| **Audio** | Sound and music systems | Audio Feedback |
| **Narrative** | Story and dialogue delivery | Corporate Radio, Chapter Story & Milestones |
| **Meta** | Systems outside the core game loop | Onboarding |

---

## Priority Tiers

| Tier | Definition | Target Milestone | Design Urgency |
|------|------------|------------------|----------------|
| **MVP** | Required for the core loop to function. Without these, you can't test "is this fun?" | MVP lake (6–10 weeks) | Design FIRST |
| **Vertical Slice** | Required for one complete, polished area. Demonstrates the full experience. | Lake + one glacier face (+2–4 months) | Design SECOND |
| **Alpha** | All features present in rough form. Here: the **Solo Release** scope (2 regions, 3–5 h, contracts). | Solo Release (12–18 months total) | Design THIRD |
| **Full Vision** | Network Test, Early Access co-op and full 6-chapter vision. | EA / 1.0 | Design as needed |

> **Creative Director Note** (CD-SYSTEMS: CONCERNS, applied 2026-11-01): (MVP) radio reacts to drawdown and reveals; placeholder sounds in MVP; MVP session target stated and stain bands carry the 5-minute visible-change target. (Vertical Slice) glacier faces are Water Source nodes so Drawdown covers them. (Full Vision) Vehicles and Prestige need a pillar case before any GDD; Prestige must keep the scars or be cut.

---

## Dependency Map

### Foundation Layer (no dependencies)

1. Game State & Simulation — container, tick and event bus; the co-op and save seam.
2. Economy, Progression & Shop (tuning table) — every designed number; depends only on Game State for credits.

### Core Layer (depends on foundation)

1. First-Person Controller & Interaction — depends on: Game State (sends requests, never writes state)
2. Water Source Model — depends on: Game State, Economy (start volume)
3. Save & Load — depends on: Game State
4. Ice Harvesting (VS) — depends on: Interaction, Game State, Economy

### Feature Layer (depends on core)

1. Hose, Storage & Flow Network — depends on: Game State, Interaction, Water Source Model, Economy
2. Hand Tools — depends on: Interaction, Water Source Model, Hose/Storage Network, Economy
3. Pumps & Machines — depends on: Hose/Storage Network, Interaction, Game State, Economy
4. Shipping & Launch — depends on: Hose/Storage Network (feed line to the pad), Economy, Interaction
5. Ice Handling & Processing (VS) — depends on: Ice Harvesting, Hose/Storage Network
6. Contracts (Alpha), Regions (Alpha), Network Spike (Network Test), Vehicles and Prestige (Full Vision)

### Presentation Layer (depends on features)

1. Drawdown Visuals & Reveals — depends on: Water Source Model events (designed early: pillar 3, MVP must-fix)
2. Corporate Radio — depends on: Game State events (`LaunchCompleted`, `Purchased`, `Milestone`, `DrawdownThreshold`, `Reveal`); every reveal gets one celebratory line
3. HUD — depends on: Hose/Storage Network, Economy
4. Audio Feedback (VS) — depends on: Game State events
5. Logistics Overlay & Flow Gauges (Alpha) — depends on: Hose/Storage Network, Shipping

### Polish Layer (depends on everything)

1. Onboarding (VS) — depends on: Game State events, Hand Tools, Pumps
2. Chapter Story & Milestones (Alpha) — depends on: Game State events, Contracts, Regions
3. Settings & Accessibility (Alpha) — own settings file, no world-save dependency
4. Co-op Networking (Full Vision) — depends on: every system's Game State slice, Network Spike

**Bottlenecks (most depended on):** Game State (nearly everything), Economy tuning table (8 dependents), Hose/Storage Network (7), Interaction (6), Water Source Model (5).
**Leaf nodes (low risk, design late):** HUD, Audio, Logistics Overlay, Settings, Prestige.

> **Technical Director Note (TD-SYSTEM-BOUNDARY: CONCERNS, resolved in this draft):** flow solve moved from Water Source to the Hose/Storage Network; designed numbers owned by the Economy table, runtime values by Game State; Game State kept to container + tick + events; listeners use domain events; Interaction never writes state; Regions→Game State (not Save); Settings in its own file; Shipping→Hose Network and Pumps→Game State added.

---

## Recommended Design Order

| Order | System | Priority | Layer | Agent(s) | Est. Effort |
|-------|--------|----------|-------|----------|-------------|
| 1 | Game State & Simulation (contract, events, play-log hook) + Water Source Model | MVP | Foundation/Core | systems-designer, technical-director | M |
| 2 | Economy, Progression & Shop (tuning table + Pacing Targets) | MVP | Foundation | economy-designer, systems-designer | M |
| 3 | Hose, Storage & Flow Network + Pumps & Machines + Hand Tools | MVP | Feature | game-designer, systems-designer | L |
| 4 | Shipping & Launch + Corporate Radio | MVP | Feature/Pres. | game-designer, narrative-director | M |
| 5 | Drawdown Visuals & Reveals | MVP | Presentation | game-designer, art-director | S |
| 6 | Controller & Interaction, Save & Load, HUD (short GDDs) | MVP | Core/Pres. | game-designer, ux-designer | S each |
| 7 | Ice Harvesting + Ice Handling | Vertical Slice | Core/Feature | game-designer, systems-designer | L |
| 8 | Audio Feedback, Onboarding | Vertical Slice | Pres./Polish | audio-director, game-designer | S |
| 9 | Network Spike | Network Test | Feature | network-programmer, technical-director | S |
| 10 | Contracts, Regions, Chapter Story, Logistics Overlay, Settings | Alpha | Feature/Polish | game-designer, narrative-director | M–L |
| 11 | Co-op Networking, Vehicles, Prestige | Full Vision | — | network-programmer, game-designer | — |

Rows 1–4 are the "3–4 GDDs before building" (timebox 2–3 weeks). Small MVP systems (row 6) get short GDDs. **Alpha fallback:** if time runs short, Contracts and Regions are essential; Logistics Overlay and Chapter Story can shrink to radio milestones.


---

## Circular Dependencies

- **Economy ↔ Shipping:** shipping pays credits by the economy's rule, and the economy's pacing depends on shipping throughput. Resolution: Economy owns the credit/price table as a contract; Shipping calls it and never sets prices. Pacing is tuned with play-log data, not a code dependency.
- **Pumps ↔ Hose Network:** pumps push into hoses, and hose capacity caps pump output. Resolution: the Hose/Storage Network owns the flow solve; pumps register as nodes with a declared rate.

---

## High-Risk Systems

| System | Risk Type | Risk Description | Mitigation |
|--------|-----------|-----------------|------------|
| Economy & Progression Curve | Design | Tier times shrink as tiers rise; source runs out before the ladder ends; hours don't add up (design review) | Pacing Targets; tune with the play log; GDD before building |
| Hand Tools | Design | Spoon/bucket could be a chore or plain rate upgrades | Distinct action per tool; spoon capped 60–90 s; timed bucket test |
| Hose, Storage & Flow Network | Technical | Rope physics can eat weeks | Spline + socket snapping + graph, no physics |
| Drawdown Visuals & Reveals | Technical/Design | Stain-band shader unproven; nothing visible early | 3-day shader spike with a flat-decal fallback; first change within 2 min |
| Corporate Radio | Design | Satire may read preachy or fall flat; untested | One line per launch in MVP; irony-over-grimness test |
| Co-op Networking | Technical/Scope | Retrofit = rewrite | Game State separation and request-only Interaction from day one; Network Spike before commitment |
| Ice Handling & Processing | Technical | Physics desync in co-op | Host-only physics, kinematic snap slots, weight classes |

---

## Progress Tracker

| Metric | Count |
|--------|-------|
| Total systems identified | 26 |
| Design docs started | 12 |
| Design docs reviewed | 1 |
| Design docs approved | 1 |
| MVP systems designed | 12/12 (in review) |
| Vertical Slice systems designed | 0/4 |

---

## Next Steps

- [ ] Review and approve this systems enumeration
- [ ] Design MVP-tier systems first (use `/design-system [system-name]`)
- [ ] Run `/design-review` on each completed GDD
- [ ] Run `/gate-check technical-setup` when MVP systems are designed
- [ ] Validate the highest-risk systems with `/vertical-slice` before committing to Production
