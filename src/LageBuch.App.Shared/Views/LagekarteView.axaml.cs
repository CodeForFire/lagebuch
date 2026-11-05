using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using LageBuch.App.Shared.Controls;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.App.Shared.Views;

public partial class LagekarteView : UserControl
{
    public LagekarteView() => InitializeComponent();

    private LagekarteViewModel? ViewModel => DataContext as LagekarteViewModel;

    private void OnMarkerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LagekarteMarker marker } control
            || ViewModel is not { } vm
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var at = e.GetPosition(Map);

        // MapMarkerPanel centres a mark on its spot, so the mark's centre is where it stands.
        var spot = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Map) ?? at;
        vm.BeginMarkerDrag(marker, (at.X, at.Y), (spot.X, spot.Y));
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnMarkerMoved(object? sender, PointerEventArgs e)
    {
        var at = e.GetPosition(Map);
        if (ViewModel?.ContinueMarkerDrag((at.X, at.Y), ScreenToGeo) is true)
        {
            e.Handled = true;
        }
    }

    private void OnMarkerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var at = e.GetPosition(Map);
        if (ViewModel?.EndMarkerDrag((at.X, at.Y), ScreenToGeo) is true)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private GeoPoint? ScreenToGeo(double x, double y) => Map.ScreenToGeo(new Point(x, y));

    private void OnMarkerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ViewModel?.CancelMarkerDrag();

    private void OnSignPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LagekarteMarker marker } control
            || ViewModel is not { } vm || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var at = e.GetPosition(Map);
        vm.BeginSignDrag(marker, (at.X, at.Y), MapMarkerPanel.GetResolvedOffset(control) ?? new LabelOffset(0, 0));
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnSignMoved(object? sender, PointerEventArgs e)
    {
        var at = e.GetPosition(Map);
        if (ViewModel?.UpdateSignDrag((at.X, at.Y)) == true)
        {
            e.Handled = true;
        }
    }

    private void OnSignReleased(object? sender, PointerReleasedEventArgs e)
    {
        var at = e.GetPosition(Map);
        if (ViewModel?.EndSignDrag((at.X, at.Y), ScreenToGeo) == true)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnSignCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ViewModel?.CancelSignDrag();
}
