namespace LageBuch.AppLogic.Services.Map;

/// <summary>How the map shows the Kartenpaket's DGM5 heights; one at a time.</summary>
public enum TerrainLayer
{
    /// <summary>The plain map.</summary>
    None,

    /// <summary>Shaded relief: slopes away from the light darker, towards it lighter, flat ground untouched.</summary>
    Schummerung,

    /// <summary>Contour lines, every Zähllinie labelled with its height.</summary>
    Hoehenlinien,

    /// <summary>A colour per height across what is on screen, over a light relief.</summary>
    Hoehenschichten,

    /// <summary>Steep ground in three classes: what a vehicle cannot drive, Böschungen, Steilhänge.</summary>
    Hangneigung,

    /// <summary>Blue below the Einsatzort, red above it, with a line every few metres: the Förderhöhe at a glance.</summary>
    RelativZumEinsatzort,

    /// <summary>Where water or a spilled Gefahrstoff runs from the Einsatzort, over a light relief (<see cref="FlowPath"/>).</summary>
    Fliessweg,
}
