namespace LageBuch.AppLogic.Services;

/// <summary>
/// Puts <see cref="AlarmMute"/> in front of the platform's alarm service (#699), so one switch
/// silences every cue — ILS reminder, Aufgabe, Druckabfrage and the Rückzugsalarm — on desktop and
/// Android alike, without any view model having to know about it. What is due still shows on
/// screen; only the sound goes.
/// </summary>
public sealed class MutedAlarmService : IAlarmService
{
    private readonly IAlarmService _inner;
    private readonly AlarmMute _mute;

    public MutedAlarmService(IAlarmService inner, AlarmMute mute)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(mute);
        _inner = inner;
        _mute = mute;
    }

    public void Play(AlarmSound sound)
    {
        if (!_mute.IsMuted)
        {
            _inner.Play(sound);
        }
    }

    /// <summary>
    /// The sound check in ÜBER is a deliberate tap, so it plays even while muted: it is how a crew
    /// checks the speaker before switching the alarms back on.
    /// </summary>
    public Task<bool> PlayTestAsync(CancellationToken cancellationToken = default) =>
        _inner.PlayTestAsync(cancellationToken);
}
