using Feuerwehr.Domain;
using Feuerwehr.Persistence;

namespace Feuerwehr.AppLogic.Services;

public sealed class IncidentStore : IIncidentStore
{
    private readonly IncidentRepository _repository = new();
    private readonly IIncidentFileStore _fileStore = new IncidentFileStore();

    public void Save(string path, Incident incident) => _repository.Save(path, incident);

    public Incident Load(string path) => _repository.Load(path);

    public IncidentState? TryReadState(string path) => _repository.TryReadState(path);

    public void SaveFileBytes(string path, string storageFileName, byte[] bytes) =>
        _fileStore.SaveBytes(path, storageFileName, bytes);

    public byte[]? TryReadFileBytes(string path, string storageFileName) =>
        _fileStore.TryReadBytes(path, storageFileName);
}
