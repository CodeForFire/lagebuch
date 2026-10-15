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

// The LAGEMELDUNG tab (#592): reports to the Leitstelle beside the Buchstabier-Helfer. Doubles as
// the PR screenshot capture (RENDER_OUT), same idiom as InvolvedPartiesTabRenderTests. Fictional
// data only.
public class SituationReportsTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(
        double width = 1920, double height = 1032)
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
            new NoopTicker(),
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
    public void The_rail_carries_a_lagemeldung_tab_right_after_the_etb()
    {
        var (window, _, _) = ShowWorkspace();
        var headers = WorkspaceRenderHelper.RailHeaders(window).ToList();

        Assert.Equal(headers.IndexOf("ETB") + 1, headers.IndexOf("LAGEMELDUNG"));
    }

    [AvaloniaFact]
    public void The_lagemeldung_tab_shows_the_list_the_draft_its_funkspruch_and_the_helper()
    {
        var (window, vm, _) = ShowWorkspace();

        WorkspaceRenderHelper.SelectTab(window, "LAGEMELDUNG");
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
        WorkspaceRenderHelper.SelectTab(window, "LAGEMELDUNG");
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
        WorkspaceRenderHelper.SelectTab(window, "LAGEMELDUNG");
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
        WorkspaceRenderHelper.SelectTab(window, "LAGEMELDUNG");
        var content = WorkspaceRenderHelper.SelectedTabContent(window);

        Assert.True(vm.SituationReports.IsNarrow);
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
}
