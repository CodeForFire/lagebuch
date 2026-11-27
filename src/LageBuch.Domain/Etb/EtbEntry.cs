namespace LageBuch.Domain.Etb;

public sealed record EtbEntry
{
    // No domain reason for an ETB line to run longer than this; caps how much a single entry (and,
    // via WithEdit, each retained edit) can grow the journal's storage and wire footprint.
    public const int MaxTextLength = 4000;

    // Von and An name a Funkrufname or a person, never a paragraph. An edit is capped at this for
    // the same reason as the text: every edit retains the prior pair, and both arrive from sync
    // peers. Create does not check it, since the app's own lines carry Trupp call signs that are
    // not capped where they are entered, and those must never fail mid-Einsatz.
    public const int MaxPartyLength = 200;

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
            From = Party(from),
            To = Party(to),
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
    /// Corrects this entry's text, Von and An, recording the version before under <see cref="Edits"/>
    /// so every prior version stays reachable — unlike <see cref="Files.IncidentFile.WithDisplayName"/>'s
    /// silent overwrite, an ETB entry is part of the incident record. A null Von or An leaves it as
    /// it is and a blank one clears it, so a correction of the text alone cannot undo a change to
    /// Von or An that another device made in the meantime.
    /// The System-direction guard (machine-generated entries are never editable) lives on
    /// <see cref="Incident.EditJournalEntry"/>, not here — this method only knows how to produce the
    /// edited copy.
    ///
    /// Resubmitting the same text, Von and An unchanged is not an edit: it returns this instance
    /// as-is rather than growing <see cref="Edits"/>, so a retry (or a hostile flood of no-op
    /// "corrections") cannot inflate the retained history without ever actually changing the entry.
    /// </summary>
    public EtbEntry WithEdit(string newText, string? newFrom, string? newTo, SessionOperator editor, DateTimeOffset editedAt)
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
        var from = newFrom is null ? From : ChangedParty(newFrom, From, nameof(newFrom));
        var to = newTo is null ? To : ChangedParty(newTo, To, nameof(newTo));
        if (string.Equals(trimmed, Text, StringComparison.Ordinal)
            && string.Equals(from, From, StringComparison.Ordinal)
            && string.Equals(to, To, StringComparison.Ordinal))
        {
            return this;
        }

        var edits = new List<EtbEntryEdit>(Edits) { new(Text, editor.Display, editedAt, From, To) };
        return this with { Text = trimmed, From = from, To = to, Edits = edits };
    }

    /// <summary>
    /// Whether the edit at <paramref name="index"/> in <see cref="Edits"/> changed Von or An: its
    /// prior pair differs from the version that followed it, the next edit's prior pair or, for the
    /// last edit, the entry as it reads now.
    /// </summary>
    public bool EditChangedParties(int index)
    {
        var edit = Edits[index];
        var (from, to) = index + 1 < Edits.Count
            ? (Edits[index + 1].PreviousFrom, Edits[index + 1].PreviousTo)
            : (From, To);
        return !string.Equals(edit.PreviousFrom, from, StringComparison.Ordinal)
            || !string.Equals(edit.PreviousTo, to, StringComparison.Ordinal);
    }

    private static string? Party(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The cap applies to a new value only: an entry written before the cap, or by the app itself,
    // keeps a longer Von or An through a correction of its text.
    private static string? ChangedParty(string? value, string? current, string paramName)
    {
        var party = Party(value);
        if (party is not null && party.Length > MaxPartyLength && !string.Equals(party, current, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Von/An ist länger als das Limit von {MaxPartyLength} Zeichen.", paramName);
        }

        return party;
    }
}
