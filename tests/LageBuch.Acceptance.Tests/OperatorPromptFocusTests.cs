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
        ChecklistTemplates = ChecklistTemplate.AufbauAbbau(new[] { new ChecklistTemplateItem("Blaulicht aus?", false) }, null),
        Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9), new Vehicle("Aich", "Aich 42/1", 6) },
    };

    private static IncidentWorkspaceViewModel ReadOnlyOpenWorkspace()
    {
        var store = new FakeStore();
        var clock = new FixedClock();
        TestSession.StartNew(
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

        var nameBox = window.GetVisualDescendants().OfType<AutoCompleteBox>()
            .Single(t => t.Name == "OperatorNameBox");
        var focused = window.FocusManager?.GetFocusedElement();

        // An AutoCompleteBox hands focus to the TextBox in its template.
        Assert.True(
            nameBox.IsKeyboardFocusWithin,
            $"NAME box not focused. FocusManager focused element = {focused?.GetType().Name ?? "null"}.");
    }

    // #469: the handover lives in the header of an editable workspace and opens the same prompt,
    // titled for the handover and ready for the successor's name.
    [AvaloniaFact]
    public void Handover_button_opens_the_prompt_focused_on_the_name_field()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(session, clock, new NoopTicker(), Md(), new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = 1000,
            Height = 700,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ChangeOperatorButton");
        Assert.True(button.IsEffectivelyVisible);
        button.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var title = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PromptTitle");
        Assert.Equal("Lagebuchführer wechseln", title.Text);
        var nameBox = window.GetVisualDescendants().OfType<AutoCompleteBox>()
            .Single(t => t.Name == "OperatorNameBox");
        Assert.True(nameBox.IsKeyboardFocusWithin, "NAME box not focused in the handover prompt.");
    }

    // #182: the join dialog's fields read top to bottom as GERÄT, PIN, then NAME -- initial focus
    // must land on the topmost one (GERÄT), not skip past it to NAME as it did before this fix.
    [AvaloniaFact]
    public void Join_prompt_focuses_the_host_field_not_the_name_field()
    {
        var vm = new OperatorPromptViewModel(collectHost: true, callSignOptions: new[] { "FFB 1/40/1" });
        var window = new Window { Content = new OperatorPromptView { DataContext = vm }, Width = 640, Height = 560 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var hostBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "HostBox");
        var focused = window.FocusManager?.GetFocusedElement();

        Assert.True(
            hostBox.IsFocused,
            $"GERÄT box not focused. FocusManager focused element = {focused?.GetType().Name ?? "null"}.");
    }
}
