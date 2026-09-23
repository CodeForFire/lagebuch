using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The view wires handlers onto each new PendingPrompt but used to leave the previous prompt's
// handlers attached. Those handlers read the view's _vm field rather than the workspace they were
// created for, so once the DataContext moved on they acted on the wrong incident. (#302)
public class WorkspacePromptHandoverTests
{
    [AvaloniaFact]
    public void Cancelling_a_previous_workspaces_prompt_leaves_the_current_one_alone()
    {
        var (view, _) = HostWorkspace(out var first);
        first.ContinueEditingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var firstPrompt = first.PendingPrompt;
        Assert.NotNull(firstPrompt);

        // Hand the view over to a second incident while the first one's prompt is still up.
        var second = ReadOnlyWorkspace();
        view.DataContext = second;
        Dispatcher.UIThread.RunJobs();
        second.ContinueEditingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(second.PendingPrompt);

        firstPrompt!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // The stale handler used to run CancelPendingPrompt on `second`, closing the prompt the
        // operator was actually looking at while the first incident kept its own.
        Assert.NotNull(second.PendingPrompt);
    }

    [AvaloniaFact]
    public void Confirming_a_previous_workspaces_prompt_does_not_unlock_the_current_one()
    {
        var (view, _) = HostWorkspace(out var first);
        first.ContinueEditingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var firstPrompt = first.PendingPrompt!;

        var second = ReadOnlyWorkspace();
        view.DataContext = second;
        Dispatcher.UIThread.RunJobs();
        Assert.True(second.IsReadOnly);

        firstPrompt.OperatorName = "Schmidt";
        firstPrompt.ConfirmCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Confirming an abandoned prompt must not take the incident now on screen out of read-only.
        Assert.True(second.IsReadOnly);
    }

    [AvaloniaFact]
    public void The_current_workspaces_own_prompt_still_works()
    {
        var (_, vm) = HostWorkspace(out _);
        vm.ContinueEditingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var prompt = vm.PendingPrompt!;
        prompt.OperatorName = "Schmidt";
        prompt.ConfirmCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsReadOnly);
        Assert.Null(vm.PendingPrompt);
    }

    [AvaloniaFact]
    public void Cancelling_the_current_workspaces_own_prompt_closes_it()
    {
        var (_, vm) = HostWorkspace(out _);
        vm.ContinueEditingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        vm.PendingPrompt!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.PendingPrompt);
        Assert.True(vm.IsReadOnly);
    }

    /// <summary>Hosts a closed (therefore continue-editable) workspace in a shown window.</summary>
    private static (IncidentWorkspaceView View, IncidentWorkspaceViewModel Vm) HostWorkspace(
        out IncidentWorkspaceViewModel vm)
    {
        vm = ReadOnlyWorkspace();
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, vm);
    }

    /// <summary>
    /// A read-only-but-still-open incident — the state that offers "weiter bearbeiten". A *closed*
    /// incident is finished and cannot be continued, so it is the wrong fixture here.
    /// </summary>
    private static IncidentWorkspaceViewModel ReadOnlyWorkspace()
    {
        var store = new FakeStore();
        var clock = new FixedClock();
        TestSession.StartNew(
            store,
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        var readOnly = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        var vm = new IncidentWorkspaceViewModel(
            readOnly,
            clock,
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        Assert.True(vm.CanContinueEditing);
        return vm;
    }
}
