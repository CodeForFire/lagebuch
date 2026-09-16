using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// A status recorded in the incident but missing from today's Stammdaten -- an older Einsatz, an
// imported file, or a joined client on different master data -- used to render as an empty Status
// cell, because the cell is a closed ComboBox with nothing matching to select. (#302, #337)
public class ForcesOffCatalogueStatusTests
{
    private const string RetiredStatus = "Einsatzbereit am Standort";

    [AvaloniaFact]
    public void A_status_missing_from_the_stammdaten_still_shows_in_the_grid()
    {
        var view = HostForcesViewWithOneUnit(RetiredStatus, out _, out _);

        Assert.Equal(RetiredStatus, StatusCellText(view));
    }

    [AvaloniaFact]
    public void A_configured_status_shows_exactly_as_before()
    {
        var view = HostForcesViewWithOneUnit("Im Einsatz", out _, out _);

        Assert.Equal("Im Einsatz", StatusCellText(view));
    }

    [AvaloniaFact]
    public void Showing_the_retired_status_does_not_rewrite_it()
    {
        var view = HostForcesViewWithOneUnit(RetiredStatus, out var vm, out _);

        // Rendering must not push a selection back through the two-way binding: the unit's status
        // is a record of what was reported, not something opening the tab may quietly change.
        Assert.Equal(RetiredStatus, vm.Forces[0].Status);
        Assert.Equal(RetiredStatus, StatusCellText(view));
    }

    /// <summary>The text the Status cell's ComboBox actually displays for the single unit's row.</summary>
    private static string? StatusCellText(ForcesView view) =>
        view.GetVisualDescendants()
            .OfType<ComboBox>()
            .Select(cb => cb.SelectionBoxItem as string)
            .FirstOrDefault(text => text is not null);

    private static ForcesView HostForcesViewWithOneUnit(string status, out ForcesViewModel vm, out Window window)
    {
        var session = LocalIncidentSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddForceUnit("FF Musterstadt", 6, "FFB 40/1", status: status, notes: null);

        var workspace = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        vm = workspace.Forces;

        var view = new ForcesView { DataContext = vm };
        window = new Window { Content = view, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }
}
