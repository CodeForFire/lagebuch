using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #836: in an abgeschlossen Einsatz the fields of Beteiligte, Ausgabe, Dateien, Funktionen and
// Kräfte were disabled, and a disabled field cannot be focused, selected or right-clicked, so
// nothing in them could be copied. They are read-only now: the text can be selected and copied,
// but not changed.
public class ReadOnlyFieldCopyTests
{
    private static async Task<Window> ShowClosedAsync(Func<LocalIncidentSession, Task> seed)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await seed(session);
        session.Close();
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());

        // Closing ends every Funktion, and FUNKTIONEN shows only the current ones by default.
        vm.Roles.ShowAllRoles = true;
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TextBox Field(Window window, string tab, Func<TextBox, bool> match)
    {
        WorkspaceRenderHelper.SelectTab(window, tab);
        return WorkspaceRenderHelper.SelectedTabContent(window).GetVisualDescendants().OfType<TextBox>()
            .First(box => box.IsEffectivelyVisible && match(box));
    }

    private static TextBox Named(Window window, string tab, string automationName) =>
        Field(window, tab, box => string.Equals(AutomationProperties.GetName(box), automationName, StringComparison.Ordinal));

    // Selects the whole field, copies it, and then tries to type over it.
    private static async Task AssertCopiesButCannotBeChangedAsync(Window window, TextBox box, string expected)
    {
        Assert.True(box.IsEffectivelyEnabled, "A read-only field has to stay enabled, or its text cannot be selected.");
        Assert.True(box.IsReadOnly);
        await window.Clipboard!.SetTextAsync("vorher");
        box.Focus();
        box.SelectAll();
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.C, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        window.Type("x");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expected, await window.Clipboard!.TryGetTextAsync());
        Assert.Equal(expected, box.Text);
    }

    [AvaloniaFact]
    public async Task A_beteiligter_s_telefon_can_be_copied_in_an_abgeschlossen_einsatz()
    {
        var window = await ShowClosedAsync(s =>
        {
            s.AddInvolvedParty("Musterfrau, Erika", "0151 2345678", null);
            return Task.CompletedTask;
        });

        await AssertCopiesButCannotBeChangedAsync(window, Named(window, "BETEILIGTE", "Telefon"), "0151 2345678");
    }

    [AvaloniaFact]
    public async Task An_issued_item_can_be_copied_in_an_abgeschlossen_einsatz()
    {
        var window = await ShowClosedAsync(s =>
        {
            s.IssueItem("HRT 12", "Musterfrau, Erika", "0151 2345678", null, null);
            return Task.CompletedTask;
        });

        await AssertCopiesButCannotBeChangedAsync(window, Named(window, "AUSGABE", "Ausgegeben an"), "Musterfrau, Erika");
    }

    [AvaloniaFact]
    public async Task A_file_name_can_be_copied_in_an_abgeschlossen_einsatz()
    {
        var window = await ShowClosedAsync(s => s.AddFileAsync("einsatzstelle.jpg", "image/jpeg", [1, 2, 3]));

        var name = Field(window, "DATEIEN", box => box.DataContext is IncidentFileRow);
        await AssertCopiesButCannotBeChangedAsync(window, name, name.Text ?? string.Empty);
    }

    [AvaloniaFact]
    public async Task A_handynummer_can_be_copied_in_an_abgeschlossen_einsatz()
    {
        var window = await ShowClosedAsync(s =>
        {
            s.AssignRole("EL", "Mustermann, Max", callSign: "FFB 1/10", phone: "0151 8765432");
            return Task.CompletedTask;
        });

        var phone = Field(window, "FUNKTIONEN", box => box.DataContext is RoleAssignmentRow);
        await AssertCopiesButCannotBeChangedAsync(window, phone, "0151 8765432");
    }

    [AvaloniaFact]
    public async Task A_bemerkung_can_be_copied_in_an_abgeschlossen_einsatz()
    {
        var window = await ShowClosedAsync(s =>
        {
            s.AddForceUnit("FFB Wache 1", 9, "FFB 1/44/1", "Alarmiert", "erste Meldung");
            return Task.CompletedTask;
        });

        var notes = Field(window, "KRÄFTE", box => box.DataContext is ForceRow);
        await AssertCopiesButCannotBeChangedAsync(window, notes, "erste Meldung");
    }

    // The Meldender and Einsatzstelle are AutoCompleteBoxes, which have no IsReadOnly of their own,
    // so a sent report used to disable them. It shows read-only TextBoxes in their place now, with
    // no suggestion list to pick another name from.
    [AvaloniaFact]
    public async Task A_sent_lagemeldung_s_meldender_and_einsatzstelle_can_be_copied()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddSituationReport("Mustermann, Max", "Musterstraße 1", "Feuer aus", null, null, null);
        session.SendSituationReport(session.Incident.SituationReports[0].Id, "ILS");
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
        WorkspaceRenderHelper.SelectModuleTab(window, "ETB", "LAGEMELDUNG");
        Assert.False(vm.SituationReports.IsEditable);

        var content = WorkspaceRenderHelper.SelectedTabContent(window);
        Assert.DoesNotContain(content.GetVisualDescendants().OfType<AutoCompleteBox>(), box => box.IsEffectivelyVisible);
        TextBox Box(string name) => content.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == name);
        await AssertCopiesButCannotBeChangedAsync(window, Box("SentReporterBox"), "Mustermann, Max");
        await AssertCopiesButCannotBeChangedAsync(window, Box("SentLocationBox"), "Musterstraße 1");
    }
}
