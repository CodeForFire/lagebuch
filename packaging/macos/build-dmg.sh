#!/usr/bin/env bash
# Build an unsigned Lagebuch.app and wrap it in a drag-to-Applications .dmg. macOS-only
# (iconutil / hdiutil / sips / codesign). Runs on the macos-latest CI runner.
#
#   build-dmg.sh <version> <publish-dir> <icon-png-1024> <out-dir>
set -euo pipefail

VERSION="$1"
PUBLISH_DIR="$2"
ICON_PNG="$3"
OUT_DIR="$4"

APP="Lagebuch.app"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

APP_DIR="$STAGE/$APP"
install -d "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

# --- payload ---------------------------------------------------------------------------------
cp -R "$PUBLISH_DIR"/. "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/Feuerwehr.App"

# --- icon: 1024 png -> multi-size .icns ------------------------------------------------------
ICONSET="$STAGE/Lagebuch.iconset"
mkdir -p "$ICONSET"
for s in 16 32 64 128 256 512; do
  sips -z $s $s      "$ICON_PNG" --out "$ICONSET/icon_${s}x${s}.png"      >/dev/null
  sips -z $((s*2)) $((s*2)) "$ICON_PNG" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP_DIR/Contents/Resources/Lagebuch.icns"

# --- Info.plist ------------------------------------------------------------------------------
cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>              <string>Lagebuch</string>
  <key>CFBundleDisplayName</key>       <string>Lagebuch</string>
  <key>CFBundleIdentifier</key>        <string>de.feuerwehr.lagebuch</string>
  <key>CFBundleVersion</key>           <string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key>        <string>Feuerwehr.App</string>
  <key>CFBundleIconFile</key>          <string>Lagebuch</string>
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>LSMinimumSystemVersion</key>    <string>11.0</string>
  <key>NSHighResolutionCapable</key>   <true/>
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
