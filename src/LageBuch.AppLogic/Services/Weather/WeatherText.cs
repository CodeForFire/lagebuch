using LageBuch.Domain;

namespace LageBuch.AppLogic.Services.Weather;

/// <summary>
/// How a <see cref="WeatherReading"/> reads on the Lagekarte and in the ETB. German, short, and
/// always naming the DWD station and the time of measurement, because a reading without them
/// cannot be judged.
/// </summary>
public static class WeatherText
{
    private static readonly string[] CompassPoints = ["N", "NO", "O", "SO", "S", "SW", "W", "NW"];

    /// <summary>The nearest of the eight compass points, in German ("NO", not "NE").</summary>
    public static string CompassPoint(double degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        var index = (int)Math.Floor((normalized / 45) + 0.5) % CompassPoints.Length;
        return CompassPoints[index];
    }

    /// <summary>Bright Sky's condition key in German, or null for none or one it does not document.</summary>
    public static string? Condition(string? key) => key switch
    {
        "dry" => "trocken",
        "fog" => "Nebel",
        "rain" => "Regen",
        "sleet" => "Schneeregen",
        "snow" => "Schnee",
        "hail" => "Hagel",
        "thunderstorm" => "Gewitter",
        _ => null,
    };

    /// <summary>"Wind aus NO (60°) · 10 km/h · Böen 16 km/h".</summary>
    public static string Wind(WeatherReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var parts = WindParts(reading);
        return parts.Count == 0 ? "Wind: keine Messung" : string.Join(" · ", parts);
    }

    /// <summary>"0,3 °C · trocken", with the last hour's precipitation when some fell.</summary>
    public static string Details(WeatherReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return string.Join(" · ", DetailParts(reading, withPrecipitationWindow: true));
    }

    /// <summary>"DWD-Station Maisach-Galgen (4,6 km) · Stand 22:10", in the device's local time.</summary>
    public static string Source(WeatherReading reading, TimeSpan localOffset)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var time = "Stand " + Formatting.TimeOfDay(reading.MeasuredAt.ToOffset(localOffset));
        if (reading.StationName is not { } station)
        {
            return "DWD · " + time;
        }

        var distance = reading.StationDistanceMetres is { } metres ? $" ({Formatting.Kilometres(metres)})" : string.Empty;
        return $"DWD-Station {station}{distance} · {time}";
    }

    /// <summary>"Amtliche WARNUNG vor STURMBÖEN · bis 05.11. 23:00".</summary>
    public static string Alert(WeatherAlert alert, TimeSpan localOffset)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return alert.Expires is { } expires
            ? $"{alert.Headline} · bis {Formatting.DayAndTime(expires.ToOffset(localOffset))}"
            : alert.Headline;
    }

    /// <summary>
    /// The reading as one ETB line: "Wetter (DWD, Station Maisach-Galgen, 22:10 Uhr): Wind aus NO
    /// (60°) 10 km/h, Böen 16 km/h, 0,3 °C, trocken." and every warning in force after it.
    /// </summary>
    public static string EtbEntry(WeatherReading reading, TimeSpan localOffset)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var station = reading.StationName is { } name ? $", Station {name}" : string.Empty;
        var head = $"Wetter (DWD{station}, {Formatting.TimeOfDay(reading.MeasuredAt.ToOffset(localOffset))} Uhr): ";

        var wind = new List<string>();
        if (reading.WindFromDegrees is { } from)
        {
            wind.Add($"aus {CompassPoint(from)} ({Degrees(from)})");
        }

        if (reading.WindSpeedKmh is { } speed)
        {
            wind.Add(Formatting.Speed(speed));
        }

        var values = new List<string>();
        if (wind.Count > 0)
        {
            values.Add("Wind " + string.Join(' ', wind));
        }

        if (reading.WindGustKmh is { } gust)
        {
            values.Add("Böen " + Formatting.Speed(gust));
        }

        values.AddRange(DetailParts(reading, withPrecipitationWindow: false));
        var body = values.Count == 0 ? "keine Messwerte." : string.Join(", ", values) + ".";

        var warnings = reading.Alerts.Select(a => a.Expires is { } expires
            ? $" Warnung: {a.Event} bis {Formatting.DayAndTime(expires.ToOffset(localOffset))}."
            : $" Warnung: {a.Event}.");
        return head + body + string.Concat(warnings);
    }

    private static List<string> WindParts(WeatherReading reading)
    {
        var parts = new List<string>();
        if (reading.WindFromDegrees is { } from)
        {
            parts.Add($"Wind aus {CompassPoint(from)} ({Degrees(from)})");
        }

        if (reading.WindSpeedKmh is { } speed)
        {
            parts.Add(parts.Count == 0 ? "Wind " + Formatting.Speed(speed) : Formatting.Speed(speed));
        }

        if (reading.WindGustKmh is { } gust)
        {
            parts.Add("Böen " + Formatting.Speed(gust));
        }

        return parts;
    }

    private static List<string> DetailParts(WeatherReading reading, bool withPrecipitationWindow)
    {
        var parts = new List<string>();
        if (reading.TemperatureCelsius is { } celsius)
        {
            parts.Add(Formatting.Celsius(celsius));
        }

        if (Condition(reading.Condition) is { } condition)
        {
            parts.Add(condition);
        }

        if (reading.PrecipitationMm is { } mm && mm > 0)
        {
            parts.Add(withPrecipitationWindow ? $"{Formatting.Millimetres(mm)} in 1 h" : $"{Formatting.Millimetres(mm)} Niederschlag in 1 h");
        }

        return parts;
    }

    private static string Degrees(double degrees) => Formatting.Number((int)Math.Round(degrees)) + "°";
}
