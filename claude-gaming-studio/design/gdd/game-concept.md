# Game Concept: Mars Needs Water

*Created: 2026-11-01*
*Status: Draft (revised after `/design-review`, 2026-11-01)*
*Source: concept GDD v1.0 (October 2026, written under the earlier title "Last Drop"), refined through `/brainstorm` with creative, art, technical and producer reviews.*

---

## Elevator Pitch

> It's a first-person co-op incremental game where you drain Earth's lakes and carve its glaciers
> into rocket cargo for sealed colonies on Mars, starting with a spoon and ending with planetary
> extractors, while your employer thanks you on the radio.

---

## Core Identity

| Aspect | Detail |
| ---- | ---- |
| **Genre** | First-person incremental / industrial extraction, with physical interaction |
| **Platform** | PC (Steam first) |
| **Target Audience** | Mid-core PC players who like incremental, factory and "satisfying job" games; see Target Player Profile |
| **Player Count** | 1–4 co-op; solo is complete and fully supported |
| **Session Length** | 30–90 minutes |
| **Monetization** | Premium (one-time purchase) |
| **Estimated Scope** | Large (3–5+ years for the full vision, solo). First shippable target: a 3–5 hour solo release in 12–18 months, solo |
| **Comparable Titles** | Don't Drain The Lake, PowerWash Simulator, Satisfactory |

---

## Core Fantasy

You are the contractor who, one spoonful at a time, turns a dying planet into cargo, and you watch the
landscape change because of you. The game offers the pleasure of a job done with your hands, growing
from a spoon to a megastructure, paired with the uneasy knowledge of what that job costs.

Why this game over another: few games let you physically take a lake apart and see the hole you
leave, and none frame it as a corporate export contract to another planet.

---

## Unique Hook

**Like a draining incremental, and also you carve glaciers into rocket cargo for a corporation that
thanks you on the radio.**

- Explainable in one sentence: yes.
- Distinct from the closest competitor, Don't Drain The Lake (reservoir draining with buckets, pumps
  and siphons): we add glacier cutting into physical cargo, an interplanetary shipping goal, a
  corporate-satire framing, and co-op physical comedy.
- Connected to the core fantasy: the cargo you cut is the planet you are dismantling.
- Affects gameplay: ice is a second, physical resource with weight classes, lifting tools and
  packaging, not a reskin of water.

*Note: Nour considers the concept close to "Holy Crop". It needs a direct comparison before the
store page is written (see Open Questions).*

---

## Player Experience Analysis (MDA Framework)

### Target Aesthetics (What the player FEELS)

| Aesthetic | Priority | How We Deliver It |
| ---- | ---- | ---- |
| **Sensation** (sensory pleasure) | 2 | Flowing hoses, pressure gauges, the audible change in flow rate, the rocket launch, water planes visibly dropping |
| **Fantasy** (make-believe, role-playing) | 3 | Being the extraction contractor; corporate radio, milestones and signage |
| **Narrative** (drama, story arc) | 6 | A six-chapter arc to "The Last Shipment"; environmental storytelling over dialogue |
| **Challenge** (obstacle course, mastery) | 7 | Logistics bottlenecks (extraction, storage, shipping); no more than three at once |
| **Fellowship** (social connection) | 5 | Optional co-op: bucket brigades, one drives while another lays hose, shared cargo handling |
| **Discovery** (exploration, secrets) | 1 | What the receding water reveals: lakebeds, wrecks, cars, signs; new regions open up |
| **Expression** (self-expression, creativity) | 8 | Layout of hoses, pumps and storage; light, not a building game |
| **Submission** (relaxation, comfort zone) | 4 | No death, no hard failure; steady, satisfying routine work |

*Discovery ranks first because the drawdown reveal (pillar 3) is the game's most distinctive
payoff. Sensation is second because the physical loop (pillar 2) has to feel good on its own.*

### Key Dynamics (Emergent player behaviors)

- Players optimise throughput by spotting the current bottleneck (extraction, storage or shipping)
  and fixing it, then finding the next one.
- Players linger at the shoreline after an upgrade to see what the water uncovered.
- In co-op, players split jobs naturally (one pumps, one carries, one launches) without assigned roles.
- Players redeploy older machines to new sources instead of leaving them idle.
- Players notice and joke about the gap between the corporate celebrations and the ruined landscape.

### Core Mechanics (Systems we build)

1. **Numerical water model:** each source holds a volume; machines extract at a rate limited by what
   remains, by pipe throughput, by storage and by shipping. The visible water plane follows the volume.
2. **Physical extraction tools:** a short hand-tool stage (spoon, then bucket) before the first pump; then place pumps, snap hoses between sockets, start and
   maintain machines by hand.
3. **Ice harvesting:** cut marked glacier faces into standard blocks in fixed weight classes, then lift,
   stack, crush or melt them. Volumes are counted as water-equivalent litres.
4. **Shipping and contracts:** move cargo to the spaceport, launch shipments, earn credits for
   delivered water-equivalent volume, fulfil colony contracts. Automated launching is built and
   maintained by hand (for example, laying the feed line to the pad), never bought as a button.
5. **Upgrade progression:** a shop of machines, each a step change in scale, unlocking new regions.
6. **Real-scale rule (Nour, 2026-11-02):** every water source holds its real-world volume (MVP: a 20,000 L farm pond; later real lakes, reservoirs and glaciers). A corporate **Earth counter** starts at all of Earth's water, about 1.386 × 10²¹ L (332.5 million cubic miles), and ticks down with every shipment. It is never emptied: after the whole campaign the player has taken a sliver, and the corporation celebrates it anyway (Pillar 4).

---

## Player Motivation Profile

### Primary Psychological Needs Served

| Need | How This Game Satisfies It | Strength |
| ---- | ---- | ---- |
| **Autonomy** (freedom, meaningful choice) | Players choose which bottleneck to fix, which source to work and how to lay out their operation; purchases are real trade-offs (bigger storage vs. faster launches) | Supporting |
| **Competence** (mastery, skill growth) | Throughput visibly rises; each tier is a jump in scale; the landscape is the scoreboard | Core |
| **Relatedness** (connection, belonging) | Co-op jobs that are faster or funnier together; shared progress in a host-owned world | Supporting (Minimal when solo) |

### Player Type Appeal (Bartle Taxonomy)

- [x] **Achievers** (primary): contracts, credits, machine unlocks, chapter milestones, the next tier.
- [x] **Explorers** (secondary): what the water reveals, how the systems interact, new regions.
- [x] **Socializers** (secondary, co-op only): bucket brigades, physical comedy, shared operations.
- [ ] **Killers/Competitors:** not served. No PvP, leaderboards or griefing.

Who it's not for: players looking for combat, survival tension or deep factory-building puzzles.

### Flow State Design

- **Onboarding curve:** the first 30 minutes (GDD section 8) teach one verb at a time: scoop with a spoon
  (60–90 s, played as a joke), carry and pour a bucket, first launch, hand pump, electric pump, first trade-off, first reveal, route to the next region.
  The world visibly changes within the first 2 minutes. No long instructions.
- **Difficulty scaling:** difficulty comes from logistics, not reflexes. New regions add a new
  bottleneck or a new physical verb, never more than three competing throughput problems at once.
- **Feedback clarity:** flow particles and gauges on hoses, a logistics overlay for blocked
  connections, the water plane and stain bands as progress, credits on launch.
- **Recovery from failure:** there is no failure state. Bad logistics lowers throughput; it never
  destroys progress. A full tank stops a pump safely.

---

## Core Loop

### Moment-to-Moment (30 seconds)

Scoop with a spoon or fill a bucket and pour it, hold a pump handle, drag a hose to a socket, cut an ice seam, stack a
block. Each action has weight, sound and a visible result (a level rises in the tank, the shore
moves). The 15–25 second bucket trip has to be satisfying on its own: this is the MVP's first test.

### Short-Term (5-15 minutes)

Collect enough for the next shipment, launch it, earn credits, buy the next machine, set it up.
"One more" lives in the next upgrade being just affordable, and in the next drop of the water
revealing something.

### Session-Level (30-90 minutes)

Work a source until a contract is fulfilled or a chapter milestone is hit: a lake is visibly drained
or a glacier face is cut back. A session ends naturally at a contract payout or a newly unlocked
region, with a reason to return: the new region and the machine that opens it.

### Long-Term Progression

Six chapters (Crystal Lake, River Valley, Great Reservoir, Frozen Frontier, The Coast, The Last
Shipment), a hand-tool stage (spoon, then bucket at 0.5 L/s effective) followed by pump tiers up to the
planetary extractor (1,000,000 L/s), nine tiers counting the bucket,
and an ice-machinery chain from ice pick to glacier excavator. The campaign ends when a sequence of
Martian colony contracts is fulfilled, starting at 1,000 L (the size of the final contract is an
open question; see Pacing Targets). Prestige or endless mode is optional,
later.

### Retention Hooks

- **Curiosity:** what's under the water; the next region; how the story ends.
- **Investment:** an operation the player built and a landscape they changed.
- **Social:** a co-op group's shared world (host-owned save).
- **Mastery:** higher throughput, better layouts, clearing bottlenecks faster.

### Pacing Targets

Targets that later GDDs and playtests calibrate against. Prices are tuned by playtest to hit them.

- **Spoon:** 60–90 seconds, then the bucket. It is a satire beat (the radio thanks you for every
  drop), not a pacing stretch.
- **First pump:** within 4 minutes of play.
- **Time per upgrade rises, never shrinks:** about 1–3 minutes early in a region, 3–6 minutes in the
  middle, 5–10 minutes near the end. The payoff tiers should not rush past while the hand work drags.
- **First visible change:** within the first 2 minutes (a steep shallow edge or an early first stain
  band). After that, a stain band or reveal at least every 5 minutes.
- **Sources outlast their ladders:** each purchase leaves enough of the source to enjoy the new
  machine on it before the region's milestone.
- **Solo release:** 3–5 hours across 2 regions, roughly 1.5–2.5 hours each. Hours come from source
  volumes, region gates and contract sizes, not from slower prices.

---

## Game Pillars

### Pillar 1: Start Tiny, End Enormous

The player grows from a spoon to planetary-scale machinery, and every jump in scale
shows up in the world, not only in the rate.

*Design test*: if we're debating between a new machine that is a +10% rate upgrade and one that
changes what the player can reach or how the landscape changes, we choose the second.

### Pillar 2: Hands on the Machinery

The player works the operation with their hands: carrying, dragging, plugging in, cutting and
stacking.

*Design test*: every new tier adds a new physical action, and automation is set up and maintained by
hand, never bought as a menu button. This includes hand tools: each one adds a new action or new
reach (for example, a bucket lets you wade in), not just more litres per trip. It also includes
launching: automated launching is built by hand. If a feature can only be done through a menu, it
doesn't ship as designed.

### Pillar 3: The World Shows the Damage

Waterlines recede, lakebeds and wrecks appear, glacier faces change: progress is visible in the
landscape.

*Design test*: if we're debating between showing progress in the UI or in the world, it appears in
the landscape first.

### Pillar 4: Bleak Corporate Satire

The tone is irony, not misery: the corporation celebrates, the player sees the cost.

*Design test*: when unsure, choose irony over grimness, and corporate rewards celebrate exactly what
the landscape mourns. The game makes the point, not the player's suffering.

### Production Rule: Co-op Helps, Solo Works

Every machine and job can be done alone. Two-person jobs may be faster or funnier, never required.
(Moved from pillar to production rule after creative review: it governs features, not creative
direction.)

### Anti-Pillars (What This Game Is NOT)

- **NOT combat or survival meters:** they would pull focus from the physical work (pillar 2) and
  the satire (pillar 4).
- **NOT hard failure or a save-killing timer:** the evacuation countdown is narrative framing; a real
  timer would punish the slow, deliberate pace and the world change (pillar 3) the game is built on.
- **NOT playable Mars:** Mars stays offscreen (hologram, radio, progress map). A second world would
  halve the payoff of changing Earth.
- **NOT a second currency:** ice and water both count as water-equivalent litres; a second currency
  would muddy the loop.
- **NOT a strict simulation:** physics is assisted and readable; fun beats realism (pillar 2 without
  fiddliness).

---

## Visual Identity Anchor

**Direction: The Bathtub Ring**, with Toy-to-Titan asset rules and Corporate Postcard accents.

**Visual rule: Every surface remembers where the water was.**

Mood: quiet and uncanny: wide skies, still air, drying mud. Shapes: soft, eroded land set against
hard, geometric human-made objects.

Supporting principles:

1. **Height is history.** Each drawdown leaves a stain band. *Test:* could a player returning after an
   hour tell how much was taken without opening the UI?
2. **Reveal, don't remove.** Lowering water uncovers authored objects (wrecks, cars, signs). *Test:*
   does each drawdown step uncover at least one readable object?
3. **Read at a distance.** Shorelines are big flat bands of value, not texture detail. *Test:* does the
   change still read as a 64 px thumbnail?

Asset standards (from Toy-to-Titan): each machine tier is at least 2× its predecessor's footprint
(machines only; hand tools stay hand-sized);
interactive parts (handles, valves, sockets, triggers) use the employer's safety orange and can be
found within 1 second; chunky,
grabbable handles, valves and hoses.

Accent layer (from Corporate Postcard): signage, milestone screens and UI use cheerful warm brand colours
(sunshine yellow, cream, a muted corporate navy), never a saturated cool hue, so water keeps that
slot to itself; every milestone pairs a celebration with the loss it caused; signage carries
jokes without voice-over.

**Colour philosophy:** water is the only fully saturated cool hue in the world; once taken, it's gone
from the palette. Exposed beds are bone, ochre and rust. The employer's safety orange appears only on
the player's own tools and machinery (which is also where every interactive part lives), so the
culprit is always the brightest thing in view.

Pipeline: flat-shaded low-poly, one shared palette texture, URP, one world-height stain-band shader,
stylised flat water planes (no photoreal fluid). The band shader is prototyped in the MVP lake.

---

## Inspiration and References

| Reference | What We Take From It | What We Do Differently | Why It Matters |
| ---- | ---- | ---- | ---- |
| Don't Drain The Lake | First-person draining incremental with buckets and pumps | Glacier cargo, shipping to Mars, satire, co-op, chaptered regions | Validates the niche; also the main competitor to differentiate from |
| PowerWash Simulator | Satisfying, visible "job done by hand" progress; co-op chores | Progress is destructive; the satisfaction is meant to sit uneasily | Proves visible-progress manual work sells |
| Satisfactory | Throughput bottlenecks and scaling up an operation in first person | Far lighter building; physical handling over factory layout | Shows first-person logistics can carry long sessions |
| Holy Crop | Close in concept (per Nour) | To be assessed | Needs direct comparison before the store page |

**Non-game inspirations**: drought photography of drained reservoirs (bathtub rings at Lake Mead);
corporate brochure and safety-poster design; satire in the vein of corporate "progress" propaganda.

---

## Target Player Profile

| Attribute | Detail |
| ---- | ---- |
| **Age range** | 18–35 |
| **Gaming experience** | Mid-core |
| **Time availability** | 30–90 minute sessions on weeknights, longer with friends at weekends |
| **Platform preference** | PC (Steam) |
| **Current games they play** | PowerWash Simulator, Satisfactory, Lethal Company |
| **What they're looking for** | A tactile, satisfying job with visible progress, playable solo or with friends, with something to say |
| **What would turn them away** | Fiddly physics, idle-game passivity, grind without new verbs, preachy tone |

---

## Technical Considerations

| Consideration | Assessment |
| ---- | ---- |
| **Engine** | Unity (developer's stated preference), with URP and the new Input System. Avoid HDRP and its water system. Configured by `/setup-engine` |
| **Key Technical Challenges** | Retrofitting co-op networking; ice rigid bodies in co-op; hose connections; receding water surfaces; very large numbers; save versioning |
| **Art Style** | 3D stylised, flat-shaded low-poly |
| **Art Pipeline Complexity** | Low to Medium (palette-atlas low-poly, asset-store first-person controller, one custom shader) |
| **Audio Needs** | Moderate (flow, machinery and launch sounds; radio broadcasts) |
| **Networking** | Host-authoritative client-server (Netcode for GameObjects or FishNet over Steam transport); none in the MVP |
| **Content Volume** | Full vision: 6 handcrafted regions, 9 pump tiers, ~9 ice tools, 15–25 hours. First release: 2 regions, 3–5 hours |
| **Procedural Systems** | None planned |

Architecture guardrails (technical review): keep game state (volumes, tank contents, credits, machine
states) in plain data classes separate from visuals and changed only through clear methods, so co-op
can be added later; water as a number that moves a plane over a pre-built lakebed, no runtime terrain
changes; hoses as visual splines with socket snapping and a connection graph; `double` for volumes and
credits so sub-litre amounts work (the exact representation and rounding rule are decided in an
architecture decision); versioned JSON saves of the data model, never BinaryFormatter.

---

## Risks and Open Questions

### Design Risks

- The early hand-tool stage may feel like a chore rather than a pleasure; the spoon is capped at
  60–90 seconds and the MVP times a bucket-only stretch to test it.
- The late game may drift into idling; mitigated by pillar 2 (new physical action every tier).
- The satire may read as preachy; mitigated by pillar 4's irony-over-grimness test.

### Technical Risks

- **Co-op retrofit (highest):** networking added to a single-player codebase often means a rewrite.
  Mitigation: data/visual separation from day one; a dedicated 2-player network test before co-op is
  committed.
- **Ice physics in co-op:** desync and jitter. Mitigation: host-only physics, kinematic snap slots,
  discrete weight classes.
- **Hoses:** rope physics can eat weeks. Mitigation: spline plus socket snapping.
- **Water surfaces:** Mitigation: numeric model, moving plane, pre-built lakebed, height-triggered
  reveals.
- **Large numbers and saves:** Mitigation as in Technical Considerations.

*Technical review verdict: CONCERNS (no blockers; the full vision is beyond a solo first project
unless networking is proven early).*

### Market Risks

- Don't Drain The Lake already occupies the niche; the hook and visual identity must separate us.
- Holy Crop may overlap; not yet assessed.
- Name: "Mars Needs Water" is not yet confirmed clear on Steam or trademark search.

### Scope Risks

- The full vision (15–25 h, 6 regions, 4-player physics co-op) is 3–5+ years solo and not credible
  as a first project. Mitigation: a solo 3–5 hour release as a real fallback, co-op as a later update.
- Work is most likely to stall between the vertical slice and Early Access; the solo release tier
  makes that a shippable point instead of a broken one.
- Polishing feel or visuals before the full loop works end to end.

*Producer review verdict: UNREALISTIC as originally planned; the revised tiers below address it.*

### Open Questions

- Does the hand-tool stage hold attention, and does the ladder keep players 15+ minutes? *Answered by
  the MVP's two tests.*
- Is there a middle hand tool (a cup) between spoon and bucket? *Only if it gets its own physical
  action; otherwise cut.*
- Can 2 players sync water volume and one machine reliably in Unity? *Answered by the network test.*
- How close is Holy Crop? *Answered by a direct comparison before store-page work.*
- Credit prices and upgrade costs: *tuned by playtest against the Pacing Targets.*
- **Voice Sound Packs (Nour, 2026-11-02):** players record their own voice for footsteps, menus, water draining, rocket launches and music, and share them with friends in co-op. Strong fit for co-op physical comedy and the satire; clashes with the quiet, uncanny Bathtub Ring mood unless it stays opt-in. Needs: a pillar case, a check of prior art, and answers on mic permissions, how many sound slots, and what friends hear from each other's packs. *Planned with the co-op update, not the MVP.*
- How big is the final colony contract? The GDD's 1 trillion litres conflicts with a 15–25 hour
  campaign. Under the real-scale rule, contracts use real numbers; the Earth counter is the satire scale, not a goal. *Answered in the Economy/Progression GDD.*

---

## MVP Definition

**Core hypothesis**: hand work, pumping and shipping water from one shrinking lake is satisfying,
tested two ways with 2–3 external testers (not the developer), using the play log:

- **(a) The hand work holds up:** in a timed 3-minute bucket-only stretch, at least 2 of 3 testers
  keep going unprompted and don't call it a chore. *Fails if* most testers stop or ask when it ends.
- **(b) The ladder pulls players forward:** testers play 15+ minutes unprompted and reach the first
  pump within 4 minutes. *Fails if* most stop before 15 minutes or the first pump takes longer.

**Required for MVP** (solo):

1. First-person controller (asset store); a spoon, then a bucket: scoop, carry and pour.
2. One compact lake as a numeric volume driving a flat water plane, with a pre-built emptied lakebed
   swapped in at thresholds and the stain-band shader. Something visibly changes within 2 minutes.
3. One pump with an upgrade (hand, then electric), hose snapped between sockets on a spline.
4. One water tank that tracks volume.
5. A rocket launch button that pays credits per litre shipped.
6. A shop with 3–4 items.
7. A versioned JSON save of credits, purchases and lake volume.
8. One corporate radio line per launch (Pillar 4 is otherwise untested).
9. A CSV play log (time between purchases, time to each 10% drained).

**Explicitly NOT in MVP** (defer to later):

- Co-op and networking
- Glaciers and ice
- Vehicles
- Missions, contracts and narrative
- Additional regions
- Final art

### Scope Tiers (if budget/time shrinks)

Timelines assume solo, first game, roughly full-time; double them if part-time.

| Tier | Content | Features | Timeline |
| ---- | ---- | ---- | ---- |
| **MVP** | One lake | Core loop only, solo | 6–10 weeks |
| **Vertical Slice** | Lake plus one glacier face | Ice blocks in weight classes with snap slots, crusher, solo | +2–4 months (4–6 months total) |
| **Network Test** | Same | 2 players syncing lake volume and one machine; decide co-op now or after launch | +1–3 months |
| **Solo Release** | Lake region plus glacier region, 3–5 hours | Full solo loop, contracts, polish; co-op as a later update | 12–18 months total |
| **Early Access** | 3–4 regions | 1–4 player co-op, 6–8 machines, 4–6 ice tools | 2–3 years total |
| **Full Vision** | 6 chapters, 15–25 hours | All features, polished | 3–5+ years total |

---

## Next Steps

- [x] Configure Unity and its reference docs (`/setup-engine`)
- [x] **Prototype the lake loop** (`/prototype lake loop`): PROCEED
- [x] Concept review (`/design-review`): NEEDS REVISION, revised 2026-11-01
- [ ] Play v2 of the prototype with 2–3 external testers
- [ ] Phase gate (`/gate-check`)
- [ ] If the prototype PROCEEDS: visual identity (`/art-bible`)
- [ ] Decompose the concept into systems (`/map-systems`)
- [ ] Design each system (`/design-system [system-name]`), each followed by `/design-review`
- [ ] Architecture blueprint and decisions (`/create-architecture`, `/architecture-decision`)
- [ ] Build the vertical slice (`/vertical-slice`) and validate with a playtest (`/playtest-report`)
- [ ] Plan the first milestone (`/sprint-plan new`)
