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

    public HomeViewModel(IIncidentStore store, IMasterDataProvider masterData, IRecentFilesStore recent, IFileDialogService dialogs, IClock clock)
    {
        _store = store;
        _masterData = masterData;
        _recent = recent;
        _dialogs = dialogs;
        _clock = clock;
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

    [RelayCommand]
    private void OpenRecent(string path) => OpenExisting(path, op: null);

    [RelayCommand]
    private async Task OpenFileAsync(SessionOperator? op)
    {
        var path = await _dialogs.PickOpenAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        OpenExisting(path, op);
    }

    private void OpenExisting(string path, SessionOperator? op)
    {
        // Peek state: if it's an open incident we need an operator. The App passes one in
        // (after prompting) for the OpenFile flow; OpenRecent relies on Open() throwing if
        // an operator is required, which the App handles by prompting then retrying.
        var probe = _store.Load(path);
        SessionOperator? effectiveOp = probe.State == IncidentState.Open ? op : null;
        if (probe.State == IncidentState.Open && effectiveOp is null)
            return; // App will prompt for operator and retry via OpenFile

        var session = IncidentSession.Open(_store, path, effectiveOp);
        OpenWorkspace(session, path, _masterData.Get());
    }

    private void OpenWorkspace(IncidentSession session, string path, Persistence.MasterData.MasterDataSet md)
    {
        _recent.Add(path);
        if (!RecentFiles.Contains(path))
            RecentFiles.Insert(0, path);
        var workspace = new IncidentWorkspaceViewModel(session, _clock, md, _dialogs);
        WorkspaceOpened?.Invoke(workspace);
    }
}
