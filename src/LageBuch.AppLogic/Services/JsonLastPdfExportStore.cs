namespace LageBuch.AppLogic.Services;

public sealed class JsonLastPdfExportStore : ILastPdfExportStore
{
    private readonly JsonFileStore<LastPdfExport> _file;

    public JsonLastPdfExportStore(string path) => _file = new JsonFileStore<LastPdfExport>(path);

    public LastPdfExport? GetLastExport() => _file.TryRead(out var export) ? export : null;

    public void SetLastExport(string path, DateTimeOffset exportedAt) =>
        _file.Write(new LastPdfExport(path, exportedAt));
}
