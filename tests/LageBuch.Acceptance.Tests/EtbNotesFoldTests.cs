using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// #973: a click on an entry's line unfolds its notes below it, and a second click folds them in.
// With RENDER_OUT=<dir> the tests also write the PR's screenshots.
public class EtbNotesFoldTests
{
    private const string Notes = "Rückruf über Festnetz zugesagt, Ansprechpartner vor Ort ist der Hausmeister.";

    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace(double width = 1920)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            clock,
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "ETB");
        return (window, vm);
    }

    private static EtbEntryRow AddEntry(IncidentWorkspaceViewModel vm, string text, string? notes)
    {
        vm.Etb.NewFrom = AnonymizedExampleData.CallSign;
        vm.Etb.NewTo = "ELW";
        vm.Etb.NewText = text;
        vm.Etb.AddEntryCommand.Execute(null);
        var row = vm.Etb.Entries.Single(e => e.Text == text);
        if (notes is not null)
        {
            row.BeginEditCommand.Execute(null);
            vm.Etb.EditNotes = notes;
            vm.Etb.SaveEditCommand.Execute(null);
        }

        Dispatcher.UIThread.RunJobs();
        return row;
    }

    private static DataGrid Grid(Window window) =>
        window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "EtbGrid");

    private static DataGridCell TextCell(DataGrid grid, EtbEntryRow row) =>
        grid.GetVisualDescendants().OfType<DataGridCell>()
            .First(c => ReferenceEquals(c.DataContext, row)
                && DataGridColumn.GetColumnContainingElement(c) is { Header: "EINTRAG" });

    private static void Click(Window window, Visual target)
    {
        var point = target.TranslatePoint(new Point(4, 4), window) ?? throw new InvalidOperationException("Not in the window.");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static bool NotesShown(Visual within) =>
        within.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Notes && t.IsEffectivelyVisible);

    private static void Shoot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("RENDER_OUT") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    [AvaloniaFact]
    public void Clicking_a_row_with_notes_unfolds_them_and_a_second_click_folds_them_in()
    {
        var (window, vm) = ShowWorkspace();
        AddEntry(vm, "Erkundung Hauptstraße 12 abgeschlossen", null);
        var row = AddEntry(vm, "Hausverwaltung verständigt", Notes);
        var grid = Grid(window);
        Assert.False(NotesShown(grid));

        // The folded row says it has notes.
        var toggle = TextCell(grid, row).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesToggle");
        Assert.True(toggle.IsEffectivelyVisible);
        Assert.Equal("Notiz einblenden", AutomationProperties.GetName(toggle));
        Shoot(window, "etb-notes-folded.png");

        Click(window, TextCell(grid, row));
        Assert.True(row.IsNotesExpanded);
        Assert.True(NotesShown(grid));
        Assert.Equal("Notiz ausblenden", AutomationProperties.GetName(toggle));
        Shoot(window, "etb-notes-unfolded.png");

        // A second click in quick succession arrives as a double tap; it folds all the same.
        Click(window, TextCell(grid, row));
        Assert.False(row.IsNotesExpanded);
        Assert.False(NotesShown(grid));
    }

    [AvaloniaFact]
    public void Clicking_a_row_without_notes_unfolds_nothing_and_shows_no_glyph()
    {
        var (window, vm) = ShowWorkspace();
        var row = AddEntry(vm, "Erkundung Hauptstraße 12 abgeschlossen", null);
        var grid = Grid(window);

        Click(window, TextCell(grid, row));

        Assert.False(row.IsNotesExpanded);
        Assert.False(TextCell(grid, row).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesToggle").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Enter_on_a_clicked_row_with_notes_still_opens_the_edit_panel()
    {
        var (window, vm) = ShowWorkspace();
        var row = AddEntry(vm, "Hausverwaltung verständigt", Notes);
        var grid = Grid(window);
        Click(window, TextCell(grid, row));

        window.Press(PhysicalKey.Enter);

        Assert.Same(row, vm.Etb.EditingEntry);
        Assert.Equal(Notes, vm.Etb.EditNotes);
        var notesBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "EditNotesBox");
        Assert.True(notesBox.IsEffectivelyVisible);
        Shoot(window, "etb-notes-edit-panel.png");
    }

    [AvaloniaFact]
    public void Tapping_a_card_with_notes_unfolds_them()
    {
        var (window, vm) = ShowWorkspace(width: 412);
        var row = AddEntry(vm, "Hausverwaltung verständigt", Notes);
        var cards = window.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "EtbCards");
        Assert.True(cards.IsEffectivelyVisible);
        var card = cards.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("row-card") && ReferenceEquals(b.DataContext, row));

        Click(window, card);

        Assert.True(row.IsNotesExpanded);
        Assert.True(NotesShown(cards));
    }
}
