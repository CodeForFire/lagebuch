namespace LageBuch.Domain.Measurements;

/// <summary>
/// One Messung of the Messprotokoll (#930): what a Messtrupp read at one Messpunkt at one time,
/// with its Messauftrag ("MA 04"), who measured in what Schutzausrüstung and how, what they
/// smelled or saw, and the values per <see cref="MeasurementChannel"/>. A value is never
/// overwritten silently: a correction keeps the previous value in <see cref="Corrections"/>.
/// </summary>
public sealed record Measurement(
    Guid Id,
    DateTimeOffset MeasuredAt,
    string? Order,
    Guid PointId,
    string? Team,
    ProtectiveEquipment Protection,
    MeasurementMode Mode,
    bool Odour,
    bool Deposits,
    string? Note,
    IReadOnlyDictionary<Guid, MeasuredValue> Values,
    MeasurementStatus Status,
    MeasurementAssessment? Assessment,
    IReadOnlyList<MeasurementCorrection> Corrections,
    DateTimeOffset RecordedAt,
    string RecordedBy)
{
    public const int MaxCount = 2000;

    /// <summary>A Messauftrag is a short reference, "MA 04".</summary>
    public const int MaxOrderLength = 20;

    /// <summary>Funkrufname and Trupp, "Florian Musterstadt 40/1 · Angriffstrupp".</summary>
    public const int MaxTeamLength = 60;

    public const int MaxNoteLength = 200;

    public const int MaxRecordedByLength = 60;

    /// <summary>How many corrections one Messung keeps. Far more than a real one needs; it bounds
    /// what a sync peer can make the file hold.</summary>
    public const int MaxCorrectionCount = 50;

    /// <summary>Whether the Messung says anything at all: a value, a smell, a deposit or a note.</summary>
    public bool HasContent => Values.Count > 0 || Odour || Deposits || Note is not null;

    /// <summary>
    /// The same Messung with every rule that cannot be trusted from outside applied: text trimmed
    /// and cut, unknown enum values read as their defaults, values and corrections of a Messgröße
    /// not in <paramref name="channelIds"/> dropped, corrections capped, and an assessment without
    /// a name dropped. Status follows the assessment that survives, Unconfirmed without one.
    /// </summary>
    public Measurement Sanitized(IReadOnlySet<Guid> channelIds)
    {
        ArgumentNullException.ThrowIfNull(channelIds);
        var assessment = Assessment?.Sanitized();
        return this with
        {
            Order = MeasurementText.Clean(Order, MaxOrderLength),
            Team = MeasurementText.Clean(Team, MaxTeamLength),
            Protection = Enum.IsDefined(Protection) ? Protection : ProtectiveEquipment.None,
            Mode = Enum.IsDefined(Mode) ? Mode : MeasurementMode.Remote,
            Note = MeasurementText.Clean(Note, MaxNoteLength),
            Values = Values
                .Where(v => channelIds.Contains(v.Key))
                .ToDictionary(v => v.Key, v => v.Value),
            Status = assessment?.Status ?? MeasurementStatus.Unconfirmed,
            Assessment = assessment,
            Corrections = Corrections
                .Where(c => channelIds.Contains(c.ChannelId))
                .Select(c => c.Sanitized())
                .OfType<MeasurementCorrection>()
                .Take(MaxCorrectionCount)
                .ToList(),
            RecordedBy = MeasurementText.Clean(RecordedBy, MaxRecordedByLength) ?? string.Empty,
        };
    }
}
