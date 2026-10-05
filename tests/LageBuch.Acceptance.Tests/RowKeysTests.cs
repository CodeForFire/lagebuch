using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #543: row keys in the Einsatz grids. Enter (or F2) runs a row's main action, Del removes through
// the confirm overlay -- never on an ETB row -- and Space ticks an Aufgabe off, which Ctrl+Z takes
// back. Each test selects a row and puts focus on its grid, as arrowing onto it would.
public class RowKeysTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(
        Action<LocalIncidentSession> seed)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed(session);
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

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name && c.IsEffectivelyVisible);

    private static DataGrid FocusFirstRow(Window window, string tab, string grid)
    {
        WorkspaceRenderHelper.SelectTab(window, tab);
        var dataGrid = Named<DataGrid>(window, grid);
        dataGrid.SelectedIndex = 0;
        dataGrid.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        return dataGrid;
    }

    [AvaloniaFact]
    public void Enter_on_an_etb_row_opens_the_edit_panel()
    {
        var (window, vm, _) = ShowWorkspace(s => s.AddJournalEntry(EtbDirection.Incoming, "Lage erkundet", "ILS", "ELW"));
        FocusFirstRow(window, "ETB", "EtbGrid");

        window.Press(PhysicalKey.Enter);

        Assert.True(vm.Etb.IsEditing);
    }

    [AvaloniaFact]
    public void F2_on_an_etb_row_opens_the_edit_panel_too()
    {
        var (window, vm, _) = ShowWorkspace(s => s.AddJournalEntry(EtbDirection.Incoming, "Lage erkundet", "ILS", "ELW"));
        FocusFirstRow(window, "ETB", "EtbGrid");

        window.Press(PhysicalKey.F2);

        Assert.True(vm.Etb.IsEditing);
    }

    [AvaloniaFact]
    public void Delete_on_an_etb_row_does_nothing()
    {
        var (window, vm, session) = ShowWorkspace(s => s.AddJournalEntry(EtbDirection.Incoming, "Lage erkundet", "ILS", "ELW"));
        var entries = session.Incident.Journal.Count;
        FocusFirstRow(window, "ETB", "EtbGrid");

        window.Press(PhysicalKey.Delete);

        Assert.Null(vm.PendingConfirm);
        Assert.False(vm.Etb.IsEditing);
        Assert.Equal(entries, session.Incident.Journal.Count);
    }

    [AvaloniaFact]
    public void Enter_on_a_kraefte_row_opens_the_staerke_editor()
    {
        var (window, _, _) = ShowWorkspace(s => s.AddForceUnit("FFB Wache 1", 9, "FFB 1/44/1", "Alarmiert", null));
        var grid = FocusFirstRow(window, "KRÄFTE", "ForcesGrid");
        var strength = grid.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == "Stärke korrigieren");

        window.Press(PhysicalKey.Enter);

        Assert.True(strength.Flyout?.IsOpen);
    }

    [AvaloniaFact]
    public void Delete_on_a_kraefte_row_asks_before_removing()
    {
        var (window, vm, session) = ShowWorkspace(s => s.AddForceUnit("FFB Wache 1", 9, "FFB 1/44/1", "Alarmiert", null));
        FocusFirstRow(window, "KRÄFTE", "ForcesGrid");

        window.Press(PhysicalKey.Delete);

        Assert.IsType<ConfirmDialogViewModel>(vm.PendingConfirm);
        Assert.Single(session.Incident.Forces);
        window.AssertFocused(Named<Button>(window, "CancelButton")); // a reflex Enter cancels (#538)
    }

    [AvaloniaFact]
    public void Delete_in_the_kraefte_bemerkung_edits_text_not_the_row()
    {
        var (window, vm, session) = ShowWorkspace(s => s.AddForceUnit("FFB Wache 1", 9, "FFB 1/44/1", "Alarmiert", "erste Meldung"));
        WorkspaceRenderHelper.SelectTab(window, "KRÄFTE");
        var notes = Named<DataGrid>(window, "ForcesGrid").GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.Text == "erste Meldung");
        notes.Focus(NavigationMethod.Pointer);
        notes.CaretIndex = 0;
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.Delete);

        Assert.Null(vm.PendingConfirm);
        Assert.Single(session.Incident.Forces);
        Assert.Equal("rste Meldung", notes.Text);
    }

    [AvaloniaFact]
    public void Enter_on_a_rollen_row_opens_the_transfer_panel()
    {
        var (window, vm, _) = ShowWorkspace(s => s.AssignRole("EL", "Mustermann, Max", callSign: "FFB 1/10"));
        FocusFirstRow(window, "FUNKTIONEN", "RolesGrid");

        window.Press(PhysicalKey.Enter);

        Assert.True(vm.Roles.IsTransferring);
    }

    [AvaloniaFact]
    public void Space_on_an_aufgabe_marks_it_done_and_ctrl_z_reopens_it()
    {
        var (window, vm, session) = ShowWorkspace(s => s.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30));
        vm.Tasks.Filter = TaskFilterKind.All;
        var grid = FocusFirstRow(window, "AUFGABEN", "TasksGrid");

        window.Press(PhysicalKey.Space);

        Assert.True(session.Incident.Tasks[0].IsCompleted);
        Assert.True(Named<Border>(window, "UndoNoticeBanner").IsVisible);

        grid.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Press(PhysicalKey.Z, RawInputModifiers.Control);

        Assert.False(session.Incident.Tasks[0].IsCompleted);
        Assert.Null(vm.UndoNotice);
    }

    // The default filter shows open Aufgaben only, so the ticked row leaves the grid under the
    // Lagebuchführer's hands; Ctrl+Z must still reach the workspace without clicking anywhere first.
    [AvaloniaFact]
    public void Ctrl_z_right_after_space_reopens_an_aufgabe_the_open_filter_just_hid()
    {
        var (window, vm, session) = ShowWorkspace(s => s.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30));
        FocusFirstRow(window, "AUFGABEN", "TasksGrid");

        window.Press(PhysicalKey.Space);
        Assert.True(session.Incident.Tasks[0].IsCompleted);
        Assert.Empty(vm.Tasks.Rows);

        window.Press(PhysicalKey.Z, RawInputModifiers.Control);

        Assert.False(session.Incident.Tasks[0].IsCompleted);
        Assert.Single(vm.Tasks.Rows);
    }

    [AvaloniaFact]
    public void Rueckgaengig_on_the_notice_reopens_the_aufgabe()
    {
        var (window, vm, session) = ShowWorkspace(s => s.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30));
        vm.Tasks.Filter = TaskFilterKind.All;
        FocusFirstRow(window, "AUFGABEN", "TasksGrid");
        window.Press(PhysicalKey.Space);

        var undo = Named<Button>(window, "UndoNoticeButton");
        undo.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Press(PhysicalKey.Enter);

        Assert.False(session.Incident.Tasks[0].IsCompleted);
    }

    [AvaloniaFact]
    public void Ctrl_z_in_a_text_field_does_not_undo_the_aufgabe()
    {
        var (window, vm, session) = ShowWorkspace(s => s.AddTask("Tür sichern", null, TaskImportance.Low, TaskUrgency.Low, 30));
        vm.Tasks.Filter = TaskFilterKind.All;
        FocusFirstRow(window, "AUFGABEN", "TasksGrid");
        window.Press(PhysicalKey.Space);

        var field = window.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.IsEffectivelyVisible && t.IsEffectivelyEnabled && !t.IsReadOnly);
        field.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        window.Press(PhysicalKey.Z, RawInputModifiers.Control);

        Assert.True(session.Incident.Tasks[0].IsCompleted);
        Assert.NotNull(vm.UndoNotice);
    }
}
