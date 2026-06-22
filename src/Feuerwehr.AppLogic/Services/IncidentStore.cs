using Feuerwehr.Domain;
using Feuerwehr.Persistence;

namespace Feuerwehr.AppLogic.Services;

public sealed class IncidentStore : IIncidentStore
{
    private readonly IncidentRepository _repository = new();

    public void Save(string path, Incident incident) => _repository.Save(path, incident);

    public Incident Load(string path) => _repository.Load(path);
}
