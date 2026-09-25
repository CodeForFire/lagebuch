using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The header Meldungen: every notification shares one anatomy (a tile naming the module, the
// message, the actions), the tiles line up in one column, and a countdown that is only running
// sits on a quiet strip until it falls due and becomes a row of its own.
public class MeldungenHeaderTests
{
    [AvaloniaFact]
    public void Ils_reminder_moves_from_the_strip_to_a_row_when_due()
    {
        var (window, view, clock, ticker) = ShowRunning(width: 1280);
        var readout = view.GetControl<Border>("ReminderBar");
        var row = view.GetControl<Border>("ReminderDueBar");
        Assert.True(readout.IsVisible);
        Assert.False(row.IsVisible);

        clock.Now = clock.Now.AddHours(1);
        ticker.Pulse();
        Dispatcher.UIThread.RunJobs();

        Assert.False(readout.IsVisible);
        Assert.True(row.IsVisible);
        Assert.True(view.GetControl<Button>("ReminderAckButton").IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Druckabfrage_moves_from_the_strip_to_a_row_when_due()
    {
        var (window, view, clock, ticker) = ShowRunning(width: 1280);
        var readout = view.GetControl<Border>("ScbaControlReadout");
        var row = view.GetControl<Border>("ScbaControlBar");
        Assert.True(readout.IsVisible);
        Assert.False(row.IsVisible);

        clock.Now = clock.Now.AddMinutes(25); // past the Druckabfrage, short of the Rückzugsalarm
        ticker.Pulse();
        Dispatcher.UIThread.RunJobs();

        Assert.False(readout.IsVisible);
        Assert.True(row.IsVisible);
        window.Close();
    }

    // Row Grids do not share Auto widths on their own; without the SharedSizeGroup each tile would
    // be as wide as its own label and the messages would start at ragged x positions.
    [AvaloniaFact]
    public void Meldung_tiles_share_one_width()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(withOverdueTask: true);
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tiles = view.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("meldung-tile") && b.IsEffectivelyVisible)
            .ToList();

        Assert.True(tiles.Count >= 3, $"expected the alarm, Druckabfrage and Aufgabe rows, found {tiles.Count} tiles");
        Assert.All(tiles, t => Assert.Equal(tiles[0].Bounds.Width, t.Bounds.Width, precision: 0));
        window.Close();
    }

    // On a phone the tile column would leave the message a few letters: the tile keeps its icon
    // and drops its label, and the message gets the room.
    [AvaloniaFact]
    public void Narrow_header_keeps_the_tile_icon_and_drops_its_label()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(withOverdueTask: true);
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = 412, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var alarm = view.GetControl<Border>("ScbaAlarmBar");
        var label = alarm.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("meldung-label"));
        var icon = alarm.GetVisualDescendants().OfType<PathIcon>().Single(p => p.Classes.Contains("meldung-icon"));
        var text = view.GetControl<TextBlock>("ScbaAlarmText");

        Assert.False(label.IsVisible);
        Assert.True(icon.Bounds.Width > 0);
        Assert.True(text.Bounds.Width > alarm.Bounds.Width / 4, $"the alarm text got {text.Bounds.Width:F0}px of {alarm.Bounds.Width:F0}px");
        window.Close();
    }

    /// <summary>A live incident with one Trupp under air, three minutes in: both countdowns run.</summary>
    private static (Window Window, IncidentWorkspaceView View, FixedClock Clock, ManualTicker Ticker) ShowRunning(double width)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            new[] { ("A?", false) },
            Array.Empty<(string, bool)>());
        var ticker = new ManualTicker();
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            ticker,
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewTruppfuehrer = AnonymizedExampleData.OperatorSurname;
        vm.Scba.NewTruppmann = AnonymizedExampleData.OperatorSurnameAlt;
        vm.Scba.AddTruppCommand.Execute(null);
        vm.Scba.Trupps[^1].StartCommand.Execute(null);
        clock.Now = clock.Now.AddMinutes(3);
        ticker.Pulse();

        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, clock, ticker);
    }
}
