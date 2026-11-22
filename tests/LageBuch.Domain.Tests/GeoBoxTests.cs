using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

public class GeoBoxTests
{
    [Fact]
    public void A_box_keeps_its_four_edges()
    {
        var box = new GeoBox(11.24, 48.17, 11.26, 48.18);

        Assert.Equal(11.24, box.West, precision: 9);
        Assert.Equal(48.17, box.South, precision: 9);
        Assert.Equal(11.26, box.East, precision: 9);
        Assert.Equal(48.18, box.North, precision: 9);
    }

    [Theory]
    [InlineData(11.26, 48.17, 11.24, 48.18)] // west of east swapped
    [InlineData(11.24, 48.18, 11.26, 48.17)] // south of north swapped
    [InlineData(11.24, 48.17, 11.24, 48.18)] // no width
    [InlineData(11.24, 48.17, 11.26, 48.17)] // no height
    [InlineData(double.NaN, 48.17, 11.26, 48.18)]
    [InlineData(11.24, 48.17, double.PositiveInfinity, 48.18)]
    [InlineData(-181, 48.17, 11.26, 48.18)]
    [InlineData(11.24, -91, 11.26, 48.18)]
    [InlineData(11.24, 48.17, 181, 48.18)]
    [InlineData(11.24, 48.17, 11.26, 91)]
    public void A_box_out_of_order_or_off_the_globe_is_refused(double west, double south, double east, double north)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoBox(west, south, east, north));
        Assert.Null(GeoBox.TryCreate(west, south, east, north));
    }

    [Fact]
    public void TryCreate_needs_all_four_edges()
    {
        Assert.Null(GeoBox.TryCreate(null, 48.17, 11.26, 48.18));
        Assert.Null(GeoBox.TryCreate(11.24, null, 11.26, 48.18));
        Assert.Null(GeoBox.TryCreate(11.24, 48.17, null, 48.18));
        Assert.Null(GeoBox.TryCreate(11.24, 48.17, 11.26, null));
        Assert.Equal(new GeoBox(11.24, 48.17, 11.26, 48.18), GeoBox.TryCreate(11.24, 48.17, 11.26, 48.18));
    }

    [Fact]
    public void A_box_may_reach_the_edges_of_the_globe()
    {
        var world = new GeoBox(-180, -90, 180, 90);

        Assert.Equal(180, world.East, precision: 9);
        Assert.Equal(-90, world.South, precision: 9);
    }
}
