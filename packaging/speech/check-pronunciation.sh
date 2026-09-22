#!/usr/bin/env bash
#
# Shows how espeak-ng will pronounce a phrase in German, as IPA.
#
# The Piper voices phonemize through espeak-ng, so this prints roughly the text-to-phoneme step the
# app performs. It turns "that abbreviation sounds wrong" into something you can read.
#
# INDICATIVE, NOT AUTHORITATIVE. This runs the espeak-ng on PATH; the app uses the copy compiled
# into libsherpa-onnx-c-api.so, and the two versions differ (system 1.52.0 here against
# espeak-ng-data dated Nov 2023 in the bundle). That gap is not theoretical: "Angriffstrupp" reads
# as a clean /s/+/t/ below and is nonetheless heard as "Angriffschtrupp" from the model. So when
# this disagrees with your ear, the ear wins -- the tool is for catching a spelling that is broken
# on its face, like "Tseh" coming out as two syllables.
#
# It earned its keep immediately: the letter C had been written "Tseh", which looks like /tseː/ but
# is actually /tˈeːzˈeː/ -- two syllables, "te-se" -- so CSA came out as "Te-Se Ess Ah". German has
# no "ts" onset; the letter z already is /ts/, so the right spelling is "Zeh".
#
# Usage:
#   packaging/speech/check-pronunciation.sh "Zeh Ess Ah"
#   packaging/speech/check-pronunciation.sh --letters        # the whole letter table
#   packaging/speech/check-pronunciation.sh --abbreviations  # every abbreviation, as normalized
#
# Needs espeak-ng on PATH (Debian/Ubuntu: apt install espeak-ng). It is a developer tool only --
# the app uses the copy compiled into libsherpa-onnx-c-api.so, not this one.

set -euo pipefail

VOICE="${ESPEAK_VOICE:-de}"

command -v espeak-ng >/dev/null 2>&1 || {
  echo "check-pronunciation: espeak-ng not found (apt install espeak-ng)" >&2
  exit 1
}

say() { printf '%-22s %s\n' "$1" "$(espeak-ng -v "$VOICE" -q --ipa "$1" 2>/dev/null | tr -d '\n' | sed 's/^ *//')"; }

case "${1:---help}" in
  --letters)
    # The German letter names as SpeechAbbreviations.GermanLetters spells them. Each line should
    # read as the letter itself: Zeh -> tsˈeː, not tˈeːzˈeː.
    for w in Ah Beh Zeh Deh Eh Eff Geh Hah Ih Jott Kah Ell Emm Enn Oh Peh Kuh Err Ess Teh Uh Fau Weh Iks Ypsilon Zett; do
      say "$w"
    done
    ;;
  --abbreviations)
    for w in "Ih Ell Ess" "Zeh Ess Ah" "Ell Peh Ah" "Peh Ah" "Ah Geh Teh" "Deh Ell Kah" \
             "Eh Ell Weh" "Ell Eff" "Eff Eff Beh" "Zeh Oh" "Peh Peh Emm"; do
      say "$w"
    done
    ;;
  --help | -h)
    sed -n '3,20p' "$0" | sed 's/^# \{0,1\}//'
    ;;
  *)
    say "$*"
    ;;
esac
