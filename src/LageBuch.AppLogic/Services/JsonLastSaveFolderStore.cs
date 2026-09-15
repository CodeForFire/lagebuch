namespace LageBuch.AppLogic.Services;

public sealed class JsonLastSaveFolderStore : ILastSaveFolderStore
{
    private readonly JsonFileStore<string> _file;

    public JsonLastSaveFolderStore(string path) => _file = new JsonFileStore<string>(path);

    public string? GetLastFolder() => _file.TryRead(out var folder) ? folder : null;

    public void SetLastFolder(string folder) => _file.Write(folder);
}
