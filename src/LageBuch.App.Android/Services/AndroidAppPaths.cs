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
        var dir = System.IO.Path.Join(context.FilesDir!.AbsolutePath, "incidents");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    public static string MasterDataDbPath(Context context) =>
        System.IO.Path.Join(context.FilesDir!.AbsolutePath, "masterdata.db");

    public static string RecentFilesJsonPath(Context context) =>
        System.IO.Path.Join(context.FilesDir!.AbsolutePath, "recent.json");

    public static string CacheDir(Context context) => context.CacheDir!.AbsolutePath;

    public static string AttachmentCacheDir(Context context) =>
        System.IO.Path.Join(CacheDir(context), "attachment-cache");

    /// <summary>
    /// One of the two subtrees the <c>FileProvider</c> grants a URI into (see
    /// <c>file_paths.xml</c>'s "shared" entry; the other is the attachments root,
    /// <c>AttachmentTempPaths.Root</c>). PDF/JSON export write straight here. <see cref="AndroidFileDialogService"/>'s
    /// private <c>EnsureShareable</c> is the structural guarantee behind that: it is the sole
    /// path <see cref="AndroidFileDialogService.ShareFileAsync"/> and
    /// <see cref="AndroidFileDialogService.OpenFileAsync"/> hand a file to the provider through,
    /// and it copies anything not already under this directory (or the attachments root) into a
    /// fresh subfolder here before ever calling <c>FileProvider.GetUriForFile</c> — so a caller
    /// handing either method a path outside both exposed roots cannot crash the provider.
    /// </summary>
    public static string SharedDir(Context context)
    {
        var dir = System.IO.Path.Join(CacheDir(context), "shared");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Where a picked attachment lands before this process reads it off disk. Each picker has its
    /// own folder under <c>picked</c>, so clearing one never deletes the other's file. Deliberately
    /// not exposed through the <c>FileProvider</c> (it never appears in <c>file_paths.xml</c>) --
    /// nothing outside this app needs a URI into it.
    /// </summary>
    public static string PickedAttachmentDir(Context context)
    {
        var dir = PickedAttachmentPath(context);
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Where an imported Stammdaten JSON lands; see <see cref="PickedAttachmentDir"/>.</summary>
    public static string PickedImportDir(Context context)
    {
        var dir = PickedImportPath(context);
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Empties <see cref="PickedAttachmentDir"/> before a new attachment is copied in (#407). Every
    /// file there is a copy the app already read -- the attachment itself is persisted into the
    /// incident -- so nothing is lost; without this the folder keeps a copy of every file ever
    /// attached on the device.
    /// </summary>
    public static void ClearPickedAttachments(Context context) => ClearFiles(PickedAttachmentPath(context));

    /// <summary>
    /// Empties both picker folders, and <c>picked</c> itself, where builds before #407 copied every
    /// pick. For a cold start only: a pick that completes after a configuration change still has to
    /// find its copy.
    /// </summary>
    public static void ClearPickedFiles(Context context)
    {
        ClearFiles(PickedRootPath(context));
        ClearFiles(PickedAttachmentPath(context));
        ClearFiles(PickedImportPath(context));
    }

    private static string PickedRootPath(Context context) => System.IO.Path.Join(CacheDir(context), "picked");

    private static string PickedAttachmentPath(Context context) =>
        System.IO.Path.Join(PickedRootPath(context), "attachment");

    private static string PickedImportPath(Context context) =>
        System.IO.Path.Join(PickedRootPath(context), "import");

    // Best-effort, and never throws: it runs in OnCreate and on the activity-result callback, where
    // nothing above it catches. A file that cannot be deleted now -- or a folder that cannot even be
    // listed, or does not exist yet -- goes on a later pass.
    private static void ClearFiles(string dir)
    {
        try
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(dir))
            {
                try
                {
                    System.IO.File.Delete(file);
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                {
                    // Left for the next pass; see above.
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Left for the next pass; see above.
        }
    }

    public static string TrustJsonPath(Context context) =>
        System.IO.Path.Join(context.FilesDir!.AbsolutePath, "trust.json");

    public static string LastConnectionJsonPath(Context context) =>
        System.IO.Path.Join(context.FilesDir!.AbsolutePath, "last-connection.json");
}
