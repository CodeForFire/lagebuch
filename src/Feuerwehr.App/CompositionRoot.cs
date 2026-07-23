using Avalonia.Controls;
using Feuerwehr.App.Services;
using Feuerwehr.App.ViewModels;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.App;

public static class CompositionRoot
{
    public static MainWindowViewModel CreateMainWindowViewModel(Func<TopLevel?> topLevel)
    {
        var store = new IncidentStore();
        var masterData = new MasterDataProvider(AppPaths.MasterDataDbPath);
        var recent = new JsonRecentFilesStore(AppPaths.RecentFilesJsonPath);
        var dialogs = new StorageProviderFileDialogService(topLevel);
        var home = new HomeViewModel(store, masterData, recent, dialogs,
            new SystemClock(), new DispatcherTimerTicker(), new SystemAlarmService());
        var editor = new MasterDataEditorViewModel(masterData);
        return new MainWindowViewModel(home, editor);
    }
}
