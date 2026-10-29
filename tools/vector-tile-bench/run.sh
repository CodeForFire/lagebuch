#!/usr/bin/env bash
# Builds and runs both benches on the same tiles of a pack's map.pmtiles (lagebuch-geodata#5).
# Usage: tools/vector-tile-bench/run.sh <archive.pmtiles> <out-dir> [tiles-per-zoom]
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PMTILES="$1"
OUT="$2"
PER_ZOOM="${3:-1500}"

dotnet build "$HERE/AvaloniaBench" -c Release -nologo -v q
dotnet build "$HERE/VexTileBench" -c Release -nologo -v q

# The app's renderer first: it picks the tiles and starts results.md.
dotnet "$HERE/AvaloniaBench/bin/Release/net10.0/AvaloniaBench.dll" "$PMTILES" "$OUT" "$PER_ZOOM"
dotnet "$HERE/VexTileBench/bin/Release/net10.0/VexTileBench.dll" "$PMTILES" "$OUT"
