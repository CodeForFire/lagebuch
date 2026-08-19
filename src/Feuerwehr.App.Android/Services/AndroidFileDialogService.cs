using Android.Content;
using AndroidX.Core.Content;

using Feuerwehr.AppLogic.Services;

namespace Feuerwehr.App.Android.Services;

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
}
