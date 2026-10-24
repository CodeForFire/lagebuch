namespace LageBuch.Domain.Leadership;

/// <summary>
/// What a node on the Führung board adds up to: the Stärke and AGT of the units on it and, for an
/// Abschnitt, on its live Unterabschnitte, which the EAL leads too. Units on a dissolved node are
/// "nicht zugeordnet" (<see cref="ReportingLine.UnplacedUnits"/>) and count nowhere here.
/// </summary>
/// <param name="Zugfuehrer">Zugführer across the units.</param>
/// <param name="Officer">Gruppenführer across the units.</param>
/// <param name="Personnel">Gesamtstärke across the units.</param>
/// <param name="Scba">Atemschutzgeräteträger across the units.</param>
/// <param name="UnitCount">How many units were added up.</param>
public sealed record NodeStrength(int Zugfuehrer, int Officer, int Personnel, int Scba, int UnitCount)
{
    public int Mannschaft => Personnel - Officer - Zugfuehrer;

    /// <summary>Zugführer / Gruppenführer / Mannschaft / Gesamt, in the order <see cref="ForceUnit.StrengthText"/> uses.</summary>
    public string Text => $"{Zugfuehrer}/{Officer}/{Mannschaft}/{Personnel}";

    public static NodeStrength Of(Incident incident, Guid nodeId)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var places = incident.CommandNodes
            .Where(n => n.IsLive && (n.Id == nodeId || n.ParentId == nodeId))
            .Select(n => n.Id)
            .ToHashSet();
        var units = incident.Forces.Where(u => u.NodeId is { } id && places.Contains(id)).ToList();
        return new NodeStrength(
            units.Sum(u => u.ZugfuehrerCount),
            units.Sum(u => u.OfficerCount),
            units.Sum(u => u.PersonnelCount),
            units.Sum(u => u.ScbaCount),
            units.Count);
    }
}
