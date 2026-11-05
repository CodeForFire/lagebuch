# Testing Lagebuch

The SDK version is pinned in `global.json` and the application targets .NET 10.
Run commands from the repository root. The UI uses German text; test names and
code are English. Use fictional incident and master data only.

## Test projects

There are currently nine test projects under `tests/`, plus the shared
`Fixtures/` directory.

| Project | What belongs here |
| --- | --- |
| `LageBuch.Domain.Tests` | Incident rules, entities, formatting and domain state transitions. |
| `LageBuch.Persistence.Tests` | SQLite storage, migrations, incident files and master-data serialization. |
| `LageBuch.Documents.Tests` | PDF/report content and document generation. |
| `LageBuch.Sync.Tests` | Sync protocol, commands and snapshot behavior without the real host. |
| `LageBuch.Sync.Hosting.Tests` | Host integration, connections, trust and transport behavior using real local sockets. |
| `LageBuch.AppLogic.Tests` | View models, commands, services and orchestration without Avalonia. |
| `LageBuch.Osm.Tests` | OpenStreetMap importing, geometry and map data behavior. |
| `LageBuch.App.Tests` | Desktop-host behavior, including platform integration and wiring. |
| `LageBuch.Acceptance.Tests` | Real Avalonia controls, bindings, keyboard interaction and rendered layouts from `App.Shared`. |

Acceptance uses **xunit.v3** and its own nested package configuration. The other
eight projects use **xunit v2**. Copy the conventions of the project you are
extending, not an attribute or package from another project.

## Run and narrow tests

```bash
make test
make test PROJECT=tests/LageBuch.Domain.Tests FILTER=FullyQualifiedName~FormattingTests
```

`make test` runs `LageBuch.Desktop.slnf`, excluding the Android application head.
`PROJECT` selects one project; `FILTER` becomes `dotnet test --filter`. Supply
both when narrowing: a filter applied to the whole solution can leave other
assemblies with no matching tests and fail the run. A nonzero exit is not a pass;
look for `Test Run Failed` as well as individual assembly summaries.

`make test-all` uses the full `LageBuch.sln`, including the Android head. It needs
the Android workload and a compatible JDK (21 or earlier), as explained in
`CONTRIBUTING.md` and `docker/README.md`. It is not a replacement for the desktop
command on a machine without that setup. CI builds the full solution on Linux
and the desktop filter on Windows.

For the clean desktop build/test gate, the repository provides `make clean ci`.
It restores, builds Release, and runs the tests without rebuilding. Formatting
is a separate `make format-check` gate. Changes to C# also require the CodeQL
check described in `CONTRIBUTING.md`; passing ordinary tests does not replace it.

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
RENDER_OUT=/tmp/lagebuch-render make test PROJECT=tests/LageBuch.Acceptance.Tests FILTER=FullyQualifiedName~FilesTabRenderTests
```

Open the generated PNGs and inspect the actual layout, text, contrast and crop.
A file existing or a test passing is not a visual review. UI contributions need
a before/after pair; use the same dimensions and fictional data for both.
Headless screenshots do not prove native platform behavior, Android gestures or
screen-reader usability. Diagnostic-only render probes are not production code;
commit them only when they also provide a useful regression test.
