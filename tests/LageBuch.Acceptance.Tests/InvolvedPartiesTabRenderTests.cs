using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// The BETEILIGTE tab: the Einsatz's own address book. Doubles as the PR screenshot capture
// (RENDER_OUT), same idiom as TasksTabRenderTests. Fictional people only.
public class InvolvedPartiesTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(
        double width = 1920, double height = 1032)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Muster", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddInvolvedParty("Beispiel, Erika", "0171 0000001", "Hauseigentümerin, Schlüssel an EL übergeben");
        session.AddInvolvedParty("Mustermann, Max", "0171 0000002", "Halter des Pkw in der Einfahrt");
        session.AddInvolvedParty("POK Musterfrau", null, "Polizei vor Ort, Streife 12/3");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    private static T Named<T>(Visual root, string name)
        where T : Visual =>
        root.GetVisualDescendants().OfType<T>().First(v => v.Name == name);

    [AvaloniaFact]
    public void The_rail_carries_a_beteiligte_tab_beside_kraefte()
    {
        var (window, _, _) = ShowWorkspace();
        var headers = WorkspaceRenderHelper.RailHeaders(window).ToList();

        Assert.Equal(headers.IndexOf("KRÄFTE") + 1, headers.IndexOf("BETEILIGTE"));
    }

    [AvaloniaFact]
    public void The_beteiligte_tab_lists_every_entry_with_its_form()
    {
        var (window, _, _) = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "BETEILIGTE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        Assert.Equal(3, Named<ItemsControl>(content, "InvolvedPartiesList").ItemCount);
        Assert.True(Named<Button>(content, "AddInvolvedPartyButton").IsEffectivelyVisible);
        Assert.False(Named<TextBlock>(content, "EmptyText").IsEffectivelyVisible);
        Capture(window, "beteiligte-tab.png");
    }

    [AvaloniaFact]
    public void Adding_through_the_form_puts_the_entry_in_the_list()
    {
        var (window, vm, session) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "BETEILIGTE");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        vm.InvolvedParties.NewName = "Beispiel, Lena";
        Named<Button>(content, "AddInvolvedPartyButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, session.Incident.InvolvedParties.Count);
        Assert.Equal(4, Named<ItemsControl>(content, "InvolvedPartiesList").ItemCount);
    }

    [AvaloniaFact]
    public void The_beteiligte_tab_renders_on_a_phone()
    {
        var (window, vm, _) = ShowWorkspace(412, 915);
        WorkspaceRenderHelper.SelectTab(window, "BETEILIGTE");

        Assert.True(vm.InvolvedParties.IsNarrow);
        Capture(window, "beteiligte-tab-phone.png");
    }
}
