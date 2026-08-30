using Avalonia;
using Avalonia.Headless;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(LageBuch.Acceptance.Tests.TestAppBuilder))]

namespace LageBuch.Acceptance.Tests;

internal static class TestAppBuilder
{
    // UseHeadlessDrawing = false keeps the real Skia text/render backend (bundled via
    // Avalonia.Desktop) so embedded custom fonts can be rasterized. The default headless
    // drawing stub cannot realize embedded glyph typefaces and throws on first text layout.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LageBuch.App.Shared.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
