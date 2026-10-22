using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class RolesView : UserControl
{
    /// <summary>What a dragged Funktion carries (#703): its assignment id, as text.</summary>
    public static readonly DataFormat<string> RoleDragFormat = DataFormat.CreateStringApplicationFormat("lagebuch-funktion");

    // The row a drop would land under, for the highlight only; whether it may is the view model's.
    private DataGridRow? _dropTarget;

    // The form's first field, FUNKTION (#540), takes focus only when the user asked for the module
    // (#542): see WorkspaceFocus and the dock's EntryForm.FirstField.
    public RolesView()
    {
        InitializeComponent();

        // Drag-and-drop in the Führungsstruktur (#703). DragDrop's events are routed events, not
        // bindable commands, so they are forwarded from here; CanMoveUnder and MoveUnder decide.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        RolesGrid.AddHandler(DragDrop.DragOverEvent, OnGridDragOver);
        RolesGrid.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(null));
        RolesGrid.AddHandler(DragDrop.DropEvent, OnGridDrop);
        TopLevelDropZone.AddHandler(DragDrop.DragOverEvent, OnTopLevelDragOver);
        TopLevelDropZone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => TopLevelDropZone.Classes.Set("drag-over", false));
        TopLevelDropZone.AddHandler(DragDrop.DropEvent, OnTopLevelDrop);
    }

    private RolesViewModel? ViewModel => DataContext as RolesViewModel;

    // A press on a row's grip starts the drag. Tunnelled, so the DataGrid's own press handling
    // (selection) cannot swallow it first.
    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control source
            || source.FindAncestorOfType<Border>(includeSelf: true) is not { Name: "RoleDragHandle", DataContext: RoleAssignmentRow row }
            || ViewModel is not { } vm
            || !row.CanSetReportsTo
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        using var data = new DataTransfer();
        data.Add(DataTransferItem.Create(RoleDragFormat, row.Id.ToString()));
        vm.BeginDrag(row.Id);
        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            vm.EndDrag();
            SetDropTarget(null);
            TopLevelDropZone.Classes.Set("drag-over", false);
        }
    }

    private static Guid? DraggedId(DragEventArgs e) =>
        e.DataTransfer.TryGetValue(RoleDragFormat) is { } text && Guid.TryParse(text, out var id) ? id : null;

    private static DataGridRow? RowUnder(DragEventArgs e) =>
        (e.Source as Control)?.FindAncestorOfType<DataGridRow>(includeSelf: true);

    private void OnGridDragOver(object? sender, DragEventArgs e)
    {
        var target = RowUnder(e);
        var allowed = DraggedId(e) is { } dragged
            && target?.DataContext is RoleAssignmentRow superior
            && ViewModel?.CanMoveUnder(dragged, superior.Id) == true;
        e.DragEffects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        SetDropTarget(allowed ? target : null);
        e.Handled = true;
    }

    private void OnGridDrop(object? sender, DragEventArgs e)
    {
        SetDropTarget(null);
        if (DraggedId(e) is { } dragged && RowUnder(e)?.DataContext is RoleAssignmentRow superior)
        {
            ViewModel?.MoveUnder(dragged, superior.Id);
        }

        e.Handled = true;
    }

    private void OnTopLevelDragOver(object? sender, DragEventArgs e)
    {
        var allowed = DraggedId(e) is { } dragged && ViewModel?.CanMoveUnder(dragged, null) == true;
        e.DragEffects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        TopLevelDropZone.Classes.Set("drag-over", allowed);
        e.Handled = true;
    }

    private void OnTopLevelDrop(object? sender, DragEventArgs e)
    {
        TopLevelDropZone.Classes.Set("drag-over", false);
        if (DraggedId(e) is { } dragged)
        {
            ViewModel?.MoveUnder(dragged, null);
        }

        e.Handled = true;
    }

    private void SetDropTarget(DataGridRow? row)
    {
        if (ReferenceEquals(_dropTarget, row))
        {
            return;
        }

        _dropTarget?.Classes.Set("drop-target", false);
        _dropTarget = row;
        _dropTarget?.Classes.Set("drop-target", true);
    }
}
