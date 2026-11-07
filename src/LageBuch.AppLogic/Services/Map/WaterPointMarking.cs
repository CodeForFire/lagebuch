using LageBuch.Domain;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// The Leistungsklasse a water point's marker is coloured by, along the Richtwerte of the
/// Löschwasserbedarf (48 / 96 / 192 m³/h). Ordered from weakest to strongest, which is also the
/// order the markers are drawn in, so a strong source lies on top.
/// </summary>
public enum FlowClass
{
    /// <summary>Not recorded or not assessable.</summary>
    Unbekannt,

    /// <summary>Under 400 l/min: not enough for an attack on its own.</summary>
    Unter400,

    /// <summary>400–799 l/min: one line, plan a feed.</summary>
    Bis799,

    /// <summary>800–1.599 l/min: Grundschutz for housing, 48 m³/h.</summary>
    Bis1599,

    /// <summary>1.600–3.199 l/min: higher demand, commercial, 96 m³/h.</summary>
    Bis3199,

    /// <summary>From 3.200 l/min: large buildings, 192 m³/h.</summary>
    Ab3200,
}

/// <summary>How much of a water point its map marker shows, by the map's zoom.</summary>
public enum WaterPointDetailLevel
{
    /// <summary>A small dot in the class colour, in a ring filled by the class.</summary>
    Punkt,

    /// <summary>A flag with the glyph and the flow.</summary>
    Mittel,

    /// <summary>A flag with the flow and its unit, the DN and the Ring/Stich sign.</summary>
    Voll,
}

/// <summary>
/// What a water point's map marker says, apart from how it is drawn: its class, its two lines of
/// text and how much of it shows at a zoom.
/// </summary>
public static class WaterPointMarking
{
    /// <summary>The value shown where nothing is known.</summary>
    public const string Unknown = "?";

    /// <summary>The class of <paramref name="litresPerMinute"/>; <see cref="FlowClass.Unbekannt"/> for null.</summary>
    public static FlowClass Classify(int? litresPerMinute) => litresPerMinute switch
    {
        null => FlowClass.Unbekannt,
        < 400 => FlowClass.Unter400,
        < 800 => FlowClass.Bis799,
        < 1600 => FlowClass.Bis1599,
        < 3200 => FlowClass.Bis3199,
        _ => FlowClass.Ab3200,
    };

    /// <summary>
    /// The flow as the marker shows it: "1600", without a thousands point so four digits stay
    /// short; "12k" from 10.000; in brackets when it is an estimate; "?" when unknown.
    /// </summary>
    public static string FlowText(WaterPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (point.FlowLitresPerMinute is not { } flow)
        {
            return Unknown;
        }

        var text = flow >= 10_000 ? $"{Formatting.UngroupedNumber(flow / 1000)}k" : Formatting.UngroupedNumber(flow);
        return point.IsEstimated ? $"({text})" : text;
    }

    /// <summary>
    /// The one line a <see cref="WaterPointDetailLevel.Mittel"/> flag has room for: the flow, or,
    /// where nobody recorded one, the DN rather than a "?". OpenStreetMap knows the DN of most
    /// hydrants and the flow of almost none, and the DN is what the Hinweisschild says too. The
    /// colour stays grey, so the DN is never mistaken for a flow.
    /// </summary>
    public static string MediumText(WaterPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.FlowLitresPerMinute is null && point.NominalDiameter is not null ? DiameterText(point) : FlowText(point);
    }

    /// <summary>"DN 100", or "DN ?" when the Nennweite is unknown.</summary>
    public static string DiameterText(WaterPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.NominalDiameter is { } dn ? $"DN {Formatting.UngroupedNumber(dn)}" : $"DN {Unknown}";
    }
}
