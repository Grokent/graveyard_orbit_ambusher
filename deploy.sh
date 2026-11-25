#!/usr/bin/env bash
# Additively install BepInEx (if available locally) and copy the plugin in.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME="${GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Graveyard Orbit}"
BEP_ZIP="$ROOT/tools/BepInEx_win_x64_5.4.23.5.zip"
DLL="$ROOT/src/GraveyardOrbit.Ambusher/bin/Release/GraveyardOrbit.Ambusher.dll"
DEST="$GAME/BepInEx/plugins/GraveyardOrbit.Ambusher"

[[ -d "$GAME" ]] || { echo "Game folder not found: $GAME" >&2; exit 1; }
[[ -f "$DLL" ]] || { echo "Build first: ./build.sh" >&2; exit 1; }

if [[ ! -d "$GAME/BepInEx" && -f "$BEP_ZIP" ]]; then
  echo "Installing BepInEx 5.4.23.5 (additive)"
  unzip -oq "$BEP_ZIP" -d "$GAME"
fi

mkdir -p "$DEST"
cp -f "$DLL" "$DEST/"
if [[ ! -f "$DEST/ambusher_contracts.json" ]]; then
  cp -f "$ROOT/ambusher_contracts.json" "$DEST/"
fi
if [[ -f "$ROOT/examples/ambusher_contracts.example.json" ]]; then
  cp -f "$ROOT/examples/ambusher_contracts.example.json" "$DEST/"
fi

echo "Deployed -> $DEST"
echo "Logs: $GAME/BepInEx/LogOutput.log  (grep ambusher)"
