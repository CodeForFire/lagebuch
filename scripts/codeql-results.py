#!/usr/bin/env python3
"""Trim CodeQL's SARIF to the code we write, and gate a branch on it locally.

CodeQL arrives on a pull request as review comments, minutes after the push, and
the code-scanning ruleset blocks the merge until each one is fixed. The same
handful of queries kept coming back that way. This script lets `make codeql`
run the identical analysis before the push, and keeps CI and the local run
reading the results the same way:

    filter  --config YML IN OUT
        Drop results in generated code (anything under an obj/ directory: the
        Regex and xunit source generators' output, which nobody edits) and
        results of a query codeql-config.yml excludes. CI runs it between
        analysis and upload, so generated code never opens an alert on main.

    check   --base REF SARIF
        List the results on lines changed since the merge base of REF and HEAD,
        working tree included, and exit 1 if there is one. A result on an
        untouched line is old debt and only counted, the way the ruleset only
        blocks alerts a pull request introduces.

    --self-test
        Check the filter and the diff matching against canned input.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

# "@@ -12,3 +14,5 @@": the new side starts at line 14 and spans 5 lines.
# A count of 0 is a pure deletion; an omitted count means 1.
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")

# "      id: cs/linq/missed-where" under an `exclude:` in codeql-config.yml.
EXCLUDED_ID = re.compile(r"^\s*id:\s*(\S+)\s*$")

GENERATED = re.compile(r"(^|/)obj/")


def excluded_ids(config: str) -> set[str]:
    """The query ids codeql-config.yml excludes; it holds nothing but exclusions."""
    return {m.group(1) for line in config.splitlines() if (m := EXCLUDED_ID.match(line))}


def location(result: dict) -> tuple[str, int]:
    """A result's file, relative to the source root, and its first line."""
    for loc in result.get("locations", []):
        physical = loc.get("physicalLocation", {})
        uri = physical.get("artifactLocation", {}).get("uri")
        if uri:
            return uri, physical.get("region", {}).get("startLine", 0)
    return "", 0


def keep(result: dict, excluded: set[str]) -> bool:
    path, _ = location(result)
    return result.get("ruleId") not in excluded and not GENERATED.search(path)


def filter_sarif(sarif: dict, excluded: set[str]) -> int:
    """Drop generated and excluded results in place; returns how many went."""
    dropped = 0
    for run in sarif.get("runs", []):
        results = run.get("results", [])
        kept = [r for r in results if keep(r, excluded)]
        dropped += len(results) - len(kept)
        run["results"] = kept
    return dropped


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


def on_changed_line(result: dict, ranges: dict[str, list[tuple[int, int]]]) -> bool:
    path, line = location(result)
    return any(first <= line <= last for first, last in ranges.get(path, []))


def message(result: dict) -> str:
    path, line = location(result)
    text = result.get("message", {}).get("text", "").splitlines()[0]
    return f"{path}:{line}: {result.get('ruleId')}: {text}"


def git(*args: str) -> str:
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout


def cmd_filter(args: argparse.Namespace) -> int:
    excluded = excluded_ids(Path(args.config).read_text(encoding="utf-8"))
    sarif = json.loads(Path(args.input).read_text(encoding="utf-8"))
    dropped = filter_sarif(sarif, excluded)
    Path(args.output).write_text(json.dumps(sarif), encoding="utf-8")
    print(f"Dropped {dropped} result(s) in generated code or of an excluded query.")
    return 0


def cmd_check(args: argparse.Namespace) -> int:
    merge_base = git("merge-base", args.base, "HEAD").strip()
    ranges = changed_lines(git("diff", "--no-color", "--no-ext-diff", "-U0", merge_base))
    results = [r for run in json.loads(Path(args.sarif).read_text(encoding="utf-8")).get("runs", [])
               for r in run.get("results", [])]
    new = [r for r in results if on_changed_line(r, ranges)]
    for result in new:
        print(message(result))
    print(f"{len(new)} CodeQL result(s) on lines changed since {args.base}; "
          f"{len(results) - len(new)} elsewhere, not gated.")
    return 1 if new else 0


def self_test() -> int:
    def result(rule: str, uri: str, line: int) -> dict:
        return {"ruleId": rule, "message": {"text": f"{rule} here\nmore"},
                "locations": [{"physicalLocation": {"artifactLocation": {"uri": uri},
                                                    "region": {"startLine": line}}}]}

    config = "query-filters:\n  - exclude:\n      id: cs/linq/missed-where\n"
    excluded = excluded_ids(config)
    assert excluded == {"cs/linq/missed-where"}, excluded

    sarif = {"runs": [{"results": [
        result("cs/useless-upcast", "src/P/A.cs", 3),
        result("cs/linq/missed-where", "src/P/A.cs", 4),
        result("cs/nested-if-statements", "src/P/obj/Release/net10.0/generated/R.g.cs", 9),
        result("cs/path-combine", "src/Pobj/B.cs", 1),
    ]}]}
    assert filter_sarif(sarif, excluded) == 2
    assert [r["ruleId"] for r in sarif["runs"][0]["results"]] == ["cs/useless-upcast", "cs/path-combine"]

    diff = "\n".join([
        "diff --git a/src/P/A.cs b/src/P/A.cs",
        "--- a/src/P/A.cs",
        "+++ b/src/P/A.cs",
        "@@ -1,0 +2,2 @@",
        "@@ -9 +11,0 @@",
        "diff --git a/src/P/Gone.cs b/src/P/Gone.cs",
        "--- a/src/P/Gone.cs",
        "+++ /dev/null",
        "@@ -1,3 +0,0 @@",
    ])
    ranges = changed_lines(diff)
    assert ranges == {"src/P/A.cs": [(2, 3)]}, ranges
    assert on_changed_line(result("x", "src/P/A.cs", 3), ranges)
    assert not on_changed_line(result("x", "src/P/A.cs", 4), ranges)
    assert not on_changed_line(result("x", "src/P/B.cs", 2), ranges)
    assert not on_changed_line({"ruleId": "x"}, ranges)
    assert message(result("cs/x", "src/P/A.cs", 3)) == "src/P/A.cs:3: cs/x: cs/x here"
    print("codeql-results self-test passed")
    return 0


def main() -> int:
    if sys.argv[1:] == ["--self-test"]:
        return self_test()
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    filt = sub.add_parser("filter", help="drop generated-code and excluded results")
    filt.add_argument("--config", required=True)
    filt.add_argument("input")
    filt.add_argument("output")
    filt.set_defaults(run=cmd_filter)
    check = sub.add_parser("check", help="fail on results on changed lines")
    check.add_argument("--base", required=True)
    check.add_argument("sarif")
    check.set_defaults(run=cmd_check)
    args = parser.parse_args()
    return args.run(args)


if __name__ == "__main__":
    sys.exit(main())
