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
    public void Ils_not_collected_by_default()
    {
        var vm = new OperatorPromptViewModel();
        Assert.False(vm.CollectsIlsNumber);
    }

    [Fact]
    public void Valid_four_digit_ils_parses()
    {
        var vm = new OperatorPromptViewModel(collectIlsNumber: true)
        {
            OperatorName = "Müller",
            IlsNumberInput = "1234",
        };
        Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.False(vm.ShowIlsError);
        Assert.Equal("1234", vm.IlsNumber!.Value);
    }

    [Fact]
    public void Non_four_digit_ils_blocks_confirm_and_shows_error()
    {
        var vm = new OperatorPromptViewModel(collectIlsNumber: true)
        {
            OperatorName = "Müller",
            IlsNumberInput = "12",
        };
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.True(vm.ShowIlsError);
        Assert.Null(vm.IlsNumber);
    }

    [Fact]
    public void Empty_ils_is_allowed()
    {
        var vm = new OperatorPromptViewModel(collectIlsNumber: true) { OperatorName = "Müller" };
        Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.False(vm.ShowIlsError);
        Assert.Null(vm.IlsNumber);
    }
}
