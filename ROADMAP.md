# DriftWood Roadmap Notes

## Objective/Event Ideas (Shortlist)

### 1) Breaker Box Chain (Recommended first)
- 3 sequential power boxes across the map.
- Each successful interaction increases AI Director pressure.
- HUD updates objective text/progress after each box.
- Final box unlocks progression path (door open / blocker disabled).

### 2) Noise Tradeoff Loot Rooms
- Optional side rooms with high-value loot (meds/ammo/throwable).
- Opening room triggers a danger spike (StaticEnemyGroup or temporary spawn boost).
- Not required for level completion; pure risk/reward decision.

### 3) Boat Fuel Finale (3-step climax)
- Sequence: Pump -> Valve -> Ignition.
- Each step raises pressure and updates objective messaging.
- Final step starts extraction countdown and completion flow.

## Deferred Idea

### Rescue Closet
- Keep as planned content, but defer implementation until multiplayer/AI teammate systems are in place.
- Future-proof by using generic locked-objective interaction patterns now.

## MVP Writeup: Reusable Multi-Step Objective System

### Goal
Create a generic staged objective system that supports: ordered steps, escalating pressure, HUD progression, and final unlock action.

### MVP Scope (Phase 1)
- Implement a 3-step Breaker Box event.
- Use current interaction flow (world button + E use).
- Raise AI pressure per completed step.
- Unlock route on final completion.

### Proposed Components

#### `MultiStepObjective` (controller)
- Owns sequence state and current step index.
- Updates HUD objective text/progress via `PlayerStats`.
- Pushes pressure changes to `AIDirector`.
- Triggers completion behavior when last step is done.

#### `ObjectiveStepButton` (interactable)
- References a `MultiStepObjective` + `StepIndex`.
- Validates whether it is the active expected step.
- On success: notifies controller, plays local feedback, enters completed state.
- Optional idle highlight for active step.

### State Flow
1. `Idle` -> `Active` (first step becomes interactable)
2. Complete step N -> unlock step N+1
3. Repeat until final step
4. `Completed` -> execute unlock + optional completion message timer

### Inspector-Driven Settings
- Step count or explicit step list.
- Per-step objective text.
- Per-step pressure targets (example: 0.35 -> 0.6 -> 0.85).
- Completion actions:
  - disable blocker colliders
  - enable/disable specific objects
  - play completion SFX
- One-shot replay toggle.

### HUD/Feedback Plan
- Objective text format: `Restore power (X/3)`.
- Progress bar normalized by completed steps.
- Urgency style near final step.
- Visual feedback:
  - active step pulses
  - completed steps dim/mark as done
  - invalid interaction gives deny cue

### Implementation Notes
- Reuse existing crane/button interaction style.
- Cache component references in `OnAwake()`.
- Avoid per-frame scene-wide searches.
- Keep controller generic for reuse in Boat Fuel Finale.

### Acceptance Criteria (MVP)
- Only current step can be used.
- HUD always shows correct stage and progress.
- Pressure reliably escalates after each completed step.
- Final step unlock action fires exactly once.
- One-shot mode prevents replay when enabled.

## Suggested Build Order
1. Build `MultiStepObjective` + one working button.
2. Expand to 3-step sequence with HUD and pressure.
3. Scene prototype pass in `firsttest.scene`.
4. Duplicate pattern for Breaker Box and Boat Finale variants.

## Near-Term Priority
1. Breaker Box Chain (first implementation target)
2. Noise Tradeoff Room (optional content)
3. Boat Fuel Finale (end-of-level escalation)
4. Rescue Closet (deferred until co-op/AI teammate support)
