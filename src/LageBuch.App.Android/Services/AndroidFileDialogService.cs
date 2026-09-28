using Android.Content;
using AndroidX.Core.Content;

using LageBuch.AppLogic.Services;
using LageBuch.Domain.Files;

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
        var path = System.IO.Path.Join(dir, suggestedFileName);
        var count = 1;
        while (System.IO.File.Exists(path))
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(suggestedFileName);
            var ext = System.IO.Path.GetExtension(suggestedFileName);
            path = System.IO.Path.Join(dir, $"{stem} ({count++}){ext}");
        }

        return Task.FromResult<string?>(path);
    }

    // No "browse anywhere" concept once incidents live in app-private storage — Home's Recent
    // list is the only way to reopen one on this platform. See design spec §5.
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickExportPdfAsync(string suggestedFileName) =>
        Task.FromResult<string?>(System.IO.Path.Join(AndroidAppPaths.SharedDir(_activity), suggestedFileName));

    public Task<string?> PickExportJsonAsync(string suggestedFileName) =>
        Task.FromResult<string?>(System.IO.Path.Join(AndroidAppPaths.SharedDir(_activity), suggestedFileName));

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
        {
            return;
        }

        if (uri is null)
        {
            pending.SetResult(null);
            return;
        }

        var destPath = System.IO.Path.Join(AndroidAppPaths.PickedDir(_activity), "import.json");
        try
        {
            CopyTo(uri, destPath);
        }
        catch (Exception ex) when (IsPickFailure(ex))
        {
            // This method runs on Android's activity-result callback, which has no caller to catch
            // for it -- an exception here takes the Activity down. Hand it to the awaiting picker
            // task instead, where MasterDataEditorViewModel.Import turns it into an error line.
            pending.SetException(ex);
            return;
        }

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
        {
            return;
        }

        if (uri is null)
        {
            pending.SetResult(null);
            return;
        }

        var destPath = System.IO.Path.Join(AndroidAppPaths.PickedDir(_activity), DisplayNameOf(uri));
        try
        {
            CopyTo(uri, destPath);
        }
        catch (Exception ex) when (IsPickFailure(ex))
        {
            // Same reasoning as CompleteImport: FilesViewModel.AddFileAsync surfaces it.
            pending.SetException(ex);
            return;
        }

        pending.SetResult(destPath);
    }

    /// <summary>
    /// Streams a picked <c>content://</c> URI into <paramref name="destPath"/>. The null-forgiving
    /// <c>!</c> on OpenInputStream is deliberate rather than checked: a provider that returns no
    /// stream is exactly the failure the callers catch, and an NRE here carries the same meaning as
    /// the IOException a broken stream would give.
    /// </summary>
    private void CopyTo(global::Android.Net.Uri uri, string destPath)
    {
        using var input = _activity.ContentResolver!.OpenInputStream(uri)!;
        using var output = System.IO.File.Create(destPath);
        input.CopyTo(output);
    }

    /// <summary>
    /// The ways reading someone else's content provider can fail: it hands back nothing, throws
    /// across the Binder, revokes the grant, or the copy runs out of space. Deliberately not a
    /// blanket catch -- a genuine bug in our own code should still surface as a crash.
    /// <para>
    /// <see cref="Java.Lang.Throwable"/> is the catch-all for the provider's side of the Binder:
    /// every bound Java exception derives from it, including the cancellation and
    /// security exceptions a provider can raise, so they need no separate entries here.
    /// </para>
    /// </summary>
    private static bool IsPickFailure(Exception ex) =>
        ex is System.IO.IOException
            or UnauthorizedAccessException
            or NullReferenceException
            or OperationCanceledException
            or Java.Lang.Throwable;

    // A content provider fully controls DISPLAY_NAME -- a hostile one can return "../../evil" to
    // escape PickedDir, so SafeFileName.Sanitize reduces it to a bare, harmless file name before
    // it ever reaches Path.Join.
    private string DisplayNameOf(global::Android.Net.Uri uri)
    {
        try
        {
            using var cursor = _activity.ContentResolver!.Query(uri, null, null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var index = cursor.GetColumnIndex(global::Android.Provider.IOpenableColumns.DisplayName);
                if (index >= 0)
                {
                    return SafeFileName.Sanitize(cursor.GetString(index));
                }
            }
        }
        catch (Exception ex) when (IsPickFailure(ex))
        {
            // A provider that will not answer the name query is no reason to abandon the pick --
            // the bytes are still readable, and the fallback name is a perfectly good one.
        }

        return SafeFileName.DefaultFallback;
    }

    public Task ShareFileAsync(string path, string mimeType)
    {
        var shareablePath = EnsureShareable(path);
        if (shareablePath is null)
        {
            return Task.CompletedTask;
        }

        var authority = $"{_activity.PackageName}.fileprovider";
        var uri = FileProvider.GetUriForFile(_activity, authority, new Java.IO.File(shareablePath));
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
        var shareablePath = EnsureShareable(path);
        if (shareablePath is null)
        {
            return Task.CompletedTask;
        }

        var authority = $"{_activity.PackageName}.fileprovider";
        var uri = FileProvider.GetUriForFile(_activity, authority, new Java.IO.File(shareablePath));
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, MimeTypeOf(shareablePath));
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        _activity.StartActivity(intent);
        return Task.CompletedTask;
    }

    // The sole choke point between a filesystem path and the FileProvider: this is a structural
    // guarantee, not caller convention. Neither ShareFileAsync nor OpenFileAsync ever calls
    // FileProvider.GetUriForFile directly on a caller-supplied path -- both route through here
    // first. A path already under SharedDir (every export writes straight there) or under the
    // "lagebuch" attachments root a sibling PR writes into (see file_paths.xml's "attachments"
    // entry) passes through untouched -- no second copy. Anything else (e.g. today,
    // FilesViewModel.OpenFileAsync's tempPath under the bare cache root, until that sibling PR
    // relocates it) is copied into a fresh SharedDir subfolder first, so
    // FileProvider.GetUriForFile can never throw IllegalArgumentException for an unconfigured
    // root no matter what a caller passes in. Returns null (callers then no-op rather than
    // launch anything) when the source is not an existing regular file.
    private string? EnsureShareable(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        var fullPath = System.IO.Path.GetFullPath(path);
        var sharedDir = AndroidAppPaths.SharedDir(_activity);
        var attachmentsDir = System.IO.Path.Join(AndroidAppPaths.CacheDir(_activity), AttachmentTempPaths.RootFolderName);

        if (IsUnder(fullPath, sharedDir) || IsUnder(fullPath, attachmentsDir))
        {
            return fullPath;
        }

        var destDir = System.IO.Path.Join(sharedDir, Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(destDir);
        var destPath = System.IO.Path.Join(destDir, System.IO.Path.GetFileName(fullPath));
        System.IO.File.Copy(fullPath, destPath, overwrite: true);
        return destPath;
    }

    private static bool IsUnder(string fullPath, string dir)
    {
        var normalizedDir = System.IO.Path.GetFullPath(dir) + System.IO.Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedDir, StringComparison.Ordinal);
    }

    // Unlike OpenFileAsync, this is a remote http(s) URL, not a local file -- no FileProvider
    // involved, just hand it straight to whatever app the device has registered for the scheme.
    // The http(s)-only check is enforced independently here too (not only by LinksViewModel, the
    // one caller today): an unfiltered scheme handed to ActionView is a known Android
    // intent-redirection surface (intent://, content://, custom app schemes), so this method's own
    // contract ("an http(s) URL") must hold regardless of what a future caller passes in.
    public Task OpenUrlAsync(string url)
    {
        if (!HttpUrlValidator.TryGetHttpUri(url, out var uri))
        {
            return Task.CompletedTask;
        }

        var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri.AbsoluteUri));
        _activity.StartActivity(intent);
        return Task.CompletedTask;
    }

    // ActionSendto, not ActionView: it is the action documented for mailto: and it resolves only to
    // apps declaring a SENDTO/mailto: intent filter, which narrows the redirection surface further
    // than a generic view. The address check runs here too, for the same reason OpenUrlAsync's
    // scheme check does -- this method's contract has to hold on its own.
    public Task OpenMailAsync(string address)
    {
        if (!MailAddressValidator.TryGetMailtoUri(address, out var uri))
        {
            return Task.CompletedTask;
        }

        // Deliberately no ResolveActivity first: Android 11 package visibility would hide the
        // result behind a <queries> declaration, and "hidden" would be indistinguishable from
        // "absent". Starting and letting ActivityNotFoundException out is what lets ContactLauncher
        // tell the operator there is no mail app, instead of failing silently.
        var intent = new Intent(Intent.ActionSendto, global::Android.Net.Uri.Parse(uri.AbsoluteUri));
        _activity.StartActivity(intent);
        return Task.CompletedTask;
    }

    // ActionDial, never ActionCall. ActionDial opens the dialer with the number filled in and the
    // operator still presses call; ActionCall places it immediately and needs the CALL_PHONE
    // permission, which this app will not ask for -- a misparsed roster number would otherwise dial
    // on its own from a device in an Einsatz. ActionDial needs no permission and no manifest entry.
    public Task OpenPhoneAsync(string number)
    {
        if (!PhoneNumberValidator.TryGetTelUri(number, out var uri))
        {
            return Task.CompletedTask;
        }

        var intent = new Intent(Intent.ActionDial, global::Android.Net.Uri.Parse(uri.AbsoluteUri));
        _activity.StartActivity(intent);
        return Task.CompletedTask;
    }

    private static string MimeTypeOf(string path) => IncidentFile.GetMimeType(path, "*/*");
}
