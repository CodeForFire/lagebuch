using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IIncidentStore _store;
    private readonly IMasterDataProvider _masterData;
    private readonly IRecentFilesStore _recent;
    private readonly IFileDialogService _dialogs;
    private readonly IClock _clock;
    private readonly ITicker _ticker;

    public HomeViewModel(IIncidentStore store, IMasterDataProvider masterData, IRecentFilesStore recent, IFileDialogService dialogs, IClock clock, ITicker ticker)
    {
        _store = store;
        _masterData = masterData;
        _recent = recent;
        _dialogs = dialogs;
        _clock = clock;
        _ticker = ticker;
        RecentFiles = new ObservableCollection<string>(recent.GetRecent());
    }

    public ObservableCollection<string> RecentFiles { get; }
    public Action<IncidentWorkspaceViewModel>? WorkspaceOpened { get; set; }

    [RelayCommand]
    private async Task NewIncidentAsync(SessionOperator op)
    {
        var path = await _dialogs.PickSaveAsync("Einsatz.fwincident");
        if (string.IsNullOrWhiteSpace(path))
            return;
        var md = _masterData.Get();
        var session = IncidentSession.StartNew(_store, _clock, op, path, md.ChecklistTemplate);
        OpenWorkspace(session, path, md);
    }

    // Opening is always read-only and prompt-free. The workspace offers "Weiter bearbeiten"
    // to upgrade a still-open incident to editable (which prompts for the operator there).
    [RelayCommand]
    private void OpenRecent(string path) =>
        OpenWorkspace(IncidentSession.OpenReadOnly(_store, path), path, _masterData.Get());

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var path = await _dialogs.PickOpenAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        OpenWorkspace(IncidentSession.OpenReadOnly(_store, path), path, _masterData.Get());
    }

    private void OpenWorkspace(IncidentSession session, string path, Persistence.MasterData.MasterDataSet md)
    {
        _recent.Add(path);
        if (!RecentFiles.Contains(path))
            RecentFiles.Insert(0, path);
        var workspace = new IncidentWorkspaceViewModel(session, _clock, _ticker, md, _dialogs);
        WorkspaceOpened?.Invoke(workspace);
    }
}
