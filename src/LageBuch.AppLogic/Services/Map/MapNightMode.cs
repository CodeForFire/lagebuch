namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// The Lagekarte's NACHTKARTE switch: dark map colours for a dark ELW, where the day map is the
/// brightest thing on the screen. It belongs to this device, not to an Einsatz, the way
/// <see cref="AlarmMute"/> does: a small JSON file beside <c>recent.json</c>, never in the incident
/// file and never on the wire, so the tablet outside in daylight keeps its day map.
/// <para>
/// A missing or corrupt file reads as the day map.
/// </para>
/// </summary>
public sealed class MapNightMode
{
    private readonly JsonFileStore<bool>? _file;

    /// <param name="path">The file to keep the switch in, or <see langword="null"/> to keep it in
    /// memory only, for a host without app storage.</param>
    public MapNightMode(string? path = null)
    {
        if (path is not null)
        {
            _file = new JsonFileStore<bool>(path);
            IsNight = _file.TryRead(out var night) && night;
        }
    }

    /// <summary>Whether the map is drawn in its night colours.</summary>
    public bool IsNight { get; private set; }

    /// <summary>Raised after <see cref="IsNight"/> changed, so every open map follows.</summary>
    public event EventHandler? Changed;

    /// <summary>Sets the switch and keeps it for the next start.</summary>
    /// <exception cref="IOException">The file could not be written; the switch keeps its value.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be written; the switch keeps its value.</exception>
    public void Set(bool night)
    {
        if (night == IsNight)
        {
            return;
        }

        _file?.Write(night);
        IsNight = night;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
