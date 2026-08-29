using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Domain.Tests;

public class FörderstreckePlannerElevationTests
{
    private static readonly FörderstreckeConfig Default = FörderstreckeConfig.Default;

    [Fact]
    public void Plan_uphill_shortens_legs_and_adds_pumps()
    {
        var plan = FörderstreckePlanner.Plan(2000, 100, Default);

        // head = 0.01 fric + 0.1*100/2000 rise = 0.015 bar/m -> leg = 6.305/0.015 = 420.3 -> 420 m
        Assert.Equal(new[] { 0.0, 420, 840, 1260, 1680 }, plan.PumpPositionsMeters);
        Assert.Equal(4, plan.PumpCount); // 3 on the flat route of the same length
    }

    [Fact]
    public void Plan_downhill_lengthens_legs_and_needs_fewer_pumps()
    {
        var plan = FörderstreckePlanner.Plan(2000, -100, Default);

        // head = 0.01 - 0.005 = 0.005 bar/m -> leg = 6.305/0.005 = 1261 -> 1260 m
        Assert.Equal(new[] { 0.0, 1260 }, plan.PumpPositionsMeters);
        Assert.Equal(1, plan.PumpCount);
    }

    [Fact]
    public void Plan_descent_cancelling_friction_fits_in_one_leg()
    {
        // 0.1*200/2000 exactly offsets the 0.01 friction loss -> a single leg reaches the end.
        var plan = FörderstreckePlanner.Plan(2000, -200, Default);

        Assert.Equal(new[] { 0.0 }, plan.PumpPositionsMeters);
        Assert.Equal(0, plan.PumpCount);
        Assert.Equal(100, plan.HoseCount);
    }

    [Fact]
    public void Plan_rejects_a_climb_too_steep_for_a_single_hose()
    {
        // 0.1*400/100 = 0.4 + 0.01 fric -> 0.41 bar/m -> leg 15.4 m < one 20 m hose.
        Assert.Throws<ArgumentException>(() => FörderstreckePlanner.Plan(100, 400, Default));
    }
}