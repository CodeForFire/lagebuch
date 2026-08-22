using System.Text.Json;

namespace LageBuch.AppLogic.Services;

public sealed class JsonRecentFilesStore : IRecentFilesStore
{
    private const int MaxEntries = 10;
    private readonly string _path;

    public JsonRecentFilesStore(string path) => _path = path;

    public IReadOnlyList<string> GetRecent()
    {
        if (!File.Exists(_path))
            return Array.Empty<string>();

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public void Add(string path)
    {
        var list = new List<string>(GetRecent());
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > MaxEntries)
            list.RemoveRange(MaxEntries, list.Count - MaxEntries);

        File.WriteAllText(_path, JsonSerializer.Serialize(list));
    }
}
