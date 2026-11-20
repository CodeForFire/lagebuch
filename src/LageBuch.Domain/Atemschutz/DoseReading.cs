namespace LageBuch.Domain.Atemschutz;

/// <summary>
/// The unit an Alarmdosimeter reading is written down in (#994). Both occur: a direct-reading
/// dosimeter shows µSv for everyday doses and mSv once they climb. The ordinals are a storage
/// contract, so they may be appended to but never renumbered.
/// </summary>
public enum DoseUnit
{
    MilliSievert = 0,
    MicroSievert = 1,
}

/// <summary>
/// The Ablesewert of an Alarmdosimeter, kept exactly as it was read off the display: value and unit
/// together, never converted (#994). Converting would put a number in the record that nobody saw.
/// </summary>
public sealed record DoseReading
{
    /// <summary>
    /// The largest value accepted, in either unit. Far beyond anything an Einsatz reads; it only
    /// stops a typo or a hostile file from carrying a number no display shows.
    /// </summary>
    public const decimal MaxValue = 100_000m;

    public DoseReading(decimal value, DoseUnit unit)
    {
        if (value < 0 || value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Der Ablesewert muss zwischen 0 und 100.000 liegen.");
        }

        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), "Unbekannte Einheit.");
        }

        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }

    public DoseUnit Unit { get; }

    /// <summary>"mSv" or "µSv".</summary>
    public static string UnitLabel(DoseUnit unit) => unit == DoseUnit.MicroSievert ? "µSv" : "mSv";

    /// <summary>The reading as the display showed it: "0,12 mSv", "35 µSv". Up to three decimals,
    /// since a small mSv reading is a fraction of one.</summary>
    public string Display => $"{Value.ToString("0.###", Formatting.NumberFormat)} {UnitLabel(Unit)}";
}
