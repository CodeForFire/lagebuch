using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.AppLogic.Tests;

public class OperatorPromptViewModelTests
{
    [Fact]
    public void Confirm_disabled_until_name_entered()
    {
        var vm = new OperatorPromptViewModel();
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        vm.OperatorName = "Müller";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Confirm_builds_session_operator_with_callsign()
    {
        var vm = new OperatorPromptViewModel { OperatorName = "Müller", OperatorCallSign = "FFB 12/1" };
        vm.ConfirmCommand.Execute(null);
        Assert.NotNull(vm.Result);
        Assert.Equal("Müller (FFB 12/1)", vm.Result!.Display);
    }

    [Fact]
    public void Confirm_raises_property_changed_for_Result()
    {
        var vm = new OperatorPromptViewModel { OperatorName = "Müller" };
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OperatorPromptViewModel.Result)) raised = true; };
        vm.ConfirmCommand.Execute(null);
        Assert.True(raised);
        Assert.NotNull(vm.Result);
    }

    [Fact]
    public void Incident_number_not_collected_by_default()
    {
        var vm = new OperatorPromptViewModel();
        Assert.False(vm.CollectsIncidentNumber);
    }

    [Fact]
    public void EinsatzartOptions_default_to_empty_and_expose_the_supplied_list()
    {
        Assert.Empty(new OperatorPromptViewModel().EinsatzartOptions);
        var options = new[] { "B", "THL" };
        Assert.Equal(options, new OperatorPromptViewModel(einsatzartOptions: options).EinsatzartOptions);
    }

    [Fact]
    public void CallSignOptions_default_to_empty()
    {
        var vm = new OperatorPromptViewModel();
        Assert.Empty(vm.CallSignOptions);
    }

    [Fact]
    public void CallSignOptions_expose_the_supplied_list()
    {
        var options = new[] { "FFB 1/40/1", "Aich 42/1" };
        var vm = new OperatorPromptViewModel(callSignOptions: options);
        Assert.Equal(options, vm.CallSignOptions);
    }

    [Fact]
    public void Call_sign_stays_free_text_even_when_not_in_the_options()
    {
        // The dropdown is a hint, not a closed set: an off-list Funkrufname must still confirm.
        var vm = new OperatorPromptViewModel(callSignOptions: new[] { "FFB 1/40/1" })
        {
            OperatorName = "Müller",
            OperatorCallSign = "Land 9",
        };
        vm.ConfirmCommand.Execute(null);
        Assert.Equal("Müller (Land 9)", vm.Result!.Display);
    }

    [Fact]
    public void Composes_the_complete_einsatznummer_from_its_parts()
    {
        var vm = new OperatorPromptViewModel(collectIncidentNumber: true)
        {
            OperatorName = "Müller",
            EinsatzartInput = "B",
            EinsatzDateInput = "260715",
            EinsatzNumberInput = "1297",
        };
        Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.Equal("B 1.2 260715 1297", vm.IncidentNumber!.Value);
    }

    [Fact]
    public void Einsatzart_is_free_text_even_when_not_in_the_options()
    {
        // The dropdown is a hint, not a closed set: an off-list Einsatzart must still compose.
        var vm = new OperatorPromptViewModel(collectIncidentNumber: true, einsatzartOptions: new[] { "B" })
        {
            OperatorName = "Müller",
            EinsatzartInput = "XYZ",
            EinsatzNumberInput = "5",
        };
        Assert.Equal("XYZ 1.2 5", vm.IncidentNumber!.Value);
    }

    [Fact]
    public void Empty_einsatznummer_is_allowed_and_confirms()
    {
        var vm = new OperatorPromptViewModel(collectIncidentNumber: true) { OperatorName = "Müller" };
        Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.Null(vm.IncidentNumber);
    }
}
