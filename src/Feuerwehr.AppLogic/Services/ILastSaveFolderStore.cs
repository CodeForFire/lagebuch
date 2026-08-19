namespace Feuerwehr.AppLogic.Services;

/// <summary>
/// Remembers the folder the last incident was saved to, so the next "new incident" save dialog
/// opens there instead of wherever the OS last happened to leave it. Desktop-only in practice —
/// Android has no save picker to hint a start location for.
/// </summary>
public interface ILastSaveFolderStore
{
    string? GetLastFolder();
    void SetLastFolder(string folder);
}
