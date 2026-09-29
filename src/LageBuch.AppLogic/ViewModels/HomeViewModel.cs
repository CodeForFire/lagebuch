using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

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
    private readonly IIncidentPdfExporter _pdfExporter;
    private readonly string _appVersion;

    // Marshals a joined client's host broadcasts onto the UI thread (see IUiDispatcher). Production
    // wires the real dispatcher via CompositionRoot; the immediate default keeps the many non-join
    // HomeViewModel tests (which never open a RemoteIncidentSession) construction-noise free.
    private readonly IUiDispatcher _uiDispatcher;

    // Runs the recent files' closed-state probes (#291), which open each file's SQLite database and
    // must stay off the UI thread. Production wires Task.Run via CompositionRoot; the inline default
    // keeps tests single-threaded, so the fakes need no locking and the markers are set on return.
    private readonly Action<Action> _runInBackground;

    // Where the last new-incident save landed, so the next one opens the picker there instead of
    // wherever the OS last remembered. Null when not supplied (e.g. most tests) -- every use site
    // is null-guarded, so the feature is simply inert rather than required.
    private readonly ILastSaveFolderStore? _lastSaveFolder;

    // The last successful join (#464): prefills the join dialog's host and drives the Home screen's
    // "Zuletzt verbunden" card. Null when not supplied (e.g. most tests) -- every use site is
    // null-guarded, so the feature is simply inert.
    private readonly ILastConnectionStore? _lastConnectionStore;

    // Threaded straight into every IncidentWorkspaceViewModel this opens (#262); null (most tests
    // and every remote/joined workspace) just means "no last-export status to seed or persist".
    private readonly ILastPdfExportStore? _lastPdfExport;
    private readonly IMailComposer? _mailComposer;

    // Where a joined client caches pulled attachment bytes (see RemoteIncidentSession.GetFileBytesAsync).
    // Null (most tests) just means "no caching" -- correct, only not free -- not an error.
    private readonly string? _attachmentCacheRoot;

    // Remembers the TLS thumbprint of each host a device first joined (Trust-on-First-Use), so a
    // re-join that presents a different certificate can be flagged as a potential MITM/duplicate.
    // Null (most tests, which never join a device) just means "join is unavailable" -- ReachDeviceAsync
    // refuses to connect rather than falling back to an unpinned connection (there is no accept-any
    // path any more, see RemoteIncidentSession.ConnectAsync).
    private readonly ITrustStore? _trustStore;

    public HomeViewModel(IIncidentStore store, IMasterDataProvider masterData, IRecentFilesStore recent, IFileDialogService dialogs, IClock clock, ITicker ticker, IAlarmService alarm, IIncidentHostController hostController, string appVersion, IUiDispatcher? uiDispatcher = null, ILastSaveFolderStore? lastSaveFolder = null, string? attachmentCacheRoot = null, ITrustStore? trustStore = null, IIncidentPdfExporter? pdfExporter = null, ILastPdfExportStore? lastPdfExport = null, ILastConnectionStore? lastConnection = null, IMailComposer? mailComposer = null, Action<Action>? runInBackground = null)
    {
        ArgumentNullException.ThrowIfNull(recent);
        _store = store;
        _masterData = masterData;
        _recent = recent;
        _dialogs = dialogs;
        _clock = clock;
        _ticker = ticker;
        _alarm = alarm;
        _hostController = hostController;
        _pdfExporter = pdfExporter ?? new NoopIncidentPdfExporter();
        _appVersion = appVersion;
        _uiDispatcher = uiDispatcher ?? new ImmediateUiDispatcher();
        _runInBackground = runInBackground ?? (work => work());
        _lastSaveFolder = lastSaveFolder;
        _attachmentCacheRoot = attachmentCacheRoot;
        _trustStore = trustStore;
        _lastPdfExport = lastPdfExport;
        _mailComposer = mailComposer;
        _lastConnectionStore = lastConnection;
        _lastConnection = lastConnection?.GetLast();

        // The rows go up unmarked and already in their final order, so the first frame never waits on
        // a file; the lock markers are filled in afterwards without the list reshuffling (#291).
        var rows = SortByFileNameDescending(recent.GetRecent().Select(path => new RecentFileItem(path, IsClosed: false))).ToArray();
        RecentFiles = new ObservableCollection<RecentFileItem>(rows);
        ProbeRecentFileStates(rows);
    }

    public ObservableCollection<RecentFileItem> RecentFiles { get; }

    // Only closed files are posted back: an open or unreadable one already shows no marker.
    private void ProbeRecentFileStates(IReadOnlyList<RecentFileItem> rows) =>
        _runInBackground(() =>
        {
            foreach (var row in rows)
            {
                if (IsClosed(row.Path))
                {
                    _uiDispatcher.Post(() => MarkClosed(row));
                }
            }
        });

    // Matched by reference, not by record equality: if the row was removed in the meantime, or
    // OpenWorkspace replaced it with one built from the loaded incident, the late probe leaves it be.
    private void MarkClosed(RecentFileItem row)
    {
        for (var i = 0; i < RecentFiles.Count; i++)
        {
            if (ReferenceEquals(RecentFiles[i], row))
            {
                RecentFiles[i] = row with { IsClosed = true };
                return;
            }
        }
    }

    // Passive peek: never migrates or mutates the file. A moved, corrupt, or too-new file just
    // shows no marker (TryReadState returns null) rather than blocking the overview. Runs off the
    // UI thread, so a store that throws anyway must not take the probe loop -- or the app -- down.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The marker is cosmetic: any failure reading a file's state means 'no marker', matching TryReadState's null contract.")]
    private bool IsClosed(string path)
    {
        try
        {
            return _store.TryReadState(path) == IncidentState.Closed;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Action<IncidentWorkspaceViewModel>? WorkspaceOpened { get; set; }

    /// <summary>Radio call signs offered as dropdown suggestions in the new-incident operator prompt.</summary>
    public IReadOnlyList<string> CallSignOptions => _masterData.Get().RadioCallSigns;

    /// <summary>Own personnel offered as name suggestions in the operator prompt (#469).</summary>
    public IReadOnlyList<Person> Personnel => _masterData.Get().Personnel;

    /// <summary>The last successful join, or null if this device never joined one (#464).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastConnection))]
    [NotifyPropertyChangedFor(nameof(LastConnectionHost))]
    [NotifyPropertyChangedFor(nameof(LastConnectionDetail))]
    private LastConnection? _lastConnection;

    public bool HasLastConnection => LastConnection is not null;

    public string? LastConnectionHost => LastConnection?.Host;

    /// <summary>
    /// Why the last connection could not be written to disk, or null. The join itself has already
    /// succeeded by then, so this only says the card will be gone after a restart.
    /// </summary>
    [ObservableProperty]
    private string? _lastConnectionError;

    /// <summary>
    /// "‹Stichwort› · ‹time›" under the host on the Home card; the Stichwort is left out while unset.
    /// A line of its own, so a long tailnet name is trimmed rather than broken mid-word on a phone.
    /// </summary>
    public string? LastConnectionDetail => LastConnection is not { } last
        ? null
        : last.Keyword is { Length: > 0 } keyword
            ? $"{keyword} · {Formatting.Timestamp(last.ConnectedAt)}"
            : Formatting.Timestamp(last.ConnectedAt);

    /// <summary>
    /// Raised by the Home card's "Neu verbinden". Home cannot reach the shell's join command, so the
    /// shell subscribes and opens the join dialog through its own navigation path.
    /// </summary>
    public Action? ReconnectRequested { get; set; }

    [RelayCommand]
    private void Reconnect() => ReconnectRequested?.Invoke();

    // The card holds a PIN, so the Lagebuchführer must be able to take it off this device. A
    // still-open session cannot bring it back: its broadcast handler only updates the connection it
    // recorded (see RememberConnection), and there is none left to match.
    [RelayCommand]
    private void ForgetLastConnection()
    {
        try
        {
            _lastConnectionStore?.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastConnectionError = $"Nicht gelöscht: {ex.Message}";
            return;
        }

        LastConnectionError = null;
        LastConnection = null;
    }

    /// <summary>
    /// Why the last open attempt failed, or null. Shown as a banner on the Home screen.
    /// </summary>
    [ObservableProperty]
    private string? _openError;

    /// <summary>
    /// The recent-list entry whose open just failed, or null. Lets the open-failure banner offer to
    /// take that entry off the list (#481); a file picked through the dialog was never listed, so it
    /// leaves this null.
    /// </summary>
    [ObservableProperty]
    private string? _failedRecentPath;

    /// <summary>Why an entry could not be taken off the recent list, or null.</summary>
    [ObservableProperty]
    private string? _recentFilesError;

    /// <summary>
    /// Why the last join attempt failed, or null. Shown as a banner on the Home screen (§7): a
    /// version mismatch, or a host that isn't reachable / isn't currently sharing an incident.
    /// </summary>
    [ObservableProperty]
    private string? _joinError;

    /// <summary>
    /// The dialed address a TOFU certificate-changed failure was just reported for, or null. Drives
    /// <see cref="CanResetTrustedCertificate"/> — set only for that one failure kind (#181), since a
    /// wrong PIN or an unreachable host has nothing to reset.
    /// </summary>
    private string? _certificateChangedHost;

    /// <summary>Whether the Home screen should offer a "reset trust and try again" button.</summary>
    public bool CanResetTrustedCertificate => _certificateChangedHost is not null;

    [RelayCommand]
    private async Task NewIncidentAsync(NewIncidentRequest request)
    {
        // Date + time, e.g. "20260819-2217.fwincident". Neither the Einsatznummer (#69) nor the
        // Stichwort is known at creation -- both are entered later through the workspace's
        // Einsatzdaten dialog -- so nothing but the timestamp is available to name the file.
        var timestamp = _clock.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var suggestedName = $"{timestamp}.fwincident";
        var path = await _dialogs.PickSaveAsync(suggestedName, _lastSaveFolder?.GetLastFolder());
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Remember where this landed so the next new incident's picker opens there too.
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
        {
            _lastSaveFolder?.SetLastFolder(dir);
        }

        var md = _masterData.Get();
        var session = LocalIncidentSession.StartNew(
            _store,
            _clock,
            request.Operator,
            path,
            md.ChecklistTemplates.Select(t => new ChecklistSeed(
                t.Id,
                t.Title,
                t.Items.Select(i => (i.Text, i.IsMandatory)).ToList())).ToList(),
            incidentNumber: null);
        OpenWorkspace(session, path, md);
    }

    // Opening is always read-only and prompt-free. The workspace offers "Weiter bearbeiten"
    // to upgrade a still-open incident to editable (which prompts for the operator there).
    [RelayCommand]
    private void OpenRecent(string path) => TryOpen(path);

    // Only the list entry goes; the .fwincident file is never touched, so nothing is lost and no
    // confirmation is asked for -- the header's ÖFFNEN brings it back.
    [RelayCommand]
    private void RemoveRecent(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            _recent.Remove(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecentFilesError = $"Nicht entfernt: {ex.Message}";
            return;
        }

        RecentFilesError = null;
        if (RecentFiles.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.Ordinal)) is { } item)
        {
            RecentFiles.Remove(item);
        }

        // The banner was about this entry; with it gone there is nothing left to report.
        if (string.Equals(path, FailedRecentPath, StringComparison.Ordinal))
        {
            FailedRecentPath = null;
            OpenError = null;
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var path = await _dialogs.PickOpenAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

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
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Deliberately broad: heterogeneous open failures all get the same user-facing answer.")]
    private void TryOpen(string path)
    {
        try
        {
            var session = LocalIncidentSession.OpenReadOnly(_store, _clock, path);
            OpenError = null;
            FailedRecentPath = null;
            OpenWorkspace(session, path, _masterData.Get());
        }
        catch (Exception ex)
        {
            OpenError = $"{Path.GetFileName(path)} konnte nicht geöffnet werden. {ex.Message}";
            FailedRecentPath = RecentFiles.Any(f => string.Equals(f.Path, path, StringComparison.Ordinal)) ? path : null;
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "Ownership transfers to WorkspaceOpened's subscriber (MainWindowViewModel.ShowWorkspace), which disposes the outgoing workspace itself once CurrentView moves away from it.")]
    private void OpenWorkspace(LocalIncidentSession session, string path, Persistence.MasterData.MasterDataSet md)
    {
        _recent.Add(path);
        var existing = RecentFiles.FirstOrDefault(f => f.Path == path);
        if (existing is not null)
        {
            RecentFiles.Remove(existing);
        }

        InsertSortedByFileNameDescending(new RecentFileItem(path, session.Incident.State == IncidentState.Closed));

        // The local workspace's own saves flow through this same _store singleton, so wiring it
        // through here (issue #167 review follow-up) lets it surface a failed background write
        // that would otherwise leave the operator believing the incident is safely persisted.
        var workspace = new IncidentWorkspaceViewModel(session, _clock, _ticker, md, _dialogs, _alarm, _hostController, _pdfExporter, _lastPdfExport, _store, _uiDispatcher, mailComposer: _mailComposer);
        WorkspaceOpened?.Invoke(workspace);
    }

    // The Übersicht reads chronologically now that filenames start with date+time (#69), so the
    // list is kept sorted by filename (newest first) rather than by open-order/MRU. The underlying
    // recent.json store stays exactly as-is (still MRU-capped) -- only the displayed order changes.
    private static IEnumerable<RecentFileItem> SortByFileNameDescending(IEnumerable<RecentFileItem> items) =>
        items.OrderByDescending(f => f.FileName, StringComparer.OrdinalIgnoreCase);

    private void InsertSortedByFileNameDescending(RecentFileItem item)
    {
        var insertAt = RecentFiles
            .TakeWhile(f => string.Compare(f.FileName, item.FileName, StringComparison.OrdinalIgnoreCase) >= 0)
            .Count();
        RecentFiles.Insert(insertAt, item);
    }

    // ===== Multi-device join (#52 §4/§6): connect to another device's hosted incident as a thin client. =====
    // Two steps (#459): ReachDevice opens the connection and reads the host's Stammdaten and incident,
    // so the dialog can suggest the host's own personnel as Lagebuchführer; JoinDevice then completes
    // it for whoever was chosen. The reached-but-not-joined connection is held here in between.
    private PendingJoin? _pendingJoin;

    /// <summary>The host's Stammdaten once <see cref="ReachDeviceCommand"/> succeeded, else null.</summary>
    public MasterDataSet? PendingJoinMasterData => _pendingJoin?.MasterData;

    /// <summary>The host's incident once <see cref="ReachDeviceCommand"/> succeeded, else null.</summary>
    public Incident? PendingJoinIncident => _pendingJoin?.Join.Incident;

    // IncludeCancelCommand: a join can hang (host unreachable but not yet timed out), so the view
    // offers a Cancel affordance bound to the generated ReachDeviceCancelCommand while IsRunning.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ReachDeviceAsync(DeviceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await DiscardPendingJoinAsync();

        // Unreachable in production (both app heads always construct this view model with a real
        // JsonTrustStore) -- only a misconfigured caller (or a test that never meant to join) ends up
        // here. RemoteJoin.OpenAsync has no accept-any fallback any more, so this must fail the same
        // graceful way as every other join precondition, not throw and take the app down.
        if (_trustStore is not { } trustStore)
        {
            JoinError = "Kein Trust Store konfiguriert — Verbindung zu anderen Geräten ist nicht möglich.";
            ClearCertificateChangedHost();
            return;
        }

        var (host, port) = ParseHost(request.Host);
        try
        {
            var join = await RemoteJoin.OpenAsync(host, _appVersion, trustStore, request.Pin, port, cancellationToken);

            // The host is the Stammdaten master (#183): the dialog's suggestions and later the
            // workspace are built from the host's set, never this device's. Parsed before anything
            // else happens, and the connection released if it fails.
            MasterDataSet hostMasterData;
            try
            {
                hostMasterData = MasterDataJson.Parse(join.HostMasterDataJson);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException
                                          or KeyNotFoundException or FormatException)
            {
                // JsonDocument.Parse only throws JsonException, and only for malformed JSON. A
                // well-formed-but-wrong-shape body ("[]", a personnel entry missing "lastName", a
                // fractional "seats") makes ParseRoot's TryGetProperty/GetProperty/GetString/GetInt32
                // calls throw InvalidOperationException, KeyNotFoundException or FormatException
                // instead — every one of those shapes must land here too, or the DisposeAsync below
                // never runs.
                await join.DisposeAsync();
                throw new HostMasterDataUnreadableException(
                    $"Stammdaten des Hosts konnten nicht gelesen werden. ({ex.Message})", ex);
            }

            JoinError = null;
            ClearCertificateChangedHost();
            _pendingJoin = new PendingJoin(join, request.Host, request.Pin, hostMasterData);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User cancelled via ReachDeviceCancelCommand — leave them in the dialog without an error.
        }
        catch (Exception ex) when (IsJoinFailure(ex))
        {
            ReportJoinFailure(ex, request.Host, host);
        }
    }

    // Completes the join ReachDevice opened, for the Lagebuchführer the dialog collected. A failure
    // here (the host went away while the name was typed) spends the connection: the dialog goes back
    // to asking for host and PIN.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task JoinDeviceAsync(SessionOperator op, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (_pendingJoin is not { } pending)
        {
            JoinError = "Keine Verbindung zu einem Gerät. Bitte erneut verbinden.";
            return;
        }

        _pendingJoin = null;
        try
        {
            var session = await pending.Join.ConnectAsync(
                op, _uiDispatcher, cacheRoot: _attachmentCacheRoot, ct: cancellationToken);
            JoinError = null;
            RememberConnection(session, pending.Address, pending.Pin);
            OpenRemoteWorkspace(session, pending.MasterData, pending.Address);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User cancelled via JoinDeviceCancelCommand. The connection is spent either way.
        }
        catch (Exception ex) when (IsJoinFailure(ex))
        {
            ReportJoinFailure(ex, pending.Address, ParseHost(pending.Address).Host);
        }
        finally
        {
            // A no-op once ConnectAsync handed the connection to the session.
            await pending.Join.DisposeAsync();
        }
    }

    /// <summary>
    /// Releases a device reached but not joined, when the dialog is closed in between. Idempotent.
    /// </summary>
    public async Task DiscardPendingJoinAsync()
    {
        if (_pendingJoin is { } pending)
        {
            _pendingJoin = null;
            await pending.Join.DisposeAsync();
        }
    }

    // The failures either join step expects and reports as JoinError; anything else is a bug and
    // escapes. Unexpected OperationCanceledExceptions are covered by TaskCanceledException below.
    private static bool IsJoinFailure(Exception ex) =>
        ex is PinRejectedException or VersionMismatchException or CertificateChangedException
            or HostMasterDataUnreadableException
            or HttpRequestException or SocketException or TaskCanceledException;

    private void ReportJoinFailure(Exception ex, string address, string host)
    {
        switch (ex)
        {
            case CertificateChangedException:
                // The host presented a different TLS cert than the one previously trusted for this
                // address (Trust-on-First-Use violation, § P0 #2) — a restart with a new ephemeral
                // cert, or a man-in-the-middle. Surface the German "geändert" message, and remember
                // the address so the dialog can offer "Vertrauen zurücksetzen" (#181) instead of
                // leaving the user stuck on a warning nobody can act on.
                JoinError = ex.Message;
                _certificateChangedHost = host;
                OnPropertyChanged(nameof(CanResetTrustedCertificate));
                return;

            case PinRejectedException or VersionMismatchException or HostMasterDataUnreadableException:
                // A wrong/missing share PIN; a wire contract that does not overlap — expected across
                // an un-auto-updated fleet (§7) and named explicitly; or a Stammdaten payload that
                // cannot be read, which means corruption or something past the TOFU pin and refuses
                // the join rather than degrading into it. Each carries its own German message.
                JoinError = ex.Message;
                break;

            default:
                // Host unreachable, or up but not currently sharing an incident — same answer for the
                // user: say which device and why, and let them try again.
                JoinError = $"Verbindung zu {address} nicht möglich. Teilt dieses Gerät gerade einen Einsatz? ({ex.Message})";
                break;
        }

        ClearCertificateChangedHost();
    }

    private sealed record PendingJoin(RemoteJoin Join, string Address, string? Pin, MasterDataSet MasterData);

    // The Stichwort is often typed on the host only after this client joined, so the record follows
    // the session's broadcasts. The session is disposed when the workspace is left, which ends
    // them; the ConnectedAt guard keeps a late broadcast from an earlier session from overwriting a
    // newer join.
    private void RememberConnection(RemoteIncidentSession session, string host, string? pin)
    {
        if (_lastConnectionStore is null)
        {
            return;
        }

        var connectedAt = _clock.Now;
        SaveLastConnection(new LastConnection(host, session.Incident.Keyword, connectedAt, pin));
        session.Changed += () =>
        {
            if (LastConnection is { } last && last.ConnectedAt == connectedAt
                && !string.Equals(last.Keyword, session.Incident.Keyword, StringComparison.Ordinal))
            {
                SaveLastConnection(last with { Keyword = session.Incident.Keyword });
            }
        };
    }

    // A convenience file must not undo a join that already succeeded: an exception here would
    // escape JoinDeviceAsync with the session open, or a host broadcast on the UI thread.
    private void SaveLastConnection(LastConnection connection)
    {
        try
        {
            _lastConnectionStore?.SetLast(connection);
            LastConnectionError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastConnectionError = $"Nicht gespeichert: {ex.Message}";
        }

        // Shown either way: after a failed write the card still holds for the rest of this run.
        LastConnection = connection;
    }

    private void ClearCertificateChangedHost()
    {
        if (_certificateChangedHost is null)
        {
            return;
        }

        _certificateChangedHost = null;
        OnPropertyChanged(nameof(CanResetTrustedCertificate));
    }

    /// <summary>
    /// Forgets the TLS thumbprint pinned for the host that just failed a TOFU check, so the next join
    /// attempt re-pins whatever certificate it presents (#181). This is the user's only way out of a
    /// "Zertifikat geändert" banner short of hand-editing the trust store file.
    /// </summary>
    [RelayCommand]
    private void ResetTrustedCertificate()
    {
        if (_certificateChangedHost is not { } host)
        {
            return;
        }

        _trustStore?.RemoveThumbprint(host);
        JoinError = null;
        ClearCertificateChangedHost();
    }

    // The address is normally just a Tailscale name (the host binds the fixed SyncProtocol.Port), but
    // an explicit "host:port" is accepted too — handy for a non-standard port or for reaching a host
    // on the same machine during testing.
    private static (string Host, int Port) ParseHost(string address)
    {
        var trimmed = address.Trim();
        var colon = trimmed.LastIndexOf(':');
        if (colon > 0 && int.TryParse(trimmed[(colon + 1)..], out var port))
        {
            return (trimmed[..colon], port);
        }

        return (trimmed, SyncProtocol.Port);
    }

    // The remote workspace can't host (a client isn't hostable), so it gets a no-op host controller
    // and the "Im Netzwerk freigeben" toggle stays hidden. It does get the PDF exporter: a client
    // renders the synced state itself (#465). It gets no store, since it writes nothing locally.
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "Ownership transfers to WorkspaceOpened's subscriber (MainWindowViewModel.ShowWorkspace), which disposes the outgoing workspace itself once CurrentView moves away from it.")]
    private void OpenRemoteWorkspace(RemoteIncidentSession session, MasterDataSet md, string host)
    {
        var workspace = new IncidentWorkspaceViewModel(
            session, _clock, _ticker, md, _dialogs, _alarm, new NoopIncidentHostController(), _pdfExporter, _lastPdfExport, remoteHost: host);
        WorkspaceOpened?.Invoke(workspace);
    }
}
