using LageBuch.Domain.Leadership;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Places a unit in a node and brings its status along (#827). A Fahrzeug placed in a
/// Bereitstellungsraum is in the Bereitstellungsraum, and one placed in an Abschnitt is im Einsatz,
/// so the Kräfte tab must not go on showing "Auf Anfahrt" after Organisation has put it to work.
/// <para>
/// This lives here rather than in <c>Incident.MoveForceUnit</c> for two reasons. The status takes
/// the Stammdaten's own spelling, and only AppLogic sees the Stammdaten. And it is two existing
/// session calls, so a joined client needs no new command and the sync protocol is unchanged. The
/// ETB gets two lines for it, the move and the status change, which is what happened.
/// </para>
/// </summary>
public static class UnitPlacement
{
    /// <summary>The status a unit placed in a Bereitstellungsraum gets.</summary>
    public const string BereitstellungsraumStatus = "Bereitstellungsraum";

    /// <summary>The status a unit placed in an Abschnitt or Unterabschnitt gets.</summary>
    public const string ImEinsatzStatus = "Im Einsatz";

    /// <summary>
    /// The status placement in a node of <paramref name="kind"/> implies, spelt the way
    /// <paramref name="catalogue"/> spells it, or null when the kind implies none. The fixed name is
    /// used as it is when the catalogue does not list it, so a fresh install without Stammdaten
    /// still gets one.
    /// </summary>
    public static string? ImpliedStatus(CommandNodeKind kind, IReadOnlyList<string> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return kind switch
        {
            CommandNodeKind.Bereitstellung => StammdatenCatalogue.Normalize(BereitstellungsraumStatus, catalogue),
            CommandNodeKind.Abschnitt or CommandNodeKind.Unterabschnitt =>
                StammdatenCatalogue.Normalize(ImEinsatzStatus, catalogue),
            _ => null,
        };
    }

    /// <summary>
    /// Moves <paramref name="unitId"/> to <paramref name="nodeId"/> (null = takes it out of its
    /// node), then sets the status the new node implies. Taking a unit out leaves its status alone:
    /// nothing says what it is doing now. The session's refusals propagate as they are, before any
    /// status is written.
    /// </summary>
    public static void Move(IIncidentSession session, IReadOnlyList<string> statusCatalogue, Guid unitId, Guid? nodeId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(statusCatalogue);

        // Read before the move: a joined client's Incident may not show the move yet afterwards.
        var unit = session.Incident.Forces.FirstOrDefault(f => f.Id == unitId);
        var target = nodeId is { } id ? session.Incident.CommandNodes.FirstOrDefault(n => n.Id == id) : null;

        session.MoveForceUnit(unitId, nodeId);

        if (unit is null || target is null || unit.NodeId == nodeId)
        {
            return;
        }

        var status = ImpliedStatus(target.Kind, statusCatalogue);
        if (status is not null && !string.Equals(status, unit.Status, StringComparison.Ordinal))
        {
            session.UpdateForceUnit(unitId, status, unit.Notes);
        }
    }
}
