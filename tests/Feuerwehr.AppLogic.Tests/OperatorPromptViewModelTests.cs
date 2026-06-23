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
}
