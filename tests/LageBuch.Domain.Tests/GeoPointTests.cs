using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

public class GeoPointTests
{
    [Fact]
    public void A_position_on_the_globe_keeps_its_coordinates()
    {
        var point = new GeoPoint(48.1785, 11.2555);

        Assert.Equal(48.1785, point.Latitude, precision: 6);
        Assert.Equal(11.2555, point.Longitude, precision: 6);
    }

    [Theory]
    [InlineData(90.0001, 0)]
    [InlineData(-90.0001, 0)]
    [InlineData(0, 180.0001)]
    [InlineData(0, -180.0001)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void A_position_off_the_globe_is_refused(double latitude, double longitude) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(latitude, longitude));

    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    public void The_poles_and_the_date_line_are_on_the_globe(double latitude, double longitude)
    {
        var point = new GeoPoint(latitude, longitude);

        Assert.Equal(latitude, point.Latitude, precision: 6);
        Assert.Equal(longitude, point.Longitude, precision: 6);
    }

    [Theory]
    [InlineData(91, 0, "latitude", "Latitude must lie between -90 and 90 degrees.")]
    [InlineData(0, 181, "longitude", "Longitude must lie between -180 and 180 degrees.")]
    public void A_refused_position_names_the_coordinate_and_its_range(double latitude, double longitude, string parameter, string message)
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(latitude, longitude));

        Assert.Equal(parameter, e.ParamName);
        Assert.StartsWith(message, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_points_at_the_same_place_are_equal() =>
        Assert.Equal(new GeoPoint(48, 11), new GeoPoint(48, 11));
}
