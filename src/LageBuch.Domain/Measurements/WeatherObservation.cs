namespace LageBuch.Domain.Measurements;

/// <summary>What the weather was at one time (#930): wind direction and speed, temperature, sky
/// and Niederschlag, and where it came from ("Handwindmesser", "DWD"). The wind decides where the
/// next Messpunkt goes, so every change is its own observation.</summary>
public sealed record WeatherObservation(
    Guid Id,
    DateTimeOffset ObservedAt,
    WindDirection Wind,
    decimal? WindSpeedKmh,
    decimal? TemperatureCelsius,
    CloudCover Clouds,
    Precipitation Precipitation,
    string? Source)
{
    public const int MaxCount = 500;

    public const int MaxSourceLength = 40;

    public const decimal MaxWindSpeedKmh = 300m;

    public const decimal MinTemperatureCelsius = -60m;

    public const decimal MaxTemperatureCelsius = 60m;

    /// <summary>A wind speed rounded to a tenth, or null when it lies outside 0 to
    /// <see cref="MaxWindSpeedKmh"/>.</summary>
    public static decimal? NormalizeWindSpeed(decimal value) =>
        value is < 0 or > MaxWindSpeedKmh ? null : decimal.Round(value, 1, MidpointRounding.AwayFromZero);

    /// <summary>A temperature rounded to a tenth, or null when it lies outside
    /// <see cref="MinTemperatureCelsius"/> to <see cref="MaxTemperatureCelsius"/>.</summary>
    public static decimal? NormalizeTemperature(decimal value) =>
        value is < MinTemperatureCelsius or > MaxTemperatureCelsius ? null : decimal.Round(value, 1, MidpointRounding.AwayFromZero);

    /// <summary>The same observation with every rule that cannot be trusted from outside applied:
    /// a number out of range dropped, an unknown enum value read as its default, the source
    /// trimmed and cut.</summary>
    public WeatherObservation Sanitized() => this with
    {
        Wind = Enum.IsDefined(Wind) ? Wind : WindDirection.Variable,
        WindSpeedKmh = WindSpeedKmh is { } speed ? NormalizeWindSpeed(speed) : null,
        TemperatureCelsius = TemperatureCelsius is { } temperature ? NormalizeTemperature(temperature) : null,
        Clouds = Enum.IsDefined(Clouds) ? Clouds : CloudCover.Clear,
        Precipitation = Enum.IsDefined(Precipitation) ? Precipitation : Precipitation.None,
        Source = MeasurementText.Clean(Source, MaxSourceLength),
    };
}
