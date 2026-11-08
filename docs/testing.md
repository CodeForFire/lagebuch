# Testing Lagebuch

The application targets .NET 10. `global.json` requests SDK `10.0.0` with
`rollForward: latestFeature`, allowing later .NET 10 feature bands rather than
pinning an exact SDK. SDK differences can affect analyzer and formatting results.
Run commands from the repository root. The UI uses German text; test names and
code are English, with German terms of art (Atemschutz, Trupp, Kräfte) kept as
they appear in the UI. Use fictional incident and master data only.

## Test projects

Test projects live under `tests/`, one per application layer:

- `LageBuch.Domain.Tests`: domain rules and invariants in `LageBuch.Domain`.
- `LageBuch.Persistence.Tests`: storage and repositories in `LageBuch.Persistence`.
- `LageBuch.Documents.Tests`: document generation in `LageBuch.Documents`.
- `LageBuch.Sync.Tests`: synchronization logic in `LageBuch.Sync`.
- `LageBuch.Sync.Hosting.Tests`: the sync server host in `LageBuch.Sync.Hosting`.
- `LageBuch.Osm.Tests`: OpenStreetMap integration in `LageBuch.Osm`.
- `LageBuch.AppLogic.Tests`: view models and application logic in `LageBuch.AppLogic`.
- `LageBuch.App.Tests`: the desktop application head `LageBuch.App`.
- `LageBuch.Acceptance.Tests`: real Avalonia controls, bindings, keyboard
  interaction and rendered layouts from `App.Shared`.

Shared fictional Stammdaten fixtures live in
[`tests/Fixtures/`](../tests/Fixtures/) (`StammdatenFixture.cs` and
`stammdaten-vollstaendig.json`). Other fictional examples live in
`AnonymizedExampleData` and `docs/samples/`.

Acceptance uses **xunit.v3** and its own nested package configuration; the other
test projects use **xunit v2**. Check the project files and copy the conventions
of the project you are extending, not an attribute or package from another project.

## Run and narrow tests

```bash
make test
make test PROJECT=tests/LageBuch.Domain.Tests FILTER=FullyQualifiedName~FormattingTests
```

`make test` runs `LageBuch.Desktop.slnf`, excluding the Android application head.
`PROJECT` selects one project; `FILTER` becomes `dotnet test --filter`. Supply
both when narrowing: a filter applied across the whole solution can match no tests
in other assemblies. The Makefile comment says these no-match assemblies fail,
but the current locked v2 and v3 runners both exit zero for an unmatched filter
in a direct project run. That comment is not a reliable guard; runner or
configuration changes can also change this exit behavior. Do not rely on a
solution-wide filter to give a consistent pass or failure. Neither a zero exit
nor `0 Error(s)` in the build tail proves the tests passed. Search the complete
log for `Test Run Failed`, and require the expected assemblies to report `Passed!`
with non-zero test counts and no failed tests.

`make test-all` uses the full `LageBuch.sln`, including the Android head. It needs
the Android workload and a compatible JDK (21 or earlier), as explained in
`CONTRIBUTING.md` and `docker/README.md`. It is not a replacement for the desktop
command on a machine without that setup. CI builds the full solution on Linux
and the desktop filter on Windows.

For the clean desktop build/test gate, the repository provides `make clean ci`.
It restores, builds Release, and runs the tests without rebuilding. Formatting
is a separate `make format-check` gate. Changes to C# also require `make codeql`
([`../AGENTS.md#static-analysis`](../AGENTS.md#static-analysis)); ordinary tests
do not replace CodeQL.

## Headless Avalonia tests

`tests/LageBuch.Acceptance.Tests/TestAppBuilder.cs` registers the test
application with the assembly-level `AvaloniaTestApplication` attribute. It
uses `.UseSkia()` and:

```csharp
.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
```

`false` keeps the real Skia text/render backend so embedded custom fonts can
rasterize. The default headless drawing stub cannot realize embedded glyph
typefaces and throws on the first such text layout. No desktop display server
is needed for these tests.

Use `[AvaloniaFact]` or `[AvaloniaTheory]` for tests that need the Avalonia
dispatcher. Plain `[Fact]` is enough for logic that does not require it.
`WorkspaceRenderHelper` creates fictional workspaces and selects tabs. Existing
render tests show the usual sequence: create a window, `Show()`, run pending
`Dispatcher.UIThread.RunJobs()`, perform the interaction and assert on the real
controls or view model. Close windows and dispose resources you own.

## Inspect rendered output

The helper `BitmapSaveExtensions.SavePng` encodes an Avalonia bitmap as PNG.
Existing tests such as `FilesTabRenderTests` capture a real frame with
`Window.CaptureRenderedFrame()` and write it only when `RENDER_OUT` is set.
Use an absolute output path: test-host working directories need not be the
repository root.

```bash
RENDER_OUT="$HOME/lagebuch-render" make test PROJECT=tests/LageBuch.Acceptance.Tests FILTER=FullyQualifiedName~FilesTabRenderTests
```

Open the generated PNGs and inspect the actual layout, text, contrast and crop.
A file existing or a test passing is not a visual review. For the harness,
before/after captures, diagnostic-only probes, attachment upload and clickable
`[![alt](U)](U)` image wrapping, follow
[AGENTS.md > Pull requests](../AGENTS.md#pull-requests) as the single source of
truth. Headless screenshots do not prove native platform behavior, Android
gestures or screen-reader usability.
