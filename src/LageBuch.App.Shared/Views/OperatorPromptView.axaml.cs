using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class OperatorPromptView : UserControl
{
    public OperatorPromptView()
    {
        InitializeComponent();

        // Cursor into the topmost field: GERÄT in the join flow (Host/PIN sit above Name there),
        // otherwise straight into the name field, since confirming the operator gates incident
        // start (#182 -- focus must follow the same top-to-bottom order the fields are laid out in).
        // Posted rather than called inline: when this prompt is realized as an overlay, its
        // subtree is not yet laid out at AttachedToVisualTree time, so a synchronous Focus() is
        // dropped and nothing ends up focused. Deferring one dispatcher cycle lets it land.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(FocusFirstField);

        // The join prompt's stages swap which fields are shown (#459); focus follows, posted for
        // the same reason: the newly visible field is not laid out until the next pass.
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _vm = DataContext as OperatorPromptViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private OperatorPromptViewModel? _vm;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperatorPromptViewModel.IsOperatorStage))
        {
            Dispatcher.UIThread.Post(FocusFirstField);
        }
    }

    private void FocusFirstField()
    {
        if (DataContext is OperatorPromptViewModel { IsHostStage: true } vm)
        {
            HostBox.Focus();

            // A prefilled host (from the last successful join) is selected so typing straight
            // away replaces it, instead of appending to or landing mid-string.
            if (!string.IsNullOrEmpty(vm.Host))
            {
                HostBox.SelectAll();
            }
        }
        else
        {
            OperatorNameBox.Focus();
        }
    }

    // Escape dismisses the prompt. The textboxes' KeyBindings already map Enter to confirm.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is OperatorPromptViewModel vm)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
