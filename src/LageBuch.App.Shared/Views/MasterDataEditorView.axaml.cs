using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class MasterDataEditorView : UserControl
{
    public MasterDataEditorView()
    {
        InitializeComponent();

        // Its own forwarder rather than one inherited from the shell: this view is also hosted
        // directly by the render tests, and a category rail that only collapses when MainView
        // happens to be its parent is a rail whose phone layout nothing can check. The container
        // query in the .axaml handles the sizing; this is the drill-down decision, which a Style
        // setter cannot make. No state is kept here.
        SizeChanged += OnSizeChanged;
        DataContextChanged += (_, _) => ApplyNarrow(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyNarrow(e.NewSize.Width);

    private void ApplyNarrow(double width)
    {
        if (DataContext is MasterDataEditorViewModel vm && width > 0)
        {
            vm.IsNarrow = width <= LayoutBreakpoints.Narrow;
        }
    }
}
