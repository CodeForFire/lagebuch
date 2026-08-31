namespace LageBuch.Domain.Wasserfoerderung;

/// <summary>
/// One planned Förderstrecke-Leitung (Ltg 1, Ltg 2, …) on the incident (#150, Plan A). Immutable
/// like every other aggregate child; the plan figures are computed once at creation by
/// <see cref="FörderstreckePlanner"/> and stored, so the PDF and remote clients always render the
/// exact numbers that were planned — never a recomputation that could drift.
/// </summary>
public sealed record WasserfoerderungLeitung
{
    private WasserfoerderungLeitung()
    {
    }

    public Guid Id { get; private init; }

    public int Number { get; private init; }

    /// <summary>Excel "Übergabestelle [Fzg., Behälter, …]" — the vehicle/container at the line end.</summary>
    public string? Uebergabestelle { get; private init; }

    /// <summary>Excel "Ansprechpartner Funkrufname".</summary>
    public string? Ansprechpartner { get; private init; }

    public int FlowLMin { get; private init; }

    public double FeedPressureBar { get; private init; }

    public double LengthMeters { get; private init; }

    public double ElevationRiseMeters { get; private init; }

    public int HoseCount { get; private init; }

    public int ReserveHoseCount { get; private init; }

    public int PumpCount { get; private init; }

    public int ReservePumpCount { get; private init; }

    /// <summary>Meters from the water source where a pump sits; index 0 is the feed pump.</summary>
    public IReadOnlyList<double> PumpPositionsMeters { get; private init; } = Array.Empty<double>();

    /// <summary>The drawn polyline (#150, Plan B); null when the Leitung came from manual entry (Plan A).</summary>
    public IReadOnlyList<GeoPoint>? RoutePoints { get; private init; }

    public static WasserfoerderungLeitung Create(
        int number,
        string? uebergabestelle,
        string? ansprechpartner,
        double lengthM,
        double riseM,
        FörderstreckeConfig? config = null)
    {
        if (number < 1)
        {
            throw new ArgumentException("Die Leitungsnummer muss >= 1 sein.", nameof(number));
        }

        config ??= FörderstreckeConfig.Default;
        var plan = FörderstreckePlanner.Plan(lengthM, riseM, config);

        return new WasserfoerderungLeitung
        {
            Id = Guid.NewGuid(),
            Number = number,
            Uebergabestelle = string.IsNullOrWhiteSpace(uebergabestelle) ? null : uebergabestelle.Trim(),
            Ansprechpartner = string.IsNullOrWhiteSpace(ansprechpartner) ? null : ansprechpartner.Trim(),
            FlowLMin = config.FlowLMin,
            FeedPressureBar = config.FeedPressureBar,
            LengthMeters = plan.LengthMeters,
            ElevationRiseMeters = riseM,
            HoseCount = plan.HoseCount,
            ReserveHoseCount = plan.ReserveHoseCount,
            PumpCount = plan.PumpCount,
            ReservePumpCount = plan.ReservePumpCount,
            PumpPositionsMeters = plan.PumpPositionsMeters,
        };
    }

    public static WasserfoerderungLeitung Rehydrate(
        Guid id,
        int number,
        string? uebergabestelle,
        string? ansprechpartner,
        int flowLMin,
        double feedPressureBar,
        double lengthM,
        double elevationRiseM,
        int hoseCount,
        int reserveHoseCount,
        int pumpCount,
        int reservePumpCount,
        IReadOnlyList<double> pumpPositionsMeters,
        IReadOnlyList<GeoPoint>? routePoints = null)
        => new()
        {
            Id = id,
            Number = number,
            Uebergabestelle = uebergabestelle,
            Ansprechpartner = ansprechpartner,
            FlowLMin = flowLMin,
            FeedPressureBar = feedPressureBar,
            LengthMeters = lengthM,
            ElevationRiseMeters = elevationRiseM,
            HoseCount = hoseCount,
            ReserveHoseCount = reserveHoseCount,
            PumpCount = pumpCount,
            ReservePumpCount = reservePumpCount,
            PumpPositionsMeters = pumpPositionsMeters,
            RoutePoints = routePoints,
        };

    /// <summary>
    /// Plan B (#150 phase 2): plans from an already-sampled terrain profile along a drawn route
    /// instead of a single manually entered length/rise. The profile is sampled once (by the
    /// caller, before this runs) so every replica stores the same numbers regardless of local DEM
    /// differences — see <see cref="Incident.AddWasserfoerderungLeitungFromRoute"/>.
    /// </summary>
    public static WasserfoerderungLeitung CreateFromRoute(
        int number,
        string? uebergabestelle,
        string? ansprechpartner,
        IReadOnlyList<GeoPoint> routePoints,
        IReadOnlyList<ElevationProfileSample> profile,
        FörderstreckeConfig? config = null)
    {
        if (number < 1)
        {
            throw new ArgumentException("Die Leitungsnummer muss >= 1 sein.", nameof(number));
        }

        config ??= FörderstreckeConfig.Default;
        var plan = FörderstreckePlanner.PlanFromProfile(profile, config);

        return new WasserfoerderungLeitung
        {
            Id = Guid.NewGuid(),
            Number = number,
            Uebergabestelle = string.IsNullOrWhiteSpace(uebergabestelle) ? null : uebergabestelle.Trim(),
            Ansprechpartner = string.IsNullOrWhiteSpace(ansprechpartner) ? null : ansprechpartner.Trim(),
            FlowLMin = config.FlowLMin,
            FeedPressureBar = config.FeedPressureBar,
            LengthMeters = plan.LengthMeters,
            ElevationRiseMeters = profile[^1].ElevationMeters - profile[0].ElevationMeters,
            HoseCount = plan.HoseCount,
            ReserveHoseCount = plan.ReserveHoseCount,
            PumpCount = plan.PumpCount,
            ReservePumpCount = plan.ReservePumpCount,
            PumpPositionsMeters = plan.PumpPositionsMeters,
            RoutePoints = routePoints,
        };
    }
}