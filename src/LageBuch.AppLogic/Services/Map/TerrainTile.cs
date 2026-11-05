namespace LageBuch.AppLogic.Services.Map;

/// <summary>One drawn terrain tile: a translucent image to lay over the map, and lines to draw on it.</summary>
/// <param name="Pixels">256 × 256 premultiplied BGRA pixels, row by row; null for a layer of lines only.</param>
/// <param name="Contours">The contour lines, empty for a layer without.</param>
public sealed record TerrainTile(ReadOnlyMemory<int>? Pixels, IReadOnlyList<ContourLevel> Contours);

/// <summary>A contour line's pieces in one tile, in tile pixels.</summary>
/// <param name="Height">The height it runs at, in metres.</param>
/// <param name="IsMajor">A Zähllinie, drawn heavier and labelled; for relative contours, the Einsatzort's own height.</param>
/// <param name="Label">The text the line carries on the map; empty for a line drawn without one.</param>
/// <param name="Segments">Its straight pieces, each from (X1, Y1) to (X2, Y2).</param>
public sealed record ContourLevel(double Height, bool IsMajor, string Label, IReadOnlyList<(double X1, double Y1, double X2, double Y2)> Segments);
