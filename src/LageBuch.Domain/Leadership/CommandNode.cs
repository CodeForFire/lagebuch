namespace LageBuch.Domain.Leadership;

/// <summary>
/// A place the forces are led from or staged at: an Abschnitt, an Unterabschnitt, a Bereitstellung
/// or a partner's Einsatzleitung. Immutable like every other aggregate child: a correction produces
/// a replacement via <see cref="WithDetails"/>, and a dissolved node stays as history.
/// </summary>
public sealed record CommandNode
{
    // Caps bound the storage and snapshot footprint of input that may come from a sync peer.
    public const int MaxNameLength = 200;

    public const int MaxTextLength = 200;

    public const int MaxPhoneLength = 50;

    public const int MaxAuftragLength = 1000;

    private CommandNode()
    {
    }

    public Guid Id { get; private init; }

    public CommandNodeKind Kind { get; private init; }

    public string Name { get; private init; } = string.Empty;

    /// <summary>The Abschnitt an Unterabschnitt lies in; null for every other kind.</summary>
    public Guid? ParentId { get; private init; }

    public string? Auftrag { get; private init; }

    public string? Standort { get; private init; }

    /// <summary>The single Funkgruppe of an Abschnitt, Unterabschnitt or Bereitstellung; null for a partner.</summary>
    public string? Funkgruppe { get; private init; }

    /// <summary>A partner Einsatzleitung's TMO and DMO Funkgruppen (#828); empty for every other kind.</summary>
    public Funkgruppen Funkgruppen { get; private init; } = Funkgruppen.Empty;

    public string? Rufname { get; private init; }

    public string? Telefon { get; private init; }

    public DateTimeOffset? DissolvedAt { get; private init; }

    public bool IsLive => DissolvedAt is null;

    public static CommandNode Create(CommandNodeKind kind, string name, Guid? parentId)
    {
        var validName = CleanName(name);
        if (kind == CommandNodeKind.Unterabschnitt)
        {
            if (parentId is null)
            {
                throw new ArgumentException("Ein Unterabschnitt braucht einen Abschnitt.", nameof(parentId));
            }
        }
        else if (parentId is not null)
        {
            throw new ArgumentException("Nur ein Unterabschnitt liegt in einem Abschnitt.", nameof(parentId));
        }

        return new CommandNode { Id = Guid.NewGuid(), Kind = kind, Name = validName, ParentId = parentId };
    }

    public static CommandNode Rehydrate(
        Guid id,
        CommandNodeKind kind,
        string name,
        Guid? parentId,
        string? auftrag,
        string? standort,
        string? funkgruppe,
        string? rufname,
        string? telefon,
        DateTimeOffset? dissolvedAt,
        Funkgruppen? funkgruppen = null)
        => new()
        {
            Id = id,
            Kind = kind,
            Name = name,
            ParentId = parentId,
            Auftrag = auftrag,
            Standort = standort,
            Funkgruppe = funkgruppe,
            Rufname = rufname,
            Telefon = telefon,
            DissolvedAt = dissolvedAt,
            Funkgruppen = funkgruppen ?? Funkgruppen.Empty,
        };

    /// <summary>
    /// Returns a copy with corrected details, trimmed and capped the way <see cref="Create"/> does. A
    /// partner Einsatzleitung takes <paramref name="funkgruppen"/> and no single Funkgruppe, every
    /// other kind the reverse; null <paramref name="funkgruppen"/> means none.
    /// </summary>
    public CommandNode WithDetails(
        string name,
        string? auftrag,
        string? standort,
        string? funkgruppe,
        string? rufname,
        string? telefon,
        Funkgruppen? funkgruppen = null)
    {
        var single = Clean(funkgruppe, MaxTextLength, "Funkgruppe");
        var groups = (funkgruppen ?? Funkgruppen.Empty).Normalized();
        if (Kind == CommandNodeKind.Partner && single is not null)
        {
            throw new ArgumentException("Eine Einsatzleitung hat Funkgruppen TMO und DMO.", nameof(funkgruppe));
        }

        if (Kind != CommandNodeKind.Partner && !groups.IsEmpty)
        {
            throw new ArgumentException("Nur eine Einsatzleitung hat Funkgruppen TMO und DMO.", nameof(funkgruppen));
        }

        return this with
        {
            Name = CleanName(name),
            Auftrag = Clean(auftrag, MaxAuftragLength, "Auftrag"),
            Standort = Clean(standort, MaxTextLength, "Standort"),
            Funkgruppe = single,
            Funkgruppen = groups,
            Rufname = Clean(rufname, MaxTextLength, "Rufname"),
            Telefon = Clean(telefon, MaxPhoneLength, "Telefon"),
        };
    }

    /// <summary>
    /// Returns a copy at its new level: under <paramref name="parentId"/> an Unterabschnitt, at the
    /// top an Abschnitt. A Bereitstellung only ever moves among the top level and stays one.
    /// </summary>
    public CommandNode MovedTo(Guid? parentId) => Kind switch
    {
        CommandNodeKind.Abschnitt or CommandNodeKind.Unterabschnitt => this with
        {
            Kind = parentId is null ? CommandNodeKind.Abschnitt : CommandNodeKind.Unterabschnitt,
            ParentId = parentId,
        },
        _ when parentId is null => this,
        _ => throw new InvalidOperationException("Nur ein Abschnitt liegt in einem Abschnitt."),
    };

    /// <summary>Returns a copy dissolved at <paramref name="at"/>; a node can be dissolved only once.</summary>
    public CommandNode DissolvedAtTime(DateTimeOffset at)
    {
        if (!IsLive)
        {
            throw new InvalidOperationException("Abschnitt ist bereits aufgelöst.");
        }

        return this with { DissolvedAt = at };
    }

    /// <summary>Trims <paramref name="value"/>, maps blank to null and refuses one over <paramref name="max"/>.</summary>
    internal static string? Clean(string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > max)
        {
            throw new ArgumentException($"{label} ist länger als das Limit von {max} Zeichen.", nameof(value));
        }

        return trimmed;
    }

    private static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name darf nicht leer sein.", nameof(name));
        }

        return Clean(name, MaxNameLength, "Name") ?? string.Empty;
    }
}
