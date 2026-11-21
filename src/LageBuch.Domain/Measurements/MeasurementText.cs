namespace LageBuch.Domain.Measurements;

/// <summary>The one text rule every Messprotokoll field follows (#930): trimmed, cut to its cap,
/// and null when nothing is left. The same text arrives from a form, a file and a sync peer, so
/// it is cut rather than rejected.</summary>
internal static class MeasurementText
{
    public static string? Clean(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Stryker disable once Equality : cutting a string of exactly max characters to max yields the same string.
        return trimmed.Length > max ? trimmed[..max].TrimEnd() : trimmed;
    }

    /// <summary>A required field: cleaned, or an <see cref="ArgumentException"/> naming the field
    /// in German when nothing is left.</summary>
    public static string Required(string? value, int max, string label, string paramName) =>
        Clean(value, max) ?? throw new ArgumentException($"{label} darf nicht leer sein.", paramName);
}
