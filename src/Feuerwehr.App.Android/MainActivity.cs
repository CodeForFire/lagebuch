using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
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
            new MasterDataProvider("/data/local/tmp/lagebuch-masterdata.db"), // replaced in Task 8
            new JsonRecentFilesStore("/data/local/tmp/lagebuch-recent.json"), // replaced in Task 8
            new ThrowingFileDialogService(), // replaced in Task 9
            new SystemClock(),
            new Feuerwehr.App.Shared.Services.DispatcherTimerTicker(),
            new NoopAlarmService(), // replaced in Task 8
            new MasterDataFileService());
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}

internal sealed class ThrowingFileDialogService : IFileDialogService
{
    public Task<string?> PickSaveAsync(string suggestedFileName) => throw new NotImplementedException();
    public Task<string?> PickOpenAsync() => throw new NotImplementedException();
    public Task<string?> PickExportPdfAsync(string suggestedFileName) => throw new NotImplementedException();
    public Task<string?> PickImportJsonAsync() => throw new NotImplementedException();
    public Task<string?> PickExportJsonAsync(string suggestedFileName) => throw new NotImplementedException();
    public Task ShareFileAsync(string path, string mimeType) => throw new NotImplementedException();
}

internal sealed class NoopAlarmService : IAlarmService
{
    public void Start() { }
    public void Stop() { }
}
