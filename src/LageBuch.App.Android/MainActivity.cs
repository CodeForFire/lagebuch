using Android.Content.PM;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using Avalonia;
using Avalonia.Android;
using LageBuch.App.Android.Services;
using LageBuch.App.Shared;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Time;
using LageBuch.Sync;

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
    private IncidentStore? _store;

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

    // Best-effort only (issue #167 P0 #1): Android can kill the process without calling OnPause at
    // all, so this narrows the background writer's data-loss window for the common "user switches
    // away" case rather than closing it. Fire-and-forget, not awaited — OnPause must return quickly.
    protected override void OnPause()
    {
        base.OnPause();
        _ = _store?.FlushAsync();
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
        _store = new IncidentStore();
        SharedApp.CreateMainViewModel = () => CompositionRoot.CreateMainWindowViewModel(
            _store,
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
            attachmentCacheRoot: AndroidAppPaths.AttachmentCacheDir(this),
            trustStore: new JsonTrustStore(AndroidAppPaths.TrustJsonPath(this)));
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
