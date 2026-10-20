#!/usr/bin/env python3
"""Score a Stryker run on the lines a branch changed, not on whole files.

Stryker can only narrow a run to files: `--since` and `--mutate` both pick
files, and the score it reports covers every mutant in them. A one-line fix to
a file with old survivors then fails the pull-request gate on debt the fix never
touched (#638, #639, #666 and #667 all did). This script narrows the score to
the lines themselves:

    files  --base REF --project-dir DIR
        Print the .cs files under DIR that changed since the merge base of REF
        and HEAD, working tree included, one per line, relative to DIR. The
        Makefile turns them into Stryker `--mutate` patterns.

    score  --report JSON --base REF --project-dir DIR --threshold N [--summary MD]
        Read Stryker's mutation-report.json, keep the mutants whose span
        overlaps a changed line, and score them the way Stryker does:
        (killed + timeout) / (killed + timeout + survived + no coverage).
        Exit 1 below N. The survivors on changed lines are listed; the other
        survivors in the touched files are listed as well, but only for
        information: they never fail the run.

    --self-test
        Check the diff parser and the scoring against canned input.

Deleted lines have no mutants, so a hunk that only removes code changes no
score. A run with no mutant on a changed line passes: there is nothing to judge.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

# "@@ -12,3 +14,5 @@": the new side starts at line 14 and spans 5 lines.
# A count of 0 is a pure deletion; an omitted count means 1.
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")

DETECTED = ("Killed", "Timeout")
UNDETECTED = ("Survived", "NoCoverage")


@dataclass
class Score:
    detected: int = 0
    undetected: int = 0
    survivors: list[tuple[str, dict]] = field(default_factory=list)
    other_survivors: list[tuple[str, dict]] = field(default_factory=list)

    @property
    def valid(self) -> int:
        return self.detected + self.undetected

    @property
    def percent(self) -> float | None:
        return 100.0 * self.detected / self.valid if self.valid else None


def changed_lines(diff: str) -> dict[str, list[tuple[int, int]]]:
    """Map each file in a `git diff -U0` to the inclusive line ranges it adds."""
    ranges: dict[str, list[tuple[int, int]]] = {}
    path: str | None = None
    for line in diff.splitlines():
        if line.startswith("+++ "):
            target = line[4:]
            path = target[2:] if target.startswith("b/") else None
            continue
        match = HUNK.match(line)
        if match and path is not None:
            start = int(match.group(1))
            count = int(match.group(2)) if match.group(2) is not None else 1
            if count:
                ranges.setdefault(path, []).append((start, start + count - 1))
    return ranges


def score(report: dict, root: Path, ranges: dict[str, list[tuple[int, int]]]) -> Score:
    """Score the mutants whose span overlaps a changed line."""
    result = Score()
    for file_name, entry in report["files"].items():
        path = Path(file_name)
        relative = (path.relative_to(root) if path.is_absolute() else path).as_posix()
        if relative not in ranges:
            continue
        for mutant in entry["mutants"]:
            status = mutant["status"]
            if status not in DETECTED + UNDETECTED:
                continue
            first = mutant["location"]["start"]["line"]
            last = mutant["location"]["end"]["line"]
            on_change = any(first <= end and start <= last for start, end in ranges[relative])
            if not on_change:
                if status in UNDETECTED:
                    result.other_survivors.append((relative, mutant))
                continue
            if status in DETECTED:
                result.detected += 1
            else:
                result.undetected += 1
                result.survivors.append((relative, mutant))
    return result


def git(*args: str) -> str:
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout


def diff_since(base: str, project_dir: str, *extra: str) -> str:
    merge_base = git("merge-base", base, "HEAD").strip()
    return git("diff", "--no-color", "--no-ext-diff", *extra, merge_base, "--", f"{project_dir}/*.cs")


def survivor_rows(survivors: list[tuple[str, dict]]) -> list[str]:
    rows = ["| Where | Mutation | Status |", "|---|---|---|"]
    for path, mutant in sorted(survivors, key=lambda s: (s[0], s[1]["location"]["start"]["line"])):
        line = mutant["location"]["start"]["line"]
        replacement = mutant.get("replacement", "").replace("|", "\\|").replace("\n", " ")
        if len(replacement) > 80:
            replacement = replacement[:77] + "..."
        rows.append(f"| `{path}:{line}` | {mutant['mutatorName']}: `{replacement}` | {mutant['status']} |")
    return rows


def render(result: Score, threshold: float) -> str:
    lines = ["### Mutation score on changed lines", ""]
    if result.percent is None:
        lines.append("No mutant on a changed line, so there is nothing to score.")
    else:
        verdict = "passes" if result.percent >= threshold else "fails"
        lines.append(
            f"**{result.percent:.1f} %** ({result.detected} of {result.valid} mutants caught), "
            f"which {verdict} the {threshold:g} % threshold."
        )
    if result.survivors:
        lines += ["", "Surviving on changed lines — each needs a test, or a `// Stryker disable once` with a reason:", ""]
        lines += survivor_rows(result.survivors)
    if result.other_survivors:
        lines += [
            "",
            "<details><summary>Older survivors in the touched files "
            f"({len(result.other_survivors)}, not counted)</summary>",
            "",
        ]
        lines += survivor_rows(result.other_survivors)
        lines += ["", "</details>"]
    return "\n".join(lines) + "\n"


def cmd_files(args: argparse.Namespace) -> int:
    for name in diff_since(args.base, args.project_dir, "--name-only", "--diff-filter=d").splitlines():
        print(Path(name).relative_to(args.project_dir).as_posix())
    return 0


def cmd_score(args: argparse.Namespace) -> int:
    root = Path(git("rev-parse", "--show-toplevel").strip())
    ranges = changed_lines(diff_since(args.base, args.project_dir, "-U0"))
    report = json.loads(Path(args.report).read_text(encoding="utf-8"))
    result = score(report, root, ranges)
    text = render(result, args.threshold)
    print(text)
    if args.summary:
        Path(args.summary).write_text(text, encoding="utf-8")
    return 1 if result.percent is not None and result.percent < args.threshold else 0


def self_test() -> int:
    diff = "\n".join([
        "diff --git a/src/P/A.cs b/src/P/A.cs",
        "--- a/src/P/A.cs",
        "+++ b/src/P/A.cs",
        "@@ -10,0 +11,2 @@ class A",
        "@@ -20 +22 @@ class A",
        "@@ -30,4 +33,0 @@ class A",
        "diff --git a/src/P/Gone.cs b/src/P/Gone.cs",
        "--- a/src/P/Gone.cs",
        "+++ /dev/null",
        "@@ -1,5 +0,0 @@",
    ])
    ranges = changed_lines(diff)
    assert ranges == {"src/P/A.cs": [(11, 12), (22, 22)]}, ranges

    def mutant(status: str, first: int, last: int | None = None) -> dict:
        return {
            "mutatorName": "Equality mutation",
            "replacement": "a != b",
            "status": status,
            "location": {"start": {"line": first, "column": 1}, "end": {"line": last or first, "column": 9}},
        }

    root = Path("/repo")
    report = {"files": {
        "/repo/src/P/A.cs": {"mutants": [
            mutant("Killed", 11),
            mutant("Timeout", 12),
            mutant("Survived", 22),
            mutant("NoCoverage", 5, 30),   # a block spanning the change counts
            mutant("CompileError", 11),    # neither caught nor missed
            mutant("Ignored", 22),
            mutant("Survived", 40),        # old debt: listed, not counted
        ]},
        "/repo/src/P/Untouched.cs": {"mutants": [mutant("Survived", 11)]},
    }}
    result = score(report, root, ranges)
    assert (result.detected, result.undetected) == (2, 2), result
    assert result.percent == 50.0, result.percent
    assert [m["location"]["start"]["line"] for _, m in result.other_survivors] == [40]
    assert "fails the 80 %" in render(result, 80), render(result, 80)
    assert "passes the 50 %" in render(result, 50)

    empty = score({"files": {"/repo/src/P/A.cs": {"mutants": [mutant("Survived", 40)]}}}, root, ranges)
    assert empty.percent is None
    assert "nothing to score" in render(empty, 80)
    print("mutation-diff-score: self-test passed")
    return 0


def main() -> int:
    if sys.argv[1:] == ["--self-test"]:
        return self_test()
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    files = sub.add_parser("files", help="list the changed .cs files to mutate")
    files.add_argument("--base", required=True)
    files.add_argument("--project-dir", required=True)
    files.set_defaults(run=cmd_files)
    scorer = sub.add_parser("score", help="score the mutants on changed lines")
    scorer.add_argument("--report", required=True)
    scorer.add_argument("--base", required=True)
    scorer.add_argument("--project-dir", required=True)
    scorer.add_argument("--threshold", type=float, required=True)
    scorer.add_argument("--summary")
    scorer.set_defaults(run=cmd_score)
    args = parser.parse_args()
    return args.run(args)


if __name__ == "__main__":
    sys.exit(main())
