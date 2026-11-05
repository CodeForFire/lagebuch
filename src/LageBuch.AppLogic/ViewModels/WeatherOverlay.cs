using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services.Weather;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// WETTER on the Lagekarte: what the nearest DWD station measured at the Einsatzort, the wind as
/// an arrow pointing downwind from it, and the DWD warnings in force. Loaded only when the button
/// is pressed, because the request tells the weather service roughly where the Einsatz is.
/// Belongs to the screen, not to the Einsatz: nothing is stored or synced until someone writes the
/// reading into the ETB.
/// </summary>
public sealed partial class WeatherOverlay : ObservableObject
{
    private readonly IWeatherSource? _source;
    private readonly MapViewport _map;
    private readonly IClock _clock;
    private readonly Func<string, bool>? _logToEtb;

    // Counts loads, so an answer for a spot the Einsatzort has since left cannot land.
    private int _load;

    /// <param name="source">Where the weather comes from; null offers no weather at all.</param>
    /// <param name="map">The map the arrow is drawn on, so it turns with it.</param>
    /// <param name="clock">Whose time zone the time of measurement is shown in.</param>
    /// <param name="logToEtb">Writes a line into the ETB and says whether it was taken; null on a read-only Einsatz.</param>
    public WeatherOverlay(IWeatherSource? source, MapViewport map, IClock clock, Func<string, bool>? logToEtb)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(clock);
        _source = source;
        _map = map;
        _clock = clock;
        _logToEtb = logToEtb;
        _map.PropertyChanged += OnMapChanged;
    }

    /// <summary>Whether this device can fetch weather at all.</summary>
    public bool IsAvailable => _source is not null;

    /// <summary>Whether a reading can go into the ETB: false on a closed or read-only Einsatz.</summary>
    public bool CanLog => _logToEtb is not null;

    /// <summary>Where the weather is loaded for; set by the Lagekarte whenever the Einsatzort changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadTip))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private GeoPoint? _einsatzort;

    /// <summary>The last reading loaded; null while none is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReading), nameof(HasWind), nameof(WindText), nameof(DetailText), nameof(HasDetail), nameof(SourceText), nameof(Alerts), nameof(HasAlerts), nameof(WindArrowAngle))]
    [NotifyCanExecuteChangedFor(nameof(LogToEtbCommand))]
    private WeatherReading? _reading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private bool _isLoading;

    /// <summary>Why the last load failed, in German; null when it did not.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>The reading on screen is already in the ETB, so a second press would only repeat it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogToEtbCommand))]
    private bool _isLogged;

    public string LoadTip => Einsatzort is null ? "Wetter: erst den Einsatzort setzen" : "Wetter am Einsatzort laden (DWD)";

    public bool HasReading => Reading is not null;

    /// <summary>A direction was measured, so there is an arrow to draw.</summary>
    public bool HasWind => Reading?.WindFromDegrees is not null;

    public string WindText => Reading is { } r ? WeatherText.Wind(r) : string.Empty;

    public string DetailText => Reading is { } r ? WeatherText.Details(r) : string.Empty;

    public bool HasDetail => DetailText.Length > 0;

    public string SourceText => Reading is { } r ? WeatherText.Source(r, _clock.Now.Offset) : string.Empty;

    public IReadOnlyList<WeatherAlertRow> Alerts =>
        Reading?.Alerts.Select(a => new WeatherAlertRow(WeatherText.Alert(a, _clock.Now.Offset), a.Severity >= WeatherAlertSeverity.Severe)).ToList() ?? [];

    public bool HasAlerts => Reading is { Alerts.Count: > 0 };

    /// <summary>
    /// Where the arrow points on screen, degrees clockwise from up: downwind, the way smoke and gas
    /// drift, turned with the map.
    /// </summary>
    public double WindArrowAngle => Reading?.WindFromDegrees is { } from ? (from + 180 + _map.Rotation) % 360 : 0;

    private bool CanLoad => _source is not null && Einsatzort is not null && !IsLoading;

    // Concurrent on purpose: moving the Einsatzort abandons a load, and the next press must not wait for it.
    [RelayCommand(CanExecute = nameof(CanLoad), AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        if (_source is null || Einsatzort is not { } at)
        {
            return;
        }

        var load = ++_load;
        IsLoading = true;
        Error = null;
        try
        {
            var reading = await _source.LoadAsync(at);
            if (load == _load)
            {
                IsLogged = false;
                Reading = reading;
            }
        }
        catch (WeatherException e)
        {
            if (load == _load)
            {
                Reading = null;
                Error = e.Message;
            }
        }
        finally
        {
            if (load == _load)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private void Close() => Clear();

    private bool CanLogReading => _logToEtb is not null && Reading is not null && !IsLogged;

    [RelayCommand(CanExecute = nameof(CanLogReading))]
    private void LogToEtb()
    {
        if (_logToEtb is not null && Reading is { } reading && _logToEtb(WeatherText.EtbEntry(reading, _clock.Now.Offset)))
        {
            IsLogged = true;
        }
    }

    // A reading belongs to the spot it was measured for; once the Einsatzort moves it says nothing about it.
    partial void OnEinsatzortChanged(GeoPoint? value) => Clear();

    private void Clear()
    {
        _load++;
        IsLoading = false;
        Error = null;
        IsLogged = false;
        Reading = null;
    }

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapViewport.Rotation) && HasWind)
        {
            OnPropertyChanged(nameof(WindArrowAngle));
        }
    }
}

/// <summary>A DWD warning as the banner shows it; a severe or extreme one is marked in the signal colour.</summary>
public sealed record WeatherAlertRow(string Text, bool IsSevere);
