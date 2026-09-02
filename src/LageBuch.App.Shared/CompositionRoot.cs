using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.App.Shared;

/// <summary>
/// Builds the shared view-model graph from platform-supplied services. Each platform head
/// (desktop <c>Program.cs</c>, Android <c>MainActivity</c>) constructs its own
/// <see cref="IFileDialogService"/>/<see cref="IAlarmService"/>/path implementations and calls
/// this once, instead of duplicating the view-model wiring per platform.
/// </summary>
public static class CompositionRoot
{
    public static MainWindowViewModel CreateMainWindowViewModel(
        IIncidentStore store,
        IMasterDataProvider masterData,
        IRecentFilesStore recent,
        IFileDialogService dialogs,
        IClock clock,
        ITicker ticker,
        IAlarmService alarm,
        IMasterDataFileService masterDataFileService,
        IIncidentHostController hostController,
        IUiDispatcher uiDispatcher,
        string appVersion,
        ILastSaveFolderStore? lastSaveFolder = null,
        string? attachmentCacheRoot = null,
        ITrustStore? trustStore = null)
    {
        var home = new HomeViewModel(store, masterData, recent, dialogs, clock, ticker, alarm, hostController, appVersion, uiDispatcher, lastSaveFolder, attachmentCacheRoot, trustStore: trustStore);
        var editor = new MasterDataEditorViewModel(masterData, dialogs, masterDataFileService);
        return new MainWindowViewModel(home, editor, dialogs, appVersion);
    }
}
