namespace LageBuch.Domain.Leadership;

/// <summary>Which post of a Führungsstelle a role fills. Members are never renamed: they travel by name on the wire.</summary>
public enum RoleSlot
{
    None,
    Leiter,
    Melder,
    Fuehrungsassistent,
    Fachberater,
}
