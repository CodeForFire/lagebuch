using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Measurements;

/// <summary>The German words of the Messprotokoll (#930), in one place so the ETB, the view models
/// and the PDF word the same thing the same way.</summary>
public static class MeasurementLabels
{
    public static string Zone(MeasurementZone zone) => zone switch
    {
        MeasurementZone.Unknown => "Bereich unbekannt",
        MeasurementZone.Danger => "Gefahrenbereich",
        MeasurementZone.Transition => "Übergangsbereich",
        MeasurementZone.Cordon => "Absperrbereich",
        MeasurementZone.Outside => "außerhalb",
        _ => throw new ArgumentOutOfRangeException(nameof(zone)),
    };

    public static string Protection(ProtectiveEquipment protection) => protection switch
    {
        ProtectiveEquipment.None => "ohne Sonderausrüstung",
        ProtectiveEquipment.FilterForm1 => "Filter + Form 1",
        ProtectiveEquipment.ScbaForm1 => "PA + Form 1",
        ProtectiveEquipment.ScbaForm2 => "PA + Form 2",
        ProtectiveEquipment.Form3 => "Form 3 (CSA)",
        _ => throw new ArgumentOutOfRangeException(nameof(protection)),
    };

    public static string Mode(MeasurementMode mode) => mode switch
    {
        MeasurementMode.Remote => "abgesetzt (Messtrupp)",
        MeasurementMode.FromVehicle => "aus dem Fahrzeug",
        MeasurementMode.Stationary => "Dauermessung (stationär)",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string Status(MeasurementStatus status) => status switch
    {
        MeasurementStatus.Unconfirmed => "unbestätigt",
        MeasurementStatus.Confirmed => "bestätigt",
        MeasurementStatus.Outlier => "Ausreißer",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>The direction the wind comes from, as it is read out: "NO", not "NE".</summary>
    public static string Wind(WindDirection wind) => wind switch
    {
        WindDirection.N => "N",
        WindDirection.NE => "NO",
        WindDirection.E => "O",
        WindDirection.SE => "SO",
        WindDirection.S => "S",
        WindDirection.SW => "SW",
        WindDirection.W => "W",
        WindDirection.NW => "NW",
        WindDirection.Variable => "umlaufend",
        WindDirection.Calm => "Windstille",
        _ => throw new ArgumentOutOfRangeException(nameof(wind)),
    };

    public static string Clouds(CloudCover clouds) => clouds switch
    {
        CloudCover.Clear => "klar",
        CloudCover.UpToHalf => "bis 50 % bedeckt",
        CloudCover.MoreThanHalf => "mehr als 50 % bedeckt",
        CloudCover.Overcast => "bedeckt",
        _ => throw new ArgumentOutOfRangeException(nameof(clouds)),
    };

    public static string Precipitation(Precipitation precipitation) => precipitation switch
    {
        Measurements.Precipitation.None => "kein Niederschlag",
        Measurements.Precipitation.Rain => "Regen",
        Measurements.Precipitation.Snow => "Schnee",
        Measurements.Precipitation.Fog => "Nebel",
        _ => throw new ArgumentOutOfRangeException(nameof(precipitation)),
    };

    public static string DeviceKind(DeviceKind kind) => kind switch
    {
        Measurements.DeviceKind.MultiGas => "Mehrgasmessgerät",
        Measurements.DeviceKind.SingleGas => "Eingasmessgerät",
        Measurements.DeviceKind.TubePump => "Prüfröhrchen / Gasspürpumpe",
        Measurements.DeviceKind.TubeAnalyser => "Röhrchen-Analysegerät (digital)",
        Measurements.DeviceKind.Pid => "Photoionisationsdetektor (PID)",
        Measurements.DeviceKind.DoseRate => "Dosisleistungsmessgerät",
        Measurements.DeviceKind.Contamination => "Kontaminationsnachweisgerät",
        Measurements.DeviceKind.Ph => "pH-Indikator",
        Measurements.DeviceKind.Other => "Sonstiges",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The band word of a value, the way the Stammdaten name the bands: "Erhöht",
    /// "Gefährlich", "Lebensgefährlich"; null for a normal value, which earns no word.</summary>
    public static string? Severity(CoSeverity severity) => severity switch
    {
        CoSeverity.Normal => null,
        CoSeverity.Elevated => "Erhöht",
        CoSeverity.Dangerous => "Gefährlich",
        CoSeverity.Lethal => "Lebensgefährlich",
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };

    /// <summary>The number or range alone, "38" or "8–14", through the de-DE format.</summary>
    public static string Number(MeasuredValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var low = Formatting.Measurement(value.Low, string.Empty);
        return value.IsRange ? $"{low}–{Formatting.Measurement(value.High, string.Empty)}" : low;
    }

    /// <summary>A value with its unit, "20,1 Vol-%" or "8–14 %UEG".</summary>
    public static string Value(MeasurementChannel channel, MeasuredValue value)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var number = Number(value);
        return channel.Unit.Length == 0 ? number : $"{number} {channel.Unit}";
    }

    /// <summary>A value as the ETB names it: "CO 38 ppm (Erhöht)", "O₂ 20,1 Vol-%"; the band word
    /// only when the value is in a band.</summary>
    public static string NamedValue(MeasurementChannel channel, MeasuredValue value)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var text = $"{channel.Name} {Value(channel, value)}";
        return Severity(MeasurementRules.SeverityOf(channel, value)) is { } band ? $"{text} ({band})" : text;
    }

    /// <summary>Which way a Messgröße gets worse: "steigend", or "fallend" for O₂.</summary>
    public static string Direction(GasDirection direction) => direction switch
    {
        GasDirection.Rising => "steigend",
        GasDirection.Falling => "fallend",
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    /// <summary>
    /// A Messgröße's bands as one line, each with the word its tile shows: "Erhöht ≥ 20 ·
    /// Gefährlich ≥ 60 · Lebensgefährlich ≥ 200 ppm", with ≤ for a falling gas. "ohne Schwellen"
    /// for a gas nobody gave any, which is recorded and shown in neutral.
    /// </summary>
    public static string Bands(GasDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var sign = definition.Direction == GasDirection.Falling ? "≤" : "≥";
        var bands = new List<string>();
        AddBand(bands, definition.Elevated, CoSeverity.Elevated, sign);
        AddBand(bands, definition.Dangerous, CoSeverity.Dangerous, sign);
        AddBand(bands, definition.Lethal, CoSeverity.Lethal, sign);
        if (bands.Count == 0)
        {
            return "ohne Schwellen";
        }

        var text = string.Join(" · ", bands);
        return definition.Unit.Length == 0 ? text : $"{text} {definition.Unit}";
    }

    private static void AddBand(List<string> bands, decimal? threshold, CoSeverity severity, string sign)
    {
        if (threshold is { } value)
        {
            bands.Add($"{Severity(severity)} {sign} {Formatting.Measurement(value, string.Empty)}");
        }
    }
}
