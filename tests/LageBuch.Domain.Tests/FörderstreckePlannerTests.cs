using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Domain.Tests;

public class FörderstreckePlannerTests
{
    private static readonly FörderstreckeConfig Default = FörderstreckeConfig.Default;

    [Fact]
    public void Plan_flat_route_counts_hoses_by_20m_lengths()
    {
        var plan = FörderstreckePlanner.Plan(2000, 0, Default);

        Assert.Equal(100, plan.HoseCount);                    // ceil(2000/20)
        Assert.Equal(20, plan.ReserveHoseCount);              // ceil(2000/100)
    }

    [Fact]
    public void Plan_flat_route_spaces_pumps_at_about_630m_excluding_the_feed_pump()
    {
        var plan = FörderstreckePlanner.Plan(2000, 0, Default);

        // budget = (8 - 1.5) * 0.97 = 6.305 bar; at 1.0 bar/100m -> 630.5 m, snapped to 620 m.
        Assert.Equal(new[] { 0.0, 620, 1240, 1860 }, plan.PumpPositionsMeters);
        Assert.Equal(3, plan.PumpCount);                       // positions minus the feed pump at 0
    }

    [Fact]
    public void Plan_short_line_needs_no_Verstaerkerpumpe()
    {
        var plan = FörderstreckePlanner.Plan(400, 0, Default);

        Assert.Equal(20, plan.HoseCount);
        Assert.Equal(0, plan.PumpCount);
        Assert.Equal(new[] { 0.0 }, plan.PumpPositionsMeters);
    }

    [Fact]
    public void Plan_computes_one_reserve_pump_per_four_Verstaerkerpumpen()
    {
        var few = FörderstreckePlanner.Plan(4000, 0, Default);          // 6 pumps -> ceil(6/4) = 2
        var none = FörderstreckePlanner.Plan(400, 0, Default);          // 0 pumps -> 0

        Assert.Equal(2, few.ReservePumpCount);
        Assert.Equal(0, none.ReservePumpCount);
    }

    [Fact]
    public void Plan_rejects_zero_length_and_out_of_table_flow()
    {
        Assert.Throws<ArgumentException>(() => FörderstreckePlanner.Plan(0, 0, Default));
        Assert.Throws<ArgumentException>(() => FörderstreckePlanner.Plan(2000, 0, Default with { FlowLMin = 1500 }));
    }
}