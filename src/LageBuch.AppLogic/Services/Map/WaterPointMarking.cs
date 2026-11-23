using LageBuch.Domain;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>How much of a water point its map marker shows, by the map's zoom.</summary>
public enum WaterPointDetailLevel
{
    /// <summary>A small dot in the source's colour.</summary>
    Punkt,

    /// <summary>A flag with the glyph and the first line.</summary>
    Mittel,

    /// <summary>A flag with the first line and, below it, Lage, outlets and private access.</summary>
    Voll,
}

/// <summary>
/// What a water point's map marker says, apart from how it is drawn: only what OpenStreetMap holds.
/// OSM knows the DN of most hydrants and the flow of almost none, and has no tag for Ring or Stich,
/// so a marker shows neither; the colour says only whether the point hangs on the Trinkwassernetz.
/// </summary>
public static class WaterPointMarking
{
    /// <summary>The value shown where nothing is known.</summary>
    public const string Unknown = "?";

    private const string Separator = " · ";

    /// <summary>
    /// The flag's first line: a hydrant's "DN 100", which the Hinweisschild says too, or "DN ?"; a
    /// Löschwasserbehälter's volume, "100 m³", or else its Nummer or name; for open water its Nummer
    /// or name. Empty when OSM says nothing worth showing.
    /// </summary>
    public static string PrimaryText(WaterPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.Kind switch
        {
            WaterPointKind.Hydrant => DiameterText(point),
            WaterPointKind.Loeschwasserbehaelter when point.VolumeCubicMetres is { } volume => $"{Formatting.UngroupedNumber(volume)} m³",
            _ => point.Label,
        };
    }

    /// <summary>
    /// The full flag's second line, "Fahrbahn · A, 2×B · privat": whatever of the three OSM holds,
    /// empty when it holds none.
    /// </summary>
    public static string SecondaryText(WaterPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return string.Join(Separator, new[] { point.Lage, point.Outlets, point.IsPrivate ? "privat" : null }.OfType<string>());
    }

    /// <summary>"DN 100", or "DN ?" when the Nennweite is unknown.</summary>
    public static string DiameterText(WaterPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.NominalDiameter is { } dn ? $"DN {Formatting.UngroupedNumber(dn)}" : $"DN {Unknown}";
    }
}
