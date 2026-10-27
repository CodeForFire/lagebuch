using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class RegionPackInfoTests
{
    private static readonly RegionPackInfo Ffb =
        new("Landkreis Fürstenfeldbruck", "ffb", "https://example.org/ffb.zip", 1, 48.0877067, 10.9930275, 48.2967233, 11.4128816, "2026-08-31", "OSM");

    [Theory]
    [InlineData(48.0877067, 10.9930275, true)]
    [InlineData(48.2967233, 11.4128816, true)]
    [InlineData(48.2, 11.2, true)]
    [InlineData(48.08, 11.2, false)]
    [InlineData(48.30, 11.2, false)]
    [InlineData(48.2, 10.99, false)]
    [InlineData(48.2, 11.42, false)]
    public void A_pack_covers_its_box_edges_included(double latitude, double longitude, bool covered) =>
        Assert.Equal(covered, Ffb.Covers(new GeoPoint(latitude, longitude)));
}
