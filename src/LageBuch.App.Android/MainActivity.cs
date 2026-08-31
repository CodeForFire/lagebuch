using Android.Content.PM;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using Avalonia;
using Avalonia.Android;
using LageBuch.App.Android.Services;
using LageBuch.App.Shared;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Time;
// Inside the LageBuch.App.Android namespace the bare name "App" binds to the LageBuch.App
// namespace, not LageBuch.App.Shared.App — alias it so the shared Application type is reachable.
using SharedApp = LageBuch.App.Shared.App;

namespace LageBuch.App.Android;

[Activity(
    Label = "Lagebuch",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<SharedApp>
{
    // OpenDocument (rather than GetContent) accepts multiple MIME types on Launch, needed
    // since an attachment can be any of several image types or a PDF.
    private static readonly string[] AttachmentMimeTypes =
        ["image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf"];

    private ActivityResultLauncher? _importLauncher;
    private ActivityResultLauncher? _attachmentLauncher;
    private AndroidFileDialogService? _dialogs;

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        _importLauncher = RegisterForActivityResult(
            new ActivityResultContracts.GetContent(),
            new ImportCallback(uri => _dialogs?.CompleteImport(uri)));
        // OpenDocument (rather than GetContent) accepts multiple MIME types on Launch, needed
        // since an attachment can be any of several image types or a PDF.
        _attachmentLauncher = RegisterForActivityResult(
            new ActivityResultContracts.OpenDocument(),
            new ImportCallback(uri => _dialogs?.CompleteAttachment(uri)));
        base.OnCreate(savedInstanceState);
    }

    // AndroidX's RegisterForActivityResult takes an IActivityResultCallback, not a delegate, so the
    // document picker's result is routed through this thin adapter back to the file-dialog service.
    private sealed class ImportCallback : Java.Lang.Object, IActivityResultCallback
    {
        private readonly Action<global::Android.Net.Uri?> _onResult;
        public ImportCallback(Action<global::Android.Net.Uri?> onResult) => _onResult = onResult;
        public void OnActivityResult(Java.Lang.Object? result) => _onResult(result as global::Android.Net.Uri);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        _dialogs = new AndroidFileDialogService(this);
        _dialogs.OnLaunchImportPicker = () => _importLauncher!.Launch("application/json");
        _dialogs.OnLaunchAttachmentPicker = () => _attachmentLauncher!.Launch(AttachmentMimeTypes);
        SharedApp.CreateMainViewModel = () => CompositionRoot.CreateMainWindowViewModel(
            new IncidentStore(),
            new MasterDataProvider(AndroidAppPaths.MasterDataDbPath(this)),
            new JsonRecentFilesStore(AndroidAppPaths.RecentFilesJsonPath(this)),
            _dialogs,
            new SystemClock(),
            new LageBuch.App.Shared.Services.DispatcherTimerTicker(),
            new AndroidAlarmService(),
            new MasterDataFileService(),
            new NoopIncidentHostController(),
            new LageBuch.App.Shared.Services.AvaloniaUiDispatcher(),
            typeof(MainActivity).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            lastSaveFolder: null,
            attachmentCacheRoot: AndroidAppPaths.AttachmentCacheDir(this));
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
