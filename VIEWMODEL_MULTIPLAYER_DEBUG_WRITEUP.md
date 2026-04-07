# Viewmodel Multiplayer Debug Writeup

## Issue Summary
- Multiplayer client can equip and fire weapons, but first-person slot viewmodels intermittently fail to resolve on client.
- Symptom: `NO VIEWMODEL FOUND for slot 0/1` during equip.
- Broadcast and ownership routing are working; failure is in local viewmodel discovery/availability timing.

## Files Touched
- `Code/WeaponManager.cs`
- `Code/GunViewModel.cs`

## What We Tried (Chronological)
1. Hardened local ownership checks in `GunViewModel` (direct owner SteamId comparison + local identity fallback).
2. Fixed compile break from escaped quotes in interpolated logs.
3. Added multi-tier GunViewModel lookup logic in `WeaponManager`:
   - direct refs
   - descendants
   - local identity hierarchy
   - ownership scene search
   - camera-controller search
   - active-camera search
4. Confirmed host correctly hides remote player viewmodels (proxy behavior expected).
5. Confirmed client local pawn is owned and non-proxy, but lookup still returned empty.
6. Added inspector `[Property]` slot refs on `WeaponManager`; wiring did not fix runtime behavior.
7. Switched to self-registration pattern:
   - `GunViewModel` registers/unregisters with `WeaponManager`
8. Added per-frame retry registration in `GunViewModel` for startup ordering races.
9. Added registration fallbacks via `PlayerIdentity.Weapons` and `PlayerIdentity.Local.Weapons`.
10. Reduced noisy warning spam while registration is still warming up.
11. Added runtime fallback in `WeaponManager` to create missing local slot VMs under Camera/Head and register immediately.
12. Added slot `0/1` VM prewarm early in local `OnUpdate` before bind/equip logic.

## Observed Logs and Interpretation
- Equip logs show `isLocallyControlled=True` while still reporting missing VM.
- That confirms local control path is correct but VM object is unavailable at lookup time.
- Additional logs are unrelated to VM binding root cause:
  - missing weapon icon png files
  - unknown image type `rgba(...)`
  - missing `SWB.Base.Particles.ParticleCount`
- `Hit World Physics - no HealthComponent found` is expected when hitting world geometry.

## Current State
- Code now has three safety layers:
  1. self-registration
  2. retry registration
  3. runtime VM auto-spawn fallback
- Expected behavior with latest code:
  - first failure should trigger runtime VM spawn log
  - subsequent equips should resolve slot VM and stop repeated missing-VM warnings

## Most Likely Remaining Gaps
- Client may still be running stale build output.
- Runtime player hierarchy in active scene/session may differ from expected Camera/Head chain.
- Rare start-order edge: gun bind may occur before newly spawned VM renderer initialization (partially mitigated by direct property assignment + later update).

## Recommended Next Debug Pass
- Full restart and capture a fresh client log.
- Specifically look for these lines:
  - `[WeaponManager] Registered VM slot=...`
  - `[GunVM] Registered slot=...`
  - `[WeaponManager] Spawned runtime VM for slot=...`
- If `NO VIEWMODEL FOUND` still appears without those logs, add a compact bind-trace log in `WeaponManager` containing:
  - player GO
  - head/camera found flags
  - registered VM count
  - requested slot at equip time

## Bottom Line
Ownership and equip routing are functioning. The unresolved problem is reliable local availability/binding of slot viewmodels at client runtime initialization.