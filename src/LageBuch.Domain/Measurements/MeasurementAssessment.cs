namespace LageBuch.Domain.Measurements;

/// <summary>Who judged a Messung, how and when (#930): bestätigt or Ausreißer, with a reason. A
/// new assessment replaces the old one; the ETB keeps every one of them.</summary>
public sealed record MeasurementAssessment(MeasurementStatus Status, string By, string? Text, DateTimeOffset At)
{
    public const int MaxByLength = 60;

    public const int MaxTextLength = 300;

    /// <summary>The same assessment with text trimmed and cut and an unknown status read as
    /// <see cref="MeasurementStatus.Unconfirmed"/>; null when nobody is named.</summary>
    public MeasurementAssessment? Sanitized() =>
        MeasurementText.Clean(By, MaxByLength) is { } by
            ? new MeasurementAssessment(
                Enum.IsDefined(Status) ? Status : MeasurementStatus.Unconfirmed,
                by,
                MeasurementText.Clean(Text, MaxTextLength),
                At)
            : null;
}
