#!/usr/bin/env python3
"""Warn when a changelog fragment looks like it was written in German.

`CHANGELOG.md` is English — see CONTRIBUTING.md. That rule cannot be enforced
by a language detector, because every correct entry about this app carries
German Fachbegriffe: Atemschutz, Stammdaten, Einsatzdaten, Messreihe, Wohnung,
Funkrufname. A detector strict enough to gate on would fail hardest on the
entries that are right.

So this looks only for German *function* words — never nouns — inside the prose,
with inline code, links and URLs removed first, since a filename such as
`docs/datenschutz-und-sicherheit.md` and a UI name such as the "Über" dialog are
both correct in an English entry. Two distinct hits are needed: "über" alone, or
a stray "der", is not evidence.

It emits GitHub workflow warnings and always exits 0. The `changelog entry`
job's conclusion keeps its one meaning — a fragment exists — and a reviewer
decides what the warning is worth.

Usage: changelog-language-hint.py <fragment> [<fragment> ...]
"""

from __future__ import annotations

import re
import sys

# German function words with no English homograph. "die", "mit", "war", "hat",
# "an", "in", "am" and "so" are deliberately absent: they read as English too
# often to carry any signal.
GERMAN_FUNCTION_WORDS = (
    "der das dass den dem des und nicht statt wird werden wurde jetzt noch "
    "beim vom zum zur eine einen einem einer eines ein ist sind kann soll "
    "mehr schon wenn weil über für auch nur aber oder sich kein keine nach "
    "aus als wie sie ihre ihr diese dieser dieses haben hatte muss müssen"
).split()

WORD = re.compile(
    r"\b(" + "|".join(GERMAN_FUNCTION_WORDS) + r")\b",
    re.IGNORECASE,
)

# Prose only: a fenced block, an inline code span, a markdown link target and a
# bare URL are all places a German word is correct in an English entry.
NOISE = re.compile(
    r"```.*?```|`[^`]*`|\]\([^)]*\)|https?://\S+",
    re.DOTALL,
)

# Two distinct words, not two occurrences of one: a single "über" is noise.
THRESHOLD = 2


def german_words_in(text: str) -> list[str]:
    prose = NOISE.sub(" ", text)
    return sorted({match.group(1).lower() for match in WORD.finditer(prose)})


def main(paths: list[str]) -> int:
    for path in paths:
        try:
            with open(path, encoding="utf-8") as handle:
                text = handle.read()
        except OSError as error:
            print(f"::warning::could not read {path}: {error}")
            continue

        hits = german_words_in(text)
        if len(hits) >= THRESHOLD:
            print(
                f"::warning file={path}::This changelog entry looks German "
                f"({', '.join(hits)}). CHANGELOG.md is English — see "
                "CONTRIBUTING.md. Ignore this if the entry really is English."
            )

    # Advisory by design: never change the job's conclusion.
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
