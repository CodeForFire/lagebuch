using System.Text.Json;

namespace LageBuch.AppLogic.Services;

public sealed class JsonLastSaveFolderStore : ILastSaveFolderStore
{
    private readonly string _path;
    private string? _cached;
    private bool _loaded;

    public JsonLastSaveFolderStore(string path) => _path = path;

    public string? GetLastFolder()
    {
        if (!_loaded)
        {
            _cached = LoadFromDisk();
            _loaded = true;
        }

        return _cached;
    }

    public void SetLastFolder(string folder)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(folder));
        _cached = folder;
        _loaded = true;
    }

    private string? LoadFromDisk()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
