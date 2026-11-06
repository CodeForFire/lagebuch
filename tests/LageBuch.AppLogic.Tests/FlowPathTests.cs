using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;
using LageBuch.Persistence.Tests;

namespace LageBuch.AppLogic.Tests;

public sealed class FlowPathTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-flow-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // A 1 km square of 5 m cells west and south of the Marktplatz's grid position.
    private static readonly (double East, double North) Origin = TerrainFixture.MarktplatzUtm;

    [Fact]
    public void On_a_plane_water_runs_straight_down_it_to_the_edge_of_the_heights()
    {
        // Falling 1 m every 10 m to the east.
        var elevation = Terrain((e, _) => 600 - ((e - Origin.East) / 10));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-400, 0)));

        Assert.Equal(FlowPathEnd.Kartenrand, route.End);
        Assert.All(route.Points, p => Assert.InRange(Utm32.FromGeo(p).Northing - Origin.North, -5, 5));
        Assert.True(Utm32.FromGeo(route.Points[^1]).Easting > Origin.East + 480);
        Assert.InRange(route.LengthMetres, 880, 905);
        Assert.InRange(route.DropMetres, 88, 91);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, 1)]
    [InlineData(-1, -1)]
    public void Water_takes_each_of_the_eight_directions_a_plane_falls_in(int east, int north)
    {
        var elevation = Terrain((e, n) => 600 - (((east * (e - Origin.East)) + (north * (n - Origin.North))) / 10));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(0, 0)));

        var (first, second) = (Utm32.FromGeo(route.Points[0]), Utm32.FromGeo(route.Points[1]));
        Assert.Equal(east * 5.0, second.Easting - first.Easting, precision: 3);
        Assert.Equal(north * 5.0, second.Northing - first.Northing, precision: 3);

        // A diagonal step is √2 cells long.
        var step = east != 0 && north != 0 ? 5 * Math.Sqrt(2) : 5;
        Assert.Equal((route.Points.Count - 1) * step, route.LengthMetres, precision: 3);
    }

    [Fact]
    public void Down_a_slope_into_a_valley_the_water_turns_along_the_valley_floor()
    {
        // A valley along the west-east line through the origin, its floor falling to the east.
        var elevation = Terrain((e, n) => 600 + (Math.Abs(n - Origin.North) / 5) - ((e - Origin.East) / 50));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-300, 200)));

        var floor = route.Points.Select(Utm32.FromGeo).SkipWhile(p => Math.Abs(p.Northing - Origin.North) > 5).ToList();
        Assert.NotEmpty(floor);
        Assert.All(floor, p => Assert.InRange(p.Northing - Origin.North, -5, 5));
        Assert.True(floor[^1].Easting - floor[0].Easting > 400);
    }

    [Fact]
    public void In_a_hollow_deeper_than_the_fill_depth_the_water_collects()
    {
        // A bowl 6 m deep, 200 m across.
        var elevation = Terrain((e, n) => (float)(600 - (6 * Math.Max(0, 1 - (Distance(e, n) / 100)))));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-60, 30)));

        Assert.Equal(FlowPathEnd.Senke, route.End);
        Assert.InRange(Distance(Utm32.FromGeo(route.Points[^1])), 0, 8);
        Assert.InRange(route.DropMetres, 2.5, 4);
    }

    [Fact]
    public void On_flat_ground_the_water_stays_where_it_is()
    {
        var elevation = Terrain((_, _) => 550);

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(0, 0)));

        Assert.Equal(FlowPathEnd.Senke, route.End);
        Assert.Single(route.Points);
        Assert.Equal(0, route.LengthMetres, precision: 6);
    }

    [Fact]
    public void A_hollow_deeper_than_the_fill_depth_holds_the_water_even_on_a_slope()
    {
        // The plane falls to the east, but a bowl 6 m deep sits in it: the water does not climb out.
        var elevation = Terrain((e, n) => (float)(600 - ((e - Origin.East) / 10) - (Math.Max(0, 1 - (Distance(e, n) / 60)) * 12)));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-200, 0)));

        Assert.Equal(FlowPathEnd.Senke, route.End);
        Assert.InRange(Distance(Utm32.FromGeo(route.Points[^1])), 0, 15);
    }

    [Fact]
    public void A_shallow_hollow_on_the_way_is_filled_and_crossed()
    {
        // A plane falling to the east with a dip 1 m deep and 60 m across on it: an embankment's
        // culvert the DGM does not know.
        var elevation = Terrain((e, n) => (float)(600 - ((e - Origin.East) / 10) - (Math.Max(0, 1 - (Distance(e, n) / 30)) * 4)));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-200, 0)));

        Assert.Equal(FlowPathEnd.Kartenrand, route.End);
        Assert.True(Utm32.FromGeo(route.Points[^1]).Easting > Origin.East + 480);
    }

    [Fact]
    public void The_route_is_followed_no_further_than_the_cap()
    {
        // Falling 1 m every 100 m across 4 km.
        var grid = new GeoTiffFixtures.Grid(Origin.East - 2000, Origin.North + 100, 5, 800, 40);
        var elevation = Terrain(grid, (e, _) => 600 - ((e - Origin.East) / 100));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-1950, 0)));

        Assert.Equal(FlowPathEnd.MaxLength, route.End);
        Assert.InRange(route.LengthMetres, FlowPath.MaxLength, FlowPath.MaxLength + 10);
    }

    [Fact]
    public void Where_there_is_no_height_there_is_no_route()
    {
        var elevation = Terrain((_, _) => 600);

        Assert.Null(FlowPath.Trace(elevation, new GeoPoint(52.52, 13.40)));
    }

    [Fact]
    public void Each_cell_of_the_route_is_next_to_the_one_before()
    {
        var elevation = Terrain((e, n) => (float)(600 - ((e - Origin.East) / 10) - (Math.Max(0, 1 - (Distance(e, n) / 30)) * 4)));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-200, 0)));

        var cells = route.Points.Select(Utm32.FromGeo).ToList();
        for (var i = 1; i < cells.Count; i++)
        {
            Assert.InRange(Math.Abs(cells[i].Easting - cells[i - 1].Easting), 0, 5.01);
            Assert.InRange(Math.Abs(cells[i].Northing - cells[i - 1].Northing), 0, 5.01);
        }
    }

    [Fact]
    public void Where_the_water_reaches_a_Gewaesser_the_route_ends_there()
    {
        // A slope falling 1 m every 10 m to the east, down to a river 200 m east whose surface is
        // flat in the DGM: there the heights cannot say where it goes on.
        var elevation = Terrain((e, _) => Math.Max(580, 600 - ((e - Origin.East) / 10)));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-200, 0), EastOf(200, "Amper")));

        Assert.Equal(FlowPathEnd.Gewaesser, route.End);
        Assert.Equal("Amper", route.WaterName);
        Assert.InRange(Utm32.FromGeo(route.Points[^1]).Easting - Origin.East, 200, 205);
        Assert.InRange(route.LengthMetres, 400, 410);
        Assert.InRange(route.DropMetres, 39, 40.5);
    }

    [Fact]
    public void A_spill_out_of_a_hollow_ends_at_the_first_cell_in_the_water()
    {
        // The shallow dip of the culvert test, with a Gewässer starting on its far side.
        var elevation = Terrain((e, n) => (float)(600 - ((e - Origin.East) / 10) - (Math.Max(0, 1 - (Distance(e, n) / 30)) * 4)));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-200, 0), EastOf(10, "Weiher")));

        Assert.Equal(FlowPathEnd.Gewaesser, route.End);
        Assert.Equal("Weiher", route.WaterName);
        Assert.InRange(Utm32.FromGeo(route.Points[^1]).Easting - Origin.East, 10, 15);
    }

    [Fact]
    public void An_Einsatzort_in_the_water_is_its_own_route()
    {
        var elevation = Terrain((e, _) => 600 - ((e - Origin.East) / 10));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(0, 0), EastOf(-100, string.Empty)));

        Assert.Equal(FlowPathEnd.Gewaesser, route.End);
        Assert.Equal(string.Empty, route.WaterName);
        Assert.Single(route.Points);
        Assert.Equal(0, route.DropMetres, precision: 6);
    }

    [Fact]
    public void A_route_that_ends_on_land_names_no_Gewaesser()
    {
        var elevation = Terrain((e, _) => 600 - ((e - Origin.East) / 10));

        var route = Assert.IsType<FlowRoute>(FlowPath.Trace(elevation, At(-400, 0), _ => null));

        Assert.Equal(FlowPathEnd.Kartenrand, route.End);
        Assert.Null(route.WaterName);
    }

    // Water everywhere east of `metres` east of the origin.
    private static Func<GeoPoint, string?> EastOf(double metres, string name) =>
        p => Utm32.FromGeo(p).Easting - Origin.East >= metres ? name : null;

    private static double Distance(double east, double north) =>
        Math.Sqrt(((east - Origin.East) * (east - Origin.East)) + ((north - Origin.North) * (north - Origin.North)));

    private static double Distance((double Easting, double Northing) p) => Distance(p.Easting, p.Northing);

    private static GeoPoint At(double east, double north) => Utm32.ToGeo(Origin.East + east, Origin.North + north);

    private ElevationSet Terrain(Func<double, double, double> height) =>
        Terrain(new GeoTiffFixtures.Grid(Origin.East - 500, Origin.North + 500, 5, 200, 200), height);

    private ElevationSet Terrain(GeoTiffFixtures.Grid grid, Func<double, double, double> height)
    {
        var path = GeoTiffFixtures.Write(Path.Join(_dir, $"{Guid.NewGuid():N}.tif"), grid, (e, n) => (float)height(e, n), tileSide: 64);
        return new ElevationSet([new GeoTiffElevation(path)]);
    }
}
