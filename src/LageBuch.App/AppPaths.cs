namespace LageBuch.App;

internal static class AppPaths
{
    public static string Root =>
        GetAppDataDir(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string MasterDataDbPath => Path.Join(Root, "masterdata.db");

    public static string RecentFilesJsonPath => Path.Join(Root, "recent.json");

    public static string LastSaveFolderJsonPath => Path.Join(Root, "last-save-folder.json");

    public static string LastPdfExportJsonPath => Path.Join(Root, "last-pdf-export.json");

    public static string LastJoinHostJsonPath => Path.Join(Root, "last-join-host.json");

    public static string AttachmentCacheDir => Path.Join(Root, "attachment-cache");

    public static string TrustJsonPath => Path.Join(Root, "trust.json");

    public static string GetAppDataDir(string baseDir)
    {
        var dir = Path.Join(baseDir, "Lagebuch");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
