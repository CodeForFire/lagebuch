namespace LageBuch.Domain.Wasserfoerderung;

/// <summary>
/// Pure engine that places Verstärkerpumpen along a Förderstrecke (#150). <see cref="Plan"/>
/// (Plan A) takes a total length and a single net elevation rise, treated as a uniform gradient;
/// <see cref="PlanFromProfile"/> (Plan B) walks an actual sampled terrain profile instead, so an
/// interior crest is caught even when the endpoints alone look fine. Physics is linear and
/// therefore testable without deps:
///
///   head-loss per leg = friction + elevation     friction = loss/100m at flow
///   usable budget    = (feed − inlet) · (1 − headroom)
///   legs snap down to whole hoses; every pump restarts from feed pressure (closed Schaltreihe),
///   the feed pump at the water source is NOT counted as a Verstärkerpumpe (user decision).
/// </summary>
public static class FörderstreckePlanner
{
    // Verified B-75 mm table: bar per 100 m at 200/400/600/800/1000/1200 l/min (midpoints).
    private static readonly double[] FlowPoints = { 200, 400, 600, 800, 1000, 1200 };
    private static readonly double[] LossBarPer100M = { 0.1, 0.3, 0.6, 1.0, 1.5, 2.25 };

    // 10 m elevation rise costs 1 bar, so 0.1 bar per meter of rise.
    private const double ElevationBarPerMeter = 0.1;

    public static FörderstreckePlan Plan(double lengthM, double riseM, FörderstreckeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (lengthM <= 0)
            throw new ArgumentException("Die Förderstrecke muss länger als 0 m sein.", nameof(lengthM));

        return PlanFromProfile(
            new[] { new ElevationProfileSample(0, 0), new ElevationProfileSample(lengthM, riseM) },
            config);
    }

    /// <summary>
    /// Plan B (#150 phase 2): same physics as <see cref="Plan"/>, but walks a sampled terrain
    /// profile instead of assuming one uniform gradient, so an interior crest (climb then
    /// descend back to the same net height) is caught even when the leg's endpoints alone would
    /// look fine.
    /// </summary>
    public static FörderstreckePlan PlanFromProfile(
        IReadOnlyList<ElevationProfileSample> profile, FörderstreckeConfig config)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(config);
        if (profile.Count < 2)
            throw new ArgumentException("Das Höhenprofil braucht mindestens zwei Punkte.", nameof(profile));

        var lengthM = profile[^1].DistanceMeters;
        if (lengthM <= 0)
            throw new ArgumentException("Die Förderstrecke muss länger als 0 m sein.", nameof(profile));

        var lossPerMeter = LossPer100Meters(config.FlowLMin) / 100;
        var budgetPerLeg = BudgetPerLegBar(config);
        var hoseLen = config.HoseLengthMeters;
        var hoseCount = (int)Math.Ceiling(lengthM / hoseLen);
        var reserveHoseCount = (int)Math.Ceiling(lengthM / 100);

        double Cost(double a, double b)
        {
            // The binding constraint is the worst (highest cumulative) pressure drop anywhere
            // along the leg, not just at its endpoint — an interior crest can exceed budget even
            // when the leg nets back down to a fine endpoint value.
            var worst = double.NegativeInfinity;
            foreach (var sample in profile)
            {
                if (sample.DistanceMeters > a && sample.DistanceMeters < b)
                    worst = Math.Max(worst, CumulativeLossBar(a, sample.DistanceMeters));
            }

            return Math.Max(worst, CumulativeLossBar(a, b));
        }

        double CumulativeLossBar(double a, double d) =>
            lossPerMeter * (d - a) + ElevationBarPerMeter * (ElevationAt(profile, d) - ElevationAt(profile, a));

        var positions = new List<double> { 0 };
        var pos = 0.0;
        while (pos < lengthM)
        {
            var remaining = lengthM - pos;
            if (remaining >= hoseLen && Cost(pos, lengthM) <= budgetPerLeg)
            {
                pos = lengthM;
                break;
            }

            var reach = 0.0;
            while (pos + reach + hoseLen <= lengthM && Cost(pos, pos + reach + hoseLen) <= budgetPerLeg)
                reach += hoseLen;

            if (reach == 0)
                throw new ArgumentException(
                    "Die Steigung ist zu stark — ein B-Schlauch (20 m) trägt das Gefälle bereits über das Druckbudget.");

            pos += reach;
            if (pos < lengthM)
                positions.Add(pos);
        }

        var pumpCount = Math.Max(0, positions.Count - 1); // exclude the feed pump at 0
        var reservePumpCount = (int)Math.Ceiling((double)pumpCount / config.ReservePumpEveryNPumps);

        return new FörderstreckePlan(lengthM, hoseCount, reserveHoseCount, pumpCount, reservePumpCount, positions);
    }

    private static double ElevationAt(IReadOnlyList<ElevationProfileSample> profile, double distanceM)
    {
        for (var i = 0; i < profile.Count - 1; i++)
        {
            var a = profile[i];
            var b = profile[i + 1];
            if (distanceM <= b.DistanceMeters)
            {
                var t = (distanceM - a.DistanceMeters) / (b.DistanceMeters - a.DistanceMeters);
                return a.ElevationMeters + t * (b.ElevationMeters - a.ElevationMeters);
            }
        }

        return profile[^1].ElevationMeters;
    }

    private static double BudgetPerLegBar(FörderstreckeConfig config) =>
        (config.FeedPressureBar - config.InletPressureBar) * (1 - config.HeadroomPercent);

    private static double LossPer100Meters(int flowLMin)
    {
        if (flowLMin < FlowPoints[0] || flowLMin > FlowPoints[^1])
            throw new ArgumentException(
                $"Durchfluss {flowLMin} l/min liegt außerhalb der Tabelle (200–1200 l/min).", nameof(flowLMin));

        var flow = (double)flowLMin;
        for (var i = 0; i < FlowPoints.Length - 1; i++)
        {
            if (flow <= FlowPoints[i + 1])
            {
                var t = (flow - FlowPoints[i]) / (FlowPoints[i + 1] - FlowPoints[i]);
                return LossBarPer100M[i] + t * (LossBarPer100M[i + 1] - LossBarPer100M[i]);
            }
        }
        return LossBarPer100M[^1];
    }
}