namespace LageBuch.Domain.Etb;

/// <summary>One prior version of an ETB entry's text, recorded when it was replaced by an edit.</summary>
public sealed record EtbEntryEdit(string PreviousText, string EditedBy, DateTimeOffset EditedAt);
