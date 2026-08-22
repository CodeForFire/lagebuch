using Android.Content;
using AndroidX.Core.Content;

using LageBuch.AppLogic.Services;

namespace LageBuch.App.Android.Services;

/// <summary>
/// Android has no free-form filesystem browsing (design spec §5's "app-managed list" decision):
/// incident creation never shows a picker — it generates a unique path in app-private storage.
/// "Open" has no Android equivalent of "browse anywhere" (Home's Recent list is how incidents are
/// reopened here), so it returns null. PDF/JSON export write to app-private cache, then
/// <see cref="ShareFileAsync"/> hands the file off via Android's share sheet.
/// </summary>
public sealed class AndroidFileDialogService : IFileDialogService
{
    private readonly Activity _activity;

    public AndroidFileDialogService(Activity activity) => _activity = activity;

    // initialFolder is meaningless here -- incidents always land in app-private storage (§ above).
    public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null)
    {
        var dir = AndroidAppPaths.IncidentsDir(_activity);
        var path = System.IO.Path.Combine(dir, suggestedFileName);
        var count = 1;
        while (System.IO.File.Exists(path))
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(suggestedFileName);
            var ext = System.IO.Path.GetExtension(suggestedFileName);
            path = System.IO.Path.Combine(dir, $"{stem} ({count++}){ext}");
        }
        return Task.FromResult<string?>(path);
    }

    // No "browse anywhere" concept once incidents live in app-private storage — Home's Recent
    // list is the only way to reopen one on this platform. See design spec §5.
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickExportPdfAsync(string suggestedFileName) =>
        Task.FromResult<string?>(System.IO.Path.Combine(AndroidAppPaths.CacheDir(_activity), suggestedFileName));

    public Task<string?> PickExportJsonAsync(string suggestedFileName) =>
        Task.FromResult<string?>(System.IO.Path.Combine(AndroidAppPaths.CacheDir(_activity), suggestedFileName));

    private TaskCompletionSource<string?>? _pendingImport;

    public Task<string?> PickImportJsonAsync()
    {
        _pendingImport = new TaskCompletionSource<string?>();
        OnLaunchImportPicker?.Invoke();
        return _pendingImport.Task;
    }

    /// <summary>Set by MainActivity to the registered ActivityResultLauncher's Launch call.</summary>
    public Action? OnLaunchImportPicker { get; set; }

    /// <summary>
    /// Called by MainActivity's registered picker callback once the user selects a file (or cancels).
    /// Copies the content:// URI's bytes into app-private cache, since IMasterDataFileService.Read
    /// needs a real filesystem path.
    /// </summary>
    public void CompleteImport(global::Android.Net.Uri? uri)
    {
        var pending = _pendingImport;
        _pendingImport = null;
        if (pending is null)
            return;
        if (uri is null)
        {
            pending.SetResult(null);
            return;
        }
        var destPath = System.IO.Path.Combine(AndroidAppPaths.CacheDir(_activity), "import.json");
        using (var input = _activity.ContentResolver!.OpenInputStream(uri)!)
        using (var output = System.IO.File.Create(destPath))
            input.CopyTo(output);
        pending.SetResult(destPath);
    }

    private TaskCompletionSource<string?>? _pendingAttachment;

    public Task<string?> PickAttachmentAsync()
    {
        _pendingAttachment = new TaskCompletionSource<string?>();
        OnLaunchAttachmentPicker?.Invoke();
        return _pendingAttachment.Task;
    }

    /// <summary>Set by MainActivity to the registered ActivityResultLauncher's Launch call.</summary>
    public Action? OnLaunchAttachmentPicker { get; set; }

    /// <summary>
    /// Called by MainActivity's registered picker callback once the user selects a file (or
    /// cancels). Copies the content:// URI's bytes into app-private cache under its original
    /// display name (falling back to a generic one), preserving the extension both
    /// <see cref="LageBuch.AppLogic.ViewModels.FilesViewModel"/>'s content-type inference and the sibling-folder attachment
    /// naming scheme rely on.
    /// </summary>
    public void CompleteAttachment(global::Android.Net.Uri? uri)
    {
        var pending = _pendingAttachment;
        _pendingAttachment = null;
        if (pending is null)
            return;
        if (uri is null)
        {
            pending.SetResult(null);
            return;
        }
        var destPath = System.IO.Path.Combine(AndroidAppPaths.CacheDir(_activity), DisplayNameOf(uri));
        using (var input = _activity.ContentResolver!.OpenInputStream(uri)!)
        using (var output = System.IO.File.Create(destPath))
            input.CopyTo(output);
        pending.SetResult(destPath);
    }

    private string DisplayNameOf(global::Android.Net.Uri uri)
    {
        using var cursor = _activity.ContentResolver!.Query(uri, null, null, null, null);
        if (cursor is not null && cursor.MoveToFirst())
        {
            var index = cursor.GetColumnIndex(global::Android.Provider.IOpenableColumns.DisplayName);
            if (index >= 0)
            {
                var name = cursor.GetString(index);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        return "anhang";
    }

    public Task ShareFileAsync(string path, string mimeType)
    {
        var authority = $"{_activity.PackageName}.fileprovider";
        var uri = FileProvider.GetUriForFile(_activity, authority, new Java.IO.File(path));
        var intent = new Intent(Intent.ActionSend);
        intent.SetType(mimeType);
        intent.PutExtra(Intent.ExtraStream, uri);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        _activity.StartActivity(Intent.CreateChooser(intent, "Teilen"));
        return Task.CompletedTask;
    }

    // View-in-place (not a share sheet): opens whatever app the device has registered for the
    // type, exactly like a desktop double-click.
    public Task OpenFileAsync(string path)
    {
        var authority = $"{_activity.PackageName}.fileprovider";
        var uri = FileProvider.GetUriForFile(_activity, authority, new Java.IO.File(path));
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, MimeTypeOf(path));
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        _activity.StartActivity(intent);
        return Task.CompletedTask;
    }

    // Unlike OpenFileAsync, this is a remote http(s) URL, not a local file -- no FileProvider
    // involved, just hand it straight to whatever app the device has registered for the scheme.
    // The http(s)-only check is enforced independently here too (not only by LinksViewModel, the
    // one caller today): an unfiltered scheme handed to ActionView is a known Android
    // intent-redirection surface (intent://, content://, custom app schemes), so this method's own
    // contract ("an http(s) URL") must hold regardless of what a future caller passes in.
    public Task OpenUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Task.CompletedTask;

        var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri.AbsoluteUri));
        _activity.StartActivity(intent);
        return Task.CompletedTask;
    }

    private static string MimeTypeOf(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "*/*"
    };
}
