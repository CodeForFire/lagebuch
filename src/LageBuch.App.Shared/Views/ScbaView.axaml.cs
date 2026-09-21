using Avalonia;
using Avalonia.Controls;
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
        TruppfuehrerBox.Focus();
        Subscribe();

        // A jump from another tab (#422) reaches no live view — this one is built a moment later,
        // once its tab is selected — so the scroll has to happen on arrival as well as on request.
        Reveal(this, EventArgs.Empty);
    }

    private void Subscribe()
    {
        Unsubscribe();
        _vm = DataContext as ScbaViewModel;
        if (_vm is not null)
        {
            _vm.RevealRequested += Reveal;
        }
    }

    private void Unsubscribe()
    {
        if (_vm is not null)
        {
            _vm.RevealRequested -= Reveal;
            _vm = null;
        }
    }

    private void Reveal(object? sender, EventArgs e)
    {
        if (_vm?.SelectedTrupp is { } row)
        {
            TruppGrid.ScrollIntoView(row, null);
        }
    }
}
