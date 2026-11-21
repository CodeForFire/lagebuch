using System.Globalization;
using LageBuch.Domain;
using LageBuch.Domain.Measurements;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Reads what a Lagebuchführer types into the Messprotokoll (#930): a reading as "12,5" or a
/// fluctuating one as the range "8–14", a time of day as "14:05", a wind speed or a temperature.
/// German first, because that is the keyboard in the ELW; a point is read as the decimal
/// separator too, since a phone's number pad often offers nothing else.
/// </summary>
public static class MeasurementInput
{
    private static readonly char[] RangeSeparators = ['–', '—', '-'];

    private static readonly string[] TimeFormats = ["H:mm", "HH:mm", "H.mm", "HH.mm", "HHmm"];

    /// <summary>
    /// Reads one reading. Blank is not an error: it means the Messgröße was not measured, and
    /// <paramref name="value"/> is null. Returns false for anything that is not a number or a
    /// range of two, or that lies outside what a reading can be.
    /// </summary>
    public static bool TryParseValue(string? text, out MeasuredValue? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim();

        // From the second character on, so a separator can never be read as a sign.
        var split = trimmed.IndexOfAny(RangeSeparators, 1);
        if (split < 0)
        {
            if (TryParseNumber(trimmed, out var single) && MeasuredValue.TryCreate(single, single, out var one))
            {
                value = one;
                return true;
            }

            return false;
        }

        if (TryParseNumber(trimmed[..split], out var low)
            && TryParseNumber(trimmed[(split + 1)..], out var high)
            && MeasuredValue.TryCreate(low, high, out var range))
        {
            value = range;
            return true;
        }

        return false;
    }

    /// <summary>Reads an optional number that may be negative (a temperature); blank gives null
    /// and true.</summary>
    public static bool TryParseOptionalNumber(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim().Replace('−', '-');
        var negative = trimmed.StartsWith('-');
        if (!TryParseNumber(negative ? trimmed[1..] : trimmed, out var number))
        {
            return false;
        }

        value = negative ? -number : number;
        return true;
    }

    /// <summary>
    /// Reads a time of day ("14:05", "1405", "14.05") as the moment it names on
    /// <paramref name="now"/>'s day and offset. A time more than twelve hours ahead of
    /// <paramref name="now"/> is taken to be yesterday's: at 00:10 a Messung entered as 23:55 was
    /// made a quarter of an hour ago, not tonight.
    /// </summary>
    public static bool TryParseTime(string? text, DateTimeOffset now, out DateTimeOffset at)
    {
        at = default;
        if (string.IsNullOrWhiteSpace(text)
            || !TimeOnly.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        var candidate = new DateTimeOffset(DateOnly.FromDateTime(now.DateTime), time, now.Offset);
        at = candidate > now.AddHours(12) ? candidate.AddDays(-1) : candidate;
        return true;
    }

    /// <summary>The time box's text for <paramref name="at"/>: "14:05".</summary>
    public static string TimeText(DateTimeOffset at) => Formatting.TimeOfDay(at);

    private static bool TryParseNumber(string text, out decimal number) =>
        decimal.TryParse(
            text.Trim().Replace('.', ','),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            Formatting.NumberFormat,
            out number);
}
