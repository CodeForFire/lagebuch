using LageBuch.Domain.ValueObjects;

namespace LageBuch.Persistence.Map.Elevation;

/// <summary>
/// ETRS89 / UTM zone 32N (EPSG:25832), the grid of every Bavarian DGM. A position is taken as
/// ETRS89; WGS84 differs from it by well under a metre, a fifth of one DGM5 cell.
/// </summary>
/// <remarks>Krüger's series to the fourth order of n, as in Karney (2011): millimetre-exact across the zone.</remarks>
public static class Utm32
{
    private const double A = 6378137.0;
    private const double F = 1 / 298.257222101; // GRS80
    private const double K0 = 0.9996;
    private const double FalseEasting = 500000.0;
    private const double CentralMeridian = 9.0;

    private const double N = F / (2 - F);

    // Stryker disable all : the fourth-order terms move a Bavarian position by micrometres, far below what any
    // test can tell from PROJ at the millimetre; they stay because the series is documented to that order.
    private static readonly double Rectifying = A / (1 + N) * (1 + (N * N / 4) + (Math.Pow(N, 4) / 64));
    private static readonly double E = 2 * Math.Sqrt(N) / (1 + N);
    private static readonly double[] Alpha =
    [
        (N / 2) - (2 * N * N / 3) + (5 * Math.Pow(N, 3) / 16) + (41 * Math.Pow(N, 4) / 180),
        (13 * N * N / 48) - (3 * Math.Pow(N, 3) / 5) + (557 * Math.Pow(N, 4) / 1440),
        (61 * Math.Pow(N, 3) / 240) - (103 * Math.Pow(N, 4) / 140),
        49561 * Math.Pow(N, 4) / 161280,
    ];

    // Stryker restore all

    /// <summary>Easting and northing in metres of <paramref name="point"/>.</summary>
    public static (double Easting, double Northing) FromGeo(GeoPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var phi = point.Latitude * Math.PI / 180;
        var lambda = (point.Longitude - CentralMeridian) * Math.PI / 180;
        var sinPhi = Math.Sin(phi);
        var t = Math.Sinh(Math.Atanh(sinPhi) - (E * Math.Atanh(E * sinPhi)));
        var xi = Math.Atan2(t, Math.Cos(lambda));
        var eta = Math.Atanh(Math.Sin(lambda) / Math.Sqrt(1 + (t * t)));
        var x = eta;
        var y = xi;

        // Stryker disable once Equality : the last term, α4, is of the fourth order too.
        for (var j = 1; j <= Alpha.Length; j++)
        {
            var k = 2.0 * j;
            x += Alpha[j - 1] * Math.Cos(k * xi) * Math.Sinh(k * eta);
            y += Alpha[j - 1] * Math.Sin(k * xi) * Math.Cosh(k * eta);
        }

        return (FalseEasting + (K0 * Rectifying * x), K0 * Rectifying * y);
    }
}
