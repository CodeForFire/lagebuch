namespace LageBuch.AppLogic.Services;

public sealed class JsonLastJoinHostStore : ILastJoinHostStore
{
    private readonly JsonFileStore<string> _file;

    public JsonLastJoinHostStore(string path) => _file = new JsonFileStore<string>(path);

    public string? GetLastHost() => _file.TryRead(out var host) ? host : null;

    public void SetLastHost(string host) => _file.Write(host);
}
