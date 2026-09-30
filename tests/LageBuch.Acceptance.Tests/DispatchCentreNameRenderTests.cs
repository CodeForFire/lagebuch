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
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #400: the Rückmeldung header names the configured Leitstelle, not a hard-coded "ILS". A long
// name must not push the countdown off a phone or widen every tile in the shared column.
// #415: an ETB entry to that Leitstelle asks whether the Rückmelde timer restarts.
public class DispatchCentreNameRenderTests
{
    private const string LongName = "Kreisleitstelle Fürstenfeldbruck";

    private static MasterDataSet MdWith(string name)
    {
        var md = WorkspaceRenderHelper.MasterData();
        return md with { Settings = md.Settings with { DispatchCentreName = name } };
    }

    private static (Window Window, IncidentWorkspaceView View, IncidentWorkspaceViewModel Vm, FixedClock Clock, ManualTicker Ticker) Show(
        string name, double width, double height = 700)
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
            session, clock, ticker, MdWith(name), new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var view = new IncidentWorkspaceView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, clock, ticker);
    }

    private static void Save(Window window, string file)
    {
        var dir = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, file));
    }

    [AvaloniaFact]
    public void The_countdown_strip_names_the_configured_leitstelle()
    {
        var (window, view, _, _, _) = Show(LongName, width: 1280);

        var bar = view.GetControl<Border>("ReminderBar");
        Assert.Contains(
            bar.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "RÜCKMELDUNG AN KREISLEITSTELLE FÜRSTENFELDBRUCK");
        Save(window, "dispatch-centre-strip-desktop.png");
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_leitstelle_keeps_the_countdown_on_a_phone()
    {
        var (window, view, _, _, _) = Show(LongName, width: 412, height: 915);

        var countdown = view.GetControl<TextBlock>("ReminderCountdownText");
        Assert.True(countdown.IsEffectivelyVisible);
        var right = countdown.TranslatePoint(new Point(countdown.Bounds.Width, 0), view);
        Assert.NotNull(right);
        Assert.True(right.Value.X <= 412, $"countdown ends at {right.Value.X}, past the 412 px screen");
        Save(window, "dispatch-centre-strip-phone.png");
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_leitstelle_does_not_widen_the_due_row_tile()
    {
        var (window, view, _, clock, ticker) = Show(LongName, width: 1280);
        clock.Now = clock.Now.AddHours(1);
        ticker.Pulse();
        Dispatcher.UIThread.RunJobs();

        var label = view.GetControl<TextBlock>("ReminderDueLabel");
        Assert.True(label.IsEffectivelyVisible);
        Assert.True(label.Bounds.Width <= 150.5, $"tile label is {label.Bounds.Width} px wide");
        Assert.Contains(
            view.GetControl<Border>("ReminderDueBar").GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == $"Rückmeldung an {LongName} fällig");
        Save(window, "dispatch-centre-due-row.png");
        window.Close();
    }

    [AvaloniaFact]
    public void An_etb_entry_to_the_leitstelle_asks_to_restart_the_timer()
    {
        var (window, view, vm, _, _) = Show("ILS", width: 1280, height: 800);
        vm.Etb.NewText = "Lagemeldung: Feuer unter Kontrolle, 2 C-Rohre im Innenangriff";
        vm.Etb.NewTo = "ILS";
        vm.Etb.AddEntryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var dialog = view.GetVisualDescendants().OfType<ConfirmDialogView>().Single();
        Assert.True(dialog.IsEffectivelyVisible);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Rückmeldung erfolgt?");
        Save(window, "dispatch-centre-reset-offer.png");
        window.Close();
    }
}
