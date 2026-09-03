namespace LageBuch.App;

internal static class AppPaths
{
    public static string Root =>
        GetAppDataDir(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string MasterDataDbPath => Path.Combine(Root, "masterdata.db");

    public static string RecentFilesJsonPath => Path.Combine(Root, "recent.json");

    public static string LastSaveFolderJsonPath => Path.Combine(Root, "last-save-folder.json");

    public static string AttachmentCacheDir => Path.Combine(Root, "attachment-cache");

    public static string TrustJsonPath => Path.Combine(Root, "trust.json");

    public static string GetAppDataDir(string baseDir)
    {
        var dir = Path.Combine(baseDir, "Lagebuch");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
