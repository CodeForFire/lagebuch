using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class GeoBoundsTests
{
    private static readonly GeoBounds Box = new(48.0, 11.0, 48.2, 11.4);

    [Fact]
    public void The_bounds_of_points_are_their_extremes_on_both_hemispheres()
    {
        var bounds = GeoBounds.Of([new GeoPoint(-10, -20), new GeoPoint(30, 40), new GeoPoint(5, 5)]);

        Assert.Equal(new GeoBounds(-10, -20, 30, 40), bounds);
    }

    [Fact]
    public void A_union_reaches_both_boxes()
    {
        Assert.Equal(new GeoBounds(47.0, 10.0, 48.2, 11.4), Box.Union(new GeoBounds(47.0, 10.0, 48.1, 11.2)));
        Assert.Equal(new GeoBounds(48.0, 11.0, 49.0, 12.0), Box.Union(new GeoBounds(48.1, 11.1, 49.0, 12.0)));
    }

    [Fact]
    public void The_centre_is_halfway_on_both_axes()
    {
        Assert.Equal(48.1, Box.Center.Latitude, precision: 9);
        Assert.Equal(11.2, Box.Center.Longitude, precision: 9);
    }

    [Theory]
    [InlineData(48.1, 11.1, 48.3, 11.5, true)]
    [InlineData(48.2, 11.4, 48.3, 11.5, true)]
    [InlineData(47.0, 10.0, 48.0, 11.0, true)]
    [InlineData(48.3, 11.1, 48.4, 11.2, false)]
    [InlineData(47.0, 11.1, 47.9, 11.2, false)]
    [InlineData(48.1, 11.5, 48.2, 11.6, false)]
    [InlineData(48.1, 10.0, 48.2, 10.9, false)]
    public void Boxes_meet_when_they_overlap_or_touch(double south, double west, double north, double east, bool meet)
    {
        var other = new GeoBounds(south, west, north, east);

        Assert.Equal(meet, Box.Intersects(other));
        Assert.Equal(meet, other.Intersects(Box));
    }
}
