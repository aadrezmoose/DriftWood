# Multiplayer Tooling Playbook (s&box)

Use this before adding or rewriting multiplayer systems.

## 1) Built-in tools we already have

### Networking/session primitives
- `Networking.IsActive`
- `Connection.Local`, `Connection.All`
- `Connection.Local.IsHost`

### Ownership/replication primitives
- `GameObject.Network`
- `gameObject.Network.Owner`
- `gameObject.Network.AssignOwnership(conn)`
- `gameObject.Network.Active`
- `gameObject.NetworkSpawn(conn)`
- `[Sync]` properties (owner -> proxies)
- `Component.IsProxy`

### Scene/runtime helpers
- `Scene.GetAllComponents<T>()`
- `Components.GetInDescendantsOrSelf<T>()`
- `Components.GetInAncestorsOrSelf<T>()`
- Runtime component creation: `gameObject.Components.Create<T>()`

## 2) Decision order (always follow)

1. Can built-ins solve this directly (`[Sync]`, `NetworkSpawn`, `Owner`)?
2. Can we solve this with a thin wrapper/helper over built-ins?
3. Only then write new custom architecture.

If step 1 or 2 works, do not build a larger framework first.

## 3) What to use built-ins for

- **Spawning players**: host only, `Clone` + `NetworkSpawn(conn)`
- **Authority checks**: prefer `Network.Owner == Connection.Local` first, fallback to `!IsProxy`
- **Gameplay state sync**: `[Sync]` primitive fields only
- **Local-only presentation**: camera/viewmodel/HUD should never drive authority logic

## 4) Known limits (so we don’t overexpect)

- No magical built-in `NetworkHelper` class.
- `[Sync]` does not directly sync `List<T>`/complex collections reliably for all cases.
- No high-level out-of-the-box spawn/session/camera framework for co-op FPS.
- Scene-wired references can fail after clone/spawn and must be re-resolved in `OnStart()`.

## 5) DriftWood implementation standard

Before merging any MP feature, verify:

- [ ] Ownership check uses one shared helper (not ad-hoc `IsProxy` checks)
- [ ] Host-only spawn/despawn is centralized
- [ ] Camera logic is client-local and isolated from spawn authority
- [ ] `[Sync]` writes happen on owner/authority side only
- [ ] Feature works in host + join-client smoke test

## 6) Do-this-first for every new MP task

Add this mini note to your task/PR description:

- Built-ins considered:
- Which built-ins were used:
- Why custom code is needed (if any):
- Risk to camera/ownership/spawn:
- Test performed (host + client):

This keeps scope tight and prevents accidental reinvention.
