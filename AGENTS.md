# Agent instructions

## Pull requests

Every PR that touches the UI must include screenshots. GitHub's pasted-image
store has no public upload API, so the flow is:

1. Render the affected view(s) to PNG using the headless Skia harness in
   `tests/LageBuch.Acceptance.Tests` (see `WorkspaceRenderHelper` +
   `Window.CaptureRenderedFrame()` — `UseHeadlessDrawing = false` so embedded
   fonts rasterize).
2. Save the PNGs to a known path and give the user the file paths.
3. The user pastes them into the PR body (the agent cannot attach images
   automatically).

For UI changes, provide a before/after pair. The render harness files are
diagnostic-only and should not be committed unless they double as a real test.

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
