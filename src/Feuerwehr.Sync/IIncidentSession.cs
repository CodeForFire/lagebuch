using Feuerwehr.Domain;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Sync;

/// <summary>
/// The mutation surface the ViewModels drive, with two implementations behind it: a local session
/// (apply to the in-memory <see cref="Incident"/>, save via the store, raise <see cref="Changed"/>)
/// and — from a later phase — a remote session that POSTs the command to the host and lets the UI
/// update when the host's broadcast lands.
///
/// The session owns the clock and operator, so these signatures drop the <c>IClock</c>/
/// <see cref="SessionOperator"/> arguments the domain methods take. Methods return the created/
/// changed entity as the domain methods do, which the local caller uses to build a row; the remote
/// implementation (and the ViewModels' switch to rebuilding rows from <see cref="Changed"/>) is
/// handled where the remote session lands.
/// </summary>
public interface IIncidentSession
{
    /// <summary>The current incident state — a live aggregate locally, or the latest host snapshot remotely.</summary>
    Incident Incident { get; }

    SessionOperator? Operator { get; }

    bool IsReadOnly { get; }

    /// <summary>Raised after the incident state changes (a local mutation, or a host broadcast).</summary>
    event Action? Changed;

    EtbEntry AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null);
    ChecklistItem ToggleChecklistItem(Guid itemId);
    RoleAssignment AssignRole(string role, string personName, string? callSign = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? section = null, string? phone = null);
    RoleAssignment EndRoleAssignment(Guid assignmentId);
    ForceUnit AddForceUnit(string brigade, int personnelCount, string? callSign = null,
        string? status = null, string? notes = null, int scbaCount = 0);
    ForceUnit UpdateForceUnit(Guid unitId, string? status, string? notes);
    AtemschutzTrupp AddScbaTrupp(string designation, IEnumerable<TruppMember> members, string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes);
    AtemschutzTrupp StartScbaTrupp(Guid truppId, int startPressure);
    AtemschutzTrupp RecordScbaPressure(Guid truppId, int bar);
    AtemschutzTrupp MarkScbaReturned(Guid truppId);
    void SetIncidentNumber(IncidentNumber? number);
    void SetKeyword(string? keyword);
    void SetAddress(string? street, string? district);
    void SetStatus(string? status);
    void Close();
}
