using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class FilesView : UserControl
{
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

    private bool AcceptsDrop(DragEventArgs e) =>
        DataContext is FilesViewModel { IsReadOnly: false, IsUploading: false } && e.DataTransfer.Contains(DataFormat.File);

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

        // A dropped folder has no local path via IStorageFile and is silently skipped, not treated
        // as a file.
        var paths = e.DataTransfer.TryGetFiles()
            ?.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Cast<string>()
            .ToArray() ?? [];

        if (paths.Length > 0)
        {
            _ = vm.AddFilesAsync(paths);
        }
    }
}
