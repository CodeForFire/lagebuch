namespace LageBuch.Domain.Map;

/// <summary>What a Kartenebene's file holds, and so how the Lagekarte draws it.</summary>
public enum MapOverlayKind
{
    /// <summary>GeoJSON shapes, such as a DWD Ausbreitungsprognose. What every Ebene was before the Luftbild.</summary>
    Vector = 0,

    /// <summary>One image, a JPEG or PNG, laid on the map at its <see cref="MapOverlay.Bounds"/>: a Luftbild or Flurstücksgrenzen.</summary>
    Raster = 1,
}
