#!/usr/bin/env bash
#
# Checks the Play Store listing text against the Console's limits and against the
# wording rules in docs/releasing.md. The Console truncates silently rather than
# warning, and a policy word in the description is a rejection, so both are worth
# catching before anyone pastes.
#
#   ./packaging/play/check-listing.sh        (or: make play-listing-check)

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
listing="$repo_root/docs/play/listing-de-DE.md"

[ -f "$listing" ] || { echo "error: listing missing: $listing" >&2; exit 1; }

fail=0

# Pulls the fenced block under the "## <heading>" that starts with $1. Counting blocks
# instead would hand back the next section's block once a pair of fences went missing,
# and check it as the wrong field. Inside a block a "## " line is text, not a heading;
# a block that is never closed yields nothing.
block() {
    awk -v want="$1" '
        !inblock && /^## / { insec = index($0, "## " want) == 1; inblock = 0; buf = ""; next }
        insec && /^```$/ { if (inblock) { printf "%s", buf; exit } inblock = 1; next }
        insec && inblock { buf = buf $0 "\n" }
    ' "$listing"
}

# Play counts characters, not bytes: "für" is three characters and four bytes.
check_len() {
    local name="$1" max="$2" text="$3"
    local len
    len=$(printf '%s' "$text" | wc -m)
    if [ "$len" -gt "$max" ]; then
        printf '  FAIL  %-22s %4d / %-4d characters (over by %d)\n' \
            "$name" "$len" "$max" "$((len - max))"
        fail=1
    else
        printf '  ok    %-22s %4d / %-4d characters\n' "$name" "$len" "$max"
    fi
}

name=$(block "App-Name")
short=$(block "Kurzbeschreibung")
full=$(block "Vollständige Beschreibung")

# A section whose fences were edited away yields an empty block, and an empty or
# blank block passes every length check, so absence is a failure of its own.
for field in "app name:$name" "short description:$short" "full description:$full"; do
    if ! [[ "${field#*:}" =~ [^[:space:]] ]]; then
        echo "error: the listing's ${field%%:*} block is missing or blank — check its \`\`\` fences." >&2
        exit 1
    fi
done

echo "Length limits"
check_len "app name" 30 "$name"
check_len "short description" 80 "$short"
check_len "full description" 4000 "$full"

# Words that imply official status, alerting, or a safety guarantee. Each one is a
# documented rejection risk for a fire-service app — see docs/releasing.md.
echo
echo "Forbidden wording (docs/releasing.md)"
# Unanchored on purpose, so a compound such as "Einsatzleitstelle" is caught too.
forbidden='amtlich|offiziell|behördlich|Leitstelle|Notruf|alarmier|Alarmierung|112'
# The disclaimer has to say what the app is NOT, so its exact phrases are exempt: it is the one
# place these words legitimately appear. Only those phrases are cut, never the line around
# them, so a claim sharing the disclaimer's line is still caught. The listing's own wording
# comes first, then the wording docs/releasing.md recommends.
haystack=$(printf '%s\n%s\n%s' "$name" "$short" "$full")
for phrase in 'Es alarmiert nicht' 'stellt keine Verbindung zu einer Leitstelle her' \
              'keine amtliche Anwendung' \
              'steht in keiner Verbindung zu einer Behörde oder einer Integrierten Leitstelle'; do
    haystack=${haystack//"$phrase"/}
done
if hits=$(printf '%s' "$haystack" | grep -oiE "$forbidden" | sort -u); [ -n "$hits" ]; then
    echo "  FAIL  found: $(printf '%s' "$hits" | tr '\n' ' ')"
    fail=1
else
    echo "  ok    none of: $forbidden"
fi

# Desktop-only capabilities must never be promised without their qualifier.
echo
echo "Desktop-only claims"
if printf '%s' "$full" | grep -qiE 'PDF-Bericht|freigeben'; then
    if printf '%s' "$full" | grep -qiE 'nur die Desktop-Version|Zwei Dinge macht nur'; then
        echo "  ok    mentioned, and qualified as desktop-only"
    else
        echo "  FAIL  PDF export / hosting mentioned without the desktop-only qualifier"
        fail=1
    fi
else
    echo "  ok    not mentioned"
fi

echo
if [ "$fail" -ne 0 ]; then
    echo "Listing is NOT ready to paste."
    exit 1
fi
echo "Listing is within every limit."
