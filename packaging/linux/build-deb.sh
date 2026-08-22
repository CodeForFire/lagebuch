#!/usr/bin/env bash
# Build a .deb from a published self-contained Lagebuch. Reproducible on any Linux box with
# dpkg-deb, so the CI leg and a local smoke test run the exact same steps.
#
#   build-deb.sh <version> <publish-dir> <icon-png> <out-dir>
set -euo pipefail

VERSION="$1"
PUBLISH_DIR="$2"
ICON_PNG="$3"
OUT_DIR="$4"

PKG="lagebuch"
ROOT="$(mktemp -d)"
trap 'rm -rf "$ROOT"' EXIT

# --- filesystem layout -----------------------------------------------------------------------
# The self-contained payload lives under /usr/lib/lagebuch; /usr/bin/lagebuch is a thin launcher
# so the binary is on PATH under a friendly name regardless of the publish output's filename.
install -d "$ROOT/usr/lib/$PKG"
cp -r "$PUBLISH_DIR"/. "$ROOT/usr/lib/$PKG/"
chmod +x "$ROOT/usr/lib/$PKG/LageBuch.App"

install -d "$ROOT/usr/bin"
cat > "$ROOT/usr/bin/$PKG" <<'LAUNCH'
#!/bin/sh
exec /usr/lib/lagebuch/LageBuch.App "$@"
LAUNCH
chmod +x "$ROOT/usr/bin/$PKG"

install -Dm644 "$ICON_PNG" "$ROOT/usr/share/icons/hicolor/512x512/apps/$PKG.png"

install -d "$ROOT/usr/share/applications"
cat > "$ROOT/usr/share/applications/$PKG.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Lagebuch
Comment=Einsatzdokumentation für die Feuerwehr
Exec=$PKG
Icon=$PKG
Terminal=false
Categories=Office;Utility;
DESKTOP

# --- control metadata ------------------------------------------------------------------------
# Installed-Size is in KiB, what apt shows before installing.
SIZE_KB=$(du -sk "$ROOT/usr" | cut -f1)
install -d "$ROOT/DEBIAN"
cat > "$ROOT/DEBIAN/control" <<CONTROL
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: CodeForFire <noreply@github.com>
Installed-Size: $SIZE_KB
Description: Lagebuch — Einsatzdokumentation
 Digitales Einsatztagebuch und Lagedarstellung für die Feuerwehr.
 Self-contained; no .NET runtime required.
CONTROL

mkdir -p "$OUT_DIR"
DEB="$OUT_DIR/${PKG}_${VERSION}_amd64.deb"
# dpkg-deb's progress line goes to stderr so stdout is only the artifact path.
dpkg-deb --root-owner-group --build "$ROOT" "$DEB" 1>&2
echo "$DEB"
