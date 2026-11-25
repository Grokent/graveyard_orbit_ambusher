# Graveyard Orbit Ambusher

A standalone BepInEx plugin that adds a new enemy to **Graveyard Orbit**: a
stationary carrier turret that spawns fast, cloaked drones. The drones circle
behind the player, uncloak, fire a lead-corrected flaming lob, telegraph a
lunge, and disappear into a faint wake before re-engaging.

It does not require the co-op mod or Custom Maps. It works alongside them.

## ⚠️ Disclaimer

This project is **100% vibe coded** and **100% the product of AI use**. It is an
unofficial fan mod. I make **no claims of ownership** over Graveyard Orbit, its
code, assets, names, or anything derived from it. **All credit and all rights
belong to Anchorhead Games.** If you own the game, you are welcome to use this
mod for private play. No game code or game asset files are redistributed here.

## Status

Early release. The carrier, drones, cloak trail, native flaming projectile,
health bars, targetability and win gating are implemented and playtested. The
cloak is a faint movement wake rather than true screen refraction; a dedicated
local distortion shader remains future work.

## Install (Windows and Linux are the same)

You only need the release zip — no compiler, no setup, no separate Linux build.

1. Close Graveyard Orbit.
2. In Steam, right-click **Graveyard Orbit** → **Manage** → **Browse local files**.
   That opens the game folder (the one containing `GraveyardOrbit.exe`).
3. Download the latest release zip:
   **[Releases](https://github.com/Grokent/graveyard_orbit_ambusher/releases/latest)**.
4. Extract **everything** from the zip into the game folder that opened in step 2.
   It only adds files (`winhttp.dll`, `doorstop_config.ini`,
   `.doorstop_version`, `changelog.txt`, `BepInEx/`); nothing in the original
   game is replaced.
5. **Linux / Proton only:** add this launch option once (game → Properties →
   Launch Options). Windows needs no launch options:

   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```

6. Launch the game through Steam normally.

### Uninstall

Delete `BepInEx/plugins/GraveyardOrbit.Ambusher/`. Remove `winhttp.dll`,
`doorstop_config.ini`, `.doorstop_version`, `changelog.txt` and `BepInEx/` only
if you do not use any other BepInEx mod.

## What it adds

- **Carrier turret.** A stationary `ambusher` enemy using the game's normal
  behaviour-tile path. It has a health bar, can be targeted and destroyed, and
  spawns drones on the vanilla swarm cadence (1.1 s, up to 7 live).
- **Ambusher drones.** 2 HP, fast (240 px), and aggressive. They hold a 320 px
  orbit behind the ship, staying cloaked while repositioning. After a visible
  0.85 s windup they fire one native flaming lob, then stay visible through a
  0.35 s lunge before cloaking into a faint wake and evading for 5 s.
- **Native projectiles.** Lobs are the game's own `_fxLava` `EnemyShot`, so they
  look and behave like the existing fire turret's shots and can be shot down by
  player weapons and point defence.
- **Win gating.** Live ambusher drones hold the contract win until they are all
  gone.
- **Auto seeding.** Carriers are seeded into every contract by map size, so the
  enemy can appear in base-game contracts, custom maps and downloaded maps
  without any setup.

## Where it appears

```ini
[Balance]
AutoSeedUnlisted = true
CellsPerCarrier = 10000
MinCarriers = 1
MaxCarriers = 12
```

Approximate automatic carrier counts:

| Map cells | Carriers |
|---:|---:|
| 10,000 | 1 |
| 25,000 | 3 |
| 50,000 | 5 |
| 100,000 | 10 |
| 250,000 | 12 (cap) |

Carriers are seeded on layer 1 and revealed at sortie start. Their death does
not remove drones already spawned.

`ambusher_contracts.json` in the plugin folder is an optional override table:

```json
{
  "my_custom_map": { "1": 4, "3": 1 },
  "default": { "1": 2 }
}
```

- A listed contract id uses exactly those layer/count pairs.
- `default` or `*` overrides auto-density for every unlisted contract.
- Map authors can author carriers directly with `alt = 16`.

## Settings

Config file: `BepInEx/config/graveyardorbit.ambusher.cfg`

| Setting | Default | Meaning |
|---|---:|---|
| `AutoSeedUnlisted` | true | Seed contracts not listed in the override table |
| `CellsPerCarrier` | 10000 | Auto-density (one carrier per N cells) |
| `MinCarriers` / `MaxCarriers` | 1 / 12 | Auto-density clamp |
| `CarrierMaxLive` | 7 | Maximum live ambusher drones |
| `LobDamage` / `LobSpeedPx` | 1 / 140 | Projectile tuning |
| `HuntTimeoutSeconds` | 10 | Time spent trying to get behind before firing |
| `HuntSpeedPx` | 240 | Repositioning speed (ship max is 200) |
| `WindupSeconds` | 0.85 | Visible telegraph before the lunge |
| `CarrierHp` | 0 | 0 = inherit the highest vanilla turret HP |
| `CloakEffect` | `trail` | `trail`, `ripple`, `mirage`, `glass`, `shimmer`, `off` |
| `TrailAlpha` / `TrailTime` / `TrailWidthHex` | 0.14 / 0.35 / 0.18 | Wake trail look |
| `CarriersVisibleAtStart` | true | Reveal seeded carriers at sortie start |
| `Hotkey` | F10 | Spawn a debug carrier ahead of the ship |

`ripple` is the game's full-screen `SpaceTimeRipple` effect — spectacular, but
global. The localized `trail` is the default. Details are in
`docs/SPACETIME_RIPPLE.md`.

## Logs

`BepInEx/LogOutput.log`, prefix `[ambusher]`. Useful lines:

```
[ambusher] injected behaviour tile 'ambusher' (alt 16).
[ambusher] carrier HP set to X (vanilla max Y).
[ambusher] auto-density for '...': N carrier(s) ...
[ambusher] drone spawned ...
```

## Build from source (developers)

Requires a legitimate copy of the game, plus `curl`, `unzip` and Python 3.

```bash
./setup.sh      # project-local .NET SDK + BepInEx compile references
./build.sh      # builds the plugin
./deploy.sh     # copies the plugin + default override file into the game
./package.sh    # rebuilds release/GraveyardOrbit-Ambusher-<version>.zip
```

Set `GAME_DIR` if the game is not in the default Steam library. Everything is
installed project-locally under `tools/`; nothing is installed system-wide.

## Docs

- `docs/DESIGN.md` — full behaviour, state machine, implementation notes.
- `docs/SPACETIME_RIPPLE.md` — reference recipe for the full-screen ripple effect.
- `INSTALL.txt`, `THIRD_PARTY_NOTICES.md`, `LICENSE`.

Unofficial fan mod. Graveyard Orbit and all related content belong to Anchorhead
Games.
