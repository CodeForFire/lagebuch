using Feuerwehr.Domain;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Sync;

/// <summary>
/// The mutation surface the ViewModels drive, with two implementations behind it: a local session
/// (apply to the in-memory <see cref="Incident"/>, save, notify) and a remote session (POST the
/// command to the host, do nothing locally — the UI updates only when the host's broadcast lands).
/// Methods are fire-and-forget: they never return the created/changed entity, because a remote
/// session cannot produce it synchronously. Callers react to <see cref="Changed"/> and read the
/// current <see cref="Incident"/> instead.
///
/// The session owns the clock and operator, so these signatures drop the <c>IClock</c>/
/// <see cref="SessionOperator"/> arguments the domain methods take — the local session supplies its
/// own; the remote session's host supplies its authoritative clock and the per-device operator that
/// rode along with the command.
/// </summary>
public interface IIncidentSession
{
    /// <summary>The current incident state — a live aggregate locally, or the latest host snapshot remotely.</summary>
    Incident Incident { get; }

    SessionOperator? Operator { get; }

    bool IsReadOnly { get; }

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
