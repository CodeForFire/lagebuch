using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;

namespace LageBuch.Persistence.Tests;

public sealed class Utm32Tests
{
    // Reference values from PROJ (pyproj 3, EPSG:4258 to EPSG:25832).
    [Theory]
    [InlineData(48.1785, 11.2555, 667664.191, 5338600.010)] // Fürstenfeldbruck
    [InlineData(47.4210, 10.9863, 649816.833, 5253862.613)] // Zugspitze
    [InlineData(50.3, 9.0, 500000.000, 5571986.941)] // on the central meridian
    [InlineData(49.0, 13.8, 851027.015, 5438565.015)] // the Bayerischer Wald, far east in zone 32
    public void A_position_lands_on_the_millimetre_PROJ_puts_it(double latitude, double longitude, double easting, double northing)
    {
        var (e, n) = Utm32.FromGeo(new GeoPoint(latitude, longitude));

        Assert.True(Math.Abs(e - easting) < 0.002, $"easting {e}, expected {easting}");
        Assert.True(Math.Abs(n - northing) < 0.002, $"northing {n}, expected {northing}");
    }
}
