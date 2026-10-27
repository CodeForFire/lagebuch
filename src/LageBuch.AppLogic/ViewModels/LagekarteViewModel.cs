using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The KARTE tab in Führung: the Einsatzort and the units on an offline map. Placing works by
/// picking what to place and then tapping the map, the same on a desk and on a tablet; a marker
/// already on the map is dragged to move it. Positions live in the incident, so they sync and
/// survive another device's Kartenpaket -- the pack is only the backdrop.
/// </summary>
public sealed partial class LagekarteViewModel : ObservableObject, INarrowAware, IDisposable
{
    /// <summary>Close enough to see which building, far enough to see the street around it.</summary>
    public const int EinsatzortZoom = 17;

    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly IReadOnlyList<InstalledRegionPack> _packs;
    private InstalledRegionPack? _shownPack;

    public LagekarteViewModel(IIncidentSession session, Action onChanged, IRegionPackLibrary? mapPacks = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        _packs = InstalledPacks(mapPacks);
        Refresh();
        ShowPackFor(FirstMark());
        if (session.Incident.Location is { } location)
        {
            Map.ChangeViewCommand.Execute(new MapViewChange(location, EinsatzortZoom));
        }

        _session.Changed += Refresh;
    }

    public void Dispose() => _session.Changed -= Refresh;

    public MapViewport Map { get; } = new();

    /// <summary>Every unit of the Einsatz, by Funkrufname, each saying whether it is on the map.</summary>
    public ObservableCollection<LagekarteUnitRow> Units { get; } = new();

    /// <summary>What the map draws over the tiles: the Einsatzort and each placed unit.</summary>
    public ObservableCollection<LagekarteMarker> Markers { get; } = new();

    public bool IsReadOnly { get; }

    public bool CanEdit => !IsReadOnly;

    /// <summary>No Kartenpaket on this device: the map still takes marks, it just has no streets under them.</summary>
    public bool HasNoMap => _packs.Count == 0;

    public bool HasEinsatzort => _session.Incident.Location is not null;

    public string EinsatzortLabel => HasEinsatzort ? "EINSATZORT VERSETZEN" : "EINSATZORT SETZEN";

    public bool HasNoUnits => Units.Count == 0;

    [ObservableProperty]
    private bool _isNarrow;

    [ObservableProperty]
    private string? _actionError;

    /// <summary>What the next tap on the map places; null while nothing is picked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlacing))]
    [NotifyPropertyChangedFor(nameof(PlacingHint))]
    private LagekarteTarget? _placing;

    public bool IsPlacing => Placing is not null;

    public string? PlacingHint => Placing is { } target ? $"Auf die Karte tippen: {target.Label} platzieren" : null;

    [RelayCommand]
    private void PlaceEinsatzort() => Pick(new LagekarteTarget(null, "Einsatzort"));

    [RelayCommand]
    private void PlaceUnit(LagekarteUnitRow? row)
    {
        if (row is not null)
        {
            Pick(new LagekarteTarget(row.UnitId, row.Title));
        }
    }

    [RelayCommand]
    private void CancelPlacing() => Placing = null;

    /// <summary>A tap on the map: places what was picked there. Without a pick a tap does nothing.</summary>
    [RelayCommand]
    private void MapTapped(GeoPoint? point)
    {
        if (point is null || Placing is not { } target)
        {
            return;
        }

        Placing = null;
        Place(target.UnitId, point);
    }

    /// <summary>Places what was picked in the middle of the map, for a keyboard or a hand that cannot tap precisely.</summary>
    [RelayCommand]
    private void PlaceAtCenter()
    {
        if (Placing is { } target)
        {
            Placing = null;
            Place(target.UnitId, Map.Center);
        }
    }

    /// <summary>A marker dragged to a new spot.</summary>
    public void MoveMarker(LagekarteMarker marker, GeoPoint position)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(position);
        if (CanEdit)
        {
            Place(marker.UnitId, position);
        }
    }

    /// <summary>Puts every mark back where the incident has it, after a drag that was abandoned.</summary>
    public void RestoreMarkers() => Refresh();

    [RelayCommand]
    private void RemoveUnit(LagekarteUnitRow? row)
    {
        if (row is { IsPlaced: true } && CanEdit)
        {
            Run(() => _session.PlaceForceUnit(row.UnitId, null));
        }
    }

    [RelayCommand]
    private void RemoveEinsatzort()
    {
        if (HasEinsatzort && CanEdit)
        {
            Run(() => _session.SetIncidentLocation(null));
        }
    }

    [RelayCommand]
    private void CenterOnEinsatzort()
    {
        if (_session.Incident.Location is { } location)
        {
            ShowPackFor(location);
            Map.ChangeViewCommand.Execute(new MapViewChange(location, Math.Max(Map.Zoom, EinsatzortZoom)));
        }
    }

    [RelayCommand]
    private void CenterOnUnit(LagekarteUnitRow? row)
    {
        if (row?.Position is { } position)
        {
            ShowPackFor(position);
            Map.ChangeViewCommand.Execute(new MapViewChange(position, Map.Zoom));
        }
    }

    private void Pick(LagekarteTarget target)
    {
        if (CanEdit)
        {
            ActionError = null;
            Placing = target;
        }
    }

    private void Place(Guid? unitId, GeoPoint position)
    {
        var placed = unitId is { } id
            ? Run(() => _session.PlaceForceUnit(id, position))
            : Run(() => _session.SetIncidentLocation(position));
        if (placed)
        {
            ShowPackFor(position);
        }
    }

    private bool Run(Action command)
    {
        ActionError = null;
        try
        {
            command();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            ActionError = DomainRefusal.Message(ex);
            return false;
        }

        _onChanged();
        return true;
    }

    private void Refresh()
    {
        var incident = _session.Incident;
        var units = incident.Forces
            .OrderBy(u => u.CallSign ?? u.Brigade, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Units.Clear();
        foreach (var unit in units)
        {
            Units.Add(new LagekarteUnitRow(unit));
        }

        Markers.Clear();
        if (incident.Location is { } location)
        {
            Markers.Add(LagekarteMarker.Einsatzort(location));
        }

        foreach (var unit in units.Where(u => u.Position is not null))
        {
            Markers.Add(LagekarteMarker.For(unit));
        }

        // A unit that left the Einsatz while it was picked cannot be placed any more.
        if (Placing is { UnitId: { } picked } && units.All(u => u.Id != picked))
        {
            Placing = null;
        }

        OnPropertyChanged(nameof(HasEinsatzort));
        OnPropertyChanged(nameof(EinsatzortLabel));
        OnPropertyChanged(nameof(HasNoUnits));
    }

    private GeoPoint? FirstMark() =>
        _session.Incident.Location ?? _session.Incident.Forces.Select(u => u.Position).FirstOrDefault(p => p is not null);

    // Keeps the pack on screen while it covers the point; otherwise the first pack that does, or
    // the first pack at all when none does, so that the map never goes blank by choice.
    private void ShowPackFor(GeoPoint? point)
    {
        if (_packs.Count == 0 || (_shownPack is not null && (point is null || _shownPack.Info.Covers(point))))
        {
            return;
        }

        var pack = (point is null ? null : _packs.FirstOrDefault(p => p.Info.Covers(point))) ?? _shownPack ?? _packs[0];
        if (ReferenceEquals(pack, _shownPack))
        {
            return;
        }

        // Show() opens on the pack's middle. That is right for the first pack; a switch while the
        // map already looks somewhere keeps looking there.
        var first = _shownPack is null;
        var view = new MapViewChange(Map.Center, Map.Zoom);
        _shownPack = pack;
        Map.Show(pack);
        if (!first)
        {
            Map.ChangeViewCommand.Execute(view);
        }
    }

    private static IReadOnlyList<InstalledRegionPack> InstalledPacks(IRegionPackLibrary? library)
    {
        try
        {
            return library?.GetInstalled() ?? [];
        }
        catch (RegionPackException)
        {
            // An unreadable pack folder costs the backdrop, not the Einsatz: the map still takes marks.
            return [];
        }
    }
}

/// <summary>What a tap on the map is about to place: a unit, or the Einsatzort when <see cref="UnitId"/> is null.</summary>
public sealed record LagekarteTarget(Guid? UnitId, string Label);

/// <summary>A unit in the list beside the map.</summary>
public sealed class LagekarteUnitRow(ForceUnit unit)
{
    public Guid UnitId { get; } = unit.Id;

    /// <summary>The Funkrufname, or the Feuerwehr for a unit without one.</summary>
    public string Title { get; } = unit.CallSign ?? unit.Brigade;

    public string Detail { get; } = unit.CallSign is null ? unit.Status ?? string.Empty : string.Join(" · ", new[] { unit.Brigade, unit.Status }.OfType<string>());

    public GeoPoint? Position { get; } = unit.Position;

    public bool IsPlaced => Position is not null;

    public string PlaceLabel => IsPlaced ? "VERSETZEN" : "AUF KARTE";
}

/// <summary>A mark on the map: the Einsatzort, or a unit when <see cref="UnitId"/> is set.</summary>
public sealed partial class LagekarteMarker : ObservableObject
{
    private LagekarteMarker(Guid? unitId, string title, string? subtitle, GeoPoint position)
    {
        UnitId = unitId;
        Title = title;
        Subtitle = subtitle;
        _position = position;
    }

    public Guid? UnitId { get; }

    public bool IsEinsatzort => UnitId is null;

    public string Title { get; }

    public string? Subtitle { get; }

    /// <summary>Where the marker is drawn. Moves under the hand while dragged, before anything is stored.</summary>
    [ObservableProperty]
    private GeoPoint _position;

    public string AutomationName => IsEinsatzort ? "Einsatzort" : $"{Title} auf der Karte";

    public static LagekarteMarker Einsatzort(GeoPoint location) => new(null, "Einsatzort", null, location);

    public static LagekarteMarker For(ForceUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var position = unit.Position ?? throw new ArgumentException("Die Einheit steht nicht auf der Karte.", nameof(unit));
        return new(unit.Id, unit.CallSign ?? unit.Brigade, unit.Status, position);
    }
}
