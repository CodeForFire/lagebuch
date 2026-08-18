namespace Feuerwehr.Domain.Time;

/// <summary>
/// Persisted state of a single incident-level timer, identified by <see cref="Key"/>. Only the
/// anchor and cadence are stored; live due/remaining values are recomputed from a supplied <c>now</c>
/// (the same design as the SCBA countdowns anchored on <c>AtemschutzTrupp.StartTime</c>), so a timer
/// survives a close+reopen or a crash. Generic by key — the "Rückmeldung an ILS" reminder is the
/// only timer today, but a future incident-level timer plugs in with a new key and no schema change.
/// </summary>
public sealed record IncidentTimerState(
    string Key,
    DateTimeOffset CycleAnchor,
    int IntervalMinutes,
    int RecurringIntervalMinutes,
    bool IsRunning);
