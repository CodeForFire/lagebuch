#!/usr/bin/env bash
#
# Fetches the German TTS voices into speech-models/.
#
# The models are NOT committed: this repository has no Git LFS, and tens of megabytes of weights do
# not belong in its history. This script is the single source for both `make audition` and the
# release build, so the developer's audition and the shipped installer always hear the same voice.
#
# Every archive is pinned by SHA-256. The digests come from the upstream release's own checksum.txt
# but are copied in here on purpose: a pin that is fetched at run time from the same place as the
# file it verifies is not a pin.
#
# Usage:
#   packaging/speech/fetch-voices.sh [<out-dir>] [<voice-id> ...]
#
# With no voice ids, fetches the shipping set. Pass ids (or `all`) to fetch the audition set.

set -euo pipefail

OUT_DIR="${1:-speech-models}"
shift || true

SHERPA_RELEASE="https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models"
PIPER_VOICES="https://huggingface.co/rhasspy/piper-voices/resolve/main"

# archive-stem <TAB> sha256.  Piper archives all unpack to a dir named after the stem.
read -r -d '' PINS <<'EOF' || true
vits-piper-de_DE-thorsten-medium-int8	07e240b7b9c1fc9211d5a69512f8cbe11b3286c2ed79c15c076ac6ed427fdf13
vits-piper-de_DE-thorsten-high-int8	cc6f0abde2b9d5811c9e692b0f625c3de2a0687383fd4a571f547e6dd0ca4823
vits-piper-de_DE-thorsten-low-int8	1d845c838b08a99dd1f86e7169a6696da6dac61530ee4a81446a20e009948233
vits-piper-de_DE-karlsson-low-int8	04f57738da5d7929d46fb445c09697f8e3e2a4d6562708c6a2ac5fbec63bbfdb
vits-piper-de_DE-kerstin-low-int8	bcd8039667940cf2efc939b844f4b33d0823096572fcc1a8caaa2faa77f3379c
vits-piper-de_DE-eva_k-x_low-int8	0501123c7e184571a40690e79943f4111a57b988632f7f60a9d2344fd061e2a2
vits-piper-de_DE-ramona-low-int8	e532ddcbb09c16a65f56441bbbbf301083d2ea5b18ce937775303942f2b7a8f4
sherpa-onnx-supertonic-3-tts-int8-2026-05-11	82fa96f91c4ef8abaae3a14a3f4153facf88bed821d1f7331cec2700f432c427
EOF

# voice-id <TAB> archive-stem.  The voice id is what VoiceCatalog and the settings use.
read -r -d '' VOICES <<'EOF' || true
thorsten-medium	vits-piper-de_DE-thorsten-medium-int8
thorsten-high	vits-piper-de_DE-thorsten-high-int8
thorsten-low	vits-piper-de_DE-thorsten-low-int8
karlsson-low	vits-piper-de_DE-karlsson-low-int8
kerstin-low	vits-piper-de_DE-kerstin-low-int8
eva_k-x_low	vits-piper-de_DE-eva_k-x_low-int8
ramona-low	vits-piper-de_DE-ramona-low-int8
supertonic-3	sherpa-onnx-supertonic-3-tts-int8-2026-05-11
EOF

AUDITION_SET="thorsten-medium thorsten-high thorsten-low karlsson-low kerstin-low eva_k-x_low ramona-low supertonic-3 mls-medium"

# Filled in after the audition picks a winner; until then `make audition` drives the fetch.
SHIPPING_SET="thorsten-medium"

CACHE="${LAGEBUCH_VOICE_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/lagebuch-voices}"

log() { printf '  %s\n' "$*" >&2; }
die() { printf 'fetch-voices: %s\n' "$*" >&2; exit 1; }

pin_for() { awk -F'\t' -v k="$1" '$1==k {print $2}' <<<"$PINS"; }
stem_for() { awk -F'\t' -v k="$1" '$1==k {print $2}' <<<"$VOICES"; }

# Downloads <stem>.tar.bz2 once into the cache and verifies it against its pin.
download() {
  local stem="$1" url="$2" want archive
  want="$(pin_for "$stem")"
  [ -n "$want" ] || die "no SHA-256 pinned for '$stem'"
  archive="$CACHE/$stem.tar.bz2"

  if [ ! -f "$archive" ]; then
    log "downloading $stem"
    curl --fail --location --silent --show-error --output "$archive.part" "$url"
    mv "$archive.part" "$archive"
  fi

  local got
  got="$(sha256sum "$archive" | cut -d' ' -f1)"
  [ "$got" = "$want" ] || die "checksum mismatch for $stem: got $got, pinned $want"
  printf '%s\n' "$archive"
}

# espeak-ng-data ships 19 MB, 95% of it dictionaries for languages we will never speak. The subset
# we need is ~1.4 MB. Keeping the whole thing would add ~18 MB to every installer for nothing.
#
# en_dict is in the list despite this being a German-only app: espeak-ng initialises with an
# English default voice and falls back to it, and without the file it logs
# "Can't read dictionary file: .../en_dict" on every utterance. German still comes out, but a
# foreign word inside a German sentence would be left to the letter rules. 167 KB is a cheap
# insurance premium.
prune_espeak_data() {
  local src="$1" dst="$2"
  rm -rf "$dst"
  mkdir -p "$dst/lang/gmw"
  local f
  for f in phondata phonindex phontab intonations phondata-manifest de_dict en_dict; do
    cp "$src/$f" "$dst/$f"
  done
  cp "$src"/lang/gmw/de "$src"/lang/gmw/en "$dst/lang/gmw/"
  cp -r "$src/voices" "$dst/voices"
}

fetch_piper() {
  local id="$1" stem archive tmp
  stem="$(stem_for "$id")"
  archive="$(download "$stem" "$SHERPA_RELEASE/$stem.tar.bz2")"

  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' RETURN
  tar -xf "$archive" -C "$tmp"

  if [ ! -d "$OUT_DIR/espeak-ng-data" ]; then
    log "pruning espeak-ng-data to German"
    prune_espeak_data "$tmp/$stem/espeak-ng-data" "$OUT_DIR/espeak-ng-data"
  fi

  rm -rf "${OUT_DIR:?}/$id"
  mkdir -p "$OUT_DIR/$id"
  # Everything but the espeak data, which is shared across every Piper voice.
  find "$tmp/$stem" -maxdepth 1 -type f -exec cp {} "$OUT_DIR/$id/" \;
}

fetch_supertonic() {
  local id="$1" stem archive tmp
  stem="$(stem_for "$id")"
  archive="$(download "$stem" "$SHERPA_RELEASE/$stem.tar.bz2")"

  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' RETURN
  tar -xf "$archive" -C "$tmp"

  rm -rf "${OUT_DIR:?}/$id"
  mkdir -p "$OUT_DIR/$id"
  cp "$tmp/$stem"/* "$OUT_DIR/$id/"
}

# mls is the only medium-quality German voice with female speakers under a permissive licence
# (CC-BY 4.0, trained from scratch). sherpa-onnx does not republish it, so it comes from the Piper
# voices repo and needs its tokens.txt derived from the model's own phoneme_id_map.
fetch_mls() {
  local dir="$OUT_DIR/mls-medium"
  local stem="de_DE-mls-medium"
  local base="$PIPER_VOICES/de/de_DE/mls/medium/$stem.onnx"

  # The pristine download is kept in the cache and never modified, because the metadata step below
  # appends to the file and must not run twice on the same bytes.
  if [ ! -f "$CACHE/$stem.onnx" ]; then
    log "downloading mls-medium (73 MB, from piper-voices)"
    curl --fail --location --silent --show-error --output "$CACHE/$stem.onnx.part" "$base"
    mv "$CACHE/$stem.onnx.part" "$CACHE/$stem.onnx"
    curl --fail --location --silent --show-error --output "$CACHE/$stem.onnx.json" "$base.json"
    curl --fail --location --silent --show-error --output "$CACHE/$stem.MODEL_CARD" \
      "$PIPER_VOICES/de/de_DE/mls/medium/MODEL_CARD"
  fi

  rm -rf "${OUT_DIR:?}/mls-medium"
  mkdir -p "$dir"
  cp "$CACHE/$stem.onnx" "$dir/$stem.onnx"
  cp "$CACHE/$stem.onnx.json" "$dir/$stem.onnx.json"
  cp "$CACHE/$stem.MODEL_CARD" "$dir/MODEL_CARD"

  # Stock Piper voices carry none of the ONNX metadata sherpa-onnx reads, so it has to be added.
  python3 "$(dirname "$0")/piper_onnx_meta.py" "$dir/$stem.onnx" German

  # mls is a Piper voice like any other, so it shares the pruned espeak data. Make sure it exists
  # even when mls is fetched on its own.
  if [ ! -d "$OUT_DIR/espeak-ng-data" ]; then
    local tmp
    tmp="$(mktemp -d)"
    tar -xf "$(download vits-piper-de_DE-thorsten-medium-int8 \
      "$SHERPA_RELEASE/vits-piper-de_DE-thorsten-medium-int8.tar.bz2")" -C "$tmp"
    log "pruning espeak-ng-data to German"
    prune_espeak_data "$tmp/vits-piper-de_DE-thorsten-medium-int8/espeak-ng-data" \
      "$OUT_DIR/espeak-ng-data"
    rm -rf "$tmp"
  fi
}

mkdir -p "$CACHE" "$OUT_DIR"

requested=("$@")
if [ ${#requested[@]} -eq 0 ]; then
  read -r -a requested <<<"$SHIPPING_SET"
elif [ "${requested[0]}" = "all" ]; then
  read -r -a requested <<<"$AUDITION_SET"
fi

for id in "${requested[@]}"; do
  case "$id" in
    supertonic-3) fetch_supertonic "$id" ;;
    mls-medium)   fetch_mls ;;
    *)            fetch_piper "$id" ;;
  esac
  log "ready: $OUT_DIR/$id"
done

printf 'fetch-voices: %s\n' "$(du -sh "$OUT_DIR" | cut -f1) in $OUT_DIR" >&2
