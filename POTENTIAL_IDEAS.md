# Potential / Maybe Ideas

## Threat Budget + Encounter Cards Director (Candidate)

### Concept
Blend L4D pacing with encounter-card scheduling: instead of only interval/cap tuning, the director gets a per-phase **Threat Budget** (points per second) and spends it on curated encounter patterns.

### Why this may improve DriftWood
- Prevents accidental overstacking (commons + specials + events all spiking at once).
- Creates intentional variety without pure randomness.
- Keeps L4D-style pressure/release rhythm while improving readability.
- Makes balancing easier with one currency (threat points) across enemy types.

### Core mechanics
- **Threat budget:** phase defines budget income (Rest low, BuildUp medium, Peak high, Relent very low).
- **Enemy costs:** example costs: Common=1, Hunter=6, Spitter=7, Charger=8, Tank=20.
- **Encounter cards:** director spends budget on cards such as:
  - Flank commons
  - Double special
  - Quiet fakeout then burst
  - Chase pressure trickle
- **Stress scaling:** budget multiplier from player stress (recent damage, low health/ammo, incapacitation, objective urgency).
- **Fairness rules:** max simultaneous disablers, spacing between special spawns, anti-repeat memory (don’t pick same card twice).

### Spawn-role targeting
Use spawn nodes by intent rather than only distance:
- **Ambush nodes:** behind LOS blockers
- **Chase nodes:** ahead on likely path
- **Support nodes:** ranged special angles

### Director mood layer
Add high-level “moods” that bias card pool and audio cues:
- Harass
- Hunt
- Starve
- Finale

### Integration approach (minimal disruption)
Keep current phase system, but phases set budget + mood instead of directly forcing each spawner value every frame.

### MVP path
1. Add threat budget accumulator to director.
2. Implement 3-4 encounter cards.
3. Route hunter/spitter/charger through explicit card-triggered spawn calls.
4. Add fairness constraints and simple anti-repeat memory.
5. Tune with 10-15 minute run target pacing.

## Potential Ideas v2 (AI / Spawn)

### 1) Heat Map Director [Complexity: Medium]
- Track where players repeatedly fight/camp and accumulate local "heat" values.
- Lower spawn preference in overheated zones and bias toward flank/forward nodes.
- Reduces repetitive hold spots without hard anti-camping rules.

### 2) Audio Telegraph Economy [Complexity: Low]
- Reserve small budget for warning cues before high-threat cards.
- Examples: distant scream, dock metal slam, radio static burst.
- Improves fairness/readability so spikes feel earned, not random.

### 3) Adaptive Loot Director [Complexity: Medium]
- Adjust optional loot opportunity frequency based on stress rather than raw HP refill.
- High stress: slightly increase pills/ammo chance in optional side paths.
- Low stress: keep loot neutral and bias toward risk temptations.

### 4) Micro-Objectives During Peaks [Complexity: Medium]
- Inject 20-40s side tasks during long peak windows (breaker, gate, crane panel).
- Completion gives temporary relief (budget freeze, short relent, or card cooldown reset).
- Converts pure survival spam into decision-driven pacing.

### 5) Infected Composition Ruleset [Complexity: Low]
- Track recent composition and enforce diversity windows.
- Example rule: after two ranged-special windows, force one melee-heavy window.
- Prevents repetitive special patterns in back-to-back encounters.

### 6) Biome Mood Modifiers [Complexity: High]
- Add zone tags (warehouse, dock, boardwalk, alleys) that bias card selection.
- Warehouse favors ambush cards; boardwalk favors chase cards; dock favors ranged harassment.
- Makes each area feel authored while still director-driven.

## Suggested Priority (Fastest Value)
1. Audio Telegraph Economy (Low)
2. Infected Composition Ruleset (Low)
3. Heat Map Director (Medium)
4. Micro-Objectives During Peaks (Medium)
5. Adaptive Loot Director (Medium)
6. Biome Mood Modifiers (High)

## Node Health Check (Low-Code QOL)

### Goal
Catch bad spawn layouts early and explain director behavior before deep tuning.

### Run timing
- Execute once on scene start (or first player spawn).
- Optional re-run command for editor testing.

### What to report
- Total `SpawnNode` count.
- Eligible node count from player start.
- Hidden vs visible ratio (LOS-blocked vs LOS-open).
- Distance coverage bands:
  - 300-500
  - 500-900
  - 900+
- Fallback risk estimate (if too few eligible nodes).

### Starting thresholds
- Total nodes target: 12+
- Hidden ratio target: >= 60%
- Band coverage targets:
  - 300-500: at least 3
  - 500-900: at least 4
  - 900+: at least 3

### Output format example
- `[NodeHealth] Total=14 Eligible=10 Hidden=7 (70%) Visible=3`
- `[NodeHealth] Bands: 300-500=3, 500-900=5, 900+=2`
- `[NodeHealth][Warn] Far band under target (2 < 3)`
- `[NodeHealth][Warn] Spawn fallback risk is high for this scene`

### Why this is high value
- Distinguishes map-layout issues from director-logic issues.
- Helps level design quickly place missing nodes in weak bands.
- Reduces unfair-feeling radial fallback usage.

## QOL Ideas (Low-Code Wins)

### 1) Spawn Debug HUD Toggle [Low]
- Add a dev-only HUD overlay showing phase, intensity, alive commons, alive specials, and budget/cooldown values.
- Bind to a debug key so tuning doesn't require log spam.

### 2) Director Snapshot Command [Low]
- Add a one-line runtime dump command (phase, timers, caps, intervals, active modifiers).
- Great for bug reports and multiplayer sync verification.

### 3) Cooldown Jitter [Low]
- Apply small per-spawn randomness (±10-20%) to special cooldowns.
- Keeps pacing from feeling metronomic while preserving balance bands.

### 4) No-Repeat Special Rule [Low]
- Prevent immediate repeat of same special type if alternatives are valid.
- Improves perceived variety with minimal complexity.

### 5) Post-Incap Grace Window [Low]
- After player incap/revive, block special spawns for 10-15 seconds.
- Strong fairness gain, tiny implementation.

### 6) Peak Warmup Delay [Low]
- On entering Peak, allow only commons for first 6-8 seconds.
- Creates smoother escalation instead of abrupt spike.

### 7) Relent Purity Lock [Low]
- During Relent, suppress special spawns entirely (or 90% chance block).
- Makes decompression clearly readable to players.

### 8) Objective Pressure Clamp [Low]
- Clamp objective pressure to avoid overstack with horde/special spikes.
- Prevents event moments from becoming chaotic noise.

### 9) Fallback Spawn Penalty [Low]
- If radial fallback is used repeatedly, raise min spawn distance temporarily.
- Reduces "pop-in" feeling when node coverage is weak.

### 10) Tuning Presets (Casual/Normal/Expert) [Low-Medium]
- Expose a small set of phase/cooldown/cap presets.
- Speeds playtest iteration and future difficulty feature work.

## Potential Ideas v3 (More Brainstorm)

### 1) Hunted Timer [Complexity: Low]
- If players remain in one area too long, gradually raise flank-card odds.
- Moving forward resets pressure buildup.

### 2) False Peak [Complexity: Medium]
- Telegraph a big wave with audio/visuals but send only a probe pack first.
- Deliver the real pressure hit from a side angle ~20s later.

### 3) Shoreline Threat Drift [Complexity: Medium]
- Rotate spawn bias around player heading (left -> rear -> right) over time.
- Creates a "moving pressure front" feel instead of static angles.

### 4) Recover-or-Risk Windows [Complexity: Medium]
- After major fights, provide a short low-threat recovery window.
- Optional side-loot objective during this window cancels safety and spikes intensity.

### 5) Targeted Counterplay Cards [Complexity: Medium]
- Detect dominant player behavior and bias encounter responses.
- ADS camping -> flank commons; kite-heavy movement -> charger bias; choke hold -> spitter bias.

### 6) Mini Weather Combat Modifiers [Complexity: Low-Medium]
- Tie weather bursts to director mood for short tactical shifts.
- Heavy rain favors close ambush pressure; clear breaks favor ranged harassment.

### 7) Pressure Debt Bank [Complexity: Low]
- If intended spawns fail repeatedly (LOS/node constraints), store pressure as debt.
- Repay debt gradually over time to avoid sudden unfair dumps.

### 8) Safe-Room Echo [Complexity: Low]
- After safe-room exit, force 45-60s of readable light-contact pacing.
- Stabilizes early-run rhythm and reduces random-feeling openings.

### 9) Narrative Sting Events [Complexity: Low]
- Trigger rare world stings (radio call, distant flare, warehouse scream).
- Slightly bias the next 20-30s encounter style for thematic direction.

### 10) Director Personality Seeds [Complexity: Low]
- Roll a run-level personality at start (Aggressive, Stalker, Attrition).
- Keep core balance intact while making runs feel less samey.

## Top 5 Prototype Order (Recommended)

### 1) Node Health Check
- **Why first:** Immediate debugging clarity for map vs director issues.
- **MVP output:** One startup report with thresholds and warnings.
- **Expected impact:** Faster spawn tuning and fewer unfair fallback spawns.

### 2) Director Snapshot Command + Spawn Debug HUD Toggle
- **Why second:** Makes every pacing bug reproducible and inspectable quickly.
- **MVP output:** Runtime dump command + optional lightweight overlay.
- **Expected impact:** Major reduction in iteration time.

### 3) No-Repeat Special Rule + Cooldown Jitter
- **Why third:** Big perceived variety with tiny implementation risk.
- **MVP output:** No back-to-back identical special, ±10-20% cooldown variance.
- **Expected impact:** Director feels less scripted and less spammy.

### 4) Peak Warmup Delay + Post-Incap Grace Window
- **Why fourth:** Fairness and readability improvements without reducing challenge identity.
- **MVP output:** 6-8s common-only at peak start, 10-15s special lockout after incap/revive.
- **Expected impact:** Smoother spikes and fewer frustration deaths.

### 5) Audio Telegraph Economy
- **Why fifth:** Adds strong L4D-style readability and atmosphere with minimal systems work.
- **MVP output:** Small cue budget + 3-4 telegraph sounds tied to high-threat cards.
- **Expected impact:** Encounters feel intentional and thematic, not random.

### Candidate after Top 5
- Heat Map Director
- Infected Composition Ruleset
- Pressure Debt Bank



# Island Choice, Progress, and Reward Structure

## Purpose

The island selection system exists to make each voyage feel like a meaningful strategic decision.

Players should not choose islands only because of difficulty modifiers or visual variety.  
They should choose islands because each island offers a **different kind of progress**.

This creates replayability by making players constantly decide:

- what they need right now
- what they want later
- what risk they are willing to take
- what they are willing to give up by not choosing another island

---

## Core Design Goal

Island choice should create tension between:

- **immediate survival**
- **long-term strength**
- **future options**

A good choice should feel like:

- "We are low on fuel, so we need to keep moving."
- "We are injured, so we need stability."
- "We are barely holding together, but a boat upgrade could save the run later."
- "We do not need supplies yet, but intel could open a safer route."

---

## Definition of Progress

In this game, **progress** is anything that improves the team's ability to continue the journey.

Progress does not only mean more loot.

Progress can mean:

- surviving the next mission
- improving the boat
- unlocking new routes
- gaining useful survivors
- learning information that changes future decisions

### Simple Definition
Progress is anything that improves the team's:

- **stability**
- **power**
- **options**
- **knowledge**

---

## The 5 Types of Progress

## 1. Survival Progress

Survival progress helps the team immediately.

This is short-term progress that keeps the current run alive.

### Examples
- ammo
- medicine
- food
- fuel
- temporary weapons
- healing supplies
- infection treatment
- crafting materials

### Why It Matters
This type of reward is best when the team is weak, injured, or low on supplies.

### Player Question
"How do we survive the next mission?"

---

## 2. Boat Progress

Boat progress improves the team's mobile base.

This is long-term progress that makes future travel and future missions easier.

### Examples
- reinforced hull
- larger fuel tank
- stronger engine
- better bilge pump
- more storage
- mounted floodlights
- improved radio
- stronger repair tools
- deck defenses
- better navigation systems

### Why It Matters
This type of reward is best when the team is stable enough to invest in the future.

### Player Question
"How do we make the rest of the campaign easier?"

---

## 3. Route Progress

Route progress changes where the team can go and what choices become available.

This does not always make players stronger directly, but it gives better strategic options.

### Examples
- sea charts
- radio coordinates
- weather reports
- dock access codes
- lighthouse signals
- alternate route data
- safe harbor locations
- hidden fuel cache locations

### Why It Matters
This type of reward is best when players want more control over future choices.

### Player Question
"How do we improve our next decisions?"

---

## 4. Crew Progress

Crew progress adds survivors, specialists, or support systems that help over time.

This creates long-term utility and can make the team feel like it is rebuilding a real group of survivors.

### Examples
- mechanic
- medic
- navigator
- radio operator
- scavenger
- security survivor
- engineer

### Example Benefits
- mechanic reduces repair event time
- medic improves healing efficiency
- navigator reveals more route choices
- scavenger increases resource finds
- radio operator reveals island conditions before landing

### Why It Matters
This type of reward is best when the player wants long-term support instead of raw resources.

### Player Question
"Who can help us survive better over time?"

---

## 5. Knowledge Progress

Knowledge progress reduces uncertainty.

It gives the team information that leads to smarter decisions, safer routes, or better preparation.

### Examples
- infection reports
- enemy type warnings
- hidden cache locations
- storm forecasts
- outbreak clues
- safe zone rumors
- building layouts
- extraction route information

### Why It Matters
This type of reward is best when players want to reduce risk and plan ahead.

### Player Question
"What do we need to know before we commit?"

---

# Island Reward Philosophy

Each island should offer a **different reward profile**.

An island should not just be "another place with loot."  
It should be a strategic option with a clear identity.

Every island should ideally provide:

- **Primary Reward** — the main reason to go there
- **Secondary Reward** — a smaller bonus
- **Main Risk** — the primary danger of the mission
- **Strategic Value** — what kind of progress it offers
- **Opportunity Cost** — what players miss by going there instead of elsewhere

---

# Why Players Choose One Island Over Another

Players choose one island over another because each island solves a different problem.

A strong island-choice system asks:

- Do we need to recover?
- Do we need to keep moving?
- Do we need long-term upgrades?
- Do we need better future options?
- Do we need information more than resources?

The choice becomes meaningful when players cannot get everything.

Choosing one island should often mean:

- delaying another reward
- losing access to another island
- entering the next mission under a different level of risk
- shaping the run in a different direction

---

# Example Reward Categories by Island Type

## Fuel Island
### Main Purpose
Keeps the journey alive.

### Typical Rewards
- fuel drums
- spare fuel lines
- portable generators
- dock equipment

### Progress Type
- survival progress
- travel progress

### Best Chosen When
- fuel is critically low
- the team must keep moving immediately

### Weakness
- usually offers little long-term improvement

---

## Medical Island
### Main Purpose
Restores stability to the team.

### Typical Rewards
- medicine
- trauma treatment
- healing kits
- infection suppressants
- med station blueprints
- rescued medic survivor

### Progress Type
- survival progress
- crew progress

### Best Chosen When
- the team is injured
- the run is becoming fragile
- players need recovery more than power

### Weakness
- often offers less fuel or upgrade progress

---

## Shipyard / Repair Island
### Main Purpose
Improves the boat permanently.

### Typical Rewards
- hull plating
- engine parts
- winches
- tools
- stronger pumps
- storage upgrades

### Progress Type
- boat progress

### Best Chosen When
- the team has enough resources to survive short term
- players want long-term campaign value

### Weakness
- may not help much if the team is already near collapse

---

## Radio Tower / Intel Island
### Main Purpose
Improves future choices.

### Typical Rewards
- route data
- weather forecasts
- coordinates
- distress signals
- safe harbor locations
- infection warnings

### Progress Type
- route progress
- knowledge progress

### Best Chosen When
- players want safer or more rewarding future paths
- players can afford a lower immediate payout

### Weakness
- often gives weak short-term survival value

---

## Survivor Island
### Main Purpose
Adds support and passive benefits.

### Typical Rewards
- specialist survivors
- side objectives
- new boat systems
- morale boosts
- unique tools

### Progress Type
- crew progress
- long-term support progress

### Best Chosen When
- players want scaling campaign benefits
- players value passive advantages over immediate resources

### Weakness
- rewards may feel delayed compared to direct supply islands

---

# Example Island Choice Scenario

## Current Team State
- low fuel
- moderate health
- limited ammo
- boat still heavily damaged

## Available Islands

### 1. Marina Depot
**Primary Reward:** large fuel gain  
**Secondary Reward:** small repair supply cache  
**Main Risk:** heavy shoreline swarms and open exposure  
**Progress Type:** survival / travel progress

**Meaning:**  
This keeps the run alive immediately, but does not improve the team much beyond that.

---

### 2. Wrecked Shipyard
**Primary Reward:** permanent hull and engine upgrade materials  
**Secondary Reward:** heavy tools  
**Main Risk:** difficult holdout event and loud industrial spaces  
**Progress Type:** boat progress

**Meaning:**  
This makes future missions easier, but it may be too risky if the team is already unstable.

---

### 3. Quarantine Clinic
**Primary Reward:** medicine and healing supplies  
**Secondary Reward:** chance to rescue a medic  
**Main Risk:** dense infected interiors and contamination hazards  
**Progress Type:** survival / crew progress

**Meaning:**  
This stabilizes the team and reduces collapse risk, but does not solve fuel problems.

---

### 4. Radio Tower
**Primary Reward:** route intel and possible safe harbor coordinates  
**Secondary Reward:** weather forecast for next mission  
**Main Risk:** exposed uphill climb and dangerous extraction  
**Progress Type:** route / knowledge progress

**Meaning:**  
This may improve future decisions, but provides very little immediate relief.

---

# What Makes This Choice Good

This choice works because each island helps in a different way:

- **Marina Depot** helps the team continue
- **Shipyard** helps the team improve
- **Clinic** helps the team recover
- **Radio Tower** helps the team make better future choices

The player is not choosing between four versions of the same reward.  
The player is choosing what kind of progress matters most right now.

That is what makes island choice meaningful.

---

# Short-Term vs Long-Term Reward Balance

A good route system should frequently ask players to choose between short-term and long-term value.

## Short-Term Rewards
These solve urgent problems right now.

### Examples
- fuel
- medicine
- food
- ammo
- temporary weapons

## Long-Term Rewards
These improve future missions and future decisions.

### Examples
- boat upgrades
- survivor specialists
- route unlocks
- passive bonuses
- intel systems
- improved extraction options

### Core Tension
A healthy run asks:
- "Do we survive now?"
or
- "Do we invest in being stronger later?"

---

# Recommended Reward Rule

To keep island selection readable, each island should strongly focus on **one main progress type** and lightly support **one secondary progress type**.

### Good Example
A clinic island might be:
- primary: survival progress
- secondary: crew progress

A shipyard island might be:
- primary: boat progress
- secondary: survival progress

A radio tower island might be:
- primary: knowledge progress
- secondary: route progress

This keeps choices clear and prevents all islands from feeling identical.

---

# Final Design Rule

If all islands reward the same thing, island choice becomes cosmetic.

If islands reward different forms of progress, island choice becomes strategic.

The player should always feel like they are deciding between:

- immediate safety
- future strength
- better options
- better information

That is the foundation of meaningful island selection.