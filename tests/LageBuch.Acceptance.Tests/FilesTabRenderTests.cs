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

// Issue #62: the new "Dateien" tab. Doubles as the PR before/after screenshot capture
// (RENDER_OUT), same idiom as SharePanelRenderTests.
public class FilesTabRenderTests
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
    public void Workspace_renders_eight_tabs_before_dateien_is_opened()
    {
        var (window, _, _) = ShowWorkspace();
        var tabs = Tabs(window);

        Assert.Equal(10, tabs.Items.Count);
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
        tabs.SelectedIndex = 7; // DATEIEN
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("DATEIEN", ((TabItem)tabs.SelectedItem!).Header);
        Assert.Single(vm.Files.Files);
        Assert.Equal("Küchenbrand, Erdgeschoss", vm.Files.Files[0].DisplayName);
        Capture(window, "files-after.png");
    }

    // #262 UX follow-up: deleting an attachment goes through the same ConfirmDialogView overlay as
    // Kraft-unit removal — this finding is specifically about the *existence* of a confirm step, so
    // the middle screenshot (the overlay open, nothing removed yet) is the point of this test.
    [AvaloniaFact]
    public void Removing_a_file_confirms_then_deletes_the_row_and_logs_the_etb()
    {
        var (window, vm, session) = ShowWorkspace();
        session.Incident.AddFile(new FixedClock(), session.Operator!, "einsatzstelle.jpg", "image/jpeg", 1_200_000);
        vm.Files.Sync();
        Dispatcher.UIThread.RunJobs();

        var tabs = Tabs(window);
        tabs.SelectedIndex = 7; // DATEIEN
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(vm.Files.Files);
        Capture(window, "files-remove-before.png");

        row.RemoveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // The overlay opened — nothing removed yet.
        Assert.NotNull(vm.PendingConfirm);
        Assert.Single(vm.Files.Files);
        Capture(window, "files-remove-confirm.png");

        vm.PendingConfirm!.ConfirmCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.PendingConfirm);
        Assert.Empty(vm.Files.Files);
        Assert.Contains(session.Incident.Journal, e => e.Text == "Datei entfernt: einsatzstelle.jpg");
        Capture(window, "files-remove-after.png");
    }

    // Issue #197: the ⚠ error banner glyph used to be Unicode text on a TextBlock, which defaults
    // to Barlow -- a font that doesn't carry it. Now a PathIcon like the ETB grid's row actions,
    // so the icon is drawn from bundled vector data.
    [AvaloniaFact]
    public void Error_banner_renders_a_laid_out_icon()
    {
        var (window, vm, _) = ShowWorkspace();
        var tabs = Tabs(window);
        tabs.SelectedIndex = 7; // DATEIEN
        vm.Files.ErrorMessage = "Fehler beim Hochladen.";
        Dispatcher.UIThread.RunJobs();

        var banner = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ErrorBanner");
        var icon = Assert.Single(banner.GetVisualDescendants().OfType<PathIcon>());
        Assert.True(icon.Bounds.Width > 0, "the error banner icon has zero width -- nothing is drawn");
    }
}
