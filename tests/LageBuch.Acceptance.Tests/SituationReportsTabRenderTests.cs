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

// The ETB's LAGEMELDUNG tab (#592): reports to the Leitstelle beside the Buchstabier-Helfer. Doubles as
// the PR screenshot capture (RENDER_OUT), same idiom as InvolvedPartiesTabRenderTests. Fictional
// data only.
public class SituationReportsTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(
        double width = 1920, double height = 1032, FixedClock? clock = null, ITicker? ticker = null)
    {
        clock ??= new FixedClock();
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
        session.AddSituationReport(
            "Florian Musterstadt 11/1",
            "Blumenstraße 13",
            "Schuppen im Vollbrand",
            "Brandbekämpfung mit einem C-Hohlstrahlrohr",
            "1 Trupp unter PA im Einsatz",
            null);
        session.SendSituationReport(session.Incident.SituationReports[0].Id, "ILS");
        session.AddSituationReport(
            "Florian Musterstadt 11/1",
            "Blumenstraße 13",
            "Feuer unter Kontrolle",
            "Nachlöscharbeiten, Wärmebildkamera",
            "FF Musterstadt (HLF 1) 0/1/8/9, FF Beispieldorf (LF 2) 0/1/5/6",
            "1 RTW zur Bereitstellung");
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            ticker ?? new NoopTicker(),
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
    public void The_etb_carries_the_lagemeldung_as_its_second_tab_not_as_a_rail_entry()
    {
        var (window, _, _) = ShowWorkspace();

        Assert.DoesNotContain("LAGEMELDUNG", WorkspaceRenderHelper.RailHeaders(window));
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        Assert.Equal(
            ["EINSATZTAGEBUCH", "LAGEMELDUNG"],
            WorkspaceRenderHelper.ModuleTabStrip(window).Items.OfType<TabItem>().Select(t => t.Header));
        Assert.Equal(EtbTabsViewModel.JournalTab, WorkspaceRenderHelper.ModuleTabStrip(window).SelectedIndex);
    }

    [AvaloniaFact]
    public void The_lagemeldung_tab_shows_the_list_the_draft_its_funkspruch_and_the_helper()
    {
        var (window, vm, _) = ShowWorkspace();

        WorkspaceRenderHelper.SelectModuleTab(window, "ETB", "LAGEMELDUNG");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        vm.SituationReports.Helper.Input = "Blumenstraße 13";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, Named<ListBox>(content, "ReportList").ItemCount);
        Assert.True(Named<Button>(content, "SendReportButton").IsEffectivelyVisible);
        Assert.True(Named<Border>(content, "HelperPanel").IsEffectivelyVisible);
        Assert.False(Named<Button>(content, "OpenHelperButton").IsEffectivelyVisible);
        Assert.StartsWith("Blumenstraße 13 · Feuer unter Kontrolle", Named<SelectableTextBlock>(content, "FunkspruchText").Text, StringComparison.Ordinal);
        Assert.StartsWith("Berta – Ludwig", Named<SelectableTextBlock>(content, "SpelledText").Text, StringComparison.Ordinal);
        Capture(window, "lagemeldung-tab.png");
    }

    [AvaloniaFact]
    public void Leaving_a_field_writes_the_draft()
    {
        var (window, vm, session) = ShowWorkspace();
        WorkspaceRenderHelper.SelectModuleTab(window, "ETB", "LAGEMELDUNG");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        var situation = Named<TextBox>(content, "SituationBox");
        situation.Focus();
        Dispatcher.UIThread.RunJobs();

        vm.SituationReports.Situation = "Feuer aus";
        Named<TextBox>(content, "RequestsBox").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Feuer aus", session.Incident.SituationReports[1].Situation);
    }

    [AvaloniaFact]
    public void Sending_writes_the_etb_entry_and_locks_the_report()
    {
        var (window, vm, session) = ShowWorkspace();
        WorkspaceRenderHelper.SelectModuleTab(window, "ETB", "LAGEMELDUNG");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        var send = Named<Button>(content, "SendReportButton").Command;
        Assert.NotNull(send);
        send.Execute(null);
        Assert.NotNull(vm.PendingConfirm);
        vm.PendingConfirm.ConfirmCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, session.Incident.SituationReports[1].Number);
        Assert.Equal(EtbDirection.Outgoing, session.Incident.Journal[^1].Direction);
        Assert.False(Named<Button>(content, "SendReportButton").IsEffectivelyVisible);
        Assert.True(Named<TextBox>(content, "SituationBox").IsReadOnly);
    }

    [AvaloniaFact]
    public void The_lagemeldung_tab_renders_on_a_phone_with_the_helper_as_a_sheet()
    {
        var (window, vm, _) = ShowWorkspace(412, 915);
        WorkspaceRenderHelper.SelectModuleTab(window, "ETB", "LAGEMELDUNG");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        Assert.True(vm.SituationReports.IsNarrow);
        Assert.True(WorkspaceRenderHelper.ModuleTabStrip(window).Items.OfType<TabItem>().All(t => t.IsEffectivelyVisible));
        Assert.False(Named<Border>(content, "HelperPanel").IsEffectivelyVisible);
        Assert.True(Named<Button>(content, "OpenHelperButton").IsEffectivelyVisible);
        Capture(window, "lagemeldung-tab-phone.png");

        vm.SituationReports.SpellFieldCommand.Execute("Blumenstraße");
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<Border>(content, "HelperPanel").IsEffectivelyVisible);
        Capture(window, "lagemeldung-tab-phone-helper.png");
    }

    [AvaloniaFact]
    public void The_etb_opens_the_buchstabier_helfer_as_an_overlay()
    {
        var (window, vm, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        var open = Named<Button>(content, "OpenSpellingHelperButton").Command;
        Assert.NotNull(open);
        open.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.PendingSpellingHelper);
        vm.PendingSpellingHelper.Helper.Input = "Schäfer";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            "Schule – Ärger – Friedrich – Emil – Richard",
            Named<SelectableTextBlock>(window, "SpelledText").Text);
        Capture(window, "etb-buchstabier-helfer.png");
    }

    [AvaloniaFact]
    public void A_sent_lagemeldung_is_marked_in_the_journal_and_the_filter_shows_only_those()
    {
        var (window, vm, _) = ShowWorkspace();
        WorkspaceRenderHelper.SelectModuleTab(window, "ETB", "EINSATZTAGEBUCH");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        var marked = Assert.Single(vm.Etb.Entries, row => row.SituationReportLabel is not null);
        Assert.Equal("LM 1", marked.SituationReportLabel);
        Assert.Contains(
            content.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "LM 1" && t.IsEffectivelyVisible);

        Named<CheckBox>(content, "OnlySituationReportsCheckBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(marked, Assert.Single(vm.Etb.Entries));
        Capture(window, "etb-nur-lagemeldungen.png");
    }

    [AvaloniaFact]
    public void The_lm_marker_survives_a_correction_of_the_entry_text()
    {
        var (_, vm, session) = ShowWorkspace();
        var entryId = session.Incident.SituationReports[0].EtbEntryId!.Value;

        session.EditJournalEntry(entryId, "Lagemeldung Nr. 1 (berichtigt)");

        Assert.Equal("LM 1", vm.Etb.Entries.Single(row => row.Id == entryId).SituationReportLabel);
    }

    [AvaloniaFact]
    public void The_rueckmeldung_row_opens_the_lagemeldung_tab_on_the_newest_draft()
    {
        var clock = new FixedClock();
        var ticker = new ManualTicker();
        var (window, vm, session) = ShowWorkspace(clock: clock, ticker: ticker);
        var view = (IncidentWorkspaceView)window.Content!;
        clock.Now = clock.Now.AddHours(1);
        ticker.Pulse();
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.GetControl<Border>("ReminderDueBar").IsVisible);

        var open = view.GetControl<Button>("ReminderSituationReportButton");
        Assert.True(open.IsEffectivelyVisible);
        open.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("ETB", vm.SelectedNavItem?.Header);
        Assert.Equal(EtbTabsViewModel.SituationReportTab, vm.EtbTabs.SelectedTabIndex);
        Assert.Equal(session.Incident.SituationReports[1].Id, vm.SituationReports.SelectedReport?.Id);
        Capture(window, "rueckmeldung-lagemeldung.png");
    }
}
