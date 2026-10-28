using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LageBuch.App.Services;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // First, so it covers everything after it. An exception escaping the UI thread ends up here
        // too, out of StartWithClassicDesktopLifetime. Unobserved tasks deliberately do not: .NET no
        // longer ends the process for one, so it is not a crash (#300).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception crash)
            {
                _ = CrashReports.TryWrite(AppPaths.Root, crash, AppVersion, TimeProvider.System.GetUtcNow());
            }
        };

        // At startup rather than exit, so a crashed run's copies go too; off the UI path because
        // nothing waits on it and the amount of file I/O is unknown (#383).
        _ = Task.Run(AttachmentTempPaths.SweepOpenDirectories);

        // Likewise a joined session's copies of the host's attachments, which a crashed run never
        // got to delete on leaving (#382).
        _ = Task.Run(() => RemoteIncidentSession.ClearAttachmentCache(AppPaths.AttachmentCacheDir));

        // Before the first window: Windows reads the ID when the taskbar button is created.
        if (OperatingSystem.IsWindows())
        {
            WindowsAppIdentity.Apply();
        }

        LageBuch.App.Shared.App.CreateMainViewModel = CreateMainViewModel;

        // A start that throws is shown rather than crashed, so the hook above never sees it; the
        // report is written here instead, and announced on the next start that gets that far.
        LageBuch.App.Shared.App.ReportStartupFailure = failure =>
            CrashReports.TryWrite(AppPaths.Root, failure, AppVersion, TimeProvider.System.GetUtcNow())
                ? CrashReports.Folder(AppPaths.Root)
                : null;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static MainWindowViewModel CreateMainViewModel()
    {
        // Resolved lazily at call time, once desktop.MainWindow has been assigned — avoids the
        // chicken-and-egg problem of needing a TopLevel before the Window that provides one exists.
        var dialogs = new StorageProviderFileDialogService(() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
        var clock = new SystemClock();
        var version = AppVersion;
        var uiDispatcher = new LageBuch.App.Shared.Services.AvaloniaUiDispatcher();
        var store = new IncidentStore();

        // CA2000: an app-lifetime singleton, disposed via the desktopLifetime.Exit hook below rather
        // than a using block — same shape as SerialAudioQueue's own CA1001 suppression.
#pragma warning disable CA2000
        var alarms = new SystemAlarmService();

        // Only the Kartenpakete download through it, on the Lagebuchführer's request.
        var http = new HttpClient();
#pragma warning restore CA2000

        // Best-effort: drain IncidentStore's background writer (issue #167 P0 #1) before the process
        // actually exits, so the last queued save isn't lost. Blocking briefly here is fine — Exit
        // fires once shutdown is already underway, and FlushAsync's own writer thread completes the
        // wait, so there's no deadlock risk. Also cleans up the alarm temp WAVs (#167 P2).
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Exit += (_, _) => store.FlushAsync().GetAwaiter().GetResult();
            desktopLifetime.Exit += (_, _) => alarms.Dispose();
            desktopLifetime.Exit += (_, _) => http.Dispose();
        }

        var mainViewModel = LageBuch.App.Shared.CompositionRoot.CreateMainWindowViewModel(
            store,
            new MasterDataProvider(AppPaths.MasterDataDbPath),
            new JsonRecentFilesStore(AppPaths.RecentFilesJsonPath),
            dialogs,
            clock,
            new LageBuch.App.Shared.Services.DispatcherTimerTicker(),
            alarms,
            new MasterDataFileService(),
            new IncidentHostController(clock, version, uiDispatcher, AppPaths.HostKeyPath),
            uiDispatcher,
            version,
            new JsonLastSaveFolderStore(AppPaths.LastSaveFolderJsonPath),
            AppPaths.AttachmentCacheDir,
            trustStore: new JsonTrustStore(AppPaths.TrustJsonPath),
            pdfExporter: new QuestPdfIncidentExporter(),
            lastPdfExport: new JsonLastPdfExportStore(AppPaths.LastPdfExportJsonPath),
            lastConnection: new JsonLastConnectionStore(AppPaths.LastConnectionJsonPath),
            mailComposer: new OsMailComposer(),
            lastCrashReport: CrashReports.TakePending(AppPaths.Root),
            alarmMute: new AlarmMute(AppPaths.AlarmMuteJsonPath),
            mapPacks: new RegionPackLibrary(http, AppPaths.RegionPacksDir));
        OpenLaunchedFile(mainViewModel);
        return mainViewModel;
    }

    // A double-clicked Einsatzdatei. Windows and Linux pass it as an argument, macOS as an
    // open-document event. Both go through the Home screen's own open, so a missing, locked or
    // too-new file surfaces in its error banner like one picked from the recent list.
    private static void OpenLaunchedFile(MainWindowViewModel mainViewModel)
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (IncidentFileExtension.StartupFile(lifetime?.Args ?? []) is { } path)
        {
            // Posted rather than run now: this is still framework initialisation, before the main
            // window exists. The post runs once the main loop has shown it.
            Dispatcher.UIThread.Post(() => _ = mainViewModel.OpenRecent(path));
        }

        // Subscribed here, before anything awaits, because the event that launched the app fires
        // right after framework initialisation and a later subscriber misses it.
        if (Avalonia.Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e is FileActivatedEventArgs fileArgs
                    && fileArgs.Files.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault(IncidentFileExtension.IsIncidentFile) is { } activated)
                {
                    _ = mainViewModel.OpenRecent(activated);
                }
            };
        }
    }

    private static string AppVersion => AppVersionText.Of(typeof(Program).Assembly);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LageBuch.App.Shared.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
