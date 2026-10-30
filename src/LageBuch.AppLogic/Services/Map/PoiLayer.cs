namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// When the map names the places an Einsatz cares about -- Feuerwachen, Krankenhäuser, Schulen,
/// Tankstellen. The drawing and the legend use the same threshold, so the legend explains the
/// dots exactly when there are some.
/// </summary>
public static class PoiLayer
{
    /// <summary>Further out, a Landkreis' Kitas and Apotheken crowd out the street names.</summary>
    public const int MinZoom = 15;
}
