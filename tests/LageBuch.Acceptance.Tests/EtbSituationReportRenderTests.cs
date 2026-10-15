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
using LageBuch.Domain.Etb;

namespace LageBuch.Acceptance.Tests;

// The Lagemeldung beside the ETB (#592). Doubles as the PR screenshot capture (RENDER_OUT), same
// idiom as InvolvedPartiesTabRenderTests. Fictional data only.
public class EtbSituationReportRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(
        double width = 1920, double height = 1032, bool withDraft = true)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Muster", "Florian Musterstadt 11/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.SetAddress("Blumenstraße 13", "Musterstadt-Nord");
        session.AddForceUnit("FF Musterstadt", 9, callSign: "HLF 1", officerCount: 1, scbaCount: 4);
        session.AddForceUnit("FF Beispieldorf", 6, callSign: "LF 2", officerCount: 1);
        session.AddJournalEntry(EtbDirection.Incoming, "Trupp 1 unter PA zur Brandbekämpfung vor", "HLF 1", "ELW 1");
        session.AddSituationReport(
            "Florian Musterstadt 11/1",
            "Blumenstraße 13",
            "Schuppen im Vollbrand",
            "Brandbekämpfung mit einem C-Hohlstrahlrohr",
            "1 Trupp unter PA im Einsatz",
            null);
        session.SendSituationReport(session.Incident.SituationReports[0].Id, "ILS");
        session.AddJournalEntry(EtbDirection.Incoming, "Wasserversorgung über Hydrant gesichert", "LF 2", "ELW 1");
        if (withDraft)
        {
            session.AddSituationReport(
                "Florian Musterstadt 11/1",
                "Blumenstraße 13",
                "Feuer unter Kontrolle",
                "Nachlöscharbeiten, Wärmebildkamera",
                "FF Musterstadt (HLF 1) 0/1/8/9, FF Beispieldorf (LF 2) 0/1/5/6",
                "1 RTW zur Bereitstellung");
        }

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
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        vm.Etb.HideSystemEntries = true;
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

    private static void Open(Window window)
    {
        var open = Named<Button>(window, "OpenSituationReportButton").Command;
        Assert.NotNull(open);
        open.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void The_rail_has_no_lagemeldung_of_its_own()
    {
        var (window, _, _) = ShowWorkspace();

        Assert.DoesNotContain("LAGEMELDUNG", WorkspaceRenderHelper.RailHeaders(window));
    }

    [AvaloniaFact]
    public void The_panel_opens_beside_the_journal_with_the_draft_and_its_funkspruch()
    {
        var (window, vm, _) = ShowWorkspace();
        Assert.False(Named<Border>(window, "SituationReportPanel").IsEffectivelyVisible);

        Open(window);

        Assert.True(Named<Border>(window, "SituationReportPanel").IsEffectivelyVisible);
        Assert.True(Named<Border>(window, "EtbGridPanel").IsEffectivelyVisible);
        Assert.Equal("Feuer unter Kontrolle", vm.Etb.SituationReport.Situation);
        Assert.StartsWith(
            "Berta – Ludwig",
            vm.Etb.SituationReport.FunkspruchLines[0].Spelling,
            StringComparison.Ordinal);
        Capture(window, "etb-lagemeldung.png");
    }

    [AvaloniaFact]
    public void Leaving_a_field_writes_the_draft()
    {
        var (window, vm, session) = ShowWorkspace();
        Open(window);
        Named<TextBox>(window, "SrSituationBox").Focus();
        Dispatcher.UIThread.RunJobs();

        vm.Etb.SituationReport.Situation = "Feuer aus";
        Named<TextBox>(window, "SrRequestsBox").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Feuer aus", session.Incident.SituationReports[1].Situation);
    }

    [AvaloniaFact]
    public void Sending_closes_the_panel_and_the_report_is_an_etb_row_with_its_number()
    {
        var (window, vm, session) = ShowWorkspace();
        Open(window);

        var send = Named<Button>(window, "SendSituationReportButton").Command;
        Assert.NotNull(send);
        send.Execute(null);
        Assert.NotNull(vm.PendingConfirm);
        vm.PendingConfirm.ConfirmCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Named<Border>(window, "SituationReportPanel").IsEffectivelyVisible);
        Assert.Equal(2, session.Incident.SituationReports[1].Number);
        Assert.Equal("LM 2", vm.Etb.Entries[0].SituationReportLabel);
        Capture(window, "etb-lagemeldung-gesendet.png");
    }

    [AvaloniaFact]
    public void Nur_lagemeldungen_narrows_the_journal_to_the_sent_reports()
    {
        var (window, vm, _) = ShowWorkspace();

        Named<CheckBox>(window, "OnlySituationReportsCheckBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("LM 1", Assert.Single(vm.Etb.Entries).SituationReportLabel);
    }

    [AvaloniaFact]
    public void Ctrl_l_and_the_due_rueckmeldung_both_open_it()
    {
        var (window, vm, _) = ShowWorkspace(withDraft: false);
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");

        Assert.True(vm.TryRunShortcut(new KeyChord(ShortcutKey.L, Ctrl: true)));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("ETB", vm.SelectedNavItem?.Header);
        Assert.True(vm.Etb.SituationReport.IsOpen);

        vm.Etb.SituationReport.CloseCommand.Execute(null);
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        vm.OpenSituationReportCommand.Execute(null);

        Assert.Equal("ETB", vm.SelectedNavItem?.Header);
        Assert.True(vm.Etb.SituationReport.IsOpen);
    }

    [AvaloniaFact]
    public void On_a_phone_the_lagemeldung_takes_the_screen_and_gives_it_back()
    {
        var (window, vm, _) = ShowWorkspace(412, 915);
        Assert.True(vm.Etb.IsNarrow);

        Open(window);

        Assert.True(Named<Border>(window, "SituationReportPanel").IsEffectivelyVisible);
        Assert.False(Named<Panel>(window, "EtbJournal").IsEffectivelyVisible);
        Capture(window, "etb-lagemeldung-phone.png");

        vm.Etb.SituationReport.CloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<Panel>(window, "EtbJournal").IsEffectivelyVisible);
    }
}
