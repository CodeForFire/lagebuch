namespace LageBuch.Domain.Measurements;

/// <summary>One corrected value of a Messung (#930): which Messgröße, what it said before, what it
/// says now, why and who. The Messung keeps every correction, so the original reading is never
/// lost. A null <see cref="Previous"/> is a value added afterwards, a null <see cref="Value"/> one
/// taken out.</summary>
public sealed record MeasurementCorrection(
    Guid ChannelId,
    MeasuredValue? Previous,
    MeasuredValue? Value,
    string Reason,
    string By,
    DateTimeOffset At)
{
    public const int MaxReasonLength = 120;

    public const int MaxByLength = 60;

    /// <summary>The same correction with text trimmed and cut; null without a reason, without a
    /// name or when it changes nothing.</summary>
    public MeasurementCorrection? Sanitized() =>
        MeasurementText.Clean(Reason, MaxReasonLength) is { } reason
        && MeasurementText.Clean(By, MaxByLength) is { } by
        && Previous != Value
            ? new MeasurementCorrection(ChannelId, Previous, Value, reason, by, At)
            : null;
}
