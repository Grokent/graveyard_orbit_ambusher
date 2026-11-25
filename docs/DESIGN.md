# Ambusher — design and implementation notes

Status: prototype step 1 (vertical slice). Built against the installed
Graveyard Orbit build (`Assembly-CSharp`, 2026-11-19) with BepInEx 5.4.23.6.

## Agreed behaviour

| Area | Spec |
|---|---|
| Carrier | Stationary `behavior` turret; dies like any enemy; spawns on the swarmer cadence (`interval 1.1 s`, `max_live 7`); drones persist after the carrier dies |
| Drone | 2 HP, 240 px hunt speed (configurable; ship max is 200 px), flies over terrain, 5-minute lifetime, no loot, 1 damage on ram; placeholder body is the SwarmUnit (kamikaze) visual |
| Orbit | Cloaked; holds a 320 px ring behind the ship, bearing re-picked every 1.5 s; forced fire attempt after 10 s of unsuccessful positioning |
| Bearing | 160–200° off ship forward, measured from `ShipController.transform.forward` |
| Attack | No firing tell; uncloak → one lead-corrected lob; visible windup (0.85 s); full 0.35 s lunge remains visible; cloak at lunge end; 5 s evade; repeat |
| Cloak | No auto-aim target, no radar; body hidden, distortion effect is the only tell |
| Cloak break | Any damage makes it visible and targetable for 0.75 s, then it recloaks |
| Damage | Pulse wave/nova immune; all other damage normal |
| Completion | Live drones should block map completion |
| Visuals | Swarmer drone graphics as placeholder; cloak distortion later |
| Audio | Placeholder hook only |

All speeds use the game's `px` unit (`hexSize / 32`), matching
`EnemyManager.SwarmUnit` conventions.

## State machine

```
CloakedHunt ──(at ring, or 10 s)──> Fire (uncloak + lob) ──> Windup (0.85 s visible)
     ^                                                        |
     |                                                        v
   Evade <──(5 s)── Lunge (cloak at start, 0.35 s) <──(0.32 s)
```

- `Hunt`: move toward `ship + behind(bearing) * 320 px`; bearing is recomputed
  each frame from the current ship forward, with a new random angle every 1.5 s.
  It keeps positioning for `HuntTimeoutSeconds` (default 10, configurable) before
  firing anyway; previously the 4 s timeout made it fire while still far out.
  Hunting speed is `HuntSpeedPx` (default **240**): the player's ship max is
  200 px, so the original 200 px could never close a straight chase.
- `Fire`: enable the body, add the drone to `EnemyManager._swarm` (so auto-aim
  and `DamageTarget` work), spawn the lob.
- `Windup`: scale the placeholder visual 1 → 1.45, then choose an away vector
  rotated ±50° and enter the lunge.
- `Lunge`: cloak immediately, move 560 px/s for 0.35 s.
- `Evade`: keep retreating at 160 px/s for 5 s, then re-enter `Hunt`.
- Lifetime: 300 s; ram radius 1.2 hex triggers `ShipController.OnEnemyHit(1, …)`
  and destroys the drone like a swarm unit.

## Implementation notes (step 1)

- **Placeholder body / targeting:** each drone gets a disabled `SwarmUnit`
  component. `SwarmUnit.Init` builds the swarmer's visual and sets 2 HP; the
  component is then disabled so its melee AI does not run.
- **Auto-aim:** `EnemyManager._swarm` (private `List<SwarmUnit>`) is reflected
  once. The drone is added while uncloaked and removed while cloaked, so
  `NearestSwarm`, `NearestTarget`, `DamageTarget(kind=2)` and projectiles only
  see it when visible.
- **Damage notification:** a Harmony postfix on `SwarmUnit.TakeDamage` calls
  `AmbusherDrone.OnDamaged`, which extends the visible window by 0.75 s.
- **Lob:** self-contained `AmbusherLob`; speed 140 px, range 1600 px, damage 1,
  lead computed from `ShipController._smoothVel` (reflected). Hits call
  `ShipController.OnEnemyHit`.
- **Director:** created from a `MiningSortie.Start` postfix, parented to the
  sortie so it dies with the scene. Holds the drone/carrier lists.
- **Debug spawns:** F10 (disabled by default; `HotkeyEnabled`) spawns a carrier 24 hexes ahead (`CarrierDistanceHex`);
  config can spawn a carrier or N drones at sortie start. The carrier is a fixed
  sphere with a 1.1 s timer and 7-live cap.
- **Hull avoidance:** while repositioning in `Hunt`, if the drone is within 3
  hexes of the ship it steers radially away instead of continuing through the
  hull. This prevents accidental ram damage from the straight-line path to the
  ring.
- **First live test (2026-11-24):** F9 spawned a carrier 6 hexes ahead; drones
  spawned around it and crossed the hull on the way to the ring, causing ram
  damage and two ship deaths. Carrier distance is now 24 hexes and Hunt skirts
  the ship. State transitions, rams, lob hits and cloak breaks now log under
  `[ambusher]`.
- **Second live test (2026-11-24):** 16 drones spawned, 19 fire cycles,
  **9 lob hits, 0 rams**, ship eventually died. Targeting/destruction and the
  cloak cycle work; the overwhelm came from volume of fire, not collisions.
  Balance knobs added: `CarrierMaxLive`, `LobDamage`, `LobSpeedPx`.

## Implemented in this build

- **Carrier tile:** `CarrierIntegration` injects a `behavior` TileDef
  (`id=ambusher`, `alt_id=16`, 7 HP, `behavior_kind=ambusher`) after
  `GameData.HydrateFromAsset`/`Hydrate`. Its `attack.type` is
  `ambusher_spawn`; `EnemyManager.DoAttack` is replaced for this kind and calls
  the director instead of firing a normal shot.
- **Authored placement:** alt 16 is registered in `SandboxMap.AltToTile`; a
  postfix on `MiningSortie.AuthoredEnemyPlacements` appends those cells.
- **Seeded carriers:** postfix on `EnemyManager.SeedPerLevel` reads explicit
  entries from `ambusher_contracts.json`, then a `default`/`*` wildcard, then
  auto-density (`CellsPerCarrier`, `MinCarriers`, `MaxCarriers`) for any other
  contract. It picks eligible layer cells with `alt == 0` and calls
  `SeedAtCellsExplicit` with kind `ambusher`. Respects `CustomRun.noEnemies`.
- **Physical hits while cloaked:** the director reads `ProjectileSystem._pool`
  each frame and lets any live player projectile within its hit radius damage a
  cloaked drone (0.2 s per-drone guard). Damage breaks the cloak via
  `SwarmUnit.TakeDamage`.
- **Carrier:** uses the vanilla `EnemyManager` reveal path unchanged, so it
  gets the same plasma-ball visual and ring as the swarm/lightning/void
  turrets. `TickHealthBars` is postfixed so carriers get a health bar even at
  full HP. `CarriersVisibleAtStart` (default true) reveals carriers at sortie
  start; F10 seeds+reveals a real carrier at the ship rather than a debug orb.
- **Lobs:** `AmbusherLob` now calls the private `EnemyManager.SpawnShot` with
  the game's `_fxLava` prefab, so it is the exact single flaming turret
  projectile. It lands in `_shots`, which makes it targetable and shootable
  without custom patches.
- **Cloak visual:** `CloakEffect = trail` (default) gives each cloaked,
  moving drone a faint 0.35 s wake trail (no body, no orb). `ripple` is the
  full-screen SpaceTimeRipple variant — spectacular but global; see
  `docs/SPACETIME_RIPPLE.md`. `mirage`/`glass` are the PRISM-material
  alternatives and `CloakOpacity` controls their tint.
- **Carrier HP:** `BuildConfigs` postfix copies the highest HP among the four
  vanilla behaviour turrets onto `ambusher` (log line reports both values);
  `CarrierHp` can override it. `glass` is the brighter
  original glass-ball look, `shimmer` the fallback bubble, `off` invisible.
  The shader is cached from the MainMenu scene and kept referenced into the
  mining scene.
- **Completion gate:** prefix on `MiningSortie.StructureCleared` blocks the win
  while `LiveDrones > 0`; the director replays `StructureCleared` when the last
  drone dies.
- **Pulse immunity:** drones are not in `EnemyManager._active`; pulse
  wave/nova paths ignore them. Re-check if they are ever registered there.

## Parked issue: ambusher body visual

The ambusher drone body is built from a disabled `SwarmUnit`, the same class the
base-game swarm turret uses for its spawned kamikaze units. In the installed
decompiled build, `SwarmUnit.Init` builds that body from
`WeaponFX/SphereRed`, a particle-only prefab shared with the turrets; there is
no grey-ship mesh in that path. The user reports the base-game swarm units look
like little grey ships, which does not match this code path. All mods' sources
were searched and none patch `SwarmUnit`/`SphereRed` visuals. Parked until we can
capture a vanilla-side screenshot or inspect a live swarm unit's visual tree.
`DroneVisualScale` was removed; the body is the stock SwarmUnit size.

## Known gaps / next milestones

1. **Cloak look validation.** `mirage` is a tuned PRISM glass material: low
   tint, no fresnel, animated wave normals. If it still reads as a bubble, the
   next step is a camera-facing quad variant or a purpose-built shader asset.
2. **Beam/hitscan physical hits.** Physical cloaked hits currently cover
   `ProjectileSystem` projectiles only; beams and instant arcs still ignore
   cloaked drones.
3. **Audio.** Placeholder hook; no sounds wired yet.
4. **Carrier visual:** uses the vanilla enemy orb; a bespoke stationary model is
   pending.

## Tuning constants (source)

`AmbusherDrone.cs`: hunt speed 240 px (config), orbit 320 px, lunge 560 px/0.35 s,
evade 5 s, hunt max 10 s (config), retarget 1.5 s, cloak break 0.75 s,
lifetime 300 s.
`AmbusherLob.cs`: 140 px/s, 1600 px range, 1 damage.
`AmbusherCarrier.cs`: 1.1 s interval, 7 live.

## Build

`./build.sh` uses a sibling .NET SDK (`../Graveyard Orbit MP/tools/dotnet`) and
the game's BepInEx/Managed assemblies. `./deploy.sh` copies the DLL into the
game's plugin folder.
