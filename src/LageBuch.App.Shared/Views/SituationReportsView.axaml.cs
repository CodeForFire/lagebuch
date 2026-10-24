using Avalonia.Controls;
using Avalonia.Interactivity;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class SituationReportsView : UserControl
{
    public SituationReportsView()
    {
        InitializeComponent();

        // The editor's fields follow every keystroke for the Funkspruch preview; leaving one is
        // what writes the draft to the incident, once per correction.
        EditorPanel.AddHandler(LostFocusEvent, OnEditorLostFocus, RoutingStrategies.Bubble);
    }

    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox && DataContext is SituationReportsViewModel vm)
        {
            vm.CommitCommand.Execute(null);
        }
    }
}
