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
    private readonly string _appVersion;

    // Marshals a joined client's host broadcasts onto the UI thread (see IUiDispatcher). Production
    // wires the real dispatcher via CompositionRoot; the immediate default keeps the many non-join
    // HomeViewModel tests (which never open a RemoteIncidentSession) construction-noise free.
    private readonly IUiDispatcher _uiDispatcher;

    // Where the last new-incident save landed, so the next one opens the picker there instead of
    // wherever the OS last remembered. Null when not supplied (e.g. most tests) -- every use site
    // is null-guarded, so the feature is simply inert rather than required.
    private readonly ILastSaveFolderStore? _lastSaveFolder;

    // Where a joined client caches pulled attachment bytes (see RemoteIncidentSession.GetFileBytesAsync).
    // Null (most tests) just means "no caching" -- correct, only not free -- not an error.
    private readonly string? _attachmentCacheRoot;

    // Remembers the TLS thumbprint of each host a device first joined (Trust-on-First-Use), so a
    // re-join that presents a different certificate can be flagged as a potential MITM/duplicate.
    // Null (most tests) means "trust nothing and never record" -- the join then fails on any cert
    // mismatch but has no store to compare against, so every first join succeeds without TOFU.
    private readonly ITrustStore? _trustStore;

    public HomeViewModel(IIncidentStore store, IMasterDataProvider masterData, IRecentFilesStore recent, IFileDialogService dialogs, IClock clock, ITicker ticker, IAlarmService alarm, IIncidentHostController hostController, string appVersion, IUiDispatcher? uiDispatcher = null, ILastSaveFolderStore? lastSaveFolder = null, string? attachmentCacheRoot = null, ITrustStore? trustStore = null)
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
        _appVersion = appVersion;
        _uiDispatcher = uiDispatcher ?? new ImmediateUiDispatcher();
        _lastSaveFolder = lastSaveFolder;
        _attachmentCacheRoot = attachmentCacheRoot;
        _trustStore = trustStore;
        RecentFiles = new ObservableCollection<RecentFileItem>(
            SortByFileNameDescending(recent.GetRecent().Select(path => new RecentFileItem(path, IsClosed(path)))));
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
        // Date + time + Stichwort, e.g. "20260819-2217-B3P.fwincident" -- the Einsatznummer is
        // unknown at creation (#69) and no longer part of the filename; it can be added later from
        // the workspace header. No Stichwort at all just leaves the timestamp alone.
        var timestamp = _clock.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var stem = string.IsNullOrWhiteSpace(request.Keyword)
            ? timestamp
            : $"{timestamp}-{StripInvalidFileNameChars(request.Keyword.Trim())}";
        var suggestedName = $"{stem}.fwincident";
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
            md.ChecklistTemplateAufbau.Select(i => (i.Text, i.IsMandatory)),
            md.ChecklistTemplateAbbau.Select(i => (i.Text, i.IsMandatory)),
            incidentNumber: null,
            keyword: request.Keyword);
        OpenWorkspace(session, path, md);
    }

    // Filesystem-invalid characters differ per platform; Path.GetInvalidFileNameChars() reflects
    // whichever OS is running, so this drops only what that platform actually rejects and
    // otherwise preserves the input verbatim, spaces included.
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
        {
            RecentFiles.Remove(existing);
        }

        InsertSortedByFileNameDescending(new RecentFileItem(path, session.Incident.State == IncidentState.Closed));
        var workspace = new IncidentWorkspaceViewModel(session, _clock, _ticker, md, _dialogs, _alarm, _hostController);
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
    [RelayCommand]
    private async Task JoinDeviceAsync(JoinRequest request)
    {
        var (host, port) = ParseHost(request.Host);
        try
        {
            var session = await RemoteIncidentSession.ConnectAsync(
                host, request.Operator, _appVersion, _uiDispatcher, request.Pin, port, cacheRoot: _attachmentCacheRoot, trustStore: _trustStore);

            // The host is the Stammdaten master (#183): the workspace is built from the host's set,
            // never this device's. Parsed before anything opens, and the session is torn down if it
            // fails — ConnectAsync has already opened a hub connection by this point, so simply
            // letting the throw travel would leak it.
            MasterDataSet hostMasterData;
            try
            {
                hostMasterData = MasterDataJson.Parse(session.HostMasterDataJson);
            }
            catch (JsonException)
            {
                await session.DisposeAsync();
                throw;
            }

            JoinError = null;
            _certificateChangedHost = null;
            OnPropertyChanged(nameof(CanResetTrustedCertificate));
            OpenRemoteWorkspace(session, hostMasterData);
        }
        catch (PinRejectedException ex)
        {
            // Wrong/missing share PIN — say so plainly and leave them on Home to retry.
            JoinError = ex.Message;
            ClearCertificateChangedHost();
        }
        catch (VersionMismatchException ex)
        {
            // Distinct, explicit message: mixed versions across an un-auto-updated fleet are expected (§7).
            JoinError = ex.Message;
            ClearCertificateChangedHost();
        }
        catch (CertificateChangedException ex)
        {
            // The host presented a different TLS cert than the one previously trusted for this address
            // (Trust-on-First-Use violation, § P0 #2) — a restart with a new ephemeral cert, or a
            // man-in-the-middle. Surface the German "geändert" message, and remember the address so
            // the Home screen can offer a "Vertrauen zurücksetzen" button (#181) instead of leaving the
            // user stuck on a warning nobody can act on.
            JoinError = ex.Message;
            _certificateChangedHost = host;
            OnPropertyChanged(nameof(CanResetTrustedCertificate));
        }
        catch (JsonException ex)
        {
            // Both ends run the same app version (the handshake enforces it), so an unparseable
            // Stammdaten payload means corruption or something past the TOFU pin. Refuse the join
            // rather than degrade into it — and never let it escape: an unhandled throw here kills
            // the app mid-Einsatz.
            JoinError = $"Stammdaten des Hosts konnten nicht gelesen werden. ({ex.Message})";
            ClearCertificateChangedHost();
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TaskCanceledException)
        {
            // Host unreachable, or up but not currently sharing an incident — same answer for the user:
            // say which device and why, and leave them on Home to try again.
            JoinError = $"Verbindung zu {request.Host} nicht möglich. Teilt dieses Gerät gerade einen Einsatz? ({ex.Message})";
            ClearCertificateChangedHost();
        }
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

    // The remote workspace can't host (a client isn't hostable) and has no local file, so it gets a
    // no-op host controller — the "Im Netzwerk freigeben" toggle and PDF export stay hidden.
    private void OpenRemoteWorkspace(RemoteIncidentSession session, MasterDataSet md)
    {
        var workspace = new IncidentWorkspaceViewModel(
            session, _clock, _ticker, md, _dialogs, _alarm, new NoopIncidentHostController());
        WorkspaceOpened?.Invoke(workspace);
    }
}
