#!/usr/bin/env bash
#
# Writes the CycloneDX SBOM for one shipped app: every NuGet package it is built
# from, with version, licence and content hash.
#
#   scripts/sbom.sh src/LageBuch.App/LageBuch.App.csproj lagebuch-desktop 0.9.2 dist/x.cdx.json
#
# The project must already be restored; the release jobs call this right after
# their `dotnet publish`, so the SBOM describes the graph that was just built
# rather than one a second restore might resolve differently. Both the release
# workflow and a local check use this script, so the exclusion list below exists
# once.
#
# Project references are walked (-rs), so packages that reach the app through
# LageBuch.Sync or LageBuch.Documents are listed too.

set -euo pipefail

if [ $# -ne 4 ]; then
  echo "usage: $0 <project.csproj> <name> <version> <output.cdx.json>" >&2
  exit 2
fi

project=$1
name=$2
version=$3
output=$4

# Build-time only, never in a published package. StyleCop arrives through
# Directory.Build.props with PrivateAssets="all", which CycloneDX's
# --exclude-dev does not recognise; the other two are MSBuild task packages.
excluded=(
  StyleCop.Analyzers
  StyleCop.Analyzers.Unstable
  Avalonia.BuildServices
  Microsoft.NET.ILLink.Tasks
)

exclude_filter=$(IFS=,; echo "${excluded[*]}")

dotnet tool run dotnet-CycloneDX -- "$project" \
  --recursive \
  --disable-package-restore \
  --configuration Release \
  --output-format Json \
  --exclude-filter "$exclude_filter" \
  --set-name "$name" \
  --set-version "$version" \
  --output "$(dirname "$output")" \
  --filename "$(basename "$output")"

# A package that slips past the filter would be listed as shipped. Fail instead.
for package in "${excluded[@]}"; do
  if jq -e --arg p "$package" '.components[] | select(.name == $p)' "$output" > /dev/null; then
    echo "::error::$package is build-time only but is listed in $output" >&2
    exit 1
  fi
done

echo "$output: $(jq '.components | length' "$output") components"
