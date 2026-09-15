#!/usr/bin/env bash
# Builds the README demo GIF from the frames DemoFlowRenderTests captures
# (`make screenshots`): resizes each frame, adds a caption bar, and encodes a
# palette-optimised GIF with ffmpeg.
#
#   packaging/demo/build-demo-gif.sh <frames-dir> <output.gif>
#
# Needs ImageMagick (`magick`) and ffmpeg.
set -euo pipefail

frames_dir="${1:-docs/screenshots}"
out="${2:-docs/demo/einsatz-flow.gif}"
width="${GIF_WIDTH:-960}"
# Every frame is padded to this height before the caption bar is added, so
# views whose rendered height differs by a few pixels don't leave slivers of
# the previous frame behind in the GIF.
height="${GIF_HEIGHT:-520}"
seconds_per_frame="${GIF_SECONDS:-2.2}"
max_colors="${GIF_COLORS:-128}"

# Frame order and captions -- the story a first-time viewer should follow.
frames=(
  "home.png|Einsatz anlegen oder öffnen"
  "einsatzdaten.png|Stichwort, Einsatznummer, Adresse"
  "etb.png|Einsatztagebuch: jede Meldung mit Zeitstempel"
  "kraefte.png|Kräfte und Stärke im Blick"
  "atemschutz.png|Atemschutzüberwachung mit Sprachansage"
  "aufgaben.png|Aufgaben mit Fälligkeit"
  "co-messung.png|CO-Messprotokoll Wohnung für Wohnung"
  "pdf-export.png|PDF-Bericht mit einem Klick"
)

command -v magick >/dev/null || { echo "ImageMagick 'magick' not found" >&2; exit 1; }
command -v ffmpeg >/dev/null || { echo "ffmpeg not found" >&2; exit 1; }

font="$(fc-match -f '%{file}' 'DejaVu Sans:bold' 2>/dev/null || true)"
font_arg=()
if [ -n "$font" ]; then font_arg=(-font "$font"); fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

i=0
for entry in "${frames[@]}"; do
  file="${entry%%|*}"
  caption="${entry#*|}"
  src="$frames_dir/$file"
  [ -f "$src" ] || { echo "missing frame: $src (run 'make screenshots' first)" >&2; exit 1; }
  i=$((i + 1))
  magick "$src" -resize "${width}x${height}" \
    -background '#0b0f14' -gravity north -extent "${width}x${height}" \
    -gravity south -splice 0x48 \
    -fill white "${font_arg[@]}" -pointsize 22 -annotate +0+12 "$caption" \
    "$(printf '%s/frame-%02d.png' "$work" "$i")"
done

mkdir -p "$(dirname "$out")"
ffmpeg -y -loglevel error \
  -framerate "1/${seconds_per_frame}" -i "$work/frame-%02d.png" \
  -vf "split[s0][s1];[s0]palettegen=max_colors=${max_colors}[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5" \
  -loop 0 "$out"

size=$(stat -c %s "$out")
echo "$out: $i frames, $((size / 1024)) KiB"
if [ "$size" -gt $((3 * 1024 * 1024)) ]; then
  echo "warning: GIF is larger than 3 MiB -- try GIF_WIDTH=800 or GIF_COLORS=96" >&2
fi
