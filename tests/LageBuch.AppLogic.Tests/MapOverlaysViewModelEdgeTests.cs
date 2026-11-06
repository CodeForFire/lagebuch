using System.Text;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Map;

namespace LageBuch.AppLogic.Tests;

public sealed class MapOverlaysViewModelEdgeTests
{
    private const string Point = """{"type": "Feature", "properties": {"name": "p"}, "geometry": {"type": "Point", "coordinates": [11.0, 48.0]}}""";

    private static readonly DateTimeOffset T0 = new(2026, 3, 14, 10, 20, 0, TimeSpan.FromHours(1));
    private static readonly SessionOperator Peer = new("Bauer", "FFB 3");

    private readonly FakeStore _store = new();
    private readonly LocalIncidentSession _session;
    private readonly List<GeoBounds> _shown = [];
    private readonly List<(string Title, Action Confirm)> _confirms = [];
    private int _changes;

    public MapOverlaysViewModelEdgeTests()
    {
        _session = TestSession.StartNew(
            _store,
            new FixedClock(T0),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
    }

    private MapOverlaysViewModel Open(bool confirm = true)
    {
        var vm = new MapOverlaysViewModel(
            _session,
            () => _changes++,
            null,
            confirm ? (title, _, onConfirmed) => _confirms.Add((title, onConfirmed)) : null,
            new FixedClock(T0),
            _shown.Add);
        _session.Changed += vm.Refresh;
        return vm;
    }

    // An Ebene as a peer's import leaves it on the host: recorded, its bytes not (yet) there.
    private Guid RecordWithoutBytes(string name = "p")
    {
        var fileId = Guid.NewGuid();
        _session.Incident.AddMapOverlay(
            new FixedClock(T0), Peer, Guid.NewGuid(), fileId, $"{name}.geojson", "application/geo+json", 10, new MapOverlaySummary(name, 1, null, null));
        return fileId;
    }

    private async Task Upload(Guid fileId, string name, string json)
    {
        using var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await _session.SaveFileStreamAsync(IncidentFile.StorageFileName(fileId, $"{name}.geojson"), body);
    }

    [Fact]
    public async Task A_fetch_that_saw_no_file_while_the_upload_was_announced_looks_again()
    {
        var vm = Open();
        var gate = new TaskCompletionSource();
        _store.ReadGate = gate.Task;
        var fileId = RecordWithoutBytes();
        _session.SaveExternalChange();

        // The fetch has looked and found nothing; the upload lands and is announced before it answers.
        await Upload(fileId, "p", Point);
        _session.SaveExternalChange();
        _store.ReadGate = null;
        gate.SetResult();
        await vm.SettledAsync();

        var item = Assert.Single(vm.Items);
        Assert.Null(item.LoadError);
        Assert.False(item.IsMissing);
        Assert.Equal("p", Assert.Single(vm.VisibleShapes).Label);
    }

    [Fact]
    public async Task A_change_elsewhere_in_the_Einsatz_neither_rebuilds_the_list_nor_redraws_the_map()
    {
        var vm = Open();
        await _session.AddMapOverlayAsync("p.geojson", "application/geo+json", Encoding.UTF8.GetBytes(Point), new MapOverlaySummary("p", 1, null, null));
        await vm.SettledAsync();
        var listChanges = 0;
        var redraws = 0;
        vm.Items.CollectionChanged += (_, _) => listChanges++;
        vm.PropertyChanged += (_, e) => redraws += e.PropertyName == nameof(MapOverlaysViewModel.VisibleShapes) ? 1 : 0;

        _session.AddJournalEntry(EtbDirection.Internal, "Lage unverändert");

        Assert.Equal(0, listChanges);
        Assert.Equal(0, redraws);
    }

    [Fact]
    public async Task The_list_says_whether_it_has_Ebenen_and_announces_it()
    {
        var vm = Open();
        var announced = new List<string?>();
        vm.PropertyChanged += (_, e) => announced.Add(e.PropertyName);
        Assert.False(vm.HasItems);

        await _session.AddMapOverlayAsync("p.geojson", "application/geo+json", Encoding.UTF8.GetBytes(Point), new MapOverlaySummary("p", 1, null, null));

        Assert.True(vm.HasItems);
        Assert.Contains(nameof(MapOverlaysViewModel.HasItems), announced);
    }

    [Fact]
    public async Task A_file_that_will_never_parse_says_why_and_is_not_fetched_again()
    {
        var vm = Open();
        var fileId = RecordWithoutBytes();
        await Upload(fileId, "p", "{ kaputt");
        _session.SaveExternalChange();
        await vm.SettledAsync();

        var item = Assert.Single(vm.Items);
        Assert.Equal("Ebene konnte nicht gelesen werden: Die Datei ist kein gültiges GeoJSON.", item.LoadError);
        Assert.False(item.IsMissing);
        Assert.False(item.IsLoading);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_costs_the_row_and_is_tried_again()
    {
        _store.ReadFailure = new IOException("Datenträger nicht bereit.");
        RecordWithoutBytes();
        var vm = Open();
        await vm.SettledAsync();

        var item = Assert.Single(vm.Items);
        Assert.Equal("Ebene konnte nicht gelesen werden: Datenträger nicht bereit.", item.LoadError);
        Assert.True(item.IsMissing);
    }

    [Fact]
    public async Task A_row_is_loading_until_its_file_has_been_read_and_says_so()
    {
        var gate = new TaskCompletionSource();
        _store.ReadGate = gate.Task;
        var fileId = RecordWithoutBytes();
        await Upload(fileId, "p", Point);
        var vm = Open();
        var item = Assert.Single(vm.Items);
        var announced = new List<string?>();
        item.PropertyChanged += (_, e) => announced.Add(e.PropertyName);
        Assert.True(item.IsLoading);

        gate.SetResult();
        await vm.SettledAsync();

        Assert.False(item.IsLoading);
        Assert.NotNull(item.Data);
        Assert.Contains(nameof(MapOverlayItem.Data), announced);
        Assert.Contains(nameof(MapOverlayItem.IsLoading), announced);
    }

    [Fact]
    public async Task Show_switches_an_Ebene_on_and_brings_it_into_view_once_it_is_read()
    {
        var vm = Open();
        var fileId = RecordWithoutBytes();
        _session.SaveExternalChange();
        RecordWithoutBytes("q");
        _session.SaveExternalChange();
        await vm.SettledAsync();
        var older = vm.Items[0];
        Assert.False(older.IsVisible);

        older.ShowCommand.Execute(null);
        Assert.True(older.IsVisible);
        Assert.Empty(_shown);

        await Upload(fileId, "p", Point);
        _session.SaveExternalChange();
        await vm.SettledAsync();
        older.ShowCommand.Execute(null);

        Assert.Equal(48.0, Assert.Single(_shown).North, precision: 6);
    }

    [Fact]
    public async Task An_import_brings_the_new_Ebene_into_view_and_reports_the_change()
    {
        var dialogs = new PickOnce(WriteTemp(Point));
        var vm = new MapOverlaysViewModel(_session, () => _changes++, dialogs, null, new FixedClock(T0), _shown.Add);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(11.0, Assert.Single(_shown).West, precision: 6);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public async Task A_file_over_the_cap_is_refused_by_its_size_and_one_at_the_cap_is_read()
    {
        var over = WriteTemp(string.Empty, IncidentFile.MaxSizeBytes + 1);
        var at = WriteTemp(string.Empty, IncidentFile.MaxSizeBytes);

        var overVm = new MapOverlaysViewModel(_session, () => { }, new PickOnce(over), null, new FixedClock(T0), _ => { });
        await overVm.ImportCommand.ExecuteAsync(null);
        var atVm = new MapOverlaysViewModel(_session, () => { }, new PickOnce(at), null, new FixedClock(T0), _ => { });
        await atVm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(IncidentFile.TooLargeMessage, overVm.Error);
        Assert.Equal("Die Datei ist kein gültiges GeoJSON.", atVm.Error);
    }

    [Fact]
    public async Task Without_a_confirmation_dialog_an_Ebene_is_removed_at_once_and_the_change_reported()
    {
        var vm = Open(confirm: false);
        await _session.AddMapOverlayAsync("p.geojson", "application/geo+json", Encoding.UTF8.GetBytes(Point), new MapOverlaySummary("p", 1, null, null));
        _changes = 0;

        vm.Items[0].RemoveCommand.Execute(null);
        await vm.SettledAsync();

        Assert.Empty(_session.Incident.MapOverlays);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public async Task A_closed_Einsatz_offers_no_removal()
    {
        var vm = Open();
        await _session.AddMapOverlayAsync("p.geojson", "application/geo+json", Encoding.UTF8.GetBytes(Point), new MapOverlaySummary("p", 1, null, null));
        _session.Close();

        vm.Items[0].RemoveCommand.Execute(null);

        Assert.Empty(_confirms);
        Assert.Single(_session.Incident.MapOverlays);
    }

    [Fact]
    public async Task A_removal_whose_bytes_cannot_be_wiped_says_so()
    {
        var vm = Open();
        await _session.AddMapOverlayAsync("p.geojson", "application/geo+json", Encoding.UTF8.GetBytes(Point), new MapOverlaySummary("p", 1, null, null));
        _store.DeleteFailure = new IOException("Datei gesperrt.");

        vm.Items[0].RemoveCommand.Execute(null);
        _confirms.Single().Confirm();

        Assert.Equal("Kartenebene nicht entfernt: Datei gesperrt.", vm.Error);
    }

    private static string WriteTemp(string content, long? length = null)
    {
        var path = Path.Join(Directory.CreateTempSubdirectory("lagebuch-overlay-").FullName, "ebene.geojson");
        using (var file = File.Create(path))
        {
            file.Write(Encoding.UTF8.GetBytes(content));
            if (length is { } l)
            {
                // Sparse: the size is what is checked, without writing 25 MB.
                file.SetLength(l);
            }
        }

        return path;
    }

    private sealed class PickOnce(string path) : Services.IFileDialogService
    {
        private string? _path = path;

        public Task<string?> PickMapOverlayAsync()
        {
            var next = _path;
            _path = null;
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
