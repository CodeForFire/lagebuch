using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
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
    private static (Window Window, IncidentWorkspaceViewModel Vm) Scenario(double width = 1920, double height = 1032)
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
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
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

        // Who lives there: the optional free-text note beside the unit's Bezeichnung. Fictional
        // names per CONTRIBUTING — screenshots must never carry real personnel data.
        session.SetDwellingDetails(id, 2, 2, "Fam. Bergmann", true);
        session.SetDwellingDetails(id, 3, 1, "Fam. Kellner", null);
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

    [AvaloniaTheory]
    [InlineData(1280)]
    [InlineData(1400)]
    [InlineData(1920)]
    public void Every_floor_fills_the_line_without_shrinking_a_tile_below_legibility(double width)
    {
        var (window, _) = Scenario(width);

        var panels = Panels(window);
        Assert.Equal(6, panels.Count);

        foreach (var panel in panels)
        {
            var tiles = Tiles(panel);
            Assert.NotEmpty(tiles);

            foreach (var tile in tiles)
            {
                var tooNarrow = $"a tile shrank to {tile.Bounds.Width:F0}px at a window width of {width}px, below MinItemWidth: the ppm reading trims away there.";
                Assert.True(tile.Bounds.Width >= panel.MinItemWidth - 0.5, tooNarrow);
            }

            var used = tiles.Max(t => RightEdgeIn(t, panel));
            var overflows = $"a floor's tiles need {used:F0}px in a {panel.Bounds.Width:F0}px band — the row overflows.";
            Assert.True(used <= panel.Bounds.Width + 1, overflows);
        }

        Capture(window, $"co-messung-{width:F0}.png");
    }

    [AvaloniaFact]
    public void Two_penthouses_span_the_same_width_as_the_four_flats_below_them()
    {
        var (window, _) = Scenario();

        var panels = Panels(window);
        var penthouses = Tiles(panels[0]);  // 4.OG, 2 units
        var flats = Tiles(panels[1]);       // 3.OG, 4 units

        Assert.Equal(2, penthouses.Count);
        Assert.Equal(4, flats.Count);

        // The point of the equal-width columns: a floor's row is the building's footprint, so
        // both rows end on the same right edge however many units subdivide them.
        var penthouseEdge = penthouses.Max(t => RightEdgeIn(t, panels[0]));
        var flatEdge = flats.Max(t => RightEdgeIn(t, panels[1]));
        var ragged = $"4.OG ends at {penthouseEdge:F0}px but 3.OG at {flatEdge:F0}px — floors don't line up.";
        Assert.True(Math.Abs(penthouseEdge - flatEdge) <= 1, ragged);

        // ...and each penthouse is correspondingly twice a flat's width, plus the spacing the
        // flats' extra gutter would have taken.
        var penthouseWidth = penthouses[0].Bounds.Width;
        var flatWidth = flats[0].Bounds.Width;
        var uneven = $"penthouse {penthouseWidth:F0}px vs. flat {flatWidth:F0}px — not a clean subdivision.";
        Assert.True(Math.Abs(penthouseWidth - ((flatWidth * 2) + 5)) <= 1, uneven);
    }

    [AvaloniaFact]
    public void A_units_residents_are_shown_on_its_tile_not_only_in_a_tooltip()
    {
        var (window, _) = Scenario();

        // The crew reads the matrix at a glance across every unit; a name reachable only by
        // hovering one tile at a time isn't displayed at all for that job.
        var residents = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Name == "UnitResident")
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        Assert.Contains("Fam. Bergmann", residents);
        Assert.Contains("Fam. Kellner", residents);
    }

    [AvaloniaFact]
    public void Tiles_never_run_under_the_vertical_scrollbar()
    {
        // Short enough that the six floors don't fit and the vertical scrollbar appears.
        var (window, _) = Scenario(height: 560);

        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
            .First(s => s.GetVisualDescendants().OfType<EqualWidthWrapPanel>().Any());
        var bar = scroller.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == Orientation.Vertical);
        Assert.True(bar.IsEffectivelyVisible, "the matrix should be scrolling for this test to mean anything.");

        var barLeft = bar.TranslatePoint(new Point(0, 0), scroller)!.Value.X;
        var tiles = scroller.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "UnitTile");
        var rightmost = tiles.Max(t => t.TranslatePoint(new Point(t.Bounds.Width, 0), scroller)!.Value.X);

        // Avalonia's default AllowAutoHide floats the scrollbar over the content instead of
        // reserving layout space for it, so full-width tiles run underneath it.
        var overlap = $"tiles reach {rightmost:F2}px but the scrollbar starts at {barLeft:F2}px (viewport {scroller.Viewport.Width:F2}) — it covers them.";
        Assert.True(rightmost <= barLeft + 0.5, overlap);
    }

    [AvaloniaFact]
    public void Floor_buttons_only_appear_in_structure_mode()
    {
        var (window, vm) = Scenario();

        Assert.False(FloorButton(window, "OG HINZUFÜGEN").IsEffectivelyVisible);
        Assert.False(FloorButton(window, "UG HINZUFÜGEN").IsEffectivelyVisible);

        vm.CoMessprotokoll.IsStructureMode = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(FloorButton(window, "OG HINZUFÜGEN").IsEffectivelyVisible);
        Assert.True(FloorButton(window, "UG HINZUFÜGEN").IsEffectivelyVisible);
    }

    private static List<EqualWidthWrapPanel> Panels(Window window) =>
        window.GetVisualDescendants().OfType<EqualWidthWrapPanel>().ToList();

    /// <summary>The visible tile Borders, not the panel's item containers. Measuring the containers
    /// hides the bug where the column stretches but the tile inside it sits narrow at the left.</summary>
    private static List<Border> Tiles(EqualWidthWrapPanel panel) =>
        panel.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "UnitTile").ToList();

    /// <summary>A tile's right edge in its panel's coordinate space. Control.Bounds is relative to
    /// the immediate parent (here the item container), so comparing raw Bounds.Right across floors
    /// compares tile widths, not positions.</summary>
    private static double RightEdgeIn(Visual tile, Visual panel) =>
        tile.TranslatePoint(new Point(tile.Bounds.Width, 0), panel)?.X ?? double.NaN;

    private static Button FloorButton(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == content);

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
