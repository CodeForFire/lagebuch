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
        var store = new IncidentStore();

        // CA2000: an app-lifetime singleton, disposed via the desktopLifetime.Exit hook below rather
        // than a using block — same shape as SerialAudioQueue's own CA1001 suppression.
#pragma warning disable CA2000
        var alarms = new SystemAlarmService();
#pragma warning restore CA2000

        // Best-effort: drain IncidentStore's background writer (issue #167 P0 #1) before the process
        // actually exits, so the last queued save isn't lost. Blocking briefly here is fine — Exit
        // fires once shutdown is already underway, and FlushAsync's own writer thread completes the
        // wait, so there's no deadlock risk. Also cleans up the alarm temp WAVs (#167 P2).
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Exit += (_, _) => store.FlushAsync().GetAwaiter().GetResult();
            desktopLifetime.Exit += (_, _) => alarms.Dispose();
        }

        return LageBuch.App.Shared.CompositionRoot.CreateMainWindowViewModel(
            store,
            new MasterDataProvider(AppPaths.MasterDataDbPath),
            new JsonRecentFilesStore(AppPaths.RecentFilesJsonPath),
            dialogs,
            clock,
            new LageBuch.App.Shared.Services.DispatcherTimerTicker(),
            alarms,
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
