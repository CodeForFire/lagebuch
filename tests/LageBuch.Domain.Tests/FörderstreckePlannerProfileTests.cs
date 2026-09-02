using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Domain.Tests;

public class FörderstreckePlannerProfileTests
{
    private static readonly FörderstreckeConfig Default = FörderstreckeConfig.Default;

    [Fact]
    public void PlanFromProfile_flat_multipoint_profile_matches_the_2point_case()
    {
        var profile = new[]
        {
            new ElevationProfileSample(0, 0),
            new ElevationProfileSample(500, 0),
            new ElevationProfileSample(1000, 0),
            new ElevationProfileSample(1500, 0),
            new ElevationProfileSample(2000, 0),
        };

        var plan = FörderstreckePlanner.PlanFromProfile(profile, Default);

        Assert.Equal(new[] { 0.0, 620, 1240, 1860 }, plan.PumpPositionsMeters);
        Assert.Equal(3, plan.PumpCount);
    }

    [Fact]
    public void PlanFromProfile_interior_crest_forces_a_pump_the_flat_endpoints_would_miss()
    {
        // Net rise is 0 (climbs 50 m over the first 200 m, then descends back down over the next
        // 200 m) -- a flat 2-point Plan(400, 0, ...) needs zero pumps for this length. But the
        // crest at 200 m costs 0.01*200 + 0.1*50 = 7.0 bar, over the 6.305 bar leg budget, even
        // though the endpoint-to-endpoint cost (400 m, net 0 m) is only 4.0 bar.
        var profile = new[]
        {
            new ElevationProfileSample(0, 0),
            new ElevationProfileSample(200, 50),
            new ElevationProfileSample(400, 0),
        };

        var plan = FörderstreckePlanner.PlanFromProfile(profile, Default);

        Assert.Equal(new[] { 0.0, 180 }, plan.PumpPositionsMeters);
        Assert.Equal(1, plan.PumpCount);
        Assert.Equal(20, plan.HoseCount);
        Assert.Equal(4, plan.ReserveHoseCount);
        Assert.Equal(1, plan.ReservePumpCount);
    }
}
