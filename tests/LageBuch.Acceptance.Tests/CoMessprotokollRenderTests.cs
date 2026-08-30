using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

public class CoMessprotokollRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
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
        frame.SavePng(Path.Combine(dir, name));
    }

    private static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    [AvaloniaFact]
    public void CoMessprotokoll_Tab_Renders()
    {
        var (window, vm, session) = ShowWorkspace();

        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        session.AddCoBuilding("Mehrfamilienhaus B", 2, 3);
        Dispatcher.UIThread.RunJobs();

        var buildingA = session.Incident.Buildings[0];
        session.RecordCoValue(buildingA.Id, 2, 1, 45);
        session.SetDwellingStatus(buildingA.Id, 2, 1, Domain.CoMeasurement.DwellingStatus.Affected);
        session.RecordCoValue(buildingA.Id, 2, 2, 120);
        session.SetDwellingStatus(buildingA.Id, 2, 2, Domain.CoMeasurement.DwellingStatus.Searched);
        session.RecordCoValue(buildingA.Id, 1, 1, 8);
        session.SetDwellingStatus(buildingA.Id, 1, 1, Domain.CoMeasurement.DwellingStatus.Searched);
        Dispatcher.UIThread.RunJobs();

        var tabs = Tabs(window);
        tabs.SelectedIndex = 6; // CO-MESSUNG
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("CO-MESSUNG", ((TabItem)tabs.SelectedItem!).Header);
        Assert.Equal(2, vm.CoMessprotokoll.BuildingOptions.Count);
        Assert.NotNull(vm.CoMessprotokoll.SelectedBuilding);
        Assert.NotEmpty(vm.CoMessprotokoll.MatrixRows);

        Capture(window, "co-messung.png");
    }
}
