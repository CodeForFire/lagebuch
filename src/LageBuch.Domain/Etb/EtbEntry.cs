namespace LageBuch.Domain.Etb;

public sealed record EtbEntry
{
    // No domain reason for an ETB line to run longer than this; caps how much a single entry (and,
    // via WithEditedText, each retained edit) can grow the journal's storage and wire footprint.
    public const int MaxTextLength = 4000;

    private EtbEntry()
    {
    }

    public Guid Id { get; private init; }

    public DateTimeOffset Timestamp { get; private init; }

    public EtbDirection Direction { get; private init; }

    public string? From { get; private init; }

    public string? To { get; private init; }

    public string Text { get; private init; } = string.Empty;

    public string EnteredBy { get; private init; } = string.Empty;

    public IReadOnlyList<EtbEntryEdit> Edits { get; private init; } = Array.Empty<EtbEntryEdit>();

    public static EtbEntry Create(
        DateTimeOffset timestamp,
        EtbDirection direction,
        string text,
        SessionOperator @operator,
        string? from = null,
        string? to = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("ETB-Eintrag darf nicht leer sein.", nameof(text));
        }

        if (text.Length > MaxTextLength)
        {
            throw new ArgumentException($"ETB-Eintrag ist länger als das Limit von {MaxTextLength} Zeichen.", nameof(text));
        }

        ArgumentNullException.ThrowIfNull(@operator);

        return new EtbEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp,
            Direction = direction,
            Text = text.Trim(),
            From = string.IsNullOrWhiteSpace(from) ? null : from.Trim(),
            To = string.IsNullOrWhiteSpace(to) ? null : to.Trim(),
            EnteredBy = @operator.Display,
        };
    }

    public static EtbEntry Rehydrate(
        Guid id,
        DateTimeOffset timestamp,
        EtbDirection direction,
        string text,
        string enteredBy,
        string? from,
        string? to,
        IEnumerable<EtbEntryEdit>? edits = null)
        => new()
        {
            Id = id,
            Timestamp = timestamp,
            Direction = direction,
            Text = text,
            EnteredBy = enteredBy,
            From = from,
            To = to,
            Edits = (edits ?? Enumerable.Empty<EtbEntryEdit>()).ToList(),
        };

    /// <summary>
    /// Corrects this entry's text, recording what it was before under <see cref="Edits"/> so every
    /// prior version stays reachable — unlike <see cref="Files.IncidentFile.WithDisplayName"/>'s
    /// silent overwrite, an ETB entry's wording is part of the incident record. The System-direction
    /// guard (machine-generated entries are never editable) lives on <see cref="Incident.EditJournalEntry"/>,
    /// not here — this method only knows how to produce the edited copy.
    ///
    /// Resubmitting the same text unchanged is not an edit: it returns this instance as-is rather
    /// than growing <see cref="Edits"/>, so a retry (or a hostile flood of no-op "corrections")
    /// cannot inflate the retained history without ever actually changing the wording.
    /// </summary>
    public EtbEntry WithEditedText(string newText, SessionOperator editor, DateTimeOffset editedAt)
    {
        if (string.IsNullOrWhiteSpace(newText))
        {
            throw new ArgumentException("ETB-Eintrag darf nicht leer sein.", nameof(newText));
        }

        if (newText.Length > MaxTextLength)
        {
            throw new ArgumentException($"ETB-Eintrag ist länger als das Limit von {MaxTextLength} Zeichen.", nameof(newText));
        }

        ArgumentNullException.ThrowIfNull(editor);

        var trimmed = newText.Trim();
        if (trimmed == Text)
        {
            return this;
        }

        var edits = new List<EtbEntryEdit>(Edits) { new(Text, editor.Display, editedAt) };
        return this with { Text = trimmed, Edits = edits };
    }
}
