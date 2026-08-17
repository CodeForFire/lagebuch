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
    public void Empty_einsatznummer_blocks_confirm_when_the_number_is_collected()
    {
        var vm = new OperatorPromptViewModel(collectIncidentNumber: true) { OperatorName = "Müller" };
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Null(vm.IncidentNumber);
    }

    [Fact]
    public void Incomplete_einsatznummer_blocks_confirm()
    {
        // Only Einsatzart filled — the classic "half-entered" case a mere non-null-Compose check
        // wouldn't catch.
        var vm = new OperatorPromptViewModel(collectIncidentNumber: true)
        {
            OperatorName = "Müller",
            EinsatzartInput = "B",
        };
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Complete_einsatznummer_required_to_confirm_when_collected()
    {
        var vm = new OperatorPromptViewModel(collectIncidentNumber: true)
        {
            OperatorName = "Müller",
            EinsatzartInput = "B",
            EinsatzDateInput = "260715",
            EinsatzNumberInput = "1297",
        };
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Einsatznummer_not_required_when_not_collected()
    {
        // The continue-editing flow (collectIncidentNumber: false) never shows these fields at
        // all, so they must not gate Confirm there.
        var vm = new OperatorPromptViewModel { OperatorName = "Müller" };
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Typing_into_any_einsatznummer_field_reevaluates_confirm()
    {
        var vm = new OperatorPromptViewModel(collectIncidentNumber: true) { OperatorName = "Müller" };
        var changed = false;
        vm.ConfirmCommand.CanExecuteChanged += (_, _) => changed = true;
        vm.EinsatzartInput = "B";
        Assert.True(changed);
    }

    [Fact]
    public void Join_flow_requires_both_host_and_pin_to_confirm()
    {
        var vm = new OperatorPromptViewModel(collectHost: true) { OperatorName = "Müller", Host = "elw-1" };
        Assert.False(vm.ConfirmCommand.CanExecute(null)); // host given, PIN still missing

        vm.Pin = "1234";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Pin_is_not_required_when_not_joining()
    {
        // The new-incident / continue-editing flows never show the PIN field, so it must not gate them.
        var vm = new OperatorPromptViewModel { OperatorName = "Müller" };
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }
}
