using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Acceptance.Tests;

/// <summary>#242: ABBRECHEN in the "WOHNUNG BEARBEITEN" sidebar used to run the same persist as
/// FERTIG, and Status/CO-Wert were written through on every click besides -- so a measurement could
/// not be discarded once entered. Driven through the real view rather than the ViewModel alone
/// because the fix moved every sidebar field onto a separate buffer object: a stale
/// <c>SelectedCell.*</c> binding left in the XAML would leave the control unbound (and silently
/// dead) without failing a single ViewModel test.</summary>
public class CoMessprotokollEditorCancelTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>());
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

    private static Border Sidebar(Window window) =>
        ((IncidentWorkspaceView)window.Content!)
            .GetVisualDescendants().OfType<CoMessprotokollView>().Single()
            .GetControl<Border>("DwellingEditor");

    private static Button SidebarButton(Window window, string content) =>
        Sidebar(window).GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content as string == content && b.IsEffectivelyVisible); // the discard question has its own ABBRECHEN (#652)

    [AvaloniaFact]
    public void Cancel_DiscardsTheEdit_AndLeavesTheJournalAlone()
    {
        var (window, vm, session) = ShowWorkspace();
        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        Dispatcher.UIThread.RunJobs();

        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");

        var co = vm.CoMessprotokoll;
        var buildingId = session.Incident.Buildings[0].Id;
        var journalBefore = session.Incident.Journal.Count;

        var tile = co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0];
        tile.OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Sidebar(window).IsEffectivelyVisible);

        // Through the rendered controls, so a broken Editor.* binding fails here.
        SidebarButton(window, "Person(en) betroffen").Command!.Execute(null);
        var ppm = Sidebar(window).GetVisualDescendants().OfType<NumericUpDown>().Single();
        ppm.Value = 120;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(120, co.Editor!.GasInputs[0].Value);
        Assert.Equal(DwellingStatus.Affected, co.Editor.Status);

        // The pending state is previewed on the tile, but nothing is written yet.
        Assert.Equal(DwellingStatus.Affected, co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].Status);
        var dwelling = session.Incident.Dwellings.Single(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == 2 && d.ApartmentNumber == 1);
        Assert.Equal(DwellingStatus.NotSearched, dwelling.Status);
        Assert.Null(dwelling.CoValue());
        Capture(window, "co-messung-cancel-pending.png");

        SidebarButton(window, "ABBRECHEN").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(co.IsEditorOpen); // #652: asks before dropping a typed value
        Capture(window, "co-messung-discard-confirm.png");
        SidebarButton(window, "VERWERFEN").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var after = session.Incident.Dwellings.Single(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == 2 && d.ApartmentNumber == 1);
        Assert.Equal(DwellingStatus.NotSearched, after.Status);
        Assert.Null(after.CoValue());
        Assert.Equal(journalBefore, session.Incident.Journal.Count);
        Assert.False(co.IsEditorOpen);
        Assert.Equal(DwellingStatus.NotSearched, co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].Status);

        Capture(window, "co-messung-cancel.png");
    }

    // #652, by keyboard: Esc on a typed value raises the question with ABBRECHEN focused (the
    // destructive-confirm rule from #545), so a second reflex Enter keeps the value.
    [AvaloniaFact]
    public void Escape_on_a_typed_value_asks_and_a_reflex_enter_keeps_it()
    {
        var (window, vm, session) = ShowWorkspace();
        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");
        var co = vm.CoMessprotokoll;
        co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var ppm = Sidebar(window).GetVisualDescendants().OfType<NumericUpDown>().Single();
        ppm.Value = 120;
        Sidebar(window).GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible).Focus();
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.Escape);
        Dispatcher.UIThread.RunJobs();

        Assert.True(co.IsEditorOpen);
        Assert.True(co.IsDiscardEditorConfirmOpen);
        var keep = Sidebar(window).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelDiscardEditorButton");
        window.AssertFocused(keep);

        window.Press(PhysicalKey.Enter);
        Dispatcher.UIThread.RunJobs();

        Assert.False(co.IsDiscardEditorConfirmOpen);
        Assert.True(co.IsEditorOpen);
        Assert.Equal(120, co.Editor!.GasInputs[0].Value);
    }

    [AvaloniaFact]
    public void Escape_on_an_untouched_editor_closes_it_in_one_key()
    {
        var (window, vm, session) = ShowWorkspace();
        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");
        var co = vm.CoMessprotokoll;
        co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        SidebarButton(window, "ABBRECHEN").Focus();
        Dispatcher.UIThread.RunJobs();

        window.Press(PhysicalKey.Escape);
        Dispatcher.UIThread.RunJobs();

        Assert.False(co.IsEditorOpen);
        Assert.False(co.IsDiscardEditorConfirmOpen);
    }

    [AvaloniaFact]
    public void Confirm_CommitsTheEdit()
    {
        var (window, vm, session) = ShowWorkspace();
        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        Dispatcher.UIThread.RunJobs();

        WorkspaceRenderHelper.SelectTab(window, "GASMESSUNG");

        var co = vm.CoMessprotokoll;
        var buildingId = session.Incident.Buildings[0].Id;

        co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        SidebarButton(window, "Person(en) betroffen").Command!.Execute(null);
        Sidebar(window).GetVisualDescendants().OfType<NumericUpDown>().Single().Value = 120;
        Dispatcher.UIThread.RunJobs();

        SidebarButton(window, "FERTIG").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var dwelling = session.Incident.Dwellings.Single(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == 2 && d.ApartmentNumber == 1);
        Assert.Equal(DwellingStatus.Affected, dwelling.Status);
        Assert.Equal(120, dwelling.CoValue());
        Assert.False(co.IsEditorOpen);
    }
}
