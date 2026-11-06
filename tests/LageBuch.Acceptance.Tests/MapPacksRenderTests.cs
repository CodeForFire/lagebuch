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

    // Fürstenfeldbruck as FictionalRegionPack installs it (checksum 'a'), or rebuilt since; its
    // neighbours not yet on the device.
    private static string Catalogue(char ffbSha) => $$"""
        {"formatVersion": 2, "packs": [
          {{Entry("ffb", "Landkreis Fürstenfeldbruck", 52_731_904, ffbSha, "2026-11-03T20:15:49Z")}},
          {{Entry("dah", "Landkreis Dachau", 88_080_384, 'c', "2026-10-30T20:15:49Z")}},
          {{Entry("sta", "Landkreis Starnberg", 69_206_016, 'd', "2026-10-30T20:15:49Z")}},
          {{Entry("m", "Landeshauptstadt München", 76_546_048, 'e', "2026-10-30T20:15:49Z")}}]}
        """;

    private static string Entry(string slug, string name, long size, char sha, string builtAt) => $$"""
        {"slug": "{{slug}}", "name": "{{name}}", "version": 1,
          "downloadUrl": "https://example.org/{{slug}}-v1.zip", "sizeBytes": {{size}}, "sha256": "{{new string(sha, 64)}}",
          "bounds": {"west": 10.9527, "south": 48.0607, "east": 11.4532, "north": 48.3237}, "minZoom": 0, "maxZoom": 15,
          "builtAt": "{{builtAt}}", "attribution": "{{FictionalRegionPack.Attribution}}"}
        """;

    // Opens the editor on Karten, which fetches the catalogue through http, and waits for it.
    private async Task<(MasterDataEditorViewModel Vm, MapPacksSection Section, MasterDataEditorView View, Window Window)> Open(
        HttpClient http, int width = 1080, int height = 760, bool withPack = true)
    {
        var root = Path.Join(_dir, "regions");
        if (withPack)
        {
            FictionalRegionPack.Install(root);
        }

        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles(), new RegionPackLibrary(http, root));
        var section = vm.DeviceSections.Single();
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        vm.SelectedSection = section;
        if (section.LoadCatalogCommand.ExecutionTask is { } loading)
        {
            await loading;
        }

        Dispatcher.UIThread.RunJobs();
        return (vm, section, view, window);
    }

    [AvaloniaFact]
    public async Task Karten_has_its_own_rail_group_that_says_what_is_installed_and_what_can_be_updated()
    {
        using var handler = new CatalogueHandler(Catalogue('b'));
        using var http = new HttpClient(handler);
        var (vm, section, view, window) = await Open(http);

        var categories = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "CategoryList");
        Assert.DoesNotContain(vm.Sections, s => s.Title == "Karten");
        Assert.Equal(vm.Sections.Count, categories.ItemCount);
        var device = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DeviceSectionList");
        Assert.True(device.IsEffectivelyVisible);
        Assert.Same(section, device.SelectedItem);
        Assert.Equal("1 installiert · 1 Update", view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MapPacksRailSummary").Text);

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "stammdaten-karten-rail.png"));
    }

    [AvaloniaFact]
    public async Task The_Karten_section_lists_every_pack_in_one_list_and_shows_the_installed_one()
    {
        using var handler = new CatalogueHandler(Catalogue('a'));
        using var http = new HttpClient(handler);
        var (_, section, view, window) = await Open(http);

        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "MapPackList");
        Assert.Equal(4, list.ItemCount);
        Assert.Equal(
            ["Landeshauptstadt München", "Landkreis Dachau", "Landkreis Fürstenfeldbruck", "Landkreis Starnberg"],
            section.Packs.Select(p => p.Name));
        Assert.False(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NoMapPacksHint").IsVisible);
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        Assert.NotNull(map.TileSource);
        Assert.True(map.WheelZoomNeedsControl);
        Assert.Equal("© OpenStreetMap contributors (ODbL).", view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MapAttribution").Text);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Installiert" && t.IsEffectivelyVisible);

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

    // Fails every request the way a device without internet does.
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
    }

    [AvaloniaFact]
    public async Task An_update_with_the_same_version_says_why_it_is_offered()
    {
        using var handler = new CatalogueHandler(Catalogue('b'));
        using var http = new HttpClient(handler);
        var (_, section, view, window) = await Open(http);

        var row = section.Packs.Single(p => p.Slug == "ffb");
        Assert.Equal("AKTUALISIEREN", row.ActionLabel);
        Assert.Equal("Neu gebaut am 03.11.2026 – installiert ist der Stand vom 27.10.2026.", row.UpdateReason);
        var reason = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == row.UpdateReason);
        Assert.True(reason.IsEffectivelyVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Update verfügbar" && t.IsEffectivelyVisible);

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "stammdaten-karten-grund.png"));
    }

    [AvaloniaFact]
    public async Task A_download_shows_its_progress_and_ABBRECHEN_in_its_own_row()
    {
        using var handler = new CatalogueHandler(Catalogue('b'));
        using var http = new HttpClient(handler);
        var (_, section, view, window) = await Open(http);
        var row = section.Packs.Single(p => p.Slug == "dah");
        row.IsDownloading = true;
        row.Progress = 0.41;
        Dispatcher.UIThread.RunJobs();

        var bar = view.GetVisualDescendants().OfType<ProgressBar>().Single(b => b.IsEffectivelyVisible);
        Assert.Same(row, bar.DataContext);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "41 % von 84 MB" && t.IsEffectivelyVisible);
        var cancel = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "ABBRECHEN" && b.IsEffectivelyVisible);
        Assert.Same(row, cancel.DataContext);

        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(Shots(), "stammdaten-karten-download.png"));
    }

    [AvaloniaFact]
    public async Task Offline_the_installed_pack_stays_listed_and_the_section_says_the_map_still_works()
    {
        using var handler = new OfflineHandler();
        using var http = new HttpClient(handler);
        var (_, section, view, _) = await Open(http);

        Assert.Equal("ffb", Assert.Single(section.Packs).Slug);
        var status = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MapPacksStatus");
        Assert.True(status.IsEffectivelyVisible);
        Assert.EndsWith("Installierte Karten funktionieren weiter.", status.Text, StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData(1024, 600)] // a small laptop
    [InlineData(400, 780)] // a phone, where the section opens full width
    public async Task On_a_short_screen_the_whole_list_and_a_download_scroll_into_view_above_the_map(int width, int height)
    {
        var packs = string.Join(",", Enumerable.Range(1, 8).Select(i =>
            Entry($"lk{i}", $"Landkreis Muster {i}", 52_731_904, 'b', "2026-11-03T20:15:49Z")));
        using var handler = new CatalogueHandler($$"""{"formatVersion": 2, "packs": [{{packs}}]}""");
        using var http = new HttpClient(handler);
        var (vm, section, view, window) = await Open(http, width, height);
        vm.IsDetailOpen = true;
        var downloading = section.Packs.Single(p => p.Slug == "lk8");
        downloading.IsDownloading = true;
        downloading.Progress = 0.4;
        Dispatcher.UIThread.RunJobs();

        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        var mapTop = map.TranslatePoint(default, window)!.Value.Y;
        var last = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Landkreis Muster 8");
        var bar = view.GetVisualDescendants().OfType<ProgressBar>().Single(b => ReferenceEquals(b.DataContext, downloading));
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
    public async Task Without_a_pack_the_Karten_section_says_so_and_shows_no_map()
    {
        using var handler = new CatalogueHandler(Catalogue('a'));
        using var http = new HttpClient(handler);
        var (_, section, view, _) = await Open(http, withPack: false);

        Assert.True(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NoMapPacksHint").IsEffectivelyVisible);
        Assert.False(view.GetVisualDescendants().OfType<MapCanvasControl>().Single().IsEffectivelyVisible);
        Assert.Equal("Nichts installiert", section.RailSummary);

        // The list takes the map's half too.
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
