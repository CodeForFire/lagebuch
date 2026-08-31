using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// Issue #150 (Plan A): the new WASSERFÖRDERUNG tab. Doubles as the PR screenshot capture
// (RENDER_OUT), same idiom as TasksTabRenderTests/ForcesTabRenderTests.
public class WasserfoerderungTabRenderTests
{
    private static (Window Window, IncidentWorkspaceViewModel Vm, LocalIncidentSession Session) ShowWorkspace(
        MasterDataSet? masterData = null)
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident",
            Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(session, clock, new ManualTicker(),
            masterData ?? WorkspaceRenderHelper.MasterData(),
            new FakeDialogs(), new NoopAlarmService(), new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, session);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
            return;
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, name));
    }

    private static TabControl Tabs(Window window) =>
        ((IncidentWorkspaceView)window.Content!).GetControl<TabControl>("ModuleTabs");

    [AvaloniaFact]
    public void Wasserfoerderung_tab_renders_empty_then_planned_streets()
    {
        var (window, vm, session) = ShowWorkspace();

        Tabs(window).SelectedIndex = 9; // WASSERFÖRDERUNG
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.Wasserfoerderung.Rows);
        Capture(window, "wasserfoerderung-before.png");

        session.AddWasserfoerderungLeitung("TLF 20/8", "FFB 1/44/1", 2000, 100);
        session.AddWasserfoerderungLeitung(null, null, 400, 0);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Wasserfoerderung.Rows.Count);
        Assert.Equal("Ltg 1", vm.Wasserfoerderung.Rows[0].NumberDisplay);
        Assert.Equal(4, session.Incident.Wasserfoerderung[0].PumpCount);
        Capture(window, "wasserfoerderung-after.png");
    }

    // #150 phase 2 (Plan B): drawing a route on the map, end to end through the real view.
    private static string CreateRegionFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"region-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        using (var writer = new BinaryWriter(File.Create(Path.Combine(dir, "region.dem"))))
        {
            writer.Write("FWDM"u8.ToArray());
            writer.Write(1);
            writer.Write(48.2); // OriginLatitude (north edge)
            writer.Write(11.4); // OriginLongitude (west edge)
            writer.Write(0.01); // CellSizeDegrees
            writer.Write(10);   // Rows
            writer.Write(10);   // Cols
            for (var i = 0; i < 100; i++)
                writer.Write((short)500);
        }

        using (var cn = LageBuch.Persistence.Sqlite.SqliteConnectionFactory.OpenReadWrite(Path.Combine(dir, "region.mbtiles")))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);";
            cmd.ExecuteNonQuery();
        }

        return dir;
    }

    [AvaloniaFact]
    public void Karte_mode_draws_a_route_and_finishing_it_adds_a_route_based_leitung()
    {
        var regionDir = CreateRegionFolder();
        try
        {
            var masterData = WorkspaceRenderHelper.MasterData() with
            {
                Einsatzgebiet = new Einsatzgebiet("Testgebiet", regionDir),
            };
            var (window, vm, session) = ShowWorkspace(masterData);

            Tabs(window).SelectedIndex = 9; // WASSERFÖRDERUNG
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.Wasserfoerderung.IsMapModeAvailable);
            var view = (IncidentWorkspaceView)window.Content!;
            var karteButton = view.GetVisualDescendants().OfType<ToggleButton>().First(b => b.Content as string == "KARTE");
            var karteCenter = karteButton.TranslatePoint(
                new Point(karteButton.Bounds.Width / 2, karteButton.Bounds.Height / 2), window)!.Value;
            window.MouseDown(karteCenter, MouseButton.Left);
            window.MouseUp(karteCenter, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.Wasserfoerderung.IsMapMode);

            var canvas = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
            var p1 = canvas.TranslatePoint(new Point(10, 10), window)!.Value;
            var p2 = canvas.TranslatePoint(new Point(canvas.Bounds.Width - 10, 10), window)!.Value;
            window.MouseDown(p1, MouseButton.Left);
            window.MouseUp(p1, MouseButton.Left);
            window.MouseDown(p2, MouseButton.Left);
            window.MouseUp(p2, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, vm.Wasserfoerderung.DrawnRoutePoints.Count);
            Capture(window, "wasserfoerderung-karte-drawing.png");

            var finishButton = view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "FERTIG");
            var finishCenter = finishButton.TranslatePoint(
                new Point(finishButton.Bounds.Width / 2, finishButton.Bounds.Height / 2), window)!.Value;
            window.MouseDown(finishCenter, MouseButton.Left);
            window.MouseUp(finishCenter, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var leitung = Assert.Single(session.Incident.Wasserfoerderung);
            Assert.NotNull(leitung.RoutePoints);
            Assert.Equal(2, leitung.RoutePoints!.Count);
            Assert.Empty(vm.Wasserfoerderung.DrawnRoutePoints);
            Capture(window, "wasserfoerderung-karte-finished.png");
        }
        finally
        {
            Directory.Delete(regionDir, recursive: true);
        }
    }

    // Regression for a real layout bug: at a window short/narrow enough that the DataGrid's
    // share of the DockPanel collapses to zero, the Bottom-docked Map Border (later in Z-order)
    // rendered on top of and overlapping the mode-toggle buttons above it, and the Karte input
    // dock's unwrapped button row pushed "FERTIG" past the window's right edge entirely.
    [AvaloniaFact]
    public void Karte_mode_content_never_overlaps_the_header_or_overflows_the_window()
    {
        var regionDir = CreateRegionFolder();
        try
        {
            var masterData = WorkspaceRenderHelper.MasterData() with
            {
                Einsatzgebiet = new Einsatzgebiet("Testgebiet", regionDir),
            };
            var (window, vm, _) = ShowWorkspace(masterData);
            window.Width = 1920;
            window.Height = 700; // short enough to reproduce the DataGrid-collapses-to-zero overflow
            Dispatcher.UIThread.RunJobs();

            Tabs(window).SelectedIndex = 9; // WASSERFÖRDERUNG
            Dispatcher.UIThread.RunJobs();

            var view = (IncidentWorkspaceView)window.Content!;
            var karteButton = view.GetVisualDescendants().OfType<ToggleButton>().First(b => b.Content as string == "KARTE");
            var karteCenter = karteButton.TranslatePoint(
                new Point(karteButton.Bounds.Width / 2, karteButton.Bounds.Height / 2), window)!.Value;
            window.MouseDown(karteCenter, MouseButton.Left);
            window.MouseUp(karteCenter, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var manuellButton = view.GetVisualDescendants().OfType<ToggleButton>().First(b => b.Content as string == "MANUELL");
            var mapCanvas = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
            var fertigButton = view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "FERTIG");

            var headerBottom = manuellButton.TranslatePoint(new Point(0, manuellButton.Bounds.Height), window)!.Value.Y;
            var mapTop = mapCanvas.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            var fertigRight = fertigButton.TranslatePoint(new Point(fertigButton.Bounds.Width, 0), window)!.Value.X;

            Assert.True(mapTop >= headerBottom,
                $"Map (top={mapTop}) overlaps the mode-toggle header (bottom={headerBottom}).");
            Assert.True(fertigRight <= window.ClientSize.Width,
                $"FERTIG button's right edge ({fertigRight}) is past the window width ({window.ClientSize.Width}).");
        }
        finally
        {
            Directory.Delete(regionDir, recursive: true);
        }
    }
}