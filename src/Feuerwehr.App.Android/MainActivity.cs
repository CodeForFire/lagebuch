using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Feuerwehr.App.Android.Services;
using Feuerwehr.App.Shared;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain.Time;
// Inside the Feuerwehr.App.Android namespace the bare name "App" binds to the Feuerwehr.App
// namespace, not Feuerwehr.App.Shared.App — alias it so the shared Application type is reachable.
using SharedApp = Feuerwehr.App.Shared.App;

namespace Feuerwehr.App.Android;

[Activity(
    Label = "Lagebuch",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<SharedApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        SharedApp.CreateMainViewModel = () => CompositionRoot.CreateMainWindowViewModel(
            new IncidentStore(),
            new MasterDataProvider(AndroidAppPaths.MasterDataDbPath(this)),
            new JsonRecentFilesStore(AndroidAppPaths.RecentFilesJsonPath(this)),
            new AndroidFileDialogService(this),
            new SystemClock(),
            new Feuerwehr.App.Shared.Services.DispatcherTimerTicker(),
            new AndroidAlarmService(),
            new MasterDataFileService());
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
