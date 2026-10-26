using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class LeadershipView : UserControl
{
    /// <summary>What a dragged unit carries: its id, as text.</summary>
    public static readonly DataFormat<string> UnitDragFormat = DataFormat.CreateStringApplicationFormat("lagebuch-einheit");

    /// <summary>What a dragged card carries: its node's id, as text.</summary>
    public static readonly DataFormat<string> NodeDragFormat = DataFormat.CreateStringApplicationFormat("lagebuch-abschnitt");

    // The card or strip a drop would land in, for the highlight only; whether it may is the view model's.
    private Border? _dropTarget;

    public LeadershipView()
    {
        InitializeComponent();

        // Drag and drop of units between the cards. DragDrop's events are routed events, not bindable
        // commands, so they are forwarded from here; CanMoveUnit and MoveUnit decide.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        BoardRoot.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        BoardRoot.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(null));
        BoardRoot.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private LeadershipViewModel? ViewModel => DataContext as LeadershipViewModel;

    // A press on a unit's grip starts the drag; a mouse or pen only, so a finger on a wide tablet
    // still scrolls the board (the picker moves the unit there). On Android a mouse only: a pen drag
    // there has never been tried on a device. Tunnelled, so no control under the grip can swallow
    // the press first.
    private static bool StartsDrag(PointerType type) =>
        type == PointerType.Mouse || (type == PointerType.Pen && !OperatingSystem.IsAndroid());

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // The nearest grip: a unit's sits inside its card, so it wins over the card's own.
        if (!StartsDrag(e.Pointer.Type)
            || e.Source is not Control source
            || source.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("unit-grip"))
                is not { DataContext: BoardUnitRow or BoardNodeCard } grip
            || ViewModel is not { IsReadOnly: false } vm
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        using var data = new DataTransfer();
        if (grip.DataContext is BoardNodeCard card)
        {
            data.Add(DataTransferItem.Create(NodeDragFormat, card.Id.ToString()));
            vm.BeginNodeDrag(card.Id);
        }
        else if (grip.DataContext is BoardUnitRow row)
        {
            data.Add(DataTransferItem.Create(UnitDragFormat, row.Id.ToString()));
            vm.BeginDrag(row.Id);
        }

        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        finally
        {
            vm.EndDrag();
            vm.EndNodeDrag();
            SetDropTarget(null);
        }
    }

    private static Guid? DraggedId(DragEventArgs e) =>
        e.DataTransfer.TryGetValue(UnitDragFormat) is { } text && Guid.TryParse(text, out var id) ? id : null;

    private static Guid? DraggedNodeId(DragEventArgs e) =>
        e.DataTransfer.TryGetValue(NodeDragFormat) is { } text && Guid.TryParse(text, out var id) ? id : null;

    // Where a dragged card would land: before the card whose head is under the pointer (as its
    // sibling), into the Abschnitt whose strip is, or last at the top level. False over anything else.
    private static bool TryNodeTargetUnder(
        DragEventArgs e, [NotNullWhen(true)] out Border? target, out Guid? parentId, out Guid? beforeNodeId)
    {
        foreach (var border in ((e.Source as Control)?.GetSelfAndVisualAncestors() ?? []).OfType<Border>())
        {
            if (border.Name == "NodeEndStrip")
            {
                (target, parentId, beforeNodeId) = (border, null, null);
                return true;
            }

            if (border.Classes.Contains("node-drop-into") && border.DataContext is BoardNodeCard into)
            {
                (target, parentId, beforeNodeId) = (border, into.Id, null);
                return true;
            }

            if (border.Classes.Contains("node-drop-before") && border.DataContext is BoardNodeCard before)
            {
                (target, parentId, beforeNodeId) = (border, before.ParentId, before.Id);
                return true;
            }
        }

        (target, parentId, beforeNodeId) = (null, null, null);
        return false;
    }

    // The place under the pointer: the innermost card (an Unterabschnitt beats its Abschnitt), or the
    // Nicht-zugeordnet strip, which stands for no node. False when the pointer is over neither.
    private static bool TryTargetUnder(DragEventArgs e, [NotNullWhen(true)] out Border? target, out Guid? nodeId)
    {
        foreach (var border in ((e.Source as Control)?.GetSelfAndVisualAncestors() ?? []).OfType<Border>())
        {
            if (border.Name == "UnplacedStrip")
            {
                (target, nodeId) = (border, null);
                return true;
            }

            if (border.Classes.Contains("board-card") && border.DataContext is BoardNodeCard card)
            {
                (target, nodeId) = (border, card.Id);
                return true;
            }
        }

        (target, nodeId) = (null, null);
        return false;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        Border? highlight = null;
        if (DraggedId(e) is { } dragged
            && TryTargetUnder(e, out var target, out var nodeId)
            && ViewModel?.CanMoveUnit(dragged, nodeId) == true)
        {
            highlight = target;
        }
        else if (DraggedNodeId(e) is { } draggedNode
            && TryNodeTargetUnder(e, out var nodeTarget, out var parentId, out var beforeNodeId)
            && ViewModel?.CanMoveNode(draggedNode, parentId, beforeNodeId) == true)
        {
            highlight = nodeTarget;
        }

        e.DragEffects = highlight is null ? DragDropEffects.None : DragDropEffects.Move;
        SetDropTarget(highlight);
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropTarget(null);
        if (DraggedId(e) is { } dragged && TryTargetUnder(e, out _, out var nodeId))
        {
            ViewModel?.MoveUnit(dragged, nodeId);
        }
        else if (DraggedNodeId(e) is { } draggedNode && TryNodeTargetUnder(e, out _, out var parentId, out var beforeNodeId))
        {
            ViewModel?.MoveNode(draggedNode, parentId, beforeNodeId);
        }

        e.Handled = true;
    }

    private void SetDropTarget(Border? target)
    {
        if (ReferenceEquals(_dropTarget, target))
        {
            return;
        }

        _dropTarget?.Classes.Set("drop-target", false);
        _dropTarget = target;
        _dropTarget?.Classes.Set("drop-target", true);
    }

    // A Button inside a Flyout does not dismiss it; the command itself runs from the Button. The
    // Stufe button lives in the El card's template, so it is looked up rather than named here.
    // Closed only once the click is over: Button runs its command after raising Click, and the
    // entries take StufeChoices from the DataContext the flyout inherits, which closing takes
    // away -- closed inside Click, the entry had no command left by then and nothing was chosen.
    private void OnStufeChoiceClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
            this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "StufeButton")?.Flyout?.Hide());
}
