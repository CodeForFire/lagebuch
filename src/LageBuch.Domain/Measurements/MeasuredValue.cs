using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Measurements;

/// <summary>
/// What a meter showed (#930): one number, or a range when the display wandered while the Trupp
/// read it ("Ex 8–14 %UEG"). Both ends are normalised like a Gasmessprotokoll reading
/// (<see cref="GasDefinition.NormalizeValue"/>: two decimals, 0 to
/// <see cref="GasDefinition.MaxValue"/>), and <see cref="Low"/> is never above <see cref="High"/>:
/// a range typed the wrong way round is turned around, not rejected.
/// </summary>
public sealed record MeasuredValue
{
    /// <summary>Builds a value from its two ends. Throws <see cref="ArgumentOutOfRangeException"/>
    /// when either end is negative or above <see cref="GasDefinition.MaxValue"/>; a file or a sync
    /// peer goes through <see cref="TryCreate"/> instead.</summary>
    public MeasuredValue(decimal low, decimal high)
    {
        var l = GasDefinition.NormalizeValue(low)
            ?? throw new ArgumentOutOfRangeException(nameof(low), $"Messwert liegt außerhalb von 0 bis {Formatting.Measurement(GasDefinition.MaxValue, string.Empty)}.");
        var h = GasDefinition.NormalizeValue(high)
            ?? throw new ArgumentOutOfRangeException(nameof(high), $"Messwert liegt außerhalb von 0 bis {Formatting.Measurement(GasDefinition.MaxValue, string.Empty)}.");
        Low = Math.Min(l, h);
        High = Math.Max(l, h);
    }

    public decimal Low { get; }

    public decimal High { get; }

    /// <summary>Whether the meter showed a range rather than one number.</summary>
    public bool IsRange => Low != High;

    /// <summary>One number, as most readings are.</summary>
    [SuppressMessage("Naming", "CA1720", Justification = "Single is the reading's shape, one number as opposed to a range; it never stands for System.Single, and MeasuredValue holds decimals only.")]
    public static MeasuredValue Single(decimal value) => new(value, value);

    /// <summary>The non-throwing form for input that may be hostile: false when either end cannot
    /// be a reading.</summary>
    public static bool TryCreate(decimal low, decimal high, [NotNullWhen(true)] out MeasuredValue? value)
    {
        value = GasDefinition.NormalizeValue(low) is not null && GasDefinition.NormalizeValue(high) is not null
            ? new MeasuredValue(low, high)
            : null;
        return value is not null;
    }

    public void Deconstruct(out decimal low, out decimal high)
    {
        low = Low;
        high = High;
    }
}
