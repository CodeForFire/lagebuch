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

    /// <summary>
    /// The single, untyped Funkgruppe a node carried before #878. Read back from an old file or
    /// snapshot only: the V41 migration moves it into <see cref="Funkgruppen"/>, and
    /// <see cref="WithDetails"/> refuses a new one. An edit keeps one the migration had no room for,
    /// until the node's Funkgruppen hold it.
    /// </summary>
    public string? Funkgruppe { get; private init; }

    /// <summary>The node's TMO and DMO Funkgruppen: a partner Einsatzleitung's since #828, every other kind's since #878.</summary>
    public Funkgruppen Funkgruppen { get; private init; } = Funkgruppen.Empty;

    public string? Rufname { get; private init; }

    public string? Telefon { get; private init; }

    /// <summary>The Funkrufname of the vehicle the node is led from (#829), e.g. "FFB 1/12/1"; null when none.</summary>
    public string? Fuehrungsfahrzeug { get; private init; }

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
        Funkgruppen? funkgruppen = null,
        string? fuehrungsfahrzeug = null)
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
            Fuehrungsfahrzeug = fuehrungsfahrzeug,
        };

    /// <summary>
    /// Returns a copy with corrected details, trimmed and capped the way <see cref="Create"/> does.
    /// Every kind takes TMO and DMO <paramref name="funkgruppen"/> (#878), null for none, and refuses
    /// a single untyped <paramref name="funkgruppe"/>. The copy keeps the single one an old file
    /// carried only while the new Funkgruppen lack it, so an unrelated edit never drops it. Every kind takes a
    /// <paramref name="fuehrungsfahrzeug"/>, null for none.
    /// </summary>
    public CommandNode WithDetails(
        string name,
        string? auftrag,
        string? standort,
        string? funkgruppe,
        string? rufname,
        string? telefon,
        Funkgruppen? funkgruppen = null,
        string? fuehrungsfahrzeug = null)
    {
        if (!string.IsNullOrWhiteSpace(funkgruppe))
        {
            throw new ArgumentException("Funkgruppen werden als TMO und DMO erfasst.", nameof(funkgruppe));
        }

        var groups = (funkgruppen ?? Funkgruppen.Empty).Normalized();

        return this with
        {
            Name = CleanName(name),
            Auftrag = Clean(auftrag, MaxAuftragLength, "Auftrag"),
            Standort = Clean(standort, MaxTextLength, "Standort"),
            Funkgruppe = KeptSingle(Funkgruppe, groups),
            Funkgruppen = groups,
            Rufname = Clean(rufname, MaxTextLength, "Rufname"),
            Telefon = Clean(telefon, MaxPhoneLength, "Telefon"),
            Fuehrungsfahrzeug = Clean(fuehrungsfahrzeug, MaxTextLength, "Führungsfahrzeug"),
        };
    }

    // The single Funkgruppe of before #878 is written "TMO X", "DMO X" or just "X".
    private static string? KeptSingle(string? single, Funkgruppen groups)
    {
        if (single is null)
        {
            return null;
        }

        var group = single.Trim();
        var isDmo = group.StartsWith("DMO ", StringComparison.OrdinalIgnoreCase);
        if (isDmo || group.StartsWith("TMO ", StringComparison.OrdinalIgnoreCase))
        {
            group = group[4..];
        }

        // Only the list of its own kind holds it, as V41 would have put it: TMO unless it says DMO.
        var ofItsKind = isDmo ? groups.Dmo : groups.Tmo;
        return ofItsKind.Any(g => FunkCheck.SameGroup(g, group)) ? null : single;
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
