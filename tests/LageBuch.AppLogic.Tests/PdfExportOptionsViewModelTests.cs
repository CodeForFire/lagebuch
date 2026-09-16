using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.AppLogic.Tests;

public class PdfExportOptionsViewModelTests
{
    private static PdfExportOptionsViewModel NewVm(Func<IncidentPdfSections, Task>? onExport = null) =>
        new(onExport ?? (_ => Task.CompletedTask));

    [Fact]
    public void All_eight_sections_are_selected_by_default()
    {
        var vm = NewVm();

        Assert.Equal(8, vm.Items.Count);
        Assert.All(vm.Items, i => Assert.True(i.IsSelected));
    }

    [Fact]
    public void CanExport_is_false_once_every_section_is_unchecked()
    {
        var vm = NewVm();
        Assert.True(vm.ExportCommand.CanExecute(null));

        foreach (var item in vm.Items)
        {
            item.IsSelected = false;
        }

        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_closes_without_invoking_the_export_callback()
    {
        var invoked = false;
        var vm = NewVm(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        var closed = false;
        vm.Closed += (_, _) => closed = true;

        vm.CancelCommand.Execute(null);
        await Task.Yield();

        Assert.False(invoked);
        Assert.True(closed);
    }

    [Fact]
    public async Task Export_aggregates_only_checked_sections_and_closes_after_the_callback_completes()
    {
        IncidentPdfSections? received = null;
        var vm = NewVm(sections =>
        {
            received = sections;
            return Task.CompletedTask;
        });
        var etb = vm.Items.Single(i => i.Section == IncidentPdfSections.Etb);
        etb.IsSelected = false;
        var closed = false;
        vm.Closed += (_, _) => closed = true;

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal(IncidentPdfSections.All & ~IncidentPdfSections.Etb, received);
        Assert.True(closed);
    }

    [Fact]
    public async Task IsBusy_is_true_only_while_the_export_callback_is_running()
    {
        var tcs = new TaskCompletionSource();
        var vm = NewVm(_ => tcs.Task);

        var exportTask = vm.ExportCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CancelCommand.CanExecute(null));

        tcs.SetResult();
        await exportTask;

        Assert.False(vm.IsBusy);
    }

    // Defense in depth: CommunityToolkit's generated RelayCommand.Execute() does NOT self-guard
    // on CanExecute -- a caller that bypasses the binding (e.g. a view's own KeyDown handler
    // calling .Execute(null) directly on Escape) could otherwise force-close the dialog while its
    // export is still running in the background, orphaning that Task.
    [Fact]
    public async Task Cancel_is_a_no_op_while_busy_even_if_invoked_directly()
    {
        var tcs = new TaskCompletionSource();
        var vm = NewVm(_ => tcs.Task);
        var exportTask = vm.ExportCommand.ExecuteAsync(null);
        var closed = false;
        vm.Closed += (_, _) => closed = true;

        vm.CancelCommand.Execute(null); // bypasses CanExecute, same as a raw ICommand.Execute call

        Assert.False(closed);

        tcs.SetResult();
        await exportTask;
    }
}
