namespace LageBuch.AppLogic.Services;

public sealed class JsonRecentFilesStore : IRecentFilesStore
{
    private const int MaxEntries = 10;
    private readonly JsonFileStore<List<string>> _file;

    public JsonRecentFilesStore(string path) => _file = new JsonFileStore<List<string>>(path);

    public IReadOnlyList<string> GetRecent() =>
        _file.TryRead(out var recent) ? recent : Array.Empty<string>();

    public void Add(string path)
    {
        var list = new List<string>(GetRecent());
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > MaxEntries)
        {
            list.RemoveRange(MaxEntries, list.Count - MaxEntries);
        }

        _file.Write(list);
    }
}
