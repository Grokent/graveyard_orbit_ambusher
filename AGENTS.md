# AGENTS.md — Graveyard Orbit Ambusher

## Purpose

Standalone BepInEx/Harmony enemy mod for **Graveyard Orbit**. Adds an `ambusher`
carrier behaviour tile and cloaked hunting drones. Independent of the co-op and
custom-map mods.

- **Repo:** https://github.com/Grokent/graveyard_orbit_ambusher (branch `main`)
- **License:** MIT (`LICENSE`); BepInEx LGPL-2.1 notices in
  `THIRD_PARTY_NOTICES.md`

## Structure

```
src/GraveyardOrbit.Ambusher/    plugin source
docs/DESIGN.md                  behaviour + implementation notes
docs/SPACETIME_RIPPLE.md        full-screen ripple reference recipe
ambusher_contracts.json         optional per-contract overrides shipped with the plugin
INSTALL.txt / README.md         player-facing docs + AI disclaimer
setup.sh / build.sh / deploy.sh / package.sh
```

## Build / deploy / package

```bash
./setup.sh     # portable .NET 9 SDK + BepInEx compile refs (project-local)
./build.sh
./deploy.sh    # -> <game>/BepInEx/plugins/GraveyardOrbit.Ambusher/
./package.sh   # release/GraveyardOrbit-Ambusher-<version>.zip
```

- `build.sh` prefers `tools/dotnet` and falls back to `dotnet` on `PATH`.
- `GAME_DIR` overrides the default Steam path.
- `release/`, `tools/`, and `bin/obj` are gitignored.
- The release zip bundles BepInEx and the plugin; it does not include game files.

## Behaviour summary

- Carrier is a normal `behavior` tile injected after `GameData` hydrates.
- `EnemyManager.DoAttack` is replaced for the carrier kind and spawns drones.
- Drone state machine: Hunt → Fire → Windup (0.85 s) → Lunge (0.35 s, visible)
  → Evade (5 s) → Hunt. Cloaked while hunting/evading.
- Lobs use `EnemyManager.SpawnShot` with `_fxLava`, so they are native
  `EnemyShot`s and targetable.
- Carriers are seeded by `EnemyManager.SeedPerLevel` postfix using explicit
  overrides, a `default`/`*` wildcard, or map-size auto-density.
- Carriers are revealed at sortie start by default; `CarriersVisibleAtStart`
  controls that.
- Live drones block `MiningSortie.StructureCleared`; the win releases when the
  last drone dies.

## Gotchas

- `BandDef`/`Cfg`/`Enemy` members are private; reflection helpers live in
  `Reflect.cs`. Keep patches defensive and log failures.
- The live `GameData` is an Odin asset; the tile is injected at runtime, not read
  from a committed game file.
- `Mirza/PRISM/Glass` is menu-scene content; the mod caches it while the menu is
  loaded. It makes a glass surface, not a true cloak.
- `SpaceTimeRipple` is a full-screen pass and affects the entire screen,
  including post-processing. Keep it opt-in (`CloakEffect = ripple`).
- `build.sh` must not embed local source paths; keep `DebugType=none`.
- No absolute home paths in committed files.

## Verification checklist

1. Start a contract; log shows carrier HP, auto-density, carriers revealed.
2. Carriers have plasma-ball visuals, rings and health bars; they can be killed.
3. Drones hunt behind the ship, stay visible through the lunge, and leave a
   faint trail while cloaked.
4. Lobs use the single flaming projectile and can be shot down.
5. Win stays locked until all ambusher drones are gone.
6. No Harmony or reflection errors in `BepInEx/LogOutput.log`.

## Working conventions

- Keep patches defensive; never throw into gameplay.
- Bump `PluginVersion` in `Plugin.cs` when shipping.
- Never commit game DLLs, decompiled code, logs, saves, `tools/`, `release/`,
  or `bin/obj`.
- README/INSTALL must carry the AI-use disclaimer and no ownership claims.
- Do not make the repository public without the owner's explicit approval.
