using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Feuerwehr.App.Shared.Views;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;

namespace Feuerwehr.Acceptance.Tests;

// Issue #62: the new "Dateien" tab. Doubles as the PR before/after screenshot capture
// (RENDER_OUT), same idiom as SharePanelRenderTests.
public class FilesTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", new[] { "Blaulicht aus?" });
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
        frame.Save(Path.Combine(dir, name));
    }

    private static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    [AvaloniaFact]
    public void Workspace_renders_six_tabs_before_dateien_is_opened()
    {
        var (window, _, _) = ShowWorkspace();
        var tabs = Tabs(window);

        Assert.Equal(6, tabs.Items.Count);
        Capture(window, "files-before.png");
    }

    [AvaloniaFact]
    public void Selecting_the_dateien_tab_shows_an_attached_file()
    {
        var (window, vm, session) = ShowWorkspace();
        session.Incident.AddFile(new FixedClock(), session.Operator!, "einsatzstelle.jpg", "image/jpeg", 1_200_000);
        vm.Files.Sync();
        Dispatcher.UIThread.RunJobs();

        var tabs = Tabs(window);
        tabs.SelectedIndex = 5; // DATEIEN
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("DATEIEN", ((TabItem)tabs.SelectedItem!).Header);
        Assert.Single(vm.Files.Files);
        Capture(window, "files-after.png");
    }
}
