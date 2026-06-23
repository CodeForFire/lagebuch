using Feuerwehr.Domain;

namespace Feuerwehr.AppLogic.Services;

public interface IIncidentStore
{
    void Save(string path, Incident incident);
    Incident Load(string path);
}
