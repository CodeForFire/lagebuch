# Contributing to Lagebuch

Thanks for your interest in contributing! Lagebuch is offline-first incident
documentation (**Einsatzdokumentation**) for fire brigades — every
contribution, from a typo fix to a new feature, is welcome.

## Your first contribution

New to the project? Start with a
[good first issue](https://github.com/CodeForFire/lagebuch/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22).
Each one names the files involved, the existing pattern to copy, and the command
that verifies your change, so you do not have to reverse-engineer the codebase
first. They range from a fifteen-minute typography fix to a page of
documentation.

**Claiming one:** comment on the issue and it is yours. If no pull request
appears within seven days, it goes back in the pool — no explanation needed;
life happens. Please do not open a pull request for an issue someone else has
claimed.

**You do not have to write C#.** Several issues are documentation, and some are
about German wording on screens used under time pressure. If you are a
firefighter rather than a developer, the wording issues are the ones where your
judgement beats ours. Propose the text in a comment and someone will help with
the markup.

Stuck? Ask in the issue, or in
[Fragen & Antworten](https://github.com/CodeForFire/lagebuch/discussions/categories/fragen-antworten).
Asking early is cheaper for everyone than a pull request that goes the wrong
way. German and English are both fine there.

Where the project is going is written up in [`ROADMAP.md`](ROADMAP.md).

## Hacktoberfest

Lagebuch takes part in Hacktoberfest, and we would rather you leave with one
merged change you are pleased with than four that get reverted.

What counts here:

- **Something a user or a maintainer would notice.** A fixed bug, a test that
  pins real behaviour, a page of documentation that did not exist. The
  `good first issue` list is exactly this kind of work.
- **The normal rules, unchanged.** Signed commits (SSH or GPG), DCO sign-off
  (`git commit -s`), Conventional Commit subjects, a `CHANGELOG.md` entry for
  anything under `src/`, and green CI. These are enforced automatically, not
  waived for October.

What does not count, and will be labelled `spam` or `invalid`:

- reformatting, whitespace, or line-ending changes nobody asked for
- typo fixes in files you did not otherwise touch, submitted in bulk
- adding yourself to a contributors list
- automated or unreviewed changes generated wholesale by a tool

Coding agents are welcome; see [AI-assisted contributions](#ai-assisted-contributions)
below. Review what they write before you send it — a pull request you cannot
explain is one nobody can merge.

## Development setup

- [.NET SDK](https://dotnet.microsoft.com/download/dotnet) — the exact SDK
  version is pinned via [`global.json`](global.json) (currently 10.x); the app
  projects themselves target `net10.0`
- Android workload for the companion-app head (one-time, per machine):

  ```bash
  dotnet workload install android
  ```

  If your JDK is newer than JDK 21, building the Android head will fail
  (`Microsoft.Android.Sdk` rejects it — error `XA0030`); use the
  Docker-based build in [`docker/`](docker/) instead.

Build and test:

```bash
dotnet build
dotnet test
```

Run the desktop app:

```bash
dotnet run --project src/LageBuch.App/LageBuch.App.csproj
```

The repo's `Makefile` wraps these and the Android/packaging commands — run
`make` for the list. Note that `make build` and `make test` use
`LageBuch.Desktop.slnf`, the solution without the Android head, so they work
on machines whose JDK is too new to build it; `make build-all` covers
everything.

## Workflow

- **Feature branches only** — open a pull request against `main`; direct
  pushes to `main` are not used.
- Keep pull requests focused: one logical change per PR.
- CI must pass (build + tests) before a PR can be merged.
- Every commit must be **cryptographically signed** and carry a DCO
  `Signed-off-by` trailer — see [Commit conventions](#commit-conventions).
  Both are enforced on `main` by branch protection, so a branch carrying one
  unsigned commit cannot be merged even when CI is green.

## Commit conventions

Every commit must meet three requirements:

1. a [Conventional Commits](https://www.conventionalcommits.org/) subject line,
2. a DCO `Signed-off-by` trailer — `git commit -s`,
3. a cryptographic signature, SSH or GPG — `git commit -S`.

```bash
git commit -s -S -m "feat(atemschutz): add pressure interval warning"
```

Subject line starts with a type prefix — `feat:`, `fix:`, `docs:`, `style:`,
`refactor:`, `perf:`, `test:`, `build:`, `ci:`, or `chore:` — optionally
scoped, e.g. `fix(backgroundjob):`.

The sign-off and the signature are different things, and you need both. The
trailer is a statement about **rights**: it certifies you may submit the change
under the project's licence (see [Licence](#licence)). The signature is a
statement about **identity**: it proves the commit came from you. Neither
implies the other.

### Signing your commits

Set this up once and `git commit -s` is all you ever type again.

**SSH** — recommended, because you already have a key:

```bash
git config --global gpg.format ssh
git config --global user.signingkey ~/.ssh/id_ed25519.pub
git config --global commit.gpgsign true
```

Then upload that same public key to GitHub **a second time**, as a signing key:
*Settings → SSH and GPG keys → New SSH key →* **Key type: Signing Key**. This
is the step people miss. An authentication key does not verify signatures, so
without it your commits are signed perfectly well and still show up as
*Unverified* — and branch protection turns them away.

**GPG** works too if you prefer it or already have a key; follow GitHub's
[Telling Git about your signing key](https://docs.github.com/en/authentication/managing-commit-signature-verification/telling-git-about-your-signing-key).

Check locally before you push:

```bash
git log --show-signature -1
```

and confirm the commit carries a green **Verified** badge once the pull request
is open. That badge, not the local output, is what branch protection reads.

### Fixing a branch you already pushed

If you learn about this after the fact, or a commit slipped through unsigned,
rewrite the branch rather than stacking a commit on top:

```bash
git rebase --signoff --gpg-sign origin/main
git push --force-with-lease
```

That gives every commit on the branch both a fresh signature and a sign-off
trailer. Force-pushing your own pull-request branch is expected here and does
not lose review comments.

## Pull requests

A PR template with a short checklist will guide you:

- Conventional Commits, DCO signed off and cryptographically signed
- `dotnet build` / `dotnet test` green locally
- **UI changes**: include before/after screenshots so reviewers can see the
  change without running the app
- **Changes under `src/`**: add an entry under `CHANGELOG.md`'s
  `## [Unreleased]` section — CI checks for this and fails the PR otherwise
- No real master data committed (see below)

## Master data and PII

Dropdown contents (roles, call signs, brigades, personnel) are treated as
personally identifying data and must **never** be committed to this repository.
`seed-source/` and `*.masterdata.json` are gitignored for exactly this reason;
the only tracked master data is the anonymised
[`docs/master-data.example.json`](docs/master-data.example.json). If you attach
screenshots, use fictional data.

## Reporting issues

Please use the issue templates:

- **Bug report** — include the version and platform, steps to reproduce,
  expected vs actual behaviour
- **Feature request** — describe the problem you are trying to solve first;
  the solution second

Security vulnerabilities do **not** belong in public issues — see
[`SECURITY.md`](SECURITY.md).

## AI-assisted contributions

Contributions made with coding agents are welcome, provided they follow the
same conventions above; [`AGENTS.md`](AGENTS.md) documents them in the form
agents consume.

## Licence

By contributing, you agree that your contributions will be licensed under the
[MIT licence](LICENSE). Signing off your commits (`git commit -s`) certifies
that you wrote the change or otherwise have the right to submit it under the
project's licence.
