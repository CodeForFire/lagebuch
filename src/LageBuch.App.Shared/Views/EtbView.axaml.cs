using Avalonia.Controls;
using Avalonia.Interactivity;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class EtbView : UserControl
{
    // The form's first field, VON (#540), takes focus only when the user asked for the module
    // (#542): see WorkspaceFocus and the dock's EntryForm.FirstField.
    public EtbView()
    {
        InitializeComponent();

        // The Lagemeldung's fields follow every keystroke for the Funkspruch; leaving one is what
        // writes the draft to the incident, once per correction (#592).
        SituationReportPanel.AddHandler(LostFocusEvent, OnSituationReportLostFocus, RoutingStrategies.Bubble);
    }

    private void OnSituationReportLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox && DataContext is EtbViewModel vm)
        {
            vm.SituationReport.CommitCommand.Execute(null);
        }
    }
}
