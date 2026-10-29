#!/usr/bin/env bash
# Renders the two bitmaps the MSI's dialogs show from the app icon, so the installer looks like
# Lagebuch instead of WiX's placeholders. Rerun when icon.svg changes and commit the output:
#
#   packaging/windows/make-installer-bitmaps.sh
#
# Needs rsvg-convert and ImageMagick 7. WixUI wants 24-bit BMPs at exactly these sizes:
#   banner.bmp  493x58   top strip of the inner dialogs; WiX draws the title in black on the left
#   dialog.bmp  493x312  Welcome and Finish; WiX draws the text in black from x≈180 rightwards
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
svg="$here/../../src/LageBuch.App/Assets/icon.svg"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

rsvg-convert -w 120 -h 120 "$svg" -o "$work/icon-120.png"
rsvg-convert -w 44 -h 44 "$svg" -o "$work/icon-44.png"

# Dark left panel in the app's own background colours, the flame centred on it, white beyond.
magick -size 493x312 xc:white \
  \( -size 164x312 gradient:'#141A24-#0B0E13' \) -geometry +0+0 -composite \
  \( -size 4x312 xc:'#FF3D2E' \) -geometry +164+0 -composite \
  "$work/icon-120.png" -geometry +22+96 -composite \
  -alpha remove -alpha off -type TrueColor "BMP3:$here/dialog.bmp"

# White strip, icon on the right where WiX leaves room for a logo.
magick -size 493x58 xc:white \
  "$work/icon-44.png" -geometry +441+7 -composite \
  -alpha remove -alpha off -type TrueColor "BMP3:$here/banner.bmp"
