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

    public static string TrustJsonPath(Context context) =>
        System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "trust.json");
}
