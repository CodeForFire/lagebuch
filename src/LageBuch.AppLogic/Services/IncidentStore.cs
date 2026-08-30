using LageBuch.Domain;
using LageBuch.Persistence;

namespace LageBuch.AppLogic.Services;

public sealed class IncidentStore : IIncidentStore
{
private readonly IncidentFileStore _fileStore = new IncidentFileStore();

    public void Save(string path, Incident incident) => IncidentRepository.Save(path, incident);

    public Incident Load(string path) => IncidentRepository.Load(path);

    public IncidentState? TryReadState(string path) => IncidentRepository.TryReadState(path);

    public void SaveFileBytes(string path, string storageFileName, byte[] bytes) =>
        _fileStore.SaveBytes(path, storageFileName, bytes);

    public byte[]? TryReadFileBytes(string path, string storageFileName) =>
        _fileStore.TryReadBytes(path, storageFileName);
}
