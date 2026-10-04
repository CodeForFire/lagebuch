using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class ScbaView : UserControl
{
    private ScbaViewModel? _vm;

    public ScbaView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += OnAttachedToVisualTree;

        // The subscription is held only while this view is on screen. The tab that hosts it is
        // realized only while selected, so a view left subscribed after its tab was switched away
        // would be kept alive by the view model that outlives it.
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Subscribe();

        // A jump from another tab (#422) reaches no live view — this one is built a moment later,
        // once its tab is selected — so the scroll has to happen on arrival as well as on request.
        // A warning-bar jump (#539) built it to take that Trupp's Druck; arriving any other way
        // starts at the registration form's first field, FUNKRUFNAME (#540).
        if (!Reveal())
        {
            CallSignBox.Focus();
        }
    }

    private void Subscribe()
    {
        Unsubscribe();
        _vm = DataContext as ScbaViewModel;
        if (_vm is not null)
        {
            _vm.RevealRequested += OnRevealRequested;
            _vm.PressureRecorded += OnPressureRecorded;
        }
    }

    private void Unsubscribe()
    {
        if (_vm is not null)
        {
            _vm.RevealRequested -= OnRevealRequested;
            _vm.PressureRecorded -= OnPressureRecorded;
            _vm = null;
        }
    }

    private void OnRevealRequested(object? sender, EventArgs e) => Reveal();

    /// <summary>Scrolls to the selected Trupp, and focuses its Druck field if a warning bar asked
    /// for it. Returns whether focus was placed.</summary>
    private bool Reveal()
    {
        if (_vm?.SelectedTrupp is { } row)
        {
            TruppGrid.ScrollIntoView(row, null);
        }

        if (_vm?.TakePressureFocusRequest() is not { } target)
        {
            return false;
        }

        FocusPressureField(target);
        return true;
    }

    // Only a Druck typed and entered moves on to the next due Trupp (#539). A DRUCK click leaves
    // focus on the button, and the pointer user is not pulled to another row.
    private void OnPressureRecorded(object? sender, PressureRecordedEventArgs e)
    {
        if (e.NextDue is not { } next || _vm is null
            || !IsInPressureField(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
        {
            return;
        }

        _vm.SelectedTrupp = next;
        TruppGrid.ScrollIntoView(next, null);
        FocusPressureField(next);
    }

    private static bool IsInPressureField(IInputElement? focused) =>
        focused is Visual visual
            && visual.GetSelfAndVisualAncestors().OfType<NumericUpDown>().Any(n => n.Name == "PressureInput");

    /// <summary>
    /// Puts the caret in <paramref name="row"/>'s Druck field, in whichever layout is showing.
    /// Posted: the row is realised only once the scroll has been laid out, and a synchronous
    /// Focus() on a control that is not there yet is dropped.
    /// </summary>
    private void FocusPressureField(ScbaTruppRow row) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                Control list = TruppGrid.IsEffectivelyVisible ? TruppGrid : ScbaCards;
                list.GetVisualDescendants()
                    .OfType<NumericUpDown>()
                    .FirstOrDefault(n => n.Name == "PressureInput" && ReferenceEquals(n.DataContext, row))
                    ?.Focus(NavigationMethod.Tab);
            },
            DispatcherPriority.Background);
}
