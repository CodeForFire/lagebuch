using LageBuch.Domain;
using LageBuch.Domain.Measurements;

namespace LageBuch.AppLogic.Services.Weather;

/// <summary>
/// How a DWD <see cref="WeatherReading"/> fills the Messprotokoll's Wetter form (#930): the
/// station's numbers as the form's coarser choices. The Lagebuchführer still saves it, so a value
/// the station did not report stays unset rather than guessed.
/// </summary>
public static class MeasurementWeather
{
    /// <summary>Below this the wind is Windstille: Beaufort 0.</summary>
    public const double CalmBelowKmh = 1;

    private static readonly WindDirection[] CompassPoints =
        [WindDirection.N, WindDirection.NE, WindDirection.E, WindDirection.SE, WindDirection.S, WindDirection.SW, WindDirection.W, WindDirection.NW];

    /// <summary>
    /// The nearest of the eight compass points; Windstille below <see cref="CalmBelowKmh"/>;
    /// umlaufend for a wind with a speed but no direction; null when neither was measured.
    /// </summary>
    public static WindDirection? Wind(WeatherReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.WindSpeedKmh is < CalmBelowKmh)
        {
            return WindDirection.Calm;
        }

        if (reading.WindFromDegrees is { } degrees)
        {
            var normalized = ((degrees % 360) + 360) % 360;
            return CompassPoints[(int)Math.Floor((normalized / 45) + 0.5) % CompassPoints.Length];
        }

        return reading.WindSpeedKmh is null ? null : WindDirection.Variable;
    }

    /// <summary>The form's four Bewölkung steps from the station's percentage; null when it gave none.</summary>
    public static CloudCover? Clouds(WeatherReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        // DWD reports cover in eighths; under half an eighth is klar, all eight bedeckt.
        return reading.CloudCoverPercent switch
        {
            null => null,
            < 6.25 => CloudCover.Clear,
            <= 50 => CloudCover.UpToHalf,
            < 93.75 => CloudCover.MoreThanHalf,
            _ => CloudCover.Overcast,
        };
    }

    /// <summary>
    /// Niederschlag from the station's condition: Schnee and Schneeregen as Schnee, Hagel and
    /// Gewitter as Regen. Without a condition, rain when the last hour brought any.
    /// </summary>
    public static Precipitation Precipitation(WeatherReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return reading.Condition switch
        {
            "rain" or "hail" or "thunderstorm" => Domain.Measurements.Precipitation.Rain,
            "snow" or "sleet" => Domain.Measurements.Precipitation.Snow,
            "fog" => Domain.Measurements.Precipitation.Fog,
            "dry" => Domain.Measurements.Precipitation.None,
            _ => reading.PrecipitationMm is > 0 ? Domain.Measurements.Precipitation.Rain : Domain.Measurements.Precipitation.None,
        };
    }

    /// <summary>
    /// "DWD Maisach-Galgen (4,6 km)": the station and how far off it is, which is what a reading
    /// is judged by. The distance goes first when it does not fit, then the name is cut.
    /// </summary>
    public static string Source(WeatherReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.StationName is not { } station)
        {
            return "DWD";
        }

        var named = "DWD " + station;
        if (reading.StationDistanceMetres is { } metres && named.Length + 3 + Formatting.Kilometres(metres).Length <= WeatherObservation.MaxSourceLength)
        {
            return $"{named} ({Formatting.Kilometres(metres)})";
        }

        return named.Length > WeatherObservation.MaxSourceLength ? named[..WeatherObservation.MaxSourceLength].TrimEnd() : named;
    }

    /// <summary>A measured number as the form's text box takes it: "12,4".</summary>
    public static string? Number(double? value) =>

        // Adding zero turns a rounded -0 into 0, which would otherwise print as "-0".
        value is { } number ? (Math.Round(number, 1, MidpointRounding.AwayFromZero) + 0.0).ToString("0.#", Formatting.NumberFormat) : null;
}
