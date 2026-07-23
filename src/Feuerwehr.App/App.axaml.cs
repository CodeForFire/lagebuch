using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Feuerwehr.App.Views;

namespace Feuerwehr.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = null!;
            var mainViewModel = CompositionRoot.CreateMainWindowViewModel(() => window);
            window = new MainWindow(mainViewModel);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
