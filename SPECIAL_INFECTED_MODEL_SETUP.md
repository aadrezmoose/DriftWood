# Special Infected Model Setup (DriftWood)

Use this as a fast path to get **Tank / Hunter / Spitter / Charger / Siren** visually testable.

## 1) Folder layout (already scaffolded)

Drop source files (`.fbx`, `.glb`, textures) into:

- `Assets/models/special_infected/tank/`
- `Assets/models/special_infected/hunter/`
- `Assets/models/special_infected/spitter/`
- `Assets/models/special_infected/charger/`
- `Assets/models/special_infected/siren/`

Keep one model pack per folder to avoid importer confusion.

## 2) Asset sourcing rules (safe/legal)

Only use assets with clear licenses:

- Preferred: **CC0**
- Acceptable: **CC-BY** (must track attribution)
- Avoid anything with unclear ownership or ripped game assets

Recommended sources:

- Poly Pizza
- Kenney
- Sketchfab (license-filtered)
- OpenGameArt (license-filtered)

## 3) Import pipeline in s&box

For each special infected folder:

1. Drag model + textures into the folder.
2. Let the importer generate `.vmdl`.
3. Open the generated model and verify:
   - scale is correct vs player
   - material assignments are valid
   - silhouette reads clearly at mid distance
4. If skinned: ensure skeleton imports cleanly.

## 4) Quick visual targets (MVP)

Until final art exists, use silhouette+tint as identity:

- Tank: largest silhouette, heavy upper body, red/dirty tint
- Hunter: lean silhouette, aggressive posture, green-ish tint
- Spitter: thin + hunched, sickly/yellow-green tint
- Charger: asymmetrical/heavy shoulder look, blue/gray tint
- Siren: pale/washed look, readable idle silhouette

## 5) Wiring to current test setup

You already have a dev bootstrap component:

- `Code/SpecialInfected/SpecialInfectedTestSetup.cs`

In scene inspector:

1. Add `SpecialInfectedTestSetup` to a scene object.
2. Set `FallbackEnemyPrefab` (for immediate behavior testing).
3. As dedicated prefabs/models are created, set:
   - `TankPrefabOverride`
   - `HunterPrefabOverride`
   - `SpitterPrefabOverride`
   - `ChargerPrefabOverride`
4. Use one-shot toggles to test:
   - `SpawnTankNow`
   - `SpawnHunterNow`
   - `SpawnSpitterNow`
   - `SpawnChargerNow`

## 6) Per-special done checklist

For each special infected:

- [ ] Model imported and `.vmdl` created
- [ ] Correct scale vs player
- [ ] Materials/textures valid in-game
- [ ] Distinct silhouette from commons
- [ ] Spawns via test setup toggle
- [ ] Takes damage + dies + ragdolls correctly on host
- [ ] Takes damage + dies + ragdolls correctly on client

## 7) Notes for this repo

- `Tank` and `Hunter` now support `SkinnedModelRenderer` fallback in code.
- Spawner tint assignment also supports skinned fallback where needed.
- You can test behavior now with fallback prefab, then swap art later without changing gameplay flow.
