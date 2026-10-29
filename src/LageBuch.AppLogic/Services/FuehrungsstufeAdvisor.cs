using LageBuch.Domain;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Suggests the Führungsstufe from the shape of the Einsatz. Only a suggestion: the Lagebuchführer
/// decides. Counts live nodes, running assignments and all units.
/// </summary>
public static class FuehrungsstufeAdvisor
{
    public static FuehrungsstufeSuggestion Suggest(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var live = incident.CommandNodes.Where(n => n.IsLive).ToList();
        var abschnitte = live.Count(n => n.Kind == CommandNodeKind.Abschnitt);
        var partner = live.Count(n => n.Kind == CommandNodeKind.Partner);
        var units = incident.Forces.Count;
        var abschnittText = Count(abschnitte, "Abschnitt", "Abschnitte");
        var counts = $"{abschnittText}, {Count(units, "Einheit", "Einheiten")}";

        if (live.Exists(n => n.Kind == CommandNodeKind.Unterabschnitt))
        {
            return new FuehrungsstufeSuggestion(Fuehrungsstufe.D, "Unterabschnitte gebildet");
        }

        if (partner >= 2 && abschnitte > 5)
        {
            return new FuehrungsstufeSuggestion(
                Fuehrungsstufe.D, $"{abschnittText}, {Formatting.Number(partner)} Partner-Einsatzleitungen");
        }

        if (incident.Roles.Any(r => r.To is null && r.Slot == RoleSlot.Fuehrungsassistent))
        {
            return new FuehrungsstufeSuggestion(Fuehrungsstufe.C, "Führungsunterstützung besetzt");
        }

        if (abschnitte >= 3)
        {
            return new FuehrungsstufeSuggestion(Fuehrungsstufe.C, counts);
        }

        return new FuehrungsstufeSuggestion(abschnitte >= 1 || units > 2 ? Fuehrungsstufe.B : Fuehrungsstufe.A, counts);
    }

    private static string Count(int n, string singular, string plural) => n switch
    {
        0 => $"keine {plural}",
        1 => $"1 {singular}",
        _ => $"{Formatting.Number(n)} {plural}",
    };
}
