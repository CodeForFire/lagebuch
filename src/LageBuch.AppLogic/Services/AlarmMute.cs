namespace LageBuch.AppLogic.Services;

/// <summary>
/// The "Alarmtöne stumm schalten" switch in ÜBER (#699). It belongs to this device, not to an
/// Einsatz: it lives in a small JSON file beside <c>recent.json</c>, never in the incident file and
/// never on the wire, so muting a tablet does not silence the ELW. <see cref="MutedAlarmService"/>
/// is what makes it bite.
/// <para>
/// A missing or corrupt file reads as "not muted": when in doubt, alarms sound.
/// </para>
/// </summary>
public sealed class AlarmMute
{
    private readonly JsonFileStore<bool>? _file;

    /// <param name="path">The file to keep the switch in, or <see langword="null"/> to keep it in
    /// memory only, for a host without app storage.</param>
    public AlarmMute(string? path = null)
    {
        if (path is not null)
        {
            _file = new JsonFileStore<bool>(path);
            IsMuted = _file.TryRead(out var muted) && muted;
        }
    }

    /// <summary>Whether every alarm cue on this device is silenced, the Rückzugsalarm included.</summary>
    public bool IsMuted { get; private set; }

    /// <summary>Raised after <see cref="IsMuted"/> changed, so an open Einsatz can show it.</summary>
    public event EventHandler? Changed;

    /// <summary>Sets the switch and keeps it for the next start.</summary>
    /// <exception cref="IOException">The file could not be written; the switch keeps its value.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be written; the switch keeps its value.</exception>
    public void Set(bool muted)
    {
        if (muted == IsMuted)
        {
            return;
        }

        _file?.Write(muted);
        IsMuted = muted;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
