#!/usr/bin/env bash
# POC for CodeForFire/lagebuch-geodata#5. Builds and runs both benches on the same tiles.
# Usage: tools/vector-tile-bench/run.sh <map.mbtiles> <archive.pmtiles> <out-dir> [tiles-per-zoom]
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MBTILES="$1"
PMTILES="$2"
OUT="$3"
PER_ZOOM="${4:-1500}"

dotnet build "$HERE/AvaloniaBench" -c Release -nologo -v q
dotnet build "$HERE/VexTileBench" -c Release -nologo -v q

# The app's renderer first: it picks the tiles and starts results.md.
dotnet "$HERE/AvaloniaBench/bin/Release/net10.0/AvaloniaBench.dll" "$MBTILES" "$PMTILES" "$OUT" "$PER_ZOOM"
dotnet "$HERE/VexTileBench/bin/Release/net10.0/VexTileBench.dll" "$PMTILES" "$OUT"
