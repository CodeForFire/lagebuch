using Feuerwehr.Domain;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Sync;

/// <summary>
/// The mutation surface the ViewModels drive, with two implementations behind it: a local session
/// (apply to the in-memory <see cref="Incident"/>, save, raise <see cref="Changed"/>) and a remote
/// session (POST the command to the host, write nothing locally — the UI updates only when the
/// host's broadcast lands). Mutations are <b>fire-and-forget</b>: the ViewModels never use a return
/// value; they react to <see cref="Changed"/> and re-read <see cref="Incident"/>, so the exact same
/// code path serves an edit made on this device or on another one.
///
/// The session owns the clock and operator, so these signatures drop the <c>IClock</c>/
/// <see cref="SessionOperator"/> arguments the domain methods take.
/// </summary>
public interface IIncidentSession
{
    /// <summary>The current incident state — a live aggregate locally, or the latest host snapshot remotely.</summary>
    Incident Incident { get; }

    SessionOperator? Operator { get; }

    bool IsReadOnly { get; }

    /// <summary>
    /// True on a joined client. Autonomous, time-driven logging (SCBA Rückzugsalarm, ILS reminder
    /// acknowledgements) must run only on the authoritative device, so the host isn't double-logged
    /// — ViewModels gate those writes on <c>!IsRemote</c>.
    /// </summary>
    bool IsRemote { get; }

    /// <summary>Raised after the incident state changes (a local mutation, or a host broadcast).</summary>
    event Action? Changed;

    void AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null);
    void ToggleChecklistItem(Guid itemId);
    void AssignRole(string role, string personName, string? callSign = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? section = null, string? phone = null);
    void EndRoleAssignment(Guid assignmentId);
    void AddForceUnit(string brigade, int personnelCount, string? callSign = null,
        string? status = null, string? notes = null, int scbaCount = 0);
    void UpdateForceUnit(Guid unitId, string? status, string? notes);
    void AddScbaTrupp(string designation, IEnumerable<TruppMember> members, string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes);
    void StartScbaTrupp(Guid truppId, int startPressure);
    void RecordScbaPressure(Guid truppId, int bar);
    void MarkScbaReturned(Guid truppId);
    void SetIncidentNumber(IncidentNumber? number);
    void SetKeyword(string? keyword);
    void SetAddress(string? street, string? district);
    void SetStatus(string? status);
    void Close();
}
