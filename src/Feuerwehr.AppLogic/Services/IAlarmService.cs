namespace Feuerwehr.AppLogic.Services;

/// <summary>
/// Sounds an audible alarm for life-safety events (currently the Atemschutz Rückzugsalarm).
/// <see cref="Start"/> begins a looping tone that continues until <see cref="Stop"/>; both
/// are idempotent so callers can drive them straight from state on every tick.
/// </summary>
public interface IAlarmService
{
    /// <summary>Begins (or continues) the looping alarm. Safe to call when already sounding.</summary>
    void Start();

    /// <summary>Silences the alarm. Safe to call when already silent.</summary>
    void Stop();
}
