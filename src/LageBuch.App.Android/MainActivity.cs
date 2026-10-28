using Android.Content.PM;
using Android.Views;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
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

// WindowSoftInputMode below: AdjustResize shrinks the window when the soft keyboard opens instead
// of drawing the keyboard over it. Without it the phone's add-entry sheet — the one place the app
// asks for typing — is exactly what the keyboard covers, and the field being filled is the field
// nobody can see (#53 §4, "on-screen keyboard avoidance"). Each composer stacks inside a
// ScrollViewer so the focused field can still be scrolled into what is left of the window.
//
// The comment sits out here rather than beside the argument it explains because the two StyleCop
// rules leave no room for it inside: SA1515 demands a blank line before a comment, SA1115 forbids
// one between parameters.
[Activity(
    Label = "Lagebuch",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // OpenDocument (rather than GetContent) accepts multiple MIME types on Launch, needed
    // since an attachment can be any of several image types or a PDF.
    private static readonly string[] AttachmentMimeTypes =
        ["image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf"];

    private ActivityResultLauncher? _importLauncher;
    private ActivityResultLauncher? _attachmentLauncher;
    private AndroidFileDialogService? _dialogs;
    private IncidentStore? _store;

    // Per process, not per Activity: its playback thread outlives every rotation (#637).
    private static AndroidAlarmService? _alarm;

    // Avalonia 12 removed AvaloniaMainActivity<TApp>/CustomizeAppBuilder(AppBuilder) — app/AppBuilder
    // setup moved to MainApplication, which runs before any Activity exists. This Activity-scoped
    // wiring (file pickers need a live Activity to register against) instead runs here, before
    // base.OnCreate() — which is what triggers Avalonia to invoke SharedApp.CreateMainViewModel via
    // IActivityApplicationLifetime.MainViewFactory (see App.axaml.cs), same ordering as before.
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        // Every ÖFFNEN leaves a copy under the attachment temp root; nothing else removes it (#289).
        // Off the UI thread like the desktop's sweep, but with an age grace: OnCreate also runs on
        // rotation, while a viewer may still be reading a fresh copy through its content:// grant.
        _ = Task.Run(() => AttachmentTempPaths.SweepStaleOpenDirectories(TimeProvider.System));

        // The last file picked in a previous run (#407) -- a cold start only. OnCreate also runs when
        // the Activity is recreated for a configuration change it does not handle (locale, font
        // scale, a split-screen resize), and a pick that just completed may not have been read yet.
        // A state restored after the process was killed skips this too; the next attachment pick
        // clears that folder anyway. On this thread, not Task.Run: a pick that completes right after
        // this OnCreate copies into the same folder, and a clear still running in the background
        // could delete that copy. Each folder holds one file at most, so this is cheap.
        if (savedInstanceState is null)
        {
            AndroidAppPaths.ClearPickedFiles(this);
        }

        _importLauncher = RegisterForActivityResult(
            new ActivityResultContracts.GetContent(),
            new ImportCallback(uri => _dialogs?.CompleteImport(uri)));

        // OpenDocument (rather than GetContent) accepts multiple MIME types on Launch, needed
        // since an attachment can be any of several image types or a PDF.
        _attachmentLauncher = RegisterForActivityResult(
            new ActivityResultContracts.OpenDocument(),
            new ImportCallback(uri => _dialogs?.CompleteAttachment(uri)));

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
            _alarm ??= new AndroidAlarmService(this),
            new MasterDataFileService(),
            new NoopIncidentHostController(),
            new LageBuch.App.Shared.Services.AvaloniaUiDispatcher(),
            AppVersionText.Of(typeof(MainActivity).Assembly),
            lastSaveFolder: null,
            attachmentCacheRoot: AndroidAppPaths.AttachmentCacheDir(this),
            trustStore: new JsonTrustStore(AndroidAppPaths.TrustJsonPath(this)),
            pdfExporter: new NoopIncidentPdfExporter(),
            lastConnection: new JsonLastConnectionStore(AndroidAppPaths.LastConnectionJsonPath(this)),
            lastCrashReport: CrashReports.TakePending(FilesDir!.AbsolutePath),
            alarmMute: new AlarmMute(AndroidAppPaths.AlarmMuteJsonPath(this)));

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
}
