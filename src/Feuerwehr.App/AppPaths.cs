namespace Feuerwehr.App;

public static class AppPaths
{
    public static string AppDataDir =>
        GetAppDataDir(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string MasterDataDbPath => Path.Combine(AppDataDir, "masterdata.db");

    public static string RecentFilesJsonPath => Path.Combine(AppDataDir, "recent.json");

    public static string LastSaveFolderJsonPath => Path.Combine(AppDataDir, "last-save-folder.json");

    public static string GetAppDataDir(string baseDir)
    {
        var dir = Path.Combine(baseDir, "Lagebuch");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
