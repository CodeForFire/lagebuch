namespace LageBuch.Domain.CoMeasurement;

/// <summary>Judges a reading against its own gas's bands (#410). Replaces the three CO constants
/// that used to live here: every brigade now sets its own bands in the Stammdaten, and
/// <see cref="GasDefinition.DefaultCo"/> carries the old ones for everybody who does not.</summary>
public static class GasSeverityClassifier
{
    /// <summary>The band a reading falls in, read in the gas's own direction: a rising gas is
    /// Erhöht from its threshold upwards, a falling gas from its threshold downwards. A gas with no
    /// thresholds is always <see cref="CoSeverity.Normal"/>.</summary>
    public static CoSeverity SeverityOf(GasDefinition gas, decimal? value)
    {
        ArgumentNullException.ThrowIfNull(gas);
        if (value is not { } v)
        {
            return CoSeverity.Normal;
        }

        if (Reached(gas.Direction, v, gas.Lethal))
        {
            return CoSeverity.Lethal;
        }

        if (Reached(gas.Direction, v, gas.Dangerous))
        {
            return CoSeverity.Dangerous;
        }

        return Reached(gas.Direction, v, gas.Elevated) ? CoSeverity.Elevated : CoSeverity.Normal;
    }

    /// <summary>Whether a reading is beyond the gas's Plausibel-bis limit -- more likely a typo
    /// (9999 vs 999) than a measurement, so the UI asks for a second look without rejecting it.
    /// The limit is an upper bound in either direction: 210 Vol-% O₂ is the typo there.</summary>
    public static bool IsImplausible(GasDefinition gas, decimal? value)
    {
        ArgumentNullException.ThrowIfNull(gas);
        return value is { } v && gas.PlausibleUpTo is { } limit && v > limit;
    }

    /// <summary>
    /// The reading that decides a Wohnung's colour: the highest severity across its gases, a tie
    /// going to the gas earlier in the list, which also makes it the first measured gas while every
    /// gas is normal. Null when nothing was measured.
    /// </summary>
    public static GasValue? Worst(IReadOnlyList<IncidentGas> gases, IReadOnlyDictionary<Guid, decimal> values)
    {
        ArgumentNullException.ThrowIfNull(gases);
        ArgumentNullException.ThrowIfNull(values);

        GasValue? worst = null;
        foreach (var gas in gases)
        {
            if (!values.TryGetValue(gas.Id, out var value))
            {
                continue;
            }

            var severity = SeverityOf(gas.Definition, value);
            if (worst is null || severity > worst.Severity)
            {
                worst = new GasValue(gas, value, severity);
            }
        }

        return worst;
    }

    private static bool Reached(GasDirection direction, decimal value, decimal? threshold) =>
        threshold is { } t && (direction == GasDirection.Falling ? value <= t : value >= t);
}
