using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared;

[SuppressMessage("Naming", "CA1724", Justification = "The Avalonia App class sits in the LageBuch.App.Shared namespace by design; the collision with the LageBuch.App namespace is inherent to the app's name.")]
public partial class App : Application
{
    /// <summary>
    /// Set by the platform entry point (desktop <c>Program.cs</c>, Android <c>MainActivity</c>)
    /// before the first <see cref="LageBuch.App.Shared.Views.MainView"/> or
    /// <see cref="LageBuch.App.Shared.Views.MainWindow"/> is constructed — only the platform
    /// head knows which <see cref="LageBuch.AppLogic.Services.IFileDialogService"/>, paths, and
    /// <see cref="LageBuch.AppLogic.Services.IAlarmService"/> implementation to wire up. On
    /// desktop that means before framework init returns. On Android, framework init runs in
    /// <c>MainApplication</c> before any Activity exists, so this is set later, in
    /// <c>MainActivity.OnCreate</c>, and read lazily via
    /// <see cref="IActivityApplicationLifetime.MainViewFactory"/> below rather than eagerly at
    /// framework-init time.
    /// </summary>
    public static Func<MainWindowViewModel>? CreateMainViewModel { get; set; }

    /// <summary>
    /// Set by the desktop entry point next to <see cref="CreateMainViewModel"/>: writes the crash
    /// report for a start that threw and returns the folder it went to, or <see langword="null"/>
    /// when it could not be written.
    /// </summary>
    public static Func<Exception, string?>? ReportStartupFailure { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The desktop's main window, or -- when building it throws -- a <see cref="StartupFailureWindow"/>
    /// in its place. Left to the unhandled-exception hook, such a failure had no window to show
    /// on, and the report it left was taken by the next start before that start failed the same
    /// way, so a store that broke every start broke it silently.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Anything that stops the main window being built must reach the screen; the failure is reported and shown, not swallowed.")]
    public static Window CreateDesktopMainWindow(Func<MainWindowViewModel> createViewModel, Func<Exception, string?> reportStartupFailure)
    {
        ArgumentNullException.ThrowIfNull(createViewModel);
        ArgumentNullException.ThrowIfNull(reportStartupFailure);
        try
        {
            return new MainWindow(createViewModel());
        }
        catch (Exception ex)
        {
            return new StartupFailureWindow(ex, reportStartupFailure(ex));
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Create the view model only inside a real lifetime branch. The headless acceptance-test
        // harness sets neither lifetime (and never assigns CreateMainViewModel), so it must fall
        // straight through to base — dereferencing CreateMainViewModel here would NRE every test.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateDesktopMainWindow(CreateMainViewModel!, ReportStartupFailure ?? (_ => null));
            if (desktop.MainWindow is StartupFailureWindow)
            {
                desktop.Exit += (_, e) => e.ApplicationExitCode = 1;
            }
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () =>
            {
                var mainView = new MainView();
                mainView.AttachViewModel(CreateMainViewModel!());
                return mainView;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
