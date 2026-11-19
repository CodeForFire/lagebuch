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

    /// <summary>
    /// The Einsatzabschnitt the entry belongs to (#971), or null for none. The quick filter matches
    /// on this id, so a renamed Abschnitt does not split its entries in two.
    /// </summary>
    public Guid? SectionId { get; private init; }

    /// <summary>
    /// What <see cref="SectionId"/> was called when the entry was assigned to it, e.g.
    /// "Nord / Zugang". A value, not a lookup: the entry reads the same once the Abschnitt is
    /// renamed or dissolved, and in a file opened where the incident's nodes say otherwise.
    /// </summary>
    public string? SectionName { get; private init; }

    public static EtbEntry Create(
        DateTimeOffset timestamp,
        EtbDirection direction,
        string text,
        SessionOperator @operator,
        string? from = null,
        string? to = null,
        Guid? id = null)
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
            Id = id is { } given && given != Guid.Empty ? given : Guid.NewGuid(),
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
        IEnumerable<EtbEntryEdit>? edits = null,
        Guid? sectionId = null,
        string? sectionName = null)
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
            SectionId = sectionId,
            SectionName = sectionId is null ? null : sectionName,
        };

    /// <summary>
    /// This entry assigned to the Einsatzabschnitt <paramref name="sectionId"/>, called
    /// <paramref name="sectionName"/>, or to none with null. Which nodes may be chosen is
    /// <see cref="Incident.AssignJournalEntrySection"/>'s rule, not this method's.
    /// </summary>
    public EtbEntry WithSection(Guid? sectionId, string? sectionName) =>
        this with { SectionId = sectionId, SectionName = sectionId is null ? null : sectionName };

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
