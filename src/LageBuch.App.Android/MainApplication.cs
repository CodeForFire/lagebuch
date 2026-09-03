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

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
