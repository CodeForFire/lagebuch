using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Documents;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class IncidentWorkspaceViewModel : ObservableObject
{
    private readonly IncidentSession _session;
    private readonly IClock _clock;
    private readonly MasterDataSet _masterData;
    private readonly IFileDialogService _dialogs;

    public IncidentWorkspaceViewModel(IncidentSession session, IClock clock, MasterDataSet masterData, IFileDialogService dialogs)
    {
        _session = session;
        _clock = clock;
        _masterData = masterData;
        _dialogs = dialogs;
        IsReadOnly = session.IsReadOnly;
        BuildChildren();
    }

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private DateTimeOffset? _lastSavedAt;

    public ChecklistViewModel Checklist { get; private set; } = null!;
    public EtbViewModel Etb { get; private set; } = null!;
    public RolesViewModel Roles { get; private set; } = null!;
    public ForcesViewModel Forces { get; private set; } = null!;

    public string IncidentNumberDisplay => Formatting.OrDash(_session.Incident.IncidentNumber?.Value);
    public string IlsNumberDisplay => Formatting.OrDash(_session.Incident.IlsNumber?.Value);
    public string StatusDisplay => Formatting.State(_session.Incident.State);

    private void OnChanged()
    {
        _session.Save();
        LastSavedAt = _clock.Now;
    }

    private void BuildChildren()
    {
        Checklist = new ChecklistViewModel(_session, OnChanged);
        Etb = new EtbViewModel(_session, _clock, OnChanged);
        Roles = new RolesViewModel(_session, _masterData, OnChanged);
        Forces = new ForcesViewModel(_session, _masterData, OnChanged);
        OnPropertyChanged(nameof(Checklist));
        OnPropertyChanged(nameof(Etb));
        OnPropertyChanged(nameof(Roles));
        OnPropertyChanged(nameof(Forces));
    }

    private bool CanClose => !IsReadOnly;

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void CloseIncident()
    {
        _session.Close(_clock);
        IsReadOnly = true;
        LastSavedAt = _clock.Now;
        BuildChildren();
        CloseIncidentCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(StatusDisplay));
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        var suggested = (_session.Incident.IncidentNumber?.Value ?? "Einsatz") + ".pdf";
        var path = await _dialogs.PickExportPdfAsync(suggested);
        if (string.IsNullOrWhiteSpace(path))
            return;
        await File.WriteAllBytesAsync(path, _session.ExportPdf());
    }
}
