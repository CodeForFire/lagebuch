using System.Text.Json;

namespace LageBuch.AppLogic.Services;

public sealed class JsonLastPdfExportStore : ILastPdfExportStore
{
    private readonly string _path;

    public JsonLastPdfExportStore(string path) => _path = path;

    public LastPdfExport? GetLastExport()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LastPdfExport>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void SetLastExport(string path, DateTimeOffset exportedAt) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(new LastPdfExport(path, exportedAt)));
}
