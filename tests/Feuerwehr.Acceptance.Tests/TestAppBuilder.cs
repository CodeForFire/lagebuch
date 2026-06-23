using Avalonia;
using Avalonia.Headless;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(Feuerwehr.Acceptance.Tests.TestAppBuilder))]

namespace Feuerwehr.Acceptance.Tests;

public static class TestAppBuilder
{
    // UseHeadlessDrawing = false keeps the real Skia text/render backend (bundled via
    // Avalonia.Desktop) so embedded custom fonts can be rasterized. The default headless
    // drawing stub cannot realize embedded glyph typefaces and throws on first text layout.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Feuerwehr.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
