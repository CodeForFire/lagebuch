using LageBuch.Domain;

namespace LageBuch.AppLogic.Services;

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

    /// <summary>
    /// Writes an attached file's bytes to storage alongside the incident at <paramref name="path"/>,
    /// keyed by <paramref name="storageFileName"/> (see <c>IncidentFile.StorageFileName</c>). Kept
    /// out of <see cref="Save"/>'s SQLite tables — see <c>LageBuch.Persistence.IncidentFileStore</c>
    /// for why.
    /// </summary>
    void SaveFileBytes(string path, string storageFileName, byte[] bytes);

    /// <summary>Null when the bytes are unavailable (never written, or storage unreachable) —
    /// never throws, so a caller degrades quietly.</summary>
    byte[]? TryReadFileBytes(string path, string storageFileName);
}
