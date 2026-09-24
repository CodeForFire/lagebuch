#!/usr/bin/env bash
#
# Regenerates every Lagebuch logo derivative from the single master badge in
# docs/logo/source/. Run it after replacing the master; never hand-edit the outputs.
#
#   ./packaging/logo/build-logo-assets.sh        (or: make logo-assets)
#
# Requires ImageMagick 7 (`magick`).
#
# The master is a 1024x1024 badge sitting on an opaque grey *gradient* backdrop, so a flat
# colour key would leave a halo. The badge is a circle, so a circular alpha mask cuts it out
# exactly instead. Geometry below was measured on the master; re-measure if it is replaced.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
src="$repo_root/docs/logo/source/lagebuch-badge.jpg"

# Full badge: centre and radius of the white ring, in master pixels.
badge_cx=512
badge_cy=502
badge_r=432

# Text-free emblem (helmet + flame + shield): a smaller disc centred on the artwork, stopping
# short of the wordmark, which starts around y=645.
mark_cx=512
mark_cy=372
mark_r=262

out_docs="$repo_root/docs/logo"
out_app="$repo_root/src/LageBuch.App.Shared/Assets"
out_pdf="$repo_root/src/LageBuch.Documents/Assets"
out_play="$repo_root/docs/play"

# The app's flat background colour, shared by the social preview and both Play Store assets,
# its accent red (taken from the emblem's shield), and its own font files -- the Play banner is
# set in them so it matches the product.
app_bg="#0B0E13"
accent="#D84938"
fonts="$repo_root/src/LageBuch.App.Shared/Assets/Fonts"

command -v magick >/dev/null || { echo "error: ImageMagick 7 (magick) not found" >&2; exit 1; }
[ -f "$src" ] || { echo "error: master art missing: $src" >&2; exit 1; }

# Squeezes an output in place. 256 colours is visually indistinguishable on this flat,
# vector-style artwork (RMSE < 1%) but roughly quarters the file; PNG32 is kept rather than
# PNG8 because PNG8 would collapse the alpha channel to on/off and leave the circle's edge
# jagged. optipng is a lossless extra and optional.
shrink() {
    magick "$1" -strip -colors 256 "PNG32:$1"
    command -v optipng >/dev/null && optipng -quiet -o2 "$1"
}

# $1 = cx, $2 = cy, $3 = radius, $4 = edge length of the output, $5 = output. Masks the master
# to a disc, keeping everything outside it transparent, then crops to that disc's bounding box.
cut_disc() {
    local cx=$1 cy=$2 r=$3 size=$4 dest=$5
    magick "$src" \
        \( +clone -alpha transparent -fill white -draw "circle $cx,$cy $cx,$((cy - r))" \) \
        -alpha off -compose CopyOpacity -composite \
        -crop "$((r * 2))x$((r * 2))+$((cx - r))+$((cy - r))" +repage \
        -resize "${size}x${size}" \
        "PNG32:$dest"
    shrink "$dest"
}

# 512 is deliberate rather than 1024: the badge is shown at 200 px in the README and 340 px in
# the About dialog, so 512 still covers HiDPI. The 1024 master stays in docs/logo/source/ for
# anything print-sized.
echo "==> docs/logo/lagebuch-logo.png (full badge, 512)"
cut_disc "$badge_cx" "$badge_cy" "$badge_r" 512 "$out_docs/lagebuch-logo.png"

echo "==> src/LageBuch.App.Shared/Assets/lagebuch-logo.png (About dialog, 512)"
cut_disc "$badge_cx" "$badge_cy" "$badge_r" 512 "$out_app/lagebuch-logo.png"

echo "==> src/LageBuch.Documents/Assets/lagebuch-mark.png (PDF header, 256)"
# Keeps its dark disc field on purpose: the helmet is white, so a background-free cutout
# would be invisible on a white PDF page.
cut_disc "$mark_cx" "$mark_cy" "$mark_r" 256 "$out_pdf/lagebuch-mark.png"

echo "==> docs/logo/lagebuch-social-preview.png (GitHub social preview, 1280x640)"
# Flat AppBgColor rather than the app's background *gradient*: a near-black gradient visibly
# bands once quantised, and dithering it away costs 3x the file size for no visible gain here.
magick -size 1280x640 "xc:$app_bg" \
    \( "$out_docs/lagebuch-logo.png" -resize 460x460 \) \
    -gravity center -compose over -composite \
    "PNG24:$out_docs/lagebuch-social-preview.png"
shrink "$out_docs/lagebuch-social-preview.png"

# --- Google Play Store listing -------------------------------------------------------------
#
# Both of these are full-bleed and opaque on purpose. Play applies its own rounded-corner mask
# and shadow to the icon, so artwork is expected to fill the square; the launcher icon's
# transparent corners would show up as clipped ones here. The feature graphic rejects alpha
# outright.
#
# Neither goes through shrink(): Play specifies a 32-bit icon, and shrink()'s -colors 256 turns
# the output into an 8-bit palette PNG. That is exactly why docs/logo/lagebuch-logo.png cannot
# be uploaded as the store icon even though it is already 512x512. Both files land around 100 kB
# as truecolour, against a 1 MB limit, so there is nothing to gain by squeezing them.

mkdir -p "$out_play"

echo "==> docs/play/icon-512.png (Play Store icon, 512x512, opaque)"
# The text-free emblem, NOT the full badge. The badge carries the wordmark, a tagline and a
# github.com URL: illegible at the 48-192 px Play actually renders an icon at, and Play's icon
# guidance disallows promotional text and URLs in it. cut_disc writes a temporary at 512 so the
# emblem can be composited full-bleed onto the app background.
cut_disc "$mark_cx" "$mark_cy" "$mark_r" 512 "$out_play/.icon-mark.tmp.png"
magick -size 512x512 "xc:$app_bg" \
    \( "$out_play/.icon-mark.tmp.png" -resize 470x470 \) \
    -gravity center -compose over -composite \
    -alpha remove -alpha off \
    "PNG32:$out_play/icon-512.png"
rm -f "$out_play/.icon-mark.tmp.png"

echo "==> docs/play/feature-graphic-1024x500.png (Play feature graphic, no alpha)"
# A horizontal lockup: the text-free emblem beside the wordmark, set in the app's own fonts
# (Oswald for the wordmark, Barlow for the tagline) so the banner matches the product rather
# than looking generic. Deliberately not the full badge -- at banner size its baked-in tagline
# and github.com URL read as clutter. Play crops this asset to several ratios, so the lockup
# stays in the middle ~80% with nothing near an edge.
#
# Each piece is rendered to its own file and appended, rather than assembled in one command
# with nested parentheses: -append takes its width from the first image's canvas, which
# silently cropped the tagline when the wordmark happened to be narrower.
t="$out_play/.tmp"
cut_disc "$mark_cx" "$mark_cy" "$mark_r" 300 "$t-mark.png"

# 108/30 is not arbitrary: it is the smallest wordmark that stays wider than the tagline
# (485 px against 458 px), which is what keeps the block left-aligned without a ragged edge.
magick -background none -fill "#FFFFFF" -font "$fonts/Oswald-SemiBold.ttf" \
    -pointsize 108 -kerning 3 label:"LAGEBUCH" +repage "PNG32:$t-word.png"
magick -background none -fill "#9AA4B2" -font "$fonts/Barlow-Regular.ttf" \
    -pointsize 30 label:"Einsatzdokumentation für den ELW" +repage "PNG32:$t-tag.png"
magick -size 110x6 "xc:$accent" +repage "PNG32:$t-rule.png"
magick -size 1x18 "xc:none" +repage "PNG32:$t-gap.png"

magick "$t-word.png" "$t-gap.png" "$t-rule.png" "$t-gap.png" "$t-tag.png" \
    -background none -gravity west -append +repage "PNG32:$t-text.png"

magick -size 1024x500 "xc:$app_bg" \
    \( "$t-mark.png" "$t-text.png" -background none -gravity center +smush 48 \) \
    -gravity center -compose over -composite \
    -alpha remove -alpha off \
    "PNG24:$out_play/feature-graphic-1024x500.png"
rm -f "$t"-*.png

echo
echo "Done. Generated:"
for f in "$out_docs/lagebuch-logo.png" "$out_docs/lagebuch-social-preview.png" \
         "$out_app/lagebuch-logo.png" "$out_pdf/lagebuch-mark.png" \
         "$out_play/icon-512.png" "$out_play/feature-graphic-1024x500.png"; do
    printf '  %-58s %s\n' "${f#"$repo_root/"}" "$(magick identify -format '%wx%h %B bytes' "$f")"
done
