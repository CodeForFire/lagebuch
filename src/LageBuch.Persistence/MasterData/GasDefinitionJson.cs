using System.Globalization;
using System.Text.Json;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Persistence.MasterData;

/// <summary>
/// The Messgase list's file format (#410): the spelling of a gas's direction, and the reading of
/// the <c>gases</c> array from a Stammdaten file. A file is hostile input -- an import, or a sync
/// host's <c>/masterdata</c> -- so everything here either clamps or throws one of the exception
/// types the callers already catch (<see cref="JsonException"/>, <see cref="FormatException"/>,
/// <see cref="InvalidOperationException"/>, <see cref="KeyNotFoundException"/>).
/// </summary>
internal static class GasDefinitionJson
{
    public static string DirectionName(GasDirection direction) =>
        direction == GasDirection.Falling ? "fallend" : "steigend";

    /// <summary>Total, like <c>Role.UniquenessFrom</c>: anything that is not "fallend" is rising,
    /// because a hand-edited file may carry anything and rising is how every gas but O₂ reads.</summary>
    public static GasDirection DirectionFrom(string? name) =>
        string.Equals(name?.Trim(), "fallend", StringComparison.OrdinalIgnoreCase) ? GasDirection.Falling : GasDirection.Rising;

    /// <summary>
    /// Reads <c>gases</c>. Absent, not an array or empty yields <see cref="GasDefinition.DefaultCo"/>
    /// alone, so a file from before #410 measures exactly what it always did. Names and units are
    /// trimmed and cut to their caps, same-named gases after the first are dropped, and the list is
    /// cut at <see cref="GasDefinition.MaxCount"/>. A threshold that is not a number, or is out of
    /// range, fails the read; thresholds out of order reject the file outright with a German
    /// message, because guessing which of them the brigade meant would colour a Wohnung wrongly.
    /// </summary>
    public static IReadOnlyList<GasDefinition> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("gases", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [GasDefinition.DefaultCo];
        }

        var read = new List<GasDefinition>();
        foreach (var x in arr.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var gas = new GasDefinition(
                x.GetProperty("name").GetString() ?? throw new JsonException("Messgas: \"name\" darf nicht null sein."),
                x.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? string.Empty : string.Empty,
                DirectionFrom(x.TryGetProperty("direction", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null),
                Number(x, "elevated"),
                Number(x, "dangerous"),
                Number(x, "lethal"),
                Number(x, "plausibleUpTo"));
            if (!gas.ThresholdsInOrder)
            {
                throw new JsonException($"Messgas „{gas.Name.Trim()}“: Schwellen nicht in Reihenfolge.");
            }

            read.Add(gas);
        }

        var gases = GasDefinition.SanitizeList(read);
        return gases.Count > 0 ? gases : [GasDefinition.DefaultCo];
    }

    /// <summary>A decimal the way the store and the incident file keep it: invariant culture, no
    /// grouping, so it reads back the same on any machine.</summary>
    public static string? ToText(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    /// <summary>The reverse of <see cref="ToText"/>. Text that is not a number reads as null --
    /// a stored file is not the place to fail an Einsatz over one threshold.</summary>
    public static decimal? FromText(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    // A JSON null or a missing key is "not set"; anything else must be a number in range. GetDecimal
    // throws FormatException for a number decimal cannot hold, and a string or NaN is refused here.
    private static decimal? Number(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (v.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException($"Messgas: \"{key}\" muss eine Zahl sein.");
        }

        return GasDefinition.NormalizeValue(v.GetDecimal())
            ?? throw new JsonException($"Messgas: \"{key}\" liegt außerhalb von 0 bis {GasDefinition.MaxValue.ToString(CultureInfo.InvariantCulture)}.");
    }
}
