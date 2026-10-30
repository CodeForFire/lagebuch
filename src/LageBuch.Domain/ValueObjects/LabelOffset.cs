namespace LageBuch.Domain.ValueObjects;

/// <summary>
/// Where a taktisches Zeichen stands relative to its pin on the Lagekarte, in screen pixels, so it
/// reads the same at every zoom. DV 102 §1.5: a sign moved aside for clarity is tied back to its
/// Örtlichkeit with a Verbindungsstrich; this is how far it was moved.
/// </summary>
/// <remarks>
/// Refuses NaN, infinity and anything beyond <see cref="MaxPixels"/>, so an offset read from a
/// hostile file or a sync peer can never throw the layout off the screen.
/// </remarks>
public sealed record LabelOffset
{
    /// <summary>How far a sign may stand from its pin, in either direction.</summary>
    public const double MaxPixels = 2000;

    public LabelOffset(double dx, double dy)
    {
        if (!IsValid(dx))
        {
            throw new ArgumentOutOfRangeException(nameof(dx), dx, "The offset must be finite and within the cap.");
        }

        if (!IsValid(dy))
        {
            throw new ArgumentOutOfRangeException(nameof(dy), dy, "The offset must be finite and within the cap.");
        }

        Dx = dx;
        Dy = dy;
    }

    public double Dx { get; }

    public double Dy { get; }

    /// <summary>The offset, or null when either half is missing or out of range: a bad value costs the offset, not the Einsatz.</summary>
    public static LabelOffset? TryCreate(double? dx, double? dy) =>
        dx is { } x && dy is { } y && IsValid(x) && IsValid(y) ? new LabelOffset(x, y) : null;

    private static bool IsValid(double value) => double.IsFinite(value) && Math.Abs(value) <= MaxPixels;
}
