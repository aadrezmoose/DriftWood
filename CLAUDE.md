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

- Before building custom systems, check `BUILDING_WITH_EXISTING_TOOLS.md` and prefer engine features, existing repo systems, and thin helpers first

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
- `Scene.Trace.Ray` does NOT hit trigger colliders (`IsTrigger=true`) — pickups need non-trigger colliders for raycast interaction
- `SoundHandle` struct throws NullReferenceException when uninitialized — always guard with a `bool isPlaying` flag before calling `.IsPlaying` or `.Stop()`
- `DrawGizmos()` override only runs in editor; anything in `OnUpdate()` renders in play mode too — debug visuals belong in `DrawGizmos()`

## Tooling-First Rule

- Use `BUILDING_WITH_EXISTING_TOOLS.md` as the default decision framework for all new work
- Use `MULTIPLAYER_TOOLING_PLAYBOOK.md` for multiplayer-specific decisions
- For every substantial feature or refactor, explicitly note: built-ins considered, repo systems considered, why custom code is needed, and how the change was validated

---

## What's Been Built

### Core Player Systems
| File | Purpose |
|------|---------|
| `Code/PlayerMovement.cs` | WASD movement, sprinting with stamina drain (L4D-style), crouching, jumping, footstep sounds |
| `Code/CameraMovement.cs` | First/third person camera, mouse look, screen shake on damage, camera lowers when downed, directional damage indicators |
| `Code/WeaponManager.cs` | Fixed 5-slot inventory (Primary, Secondary, Main Heal, Sub Heal, Utility). Weapon switching, throwable throw system with `ThrowDelay` pattern, throwable swap on pickup |
| `Code/Gun.cs` | Abstract weapon base: shooting, reloading, reload animation, ammo tracking, muzzle flash point light |
| `Code/HealthComponent.cs` | Health/damage/death with incapacitation intercept — at 0 HP triggers down state instead of instant death |
| `Code/IncapacitationComponent.cs` | L4D-style downed state: 60s bleed-out timer, enemies drain it faster, revive via health kit |
| `Code/PlayerStats.cs` | Static data bus for UI — health, stamina, ammo, incap state, damage indicators, objective text/progress |
| `Code/GameSettings.cs` | Persisted settings (Sensitivity, FOV) saved to JSON via `FileSystem.Data`. Load on startup. |
| `Code/Flashlight.cs` | Player spotlight flashlight, toggle with "Flashlight" input action (bind F). Attach to Camera. |

### Weapons
| File | Details |
|------|---------|
| `Code/Guns/Pistol_New.cs` | Raycast-based hitscan pistol with headshot detection |
| `Code/Guns/SMG.cs` | Automatic SMG |
| `Code/Guns/Shotgun.cs` | Pellet spread shotgun — 8 pellets, 20 damage each, spread 0.04. Uses ancestor fallback for HealthComponent search |
| `Code/Weapons/MeleeWeapon.cs` | Abstract melee base using sphere trace |
| `Code/Weapons/Bat.cs` | Baseball bat |
| `Code/Weapons/Axe.cs` | Fire axe |
| `Assets/weapons/swb/colt/` | Colt pistol (SWB-based) with `v_colt.vmdl` viewmodel + `v_colt.vanmgrph` animgraph |
| `Assets/weapons/swb/remington/` | Remington shotgun (SWB-based) with `v_remington.vmdl` viewmodel |

Headshots detected via `HeadshotZone.cs` (trigger zone on enemy head, 2x damage multiplier).

### Enemy Systems
| File | Purpose |
|------|---------|
| `Code/Enemy.cs` | Standard zombie AI: Patrol → Chase → Attack → Search state machine, attack lunge, flash on hit, stagger, knockback, ragdoll death, ambient growl sounds. AttackDamage = 2f. Speed varies ±15% per spawn. Checks `PipeBombProjectile.ActiveLure` each frame and chases it if set. |
| `Code/EnemySpawner.cs` | Wave-based spawning; adds BoxCollider + HeadshotZone to each spawned enemy. Spawn ray starts at player Z+20, traces down 500 units. |
| `Code/AIDirector.cs` | Dynamic difficulty — scales spawn rate/count based on player stress (L4D-style). Has `SetObjectivePressure(float)` and `ClearObjectivePressure()` for objective events. |
| `Code/StaticEnemyGroup.cs` | Pre-placed enemy groups that activate on trigger. |
| `Code/SpawnNode.cs` | Marks valid spawn positions for EnemySpawner. |

### Special Infected
| File | Purpose |
|------|---------|
| `Code/SpecialInfected/Tank.cs` | Boss: 1500 HP, charge attack, devastating punch |
| `Code/SpecialInfected/TankSpawner.cs` | Spawns Tank at AI Director intensity >0.8 |
| `Code/SpecialInfected/Hunter.cs` | Pouncer: telegraph crouch → leap at player → pin |
| `Code/SpecialInfected/HunterSpawner.cs` | Spawns Hunters at medium intensity |
| `Code/SpecialInfected/Spitter.cs` | Ranged: stays at distance, launches acid projectiles on cooldown (4s default). LOS check. |
| `Code/SpecialInfected/SpitterSpawner.cs` | Spawns Spitters |
| `Code/SpecialInfected/AcidProjectile.cs` | Spitter's projectile — spawns AcidPool on impact |
| `Code/SpecialInfected/AcidPool.cs` | ITriggerListener DoT zone: 5 dmg/sec for 8 seconds. Auto-creates BoxCollider trigger. |
| `Code/SpecialInfected/Charger.cs` | Bulldozer: Idle → Chase → WindUp (1.2s telegraph) → Charging (high-speed 600, locks direction) → Recovering. Pins player on impact for 2s. Stops on wall contact. |
| `Code/SpecialInfected/ChargerSpawner.cs` | Spawns Chargers |
| `Code/SpecialInfected/ArmoredZombie.cs` | Add-on component: 60% body-shot damage reduction; headshots bypass armor entirely. Works by healing back absorbed portion after each non-headshot hit. |
| `Code/SpecialInfected/Siren.cs` | Stationary, hand-placed (no spawner). 3 stages: Idle (playing crying sound) → Agitated (stands, warns when player within 350u) → Enraged (charges at 380 speed, 50 dmg/hit). Shooting her also triggers Enraged. |

### Items & Inventory
| File | Purpose |
|------|---------|
| `Code/Items/BaseItem.cs` | Abstract pickup: trigger-based detection, auto-use vs carry modes, SlotType enum |
| `Code/Items/HealthKit.cs` | Heals 80% of max HP; if player is downed, revives them instead |
| `Code/Items/PainPills.cs` | Temporary health boost (50 temp HP, decays at 5/sec) |
| `Code/Items/AmmoPile.cs` | Restores reserve ammo on E press. Does not destroy — all players can use it. |
| `Code/Items/WeaponPickup.cs` | World weapon pickup — press E to equip, replaces current slot weapon |
| `Code/Items/ThrowableBase.cs` | Abstract base for throwables. Utility slot, carry mode. Has `ViewModelOverlayModel` property for first-person mesh. `OnUse()` spawns projectile from head position. |
| `Code/Items/Molotov.cs` | Molotov throwable — inherits ThrowableBase |
| `Code/Items/MolotovProjectile.cs` | Molotov projectile — creates FireZone on impact |
| `Code/Items/FireZone.cs` | Fire DoT zone — timed, damages any HealthComponent inside |
| `Code/Items/PipeBomb.cs` | Pipe bomb throwable — inherits ThrowableBase |
| `Code/Items/PipeBombProjectile.cs` | Static `ActiveLure` that enemies chase. Explosion with LOS check: strict block for players, enemy-passthrough logic so all zombies in radius get hit even if others block line of sight. |

**Throwable one-slot rule:** Only one throwable at a time (Utility slot). Picking up a new throwable drops the old one with a forward offset.

**ViewModelOverlayModel convention:** The `[Property]` on the prefab holds the model. The `GunViewModel` `OverlayModel` inspector property should be **null** — the model is assigned at runtime via `UpdateOverlayModel()` when a throwable is picked up or swapped.

### Viewmodel System
| File | Purpose |
|------|---------|
| `Code/GunViewModel.cs` | Per-slot viewmodel: SWB animgraph + hands bonemerge. `OverlayModel` hides skeleton mesh and shows custom model on top (used for throwables with Facepunch arms animgraph). `overlayRenderer` field stores direct ref to avoid child-search failures when slot is inactive. Slot visibility: hides when slot is empty (placeholder name). |
| `Code/ViewModelHandler.cs` | Procedural FPS animations on the GunModel child: breathing idle, walk bob, mouse look sway, jump bezier, sprint tuck. Attach to Camera alongside CameraMovement. |
| `Code/ViewModelArms.cs` | Placeholder — drives CitizenAnimationHelper hold type. Currently no-op until BoneMerge hands model is properly set up. |

**Facepunch FPS arms pattern for throwables:** Use a Facepunch v_first_person_arms_human model + punching animgraph as the skeleton driver (WeaponModel). Add `OverlayModel` = custom world mesh (e.g. molotov/pipebomb model). The arms mesh is hidden (`ShadowsOnly`); custom mesh renders on top. Params: `b_charge` (ready state, self-resets), `b_attack` (throw), `b_pin_remove`. `charge_type` = 1 for grenades.

### World & Environment
| File | Purpose |
|------|---------|
| `Code/SafeRoom.cs` | L4D-style safe zones — proximity-based detection (SafeRadius), stops spawning, triggers win condition. Has static events `OnPlayerEntered` and `OnPlayerExited`. |
| `Code/SafeRoomDoor.cs` | Swinging safe room door on hinge pivot. SmartOpen determines inward/outward from player position. AutoCloseOnPlayerLeave closes start room door when player leaves. |
| `Code/AmbientSoundLoop.cs` | Plays a looping 2D ambient sound. Guards `SoundHandle` with `isPlaying` bool to avoid NullReferenceException. Re-checks if sound stopped in OnUpdate and restarts. |
| `Code/DayNightCycle.cs` | Gradually transitions DirectionalLight + SkyBox2D from day to night over CycleDuration. Snaps to full night when player enters end safe room. |
| `Code/WeatherSystem.cs` | Rain cycle: Clear (120s) → RainingLight (60s) → RainingHeavy (45s) → Clear. Starts/stops rain SoundEvent. Snaps to heavy rain when player reaches end safe room. |
| `Code/ZoneTrigger.cs` | ITriggerListener that enables/disables an EnemySpawner when the player walks through. OneShot by default. |
| `Code/LevelManager.cs` | Listens to `SafeRoom.OnPlayerEntered`; sets `PlayerStats.LevelComplete = true` when the assigned EndRoom is reached. |

### Objectives & Events
| File | Purpose |
|------|---------|
| `Code/ObjectiveEvent.cs` | Holdout objective. Stages: Idle → Holding (AI Director pressure ramps, HUD timer) → Moving (lerps BlockingContainer + CraneVisual by offset, shakes camera, plays loop sound) → Complete. Can be started by trigger enter or `CraneEventButton`. Updates `PlayerStats.ObjectiveText/Progress01/Urgent`. |
| `Code/CraneEventButton.cs` | Interactable world button that calls `ObjectiveEvent.TryStartFromButton()`. Supports OneShot, interaction hint, press sound. |

**Crane event setup:** Place `ObjectiveEvent` on a trigger volume (or set `AutoStartOnAwake`). Assign `BlockingContainer` (the container to lift), `CraneVisual` (hook/arm), offsets for where each moves to, and an `AIDirector` reference. Place `CraneEventButton` in the world pointing at the event. Wire up `CameraMovement.Shake()` via `MoveShakeMagnitude`.

### Level Design
- Maps built in **Hammer Next** (S&box built-in), saved as `.vmap`, compiled and referenced via `MapInstance` component
- Current scene: `Assets/scenes/firsttest.scene` — beach port level with start safe room, boardwalk, warehouse with pillars, port yard, end safe room
- Enemy spawning tuned for enclosed Hammer maps — spawn ray starts near player Z level
- Fog: `GradientFog` component in scene (StartDistance=300, EndDistance=3500, FalloffExponent=1.5)

### UI
| File | Purpose |
|------|---------|
| `Code/ui/HUD.razor` | Main HUD: 5-slot inventory column, health/stamina bars, incap bleed-out bar, ammo counter, directional damage indicators, pickup hint, death overlay, win screen, objective text/progress bar |
| `Code/ui/HUD.razor.scss` | All HUD styling |
| `Code/ui/PauseMenu.razor` | Pause menu with sensitivity/FOV sliders backed by `GameSettings` |

---

## Inventory System (5-Slot Fixed)

L4D-style fixed slots, always visible in HUD:

| Slot | Category | Key | Holds |
|------|----------|-----|-------|
| 0 | Primary | 1 | Shotgun, SMG, Rifle |
| 1 | Secondary | 2 | Pistol, Melee |
| 2 | Main Heal | 3 | Health Kit |
| 3 | Sub Heal | 4 | Pain Pills |
| 4 | Utility | 5 | Molotov, Pipe Bomb (one at a time) |

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

- **Enemies float in air at spawn** — was `Vector3.Up * 80f` height; fixed to `5f`. Rejection threshold was too high.
- **Enemies fly after spawn** — `Rigidbody` was fighting `CharacterController`; now disabled at spawn, re-enabled only on death for ragdoll.
- **Bullets don't hit enemies** — `CharacterController` has no raycast collision. `EnemySpawner` adds a 40×40×100 `BoxCollider` to every spawned enemy.
- **Bullets hit child mesh but HealthComponent on parent** — fixed with `GetInAncestorsOrSelf` fallback search in gun code.
- **Enemy stuck in run pose on death** — `CitizenAnimationHelper` kept last velocity; fixed by zeroing velocity/wishvelocity in `OnEnemyDeath()`.
- **Player dies instantly instead of going down** — `HealthComponent.Die()` was using `GameObject.Root` (scene root); now uses `GetInDescendantsOrSelf ?? Parent?.Components...`
- **Player goes dead in half a second while downed** — enemies still dealt HP damage while downed; `TakeDamage` now clamps to 0.1f when `PlayerStats.IsIncapacitated`.
- **Footsteps not playing** — `characterController.IsOnGround` unreliable; replaced with short downward scene trace.
- **Enemy spawns on roof in Hammer maps** — fixed by starting spawn ray at player Z+20 instead of fixed offset.
- **Gun sounds quiet/distant** — fixed by removing WorldPosition from `Sound.Play()` on player weapons. Weapons are now 2D.
- **AmmoPile not refilling** — fixed by using direct slot references instead of scene-wide search.
- **Molotov not pickup-able** — SphereCollider was `IsTrigger=true`; changed to false. `Scene.Trace.Ray` ignores triggers.
- **PipeBomb falling through floor** — no collider on projectile; added SphereCollider (Radius=4, Friction=0.9, Elasticity=0.1).
- **PipeBomb explosion hitting through walls** — added LOS check: strict block for players, enemy-passthrough (if trace hits enemy BoxCollider, don't treat as blocked).
- **SafeRoom debug sphere visible in game** — debug visuals were in `OnUpdate()`; moved to `DrawGizmos()` override.
- **AmbientSoundLoop crashing** — `SoundHandle.IsPlaying` throws on uninitialized struct; fixed with `isPlaying` bool guard.
- **AmbientSoundLoop not repeating** — `isPlaying` never reset when sound ended; fixed with try/catch check in OnUpdate.
- **Throwable overlay model always shows molotov** — `OverlayModel` inspector property was set to molotov in scene; should be **null**. Model is assigned dynamically via `UpdateOverlayModel()` on pickup.
- **UpdateOverlayModel can't find child** — overlayGO was only created in `OnStart()` when inspector `OverlayModel != null`. Fixed: always create overlayGO (hidden initially). Also fixed `var overlayRenderer` local var shadowing the class field — now uses `this.overlayRenderer =`.

---

## Planned Features (Phased Roadmap)

| Phase | Feature | Status |
|-------|---------|--------|
| 1 | Special Infected — Spitter, Charger, Siren, Armored Zombie | **Done** |
| 2 | Throwables — Molotov, Pipe Bomb (utility slot, one at a time) | **Done** |
| 3 | Dynamic Events — crane event (ObjectiveEvent + CraneEventButton) | **Done** |
| 4 | Weather System — Clear/RainingLight/RainingHeavy cycle | **Done** |
| 5 | Procedural viewmodel animation (bob, sway, sprint) | **Done** |
| 6 | Stats Screen — kills, damage taken, times downed, accuracy | Planned |
| 7 | Difficulty Settings — Casual/Normal/Expert presets | Planned |
| 8 | Rescue Closets — locked NPC survivors, reward on open | Planned |
| 9 | Between-Level Safe House — ship hub, port map UI | Planned (needs multiple levels) |
| 10 | Character Selection — cosmetic only, 4 characters | Planned |
| 11 | Versus Mode — player-controlled special infected | Far future (needs multiplayer) |
| 12 | Crawler special infected | Planned |

---

## Asset Conventions

- Sounds: assign `SoundEvent` properties in the editor inspector by dragging from the Asset Browser
- Models: `[Property] public Model WeaponModel` for drag-drop in editor
- Prefabs: enemies use `Enemy_Prefab` in the scene hierarchy; throwable projectiles live under `Assets/prefabs/weapons/`
- Tags in use: `"player"`, `"trigger"`, `"headzone"` — used in trace exclusions
- Maps: built in Hammer Next, saved to `Assets/scenes/`, referenced via `MapInstance` component in scene
- SWB weapons: `Assets/weapons/swb/` — each weapon has `v_*.vmdl` (viewmodel), `w_*.vmdl` (world), `.vanmgrph` (animgraph)
- Attachments (scopes, silencers, rails): `Assets/attachments/swb/`

---

## Potential Level/Island Types
- Resort Island
- Dense jungle
- Fishing Village
- Military Checkpoint
- Industrial Port
- Luxury/Private Island
- Radio Tower

---

## Potential Level Tasks
- Refuel pump
- Patch hull
- Start engine
- Align radio dish

---

## Potential Mission Ideas
- Mission 1: outbreak on cruise ship
- Mission 2: escape wreck and reach beach
- Mission 3: secure temporary camp and locate repairable boat
- Mission 4: get fuel from marina/fishing village
- Mission 5: find engine parts in resort maintenance depot
- Mission 6: retrieve medicine and battery packs from clinic/research station
- Mission 7: radio tower mission to locate safe zone
- Mission 8: island hop through worsening storm/infection spread
- Mission 9: discover "safe zone" is collapsing or abandoned
- Mission 10: final escape to fortified offshore platform / cargo ship / military carrier / hidden survivor enclave
- Escaping a tilting cruise atrium
- Fighting through a flooded lower deck
- Defending a boat refuel station on a dock at sunset
- Crossing a jungle trail at night with only flashlights
- Holding out in a beach resort lobby while shutters fail
- Scavenging a wrecked ferry half-submerged offshore
- Climbing a radio tower during a storm
- Making final repairs while infected swarm the shoreline and leap onto the boat
