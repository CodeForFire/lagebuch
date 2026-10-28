using Android.Runtime;
using Avalonia;
using Avalonia.Android;

// Inside the LageBuch.App.Android namespace the bare name "App" binds to the LageBuch.App
// namespace, not LageBuch.App.Shared.App — alias it so the shared Application type is reachable.
using SharedApp = LageBuch.App.Shared.App;

namespace LageBuch.App.Android;

/// <summary>
/// Android application entry point. Avalonia 12 moved app/AppBuilder initialization here
/// (off <see cref="MainActivity"/>) since an Android process can host multiple activities
/// during its lifetime; Activity-scoped setup (file pickers, <see cref="SharedApp.CreateMainViewModel"/>)
/// stays on <see cref="MainActivity"/> instead, where an Activity instance actually exists.
/// </summary>
[Application]
public class MainApplication : AvaloniaAndroidApplication<SharedApp>
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    // Here rather than beside MainActivity's temp sweep: the Activity is created again on every
    // rotation, possibly while a session is joined, and this runs once per process. A joined
    // session's copies of the host's attachments are deleted when it ends; this clears what a
    // killed process left (#382).
    public override void OnCreate()
    {
        // Before anything else, like the desktop's Program.Main (#300). Android raises a managed
        // exception that escapes on a Java-driven thread through AndroidEnvironment, the rest
        // through the AppDomain; both leave the same report, announced on the next start.
        var crashRoot = FilesDir!.AbsolutePath;
        var version = LageBuch.AppLogic.Services.AppVersionText.Of(typeof(MainApplication).Assembly);
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            _ = LageBuch.AppLogic.Services.CrashReports.TryWrite(crashRoot, e.Exception, version, TimeProvider.System.GetUtcNow());
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception crash)
            {
                _ = LageBuch.AppLogic.Services.CrashReports.TryWrite(crashRoot, crash, version, TimeProvider.System.GetUtcNow());
            }
        };

        base.OnCreate();
        var cacheRoot = Services.AndroidAppPaths.AttachmentCacheDir(this);
        _ = Task.Run(() => LageBuch.Sync.RemoteIncidentSession.ClearAttachmentCache(cacheRoot));
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
