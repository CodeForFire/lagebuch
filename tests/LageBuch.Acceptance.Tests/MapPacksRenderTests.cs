using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

public sealed class MapPacksRenderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public void The_Karten_section_lists_the_installed_pack_and_shows_it_with_its_attribution()
    {
        var root = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(root);
        using var http = new HttpClient();
        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles(), new RegionPackLibrary(http, root));
        vm.SelectedSection = vm.Sections.OfType<MapPacksSection>().Single();
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(vm.Sections, s => s.Title == "Karten");
        Assert.Equal(1, view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "InstalledMapPacks").ItemCount);
        Assert.False(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NoMapPacksHint").IsVisible);
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        Assert.NotNull(map.TileSource);
        Assert.True(map.WheelZoomNeedsControl);
        Assert.Equal("© OpenStreetMap contributors (ODbL).", view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MapAttribution").Text);

        // The PR screenshot.
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "stammdaten-karten.png"));
    }

    private static string Shots()
    {
        var shots = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(shots);
        return shots;
    }

    // Serves one catalogue and nothing else; no test reaches the network.
    private sealed class CatalogueHandler(string catalogue) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(catalogue) });
    }

    [AvaloniaFact]
    public async Task An_update_with_the_same_version_says_why_it_is_offered()
    {
        var root = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(root);
        using var handler = new CatalogueHandler($$"""
            {"formatVersion": 2, "packs": [{"slug": "ffb", "name": "Landkreis Fürstenfeldbruck", "version": 1,
              "downloadUrl": "https://example.org/ffb-v1.zip", "sizeBytes": 264978779, "sha256": "{{new string('b', 64)}}",
              "bounds": {"west": 10.9527, "south": 48.0607, "east": 11.4532, "north": 48.3237}, "minZoom": 11, "maxZoom": 17,
              "builtAt": "2026-11-03T20:15:49Z", "attribution": "{{FictionalRegionPack.Attribution}}"}]}
            """);
        using var http = new HttpClient(handler);
        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles(), new RegionPackLibrary(http, root));
        var section = vm.Sections.OfType<MapPacksSection>().Single();
        vm.SelectedSection = section;
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 760 };
        window.Show();

        await section.LoadCatalogCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var row = Assert.Single(section.Available);
        Assert.Equal("AKTUALISIEREN", row.ActionLabel);
        Assert.Equal("Neu gebaut am 03.11.2026 – installiert ist der Stand vom 27.10.2026.", row.UpdateReason);
        var reason = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == row.UpdateReason);
        Assert.True(reason.IsVisible);

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "stammdaten-karten-grund.png"));
    }

    [AvaloniaTheory]
    [InlineData(1024, 600)] // a small laptop
    [InlineData(400, 780)] // a phone, where the section opens full width
    public async Task On_a_short_screen_the_whole_catalogue_and_the_download_progress_scroll_into_view_above_the_map(int width, int height)
    {
        var root = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(root);
        var packs = string.Join(",", Enumerable.Range(1, 8).Select(i => $$"""
            {"slug": "lk{{i}}", "name": "Landkreis Muster {{i}}", "version": 1,
              "downloadUrl": "https://example.org/lk{{i}}-v1.zip", "sizeBytes": 264978779, "sha256": "{{new string('b', 64)}}",
              "bounds": {"west": 10.9527, "south": 48.0607, "east": 11.4532, "north": 48.3237}, "minZoom": 11, "maxZoom": 17,
              "builtAt": "2026-11-03T20:15:49Z", "attribution": "{{FictionalRegionPack.Attribution}}"}
            """));
        using var handler = new CatalogueHandler($$"""{"formatVersion": 2, "packs": [{{packs}}]}""");
        using var http = new HttpClient(handler);
        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles(), new RegionPackLibrary(http, root));
        var section = vm.Sections.OfType<MapPacksSection>().Single();
        vm.SelectedSection = section;
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        await section.LoadCatalogCommand.ExecuteAsync(null);
        vm.IsDetailOpen = true;
        section.IsBusy = true;
        section.Progress = 0.4;
        Dispatcher.UIThread.RunJobs();

        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var mapTop = map.TranslatePoint(default, window)!.Value.Y;
        var last = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Landkreis Muster 8");
        var bar = view.GetVisualDescendants().OfType<ProgressBar>().Single();
        var bottoms = new List<(string Name, double Bottom)>();
        foreach (var control in new Control[] { last, bar })
        {
            control.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Assert.True(control.IsEffectivelyVisible);
            bottoms.Add((control.GetType().Name, control.TranslatePoint(new Point(0, control.Bounds.Height), window)!.Value.Y));
        }

        // Before the assertions, so a failing layout still leaves its picture behind.
        using (var frame = window.CaptureRenderedFrame()!)
        {
            frame.SavePng(Path.Join(Shots(), $"stammdaten-karten-{width}x{height}.png"));
        }

        Assert.All(bottoms, b => Assert.True(b.Bottom <= mapTop, $"{b.Name} ends at {b.Bottom}, under the map at {mapTop}."));
        Assert.True(map.Bounds.Height >= 150, $"The map is {map.Bounds.Height}px high.");
    }

    [AvaloniaFact]
    public void Without_a_pack_the_Karten_section_says_so_and_shows_no_map()
    {
        using var http = new HttpClient();
        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles(), new RegionPackLibrary(http, Path.Join(_dir, "none")));
        vm.SelectedSection = vm.Sections.OfType<MapPacksSection>().Single();
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NoMapPacksHint").IsEffectivelyVisible);
        Assert.False(view.GetVisualDescendants().OfType<MapCanvasControl>().Single().IsEffectivelyVisible);

        // The lists take the map's half too.
        Assert.Equal(2, Grid.GetRowSpan(view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "MapPacksScroll")));
    }

    private sealed class EmptyProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty;

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, []);

        public void Write(string path, MasterDataSet set)
        {
        }
    }
}
