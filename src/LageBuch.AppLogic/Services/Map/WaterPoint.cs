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

/// <summary>
/// A Wasserentnahmestelle from a Kartenpaket's <c>water.geojson</c>, as OpenStreetMap has it.
/// <see cref="Label"/> is the short text drawn beside it on a close map ("UH 100", a name, a
/// Nummer), empty when OSM says nothing worth showing. <see cref="Details"/> is what its card
/// lists, in German, each value as OSM spells it unless it is one of the well-known ones. OSM is
/// not the Wasserversorger's register.
/// </summary>
public sealed record WaterPoint(GeoPoint Position, WaterPointKind Kind, string Label)
{
    /// <summary>The card's rows: Bauart, Durchmesser, Nummer and whatever else OSM holds.</summary>
    public IReadOnlyList<WaterPointDetail> Details { get; init; } = [];

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
