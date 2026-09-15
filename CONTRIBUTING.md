# Contributing to Lagebuch

Thanks for your interest in contributing! Lagebuch is offline-first incident
documentation (**Einsatzdokumentation**) for fire brigades — every
contribution, from a typo fix to a new feature, is welcome.

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

## Commit conventions

All commits must follow [Conventional Commits](https://www.conventionalcommits.org/)
and be signed off (DCO, `Signed-off-by` trailer):

```bash
git commit -s -m "feat(atemschutz): add pressure interval warning"
```

Subject line starts with a type prefix — `feat:`, `fix:`, `docs:`, `style:`,
`refactor:`, `perf:`, `test:`, `build:`, `ci:`, or `chore:` — optionally
scoped, e.g. `fix(backgroundjob):`.

## Pull requests

A PR template with a short checklist will guide you:

- Conventional Commits with DCO sign-off
- `dotnet build` / `dotnet test` green locally
- **UI changes**: include before/after screenshots so reviewers can see the
  change without running the app
- **Changes under `src/`**: add a changelog entry as a new file in
  `changelog.d/` — CI checks for this and fails the PR otherwise
  (see [Changelog entries](#changelog-entries))
- No real master data committed (see below)

## Changelog entries

`CHANGELOG.md` is assembled, not edited. Each change brings its own file in
`changelog.d/`, so two pull requests never touch the same lines and can never
conflict over the changelog:

```
changelog.d/+<slug>.<type>.md
```

`<type>` is one of `added`, `changed`, `deprecated`, `removed`, `fixed` or
`security` — the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
sections. `<slug>` is a short kebab-case description. The leading `+` is
required: it is what keeps every filename unique, so two pull requests cannot
collide even when they describe the same ticket.

Put the entry in the file as plain prose, with no leading `- `, and reference
the issue or PR inline the way the existing entries do:

```markdown
The PDF's Aufgaben section marks a task "FÄLLIG" against the moment the export
was taken, instead of reading the wall clock while the document renders. (#302)
```

Write it for someone reading the release notes rather than the commit log: what
changed, and why it matters. Do not edit `CHANGELOG.md` directly — entries land
there when a release is cut.

### Cutting a release

[towncrier](https://towncrier.readthedocs.io) folds `changelog.d/` into
`CHANGELOG.md`:

```bash
pip install -r .github/requirements-changelog.txt
towncrier build --draft --version X.Y.Z   # preview; writes nothing
towncrier build --version X.Y.Z           # writes the section, removes the fragments
```

Two things stay manual afterwards: the summary paragraph under the new heading,
if the release deserves one, and the link definitions at the bottom of the file
(repoint `[Unreleased]` at the new tag and add an `[X.Y.Z]` line). Open that as
its own pull request; pushing the `vX.Y.Z` tag once it merges is what triggers
`release.yml`.

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
