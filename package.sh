#!/usr/bin/env bash
# Build the redistributable install zip. Windows and Linux/Proton use the same
# payload: extract the zip into the Graveyard Orbit game folder.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSION="$(sed -n 's/.*PluginVersion[[:space:]]*=[[:space:]]*"\([0-9][0-9.]*\)".*/\1/p' \
  "$ROOT/src/GraveyardOrbit.Ambusher/Plugin.cs" | head -n1)"
[[ -n "$VERSION" ]] || { echo "Could not read PluginVersion from Plugin.cs" >&2; exit 1; }
OUT="$ROOT/release"
STAGE="$OUT/stage"
ZIP="$OUT/GraveyardOrbit-Ambusher-$VERSION.zip"
DLL="$ROOT/src/GraveyardOrbit.Ambusher/bin/Release/GraveyardOrbit.Ambusher.dll"
BEP_ZIP="$ROOT/tools/BepInEx_win_x64_5.4.23.5.zip"

[[ -f "$DLL" ]] || { echo "Build first: ./build.sh" >&2; exit 1; }
[[ -f "$BEP_ZIP" ]] || { echo "Missing $BEP_ZIP (run ./setup.sh)." >&2; exit 1; }

rm -rf "$OUT"
mkdir -p "$STAGE/BepInEx/plugins/GraveyardOrbit.Ambusher"
unzip -oq "$BEP_ZIP" -d "$STAGE"
cp -f "$DLL" "$STAGE/BepInEx/plugins/GraveyardOrbit.Ambusher/"
cp -f "$ROOT/ambusher_contracts.json" "$STAGE/BepInEx/plugins/GraveyardOrbit.Ambusher/"
cp -f "$ROOT/INSTALL.txt" "$STAGE/INSTALL.txt"
cp -f "$ROOT/README.md" "$STAGE/README.md"
cp -f "$ROOT/LICENSE" "$STAGE/LICENSE"
cp -f "$ROOT/THIRD_PARTY_NOTICES.md" "$STAGE/THIRD_PARTY_NOTICES.md"
mkdir -p "$STAGE/licenses" "$STAGE/docs"
cp -f "$ROOT/licenses/LGPL-2.1.txt" "$STAGE/licenses/"
cp -f "$ROOT/docs/DESIGN.md" "$STAGE/docs/"
cp -f "$ROOT/docs/SPACETIME_RIPPLE.md" "$STAGE/docs/"

python3 - "$STAGE" "$ZIP" <<'PY'
import os, sys, zipfile
stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for root, dirs, files in os.walk(stage):
        dirs.sort()
        for name in sorted(files):
            path = os.path.join(root, name)
            z.write(path, os.path.relpath(path, stage))
PY

rm -rf "$STAGE"
echo "Created: $ZIP"
ls -la "$ZIP"
