using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

/// <summary>The GELÄNDE picker over the made-up terrain of <see cref="FictionalRegionPack"/>.</summary>
public sealed class LagekarteTerrainRenderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;
    private readonly HttpClient _http = new();

    public void Dispose()
    {
        _http.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [AvaloniaFact]
    public void Picking_a_layer_in_the_Gelaende_flyout_lays_it_over_the_map_and_closes_the_flyout()
    {
        var (window, view, vm) = Show();
        var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TerrainButton");
        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("Gelände", AutomationProperties.GetName(button));
        using var before = Frame(window);

        Click(button);
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        Assert.True(flyout.IsOpen);
        var radio = Assert.IsAssignableFrom<Control>(flyout.Content).GetVisualDescendants().OfType<RadioButton>()
            .Single(r => r.Content as string == "Schummerung");
        Click(radio);

        Assert.Equal(TerrainLayer.Schummerung, vm.Terrain.Layer);
        Assert.False(flyout.IsOpen);
        WaitForTerrain(view);
        using var after = Frame(window);
        Assert.True(DifferingPixels(before, after) > 10_000, "the relief changed too little of the map");
    }

    [AvaloniaFact]
    public void A_water_point_s_card_opens_the_Hoehenprofil_to_the_Einsatzort_under_the_map()
    {
        var (window, view, vm) = Show(einsatzort: true);
        SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return vm.WaterPoints is not null;
            },
            TimeSpan.FromSeconds(10));

        // The Saugstelle at the Musterbach, south-east of the Marktplatz.
        vm.SelectedWaterPoint = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<WaterPoint>>(vm.WaterPoints), w => w.Kind == WaterPointKind.Saugstelle && w.Label == "Musterbach");
        Dispatcher.UIThread.RunJobs();
        var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShowProfileButton");
        Assert.True(button.IsEffectivelyVisible);
        var panel = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ProfilePanel");
        Assert.False(panel.IsVisible);

        Click(button);

        Assert.True(panel.IsEffectivelyVisible);
        var chart = view.GetVisualDescendants().OfType<ElevationProfileChart>().Single();
        Assert.Same(vm.Profile, chart.Profile);
        Assert.Equal("Höhenprofil", AutomationProperties.GetName(chart));
        var summary = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ProfileSummary");
        Assert.Equal(vm.ProfileSummary, summary.Text);
        Assert.Contains(" m Luftlinie · Einsatzort ", summary.Text, StringComparison.Ordinal);
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        Assert.Contains(Assert.IsAssignableFrom<IReadOnlyList<OverlayShape>>(map.Overlays), s => s.Kind == OverlayShapeKind.Line);

        using (var frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame()))
        {
            frame.SavePng(Path.Join(Shots(), "lagekarte-hoehenprofil.png"));
        }

        Click(view.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Höhenprofil schließen"));
        Assert.False(panel.IsVisible);
    }

    [AvaloniaFact]
    public void Without_heights_in_the_pack_there_is_no_Gelaende_picker()
    {
        var (_, view, _) = Show(withElevation: false);

        Assert.False(view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TerrainButton").IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(TerrainLayer.Schummerung, "lagekarte-gelaende-schummerung.png")]
    [InlineData(TerrainLayer.Hoehenlinien, "lagekarte-gelaende-hoehenlinien.png")]
    [InlineData(TerrainLayer.Hoehenschichten, "lagekarte-gelaende-hoehenschichten.png")]
    [InlineData(TerrainLayer.Hangneigung, "lagekarte-gelaende-hangneigung.png")]
    [InlineData(TerrainLayer.RelativZumEinsatzort, "lagekarte-gelaende-relativ.png")]
    [InlineData(TerrainLayer.Fliessweg, "lagekarte-gelaende-fliessweg.png")]
    public void Each_layer_shows_with_its_key(TerrainLayer layer, string shot)
    {
        var (window, view, vm) = Show(einsatzort: true);

        vm.Terrain.Layer = layer;
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return !vm.Terrain.IsTracingFlow;
            },
            TimeSpan.FromSeconds(20)));
        WaitForTerrain(view);

        var key = view.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.Name == "TerrainKey");
        Assert.True(key.IsEffectivelyVisible);
        var text = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TerrainLegendText");
        Assert.Equal(vm.Terrain.LegendText, text.Text);
        Assert.NotNull(vm.Terrain.Style);
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        Assert.Equal(layer == TerrainLayer.Fliessweg, map.FlowPath is { Count: > 1 });

        using var frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
        frame.SavePng(Path.Join(Shots(), shot));
    }

    private (Window Window, LagekarteView View, LagekarteViewModel Vm) Show(bool withElevation = true, bool einsatzort = false)
    {
        var root = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(root, withElevation);
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator(AnonymizedExampleData.OperatorSurname, "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        if (einsatzort)
        {
            session.SetIncidentLocation(FictionalRegionPack.Musterstrasse1);
        }

        var vm = new LagekarteViewModel(session, () => { }, new RegionPackLibrary(_http, root));
        vm.Map.ChangeViewCommand.Execute(new MapViewChange(FictionalRegionPack.Musterstrasse1, 15));
        var view = new LagekarteView { DataContext = vm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    // Terrain tiles are asked for when the map is drawn and worked out off the UI thread; each
    // arrives as a dispatcher job. Headless, nothing is drawn until a frame is taken.
    private static void WaitForTerrain(LagekarteView view)
    {
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        Dispatcher.UIThread.RunJobs();
        using (Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(map)).CaptureRenderedFrame())
        {
            // Only drawn, so the map asks for its terrain tiles.
        }

        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return !map.IsTerrainBusy;
            },
            TimeSpan.FromSeconds(20)));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Control control)
    {
        var topLevel = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        var centre = Assert.NotNull(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel));
        topLevel.MouseDown(centre, MouseButton.Left);
        topLevel.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static WriteableBitmap Frame(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
    }

    private static int DifferingPixels(WriteableBitmap a, WriteableBitmap b)
    {
        using var left = a.Lock();
        using var right = b.Lock();
        var count = 0;
        for (var i = 0; i < left.RowBytes * left.Size.Height; i += 4)
        {
            if (Marshal.ReadInt32(left.Address, i) != Marshal.ReadInt32(right.Address, i))
            {
                count++;
            }
        }

        return count;
    }

    private static string Shots()
    {
        var shots = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(shots);
        return shots;
    }
}
