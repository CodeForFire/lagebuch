using LageBuch.Domain.ValueObjects;

namespace LageBuch.Persistence.Map.Elevation;

/// <summary>
/// The heights of every shown Kartenpaket as one surface. Packs overlap only in their buffer,
/// where both hold the same DGM5 cells, so the first pack that has a height answers.
/// </summary>
public sealed class ElevationSet(IReadOnlyList<GeoTiffElevation> grids)
{
    public IReadOnlyList<GeoTiffElevation> Grids { get; } = grids ?? throw new ArgumentNullException(nameof(grids));

    /// <summary>The height in metres above NHN at <paramref name="point"/>, or null where no pack has one.</summary>
    public double? HeightAt(GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var (easting, northing) = Utm32.FromGeo(point);
        return HeightAt(easting, northing);
    }

    /// <summary>Whether any pack's grid reaches into the EPSG:25832 rectangle.</summary>
    public bool Covers(double minX, double minY, double maxX, double maxY) =>
        Grids.Any(g => g.Bounds is { } b && b.MinX < maxX && b.MaxX > minX && b.MinY < maxY && b.MaxY > minY);

    /// <summary>The height at an EPSG:25832 position, or null where no pack has one.</summary>
    public double? HeightAt(double easting, double northing)
    {
        foreach (var grid in Grids)
        {
            if (grid.HeightAt(easting, northing) is { } height)
            {
                return height;
            }
        }

        return null;
    }
}
