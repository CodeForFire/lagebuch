#!/usr/bin/env bash
# Build a .deb from a published self-contained Lagebuch. Reproducible on any Linux box with
# dpkg-deb, so the CI leg and a local smoke test run the exact same steps.
#
#   build-deb.sh <version> <publish-dir> <icon-png> <icon-svg> <document-png> <out-dir>
#
# <version> is the semver spelling, e.g. 0.6.0 or 0.6.0-rc.2. The two spellings a .deb needs are
# derived from it here rather than by the caller, so the file name and the control field cannot
# drift apart.
set -euo pipefail

VERSION="$1"
# The file name keeps the semver spelling. It used to carry the Debian one, and GitHub rewrites a
# '~' in a release asset name to '.': the release then offered lagebuch_0.6.0.rc.1_amd64.deb while
# SHA256SUMS.txt named lagebuch_0.6.0~rc.1_amd64.deb, so `sha256sum -c` failed on a file nobody
# could download (#448). Only pre-release tags ever produced a '~', which is why it took the first
# rc to surface.
#
# The control field keeps the Debian spelling. Debian reads '-' as opening a Debian revision, which
# sorts ABOVE the plain version, so apt would see 0.6.0 as older than 0.6.0-rc.2 and refuse the
# final release as a downgrade. '~' sorts below, which is what makes the final an upgrade over its
# own pre-releases. apt reads this field, never the file name.
DEB_VERSION="${VERSION//-/\~}"
PUBLISH_DIR="$2"
ICON_PNG="$3"
ICON_SVG="$4"
DOCUMENT_PNG="$5"
OUT_DIR="$6"

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

# The Einsatzdatei's document icon, under the name the MIME type below gives it. From the PNG
# master only, as there is no vector one, so this is the one icon rsvg-convert cannot render.
case "$RENDERER" in
    magick|convert) ;;
    *) echo "error: need magick or convert to render the document icon" >&2
       echo "       Debian/Ubuntu: apt-get install -y imagemagick" >&2
       exit 1 ;;
esac
for size in $ICON_SIZES; do
    DEST="$ROOT/usr/share/icons/hicolor/${size}x${size}/mimetypes/application-x-lagebuch.png"
    install -d "$(dirname "$DEST")"
    "$RENDERER" "$DOCUMENT_PNG" -filter Lanczos -resize "${size}x${size}" -strip "PNG32:$DEST"
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
# --- Einsatzdatei MIME type --------------------------------------------------------------------
# What makes a file manager show the document icon for an Einsatzdatei and offer Lagebuch to open
# it. .fwincident is what the app saved before .lagebuch and stays a glob of the same type, so an
# older Einsatz opens the same way. An Einsatzdatei is a SQLite database, and shared-mime-info's
# magic sniffs it as application/vnd.sqlite3; declaring the type a subclass of that is what lets
# the glob decide instead of the two conflicting.
install -d "$ROOT/usr/share/mime/packages"
cat > "$ROOT/usr/share/mime/packages/$PKG.xml" <<'MIME'
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="application/x-lagebuch">
    <comment>Lagebuch incident</comment>
    <comment xml:lang="de">Lagebuch-Einsatz</comment>
    <sub-class-of type="application/vnd.sqlite3"/>
    <icon name="application-x-lagebuch"/>
    <glob pattern="*.lagebuch"/>
    <glob pattern="*.fwincident"/>
  </mime-type>
</mime-info>
MIME
chmod 644 "$ROOT/usr/share/mime/packages/$PKG.xml"

install -d "$ROOT/usr/share/applications"
cat > "$ROOT/usr/share/applications/$PKG.desktop" <<DESKTOP
[Desktop Entry]
Version=1.1
Type=Application
Name=Lagebuch
GenericName=Einsatzdokumentation
Comment=Einsatztagebuch, Atemschutzüberwachung und Einsatzberichte für den ELW
Exec=$PKG %f
Icon=$PKG
Terminal=false
StartupNotify=false
StartupWMClass=LageBuch.App
Categories=Office;
MimeType=application/x-lagebuch;
Keywords=Einsatz;Einsatztagebuch;ETB;Feuerwehr;ELW;Atemschutz;Einsatzdokumentation;Lagedarstellung;
DESKTOP

# Optional on purpose, unlike the renderer above: this only inspects, so its absence cannot change
# what gets built. set -e turns a validation failure into a build failure, which is the point.
if command -v desktop-file-validate >/dev/null 2>&1; then
    desktop-file-validate "$ROOT/usr/share/applications/$PKG.desktop"
fi

# --- control metadata ------------------------------------------------------------------------
# Installed-Size is in KiB, what apt shows before installing.
#
# "Self-contained" means no .NET *runtime* is needed. It does not mean no system libraries are:
# the payload still links fontconfig and libstdc++, still dlopens the X11 client libs, and still
# aborts before the first window without ICU. Until this list existed the package declared only
# hicolor-icon-theme, so on a machine without a desktop already present `apt install ./lagebuch.deb`
# reported success and the app then died with "Couldn't find a valid ICU package installed on the
# system" — a failure the package itself had promised could not happen.
#
# Every entry below was checked by installing the .deb into a bare debian:bookworm-slim container
# and running the app against an X display outside the container (installing xvfb *inside* it would
# drag in libx11-6 & friends and mask the very gaps being measured). "fatal" = the app aborts
# without it; "silent" = it starts and maps a window anyway, which is the reason the line is here.
#
#   libc6 >= 2.34     highest GLIBC_ symbol over every shipped .so (libe_sqlite3.so)
#   libstdc++6 >= 6   GLIBCXX_3.4.22 in libhostpolicy.so; CXXABI_1.3.9 in libqpdf.so
#   libfontconfig1    fatal — NEEDED by libSkiaSharp.so, so Avalonia's renderer will not load
#   libicu*           fatal — Formatting.cs pins de-DE and InvariantGlobalization is off
#   libICE/libSM      fatal — Avalonia's X11PlatformLifetimeEvents ctor calls IceAddConnectionWatch
#                     unconditionally; without them startup throws DllNotFoundException
#   libxi6            silent — XInput2 is how Avalonia receives touch and pen input. The app starts
#                     without it on core events, so a touchscreen ELW would simply stop responding
#                     to touch with nothing in the log. A hard dependency is the cheaper failure.
#   libx11-6/libxext6 dlopened directly by Avalonia.X11
#
# libssl3 is the one entry that is reasoned rather than observed: .NET dlopens libssl.so.3 on first
# use, so it never appears in a start-and-quit trace, but the sync host's TLS and its self-signed
# certificate have no other provider on Linux. libssl3 is a real package on bookworm and jammy and
# a virtual one provided by libssl3t64 on trixie and noble, hence the alternation.
#
# The ICU alternation is newest-first because apt takes the first alternative it can resolve:
# 76 trixie, 74 noble, 72 bookworm, 70 jammy — all four verified against the real archives. A
# future release adds a new head entry; nothing else changes.
#
# Recommends, not Depends, for the rest — apt installs recommends by default, so they still arrive
# on a normal `apt install ./lagebuch.deb`, but none of them can keep the app from starting:
#   libxrandr2/libxcursor1  measured: window still maps without them (screen enumeration and
#                           themed cursors fall back)
#   libgl1                  measured: window still maps; Avalonia falls back to software rendering
#   xdg-utils               xdg-open, for attachments and links
#
# alsa-utils is a Depends although the app starts without it: it provides aplay, which plays the
# Rückzugsalarm. An install with --no-install-recommends would otherwise ship a silent alarm that
# nothing on screen announces — "Ton testen" in Über reports it, but only if someone presses it
# (#603). A missing player is not a supported configuration.
#
# hicolor-icon-theme owns /usr/share/icons/hicolor/index.theme; without that file the theme has
# no directory list and a lookup of Icon=lagebuch finds nothing at all. A normal GUI app gets it
# transitively via libgtk, but a self-contained Avalonia app links no GTK. That package also
# carries "interest-noawait /usr/share/icons/hicolor", and dpkg activates file triggers for
# third-party packages exactly as for archive ones — so depending on it is also what refreshes
# the icon cache, and a postinst doing it by hand would only duplicate the work.
# shared-mime-info and desktop-file-utils carry the Einsatzdatei association the same way: the first
# owns update-mime-database, which compiles mime/packages/lagebuch.xml into the database file
# managers read, the second update-desktop-database, which indexes this entry's MimeType key in
# mimeinfo.cache. Both run from their own file triggers on /usr/share/mime/packages and
# /usr/share/applications, so here too a postinst would only duplicate the work.
SIZE_KB=$(du -sk "$ROOT/usr" | cut -f1)
install -d "$ROOT/DEBIAN"
cat > "$ROOT/DEBIAN/control" <<CONTROL
Package: $PKG
Version: $DEB_VERSION
Section: utils
Priority: optional
Architecture: amd64
Depends: libc6 (>= 2.34), libstdc++6 (>= 6), libgcc-s1, zlib1g,
 libfontconfig1,
 libicu76 | libicu74 | libicu72 | libicu70,
 libssl3 | libssl3t64,
 libx11-6, libxext6, libxi6, libice6, libsm6,
 hicolor-icon-theme, shared-mime-info, desktop-file-utils,
 alsa-utils
Recommends: libxrandr2, libxcursor1, libgl1, xdg-utils
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
