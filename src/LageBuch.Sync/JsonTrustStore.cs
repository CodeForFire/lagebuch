using System.Text.Json;

namespace LageBuch.Sync;

/// <summary>
/// <see cref="ITrustStore"/> backed by a flat JSON dictionary file (address -> SHA-256 thumbprint).
/// A missing or corrupt file is treated as "trust nothing"; every write goes to a temp file that is
/// then renamed onto <c>_path</c> (<see cref="Persist"/>), so a crash mid-write can never leave a
/// half-written file behind. Thread-safe: every read and write takes <see cref="_gate"/> for the
/// whole operation, since <see cref="GetThumbprint"/> is called from TLS validation callbacks that
/// can run concurrently across multiple handshake threads while a save/remove is in flight.
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

    public string? GetThumbprint(string hostAddress)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(hostAddress, out var t) ? t : null;
        }
    }

    public void SaveThumbprint(string hostAddress, string thumbprint)
    {
        lock (_gate)
        {
            _cache[hostAddress] = thumbprint;
            Persist();
        }
    }

    public void RemoveThumbprint(string hostAddress)
    {
        lock (_gate)
        {
            if (_cache.Remove(hostAddress))
            {
                Persist();
            }
        }
    }

    // Write-then-rename: a crash between the two leaves either the old file untouched or the new one
    // fully in place -- never a half-written trust.json. Always called with _gate already held (both
    // mutators take it for the whole read-modify-write), so the temp file's fixed name is safe from a
    // sibling in-process write; overwrite: true also clobbers a stale *.tmp left by a process that
    // crashed between the write and the move on its own previous attempt.
    private void Persist()
    {
        var tmpPath = _path + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(_cache));
        File.Move(tmpPath, _path, overwrite: true);
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

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
