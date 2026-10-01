using Avalonia.Controls;
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

/// <summary>#241: every incident change ran CoMessprotokollViewModel.Refresh, which cleared and
/// refilled the "HAUS" ComboBox's ItemsSource. The real ComboBox answers that Reset by nulling its
/// SelectedItem through the two-way binding, and a null Haus discards the open sidebar -- so an
/// ETB line written elsewhere (or arriving from a joined device) threw away a half-entered ppm
/// value. Only the real view triggers it: the ViewModel alone has nothing reacting to the Reset.</summary>
public class CoMessprotokollEditorSurvivesRefreshTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
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

    private static Border Sidebar(Window window) =>
        ((IncidentWorkspaceView)window.Content!)
            .GetVisualDescendants().OfType<CoMessprotokollView>().Single()
            .GetControl<Border>("DwellingEditor");

    private static (CoMessprotokollViewModel Co, Guid BuildingId) OpenEditorAndTypePpm(
        Window window, IncidentWorkspaceViewModel vm, LocalIncidentSession session)
    {
        session.AddCoBuilding("Mehrfamilienhaus A", 3, 4);
        session.AddCoBuilding("Mehrfamilienhaus B", 2, 2);
        Dispatcher.UIThread.RunJobs();
        WorkspaceRenderHelper.SelectTab(window, "CO-MESSUNG");

        var co = vm.CoMessprotokoll;
        var buildingId = co.SelectedBuilding!.Id;
        co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].OpenEditorCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var ppm = Sidebar(window).GetVisualDescendants().OfType<NumericUpDown>().Single();
        ppm.Value = 120;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(120, co.Editor!.CoValue);
        return (co, buildingId);
    }

    [AvaloniaFact]
    public void UnrelatedIncidentChange_KeepsTheOpenEditorAndItsTypedValue()
    {
        var (window, vm, session) = ShowWorkspace();
        var (co, buildingId) = OpenEditorAndTypePpm(window, vm, session);

        session.AddJournalEntry(EtbDirection.Internal, "Lage unverändert");
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(co.Editor);
        Assert.Equal(120, co.Editor!.CoValue);
        Assert.True(Sidebar(window).IsEffectivelyVisible);
        Assert.Equal(buildingId, co.SelectedBuilding?.Id);

        // The pending value is still previewed on its tile after the matrix rebuild.
        Assert.Equal(120, co.MatrixRows.Single(r => r.Ordinal == 2).Cells[0].CoValue);
    }

    [AvaloniaFact]
    public void AddingAFloor_KeepsTheOpenEditor_AndShowsTheNewFloor()
    {
        var (window, vm, session) = ShowWorkspace();
        var (co, buildingId) = OpenEditorAndTypePpm(window, vm, session);

        // Same Haus Id, new Building instance: the matrix must pick up the fresh one.
        co.AddObergeschossCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(co.Editor);
        Assert.Equal(120, co.Editor!.CoValue);
        Assert.Equal(buildingId, co.SelectedBuilding?.Id);
        Assert.Equal(4, co.SelectedBuilding!.FloorCount);
        Assert.Contains(co.MatrixRows, r => r.Ordinal == 4);
    }
}
