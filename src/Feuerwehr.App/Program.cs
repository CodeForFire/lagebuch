using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Feuerwehr.App.Services;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Feuerwehr.App.Shared.App.CreateMainViewModel = CreateMainViewModel;
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
        var uiDispatcher = new Feuerwehr.App.Shared.Services.AvaloniaUiDispatcher();
        return Feuerwehr.App.Shared.CompositionRoot.CreateMainWindowViewModel(
            new IncidentStore(),
            new MasterDataProvider(AppPaths.MasterDataDbPath),
            new JsonRecentFilesStore(AppPaths.RecentFilesJsonPath),
            dialogs,
            clock,
            new Feuerwehr.App.Shared.Services.DispatcherTimerTicker(),
            new SystemAlarmService(),
            new MasterDataFileService(),
            new IncidentHostController(clock, version, uiDispatcher),
            uiDispatcher,
            version,
            new JsonLastSaveFolderStore(AppPaths.LastSaveFolderJsonPath),
            AppPaths.AttachmentCacheDir);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Feuerwehr.App.Shared.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
