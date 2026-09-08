using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// The mixed-use building #265 is really about: 1.UG 14 Kellerabteile, EG 3 Läden, 1.OG 2 Praxen,
/// 2./3.OG 4 Wohnungen each, 4.OG 2 Penthäuser -- 29 units across 6 floors with five different
/// per-floor counts, none of which a building-wide ApartmentsPerFloor can express.
/// </summary>
public class CoMessprotokollMixedBuildingTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm) Scenario()
    {
        var session = LocalIncidentSession.StartNew(
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

        session.AddCoBuilding("Wohn- und Geschäftshaus", 4, 4, undergroundFloorCount: 1);
        Dispatcher.UIThread.RunJobs();
        var id = session.Incident.Buildings[0].Id;

        session.SetApartmentCount(id, -1, 14);
        session.SetApartmentCount(id, 0, 3);
        session.SetApartmentCount(id, 1, 2);
        session.SetApartmentCount(id, 4, 2);

        session.SetFloorDescription(id, 4, "Penthäuser");
        session.SetFloorDescription(id, 3, "Wohnungen");
        session.SetFloorDescription(id, 2, "Wohnungen");
        session.SetFloorDescription(id, 1, "Praxen");
        session.SetFloorDescription(id, 0, "Läden");
        session.SetFloorDescription(id, -1, "Kellerabteile");

        session.SetApartmentLabel(id, 4, 1, "Penthouse West");
        session.SetApartmentLabel(id, 4, 2, "Penthouse Ost");
        session.SetApartmentLabel(id, 1, 1, "Praxis");
        session.SetApartmentLabel(id, 1, 2, "Kanzlei");
        session.SetApartmentLabel(id, 0, 1, "Bäckerei");
        session.SetApartmentLabel(id, 0, 2, "Kiosk");
        session.SetApartmentLabel(id, 0, 3, "Supermarkt");
        Dispatcher.UIThread.RunJobs();

        // Mid-incident: 3.OG cleared, one find on 2.OG, EG part-way, basement not started -- so the
        // summary, the per-floor progress and the filters all have something real to show.
        foreach (var apt in new[] { 1, 2, 3, 4 })
        {
            session.SetDwellingStatus(id, 3, apt, DwellingStatus.Searched);
        }

        session.RecordCoValue(id, 3, 1, 0);
        session.RecordCoValue(id, 3, 4, 12);

        session.SetDwellingStatus(id, 2, 1, DwellingStatus.Searched);
        session.SetDwellingStatus(id, 2, 2, DwellingStatus.Affected);
        session.RecordCoValue(id, 2, 2, 112);
        session.SetDwellingStatus(id, 2, 3, DwellingStatus.Searched);

        session.SetDwellingStatus(id, 0, 1, DwellingStatus.Searched);
        session.RecordCoValue(id, 0, 1, 0);
        Dispatcher.UIThread.RunJobs();

        var tabs = ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");
        tabs.SelectedIndex = 6; // CO-MESSUNG
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
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
        frame.SavePng(Path.Combine(dir, name));
    }

    [AvaloniaFact]
    public void Every_floor_keeps_its_own_unit_count_and_progress()
    {
        var (window, vm) = Scenario();
        var co = vm.CoMessprotokoll;

        // 4 on 3.OG + 2 on 2.OG + 1 in the EG = 7 searched, plus the single find on 2.OG.
        Assert.Equal(29, co.TotalUnits);
        Assert.Equal(7, co.SearchedUnits);
        Assert.Equal(1, co.AffectedUnits);
        Assert.Equal(21, co.OpenUnits);

        Assert.Equal(14, co.MatrixRows.Single(r => r.Ordinal == -1).Cells.Count);
        Assert.Equal(3, co.MatrixRows.Single(r => r.Ordinal == 0).Cells.Count);
        Assert.Equal(2, co.MatrixRows.Single(r => r.Ordinal == 1).Cells.Count);
        Assert.Equal(4, co.MatrixRows.Single(r => r.Ordinal == 2).Cells.Count);
        Assert.Equal(2, co.MatrixRows.Single(r => r.Ordinal == 4).Cells.Count);

        var cleared = co.MatrixRows.Single(r => r.Ordinal == 3);
        Assert.Equal("4/4", cleared.ProgressLabel);
        Assert.True(cleared.IsComplete);
        Assert.True(co.MatrixRows.Single(r => r.Ordinal == 2).HasAffected);

        Capture(window, "co-messung-bands.png");
    }

    [AvaloniaFact]
    public void An_unmeasured_tile_shows_no_ppm_text_at_all()
    {
        var (_, vm) = Scenario();

        var unmeasured = vm.CoMessprotokoll.MatrixRows.Single(r => r.Ordinal == -1).Cells[0];
        Assert.Equal(string.Empty, unmeasured.CoCompact);

        var measured = vm.CoMessprotokoll.MatrixRows.Single(r => r.Ordinal == 2).Cells[1];
        Assert.Equal("112 ppm", measured.CoCompact);
    }

    [AvaloniaFact]
    public void Filtering_to_open_units_drops_finished_floors_but_not_their_counts()
    {
        var (window, vm) = Scenario();
        var co = vm.CoMessprotokoll;

        co.ShowOpenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // 3.OG is fully processed, so its band drops out entirely.
        Assert.DoesNotContain(co.MatrixRows, r => r.Ordinal == 3);
        Assert.Equal(21, co.MatrixRows.Sum(r => r.Cells.Count));

        // Header counts still describe the whole floor, not the filtered subset.
        Assert.Equal("3/4", co.MatrixRows.Single(r => r.Ordinal == 2).ProgressLabel);
        Assert.Equal(29, co.TotalUnits);

        Capture(window, "co-messung-filter-open.png");
    }

    [AvaloniaFact]
    public void Structure_mode_clears_an_active_filter_so_counts_are_edited_against_whole_floors()
    {
        var (window, vm) = Scenario();
        var co = vm.CoMessprotokoll;

        co.ShowOpenCommand.Execute(null);
        co.IsStructureMode = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(CoUnitFilter.All, co.Filter);
        Assert.Equal(29, co.MatrixRows.Sum(r => r.Cells.Count));

        Capture(window, "co-messung-structure.png");
    }
}
