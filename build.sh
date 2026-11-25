#!/usr/bin/env bash
# Build the ambusher plugin with a local .NET SDK.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOTNET="${DOTNET:-$ROOT/tools/dotnet}"
if [[ ! -x "$DOTNET/dotnet" ]]; then
  DOTNET="$(command -v dotnet || true)"
  [[ -n "$DOTNET" ]] && DOTNET="$(dirname "$DOTNET")"
fi
[[ -n "${DOTNET:-}" && -x "$DOTNET/dotnet" ]] || {
  echo "No .NET SDK found. Run ./setup.sh or set DOTNET=/path/to/dotnet." >&2
  exit 1
}

export DOTNET_ROOT="$DOTNET"
export PATH="$DOTNET:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

dotnet build "$ROOT/src/GraveyardOrbit.Ambusher/GraveyardOrbit.Ambusher.csproj" -c Release "$@"
