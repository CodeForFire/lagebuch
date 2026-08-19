using System.Text.Json;

namespace Feuerwehr.AppLogic.Services;

public sealed class JsonLastSaveFolderStore : ILastSaveFolderStore
{
    private readonly string _path;

    public JsonLastSaveFolderStore(string path) => _path = path;

    public string? GetLastFolder()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<string>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void SetLastFolder(string folder) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(folder));
}
