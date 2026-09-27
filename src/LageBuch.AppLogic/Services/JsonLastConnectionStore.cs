namespace LageBuch.AppLogic.Services;

public sealed class JsonLastConnectionStore : ILastConnectionStore
{
    private readonly JsonFileStore<LastConnection> _file;

    public JsonLastConnectionStore(string path) => _file = new JsonFileStore<LastConnection>(path);

    // A record struct deserializes "{}" without complaint, leaving Host null despite its type; that
    // is as unusable as a corrupt file, so it reads the same way: nothing stored.
    public LastConnection? GetLast() =>
        _file.TryRead(out var connection) && !string.IsNullOrWhiteSpace(connection.Host) ? connection : null;

    public void SetLast(LastConnection connection) => _file.Write(connection);

    public void Clear() => _file.Delete();
}
