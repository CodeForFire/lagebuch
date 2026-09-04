using LageBuch.AppLogic.Services;
using LageBuch.Documents;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;
using LageBuch.Sync;

namespace LageBuch.AppLogic;

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
        {
            throw new InvalidOperationException("Ein offener Einsatz erfordert einen Bearbeiter.");
        }

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
        {
            throw new InvalidOperationException("Ein abgeschlossener Einsatz kann nicht weiter bearbeitet werden.");
        }

        if (Operator is not null)
        {
            return; // already editable
        }

        Operator = op;
        Incident.ResumeEditing(_clock, op);
        Save();
        Changed?.Invoke();
    }

    // Export is host-only (IncidentWorkspaceViewModel.CanExport gates on _local being non-null), and
    // every attached file's bytes land in this device's own sibling folder the moment it's added —
    // whether typed here or uploaded by a joined client via AddFileCommand — so this never needs a
    // network pull, only IIncidentStore.
    public async Task<byte[]> ExportPdfAsync()
    {
        var fileBytes = new Dictionary<Guid, byte[]>();
        var pdfAttachmentPaths = new Dictionary<Guid, string>();
        foreach (var file in Incident.Files)
        {
            var storageFileName = IncidentFile.StorageFileName(file.Id, file.FileName);
            if (file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                // Merged straight from disk (see issue #167 P1 #3) — never loaded into fileBytes,
                // which would otherwise round-trip every PDF attachment through memory twice.
                var diskPath = _store.ResolveFileDiskPath(Path, storageFileName);
                if (File.Exists(diskPath))
                {
                    pdfAttachmentPaths[file.Id] = diskPath;
                }

                continue;
            }

            var bytes = await _store.TryReadFileBytesAsync(Path, storageFileName);
            if (bytes is not null)
            {
                fileBytes[file.Id] = bytes;
            }
        }

        return IncidentPdf.Generate(Incident, fileBytes, pdfAttachmentPaths);
    }

    // --- IIncidentSession mutation surface: build the same SyncCommand RemoteIncidentSession would
    // send for this operation, then apply it locally via CommandApplier — the same dispatcher the
    // sync host uses to apply a joined client's command (issue #167 P2: this used to hand-roll each
    // domain call here, duplicating CommandApplier's own switch, so a fix to how a mutation applies
    // had to land in two places). The command is built inside Apply's lambda, not before calling it,
    // so Mutate's IsReadOnly guard still runs before Op() ever touches Operator. ---
    public void AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null) =>
        Apply(() => new AddJournalEntryCommand(Op(), direction, text, from, to));

    public void EditJournalEntry(Guid entryId, string text) =>
        Apply(() => new EditJournalEntryCommand(Op(), entryId, text));

    public void ToggleChecklistItem(Guid itemId) => Apply(() => new ToggleChecklistItemCommand(Op(), itemId));

    public void AssignRole(
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? section = null,
        string? phone = null) =>
        Apply(() => new AssignRoleCommand(Op(), role, personName, callSign, from, to, section, phone));

    public void TransferRole(Guid assignmentId, string newPersonName, string? newCallSign = null, string? newPhone = null) =>
        Apply(() => new TransferRoleCommand(Op(), assignmentId, newPersonName, newCallSign, newPhone));

    public void EditRolePhone(Guid assignmentId, string? phone) =>
        Apply(() => new EditRolePhoneCommand(Op(), assignmentId, phone));

    public void AddForceUnit(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0,
        int officerCount = 0) =>
        Apply(() => new AddForceUnitCommand(Op(), brigade, personnelCount, callSign, status, notes, scbaCount, officerCount));

    public void UpdateForceUnit(Guid unitId, string? status, string? notes) =>
        Apply(() => new UpdateForceUnitCommand(Op(), unitId, status, notes));

    public void UpdateForceStrength(Guid unitId, int officerCount, int personnelCount, int scbaCount) =>
        Apply(() => new UpdateForceStrengthCommand(Op(), unitId, officerCount, personnelCount, scbaCount));

    public void RemoveForceUnit(Guid unitId) =>
        Apply(() => new RemoveForceUnitCommand(Op(), unitId));

    public void AddTask(string text, string? assignee, TaskImportance importance, TaskUrgency urgency, int timerMinutes) =>
        Apply(() => new AddTaskCommand(Op(), text, assignee ?? string.Empty, importance, urgency, timerMinutes));

    public void SetTaskCompleted(Guid taskId, bool isDone) =>
        Apply(() => new SetTaskCompletedCommand(Op(), taskId, isDone));

    public void AddScbaTrupp(
        string designation,
        IEnumerable<TruppMember> members,
        int entryPressure,
        int? truppNumber = null,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes) =>
        Apply(() => new AddScbaTruppCommand(
            designation,
            members.Select(m => new TruppMemberDto(m.Role, m.Name)).ToList(),
            callSign,
            task,
            maxDurationMinutes,
            returnPressureBar,
            pressureControlIntervalMinutes,
            entryPressure,
            truppNumber));

    public void StartScbaTrupp(Guid truppId) =>
        Apply(() => new StartScbaTruppCommand(truppId));

    public void RecordScbaPressure(Guid truppId, int bar) =>
        Apply(() => new RecordScbaPressureCommand(truppId, bar));

    public void WithdrawScbaTrupp(Guid truppId) =>
        Apply(() => new WithdrawScbaTruppCommand(truppId));

    public void MarkScbaRemoved(Guid truppId) =>
        Apply(() => new MarkScbaRemovedCommand(truppId));

    public void SetIncidentNumber(IncidentNumber? number) => Apply(() => new SetIncidentNumberCommand(number?.Value));

    public void SetKeyword(string? keyword) => Apply(() => new SetKeywordCommand(keyword));

    public void SetAddress(string? street, string? district) => Apply(() => new SetAddressCommand(street, district));

    public void SetStatus(string? status) => Apply(() => new SetStatusCommand(status));

    // Not routed through Apply/CommandApplier: incident-level timers are host-authoritative and have
    // no SyncCommand at all (RemoteIncidentSession.UpsertTimer is an intentional no-op) — this really
    // is Local-only state, not a duplicated code path.
    public void UpsertTimer(string key, DateTimeOffset cycleAnchor, int intervalMinutes, int recurringIntervalMinutes, bool isRunning) =>
        Mutate(() => Incident.UpsertTimer(key, cycleAnchor, intervalMinutes, recurringIntervalMinutes, isRunning));

    public async Task AddFileAsync(string fileName, string contentType, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var file = Incident.AddFile(_clock, RequireOperator(), fileName, contentType, bytes.LongLength);
        await _store.SaveFileBytesAsync(Path, IncidentFile.StorageFileName(file.Id, file.FileName), bytes, cancellationToken);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Streams attachment bytes for a file already recorded on <see cref="Incident"/> elsewhere — used
    /// by the host after applying a joined client's <c>AddFileCommand</c> via
    /// <see cref="LageBuch.Sync.CommandApplier"/>, where the metadata mutation and the byte transfer
    /// are two separate requests (issue #167 P1 #2: the client PUTs the raw bytes directly rather than
    /// riding them on the command), so this is called from the upload handler, not
    /// <see cref="AddFileAsync"/>.
    /// </summary>
    public Task SaveFileStreamAsync(string storageFileName, Stream source, CancellationToken cancellationToken = default) =>
        _store.SaveFileStreamAsync(Path, storageFileName, source, cancellationToken);

    public async Task<byte[]?> GetFileBytesAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = Incident.Files.FirstOrDefault(f => f.Id == fileId);
        return file is null ? null : await _store.TryReadFileBytesAsync(Path, IncidentFile.StorageFileName(file.Id, file.FileName), cancellationToken);
    }

    public void RenameFile(Guid fileId, string? displayName) => Apply(() => new RenameFileCommand(fileId, displayName));

    public void AddCoBuilding(string name, int floorCount, int apartmentsPerFloor) =>
        Apply(() => new AddCoBuildingCommand(Op(), name, floorCount, apartmentsPerFloor));

    public void UpdateCoBuildingStructure(Guid buildingId, int floorCount, int apartmentsPerFloor) =>
        Apply(() => new UpdateCoBuildingStructureCommand(Op(), buildingId, floorCount, apartmentsPerFloor));

    public void RemoveCoBuilding(Guid buildingId) =>
        Apply(() => new RemoveCoBuildingCommand(Op(), buildingId));

    public void RecordCoValue(Guid buildingId, int floorOrdinal, int apartmentNumber, int? coValue) =>
        Apply(() => new RecordCoValueCommand(Op(), buildingId, floorOrdinal, apartmentNumber, coValue));

    public void SetDwellingStatus(Guid buildingId, int floorOrdinal, int apartmentNumber, DwellingStatus status) =>
        Apply(() => new SetDwellingStatusCommand(Op(), buildingId, floorOrdinal, apartmentNumber, status));

    public void SetDwellingDetails(Guid buildingId, int floorOrdinal, int apartmentNumber, string? residentName, bool? keyAvailable) =>
        Apply(() => new UpdateDwellingDetailsCommand(buildingId, floorOrdinal, apartmentNumber, residentName, keyAvailable));

    public void SetFloorDescription(Guid buildingId, int floorOrdinal, string? description) =>
        Apply(() => new SetFloorDescriptionCommand(buildingId, floorOrdinal, description));

    public void SetApartmentLabel(Guid buildingId, int apartmentNumber, string? label) =>
        Apply(() => new SetApartmentLabelCommand(buildingId, apartmentNumber, label));

    public void Close() => Apply(() => new CloseIncidentCommand(Op()));

    private void Apply(Func<SyncCommand> command) => Mutate(() => CommandApplier.Apply(command(), Incident, _clock));

    private void Mutate(Action apply)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("Der Einsatz ist bereits abgeschlossen.");
        }

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

    private OperatorDto Op()
    {
        var op = RequireOperator();
        return new OperatorDto(op.Name, op.CallSign);
    }
}
