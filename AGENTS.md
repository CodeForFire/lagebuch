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
- PGP signed: configured globally (`commit.gpgsign = true`)

Signing in the sandbox: SSH signing (`gpg.format = ssh`) needs the SSH key,
which the command sandbox blocks — inside the sandbox `git commit` silently
produces an unsigned commit despite `commit.gpgsign = true`. Run signing
commits with the sandbox disabled and verify with
`git cat-file commit HEAD | grep gpgsig` before pushing.

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

Any PR touching `src/` must add a changelog entry as its own file in
`changelog.d/`, named `+<slug>.<type>.md`, where `<type>` is one of `added`,
`changed`, `deprecated`, `removed`, `fixed` or `security`. The file holds the
entry text as plain prose, with no leading `- `; reference issues inline
(`(#302)`). CI enforces this — the `changelog entry` job runs
`towncrier check` — so do it in the same PR, not as a follow-up.

Never edit `CHANGELOG.md` directly. It is assembled from `changelog.d/` by
`towncrier build` when a release is cut. Editing it from a feature branch is
what used to make every other open PR conflict, which is the whole reason for
the split. The leading `+` in the filename is required: it keeps filenames
unique so two PRs citing the same ticket cannot collide.

## Master-data example file

`docs/master-data.example.json` is documented (README.md) as the full
Stammdaten schema reference. Whenever `MasterDataSet` or `MasterDataJson`
in `src/LageBuch.Persistence/MasterData/MasterDataSet.cs` gains, loses, or
renames a top-level field, update `docs/master-data.example.json` in the
same PR so it stays a complete, accurate example of every category.
