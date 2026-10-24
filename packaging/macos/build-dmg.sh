#!/usr/bin/env bash
# Build an unsigned Lagebuch.app and wrap it in a drag-to-Applications .dmg. macOS-only
# (iconutil / hdiutil / sips / codesign). Runs on the macos-latest CI runner.
#
#   build-dmg.sh <version> <publish-dir> <icon-png-1024> <document-png-1024> <out-dir>
set -euo pipefail

VERSION="$1"
PUBLISH_DIR="$2"
ICON_PNG="$3"
DOCUMENT_PNG="$4"
OUT_DIR="$5"

APP="Lagebuch.app"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

APP_DIR="$STAGE/$APP"
install -d "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

# --- payload ---------------------------------------------------------------------------------
cp -R "$PUBLISH_DIR"/. "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/LageBuch.App"

# --- icons: 1024 png -> multi-size .icns -----------------------------------------------------
# $1 = 1024 master, $2 = name of the .icns in Resources (what Info.plist refers to, sans extension).
build_icns() {
  local png=$1 name=$2
  local iconset="$STAGE/$name.iconset"
  mkdir -p "$iconset"
  for s in 16 32 64 128 256 512; do
    sips -z $s $s      "$png" --out "$iconset/icon_${s}x${s}.png"      >/dev/null
    sips -z $((s*2)) $((s*2)) "$png" --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset" -o "$APP_DIR/Contents/Resources/$name.icns"
}
build_icns "$ICON_PNG" Lagebuch
build_icns "$DOCUMENT_PNG" LagebuchDocument

# --- Info.plist ------------------------------------------------------------------------------
# The exported type and the document type are what make Finder show the document icon for an
# Einsatzdatei and open it in Lagebuch on a double-click; the app receives it as an open-document
# event, not an argument. .fwincident is what the app saved before .lagebuch and stays a tag of the
# same type, so an older Einsatz opens the same way. public.data, not public.database: Finder never
# needs to know it is SQLite, and the narrower parent would only invite other apps to claim it.
cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>              <string>Lagebuch</string>
  <key>CFBundleDisplayName</key>       <string>Lagebuch</string>
  <key>CFBundleIdentifier</key>        <string>de.codeforfire.lagebuch</string>
  <key>CFBundleVersion</key>           <string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key>        <string>LageBuch.App</string>
  <key>CFBundleIconFile</key>          <string>Lagebuch</string>
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>LSMinimumSystemVersion</key>    <string>11.0</string>
  <key>NSHighResolutionCapable</key>   <true/>
  <key>UTExportedTypeDeclarations</key>
  <array>
    <dict>
      <key>UTTypeIdentifier</key>      <string>de.codeforfire.lagebuch.incident</string>
      <key>UTTypeDescription</key>     <string>Lagebuch-Einsatz</string>
      <key>UTTypeConformsTo</key>      <array><string>public.data</string></array>
      <key>UTTypeIconFile</key>        <string>LagebuchDocument</string>
      <key>UTTypeTagSpecification</key>
      <dict>
        <key>public.filename-extension</key> <array><string>lagebuch</string><string>fwincident</string></array>
        <key>public.mime-type</key>          <array><string>application/x-lagebuch</string></array>
      </dict>
    </dict>
  </array>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key>      <string>Lagebuch-Einsatz</string>
      <key>CFBundleTypeRole</key>      <string>Editor</string>
      <key>LSHandlerRank</key>         <string>Owner</string>
      <key>LSItemContentTypes</key>    <array><string>de.codeforfire.lagebuch.incident</string></array>
      <key>CFBundleTypeIconFile</key>  <string>LagebuchDocument</string>
    </dict>
  </array>
</dict>
</plist>
PLIST

# --- ad-hoc sign -----------------------------------------------------------------------------
# No Developer ID, so sign with the ad-hoc identity "-". This is what lets the app launch at all
# once the user allows it in Gatekeeper; without any signature Gatekeeper reports "damaged".
codesign --force --deep --sign - "$APP_DIR"

# --- .dmg (drag-to-Applications) -------------------------------------------------------------
ln -s /Applications "$STAGE/Applications"
mkdir -p "$OUT_DIR"
DMG="$OUT_DIR/lagebuch-${VERSION}-macos-arm64.dmg"
hdiutil create -volname "Lagebuch" -srcfolder "$STAGE" -ov -format UDZO "$DMG" 1>&2
echo "$DMG"
