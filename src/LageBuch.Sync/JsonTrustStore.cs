using System.Text.Json;

namespace LageBuch.Sync;

/// <summary>
/// <see cref="ITrustStore"/> backed by a flat JSON dictionary file (address -> SHA-256 thumbprint).
/// A missing or corrupt file is treated as "trust nothing"; writes rewrite the file atomically
/// enough for a single process holding this instance.
/// </summary>
public sealed class JsonTrustStore : ITrustStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, string> _cache;

    public JsonTrustStore(string path)
    {
        _path = path;
        _cache = Load();
    }

    public string? GetThumbprint(string hostAddress) =>
        _cache.TryGetValue(hostAddress, out var t) ? t : null;

    public void SaveThumbprint(string hostAddress, string thumbprint)
    {
        lock (_gate)
        {
            _cache[hostAddress] = thumbprint;
            File.WriteAllText(_path, JsonSerializer.Serialize(_cache));
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path))
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
