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

// Issue #62: the new "Dateien" tab. Doubles as the PR before/after screenshot capture
// (RENDER_OUT), same idiom as SharePanelRenderTests.
public class FilesTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident",
            new[] { ("Blaulicht aus?", false) }, Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(session, new FixedClock(), new NoopTicker(), WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
            return;
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, name));
    }

    private static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    [AvaloniaFact]
    public void Workspace_renders_eight_tabs_before_dateien_is_opened()
    {
        var (window, _, _) = ShowWorkspace();
        var tabs = Tabs(window);

        Assert.Equal(9, tabs.Items.Count);
        Capture(window, "files-before.png");
    }

    [AvaloniaFact]
    public void Selecting_the_dateien_tab_shows_an_attached_file()
    {
        var (window, vm, session) = ShowWorkspace();
        var file = session.Incident.AddFile(new FixedClock(), session.Operator!, "einsatzstelle.jpg", "image/jpeg", 1_200_000);
        // A renamed display name (independent of the original file name) is the point of the
        // screenshot below — the editable Name field.
        session.Incident.RenameFile(file.Id, "Küchenbrand, Erdgeschoss");
        vm.Files.Sync();
        Dispatcher.UIThread.RunJobs();

        var tabs = Tabs(window);
        tabs.SelectedIndex = 6; // DATEIEN
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("DATEIEN", ((TabItem)tabs.SelectedItem!).Header);
        Assert.Single(vm.Files.Files);
        Assert.Equal("Küchenbrand, Erdgeschoss", vm.Files.Files[0].DisplayName);
        Capture(window, "files-after.png");
    }
}
