namespace Feuerwehr.Domain.Etb;

public sealed record EtbEntry
{
    private EtbEntry() { }

    public Guid Id { get; private init; }
    public DateTimeOffset Timestamp { get; private init; }
    public EtbDirection Direction { get; private init; }
    public string? From { get; private init; }
    public string? To { get; private init; }
    public string Text { get; private init; } = string.Empty;
    public string EnteredBy { get; private init; } = string.Empty;

    public static EtbEntry Create(
        DateTimeOffset timestamp,
        EtbDirection direction,
        string text,
        SessionOperator @operator,
        string? from = null,
        string? to = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("ETB-Eintrag darf nicht leer sein.", nameof(text));
        ArgumentNullException.ThrowIfNull(@operator);

        return new EtbEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp,
            Direction = direction,
            Text = text.Trim(),
            From = string.IsNullOrWhiteSpace(from) ? null : from.Trim(),
            To = string.IsNullOrWhiteSpace(to) ? null : to.Trim(),
            EnteredBy = @operator.Display
        };
    }

    public static EtbEntry Rehydrate(
        Guid id,
        DateTimeOffset timestamp,
        EtbDirection direction,
        string text,
        string enteredBy,
        string? from,
        string? to)
        => new()
        {
            Id = id,
            Timestamp = timestamp,
            Direction = direction,
            Text = text,
            EnteredBy = enteredBy,
            From = from,
            To = to
        };
}
