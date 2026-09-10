using Android.Content;

namespace LageBuch.App.Android.Services;

/// <summary>
/// App-private storage paths for Android, mirroring desktop's <c>AppPaths</c> but rooted at
/// <see cref="Context.FilesDir"/> instead of a per-OS AppData folder. Per the design spec's
/// "app-managed list" decision, incidents live in one app-private directory rather than
/// anywhere the user picks — there is no Android equivalent of a free-form save dialog here.
/// </summary>
internal static class AndroidAppPaths
{
    public static string IncidentsDir(Context context)
    {
        var dir = System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "incidents");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    public static string MasterDataDbPath(Context context) =>
        System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "masterdata.db");

    public static string RecentFilesJsonPath(Context context) =>
        System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "recent.json");

    public static string CacheDir(Context context) => context.CacheDir!.AbsolutePath;

    public static string AttachmentCacheDir(Context context) =>
        System.IO.Path.Combine(CacheDir(context), "attachment-cache");

    /// <summary>
    /// The only subtree the <c>FileProvider</c> grants a URI into (see <c>file_paths.xml</c>'s
    /// "shared" entry). Every file handed to <see cref="AndroidFileDialogService.ShareFileAsync"/>
    /// or <see cref="AndroidFileDialogService.OpenFileAsync"/> — PDF/JSON export temp files
    /// included — must live here, never directly under <see cref="CacheDir"/>, so a granted URI
    /// can never address the whole cache dir (which also holds picked attachments and
    /// <c>import.json</c>).
    /// </summary>
    public static string SharedDir(Context context)
    {
        var dir = System.IO.Path.Combine(CacheDir(context), "shared");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Where picked attachments and imported master-data JSON land before this process reads
    /// them off disk. Deliberately not exposed through the <c>FileProvider</c> (it never appears
    /// in <c>file_paths.xml</c>) — nothing outside this app needs a URI into it.
    /// </summary>
    public static string PickedDir(Context context)
    {
        var dir = System.IO.Path.Combine(CacheDir(context), "picked");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    public static string TrustJsonPath(Context context) =>
        System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "trust.json");
}
