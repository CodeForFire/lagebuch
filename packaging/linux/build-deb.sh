#!/usr/bin/env bash
# Build a .deb from a published self-contained Lagebuch. Reproducible on any Linux box with
# dpkg-deb, so the CI leg and a local smoke test run the exact same steps.
#
#   build-deb.sh <version> <publish-dir> <icon-png> <icon-svg> <out-dir>
set -euo pipefail

VERSION="$1"
PUBLISH_DIR="$2"
ICON_PNG="$3"
ICON_SVG="$4"
OUT_DIR="$5"

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

# --- icons -----------------------------------------------------------------------------------
# /usr/share/icons/hicolor/index.theme enumerates the directories a lookup searches, and it stops
# at 512x512 — so the 1024 master this script used to install could only ever be filed under a
# size it is not, and was the single candidate for every consumer: the panel asking for 16-24px,
# menus 24-32, docks 48-64, the GNOME app grid 96. Each of them was decoding a megapixel PNG and
# downscaling it. Ship one real file per declared size instead.
# Generated rather than committed: icon-1024.png is itself a render of icon.svg, so committing a
# dozen derivatives would be a dozen files free to drift from the master. Same call the macOS leg
# already makes when it builds its .icns from this very PNG with sips.
ICON_SIZES="16 22 24 32 36 48 64 72 96 128 192 256 512"

RENDERER=""
for candidate in magick convert rsvg-convert; do
    command -v "$candidate" >/dev/null 2>&1 && { RENDERER="$candidate"; break; }
done
[ -n "$RENDERER" ] || {
    echo "error: need magick, convert or rsvg-convert to render the icon sizes" >&2
    echo "       Debian/Ubuntu: apt-get install -y imagemagick" >&2
    exit 1
}

for size in $ICON_SIZES; do
    DEST="$ROOT/usr/share/icons/hicolor/${size}x${size}/apps/$PKG.png"
    install -d "$(dirname "$DEST")"
    # PNG32 because a palette PNG collapses the antialiased tile edge to 1-bit alpha, and Lanczos
    # because the default filter visibly softens the flame's inner highlight at 16-32px — the
    # same two traps packaging/logo/build-logo-assets.sh documents.
    case "$RENDERER" in
        magick|convert) "$RENDERER" "$ICON_PNG" -filter Lanczos -resize "${size}x${size}" \
                            -strip "PNG32:$DEST" ;;
        rsvg-convert)   rsvg-convert -w "$size" -h "$size" "$ICON_SVG" -o "$DEST" ;;
    esac
    chmod 644 "$DEST"
done

# The vector master too: KDE prefers scalable, and a size hicolor does not declare (40, 80, ...)
# renders from this instead of upscaling a PNG.
install -Dm644 "$ICON_SVG" "$ROOT/usr/share/icons/hicolor/scalable/apps/$PKG.svg"

# StartupWMClass must equal the window's WM_CLASS, which Avalonia derives from the assembly name
# (verified with xprop: "LageBuch.App"). Without it the desktop cannot tie the running window to
# this entry, so the dock/task switcher shows a generic icon next to a separate, unnamed group.
# StartupNotify is deliberately false, not true: Avalonia 12 never sends the startup-notification
# "remove" message (no _NET_STARTUP_ID in the binary), so claiming support would leave the busy
# cursor spinning until the desktop times out. False tells the desktop to use its own heuristics —
# i.e. the StartupWMClass match above — instead of waiting for a message that never arrives.
# One main category only: Office;Utility; made the app show up twice in the menu.
# Version is the Desktop Entry spec version this entry targets, not the app version. 1.1 is the
# newest spec that added a key used here (Keywords); everything else predates it. Declaring 1.5
# would be a hard error in desktop-file-validate <= 0.26 (Debian 12, Ubuntu 22.04), which knows
# no version above 1.4 and fails on anything it does not recognise.
install -d "$ROOT/usr/share/applications"
cat > "$ROOT/usr/share/applications/$PKG.desktop" <<DESKTOP
[Desktop Entry]
Version=1.1
Type=Application
Name=Lagebuch
GenericName=Einsatzdokumentation
Comment=Einsatztagebuch, Atemschutzüberwachung und Einsatzberichte für den ELW
Exec=$PKG
Icon=$PKG
Terminal=false
StartupNotify=false
StartupWMClass=LageBuch.App
Categories=Office;
Keywords=Einsatz;Einsatztagebuch;ETB;Feuerwehr;ELW;Atemschutz;Einsatzdokumentation;Lagedarstellung;
DESKTOP

# Optional on purpose, unlike the renderer above: this only inspects, so its absence cannot change
# what gets built. set -e turns a validation failure into a build failure, which is the point.
if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$ROOT/usr/share/applications/$PKG.desktop"
fi

# --- control metadata ------------------------------------------------------------------------
# Installed-Size is in KiB, what apt shows before installing.
# Depends is about the icon, not about running the app (the payload is self-contained).
# hicolor-icon-theme owns /usr/share/icons/hicolor/index.theme; without that file the theme has
# no directory list and a lookup of Icon=lagebuch finds nothing at all. A normal GUI app gets it
# transitively via libgtk, but a self-contained Avalonia app links no GTK. That package also
# carries "interest-noawait /usr/share/icons/hicolor", and dpkg activates file triggers for
# third-party packages exactly as for archive ones — so depending on it is also what refreshes
# the icon cache, and a postinst doing it by hand would only duplicate the work.
# desktop-file-utils is deliberately not listed: it builds mimeinfo.cache, which is only read for
# MIME-type association, and this entry declares no MimeType key. Menus parse .desktop directly.
SIZE_KB=$(du -sk "$ROOT/usr" | cut -f1)
install -d "$ROOT/DEBIAN"
cat > "$ROOT/DEBIAN/control" <<CONTROL
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Depends: hicolor-icon-theme
Maintainer: CodeForFire <noreply@github.com>
Installed-Size: $SIZE_KB
Description: Lagebuch — Einsatzdokumentation
 Einsatztagebuch, Atemschutzüberwachung und Einsatzberichte für den ELW.
 Self-contained; no .NET runtime required.
CONTROL

mkdir -p "$OUT_DIR"
DEB="$OUT_DIR/${PKG}_${VERSION}_amd64.deb"
# dpkg-deb's progress line goes to stderr so stdout is only the artifact path.
dpkg-deb --root-owner-group --build "$ROOT" "$DEB" 1>&2
echo "$DEB"
