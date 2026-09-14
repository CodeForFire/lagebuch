using System.Text.Json;

namespace LageBuch.AppLogic.Services;

public sealed class JsonLastJoinHostStore : ILastJoinHostStore
{
    private readonly string _path;

    public JsonLastJoinHostStore(string path) => _path = path;

    public string? GetLastHost()
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

    public void SetLastHost(string host) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(host));
}
