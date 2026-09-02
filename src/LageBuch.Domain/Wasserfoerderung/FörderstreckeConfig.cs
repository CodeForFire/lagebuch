namespace LageBuch.Domain.Wasserfoerderung;

/// <summary>
/// Tunables for <see cref="FörderstreckePlanner"/>. Defaults mirror the verified B-line figures:
/// TS 8/8 (8 bar @ 800 l/min), 1.5 bar inlet at the next pump (closed Schaltreihe), 20 m B-hose
/// (DIN 14811), 3% headroom per leg, one reserve pump per four Verstärkerpumpen.
/// </summary>
public sealed record FörderstreckeConfig
{
    /// <summary>Nominal B-flow in l/min; drives the friction-loss lookup table.</summary>
    public int FlowLMin { get; init; } = 800;

    /// <summary>Discharge pressure of each pump (weakest pump governs the chain).</summary>
    public double FeedPressureBar { get; init; } = 8;

    /// <summary>Required suction pressure at the next pump before it can re-pressurize.</summary>
    public double InletPressureBar { get; init; } = 1.5;

    /// <summary>Fraction of the usable pressure budget kept in reserve on every leg.</summary>
    public double HeadroomPercent { get; init; } = 0.03;

    /// <summary>Length of one B-Schlauch in meters; legs snap down to whole hoses.</summary>
    public double HoseLengthMeters { get; init; } = 20;

    /// <summary>One reserve pump per this many Verstärkerpumpen (rule of thumb 1:3–5).</summary>
    public int ReservePumpEveryNPumps { get; init; } = 4;

    public static FörderstreckeConfig Default => new();
}

/// <summary>The result of a <see cref="FörderstreckePlanner.Plan"/> run.</summary>
public sealed record FörderstreckePlan(
    double LengthMeters,
    int HoseCount,
    int ReserveHoseCount,
    int PumpCount,
    int ReservePumpCount,
    IReadOnlyList<double> PumpPositionsMeters);