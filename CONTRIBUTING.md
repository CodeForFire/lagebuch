# Contributing to Lagebuch

Thanks for your interest in contributing! Lagebuch is offline-first incident
documentation (**Einsatzdokumentation**) for fire brigades — every
contribution, from a typo fix to a new feature, is welcome.

## Development setup

- [.NET SDK](https://dotnet.microsoft.com/download/dotnet) — the exact SDK
  version is pinned via [`global.json`](global.json) (currently 9.x); the app
  projects themselves target `net8.0`
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
