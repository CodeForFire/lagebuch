using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;

namespace LageBuch.Acceptance.Tests;

// The ETB edit panel carries VON, AN, ABSCHNITT and EINTRAG on one row above the narrow
// breakpoint; SPEICHERN and ABBRECHEN must stay inside it on a 1280px ELW screen with the
// nav rail. Doubles as the PR screenshot capture (RENDER_OUT).
public class EtbEditPanelRenderTests
{
    [AvaloniaFact]
    public void Edit_panel_buttons_stay_inside_the_panel_at_1280()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Nord");
        session.AddJournalEntry(EtbDirection.Incoming, "Lage: Rauch aus dem 1. OG", "Florian Musterstadt 1/44/1", "ELW 1");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new ManualTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ETB");

        vm.Etb.Entries.Single(r => r.Text.StartsWith("Lage:", StringComparison.Ordinal)).BeginEditCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var panel = Named<Border>(window, "EditPanel");
        Assert.True(panel.IsEffectivelyVisible);
        Capture(window, "etb-bearbeiten-1280.png");
        var panelRight = Assert.NotNull(panel.TranslatePoint(new Point(panel.Bounds.Width - panel.Padding.Right, 0), window)).X;
        foreach (var name in new[] { "SaveEditButton", "CancelEditButton" })
        {
            var button = Named<Button>(window, name);
            var right = Assert.NotNull(button.TranslatePoint(new Point(button.Bounds.Width, 0), window)).X;
            Assert.True(right <= panelRight, $"{name} ends at x={right:0.#}, the panel at x={panelRight:0.#}");
        }
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        Thread.Sleep(250);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.SavePng(Path.Join(dir, name));
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
}
