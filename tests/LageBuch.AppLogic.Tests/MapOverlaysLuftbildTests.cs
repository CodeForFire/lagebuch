using System.Net;
using System.Text;
using System.Web;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

/// <summary>LUFTBILD LADEN: the visible section's Luftbild and Flurstücksgrenzen become two Ebenen.</summary>
public sealed class MapOverlaysLuftbildTests : IDisposable
{
    private const string Sperrgebiet = """
        {"type": "Feature", "properties": {"name": "Sperrgebiet"},
         "geometry": {"type": "Polygon", "coordinates": [[[11.25, 48.17], [11.26, 48.17], [11.26, 48.18], [11.25, 48.17]]]}}
        """;

    private static readonly DateTimeOffset T0 = new(2026, 3, 14, 10, 20, 0, TimeSpan.FromHours(1));

    // About 1.2 by 0.6 km around the Fürstenfeldbrucker Klosterkirche.
    private static readonly GeoBox Visible = new(11.2475, 48.1765, 11.2635, 48.1805);

    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    /// <summary>Answers like the LDBV: a JPEG for the DOP, a PNG for the Parzellarkarte, at the size asked for.</summary>
    private sealed class LdbvHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        public List<string> Layers { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = HttpUtility.ParseQueryString(Assert.IsType<Uri>(request.RequestUri).Query);
            var layer = query["LAYERS"] ?? string.Empty;
            Layers.Add(layer);
            if (respond is not null)
            {
                return Task.FromResult(respond(request));
            }

            var (width, height) = (int.Parse(query["WIDTH"] ?? "0", System.Globalization.CultureInfo.InvariantCulture), int.Parse(query["HEIGHT"] ?? "0", System.Globalization.CultureInfo.InvariantCulture));
            var (bytes, type) = layer == "by_dop40c" ? (TestImages.Jpeg(width, height), "image/jpeg") : (TestImages.Png(width, height), "image/png");
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>Waits for the caller to give up.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private LdbvImageryClient Client(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        _clients.Add(http);
        return new LdbvImageryClient(http);
    }

    private static LocalIncidentSession NewSession() => TestSession.StartNew(
        new FakeStore(),
        new FixedClock(T0),
        new SessionOperator("Müller", "FFB 12/1"),
        "/x.lagebuch",
        Array.Empty<(string, bool)>(),
        Array.Empty<(string, bool)>());

    private static (MapOverlaysViewModel Vm, Func<int> Changes) Open(LocalIncidentSession session, LdbvImageryClient? client)
    {
        var changes = 0;
        var vm = new MapOverlaysViewModel(session, () => changes++, null, null, new FixedClock(T0), _ => { }, luftbild: client);
        session.Changed += vm.Refresh;
        return (vm, () => changes);
    }

    [Fact]
    public async Task The_visible_section_becomes_a_Luftbild_and_its_Flurstuecksgrenzen_both_shown()
    {
        var session = NewSession();
        var handler = new LdbvHandler();
        var (vm, changes) = Open(session, Client(handler));

        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        await vm.SettledAsync();

        Assert.Null(vm.Error);
        Assert.Null(vm.LuftbildStatus);
        Assert.Equal(["by_dop40c", "by_alkis_parzellarkarte_umr_gelb"], handler.Layers);
        var area = Assert.IsType<LuftbildArea>(LuftbildArea.For(Visible));
        var overlays = session.Incident.MapOverlays;
        Assert.Equal(["Luftbild (LDBV)", "Flurstücksgrenzen (LDBV)"], overlays.Select(o => o.Name));
        Assert.All(overlays, o => Assert.Equal(MapOverlayKind.Raster, o.Kind));
        Assert.All(overlays, o => Assert.Equal(area.Bounds, o.Bounds));
        Assert.All(overlays, o => Assert.Equal(LdbvImageryClient.Attribution, o.Attribution));
        Assert.Equal(["image/jpeg", "image/png"], overlays.Select(o => session.Incident.Files.Single(f => f.Id == o.FileId).ContentType));
        Assert.Equal([true, true], vm.Items.Select(i => i.IsVisible));
        Assert.Equal([RasterFormat.Jpeg, RasterFormat.Png], vm.VisibleRasters.Select(r => r.Info.Format));
        Assert.Equal(LdbvImageryClient.Attribution, vm.RasterAttribution);
        Assert.Equal(1, changes());
    }

    [Fact]
    public async Task A_section_outside_Bavaria_asks_nothing_and_says_why()
    {
        var session = NewSession();
        var handler = new LdbvHandler();
        var (vm, _) = Open(session, Client(handler));

        await vm.LoadLuftbildCommand.ExecuteAsync(new GeoBox(13.38, 52.51, 13.42, 52.53));

        Assert.Equal("Luftbilder gibt es nur für Bayern.", vm.Error);
        Assert.Empty(handler.Layers);
        Assert.Empty(session.Incident.MapOverlays);
    }

    [Fact]
    public async Task A_refusal_stores_nothing_and_names_what_failed()
    {
        var session = NewSession();
        var (vm, changes) = Open(session, Client(new LdbvHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);

        Assert.Equal("Luftbild nicht geladen: Der Kartendienst der Vermessungsverwaltung hat mit HTTP 503 geantwortet.", vm.Error);
        Assert.Null(vm.LuftbildStatus);
        Assert.False(vm.IsLoadingLuftbild);
        Assert.Empty(session.Incident.MapOverlays);
        Assert.Equal(0, changes());
    }

    [Fact]
    public async Task When_only_the_Flurstuecksgrenzen_fail_nothing_is_stored()
    {
        var session = NewSession();
        var handler = new LdbvHandler(r => HttpUtility.ParseQueryString(Assert.IsType<Uri>(r.RequestUri).Query)["LAYERS"] == "by_dop40c"
            ? Jpeg(r)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<ServiceExceptionReport/>", Encoding.UTF8, "text/xml") });
        var (vm, _) = Open(session, Client(handler));

        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);

        Assert.Equal("Flurstücksgrenzen nicht geladen: Der Kartendienst der Vermessungsverwaltung hat die Anfrage abgelehnt.", vm.Error);
        Assert.Empty(session.Incident.MapOverlays);
    }

    [Fact]
    public async Task A_cancelled_download_stores_nothing_and_says_so()
    {
        var session = NewSession();
        var handler = new HangingHandler();
        var (vm, _) = Open(session, Client(handler));

        var load = vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        await handler.Started.Task;
        Assert.True(vm.IsLoadingLuftbild);
        Assert.Equal("Luftbild wird geladen …", vm.LuftbildStatus);
        vm.LoadLuftbildCancelCommand.Execute(null);
        await load;

        Assert.Equal("Download abgebrochen.", vm.LuftbildStatus);
        Assert.Null(vm.Error);
        Assert.False(vm.IsLoadingLuftbild);
        Assert.Empty(session.Incident.MapOverlays);
    }

    [Fact]
    public void Without_a_client_or_on_a_closed_Einsatz_there_is_no_Luftbild_to_load()
    {
        var session = NewSession();
        var (without, _) = Open(session, null);
        Assert.False(without.CanLoadLuftbild);

        var closed = NewSession();
        closed.Close();
        var (readOnly, _) = Open(closed, Client(new LdbvHandler()));
        Assert.False(readOnly.CanLoadLuftbild);
        Assert.False(readOnly.LoadLuftbildCommand.CanExecute(Visible));

        var (open, _) = Open(NewSession(), Client(new LdbvHandler()));
        Assert.True(open.CanLoadLuftbild);
    }

    [Fact]
    public async Task A_Luftbild_leaves_a_shown_GeoJSON_Ebene_shown_and_a_new_one_leaves_the_Luftbild()
    {
        var session = NewSession();
        var (vm, _) = Open(session, Client(new LdbvHandler()));
        await session.AddMapOverlayAsync("sperrgebiet.geojson", "application/geo+json", Encoding.UTF8.GetBytes(Sperrgebiet), new MapOverlaySummary("alt", 1, null, null));

        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        await session.AddMapOverlayAsync("neu.geojson", "application/geo+json", Encoding.UTF8.GetBytes(Sperrgebiet), new MapOverlaySummary("neu", 1, null, null));
        await vm.SettledAsync();

        Assert.Equal([false, true, true, true], vm.Items.Select(i => i.IsVisible));
        Assert.Equal(2, vm.VisibleRasters.Count);
        Assert.Single(vm.VisibleShapes);
    }

    [Fact]
    public async Task Hiding_both_Ebenen_takes_the_pictures_and_the_attribution_off_the_map()
    {
        var session = NewSession();
        var (vm, _) = Open(session, Client(new LdbvHandler()));
        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        await vm.SettledAsync();

        vm.Items[0].IsVisible = false;
        Assert.Equal(RasterFormat.Png, Assert.Single(vm.VisibleRasters).Info.Format);
        vm.Items[1].IsVisible = false;

        Assert.Empty(vm.VisibleRasters);
        Assert.Null(vm.RasterAttribution);
    }

    [Fact]
    public async Task A_newer_Luftbild_s_photo_lies_under_an_older_one_s_Flurstuecksgrenzen()
    {
        var session = NewSession();
        var (vm, _) = Open(session, Client(new LdbvHandler()));
        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        await vm.LoadLuftbildCommand.ExecuteAsync(new GeoBox(11.2635, 48.1765, 11.2795, 48.1805));
        await vm.SettledAsync();

        Assert.Equal(4, vm.Items.Count);
        Assert.Equal([RasterFormat.Jpeg, RasterFormat.Jpeg, RasterFormat.Png, RasterFormat.Png], vm.VisibleRasters.Select(r => r.Info.Format));
        Assert.Equal(LdbvImageryClient.Attribution, vm.RasterAttribution);
    }

    [Fact]
    public async Task Leaving_cancels_a_download_under_way_and_waits_for_it_to_end()
    {
        var session = NewSession();
        var handler = new HangingHandler();
        var (vm, _) = Open(session, Client(handler));
        var load = vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        await handler.Started.Task;

        await vm.StopLuftbildAsync();

        Assert.True(load.IsCompleted);
        Assert.False(vm.IsLoadingLuftbild);
        Assert.Empty(session.Incident.MapOverlays);
    }

    [Fact]
    public async Task Stopping_with_no_download_under_way_does_nothing()
    {
        var (vm, _) = Open(NewSession(), Client(new LdbvHandler()));

        await vm.StopLuftbildAsync();

        Assert.False(vm.IsLoadingLuftbild);
        Assert.Null(vm.LuftbildStatus);
    }

    [Fact]
    public async Task Without_room_for_both_Ebenen_nothing_is_downloaded_or_stored()
    {
        var session = NewSession();
        for (var i = 0; i < Incident.MaxRasterMapOverlays - 1; i++)
        {
            await session.AddMapOverlayAsync(
                "luftbild.png", "image/png", TestImages.Png(10, 10), new MapOverlaySummary("alt", 1, null, null, MapOverlayKind.Raster, Visible));
        }

        var handler = new LdbvHandler();
        var (vm, _) = Open(session, Client(handler));

        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);

        Assert.Equal($"Kein Platz für ein weiteres Luftbild: Ein Einsatz hat höchstens {Incident.MaxRasterMapOverlays} Luftbild-Ebenen. Ältere entfernen.", vm.Error);
        Assert.Empty(handler.Layers);
        Assert.Equal(Incident.MaxRasterMapOverlays - 1, session.Incident.MapOverlays.Count);
    }

    [Fact]
    public async Task A_removed_Ebene_s_picture_is_not_kept_in_memory()
    {
        var session = NewSession();
        var (vm, _) = Open(session, Client(new LdbvHandler()));
        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        await vm.SettledAsync();
        Assert.Equal(2, vm.LoadedFileCount);

        await session.RemoveMapOverlayAsync(session.Incident.MapOverlays[0].Id);
        await vm.SettledAsync();

        Assert.Equal(1, vm.LoadedFileCount);
    }

    [Fact]
    public async Task A_picture_the_map_could_not_draw_says_so_on_its_row()
    {
        var session = NewSession();
        var (vm, _) = Open(session, Client(new LdbvHandler()));
        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        await vm.SettledAsync();

        vm.RasterFailedCommand.Execute(vm.Items[0].FileId);

        Assert.Equal("Ebene konnte nicht gelesen werden: Das Bild lässt sich nicht darstellen.", vm.Items[0].LoadError);
        Assert.Null(vm.Items[1].LoadError);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Reopening_an_Einsatz_shows_its_Luftbilder_and_can_centre_on_one()
    {
        var session = NewSession();
        var (vm, _) = Open(session, Client(new LdbvHandler()));
        await vm.LoadLuftbildCommand.ExecuteAsync(Visible);
        GeoBounds? shown = null;

        var reopened = new MapOverlaysViewModel(session, () => { }, null, null, new FixedClock(T0), b => shown = b);
        await reopened.SettledAsync();

        Assert.Equal([true, true], reopened.Items.Select(i => i.IsVisible));
        Assert.All(reopened.Items, i => Assert.True(i.CanShow));
        reopened.Items[0].ShowCommand.Execute(null);
        var bounds = Assert.IsType<LuftbildArea>(LuftbildArea.For(Visible)).Bounds;
        Assert.Equal(new GeoBounds(bounds.South, bounds.West, bounds.North, bounds.East), shown);
    }

    [Fact]
    public async Task A_raster_Ebene_whose_file_is_no_picture_says_so_on_its_row()
    {
        var session = NewSession();
        await session.AddMapOverlayAsync(
            "luftbild.png", "image/png", Encoding.UTF8.GetBytes(Sperrgebiet), new MapOverlaySummary("Luftbild", 1, null, null, MapOverlayKind.Raster, Visible));

        var (vm, _) = Open(session, null);
        await vm.SettledAsync();

        var item = Assert.Single(vm.Items);
        Assert.Equal("Ebene konnte nicht gelesen werden: Die Datei ist kein JPEG- oder PNG-Bild.", item.LoadError);
        Assert.False(item.CanShow);
        Assert.Empty(vm.VisibleRasters);
    }

    [Fact]
    public async Task A_picture_too_large_to_draw_is_refused_before_anything_decodes_it()
    {
        var session = NewSession();
        await session.AddMapOverlayAsync(
            "luftbild.png", "image/png", TestImages.Png(60_000, 60_000), new MapOverlaySummary("Luftbild", 1, null, null, MapOverlayKind.Raster, Visible));

        var (vm, _) = Open(session, null);
        await vm.SettledAsync();

        Assert.Equal("Ebene konnte nicht gelesen werden: Das Bild ist zu groß für die Karte.", Assert.Single(vm.Items).LoadError);
        Assert.Empty(vm.VisibleRasters);
    }

    [Fact]
    public async Task On_opening_an_Einsatz_only_its_newest_Luftbild_and_Flurstuecksgrenzen_are_shown()
    {
        var session = NewSession();
        await AddPair(session, 1000, 1000);
        await AddPair(session, 1000, 1000);

        var (vm, _) = Open(session, null);
        await vm.SettledAsync();

        Assert.Equal([false, false, true, true], vm.Items.Select(i => i.IsVisible));
        Assert.Equal(2, vm.VisibleRasters.Count);
    }

    [Fact]
    public async Task Pictures_past_the_memory_budget_stay_off_the_map_and_say_so()
    {
        var session = NewSession();
        await AddPair(session, 4096, 4096);
        await AddPair(session, 4096, 4096);
        var (vm, _) = Open(session, null);
        await vm.SettledAsync();
        Assert.Null(vm.RasterBudgetHint);

        // The older pair switched on as well: the newer pair fills the budget, so the older stays off.
        vm.Items[0].IsVisible = true;
        vm.Items[1].IsVisible = true;

        Assert.Equal(2, vm.VisibleRasters.Count);
        Assert.All(vm.VisibleRasters, r => Assert.Contains(r.FileId, vm.Items.Skip(2).Select(i => i.FileId)));
        Assert.Equal("Nicht alle eingeblendeten Luftbilder passen in den Speicher. Ältere ausblenden.", vm.RasterBudgetHint);
        Assert.True(MapOverlaysViewModel.MaxVisibleRasterBytes >= 2L * 4096 * 4096 * 4);

        vm.Items[0].IsVisible = false;
        vm.Items[1].IsVisible = false;
        Assert.Null(vm.RasterBudgetHint);
    }

    [Fact]
    public async Task A_hostile_file_of_many_pictures_never_lays_more_than_the_budget_on_the_map()
    {
        var session = NewSession();
        for (var i = 0; i < 5; i++)
        {
            await AddPair(session, 4096, 4096);
        }

        var (vm, _) = Open(session, null);
        await vm.SettledAsync();
        foreach (var item in vm.Items)
        {
            item.IsVisible = true;
        }

        var bytes = vm.VisibleRasters.Sum(r => 4L * r.Info.Width * r.Info.Height);
        Assert.InRange(bytes, 1, MapOverlaysViewModel.MaxVisibleRasterBytes);
        Assert.NotNull(vm.RasterBudgetHint);
    }

    [Fact]
    public void A_picture_is_drawn_up_to_what_LUFTBILD_LADEN_asks_for_and_no_larger()
    {
        Assert.True(RasterImage.TryRead(Guid.NewGuid(), TestImages.Png(LuftbildArea.MaxPixels, LuftbildArea.MaxPixels), Visible, out _, out _));
        Assert.False(RasterImage.TryRead(Guid.NewGuid(), TestImages.Png(LuftbildArea.MaxPixels + 1, 10), Visible, out _, out var error));
        Assert.Equal("Das Bild ist zu groß für die Karte.", error);
    }

    private static async Task AddPair(LocalIncidentSession session, int width, int height)
    {
        await session.AddMapOverlayAsync(
            "luftbild.jpg", "image/jpeg", TestImages.Jpeg(width, height), new MapOverlaySummary("Luftbild (LDBV)", 1, null, null, MapOverlayKind.Raster, Visible, LdbvImageryClient.Attribution));
        await session.AddMapOverlayAsync(
            "flurstuecke.png", "image/png", TestImages.Png(width, height), new MapOverlaySummary("Flurstücksgrenzen (LDBV)", 1, null, null, MapOverlayKind.Raster, Visible, LdbvImageryClient.Attribution));
    }

    private static HttpResponseMessage Jpeg(HttpRequestMessage request)
    {
        var query = HttpUtility.ParseQueryString(Assert.IsType<Uri>(request.RequestUri).Query);
        var content = new ByteArrayContent(TestImages.Jpeg(
            int.Parse(query["WIDTH"] ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(query["HEIGHT"] ?? "0", System.Globalization.CultureInfo.InvariantCulture)));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
