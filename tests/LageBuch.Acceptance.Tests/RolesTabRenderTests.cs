using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #137: labeled-field pass over FUNKTIONEN. Doubles as the PR screenshot capture, same idiom
// as ForcesTabRenderTests.
public class RolesTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static MasterDataSet MasterData() => MasterDataSet.Empty with
    {
        Roles = new[] { AnonymizedExampleData.RoleExample, "ZF" },
        RadioCallSigns = AnonymizedExampleData.RadioCallSigns,
    };

    private static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    [AvaloniaFact]
    public void Funktionen_tab_shows_a_running_assignment()
    {
        var (window, vm) = ShowWorkspace();
        var tabs = Tabs(window);
        tabs.SelectedIndex = 3; // FUNKTIONEN
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("FUNKTIONEN", ((TabItem)tabs.SelectedItem!).Header);

        vm.Roles.NewRole = AnonymizedExampleData.RoleExample;
        vm.Roles.NewPersonName = $"{AnonymizedExampleData.PersonLastName}, {AnonymizedExampleData.PersonFirstName}";
        vm.Roles.NewSection = AnonymizedExampleData.SectionExample;
        vm.Roles.NewPhone = AnonymizedExampleData.PhoneNumber;
        vm.Roles.AddRoleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Roles.Roles);

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, "roles-assignment.png"));
    }
}
