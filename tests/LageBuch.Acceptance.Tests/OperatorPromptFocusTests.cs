using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

public class OperatorPromptFocusTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        ChecklistTemplateAufbau = new[] { new ChecklistTemplateItem("Blaulicht aus?", false) },
        RadioCallSigns = new[] { "FFB 1/40/1", "Aich 42/1" },
    };

    private static IncidentWorkspaceViewModel ReadOnlyOpenWorkspace()
    {
        var store = new FakeStore();
        var clock = new FixedClock();
        LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        return new IncidentWorkspaceViewModel(ro, clock, new NoopTicker(), Md(), new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
    }

    [AvaloniaFact]
    public void Reopen_prompt_focuses_the_name_field()
    {
        var vm = ReadOnlyOpenWorkspace();
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1000,
            Height = 700,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.ContinueEditingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var nameBox = window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.Name == "OperatorNameBox");
        var focused = window.FocusManager?.GetFocusedElement();

        Assert.True(
            nameBox.IsFocused,
            $"NAME box not focused. FocusManager focused element = {focused?.GetType().Name ?? "null"}.");
    }
}
