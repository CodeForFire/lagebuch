using System.Collections.ObjectModel;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;
using Feuerwehr.Sync;

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
    private readonly IIncidentHostController _hostController;
    private readonly string _appVersion;
    // Marshals a joined client's host broadcasts onto the UI thread (see IUiDispatcher). Production
    // wires the real dispatcher via CompositionRoot; the immediate default keeps the many non-join
    // HomeViewModel tests (which never open a RemoteIncidentSession) construction-noise free.
    private readonly IUiDispatcher _uiDispatcher;

    public HomeViewModel(IIncidentStore store, IMasterDataProvider masterData, IRecentFilesStore recent, IFileDialogService dialogs, IClock clock, ITicker ticker, IAlarmService alarm, IIncidentHostController hostController, string appVersion, IUiDispatcher? uiDispatcher = null)
    {
        _store = store;
        _masterData = masterData;
        _recent = recent;
        _dialogs = dialogs;
        _clock = clock;
        _ticker = ticker;
        _alarm = alarm;
        _hostController = hostController;
        _appVersion = appVersion;
        _uiDispatcher = uiDispatcher ?? new ImmediateUiDispatcher();
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

    /// <summary>Einsatzart values offered as the dropdown in the new-incident operator prompt.</summary>
    public IReadOnlyList<string> EinsatzartOptions => _masterData.Get().Einsatzarten;

    /// <summary>
    /// Why the last open attempt failed, or null. Shown as a banner on the Home screen.
    /// </summary>
    [ObservableProperty]
    private string? _openError;

    /// <summary>
    /// Why the last join attempt failed, or null. Shown as a banner on the Home screen (§7): a
    /// version mismatch, or a host that isn't reachable / isn't currently sharing an incident.
    /// </summary>
    [ObservableProperty]
    private string? _joinError;

    [RelayCommand]
    private async Task NewIncidentAsync(NewIncidentRequest request)
    {
        // The Einsatznummer is mandatory in the new-incident prompt (OperatorPromptViewModel),
        // so request.IncidentNumber should always be set here. The null branch is defense-in-depth
        // only, so this method stays total rather than throwing if that gate is ever bypassed.
        var suggestedName = request.IncidentNumber is { } num
            ? $"Einsatz {StripInvalidFileNameChars(num.Value)}.fwincident"
            : "Einsatz.fwincident";
        var path = await _dialogs.PickSaveAsync(suggestedName);
        if (string.IsNullOrWhiteSpace(path))
            return;
        var md = _masterData.Get();
        var session = LocalIncidentSession.StartNew(
            _store, _clock, request.Operator, path, md.ChecklistTemplate, request.IncidentNumber);
        OpenWorkspace(session, path, md);
    }

    // Filesystem-invalid characters differ per platform; Path.GetInvalidFileNameChars() reflects
    // whichever OS is running, so this drops only what that platform actually rejects and
    // otherwise preserves the composed Einsatznummer verbatim, spaces included.
    private static string StripInvalidFileNameChars(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(c => Array.IndexOf(invalid, c) < 0).ToArray());
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
            var session = LocalIncidentSession.OpenReadOnly(_store, _clock, path);
            OpenError = null;
            OpenWorkspace(session, path, _masterData.Get());
        }
        catch (Exception ex)
        {
            OpenError = $"{Path.GetFileName(path)} konnte nicht geöffnet werden. {ex.Message}";
        }
    }

    private void OpenWorkspace(LocalIncidentSession session, string path, Persistence.MasterData.MasterDataSet md)
    {
        _recent.Add(path);
        var existing = RecentFiles.FirstOrDefault(f => f.Path == path);
        if (existing is not null)
            RecentFiles.Remove(existing);
        RecentFiles.Insert(0, new RecentFileItem(path, session.Incident.State == IncidentState.Closed));
        var workspace = new IncidentWorkspaceViewModel(session, _clock, _ticker, md, _dialogs, _alarm, _hostController);
        WorkspaceOpened?.Invoke(workspace);
    }

    // ===== Multi-device join (#52 §4/§6): connect to another device's hosted incident as a thin client. =====

    [RelayCommand]
    private async Task JoinDeviceAsync(JoinRequest request)
    {
        var (host, port) = ParseHost(request.Host);
        try
        {
            var session = await RemoteIncidentSession.ConnectAsync(
                host, request.Operator, _appVersion, _uiDispatcher, request.Pin, port);
            JoinError = null;
            OpenRemoteWorkspace(session, _masterData.Get());
        }
        catch (PinRejectedException ex)
        {
            // Wrong/missing share PIN — say so plainly and leave them on Home to retry.
            JoinError = ex.Message;
        }
        catch (VersionMismatchException ex)
        {
            // Distinct, explicit message: mixed versions across an un-auto-updated fleet are expected (§7).
            JoinError = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TaskCanceledException)
        {
            // Host unreachable, or up but not currently sharing an incident — same answer for the user:
            // say which device and why, and leave them on Home to try again.
            JoinError = $"Verbindung zu {request.Host} nicht möglich. Teilt dieses Gerät gerade einen Einsatz? ({ex.Message})";
        }
    }

    // The address is normally just a Tailscale name (the host binds the fixed SyncProtocol.Port), but
    // an explicit "host:port" is accepted too — handy for a non-standard port or for reaching a host
    // on the same machine during testing.
    private static (string Host, int Port) ParseHost(string address)
    {
        var trimmed = address.Trim();
        var colon = trimmed.LastIndexOf(':');
        if (colon > 0 && int.TryParse(trimmed[(colon + 1)..], out var port))
            return (trimmed[..colon], port);
        return (trimmed, SyncProtocol.Port);
    }

    // The remote workspace can't host (a client isn't hostable) and has no local file, so it gets a
    // no-op host controller — the "Im Netzwerk freigeben" toggle and PDF export stay hidden.
    private void OpenRemoteWorkspace(RemoteIncidentSession session, MasterDataSet md)
    {
        var workspace = new IncidentWorkspaceViewModel(
            session, _clock, _ticker, md, _dialogs, _alarm, new NoopIncidentHostController());
        WorkspaceOpened?.Invoke(workspace);
    }
}
