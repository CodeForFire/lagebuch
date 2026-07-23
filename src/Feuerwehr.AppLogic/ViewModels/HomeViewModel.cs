using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly IAlarmService _alarm;

    public HomeViewModel(IIncidentStore store, IMasterDataProvider masterData, IRecentFilesStore recent, IFileDialogService dialogs, IClock clock, ITicker ticker, IAlarmService alarm)
    {
        _store = store;
        _masterData = masterData;
        _recent = recent;
        _dialogs = dialogs;
        _clock = clock;
        _ticker = ticker;
        _alarm = alarm;
        RecentFiles = new ObservableCollection<RecentFileItem>(
            recent.GetRecent().Select(path => new RecentFileItem(path, IsClosed(path))));
    }

    public ObservableCollection<RecentFileItem> RecentFiles { get; }

    // Passive peek: never migrates or mutates the file. A moved, corrupt, or too-new file just
    // shows no marker (TryReadState returns null) rather than blocking the overview.
    private bool IsClosed(string path) => _store.TryReadState(path) == IncidentState.Closed;
    public Action<IncidentWorkspaceViewModel>? WorkspaceOpened { get; set; }

    /// <summary>Radio call signs offered as dropdown suggestions in the new-incident operator prompt.</summary>
    public IReadOnlyList<string> CallSignOptions => _masterData.Get().RadioCallSigns;

    /// <summary>
    /// Why the last open attempt failed, or null. Shown as a banner on the Home screen.
    /// </summary>
    [ObservableProperty]
    private string? _openError;

    [RelayCommand]
    private async Task NewIncidentAsync(NewIncidentRequest request)
    {
        var date = _clock.Now.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var suggestedName = request.IlsNumber is { } ils
            ? $"Einsatz-{ils.Value}-{date}.fwincident"
            : $"Einsatz-{date}.fwincident";
        var path = await _dialogs.PickSaveAsync(suggestedName);
        if (string.IsNullOrWhiteSpace(path))
            return;
        var md = _masterData.Get();
        var session = IncidentSession.StartNew(
            _store, _clock, request.Operator, path, md.ChecklistTemplate, request.IlsNumber);
        OpenWorkspace(session, path, md);
    }

    // Opening is always read-only and prompt-free. The workspace offers "Weiter bearbeiten"
    // to upgrade a still-open incident to editable (which prompts for the operator there).
    [RelayCommand]
    private void OpenRecent(string path) => TryOpen(path);

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var path = await _dialogs.PickOpenAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        TryOpen(path);
    }

    /// <summary>
    /// Opens a file, turning any failure into a banner rather than an unhandled exception.
    ///
    /// The catch is deliberately broad. Every everyday reason an open fails -- the file was moved,
    /// truncated, written by a newer build, or was never a .fwincident at all -- surfaces here as a
    /// different exception type, and on the Home screen they all have the same answer: tell the
    /// user which file and why, and leave the app standing. Letting any of them escape kills the
    /// process, which during an Einsatz is the worst possible outcome.
    /// </summary>
    private void TryOpen(string path)
    {
        try
        {
            var session = IncidentSession.OpenReadOnly(_store, path);
            OpenError = null;
            OpenWorkspace(session, path, _masterData.Get());
        }
        catch (Exception ex)
        {
            OpenError = $"{Path.GetFileName(path)} konnte nicht geöffnet werden. {ex.Message}";
        }
    }

    private void OpenWorkspace(IncidentSession session, string path, Persistence.MasterData.MasterDataSet md)
    {
        _recent.Add(path);
        var existing = RecentFiles.FirstOrDefault(f => f.Path == path);
        if (existing is not null)
            RecentFiles.Remove(existing);
        RecentFiles.Insert(0, new RecentFileItem(path, session.Incident.State == IncidentState.Closed));
        var workspace = new IncidentWorkspaceViewModel(session, _clock, _ticker, md, _dialogs, _alarm);
        WorkspaceOpened?.Invoke(workspace);
    }
}
