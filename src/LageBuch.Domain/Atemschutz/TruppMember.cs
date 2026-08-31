namespace LageBuch.Domain.Atemschutz;

/// <summary>
/// Position within an Atemschutztrupp. The Excel Atemschutzüberwachung sheet lays out exactly
/// Truppführer + Truppmann per Trupp; the second Truppmann exists only for a CSA-Trupp.
/// </summary>
public enum TruppRole
{
    Truppfuehrer = 0,
    Truppmann = 1,
    ZweiterTruppmann = 2,
}

/// <summary>One named person in a Trupp, addressable by their position.</summary>
public sealed record TruppMember(TruppRole Role, string Name)
{
    public static TruppMember Create(TruppRole role, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name darf nicht leer sein.", nameof(name));
        }

        return new TruppMember(role, name.Trim());
    }

    /// <summary>
    /// Builds a crew in position order: Truppführer, Truppmann, and — for a CSA-Trupp — a second
    /// Truppmann. Callers pass names and get the positions right by construction.
    /// </summary>
    public static IReadOnlyList<TruppMember> Crew(
        string truppfuehrer, string truppmann, string? zweiterTruppmann = null)
    {
        var crew = new List<TruppMember>
        {
            Create(TruppRole.Truppfuehrer, truppfuehrer),
            Create(TruppRole.Truppmann, truppmann),
        };
        if (!string.IsNullOrWhiteSpace(zweiterTruppmann))
        {
            crew.Add(Create(TruppRole.ZweiterTruppmann, zweiterTruppmann));
        }

        return crew;
    }

    public string RoleDisplay => Role switch
    {
        TruppRole.Truppfuehrer => "Truppführer",
        TruppRole.Truppmann => "Truppmann",
        TruppRole.ZweiterTruppmann => "2. Truppmann",
        _ => Role.ToString(),
    };
}
