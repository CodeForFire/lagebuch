using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>What a Wasserentnahmestelle is, by its OSM <c>emergency</c> tag.</summary>
public enum WaterPointKind
{
    /// <summary><c>fire_hydrant</c>.</summary>
    Hydrant,

    /// <summary><c>suction_point</c>: a Saugstelle at open water.</summary>
    Saugstelle,

    /// <summary><c>water_tank</c>: a Löschwasserbehälter or Zisterne.</summary>
    Loeschwasserbehaelter,

    /// <summary><c>fire_water_pond</c>.</summary>
    Loeschteich,
}

/// <summary>The glyph a water point's marker carries in its coloured field: UH, ÜH, WH, SS, BR, ZI, OW.</summary>
public enum WaterPointSymbol
{
    /// <summary>UH; also a hydrant whose Bauart OSM does not say, the commonest kind.</summary>
    Unterflurhydrant,

    /// <summary>ÜH.</summary>
    Ueberflurhydrant,

    /// <summary>WH.</summary>
    Wandhydrant,

    /// <summary>SS.</summary>
    Saugstelle,

    /// <summary>BR: a Saugstelle drawing on groundwater.</summary>
    Loeschwasserbrunnen,

    /// <summary>ZI: a Löschwasserbehälter.</summary>
    Zisterne,

    /// <summary>OW: a Löschteich.</summary>
    OffenesGewaesser,
}

/// <summary>
/// A Wasserentnahmestelle from a Kartenpaket's <c>water.geojson</c>, as OpenStreetMap has it.
/// <see cref="Label"/> is the short text drawn beside it on a close map ("UH 100", a name, a
/// Nummer), empty when OSM says nothing worth showing. <see cref="Details"/> is what its card
/// lists, in German, each value as OSM spells it unless it is one of the well-known ones. OSM is
/// not the Wasserversorger's register.
/// </summary>
public sealed record WaterPoint(GeoPoint Position, WaterPointKind Kind, string Label)
{
    private readonly WaterPointSymbol? symbol;

    /// <summary>The card's rows: Bauart, Durchmesser, Nummer and whatever else OSM holds.</summary>
    public IReadOnlyList<WaterPointDetail> Details { get; init; } = [];

    /// <summary>The marker's glyph; unless set, the one <see cref="Kind"/> stands for.</summary>
    public WaterPointSymbol Symbol
    {
        get => symbol ?? Kind switch
        {
            WaterPointKind.Saugstelle => WaterPointSymbol.Saugstelle,
            WaterPointKind.Loeschwasserbehaelter => WaterPointSymbol.Zisterne,
            WaterPointKind.Loeschteich => WaterPointSymbol.OffenesGewaesser,
            _ => WaterPointSymbol.Unterflurhydrant,
        };
        init => symbol = value;
    }

    /// <summary>
    /// Fed from somewhere other than the Trinkwassernetz: open water, a well or a tank, which a
    /// burst main or a weak network does not take down with the hydrants.
    /// </summary>
    public bool IsIndependent => Kind != WaterPointKind.Hydrant;

    /// <summary>The Nennweite of the main, DN; null when unknown.</summary>
    public int? NominalDiameter { get; init; }

    /// <summary>A Löschwasserbehälter's volume in m³; null when unknown.</summary>
    public int? VolumeCubicMetres { get; init; }

    /// <summary>Where it sits, "Fahrbahn", "Gehweg", "Grünfläche" or "Parkplatz"; null for anything else.</summary>
    public string? Lage { get; init; }

    /// <summary>Its outlets by Storz size, "A, 2×B", or only how many, "2 Abgänge"; null when unknown.</summary>
    public string? Outlets { get; init; }

    /// <summary>On private ground: OSM says <c>access=private</c> or <c>no</c>.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// Außer Betrieb. A pack never holds one: its build drops every point OSM marks disused or
    /// abandoned.
    /// </summary>
    public bool IsOutOfService { get; init; }

    /// <summary>"Unterflurhydrant", "Saugstelle": what the card is headed with.</summary>
    public string Title => Kind switch
    {
        WaterPointKind.Hydrant => Details.FirstOrDefault(d => d.Label == WaterPointDetail.TypeLabel)?.Value switch
        {
            "Unterflur" => "Unterflurhydrant",
            "Überflur" => "Überflurhydrant",
            "Wand" => "Wandhydrant",
            _ => "Hydrant",
        },
        WaterPointKind.Saugstelle => "Saugstelle",
        WaterPointKind.Loeschwasserbehaelter => "Löschwasserbehälter",
        _ => "Löschteich",
    };
}
