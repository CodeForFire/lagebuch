# Agent instructions

## Pull requests

Every PR that touches the UI must include screenshots. The flow is:

1. Render the affected view(s) to PNG using the headless Skia harness in
   `tests/LageBuch.Acceptance.Tests` (see `WorkspaceRenderHelper` +
   `Window.CaptureRenderedFrame()` — `UseHeadlessDrawing = false` so embedded
   fonts rasterize).
2. Attach the PNGs directly with `gh`'s `--attach` flag — no manual pasting
   needed. It uploads to `github.com/user-attachments/assets` (the same
   pipeline the web UI's drag-and-drop uses) and rewrites any
   `![alt](./path.png)` reference in the body to point at the uploaded asset:
   - New PR: `gh pr create --attach './before.png#Before' --attach './after.png#After'`
   - Existing PR body: `gh pr edit <number> --attach './screenshot.png#Alt text'`
   - PR comment: `gh pr comment <number> --attach './screenshot.png#Alt text'`

For UI changes, provide a before/after pair. The render harness files are
diagnostic-only and should not be committed unless they double as a real test.

The README's screenshots and demo GIF come from one such test,
`DemoFlowRenderTests` — regenerate them with `make screenshots` and
`make demo-gif` whenever a view they show changes; `make samples` refreshes
the fictional `docs/samples/uebung.fwincident` after a schema change.

## Git commits

All commits must be:
- Semantic / Conventional Commits: subject starts with `feat:`, `fix:`,
  `docs:`, `style:`, `refactor:`, `perf:`, `test:`, `build:`, `ci:`, `chore:`
  (optionally scoped, e.g. `fix(backgroundjob):`)
- DCO signed-off: always pass `-s` to `git commit`
- Cryptographically signed, SSH or GPG: always pass `-S`, or set
  `commit.gpgsign = true`

The last two are project requirements, not local conventions — `main`'s branch
protection rejects a pull request carrying an unsigned commit, and the DCO
check fails one missing a `Signed-off-by` trailer. Neither can be merged past.
[`CONTRIBUTING.md`](CONTRIBUTING.md#signing-your-commits) has the setup and the
recovery recipe for a branch already pushed.

Signing in the sandbox: SSH signing (`gpg.format = ssh`) needs the SSH key,
which the command sandbox blocks — inside the sandbox `git commit` silently
produces an unsigned commit despite `commit.gpgsign = true`, and you find out
only when the merge is blocked. Run signing commits with the sandbox disabled
and verify before pushing with:

```bash
git cat-file commit HEAD | grep gpgsig
```

That check covers SSH signatures too: git stores them in the same `gpgsig`
header, so the absence of that header means the commit is unsigned whichever
format you use.

## Git push

NEVER push directly to `master` or `main`. Always use a feature branch and
open a PR. Absolute rule, no exceptions.

## GitHub Actions workflows

- Pin third-party actions to a full 40-char commit SHA, never a movable tag
  (`uses: owner/action@<sha> # vX.Y.Z`). First-party internal reusable
  workflows may follow repo convention.
- Always pin to the latest release: resolve with
  `gh api repos/<owner>/<action>/releases/latest --jq .tag_name`, then
  `gh api repos/<owner>/<action>/git/refs/tags/<tag> --jq .object.sha`.
- Every repo needs `.github/dependabot.yml` covering the `github-actions`
  ecosystem; add it if missing.

## Repository hygiene

Never commit `docs/superpowers/` — local tooling artifacts only. If tracked,
remove them and add to `.gitignore`.

## Changelog entries

Any PR touching `src/` must add an entry under `CHANGELOG.md`'s
`## [Unreleased]` section. CI enforces this (a `changelog-check` job fails
the PR if `src/` changed but `CHANGELOG.md` didn't) — do it in the same PR,
not as a follow-up.

## Master-data example file

`docs/master-data.example.json` is documented (README.md) as the full
Stammdaten schema reference. Whenever `MasterDataSet` or `MasterDataJson`
in `src/LageBuch.Persistence/MasterData/MasterDataSet.cs` gains, loses, or
renames a top-level field, update `docs/master-data.example.json` in the
same PR so it stays a complete, accurate example of every category.
