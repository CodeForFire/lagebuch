namespace Feuerwehr.AppLogic.Services;

public interface IRecentFilesStore
{
    IReadOnlyList<string> GetRecent();
    void Add(string path);
}
