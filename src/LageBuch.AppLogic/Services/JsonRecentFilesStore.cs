using System.Text.Json;

namespace LageBuch.AppLogic.Services;

public sealed class JsonRecentFilesStore : IRecentFilesStore
{
    private const int MaxEntries = 10;
    private readonly string _path;
    private List<string>? _cached;

    public JsonRecentFilesStore(string path) => _path = path;

    public IReadOnlyList<string> GetRecent() => _cached ??= LoadFromDisk();

    public void Add(string path)
    {
        var list = new List<string>(GetRecent());
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > MaxEntries)
        {
            list.RemoveRange(MaxEntries, list.Count - MaxEntries);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(list));
        _cached = list;
    }

    private List<string> LoadFromDisk()
    {
        if (!File.Exists(_path))
        {
            return new List<string>();
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
