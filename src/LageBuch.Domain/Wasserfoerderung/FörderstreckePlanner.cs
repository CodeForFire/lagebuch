namespace LageBuch.Domain.Wasserfoerderung;

/// <summary>
/// Pure engine that places Verstärkerpumpen along a Förderstrecke (#150, Plan A). Given a total
/// length, a total elevation rise (treated as a uniform gradient — Plan B replaces that with a
/// sampled profile) and a <see cref="FörderstreckeConfig"/>, it computes B-hose count, pump
/// positions and reserve figures. Physics is linear and therefore testable without deps:
///
///   head-loss per leg = friction + elevation     friction = loss/100m at flow
///   usable budget    = (feed − inlet) · (1 − headroom)
///   legs snap down to whole hoses; every pump restarts from feed pressure (closed Schaltreihe),
///   the feed pump at the water source is NOT counted as a Verstärkerpumpe (user decision).
/// </summary>
public static class FörderstreckePlanner
{
    // 10 m elevation rise costs 1 bar, so 0.1 bar per meter of rise.
    private const double ElevationBarPerMeter = 0.1;

    // Verified B-75 mm table: bar per 100 m at 200/400/600/800/1000/1200 l/min (midpoints).
    private static readonly double[] FlowPoints = { 200, 400, 600, 800, 1000, 1200 };
    private static readonly double[] LossBarPer100M = { 0.1, 0.3, 0.6, 1.0, 1.5, 2.25 };

    public static FörderstreckePlan Plan(double lengthM, double riseM, FörderstreckeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (lengthM <= 0)
        {
            throw new ArgumentException("Die Förderstrecke muss länger als 0 m sein.", nameof(lengthM));
        }

        var lossPerMeter = LossPer100Meters(config.FlowLMin) / 100;
        var headPerMeter = lossPerMeter + (ElevationBarPerMeter * riseM / lengthM);
        var hoseCount = (int)Math.Ceiling(lengthM / config.HoseLengthMeters);
        var reserveHoseCount = (int)Math.Ceiling(lengthM / 100);

        // Gravity assist (or a flat short line) means one leg can carry the whole route.
        var legLengthM = headPerMeter <= 0
            ? lengthM
            : BudgetPerLegBar(config) / headPerMeter;

        var legSnapped = Math.Min(
            Math.Floor(legLengthM / config.HoseLengthMeters) * config.HoseLengthMeters,
            lengthM);

        // A single hose cannot carry a leg: the climb physically does not fit.
        if (legSnapped < config.HoseLengthMeters)
        {
            throw new ArgumentException(
                "Die Steigung ist zu stark — ein B-Schlauch (20 m) trägt das Gefälle bereits über das Druckbudget.");
        }

        var positions = new List<double>();
        for (var pos = 0.0; pos < lengthM; pos += legSnapped)
        {
            positions.Add(pos);
        }

        var pumpCount = Math.Max(0, positions.Count - 1); // exclude the feed pump at 0
        var reservePumpCount = (int)Math.Ceiling((double)pumpCount / config.ReservePumpEveryNPumps);

        return new FörderstreckePlan(lengthM, hoseCount, reserveHoseCount, pumpCount, reservePumpCount, positions);
    }

    private static double BudgetPerLegBar(FörderstreckeConfig config) =>
        (config.FeedPressureBar - config.InletPressureBar) * (1 - config.HeadroomPercent);

    private static double LossPer100Meters(int flowLMin)
    {
        if (flowLMin < FlowPoints[0] || flowLMin > FlowPoints[^1])
        {
            throw new ArgumentException(
                $"Durchfluss {flowLMin} l/min liegt außerhalb der Tabelle (200–1200 l/min).", nameof(flowLMin));
        }

        var flow = (double)flowLMin;
        for (var i = 0; i < FlowPoints.Length - 1; i++)
        {
            if (flow <= FlowPoints[i + 1])
            {
                var t = (flow - FlowPoints[i]) / (FlowPoints[i + 1] - FlowPoints[i]);
                return LossBarPer100M[i] + (t * (LossBarPer100M[i + 1] - LossBarPer100M[i]));
            }
        }

        return LossBarPer100M[^1];
    }
}