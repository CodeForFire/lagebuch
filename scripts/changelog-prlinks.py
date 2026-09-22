#!/usr/bin/env python3
"""Prepend each changelog fragment with a link to the pull request that added it.

Fragments in changelog.d/ are named `+<slug>.<type>.md` — towncrier calls these
*orphans*, and an orphan carries no issue number, so towncrier cannot render a
link for it. The number could not be written into the fragment by hand either:
the fragment is authored in the pull request that does not have a number yet.

It is recoverable from git instead. After a squash-merge there is exactly one
commit that adds a given fragment, and its subject ends in `(#NNN)`. This script
resolves that number for every fragment and rewrites the fragment in place as

    [#442](https://github.com/CodeForFire/lagebuch/pull/442) - Entry text.

so that plain `towncrier build` renders the bullet in the form CHANGELOG.md
uses. Run it immediately before `towncrier build`; see docs/releasing.md.

Rewriting in place is safe because `towncrier build` deletes the fragments in
the same breath, on a release branch that exists only until the release pull
request merges.

Exit status is 1 if any fragment could not be resolved, so a release is never
cut with a silently unlinked entry.
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

FRAGMENT_DIR = Path("changelog.d")

# The six Keep a Changelog types, mirroring towncrier.toml.
TYPES = ("added", "changed", "deprecated", "removed", "fixed", "security")

# The trailing `(#NNN)` a squash-merge subject carries, e.g.
# "fix(co-messung): Wohnungen entfernen fragt, welche (#442)".
PR_IN_SUBJECT = re.compile(r"\(#(\d+)\)\s*$")

# git@github.com:owner/repo.git, https://github.com/owner/repo.git, and the
# ssh:// and .git-less spellings of both.
REMOTE_SLUG = re.compile(r"[:/]([^/:]+/[^/]+?)(?:\.git)?/?$")

# A fragment this script has already processed. Recognising it keeps a re-run
# after a `--draft` rehearsal from prefixing the link twice.
ALREADY_LINKED = re.compile(r"^\s*\[#\d+\]\(")


def run_git(*args: str) -> str:
    """Return stdout of a git command, or "" if git itself fails."""
    try:
        result = subprocess.run(
            ("git", *args),
            capture_output=True,
            text=True,
            check=True,
        )
    except (subprocess.CalledProcessError, OSError):
        return ""
    return result.stdout.strip()


def repo_slug() -> str | None:
    """owner/repo for the `origin` remote, so a fork does not link upstream."""
    url = run_git("remote", "get-url", "origin")
    if not url:
        return None
    match = REMOTE_SLUG.search(url)
    return match.group(1) if match else None


def pull_request_for(fragment: Path) -> int | None:
    """The pull request number of the commit that added this fragment."""
    subject = run_git(
        "log",
        "--diff-filter=A",
        "--first-parent",
        "-1",
        "--format=%s",
        "--",
        str(fragment),
    )
    if not subject:
        return None
    match = PR_IN_SUBJECT.search(subject)
    return int(match.group(1)) if match else None


def fragments() -> list[Path]:
    """Every orphan fragment, sorted. Numbered fragments are towncrier's job."""
    found = [
        path
        for change_type in TYPES
        for path in FRAGMENT_DIR.glob(f"+*.{change_type}.md")
    ]
    return sorted(found)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Prepend a pull request link to every changelog fragment.",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="report what would change without writing anything",
    )
    args = parser.parse_args()

    if not FRAGMENT_DIR.is_dir():
        print(f"error: {FRAGMENT_DIR}/ not found — run from the repository root.")
        return 1

    slug = repo_slug()
    if slug is None:
        print("error: could not read owner/repo from the 'origin' remote.")
        return 1

    unresolved: list[Path] = []
    linked = skipped = 0

    for fragment in fragments():
        text = fragment.read_text(encoding="utf-8")
        if ALREADY_LINKED.match(text):
            skipped += 1
            continue

        number = pull_request_for(fragment)
        if number is None:
            unresolved.append(fragment)
            continue

        link = f"[#{number}](https://github.com/{slug}/pull/{number})"
        if args.check:
            print(f"would link {fragment} -> #{number}")
        else:
            fragment.write_text(f"{link} - {text.lstrip()}", encoding="utf-8")
        linked += 1

    for fragment in unresolved:
        print(
            f"warning: no pull request found for {fragment} — it is either "
            "uncommitted, or the commit that added it has no '(#NNN)' subject. "
            "Commit the fragment, or add the link by hand.",
            file=sys.stderr,
        )

    verb = "would link" if args.check else "linked"
    print(f"{verb} {linked}, already linked {skipped}, unresolved {len(unresolved)}")
    return 1 if unresolved else 0


if __name__ == "__main__":
    sys.exit(main())
