using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Behaviors;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class FilesView : UserControl
{
    private TopLevel? _pasteHost;

    public FilesView()
    {
        InitializeComponent();

        // Drag-and-drop (#262 UX follow-up): Avalonia's DragDrop events are routed events, not
        // bindable commands, so wiring lives here rather than in FilesViewModel/XAML bindings.
        FilesDropZone.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        FilesDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        FilesDropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        FilesDropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // Strg+V (#421) is heard on the TopLevel while DATEIEN is on screen, not on this view: after a
    // click on the nav rail the focus sits outside it, and the key would never pass here. Bubbling,
    // so a focused TextBox (the rename field, any other field) pastes its own text and marks the key
    // handled first.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _pasteHost = TopLevel.GetTopLevel(this);
        _pasteHost?.AddHandler(KeyDownEvent, OnKeyDown);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _pasteHost?.RemoveHandler(KeyDownEvent, OnKeyDown);
        _pasteHost = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Image data and plain text are accepted too: a browser drags an image as bitmap data or as a
    // link, never as a local file, and a link earns a hint rather than silence (#421).
    private bool AcceptsDrop(DragEventArgs e) =>
        DataContext is FilesViewModel { IsReadOnly: false, IsUploading: false }
        && (e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.Contains(DataFormat.Bitmap) || e.DataTransfer.Contains(DataFormat.Text));

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var accepts = AcceptsDrop(e);
        e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        FilesDropZone.Classes.Set("drag-over", accepts);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = AcceptsDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e) => FilesDropZone.Classes.Remove("drag-over");

    private void OnDrop(object? sender, DragEventArgs e)
    {
        FilesDropZone.Classes.Remove("drag-over");
        e.Handled = true;
        if (!AcceptsDrop(e) || DataContext is not FilesViewModel vm)
        {
            return;
        }

        var paths = LocalPaths(e.DataTransfer.TryGetFiles());
        if (paths.Length > 0)
        {
            _ = vm.AddFilesAsync(paths);
            return;
        }

        using var bitmap = e.DataTransfer.TryGetBitmap();
        if (bitmap is not null)
        {
            _ = vm.PasteImageAsync(EncodePng(bitmap));
        }
        else if (e.DataTransfer.Contains(DataFormat.Text))
        {
            vm.ReportLinkDropped();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not FilesViewModel { IsReadOnly: false, IsUploading: false } vm || !IsPasteGesture(e)
            || (e.Source is Visual source && Overlay.IsInOverlay(source)))
        {
            return;
        }

        e.Handled = true;
        _ = PasteAsync(vm);
    }

    // The platform's own paste gesture, the one TextBox answers to: Strg+V, or Cmd+V on macOS.
    private bool IsPasteGesture(KeyEventArgs e) =>
        this.GetPlatformSettings()?.HotkeyConfiguration.Paste is { Count: > 0 } gestures
            ? gestures.Any(g => g.Matches(e))
            : e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control;

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The platform clipboard can fail in ways of its own (X11 selection owner gone, a format that will not decode); it surfaces through ReportPasteFailed as the Dateien error line.")]
    private async Task PasteAsync(FilesViewModel vm)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            using var data = await clipboard.TryGetDataAsync();
            if (data is null)
            {
                vm.ReportNothingToPaste();
                return;
            }

            // A file copied in the file manager pastes as itself, under its own name and type.
            var paths = LocalPaths(await data.TryGetFilesAsync());
            if (paths.Length > 0)
            {
                await vm.AddFilesAsync(paths);
                return;
            }

            using var bitmap = await data.TryGetBitmapAsync();
            if (bitmap is null)
            {
                vm.ReportNothingToPaste();
                return;
            }

            await vm.PasteImageAsync(EncodePng(bitmap));
        }
        catch (Exception ex)
        {
            vm.ReportPasteFailed(ex.Message);
        }
    }

    // A dropped or copied folder has no local path via IStorageFile and is skipped, not treated as
    // a file.
    private static string[] LocalPaths(IEnumerable<IStorageItem>? items) =>
        items?.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToArray() ?? [];

    // Re-encoding as PNG also drops whatever metadata (EXIF, a camera's GPS position) the source
    // image carried, so a pasted photo arrives clean (#384).
    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
}
