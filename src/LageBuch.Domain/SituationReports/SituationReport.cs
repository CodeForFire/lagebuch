namespace LageBuch.Domain.SituationReports;

/// <summary>
/// A Lagemeldung (#592): the periodic situation report the Lagebuchführer reads out to the ILS.
/// Always the same structure — Meldender, Einsatzstelle, Lage, Durchgeführte Maßnahmen,
/// Eingesetzte Einheiten, Nachforderungen — composed into one <see cref="Funkspruch"/>.
/// </summary>
/// <remarks>
/// A report starts as a draft and is numbered only when it is sent, so deleting a draft never
/// leaves a gap in Nr. 1, Nr. 2, …. Once sent it is the record of what was said over the radio and
/// no longer changes. Immutable like every other aggregate child: a correction to a draft produces
/// a replacement via <see cref="WithDetails"/>.
/// </remarks>
public sealed record SituationReport
{
    // Caps bound the storage and snapshot footprint of input that may come from a sync peer. Taken
    // together they keep the composed Funkspruch, with its "Lagemeldung Nr. N: " prefix, well below
    // EtbEntry.MaxTextLength, so sending can never be refused for length.
    public const int MaxReporterLength = 100;

    public const int MaxLocationLength = 300;

    public const int MaxFieldLength = 600;

    /// <summary>What separates the parts of the Funkspruch when it is read out.</summary>
    public const string FunkspruchSeparator = " · ";

    private SituationReport()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>The Nr. given when the report was sent; null while it is a draft.</summary>
    public int? Number { get; private init; }

    /// <summary>Meldender: who reports — usually the ELW's own Funkrufname.</summary>
    public string? Reporter { get; private init; }

    /// <summary>Einsatzstelle.</summary>
    public string? Location { get; private init; }

    /// <summary>Lage.</summary>
    public string? Situation { get; private init; }

    /// <summary>Durchgeführte Maßnahmen.</summary>
    public string? Measures { get; private init; }

    /// <summary>Eingesetzte Einheiten.</summary>
    public string? Units { get; private init; }

    /// <summary>Nachforderungen.</summary>
    public string? Requests { get; private init; }

    public string CreatedBy { get; private init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? SentAt { get; private init; }

    public string? SentBy { get; private init; }

    /// <summary>The ETB entry the send wrote, so the journal can mark it as this Lagemeldung even
    /// after its text has been corrected. Null while a draft.</summary>
    public Guid? EtbEntryId { get; private init; }

    public bool IsSent => SentAt is not null;

    /// <summary>
    /// The text read out over the radio: the filled-in parts in the order they are spoken, joined
    /// by <see cref="FunkspruchSeparator"/>. Meldender is not part of it — the ETB carries it as
    /// the sender. Empty while nothing has been filled in.
    /// </summary>
    public string Funkspruch => Compose(Location, Situation, Measures, Units, Requests);

    public static SituationReport Create(
        DateTimeOffset createdAt,
        string? reporter,
        string? location,
        string? situation,
        string? measures,
        string? units,
        string? requests,
        SessionOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        return new SituationReport
        {
            Id = Guid.NewGuid(),
            CreatedBy = @operator.Display,
            CreatedAt = createdAt,
        }.WithDetails(reporter, location, situation, measures, units, requests);
    }

    public static SituationReport Rehydrate(
        Guid id,
        int? number,
        string? reporter,
        string? location,
        string? situation,
        string? measures,
        string? units,
        string? requests,
        string createdBy,
        DateTimeOffset createdAt,
        DateTimeOffset? sentAt,
        string? sentBy,
        Guid? etbEntryId = null)
        => new()
        {
            Id = id,
            Number = number,
            Reporter = reporter,
            Location = location,
            Situation = situation,
            Measures = measures,
            Units = units,
            Requests = requests,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            SentAt = sentAt,
            SentBy = sentBy,
            EtbEntryId = etbEntryId,
        };

    /// <summary>
    /// The Funkspruch these parts make. The same composition the view model shows as a preview
    /// while the fields are still being typed.
    /// </summary>
    public static string Compose(string? location, string? situation, string? measures, string? units, string? requests)
    {
        var parts = new[] { location, situation, measures, units }
            .Select(Trimmed)
            .OfType<string>()
            .ToList();
        if (Trimmed(requests) is { } nachforderung)
        {
            parts.Add($"Nachforderung: {nachforderung}");
        }

        return string.Join(FunkspruchSeparator, parts);
    }

    /// <summary>Returns a copy with corrected details, validated and trimmed. Id, creation stamp
    /// and send state stay.</summary>
    public SituationReport WithDetails(
        string? reporter, string? location, string? situation, string? measures, string? units, string? requests) =>
        this with
        {
            Reporter = Checked(reporter, MaxReporterLength, "Meldender", nameof(reporter)),
            Location = Checked(location, MaxLocationLength, "Einsatzstelle", nameof(location)),
            Situation = Checked(situation, MaxFieldLength, "Lage", nameof(situation)),
            Measures = Checked(measures, MaxFieldLength, "Maßnahmen", nameof(measures)),
            Units = Checked(units, MaxFieldLength, "Einheiten", nameof(units)),
            Requests = Checked(requests, MaxFieldLength, "Nachforderungen", nameof(requests)),
        };

    /// <summary>Returns the sent copy, stamped with its Nr. and the ETB entry it was written as.</summary>
    public SituationReport AsSent(int number, DateTimeOffset sentAt, SessionOperator @operator, Guid etbEntryId)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        return this with { Number = number, SentAt = sentAt, SentBy = @operator.Display, EtbEntryId = etbEntryId };
    }

    private static string? Checked(string? value, int maxLength, string label, string paramName)
    {
        var trimmed = Trimmed(value);
        return trimmed is not null && trimmed.Length > maxLength
            ? throw new ArgumentException($"{label} ist länger als das Limit von {maxLength} Zeichen.", paramName)
            : trimmed;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
