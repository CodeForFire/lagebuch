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

    public Task<string?> PickSaveAsync(string suggestedFileName)
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

    // Implemented in Task 10 — needs the ActivityResultLauncher document-picker registered in
    // MainActivity.OnCreate.
    public Task<string?> PickImportJsonAsync() => throw new NotImplementedException();

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
