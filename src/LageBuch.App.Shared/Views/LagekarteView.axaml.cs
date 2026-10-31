using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using LageBuch.App.Shared.Controls;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.App.Shared.Views;

public partial class LagekarteView : UserControl
{
    // Same slop as the map's own: a mark pressed and let go in place is a tap on the map there.
    private const double TapSlop = 6;

    // The mark under the hand while it is dragged, and where the press began.
    private LagekarteMarker? _dragged;
    private Point _pressedAt;
    private bool _moved;

    // The sign under the hand while it is dragged, and where it stood from its pin at the press.
    private LagekarteMarker? _draggedSign;
    private LabelOffset _offsetAtPress = new(0, 0);

    public LagekarteView() => InitializeComponent();

    private LagekarteViewModel? ViewModel => DataContext as LagekarteViewModel;

    private void OnMarkerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LagekarteMarker marker } control
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragged = marker;
        _pressedAt = e.GetPosition(Map);
        _moved = false;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    // The mark follows the hand; nothing is stored until it is let go.
    private void OnMarkerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null || ViewModel is not { CanEdit: true })
        {
            return;
        }

        var at = e.GetPosition(Map);
        if (!_moved && Math.Abs(at.X - _pressedAt.X) < TapSlop && Math.Abs(at.Y - _pressedAt.Y) < TapSlop)
        {
            return;
        }

        _moved = true;
        if (Map.ScreenToGeo(at) is { } position)
        {
            _dragged.Position = position;
        }

        e.Handled = true;
    }

    private void OnMarkerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged is not { } marker || ViewModel is not { } vm)
        {
            return;
        }

        _dragged = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (Map.ScreenToGeo(e.GetPosition(Map)) is not { } position)
        {
            return;
        }

        if (_moved)
        {
            vm.MoveMarker(marker, position);
        }
        else if (vm.MapTappedCommand.CanExecute(position))
        {
            vm.MapTappedCommand.Execute(position);
        }
    }

    // Lost mid-drag (a window switch, a second finger): put the mark back where it is stored.
    private void OnMarkerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragged is not null && _moved)
        {
            ViewModel?.RestoreMarkers();
        }

        _dragged = null;
    }

    private void OnSignPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LagekarteMarker marker } control
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A sign the map placed has no offset of its own yet: the drag starts from where it is drawn.
        _draggedSign = marker;
        _offsetAtPress = marker.LabelOffset ?? MapMarkerPanel.GetResolvedOffset(control) ?? new LabelOffset(0, 0);
        _pressedAt = e.GetPosition(Map);
        _moved = false;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    // The sign follows the hand and the strich with it; the pin, and so the unit, stays put.
    private void OnSignMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedSign is null || ViewModel is not { CanEdit: true })
        {
            return;
        }

        var at = e.GetPosition(Map);
        if (!_moved && Math.Abs(at.X - _pressedAt.X) < TapSlop && Math.Abs(at.Y - _pressedAt.Y) < TapSlop)
        {
            return;
        }

        _moved = true;
        _draggedSign.LabelOffset = new LabelOffset(
            Math.Clamp(_offsetAtPress.Dx + at.X - _pressedAt.X, -LabelOffset.MaxPixels, LabelOffset.MaxPixels),
            Math.Clamp(_offsetAtPress.Dy + at.Y - _pressedAt.Y, -LabelOffset.MaxPixels, LabelOffset.MaxPixels));
        e.Handled = true;
    }

    private void OnSignReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedSign is not { } marker || ViewModel is not { } vm)
        {
            return;
        }

        _draggedSign = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (_moved && marker.LabelOffset is { } offset)
        {
            vm.MoveLabel(marker, offset);
        }
        else if (Map.ScreenToGeo(e.GetPosition(Map)) is { } position && vm.MapTappedCommand.CanExecute(position))
        {
            vm.MapTappedCommand.Execute(position);
        }
    }

    private void OnSignCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_draggedSign is not null && _moved)
        {
            ViewModel?.RestoreMarkers();
        }

        _draggedSign = null;
    }
}
