# Project rules

## Pull requests

**Every PR that touches the UI must include screenshots.** GitHub's pasted-image
store has no public upload API, so the flow is:

1. Render the affected view(s) to PNG using the headless Skia harness in
   `tests/Feuerwehr.Acceptance.Tests` (see `WorkspaceRenderHelper` +
   `Window.CaptureRenderedFrame()` — `UseHeadlessDrawing = false` so embedded
   fonts rasterize).
2. Save the PNGs to a known path and give the user the file paths.
3. The user pastes them into the PR body (Claude cannot attach them automatically).

For UI *changes*, provide a **before/after pair**. The render harness files are
diagnostic-only and should not be committed unless they double as a real test.
