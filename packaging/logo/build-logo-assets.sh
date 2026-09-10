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
magick -size 1280x640 "xc:#0B0E13" \
    \( "$out_docs/lagebuch-logo.png" -resize 460x460 \) \
    -gravity center -compose over -composite \
    "PNG24:$out_docs/lagebuch-social-preview.png"
shrink "$out_docs/lagebuch-social-preview.png"

echo
echo "Done. Generated:"
for f in "$out_docs/lagebuch-logo.png" "$out_docs/lagebuch-social-preview.png" \
         "$out_app/lagebuch-logo.png" "$out_pdf/lagebuch-mark.png"; do
    printf '  %-58s %s\n' "${f#"$repo_root/"}" "$(magick identify -format '%wx%h %B bytes' "$f")"
done
