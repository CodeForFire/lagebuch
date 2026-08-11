using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Feuerwehr.App.Shared.Views;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.Acceptance.Tests;

public class OperatorPromptFocusTests
{
    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        ChecklistTemplate = new[] { "Blaulicht aus?" },
        RadioCallSigns = new[] { "FFB 1/40/1", "Aich 42/1" },
    };

    private static IncidentWorkspaceViewModel ReadOnlyOpenWorkspace()
    {
        var store = new FakeStore();
        var clock = new FixedClock();
        IncidentSession.StartNew(store, clock, new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident", new[] { "Blaulicht aus?" });
        var ro = IncidentSession.OpenReadOnly(store, "/x.fwincident");
        return new IncidentWorkspaceViewModel(ro, clock, new NoopTicker(), Md(), new FakeDialogs(), new NoopAlarmService());
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

        Assert.True(nameBox.IsFocused,
            $"NAME box not focused. FocusManager focused element = {focused?.GetType().Name ?? "null"}.");
    }
}
