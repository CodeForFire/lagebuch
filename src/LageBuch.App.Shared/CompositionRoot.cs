using System.Diagnostics.CodeAnalysis;
using LageBuch.App.Shared.Services;
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
    /// <summary>
    /// Raw-served manifest of published Wasserförderung region packs (#150 follow-up) — see
    /// tools/build-region-pack/README.md for how a pack is built and published here.
    /// </summary>
    public const string RegionPackManifestUrl =
        "https://raw.githubusercontent.com/CodeForFire/lagebuch-regions/main/regions.json";

    [SuppressMessage("Reliability", "CA2000", Justification = "The HttpClient is shared by the region-pack catalog/installer services for the app's whole lifetime, not disposed per-call.")]
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
        string regionsDir,
        ILastSaveFolderStore? lastSaveFolder = null,
        string? attachmentCacheRoot = null)
    {
        var home = new HomeViewModel(store, masterData, recent, dialogs, clock, ticker, alarm, hostController, appVersion, uiDispatcher, lastSaveFolder, attachmentCacheRoot, new RouteOverviewRenderer());
        var httpClient = new HttpClient();
        var regionCatalog = new RegionPackCatalogService(httpClient, RegionPackManifestUrl);
        var regionInstaller = new RegionPackInstaller(httpClient, regionsDir);
        var editor = new MasterDataEditorViewModel(masterData, dialogs, masterDataFileService, regionCatalog, regionInstaller);
        return new MainWindowViewModel(home, editor, dialogs, appVersion);
    }
}
