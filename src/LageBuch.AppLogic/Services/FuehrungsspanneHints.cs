using LageBuch.Domain;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.Services;

/// <summary>FwDV 100's span of control: at most five subordinates per leader. Hints, never refusals.</summary>
public static class FuehrungsspanneHints
{
    private const int MaxSpan = 5;

    public static IReadOnlyList<SpanHint> For(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var hints = new List<SpanHint>();
        var live = incident.CommandNodes.Where(n => n.IsLive).ToList();
        if (live.Count(n => n.Kind == CommandNodeKind.Abschnitt) > MaxSpan)
        {
            hints.Add(new SpanHint(null, "FwDV 100: höchstens 5 Einsatzabschnitte"));
        }

        foreach (var node in live.Where(n => n.Kind is CommandNodeKind.Abschnitt or CommandNodeKind.Bereitstellung))
        {
            if (incident.Forces.Count(u => u.NodeId == node.Id) > MaxSpan)
            {
                hints.Add(new SpanHint(node.Id, "Mehr als 5 Einheiten – Unterabschnitt bilden?"));
            }
        }

        return hints;
    }
}
