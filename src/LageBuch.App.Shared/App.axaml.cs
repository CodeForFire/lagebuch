using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared;

public partial class App : Application
{
    /// <summary>
    /// Set by the platform entry point (desktop <c>Program.cs</c>, Android <c>MainActivity</c>)
    /// before Avalonia's framework init runs — only the platform head knows which
    /// <see cref="LageBuch.AppLogic.Services.IFileDialogService"/>, paths, and
    /// <see cref="LageBuch.AppLogic.Services.IAlarmService"/> implementation to wire up.
    /// </summary>
    public static Func<MainWindowViewModel>? CreateMainViewModel { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Create the view model only inside a real lifetime branch. The headless acceptance-test
        // harness sets neither lifetime (and never assigns CreateMainViewModel), so it must fall
        // straight through to base — dereferencing CreateMainViewModel here would NRE every test.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(CreateMainViewModel!());
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var mainView = new MainView();
            mainView.AttachViewModel(CreateMainViewModel!());
            singleView.MainView = mainView;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
