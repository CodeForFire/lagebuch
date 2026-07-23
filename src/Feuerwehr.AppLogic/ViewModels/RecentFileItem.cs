namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>
/// One row in the Home screen's "zuletzt verwendet" list: the file's path, its display name, and
/// whether the incident inside is finally closed (shown with a lock marker). The closed flag is a
/// snapshot taken when the row is built — reopening the file rebuilds the row.
/// </summary>
public sealed record RecentFileItem(string Path, bool IsClosed)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}
