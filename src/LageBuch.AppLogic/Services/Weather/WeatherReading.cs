namespace LageBuch.AppLogic.Services.Weather;

/// <summary>
/// What the Deutscher Wetterdienst measured near the Einsatzort, as Bright Sky reports it. Every
/// measurement is optional: a station that does not record a quantity leaves it out, and the
/// answer is untrusted input, so a value that makes no sense is dropped rather than shown.
/// </summary>
/// <param name="MeasuredAt">When the station took the observation.</param>
/// <param name="StationName">The DWD station the observation comes from, if it was named.</param>
/// <param name="StationDistanceMetres">How far that station lies from the requested point.</param>
/// <param name="WindFromDegrees">Where the wind comes from, 0 up to 360°, clockwise from north.</param>
/// <param name="WindSpeedKmh">Mean wind speed over the last ten minutes.</param>
/// <param name="WindGustKmh">The strongest gust over the last ten minutes.</param>
/// <param name="TemperatureCelsius">Air temperature.</param>
/// <param name="Condition">Bright Sky's condition key ("dry", "rain", …), see <see cref="WeatherText.Condition"/>.</param>
/// <param name="PrecipitationMm">Precipitation over the last hour.</param>
/// <param name="Alerts">The DWD warnings in force for the point; empty when there are none.</param>
public sealed record WeatherReading(
    DateTimeOffset MeasuredAt,
    string? StationName,
    double? StationDistanceMetres,
    double? WindFromDegrees,
    double? WindSpeedKmh,
    double? WindGustKmh,
    double? TemperatureCelsius,
    string? Condition,
    double? PrecipitationMm,
    IReadOnlyList<WeatherAlert> Alerts)
{
    /// <summary>How much of the sky is covered, 0 up to 100 %; null when the station does not say.</summary>
    public double? CloudCoverPercent { get; init; }
}

/// <summary>A DWD warning (Unwetterwarnung) for the point, in the German wording DWD issues.</summary>
/// <param name="Event">The kind of warning, e.g. "STURMBÖEN".</param>
/// <param name="Headline">DWD's headline, e.g. "Amtliche WARNUNG vor STURMBÖEN".</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Onset">From when it applies, if given.</param>
/// <param name="Expires">Until when it applies, if given.</param>
public sealed record WeatherAlert(
    string Event,
    string Headline,
    WeatherAlertSeverity Severity,
    DateTimeOffset? Onset,
    DateTimeOffset? Expires);

/// <summary>DWD's severity levels, mildest first; anything unrecognised is <see cref="Minor"/>.</summary>
public enum WeatherAlertSeverity
{
    Minor,
    Moderate,
    Severe,
    Extreme,
}
