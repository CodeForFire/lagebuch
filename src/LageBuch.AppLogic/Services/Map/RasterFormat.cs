namespace LageBuch.AppLogic.Services.Map;

/// <summary>The two image formats a raster Kartenebene may be.</summary>
public enum RasterFormat
{
    /// <summary>A photo, such as the DOP40 Luftbild: opaque.</summary>
    Jpeg,

    /// <summary>A drawing with a transparent background, such as the Flurstücksgrenzen.</summary>
    Png,
}
