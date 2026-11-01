using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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

// #836: Strg+C on a grid copied the whole selected row, quoted, and a row of "" where every column
// is a template column (Aufgaben, Atemschutz). Strg+C now copies the cell that was clicked, and a
// right-click on a cell offers that cell and the whole row.
public class GridCopyTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace(Action<LocalIncidentSession> seed)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
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
        return (window, vm);
    }

    private static DataGrid Grid(Window window, string tab, string name)
    {
        WorkspaceRenderHelper.SelectTab(window, tab);
        return window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == name && g.IsEffectivelyVisible);
    }

    private static DataGridCell Cell(DataGrid grid, string header) =>
        grid.GetVisualDescendants().OfType<DataGridCell>()
            .First(c => c.IsEffectivelyVisible && DataGridColumn.GetColumnContainingElement(c) is { Header: string h }
                && string.Equals(h, header, StringComparison.Ordinal));

    private static Point PointOf(Window window, DataGridCell cell) =>
        cell.TranslatePoint(new Point(4, 4), window) ?? throw new InvalidOperationException("Cell is not in the window.");

    private static void Click(Window window, DataGridCell cell)
    {
        var point = PointOf(window, cell);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<string?> CopyClickedCellAsync(Window window, DataGrid grid, string header)
    {
        await window.Clipboard!.SetTextAsync("vorher");
        Click(window, Cell(grid, header));
        window.Press(PhysicalKey.C, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        return await window.Clipboard!.TryGetTextAsync();
    }

    private static async Task<string?> CopyFromMenuAsync(Window window, DataGrid grid, string header, string item)
    {
        await window.Clipboard!.SetTextAsync("vorher");
        var point = PointOf(window, Cell(grid, header));
        window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        var menu = Assert.IsType<ContextMenu>(grid.ContextMenu);
        Assert.True(menu.IsOpen, "Expected a right-click on a cell to open the copy menu.");
        var menuItem = menu.Items.OfType<MenuItem>().Single(m => string.Equals(m.Header as string, item, StringComparison.Ordinal));
        menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        return await window.Clipboard!.TryGetTextAsync();
    }

    [AvaloniaFact]
    public async Task Strg_c_on_an_etb_row_copies_the_clicked_cell_not_the_row()
    {
        var (window, _) = ShowWorkspace(s => s.AddJournalEntry(EtbDirection.Incoming, "Lage erkundet", "ILS", "ELW"));
        var grid = Grid(window, "ETB", "EtbGrid");

        Assert.Equal("ILS", await CopyClickedCellAsync(window, grid, "VON"));
        Assert.Equal("Lage erkundet", await CopyClickedCellAsync(window, grid, "EINTRAG"));
    }

    [AvaloniaFact]
    public async Task Strg_c_on_an_aufgabe_copies_its_text_not_an_empty_cell()
    {
        var (window, _) = ShowWorkspace(s => s.AddTask("Tür sichern", "EL", TaskImportance.High, TaskUrgency.Low, 30));
        var grid = Grid(window, "AUFGABEN", "TasksGrid");

        Assert.Equal("Tür sichern", await CopyClickedCellAsync(window, grid, "AUFGABE"));
        Assert.Equal("EL", await CopyClickedCellAsync(window, grid, "ZUGETEILT AN"));
    }

    [AvaloniaFact]
    public async Task Strg_c_on_a_funktion_copies_the_clicked_cell()
    {
        var (window, _) = ShowWorkspace(s => s.AssignRole("EL", "Mustermann, Max", callSign: "FFB 1/10"));
        var grid = Grid(window, "FUNKTIONEN", "RolesGrid");

        Assert.Equal("FFB 1/10", await CopyClickedCellAsync(window, grid, "FUNKRUFNAME"));
    }

    [AvaloniaFact]
    public async Task Strg_c_on_a_unit_copies_the_clicked_cell()
    {
        var (window, _) = ShowWorkspace(s => s.AddForceUnit("FFB Wache 1", 9, "FFB 1/44/1", "Alarmiert", null));
        var grid = Grid(window, "KRÄFTE", "ForcesGrid");

        Assert.Equal("FFB 1/44/1", await CopyClickedCellAsync(window, grid, "FUNKRUFNAME"));
    }

    [AvaloniaFact]
    public async Task Strg_c_in_a_field_inside_a_cell_copies_the_field_s_selection()
    {
        var (window, _) = ShowWorkspace(s => s.AddForceUnit("FFB Wache 1", 9, "FFB 1/44/1", "Alarmiert", "erste Meldung"));
        var grid = Grid(window, "KRÄFTE", "ForcesGrid");
        await window.Clipboard!.SetTextAsync("vorher");
        var notes = Cell(grid, "BEMERKUNG").GetVisualDescendants().OfType<TextBox>().Single();
        notes.Focus();
        notes.SelectionStart = 0;
        notes.SelectionEnd = 5;
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.C, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("erste", await window.Clipboard!.TryGetTextAsync());
    }

    [AvaloniaFact]
    public async Task Strg_c_on_a_trupp_copies_its_name_not_an_empty_cell()
    {
        var (window, vm) = ShowWorkspace(_ => { });
        vm.Scba.NewDesignation = "Angriffstrupp";
        vm.Scba.NewTruppfuehrer = AnonymizedExampleData.OperatorSurname;
        vm.Scba.NewTruppmann = AnonymizedExampleData.OperatorSurnameAlt;
        vm.Scba.AddTruppCommand.Execute(null);
        var grid = Grid(window, "ATEMSCHUTZ", "TruppGrid");

        Assert.Equal(vm.Scba.Trupps[^1].DisplayName, await CopyClickedCellAsync(window, grid, "TRUPP"));
        Assert.Equal(vm.Scba.Trupps[^1].Members, await CopyClickedCellAsync(window, grid, "MANNSCHAFT"));
    }

    [AvaloniaFact]
    public async Task The_cell_menu_copies_the_cell_or_the_whole_row_unquoted()
    {
        var (window, vm) = ShowWorkspace(s => s.AddJournalEntry(EtbDirection.Incoming, "Lage erkundet", "ILS", "ELW"));
        var grid = Grid(window, "ETB", "EtbGrid");
        var entry = vm.Etb.Entries[0];

        Assert.Equal("Lage erkundet", await CopyFromMenuAsync(window, grid, "EINTRAG", "Kopieren"));
        Assert.Equal(
            string.Join('\t', entry.Time, "ILS", "ELW", "Lage erkundet", entry.EnteredBy),
            await CopyFromMenuAsync(window, grid, "EINTRAG", "Zeile kopieren"));
    }
}
