namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>
/// <see cref="IDispatchInboxStore"/> in one small JSON file. An answered alarm is kept for
/// <see cref="Retention"/>, far longer than the scanner ever looks back, and dropped after that so
/// the file does not grow with every Einsatz.
/// </summary>
public sealed class JsonDispatchInboxStore : IDispatchInboxStore
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly JsonFileStore<DispatchInboxState> _file;

    public JsonDispatchInboxStore(string path) => _file = new JsonFileStore<DispatchInboxState>(path);

    public string? GetFolder() => Read().Folder;

    public void SetFolder(string? folder) =>
        _file.Write(Read() with { Folder = string.IsNullOrWhiteSpace(folder) ? null : folder });

    public IReadOnlySet<string> GetHandled() => Read().Handled.Keys.ToHashSet(StringComparer.Ordinal);

    public void MarkHandled(string key, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(key);
        var state = Read();
        var handled = state.Handled
            .Where(entry => at - entry.Value < Retention)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        handled[key] = at;
        _file.Write(state with { Handled = handled });
    }

    private DispatchInboxState Read() =>
        _file.TryRead(out var state) && state.Handled is not null ? state : new DispatchInboxState(null, []);

    /// <summary>The file's contents.</summary>
    /// <param name="Folder">The alarm folder, or null.</param>
    /// <param name="Handled">Each answered alarm's key, with when it was answered.</param>
    private sealed record DispatchInboxState(string? Folder, Dictionary<string, DateTimeOffset> Handled);
}
