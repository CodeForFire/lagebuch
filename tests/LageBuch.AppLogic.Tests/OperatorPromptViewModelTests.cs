using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

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
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperatorPromptViewModel.Result))
            {
                raised = true;
            }
        };
        vm.ConfirmCommand.Execute(null);
        Assert.True(raised);
        Assert.NotNull(vm.Result);
    }

    [Fact]
    public void Keyword_not_collected_by_default()
    {
        var vm = new OperatorPromptViewModel();
        Assert.False(vm.CollectsKeyword);
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
    public void Keyword_is_null_by_default_and_settable()
    {
        var vm = new OperatorPromptViewModel(collectKeyword: true) { OperatorName = "Müller" };
        Assert.Null(vm.Keyword);
        vm.Keyword = "B3P";
        Assert.Equal("B3P", vm.Keyword);
    }

    [Fact]
    public void Keyword_never_gates_confirm_set_or_unset()
    {
        // The Einsatznummer is unknown at creation (#69) -- the Stichwort that replaces it in this
        // dialog is optional too, so an incident can be started with neither.
        var vm = new OperatorPromptViewModel(collectKeyword: true) { OperatorName = "Müller" };
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        vm.Keyword = "B3P";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Keyword_not_required_when_not_collected()
    {
        // The continue-editing flow (collectKeyword: false) never shows the field at all, so it
        // must not gate Confirm there.
        var vm = new OperatorPromptViewModel { OperatorName = "Müller" };
        Assert.True(vm.ConfirmCommand.CanExecute(null));
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
