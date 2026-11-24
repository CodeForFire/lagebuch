namespace LageBuch.Domain.Etb;

/// <summary>
/// One prior version of an ETB entry, recorded when an edit replaced it: its text, and the Von and
/// An it carried then (null for none). Edits recorded before Von and An became editable carry the
/// entry's Von and An, which could not have changed until then.
/// </summary>
public sealed record EtbEntryEdit(
    string PreviousText,
    string EditedBy,
    DateTimeOffset EditedAt,
    string? PreviousFrom = null,
    string? PreviousTo = null);
