using Avalonia;
using Avalonia.Headless;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(Feuerwehr.Acceptance.Tests.TestAppBuilder))]

namespace Feuerwehr.Acceptance.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Feuerwehr.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
