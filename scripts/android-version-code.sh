#!/usr/bin/env bash
#
# Prints the Android versionCode for a version string.
#
#   scripts/android-version-code.sh 0.6.1        -> 601999
#   scripts/android-version-code.sh 0.6.1-rc.1   -> 601301
#   scripts/android-version-code.sh --self-test
#
# This is the single source of truth for the number. Both the release workflow's
# `version` job and the Makefile's `aab` target call it, because the two must not
# be able to disagree: Google Play refuses any upload whose versionCode is not
# strictly greater than every versionCode it has already seen, on any track,
# permanently. A number that is wrong once is burned for the life of the listing,
# so it is derived from the tag and never typed.
#
#   MAJOR*10000000 + MINOR*100000 + PATCH*1000 + offset
#   offset:  alpha.N -> 100+N   beta.N -> 200+N   rc.N -> 300+N   final -> 999
#
# Play accepts one integer where semver has four fields, so the offset is what
# encodes the prerelease, and it faces the ordering problem the .deb solves with
# a tilde: a prerelease must sort BELOW the final release of the same version.
# Reserving 999 for the final leaves 699 unused offsets per version, so a future
# suffix scheme can be added without disturbing any number already published.
#
# The ceiling is deliberate. 209.99.99 maps to 2099999999, one below Play's limit
# of 2100000000, and a version above it fails rather than silently wrapping into
# a number that sorts below its predecessor.

set -euo pipefail

usage() {
    sed -n '3,8p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

version_code() {
    local version="$1"

    # Validated up front, as a whole. Letting the arithmetic below fail on a
    # non-numeric field would abort with "integer expression expected", which
    # says nothing about which tag is wrong or what shape was expected.
    if ! [[ "$version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(-(alpha|beta|rc)\.([0-9]+))?$ ]]; then
        echo "error: '$version' is not a version this scheme can order." >&2
        echo "       Expected MAJOR.MINOR.PATCH, optionally -alpha.N, -beta.N or -rc.N." >&2
        return 1
    fi

    local major="${BASH_REMATCH[1]}"
    local minor="${BASH_REMATCH[2]}"
    local patch="${BASH_REMATCH[3]}"
    local kind="${BASH_REMATCH[5]}"
    local number="${BASH_REMATCH[6]}"

    local offset
    case "$kind" in
        "")      offset=999 ;;
        alpha)   offset=$((100 + number)) ;;
        beta)    offset=$((200 + number)) ;;
        rc)      offset=$((300 + number)) ;;
    esac

    # 10#  forces base 10: a zero-padded field such as 08 would otherwise be read
    # as octal and either shift the result or fail outright.
    if [ "$((10#$major))" -gt 209 ] || [ "$((10#$minor))" -gt 99 ] || [ "$((10#$patch))" -gt 99 ]; then
        echo "error: '$version' does not fit the versionCode scheme (max 209.99.99)." >&2
        return 1
    fi
    if [ -n "$kind" ] && [ "$((10#$number))" -gt 99 ]; then
        echo "error: '$version' has a prerelease number above 99." >&2
        return 1
    fi

    echo $(( 10#$major * 10000000 + 10#$minor * 100000 + 10#$patch * 1000 + offset ))
}

self_test() {
    local failures=0

    expect() {
        local version="$1" want="$2" got
        got=$(version_code "$version" 2>/dev/null) || got="<rejected>"
        if [ "$got" = "$want" ]; then
            printf '  ok    %-16s %s\n' "$version" "$got"
        else
            printf '  FAIL  %-16s got %s, want %s\n' "$version" "$got" "$want"
            failures=$((failures + 1))
        fi
    }

    reject() {
        local version="$1" why="$2"
        if version_code "$version" >/dev/null 2>&1; then
            printf '  FAIL  %-16s accepted, expected rejection (%s)\n' "$version" "$why"
            failures=$((failures + 1))
        else
            printf '  ok    %-16s rejected (%s)\n' "$version" "$why"
        fi
    }

    echo "Worked examples"
    expect 0.6.0-alpha.1 600101
    expect 0.6.0-beta.1  600201
    expect 0.6.0-rc.1    600301
    expect 0.6.0-rc.2    600302
    expect 0.6.0         600999
    expect 0.6.1-rc.1    601301
    expect 0.6.1         601999
    expect 0.7.0         700999
    expect 1.0.0         10000999
    expect 209.99.99     2099999999

    # Every tag this project has ever cut, plus the ones it plausibly will, in
    # release order. The scheme is worthless if any pair inverts.
    echo
    echo "Strictly increasing over the project's release history"
    local previous=0 code
    local version
    for version in 0.1.0 0.2.0 0.3.0 0.4.0 0.4.1 0.5.0 \
                   0.6.0-rc.1 0.6.0-rc.2 0.6.0 \
                   0.6.1-rc.1 0.6.1 0.7.0 0.9.9 1.0.0 1.0.1-rc.1 1.0.1 2.0.0; do
        code=$(version_code "$version")
        if [ "$code" -le "$previous" ]; then
            printf '  FAIL  %-16s %s is not above the previous %s\n' "$version" "$code" "$previous"
            failures=$((failures + 1))
        fi
        previous=$code
    done
    [ "$failures" -eq 0 ] && echo "  ok    17 versions, each above the last"

    echo
    echo "Play's ceiling"
    if [ "$(version_code 209.99.99)" -lt 2100000000 ]; then
        echo "  ok    209.99.99 stays below 2100000000"
    else
        echo "  FAIL  209.99.99 reaches Play's limit"
        failures=$((failures + 1))
    fi

    echo
    echo "Rejected"
    reject 1.0.0-foo.1 "unknown suffix"
    reject 210.0.0     "major above the ceiling"
    reject 1.0.100     "patch above 99"
    reject 1.0         "not three fields"
    reject 1.0.0-rc    "suffix without a number"
    reject ""          "empty"
    reject v1.0.0      "leading v — the workflow strips it before calling this"

    echo
    if [ "$failures" -ne 0 ]; then
        echo "$failures check(s) failed."
        return 1
    fi
    echo "All checks passed."
}

case "${1-}" in
    --self-test) self_test ;;
    -h|--help)   usage ;;
    "")          usage >&2; exit 64 ;;
    *)           version_code "$1" ;;
esac
