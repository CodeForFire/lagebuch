#!/usr/bin/env bash
#
# Runs the pull request's CodeQL analysis on this machine, before the push:
#
#   scripts/codeql-local.sh [BASE]        # BASE defaults to origin/main
#
# Same CodeQL as CI: the bundle version is read from the codeql-action commit
# that .github/workflows/codeql.yml pins, so a Dependabot bump of the action
# moves this script along with it. Same queries (security-and-quality), the
# same build (LageBuch.Desktop.slnf, Release) and the same exclusions
# (.github/codeql/codeql-config.yml, applied by scripts/codeql-results.py).
#
# Fails when a result lands on a line changed since BASE -- what the
# code-scanning ruleset would block. The bundle (about 1 GB unpacked) is cached
# under ${XDG_CACHE_HOME:-~/.cache}/lagebuch-codeql; the database and SARIF go
# to out/codeql. A first run takes about ten minutes, most of it the traced
# clean build and the analysis, not the download.

set -euo pipefail

base=${1:-origin/main}
root=$(git rev-parse --show-toplevel)
cd "$root"

action_sha=$(grep -oE 'github/codeql-action/init@[0-9a-f]{40}' .github/workflows/codeql.yml | head -1 | cut -d@ -f2)
if [ -z "$action_sha" ]; then
  echo "codeql.yml pins no github/codeql-action/init commit" >&2
  exit 2
fi

bundle=$(curl -fsSL "https://raw.githubusercontent.com/github/codeql-action/$action_sha/src/defaults.json" |
  python3 -I -c 'import json, sys; print(json.load(sys.stdin)["bundleVersion"])')

case "$(uname -s)" in
  Linux) platform=linux64 ;;
  Darwin) platform=osx64 ;;
  *) echo "no CodeQL bundle for $(uname -s); run it on Linux or macOS" >&2; exit 2 ;;
esac

cache=${XDG_CACHE_HOME:-$HOME/.cache}/lagebuch-codeql/$bundle
codeql=$cache/codeql/codeql
if [ ! -x "$codeql" ]; then
  echo "Fetching $bundle ($platform) into $cache"
  mkdir -p "$cache"
  curl -fL --progress-bar \
    "https://github.com/github/codeql-action/releases/download/$bundle/codeql-bundle-$platform.tar.gz" |
    tar -xz -C "$cache"
fi

out=$root/out/codeql
mkdir -p "$out"

# Two runs on one checkout would overwrite each other's database mid-build.
# macOS ships no flock; there the guard is skipped.
exec 9>"$out/.lock"
if command -v flock >/dev/null && ! flock -n 9; then
  echo "make codeql is already running in this checkout" >&2
  exit 2
fi

# A clean build, so every file is compiled under the tracer; an incremental one
# would hand CodeQL only the projects that happened to be stale. For the same
# reason no build may run outside it: an MSBuild node or compiler server left
# over from an earlier build takes projects on untraced, and the database then
# silently lacks them (a second run once saw 257 of 763 files). So shut them
# down first and start no new long-lived ones.
dotnet build-server shutdown >/dev/null
export MSBUILDDISABLENODEREUSE=1

"$codeql" database create "$out/db" --overwrite --language=csharp --build-mode=manual \
  --source-root="$root" \
  --command="dotnet build LageBuch.Desktop.slnf -c Release --no-incremental -nodeReuse:false -p:UseSharedCompilation=false"

"$codeql" database analyze "$out/db" codeql/csharp-queries:codeql-suites/csharp-security-and-quality.qls \
  --format=sarif-latest --output="$out/raw.sarif" --threads=0 2>&1 | tee "$out/analyze.log"

# A database that missed files reports nothing for them, which reads as clean.
read -r scanned total < <(grep -oE 'scanned [0-9]+ out of [0-9]+' "$out/analyze.log" | awk '{print $2, $5}')
if [ -z "${total:-}" ] || [ $((scanned * 100)) -lt $((total * 95)) ]; then
  echo "CodeQL saw only ${scanned:-?} of ${total:-?} C# files; the build ran partly outside the tracer. Rerun." >&2
  exit 2
fi

python3 -I scripts/codeql-results.py filter --config .github/codeql/codeql-config.yml \
  "$out/raw.sarif" "$out/results.sarif"
python3 -I scripts/codeql-results.py check --base "$base" "$out/results.sarif"
