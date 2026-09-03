using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using LageBuch.App.Services;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        LageBuch.App.Shared.App.CreateMainViewModel = CreateMainViewModel;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static MainWindowViewModel CreateMainViewModel()
    {
        // Resolved lazily at call time, once desktop.MainWindow has been assigned — avoids the
        // chicken-and-egg problem of needing a TopLevel before the Window that provides one exists.
        var dialogs = new StorageProviderFileDialogService(() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
        var clock = new SystemClock();
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var uiDispatcher = new LageBuch.App.Shared.Services.AvaloniaUiDispatcher();
        return LageBuch.App.Shared.CompositionRoot.CreateMainWindowViewModel(
            new IncidentStore(),
            new MasterDataProvider(AppPaths.MasterDataDbPath),
            new JsonRecentFilesStore(AppPaths.RecentFilesJsonPath),
            dialogs,
            clock,
            new LageBuch.App.Shared.Services.DispatcherTimerTicker(),
            new SystemAlarmService(),
            new MasterDataFileService(),
            new IncidentHostController(clock, version, uiDispatcher),
            uiDispatcher,
            version,
            new JsonLastSaveFolderStore(AppPaths.LastSaveFolderJsonPath),
            AppPaths.AttachmentCacheDir,
            trustStore: new JsonTrustStore(AppPaths.TrustJsonPath));
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LageBuch.App.Shared.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
