using System.Text;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Map;

namespace LageBuch.AppLogic.Tests;

public sealed class MapOverlaysViewModelTests : IDisposable
{
    // Two quarter hours, 09:00 and 09:15 UTC: 10:00 and 10:15 in Germany in March.
    private const string Forecast = """
        {"type": "FeatureCollection", "features": [
          {"type": "Feature", "properties": {"ftime": "2026-03-14T09:00:00+00:00"}, "geometry": {"type": "Point", "coordinates": [11.0, 48.0]}},
          {"type": "Feature", "properties": {"ftime": "2026-03-14T09:15:00+00:00"}, "geometry": {"type": "Point", "coordinates": [11.1, 48.0]}}
        ]}
        """;

    // One DWD-shaped file: a plume of class 1 at 09:00 UTC, styled the way the DWD styles it.
    private const string DwdForecast = """
        {"type": "FeatureCollection", "crs": {"type": "name", "properties": {"name": "urn:ogc:def:crs:OGC:1.3:CRS84"}}, "features": [
          {"type": "Feature", "properties": {"pixelvalue": 1, "aegl": "stoff", "thr_ppm": "1e-08", "fill": "#228B22", "fill-opaci": 0.5, "ftime": "2026-03-14T09:00:00+00:00"},
           "geometry": {"type": "Polygon", "coordinates": [[[11.0, 48.0], [11.1, 48.0], [11.1, 48.1], [11.0, 48.0]]]}}
        ]}
        """;

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly DateTimeOffset T0 = new(2026, 3, 14, 10, 20, 0, TimeSpan.FromHours(1));

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-overlay-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Sperrgebiet(string name) => $$$"""
        {"type": "Feature", "properties": {"name": "{{{name}}}"},
         "geometry": {"type": "Polygon", "coordinates": [[[11.0, 48.0], [11.1, 48.0], [11.1, 48.1], [11.0, 48.0]]]}}
        """;

    private string Write(string fileName, string content)
    {
        var path = Path.Join(_dir, fileName);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    [Fact]
    public async Task An_imported_file_becomes_an_Ebene_shown_on_the_map_and_recorded()
    {
        var (session, vm, dialogs, changes) = Open();
        dialogs.Next = Write("sperrgebiet.geojson", Sperrgebiet("Sperrgebiet Süd"));

        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();

        Assert.Null(vm.Error);
        var item = Assert.Single(vm.Items);
        Assert.Equal("sperrgebiet", item.Name);
        Assert.True(item.IsVisible);
        Assert.Equal("Sperrgebiet Süd", Assert.Single(vm.VisibleShapes).Label);
        Assert.Null(vm.Error);
        Assert.Equal(EtbDirection.Measurement, session.Incident.Journal[^1].Direction);
        Assert.Equal(1, changes());
    }

    [Fact]
    public async Task A_file_that_is_not_GeoJSON_is_refused_before_anything_is_stored()
    {
        var (session, vm, dialogs, changes) = Open();
        dialogs.Next = Write("kaputt.geojson", "{ nein");

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal("Die Datei ist kein gültiges GeoJSON.", vm.Error);
        Assert.Empty(session.Incident.MapOverlays);
        Assert.Empty(session.Incident.Files);
        Assert.Equal(0, changes());
    }

    [Fact]
    public async Task A_file_of_another_type_is_refused_by_its_name()
    {
        var (session, vm, dialogs, _) = Open();
        dialogs.Next = Write("lageplan.kml", Sperrgebiet("x"));

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal("Nur GeoJSON-Dateien (.geojson, .json) oder ZIP-Archive davon.", vm.Error);
        Assert.Empty(session.Incident.MapOverlays);
    }

    [Fact]
    public async Task A_cancelled_pick_changes_nothing()
    {
        var (session, vm, _, changes) = Open();

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Empty(session.Incident.MapOverlays);
        Assert.Null(vm.Error);
        Assert.Equal(0, changes());
    }

    [Fact]
    public async Task A_newer_Ebene_is_shown_and_hides_the_one_before_it_which_can_be_switched_back_on()
    {
        var (_, vm, dialogs, _) = Open();
        dialogs.Next = Write("alt.geojson", Sperrgebiet("alt"));
        await vm.ImportCommand.ExecuteAsync(null);
        dialogs.Next = Write("neu.geojson", Sperrgebiet("neu"));
        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();

        Assert.Equal([false, true], vm.Items.Select(i => i.IsVisible));
        Assert.Equal("neu", Assert.Single(vm.VisibleShapes).Label);

        vm.Items[0].IsVisible = true;

        Assert.Equal(["alt", "neu"], vm.VisibleShapes.Select(s => s.Label));
    }

    [Fact]
    public async Task On_opening_an_Einsatz_only_its_newest_Ebene_is_shown()
    {
        var (session, _, dialogs, _) = Open();
        foreach (var name in new[] { "eins", "zwei", "drei" })
        {
            var path = Write($"{name}.geojson", Sperrgebiet(name));
            await session.AddMapOverlayAsync($"{name}.geojson", "application/geo+json", await File.ReadAllBytesAsync(path), new MapOverlaySummary(name, 1, null, null));
        }

        var reopened = new MapOverlaysViewModel(session, () => { }, dialogs, null, new FixedClock(T0), _ => { }, Berlin);
        await reopened.SettledAsync();

        Assert.Equal([false, false, true], reopened.Items.Select(i => i.IsVisible));
        Assert.Equal("drei", Assert.Single(reopened.VisibleShapes).Label);
    }

    [Fact]
    public async Task A_forecast_opens_at_the_quarter_hour_that_has_begun()
    {
        var (_, vm, dialogs, _) = Open();
        dialogs.Next = Write("prognose.geojson", Forecast);

        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();

        var item = Assert.Single(vm.Items);
        Assert.Equal(1, item.FrameIndex);
        Assert.Equal(11.1, Assert.Single(vm.VisibleShapes).Parts[0][0].Longitude, precision: 6);

        item.FrameIndex = 0;

        Assert.Equal(11.0, Assert.Single(vm.VisibleShapes).Parts[0][0].Longitude, precision: 6);
    }

    [Fact]
    public async Task The_Zeitleiste_steps_through_the_forecast_and_says_the_time_in_local_time()
    {
        var (_, vm, dialogs, _) = Open();
        dialogs.Next = Write("prognose.geojson", Forecast);
        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();
        var item = Assert.Single(vm.Items);

        Assert.True(item.ShowsTimeline);
        Assert.Equal(1, item.MaxFrameIndex);
        Assert.Equal("Prognose 10:15 Uhr", item.FrameLabel);
        Assert.False(item.NextFrameCommand.CanExecute(null));

        item.PreviousFrameCommand.Execute(null);

        Assert.Equal("Prognose 10:00 Uhr", item.FrameLabel);
        Assert.False(item.PreviousFrameCommand.CanExecute(null));
        Assert.Equal(11.0, Assert.Single(vm.VisibleShapes).Parts[0][0].Longitude, precision: 6);

        item.FramePosition = 0.9;
        Assert.Equal(1, item.FrameIndex);
        item.FramePosition = 99;
        Assert.Equal(1, item.FrameIndex);
    }

    [Fact]
    public async Task A_summer_forecast_reopened_in_winter_still_says_its_summer_hour()
    {
        // The clock reads March (UTC+1); the forecast is for July (UTC+2).
        var (_, vm, dialogs, _) = Open();
        dialogs.Next = Write("sommer.geojson", Forecast.Replace("2026-03-14", "2026-07-01", StringComparison.Ordinal));

        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();

        Assert.Equal("Prognose 11:00 Uhr", Assert.Single(vm.Items).FrameLabel);
    }

    [Fact]
    public async Task The_Zeitleiste_steps_stop_at_either_end()
    {
        var (_, vm, dialogs, _) = Open();
        dialogs.Next = Write("prognose.geojson", Forecast);
        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();
        var item = Assert.Single(vm.Items);

        item.FrameIndex = 0;
        Assert.False(item.PreviousFrameCommand.CanExecute(null));
        Assert.True(item.NextFrameCommand.CanExecute(null));

        item.FrameIndex = item.MaxFrameIndex;
        Assert.True(item.PreviousFrameCommand.CanExecute(null));
        Assert.False(item.NextFrameCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_legend_shows_only_for_a_shown_Ebene_that_has_one()
    {
        var (_, vm, dialogs, _) = Open();
        dialogs.Next = Write("d93_9.geojson", DwdForecast);
        await vm.ImportCommand.ExecuteAsync(null);
        dialogs.Next = Write("sperrgebiet.geojson", Sperrgebiet("x"));
        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();
        var (dwd, plain) = (vm.Items[0], vm.Items[1]);

        Assert.False(dwd.IsVisible);
        Assert.False(dwd.ShowsLegend);
        Assert.True(plain.IsVisible);
        Assert.Empty(plain.Legend);
        Assert.False(plain.ShowsLegend);

        dwd.IsVisible = true;
        Assert.True(dwd.ShowsLegend);
    }

    [Fact]
    public void An_Ebene_still_being_read_has_an_empty_legend()
    {
        var (session, _, _, _) = Open();
        session.Incident.AddMapOverlay(
            new FixedClock(T0), new SessionOperator("Bauer", "FFB 3"), Guid.NewGuid(), Guid.NewGuid(), "p.geojson", "application/geo+json", 10, new MapOverlaySummary("p", 1, null, null));

        var item = new MapOverlaysViewModel(session, () => { }, null, null, new FixedClock(T0), _ => { }, Berlin).Items.Single();

        Assert.Empty(item.Legend);
        Assert.False(item.ShowsLegend);
    }

    [Fact]
    public async Task A_hidden_Ebene_shows_no_Zeitleiste_and_a_plain_one_never_does()
    {
        var (_, vm, dialogs, _) = Open();
        dialogs.Next = Write("prognose.geojson", Forecast);
        await vm.ImportCommand.ExecuteAsync(null);
        dialogs.Next = Write("sperrgebiet.geojson", Sperrgebiet("x"));
        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();

        Assert.False(vm.Items[0].ShowsTimeline);
        Assert.False(vm.Items[1].ShowsTimeline);
        Assert.Null(vm.Items[1].FrameLabel);
    }

    [Fact]
    public async Task A_DWD_forecast_is_named_for_its_first_forecast_time_and_shows_its_legend()
    {
        var (session, vm, dialogs, _) = Open();
        dialogs.Next = Write("d93_9.geojson", DwdForecast);

        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();

        var item = Assert.Single(vm.Items);
        Assert.Equal("Ausbreitungsprognose DWD 14.03.2026 10:00", item.Name);
        Assert.True(item.ShowsLegend);
        Assert.Equal("Stoff > 1e-08 ppm", Assert.Single(item.Legend).Label);
        Assert.Equal("Kartenebene importiert: Ausbreitungsprognose DWD 14.03.2026 10:00 (Stand 10:00 Uhr)", session.Incident.Journal[^1].Text);
    }

    [Fact]
    public async Task Removing_an_Ebene_asks_first_then_takes_it_and_its_file_out()
    {
        var (session, vm, dialogs, _) = Open();
        dialogs.Next = Write("sperrgebiet.geojson", Sperrgebiet("x"));
        await vm.ImportCommand.ExecuteAsync(null);
        await vm.SettledAsync();

        vm.Items[0].RemoveCommand.Execute(null);
        Assert.Equal("Kartenebene entfernen?", dialogs.ConfirmTitle);
        Assert.Single(session.Incident.MapOverlays);

        dialogs.Confirm();

        Assert.Empty(session.Incident.MapOverlays);
        Assert.Empty(session.Incident.Files);
        Assert.Empty(vm.Items);
        Assert.Empty(vm.VisibleShapes);
    }

    [Fact]
    public async Task An_Ebene_whose_file_is_not_there_yet_says_so_and_is_drawn_once_it_arrives()
    {
        // A peer's import as the host sees it: the command has landed, the upload has not.
        var (session, vm, _, _) = Open();
        var bytes = Encoding.UTF8.GetBytes(Sperrgebiet("spät"));
        var fileId = Guid.NewGuid();
        session.Incident.AddMapOverlay(
            new FixedClock(T0), new SessionOperator("Bauer", "FFB 3"), Guid.NewGuid(), fileId, "spaet.geojson", "application/geo+json", bytes.Length, new MapOverlaySummary("spät", 1, null, null));
        session.SaveExternalChange();
        await vm.SettledAsync();

        Assert.Equal("Ebene konnte nicht gelesen werden: Datei ist noch nicht verfügbar.", Assert.Single(vm.Items).LoadError);
        Assert.Empty(vm.VisibleShapes);

        using var upload = new MemoryStream(bytes);
        await session.SaveFileStreamAsync(IncidentFile.StorageFileName(fileId, "spaet.geojson"), upload);
        session.SaveExternalChange();
        await vm.SettledAsync();

        Assert.Null(vm.Items[0].LoadError);
        Assert.Equal("spät", Assert.Single(vm.VisibleShapes).Label);
    }

    [Fact]
    public void A_read_only_Einsatz_offers_no_import()
    {
        var (session, _, dialogs, _) = Open();
        session.Close();
        var vm = new MapOverlaysViewModel(session, () => { }, dialogs, null, new FixedClock(T0), _ => { }, Berlin);

        Assert.False(vm.CanImport);
    }

    private static (LocalIncidentSession Session, MapOverlaysViewModel Vm, OverlayDialogs Dialogs, Func<int> Changes) Open()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(T0),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var changes = 0;
        var dialogs = new OverlayDialogs();
        var vm = new MapOverlaysViewModel(session, () => changes++, dialogs, dialogs.RequestConfirm, new FixedClock(T0), _ => { }, Berlin);
        session.Changed += vm.Refresh;
        return (session, vm, dialogs, () => changes);
    }

    private sealed class OverlayDialogs : IFileDialogService
    {
        private Action? _confirm;

        public string? Next { get; set; }

        public string? ConfirmTitle { get; private set; }

        public void RequestConfirm(string title, string message, Action onConfirmed)
        {
            ConfirmTitle = title;
            _confirm = onConfirmed;
        }

        public void Confirm() => _confirm?.Invoke();

        public Task<string?> PickMapOverlayAsync()
        {
            var next = Next;
            Next = null;
            return Task.FromResult(next);
        }

        public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>(null);

        public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);

        public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);

        public Task OpenFileAsync(string path) => Task.CompletedTask;

        public Task OpenUrlAsync(string url) => Task.CompletedTask;

        public Task OpenMailAsync(string address) => Task.CompletedTask;

        public Task OpenPhoneAsync(string number) => Task.CompletedTask;

        public bool CanShareFiles => false;

        public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
    }
}
