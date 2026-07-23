using Feuerwehr.Domain;

namespace Feuerwehr.AppLogic.Services;

public interface IIncidentStore
{
    void Save(string path, Incident incident);
    Incident Load(string path);

    /// <summary>
    /// Cheaply peeks at an incident file's lifecycle state without loading or migrating it, for the
    /// Home overview's closed marker. Returns null when the file is missing, unreadable, or otherwise
    /// cannot be inspected — the overview then shows no marker rather than failing.
    /// </summary>
    IncidentState? TryReadState(string path);
}
