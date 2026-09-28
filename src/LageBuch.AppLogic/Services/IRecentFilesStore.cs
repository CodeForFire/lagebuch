namespace LageBuch.AppLogic.Services;

public interface IRecentFilesStore
{
    IReadOnlyList<string> GetRecent();

    void Add(string path);

    /// <summary>
    /// Takes a path off the list. Only the entry goes: the incident file itself is never touched,
    /// so it can be opened, and listed, again. An unlisted path is not an error.
    /// </summary>
    void Remove(string path);
}
