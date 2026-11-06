using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;
using LageBuch.Persistence.Tests;

namespace LageBuch.AppLogic.Tests;

public sealed class ElevationProfileTests : IDisposable
{
    private static readonly (double East, double North) Origin = TerrainFixture.MarktplatzUtm;

    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-profile-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void On_a_plane_the_profile_rises_evenly_a_sample_every_5_m()
    {
        // Rising 1 m every 10 m to the east; the line runs 400 m east.
        var elevation = Terrain((e, _) => 500 + ((e - Origin.East) / 10));
        var (from, to) = (At(-200, 0), At(200, 0));

        var profile = ElevationProfile.Along(elevation, from, to);

        Assert.InRange(profile.Length, 399, 401);
        Assert.Equal((int)Math.Ceiling(profile.Length / ElevationProfile.Step) + 1, profile.Heights.Count);
        Assert.InRange(profile.Distances[1], 4.9, 5.1);
        Assert.InRange(Assert.NotNull(profile.Rise), 39.5, 40.5);
        Assert.InRange(profile.Climb, 39.5, 40.5);
        var top = Assert.NotNull(profile.Highest);
        Assert.Equal(profile.Length, top.Distance, precision: 6);
        Assert.False(profile.HasGaps);
    }

    [Fact]
    public void Over_a_hill_the_climb_counts_the_way_up_even_where_the_end_lies_lower()
    {
        // 20 m up to a crest in the middle, 30 m down beyond it.
        var elevation = Terrain((e, _) =>
        {
            var x = e - Origin.East;
            return x < 0 ? 520 + (x / 10) : 520 - (x * 0.15);
        });

        var profile = ElevationProfile.Along(elevation, At(-200, 0), At(200, 0));

        Assert.InRange(Assert.NotNull(profile.Rise), -10.5, -9.5);
        Assert.InRange(profile.Climb, 19, 20.5);
        var top = Assert.NotNull(profile.Highest);
        Assert.InRange(top.Height, 519.5, 520.5);
        Assert.InRange(top.Distance, 195, 205);
    }

    [Fact]
    public void Ripples_smaller_than_a_metre_do_not_add_up_to_a_climb()
    {
        // Flat but rippling 0.8 m every 10 m: no hill to pump over.
        var elevation = Terrain((e, _) => 500 + (0.4 * Math.Sin((e - Origin.East) * Math.PI / 5)));

        var profile = ElevationProfile.Along(elevation, At(-200, 0), At(200, 0));

        Assert.Equal(0, profile.Climb, precision: 6);
    }

    [Fact]
    public void Where_the_line_leaves_the_heights_the_profile_has_gaps_and_no_end()
    {
        var elevation = Terrain((_, _) => 500);

        var profile = ElevationProfile.Along(elevation, At(0, 0), new GeoPoint(48.1785, 11.40));

        Assert.True(profile.HasGaps);
        Assert.NotNull(profile.StartHeight);
        Assert.Null(profile.EndHeight);
        Assert.Null(profile.Rise);
    }

    [Fact]
    public void A_very_long_line_is_sampled_more_coarsely_never_more_than_the_cap()
    {
        var elevation = Terrain((_, _) => 500);

        var profile = ElevationProfile.Along(elevation, new GeoPoint(48.0, 11.0), new GeoPoint(48.2, 11.3));

        Assert.Equal(ElevationProfile.MaxSamples, profile.Heights.Count);
        Assert.Equal(profile.Distances.Count, profile.Heights.Count);
    }

    [Fact]
    public void A_line_of_no_length_is_one_step_from_the_point_to_itself()
    {
        var elevation = Terrain((_, _) => 500);
        var at = At(0, 0);

        var profile = ElevationProfile.Along(elevation, at, at);

        Assert.Equal(2, profile.Heights.Count);
        Assert.Equal(0, profile.Length, precision: 6);
    }

    // The position at an offset from the origin in metres, found by Newton's method on the forward projection.
    private static GeoPoint At(double east, double north)
    {
        var guess = TerrainFixture.Marktplatz;
        for (var i = 0; i < 6; i++)
        {
            var (e, n) = Utm32.FromGeo(guess);
            var (e2, _) = Utm32.FromGeo(new GeoPoint(guess.Latitude, guess.Longitude + 1e-5));
            var (_, n2) = Utm32.FromGeo(new GeoPoint(guess.Latitude + 1e-5, guess.Longitude));
            guess = new GeoPoint(
                guess.Latitude + ((Origin.North + north - n) / ((n2 - n) / 1e-5)),
                guess.Longitude + ((Origin.East + east - e) / ((e2 - e) / 1e-5)));
        }

        return guess;
    }

    private ElevationSet Terrain(Func<double, double, double> height)
    {
        var grid = new GeoTiffFixtures.Grid(Origin.East - 500, Origin.North + 500, 5, 200, 200);
        var path = GeoTiffFixtures.Write(Path.Join(_dir, $"{Guid.NewGuid():N}.tif"), grid, (e, n) => (float)height(e, n), tileSide: 64);
        return new ElevationSet([new GeoTiffElevation(path)]);
    }
}
