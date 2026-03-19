# DriftWood — Project Overview for Claude

## What is this?

DriftWood is a **Left 4 Dead-inspired cooperative zombie survival game** built in **S&box** (the Garry's Mod successor by Facepunch Studios). It's a first-person shooter where players fight through waves of infected enemies, manage resources, and try to survive long enough to reach a safe room.

### Core Concept & Vision

The infection broke out on a cruise ship. Survivors from different walks of life escape to shore and must fight port to port, trying to find somewhere safe.

**Characters (cosmetic only, no stat differences):**
- Tourist — just on vacation, unprepared
- Port Worker — knows the layout, practical
- Cruise Employee — knows the ship inside out
- Stowaway — mysterious background

**Level structure:** Linear levels with start/end safe rooms. Levels are released continuously as the game grows. The ship may serve as a tutorial level. Between-level hub (the ship) shows port-to-port progress on a map.

**Objectives:** Task-based intensity spikes instead of boss fights — e.g. signal a rescue boat, secure a vessel, find supplies. While the task is being completed the AI Director ramps up. Objectives are themed to the beach port setting.

**Tone:** Beach/coastal survival horror. Starts daytime, gets darker as the level progresses. Mix of tight indoor corridors and open outdoor port areas.

**End goal:** 4-player cooperative multiplayer.

---

## S&box Architecture Rules

These are non-negotiable constraints of the engine — always follow them:

- All game logic must inherit from `Component`
- Editor-exposed values use `[Property]` attributes
- Cache component references in `OnAwake()`, **never** in `OnUpdate()`
- Use `GetInDescendantsOrSelf` / `GetInAncestorsOrSelf` for flexible component search
- **`GameObject.Root` is the scene root, NOT the player root** — this has caused bugs before
- `CharacterController` does NOT create raycast-hittable colliders — enemies need a separate `BoxCollider` added at spawn
- `SkinnedModelRenderer` is NOT returned by `Components.Get<ModelRenderer>()` — use a `GetRenderer()` helper that checks skinned first
- Events must be unsubscribed in `OnDestroy()`
- Static fields on classes like `PlayerStats` persist between play sessions in the editor — always reset them in `OnAwake()`
- UI uses Razor components (`.razor` + `.razor.scss`) with `BuildHash()` for efficient re-renders
- `Sound.Play(SoundEvent, Vector3)` spatializes sound — for player weapons use `Sound.Play(SoundEvent)` (no position) so they play as 2D
- Enemy sounds (growls, footsteps, pain) should keep WorldPosition since they're spatial
- NavMesh pathfinding not yet implemented — enemies use straight-line movement. Bake NavMesh per map in Hammer once levels are closer to final geometry

---

## What's Been Built

### Core Player Systems
| File | Purpose |
|------|---------|
| `Code/PlayerMovement.cs` | WASD movement, sprinting with stamina drain (L4D-style), crouching, jumping, footstep sounds |
| `Code/CameraMovement.cs` | First/third person camera, mouse look, screen shake on damage, camera lowers when downed, directional damage indicators |
| `Code/WeaponManager.cs` | Fixed 5-slot inventory (Primary, Secondary, Main Heal, Sub Heal, Utility). Weapon switching, input handling, updates `PlayerStats` for HUD |
| `Code/Gun.cs` | Abstract weapon base: shooting, reloading, reload animation, ammo tracking, muzzle flash point light |
| `Code/HealthComponent.cs` | Health/damage/death with incapacitation intercept — at 0 HP triggers down state instead of instant death |
| `Code/IncapacitationComponent.cs` | L4D-style downed state: 60s bleed-out timer, enemies drain it faster, revive via health kit |
| `Code/PlayerStats.cs` | Static data bus for UI — health, stamina, ammo, incap state, damage indicators |

### Weapons
| File | Details |
|------|---------|
| `Code/Guns/Pistol_New.cs` | Raycast-based hitscan pistol with headshot detection |
| `Code/Guns/SMG.cs` | Automatic SMG |
| `Code/Guns/Shotgun.cs` | Pellet spread shotgun — 8 pellets, 20 damage each, spread 0.04. Uses ancestor fallback for HealthComponent search |
| `Code/Weapons/MeleeWeapon.cs` | Abstract melee base using sphere trace |
| `Code/Weapons/Bat.cs` | Baseball bat |
| `Code/Weapons/Axe.cs` | Fire axe |

Headshots detected via `HeadshotZone.cs` (trigger zone on enemy head, 2x damage multiplier).

### Enemy Systems
| File | Purpose |
|------|---------|
| `Code/Enemy.cs` | Standard zombie AI: Patrol → Chase → Attack → Search state machine, attack lunge, flash on hit, stagger, knockback, ragdoll death, ambient growl sounds. AttackDamage = 2f (intentionally low). Speed varies ±15% per spawn. |
| `Code/EnemySpawner.cs` | Wave-based spawning; adds BoxCollider + HeadshotZone to each spawned enemy. Spawn ray starts at player Z+20 and traces down 500 units. |
| `Code/AIDirector.cs` | Dynamic difficulty — scales spawn rate/count based on player stress (L4D-style intensity system) |
| `Code/SpecialInfected/Tank.cs` | Boss enemy: 1500 HP, charge attack, devastating punch |
| `Code/SpecialInfected/Hunter.cs` | Pouncing enemy: crouches to telegraph, leaps at player, pins them |
| `Code/SpecialInfected/TankSpawner.cs` | Spawns Tank at high AI Director intensity (>0.8) |
| `Code/SpecialInfected/HunterSpawner.cs` | Spawns Hunters at medium intensity |

### Items & Inventory
| File | Purpose |
|------|---------|
| `Code/Items/BaseItem.cs` | Abstract pickup: trigger-based detection, auto-use vs carry modes, SlotType enum |
| `Code/Items/HealthKit.cs` | Heals 80% of max HP; if player is downed, revives them instead |
| `Code/Items/PainPills.cs` | Temporary health boost (50 temp HP, decays at 5/sec) |
| `Code/Items/AmmoPile.cs` | Restores reserve ammo on E press. Does not destroy — all players can use it. |
| `Code/Items/WeaponPickup.cs` | World weapon pickup — press E to equip, replaces current slot weapon |

### World & Environment
| File | Purpose |
|------|---------|
| `Code/SafeRoom.cs` | L4D-style safe zones — proximity-based detection (SafeRadius), stops spawning, triggers win condition on end room |
| `Code/AmbientSoundLoop.cs` | Plays a looping 2D ambient sound. Assign SoundEvent with Loop enabled in inspector. |
| `Code/DayNightCycle.cs` | Gradually transitions DirectionalLight + SkyBox2D from day to night over CycleDuration seconds. Snaps to full night when player enters end safe room. |

### Level Design
- Maps built in **Hammer Next** (S&box built-in), saved as `.vmap`, compiled and referenced via `MapInstance` component
- Current level: `Assets/scenes/firsttest.scene` — beach port level with start safe room, boardwalk, warehouse with pillars, port yard, end safe room
- Enemy spawning tuned for enclosed Hammer maps — spawn ray starts near player Z level

### UI
| File | Purpose |
|------|---------|
| `Code/ui/HUD.razor` | Main HUD: 5-slot inventory column, health/stamina bars, incap bleed-out bar, ammo counter, directional damage indicators, pickup hint, death overlay, win screen |
| `Code/ui/HUD.razor.scss` | All HUD styling |

---

## Inventory System (5-Slot Fixed)

L4D-style fixed slots, always visible in HUD:

| Slot | Category | Key | Holds |
|------|----------|-----|-------|
| 0 | Primary | 1 | Shotgun, SMG, Rifle |
| 1 | Secondary | 2 | Pistol, Melee |
| 2 | Main Heal | 3 | Health Kit |
| 3 | Sub Heal | 4 | Pain Pills |
| 4 | Utility | 5 | Grenades (future) |

Empty slots show as dimmed placeholders. Active slot shows `[LMB]` or `[E]` hint.

---

## Incapacitation System (Important — has had bugs)

When a player hits 0 HP:
1. `HealthComponent.Die()` searches for `IncapacitationComponent` on self/descendants/parent
2. If found and `CanBeIncapacitated()` is true → calls `incap.Incapacitate()`, sets HP to 1f, returns
3. `IncapacitationComponent` sets `IsPinned = true` (disables movement), starts 60s bleed-out timer
4. Enemies hitting a downed player call `incap.ApplyBleedDamage()` instead of HP damage
5. Health kit use calls `incap.Revive()` → restores 45% HP, re-enables movement
6. If bleed-out timer reaches 0 → `ActuallyDie()` → real death

**Known fix:** `PlayerStats` static fields persist between editor play sessions. `IncapacitationComponent.OnAwake()` resets `IsIncapacitated`, `BleedOutHealth`, and `IsDead` to defaults.

---

## Directional Damage Indicators

Red triangles appear around the crosshair pointing toward where damage came from:
- `CameraMovement.OnDamageTakenWithAttacker()` calculates angle using dot products against player's facing direction
- `PlayerStats.AddDamageIndicator(angle)` adds to list
- `HUD.razor` renders them in a ring and calls `PlayerStats.TickDamageIndicators()` to fade over ~0.8s

---

## Known Gotchas / Previously Fixed Bugs

- **Enemies float in air at spawn** — was `Vector3.Up * 80f` height; fixed to `5f`. Rejection threshold was too high, rejecting ground-level spawns.
- **Enemies fly after spawn** — `Rigidbody` was fighting `CharacterController`; now disabled at spawn, re-enabled only on death for ragdoll.
- **Bullets don't hit enemies** — `CharacterController` has no raycast collision. `EnemySpawner` adds a 40×40×100 `BoxCollider` to every spawned enemy.
- **Bullets hit child mesh but HealthComponent on parent** — fixed with `GetInAncestorsOrSelf` fallback search in gun code.
- **Enemy stuck in run pose on death** — `CitizenAnimationHelper` kept last velocity; fixed by zeroing velocity/wishvelocity in `OnEnemyDeath()`.
- **Player dies instantly instead of going down** — `HealthComponent.Die()` was using `GameObject.Root` (scene root) to find `IncapacitationComponent`; now uses `GetInDescendantsOrSelf ?? Parent?.Components...`
- **Player goes dead in half a second while downed** — enemies still dealt HP damage while downed; `TakeDamage` now clamps to 0.1f when `PlayerStats.IsIncapacitated` is true.
- **Footsteps not playing** — `characterController.IsOnGround` may return false if ground plane has no proper physics collision; replaced with a short downward scene trace.
- **Enemy spawns on roof in Hammer maps** — fixed by starting spawn ray at player Z+20 instead of fixed 500 unit offset. Traces down 500 units with trigger tag filter.
- **Gun sounds quiet/distant** — fixed by removing WorldPosition from `Sound.Play()` calls on player weapons. Weapon sounds are now 2D.
- **AmmoPile not refilling** — fixed by using direct `PrimarySlot/SecondarySlot` references in `RestoreAmmoForAllGuns()` instead of scene-wide search.

---

## Planned Features (Phased Roadmap)

| Phase | Feature | Status |
|-------|---------|--------|
| 1 | Special Infected — Spitter, Charger, Crawler, Armored Zombie | Planned |
| 2 | Throwables — Molotov (utility slot), Pipe Bomb (utility slot) | Planned |
| 3 | Dynamic Events — car alarms, environmental Director ramps | Planned |
| 4 | Stats Screen — kills, damage taken, times downed, accuracy | Planned |
| 5 | Difficulty Settings — Casual/Normal/Expert presets | Planned |
| 6 | Weather System — rain, fog, ties into day/night | Planned |
| 7 | Rescue Closets — locked NPC survivors, reward on open | Planned |
| 8 | Between-Level Safe House — ship hub, port map UI | Planned (needs multiple levels) |
| 9 | Character Selection — cosmetic only, 4 characters | Planned |
| 10 | Versus Mode — player-controlled special infected | Far future (needs multiplayer) |

---

## Asset Conventions

- Sounds: assign `SoundEvent` properties in the editor inspector by dragging from the Asset Browser
- Models: `[Property] public Model WeaponModel` for drag-drop in editor
- Prefabs: enemies use `Enemy_Prefab` in the scene hierarchy
- Tags in use: `"player"`, `"trigger"`, `"headzone"` — used in trace exclusions
- Maps: built in Hammer Next, saved to `Assets/maps/`, referenced via `MapInstance` component in scene
