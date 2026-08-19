using Feuerwehr.AppLogic.Services;
using Feuerwehr.Documents;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Files;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;
using Feuerwehr.Sync;

namespace Feuerwehr.AppLogic;

/// <summary>
/// The default, fully-offline session: mutations apply straight to the in-memory
/// <see cref="Incident"/>, persist via <see cref="IIncidentStore"/>, and raise <see cref="Changed"/>.
/// Today's solo mode is exactly this with no clients connected. It owns the clock and operator, so
/// the <see cref="IIncidentSession"/> mutation methods drop the arguments the domain methods take.
/// </summary>
public sealed class LocalIncidentSession : IIncidentSession
{
    private readonly IIncidentStore _store;
    private readonly IClock _clock;

    private LocalIncidentSession(IIncidentStore store, IClock clock, Incident incident, string path, SessionOperator? op)
    {
        _store = store;
        _clock = clock;
        Incident = incident;
        Path = path;
        Operator = op;
    }

    public Incident Incident { get; }
    public string Path { get; }
    public SessionOperator? Operator { get; private set; }

    public event Action? Changed;

    // Read-only when there is no operator to attribute edits to, OR the incident is closed
    // (closing keeps its operator for attribution, but a closed incident is irreversibly
    // read-only — the domain rejects mutations either way).
    public bool IsReadOnly => Operator is null || Incident.State == IncidentState.Closed;

    // This device is authoritative — it does its own time-driven logging (see IIncidentSession.IsRemote).
    public bool IsRemote => false;

    public static LocalIncidentSession StartNew(
        IIncidentStore store,
        IClock clock,
        SessionOperator op,
        string path,
        IEnumerable<(string Text, bool IsMandatory)> checklistTemplateAufbau,
        IEnumerable<(string Text, bool IsMandatory)> checklistTemplateAbbau,
        IncidentNumber? incidentNumber = null,
        string? keyword = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(op);
        // The Einsatznummer goes through the factory rather than SetIncidentNumber afterwards, so the
        // automatic "Einsatz begonnen" entry can name it.
        var incident = Incident.Start(clock, op, keyword: keyword, incidentNumber: incidentNumber);
        incident.SeedChecklist(checklistTemplateAufbau, checklistTemplateAbbau);
        var session = new LocalIncidentSession(store, clock, incident, path, op);
        session.Save();
        return session;
    }

    public static LocalIncidentSession Open(IIncidentStore store, IClock clock, string path, SessionOperator? op)
    {
        ArgumentNullException.ThrowIfNull(store);
        var incident = store.Load(path);
        if (incident.State == IncidentState.Open && op is null)
            throw new InvalidOperationException("Ein offener Einsatz erfordert einen Bearbeiter.");
        var effectiveOperator = incident.State == IncidentState.Closed ? null : op;
        return new LocalIncidentSession(store, clock, incident, path, effectiveOperator);
    }

    // Opens any incident read-only, regardless of state, without requiring an operator.
    // The workspace can later upgrade an open incident to editable via ContinueEditing.
    public static LocalIncidentSession OpenReadOnly(IIncidentStore store, IClock clock, string path)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new LocalIncidentSession(store, clock, store.Load(path), path, op: null);
    }

    // Upgrades a read-only-opened, still-open incident to editable by attaching an operator.
    public void ContinueEditing(SessionOperator op)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (Incident.State == IncidentState.Closed)
            throw new InvalidOperationException("Ein abgeschlossener Einsatz kann nicht weiter bearbeitet werden.");
        if (Operator is not null)
            return; // already editable
        Operator = op;
        Incident.ResumeEditing(_clock, op);
        Save();
        Changed?.Invoke();
    }

    // Export is host-only (IncidentWorkspaceViewModel.CanExport gates on _local being non-null), and
    // every attached file's bytes land in this device's own sibling folder the moment it's added —
    // whether typed here or uploaded by a joined client via AddFileCommand — so this never needs a
    // network pull, only IIncidentStore.
    public Task<byte[]> ExportPdfAsync()
    {
        var fileBytes = new Dictionary<Guid, byte[]>();
        foreach (var file in Incident.Files)
        {
            var bytes = _store.TryReadFileBytes(Path, IncidentFile.StorageFileName(file.Id, file.FileName));
            if (bytes is not null)
                fileBytes[file.Id] = bytes;
        }
        return Task.FromResult(IncidentPdf.Generate(Incident, fileBytes));
    }

    // --- IIncidentSession mutation surface: apply → persist → notify. ---

    public void AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null) =>
        Mutate(() => Incident.AddJournalEntry(_clock, RequireOperator(), direction, text, from, to));

    public void ToggleChecklistItem(Guid itemId) => Mutate(() => Incident.ToggleChecklistItem(_clock, RequireOperator(), itemId));

    public void AssignRole(string role, string personName, string? callSign = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? section = null, string? phone = null) =>
        Mutate(() => Incident.AssignRole(role, personName, callSign, from, to, section, phone));

    public void EndRoleAssignment(Guid assignmentId) =>
        Mutate(() => Incident.EndRoleAssignment(assignmentId, _clock.Now));

    public void AddForceUnit(string brigade, int personnelCount, string? callSign = null,
        string? status = null, string? notes = null, int scbaCount = 0) =>
        Mutate(() => Incident.AddForceUnit(_clock, RequireOperator(), brigade, personnelCount, callSign, status, notes, scbaCount));

    public void UpdateForceUnit(Guid unitId, string? status, string? notes) =>
        Mutate(() => Incident.UpdateForceUnit(_clock, RequireOperator(), unitId, status, notes));

    public void AddScbaTrupp(string designation, IEnumerable<TruppMember> members, string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes) =>
        Mutate(() => Incident.AddScbaTrupp(_clock, designation, members, callSign, task,
            maxDurationMinutes, returnPressureBar, pressureControlIntervalMinutes));

    public void StartScbaTrupp(Guid truppId, int startPressure) =>
        Mutate(() => Incident.StartScbaTrupp(_clock, truppId, startPressure));

    public void RecordScbaPressure(Guid truppId, int bar) =>
        Mutate(() => Incident.RecordScbaPressure(_clock, truppId, bar));

    public void MarkScbaReturned(Guid truppId) =>
        Mutate(() => Incident.MarkScbaReturned(_clock, truppId));

    public void SetIncidentNumber(IncidentNumber? number) => Mutate(() => Incident.SetIncidentNumber(number));
    public void SetKeyword(string? keyword) => Mutate(() => Incident.SetKeyword(keyword));
    public void SetAddress(string? street, string? district) => Mutate(() => Incident.SetAddress(street, district));
    public void SetStatus(string? status) => Mutate(() => Incident.SetStatus(status));

    public void UpsertTimer(string key, DateTimeOffset cycleAnchor, int intervalMinutes, int recurringIntervalMinutes, bool isRunning) =>
        Mutate(() => Incident.UpsertTimer(key, cycleAnchor, intervalMinutes, recurringIntervalMinutes, isRunning));

    public Task AddFileAsync(string fileName, string contentType, byte[] bytes)
    {
        var file = Incident.AddFile(_clock, RequireOperator(), fileName, contentType, bytes.LongLength);
        _store.SaveFileBytes(Path, IncidentFile.StorageFileName(file.Id, file.FileName), bytes);
        Save();
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes attachment bytes already recorded on <see cref="Incident"/> elsewhere — used by the
    /// host applying a joined client's <c>AddFileCommand</c> (see <c>CommandApplier</c>'s
    /// <c>saveFileBytes</c> callback), where the metadata mutation and the byte write are two
    /// separate steps rather than going through <see cref="AddFileAsync"/>.
    /// </summary>
    public void SaveFileBytes(string storageFileName, byte[] bytes) => _store.SaveFileBytes(Path, storageFileName, bytes);

    public Task<byte[]?> GetFileBytesAsync(Guid fileId)
    {
        var file = Incident.Files.FirstOrDefault(f => f.Id == fileId);
        var bytes = file is null ? null : _store.TryReadFileBytes(Path, IncidentFile.StorageFileName(file.Id, file.FileName));
        return Task.FromResult(bytes);
    }

    public void RenameFile(Guid fileId, string? displayName) => Mutate(() => Incident.RenameFile(fileId, displayName));

    public void Close()
    {
        if (IsReadOnly)
            throw new InvalidOperationException("Der Einsatz ist bereits abgeschlossen.");
        Incident.Close(_clock, RequireOperator());
        Save();
        Changed?.Invoke();
    }

    private void Mutate(Action apply)
    {
        apply();
        Save();
        Changed?.Invoke();
    }

    public void Save() => _store.Save(Path, Incident);

    /// <summary>
    /// Persists and announces a change applied to <see cref="Incident"/> from outside this session's
    /// own mutation methods — specifically a command the host received from a joined client (applied
    /// via <c>CommandApplier</c>). Raising <see cref="Changed"/> refreshes the host's own UI and,
    /// through the host's <see cref="Changed"/> subscription, rebroadcasts the new snapshot to every
    /// client — so a client's edit travels the exact same path as one the host typed itself (§5).
    /// </summary>
    public void SaveExternalChange()
    {
        Save();
        Changed?.Invoke();
    }

    private SessionOperator RequireOperator() =>
        Operator ?? throw new InvalidOperationException("Kein Bearbeiter für diese Änderung vorhanden.");
}
