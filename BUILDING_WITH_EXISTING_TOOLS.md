# Building With Existing Tools

Use this playbook for every area of DriftWood: gameplay, multiplayer, UI, editor tooling, content workflows, debugging, and optimization.

## Core rule

Before building something custom, check what already exists in this order:

1. s&box built-ins
2. Existing code already in DriftWood
3. Thin wrappers/helpers around built-ins
4. Small project-specific systems
5. Larger custom architecture only when the above are insufficient

Custom code should be the last option, not the first instinct.

## Decision checklist

For any new feature or refactor, answer these first:

- What engine/tooling feature already exists for this?
- What do we already have in this repo that solves part of it?
- Can a thin helper make the built-in tool usable enough?
- What exact gap remains that requires custom code?
- How will this choice affect future scope?

If those answers are unclear, stop and research before implementing.

## Tool categories to check first

### s&box engine/runtime
- Component lifecycle: `OnAwake()`, `OnStart()`, `OnUpdate()`, `OnFixedUpdate()`, `OnDestroy()`
- Scene queries: `Scene.GetAllComponents<T>()`
- Hierarchy queries: `GetInDescendantsOrSelf`, `GetInAncestorsOrSelf`
- Physics/tracing: `Scene.Trace`, `Scene.PhysicsWorld`
- Networking: `Networking`, `Connection`, `GameObject.Network`, `NetworkSpawn`, `[Sync]`
- Rendering/camera: `CameraComponent`, renderer components, render tags
- Audio: `Sound.Play`, `SoundHandle`, sound events
- UI: Razor components, `.razor.scss`, `BuildHash()`
- Editor exposure: `[Property]`

### Existing DriftWood systems
- Reuse current systems before adding parallel ones.
- Extend existing managers/components if the responsibility is already there.
- Prefer shared helpers over repeated fallback logic.

Examples to check first:
- Player stats/state: `PlayerIdentity`, `PlayerStats`
- Spawn/session behavior: `GameManager`, `SpawnNode`, `EnemySpawner`
- Input/camera/player flow: `PlayerMovement`, `CameraMovement`, `WeaponManager`
- UI patterns: `Code/ui/`
- Objective/event flow: `ObjectiveEvent`, `CraneEventButton`

### Thin wrappers that are worth creating
- Ownership helpers
- Renderer lookup helpers
- Runtime reference rebinding helpers
- Debug/validation helpers
- Small adapter classes for repetitive engine API usage

These are good when they reduce copy/paste without hiding engine behavior too much.

## When custom architecture is justified

Build a larger custom system only if at least one of these is true:

- Built-ins are too low-level and the same glue code is appearing in multiple places
- Repo code already has repeated patterns that should be centralized
- The feature needs consistent behavior across multiple systems
- Debugging cost is rising because responsibilities are scattered
- Scope growth will make the current ad-hoc approach expensive to maintain

## Anti-patterns

Avoid these unless there is a strong reason:

- Rebuilding systems the engine already provides
- Creating a second system that overlaps an existing project system
- Making scene references mandatory when runtime resolution is safer
- Mixing authority logic with presentation logic
- Introducing large frameworks before the real gap is understood
- Solving one bug by adding hidden fallback logic in multiple places

## Required note for future work

For every meaningful feature/refactor, write a short note in the task, PR, or work summary:

- Built-ins considered:
- Existing repo systems considered:
- Helpers/wrappers considered:
- Why custom code is needed:
- Scope risk if custom code grows:
- Validation performed:

## DriftWood-specific default

DriftWood should favor:
- engine-first solutions
- small, composable components
- runtime rebinding over brittle scene-only wiring
- shared helpers over repeated patches
- phased architecture changes instead of big rewrites

## Related docs
- `MULTIPLAYER_TOOLING_PLAYBOOK.md`
- `CLAUDE.md`
- `.github/copilot-instructions.md`
