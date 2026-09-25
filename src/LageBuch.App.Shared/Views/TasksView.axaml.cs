using Avalonia;
using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class TasksView : UserControl
{
    private TasksViewModel? _vm;

    public TasksView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += OnAttachedToVisualTree;

        // Held only while on screen, as in ScbaView: the tab is realized only while selected, and a
        // view left subscribed would be kept alive by the view model that outlives it.
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Subscribe();

        // A jump from another tab (#460) reaches no live view - this one is built once its tab is
        // selected - so the scroll has to happen on arrival as well as on request.
        Reveal(this, EventArgs.Empty);
    }

    private void Subscribe()
    {
        Unsubscribe();
        _vm = DataContext as TasksViewModel;
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
        if (_vm?.SelectedTask is { } row)
        {
            TasksGrid.ScrollIntoView(row, null);
        }
    }
}
