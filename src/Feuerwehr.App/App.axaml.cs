using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Feuerwehr.App.ViewModels;
using Feuerwehr.App.Views;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = null!;
            var home = CompositionRoot.CreateHome(() => window);
            // TODO(Task 7): wire this through CompositionRoot instead of constructing a second
            // MasterDataProvider here — this is a stopgap so the Task 6 constructor change builds.
            var editor = new MasterDataEditorViewModel(new MasterDataProvider(AppPaths.MasterDataDbPath));
            var mainViewModel = new MainWindowViewModel(home, editor);
            window = new MainWindow(mainViewModel);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
