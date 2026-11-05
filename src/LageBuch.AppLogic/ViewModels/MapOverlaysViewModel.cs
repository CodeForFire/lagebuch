using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;
using LageBuch.Domain.Files;
using LageBuch.Domain.Map;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The EBENEN beside the Lagekarte: the GeoJSON files imported into the Einsatz, each switched on
/// or off. Which Ebene is shown, and at which time, belongs to this screen and is not stored, like
/// the HYDRANTEN switch; the Ebenen themselves are the Einsatz's and sync. A new Ebene is shown
/// and the ones before it are hidden, so a newer Ausbreitungsprognose replaces the old one on the
/// map while the old one stays on record.
/// </summary>
public sealed partial class MapOverlaysViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly IFileDialogService? _dialogs;
    private readonly Action<string, string, Action>? _requestConfirm;
    private readonly IClock _clock;
    private readonly Action<GeoBounds> _showBounds;

    // Where the forecast times are read: a DWD file carries them in UTC, and a forecast reopened
    // after the clocks changed must still say the hour it said on the day.
    private readonly TimeZoneInfo _timeZone;

    // Read and parsed once per file and session: the file never changes, only whether it is shown.
    private readonly Dictionary<Guid, Task<(OverlayData? Data, string? Error, bool Parsed)>> _loads = [];

    // Every row's load since the last settle, for a caller that has to wait on the files being read.
    private readonly List<Task> _pending = [];

    // Counts Refresh calls, so a fetch can tell whether the Einsatz changed while it was looking.
    private int _changes;

    public MapOverlaysViewModel(
        IIncidentSession session,
        Action onChanged,
        IFileDialogService? dialogs,
        Action<string, string, Action>? requestConfirm,
        IClock clock,
        Action<GeoBounds> showBounds,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(showBounds);
        _session = session;
        _onChanged = onChanged;
        _dialogs = dialogs;
        _requestConfirm = requestConfirm;
        _clock = clock;
        _showBounds = showBounds;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        Refresh();
    }

    /// <summary>The Ebenen, oldest first, as the Einsatz has them.</summary>
    public ObservableCollection<MapOverlayItem> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    /// <summary>Importing needs an open Einsatz and a picker on this platform.</summary>
    public bool CanImport => !_session.IsReadOnly && _dialogs is not null;

    /// <summary>What the map draws: the current frame of every shown Ebene, the newest on top.</summary>
    [ObservableProperty]
    private IReadOnlyList<OverlayShape> _visibleShapes = [];

    [ObservableProperty]
    private bool _isImporting;

    /// <summary>Why the last import or removal failed, in German.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>
    /// Brings the list in line with the Einsatz, keeping what this screen shows of each Ebene. Runs
    /// on every change of the Einsatz, so the list is only rebuilt when its Ebenen changed.
    /// </summary>
    public void Refresh()
    {
        _changes++;
        var overlays = _session.Incident.MapOverlays;
        if (!overlays.Select(o => o.Id).SequenceEqual(Items.Select(i => i.Id)))
        {
            Rebuild(overlays);
        }

        // A file not there yet -- a peer's upload still under way -- is looked for again on every
        // change, the upload's own announcement included.
        foreach (var item in Items.Where(i => i.IsMissing))
        {
            Track(LoadAsync(item));
        }
    }

    private void Rebuild(IReadOnlyList<MapOverlay> overlays)
    {
        var known = Items.ToDictionary(i => i.Id);
        var first = Items.Count == 0;
        Items.Clear();
        foreach (var overlay in overlays)
        {
            if (!known.TryGetValue(overlay.Id, out var item))
            {
                item = new MapOverlayItem(overlay, this, _timeZone);
                Track(LoadAsync(item));
            }

            Items.Add(item);
        }

        // The newest Ebene is shown when it arrives, here or from another device, and hides the
        // ones before it. On opening the Einsatz only the newest is shown.
        if (Items.Count > 0 && (first || !known.ContainsKey(Items[^1].Id)))
        {
            foreach (var item in Items)
            {
                item.IsVisible = ReferenceEquals(item, Items[^1]);
            }
        }

        OnPropertyChanged(nameof(HasItems));
        UpdateVisibleShapes();
    }

    private void Track(Task load)
    {
        // Stryker disable once Statement : only keeps the list short; SettledAsync waits on finished loads at no cost.
        _pending.RemoveAll(t => t.IsCompleted);
        _pending.Add(load);
    }

    /// <summary>Completes when every Ebene's file has been looked for. Tests wait on it; the view never needs to.</summary>
    internal async Task SettledAsync()
    {
        while (_pending.Count > 0)
        {
            var pending = _pending.ToList();
            _pending.Clear();
            await Task.WhenAll(pending);
        }
    }

    // The map is redrawn only when what it shows changed: the shapes are the parser's own
    // instances, so the same frame yields the same references.
    internal void UpdateVisibleShapes()
    {
        var shapes = Items.Where(i => i.IsVisible).SelectMany(i => i.CurrentShapes).ToList();
        if (!shapes.SequenceEqual(VisibleShapes, ReferenceEqualityComparer.Instance))
        {
            VisibleShapes = shapes;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Picker, read and upload failures are heterogeneous; every one surfaces in Error, as FilesViewModel's do.")]
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!CanImport || _dialogs is null || IsImporting)
        {
            return;
        }

        Error = null;
        string? path;
        try
        {
            path = await _dialogs.PickMapOverlayAsync();
        }
        catch (Exception ex)
        {
            Error = $"Kartenebene nicht übernommen: {ex.Message}";
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        IsImporting = true;
        try
        {
            Error = await ImportFileAsync(path);
        }
        catch (Exception ex)
        {
            Error = $"Kartenebene nicht übernommen: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    // Null when the file became an Ebene, else why not. The file is read and checked before
    // anything is stored, so only a file the Lagekarte can draw joins the Einsatz.
    private async Task<string?> ImportFileAsync(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!IncidentFile.MapOverlayMimeTypesByExtension.TryGetValue(Path.GetExtension(fileName), out var contentType))
        {
            return "Nur GeoJSON-Dateien (.geojson, .json) oder ZIP-Archive davon.";
        }

        if (new FileInfo(path).Length > IncidentFile.MaxSizeBytes)
        {
            return IncidentFile.TooLargeMessage;
        }

        var bytes = await File.ReadAllBytesAsync(path);
        var (data, error) = await Task.Run(() => Parse(bytes));
        if (data is null)
        {
            return error;
        }

        await _session.AddMapOverlayAsync(fileName, contentType, bytes, data.Summary(NameFor(data, fileName)));
        _onChanged();
        _showBounds(data.Bounds);
        return null;
    }

    // A DWD Ausbreitungsprognose is named for when it starts, in the Einsatz's time: its file name
    // is a run id nobody can read. Anything else keeps its file's name.
    private string NameFor(OverlayData data, string fileName) =>
        data.IsDwdForecast && data.Frames[0].Time is { } first
            ? $"Ausbreitungsprognose DWD {Formatting.Timestamp(TimeZoneInfo.ConvertTime(first, _timeZone))}"
            : Path.GetFileNameWithoutExtension(fileName);

    internal void ConfirmRemove(MapOverlayItem item)
    {
        if (_session.IsReadOnly)
        {
            return;
        }

        void Remove() => _ = RemoveAsync(item.Id);
        if (_requestConfirm is null)
        {
            Remove();
            return;
        }

        _requestConfirm("Kartenebene entfernen?", $"„{item.Name}“ von der Lagekarte und aus dem Einsatz entfernen. Fortfahren?", Remove);
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A refused or failed removal surfaces in Error, as FilesViewModel.RemoveFileAsync's does.")]
    private async Task RemoveAsync(Guid overlayId)
    {
        Error = null;
        try
        {
            await _session.RemoveMapOverlayAsync(overlayId);
            _onChanged();
        }
        catch (Exception ex)
        {
            Error = $"Kartenebene nicht entfernt: {ex.Message}";
        }
    }

    internal void ShowBounds(MapOverlayItem item)
    {
        if (item.Data is { } data)
        {
            _showBounds(data.Bounds);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A file that cannot be fetched costs its own Ebene: the failure is shown on that row, never thrown into the map.")]
    private async Task LoadAsync(MapOverlayItem item)
    {
        var fileId = item.FileId;
        bool missing;
        bool again;
        (OverlayData? Data, string? Error, bool Parsed) result;
        do
        {
            var changesAtStart = _changes;
            if (!_loads.TryGetValue(fileId, out var load))
            {
                load = Fetch(fileId);
                _loads[fileId] = load;
            }

            try
            {
                result = await load;
            }
            catch (Exception ex)
            {
                result = (null, ex.Message, false);
            }

            // A file that could not be fetched is tried again; one that was read, or will never parse, stays.
            missing = result.Data is null && !result.Parsed;
            if (missing && _loads.TryGetValue(fileId, out var cached) && ReferenceEquals(cached, load))
            {
                _loads.Remove(fileId);
            }

            // A change that arrived while this was looking may be the upload's own announcement,
            // and Refresh skipped this row then, because it was not missing yet: look again now,
            // or the Ebene would wait for the next unrelated edit.
            again = missing && _changes != changesAtStart;
        }
        while (again);

        item.Loaded(result.Data, result.Error, missing, _clock.Now);
        UpdateVisibleShapes();
    }

    private async Task<(OverlayData? Data, string? Error, bool Parsed)> Fetch(Guid fileId)
    {
        var bytes = await _session.GetFileBytesAsync(fileId);
        if (bytes is null)
        {
            return (null, "Datei ist noch nicht verfügbar.", false);
        }

        var (data, error) = await Task.Run(() => Parse(bytes));
        return (data, error, true);
    }

    private static (OverlayData? Data, string? Error) Parse(byte[] bytes) =>
        OverlayGeoJson.TryParse(bytes, out var data, out var error) ? (data, null) : (null, error);
}

/// <summary>One Ebene in the EBENEN list.</summary>
public sealed partial class MapOverlayItem : ObservableObject
{
    private readonly MapOverlaysViewModel _owner;
    private readonly TimeZoneInfo _timeZone;

    internal MapOverlayItem(MapOverlay overlay, MapOverlaysViewModel owner, TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
        Id = overlay.Id;
        FileId = overlay.FileId;
        Name = overlay.Name;
        Detail = $"{Formatting.Timestamp(overlay.ImportedAt)} · {overlay.ImportedBy}";
        _owner = owner;
    }

    public Guid Id { get; }

    public Guid FileId { get; }

    public string Name { get; }

    /// <summary>When and by whom it was imported.</summary>
    public string Detail { get; }

    public string VisibilityAutomationName => $"{Name}: auf der Karte zeigen";

    public string RemoveAutomationName => $"{Name}: entfernen";

    public string ShowAutomationName => $"{Name}: auf der Karte anzeigen";

    public string TimelineAutomationName => $"{Name}: Prognosezeitpunkt";

    public string PreviousAutomationName => $"{Name}: früherer Zeitpunkt";

    public string NextAutomationName => $"{Name}: späterer Zeitpunkt";

    /// <summary>Null while the file is still being read, and when it could not be.</summary>
    public OverlayData? Data { get; private set; }

    public bool IsLoading => Data is null && LoadError is null;

    /// <summary>Why the Ebene cannot be drawn, in German.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private string? _loadError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTimeline))]
    [NotifyPropertyChangedFor(nameof(ShowsLegend))]
    private bool _isVisible;

    /// <summary>Which point in time is shown, for an Ebene that has several.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FramePosition))]
    [NotifyPropertyChangedFor(nameof(FrameLabel))]
    [NotifyCanExecuteChangedFor(nameof(PreviousFrameCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextFrameCommand))]
    private int _frameIndex;

    /// <summary>The Ebene steps through several points in time.</summary>
    public bool IsTimeSeries => Data is { Frames.Count: > 1 };

    /// <summary>The Zeitleiste shows under a shown Ebene that has several points in time.</summary>
    public bool ShowsTimeline => IsVisible && IsTimeSeries;

    public int MaxFrameIndex => Data is { } data ? data.Frames.Count - 1 : 0;

    /// <summary><see cref="FrameIndex"/> as the Zeitleiste's slider moves it.</summary>
    public double FramePosition
    {
        get => FrameIndex;
        set
        {
            if (double.IsFinite(value))
            {
                FrameIndex = Math.Clamp((int)Math.Round(value), 0, MaxFrameIndex);
            }
        }
    }

    /// <summary>„Prognose 16:15 Uhr“, in the Einsatz's time; null for an Ebene without times.</summary>
    public string? FrameLabel =>
        Data?.Frames[Math.Clamp(FrameIndex, 0, MaxFrameIndex)].Time is { } time
            ? $"Prognose {Formatting.TimeOfDay(TimeZoneInfo.ConvertTime(time, _timeZone))} Uhr"
            : null;

    /// <summary>What the Ebene's colours mean, for a DWD Ausbreitungsprognose.</summary>
    public IReadOnlyList<OverlayLegendEntry> Legend => Data?.Legend ?? [];

    public bool ShowsLegend => IsVisible && Legend.Count > 0;

    /// <summary>The shapes of the shown frame; none until the file is read.</summary>
    public IReadOnlyList<OverlayShape> CurrentShapes =>
        Data is { } data ? data.Frames[Math.Clamp(FrameIndex, 0, data.Frames.Count - 1)].Shapes : [];

    /// <summary>The file was not there to read; it is looked for again on the next change.</summary>
    public bool IsMissing { get; private set; }

    private bool CanStepBack => FrameIndex > 0;

    private bool CanStepForward => FrameIndex < MaxFrameIndex;

    internal void Loaded(OverlayData? data, string? error, bool missing, DateTimeOffset now)
    {
        IsMissing = missing;
        Data = data;
        FrameIndex = data?.FrameIndexAt(now) ?? 0;
        LoadError = data is null ? $"Ebene konnte nicht gelesen werden: {error}" : null;
        OnPropertyChanged(nameof(Data));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsTimeSeries));
        OnPropertyChanged(nameof(ShowsTimeline));
        OnPropertyChanged(nameof(MaxFrameIndex));
        OnPropertyChanged(nameof(FramePosition));
        OnPropertyChanged(nameof(FrameLabel));
        OnPropertyChanged(nameof(Legend));
        OnPropertyChanged(nameof(ShowsLegend));
        PreviousFrameCommand.NotifyCanExecuteChanged();
        NextFrameCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsVisibleChanged(bool value) => _owner.UpdateVisibleShapes();

    partial void OnFrameIndexChanged(int value)
    {
        if (IsVisible)
        {
            _owner.UpdateVisibleShapes();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStepBack))]
    private void PreviousFrame() => FrameIndex--;

    [RelayCommand(CanExecute = nameof(CanStepForward))]
    private void NextFrame() => FrameIndex++;

    [RelayCommand]
    private void Remove() => _owner.ConfirmRemove(this);

    [RelayCommand]
    private void Show()
    {
        IsVisible = true;
        _owner.ShowBounds(this);
    }
}
