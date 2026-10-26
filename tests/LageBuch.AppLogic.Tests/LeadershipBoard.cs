using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Involved;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Tests;

/// <summary>Builds the sessions and incidents the Führung board tests run against.</summary>
internal static class LeadershipBoard
{
    internal const string IncidentPath = "/x.fwincident";

    internal static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    internal static readonly FixedClock Clock = new(T0);

    internal static readonly SessionOperator Op = new("Mustermann");

    internal static LocalIncidentSession NewSession(FakeStore? store = null) =>
        TestSession.StartNew(
            store ?? new FakeStore(),
            Clock,
            Op,
            IncidentPath,
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    internal static LeadershipViewModel NewVm(IIncidentSession session, int[]? changes = null, MasterDataSet? masterData = null) =>
        new(session, Clock, masterData ?? MasterDataSet.Empty, () =>
        {
            if (changes is not null)
            {
                changes[0]++;
            }
        });

    internal static Guid AddNode(LocalIncidentSession session, CommandNodeKind kind, string name, Guid? parent = null)
    {
        session.AddCommandNode(kind, name, parent);
        return session.Incident.CommandNodes[^1].Id;
    }

    internal static Guid AddUnit(LocalIncidentSession session, string brigade, string? callSign, Guid? nodeId = null)
    {
        session.AddForceUnit(brigade, 9, callSign, scbaCount: 4, officerCount: 1);
        var id = session.Incident.Forces[^1].Id;
        if (nodeId is not null)
        {
            session.MoveForceUnit(id, nodeId);
        }

        return id;
    }

    // Saves a hand-built (hostile) incident into a store and opens it as a session.
    internal static LocalIncidentSession Hostile(
        IEnumerable<CommandNode> nodes, IEnumerable<ForceUnit>? forces = null, IEnumerable<RoleAssignment>? roles = null)
    {
        var store = new FakeStore();
        var incident = Incident.Rehydrate(
            Guid.NewGuid(),
            T0,
            IncidentState.Open,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ChecklistList>(),
            Array.Empty<EtbEntry>(),
            roles ?? Array.Empty<RoleAssignment>(),
            forces ?? Array.Empty<ForceUnit>(),
            Array.Empty<AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<IncidentTimerState>(),
            Array.Empty<IncidentFile>(),
            Array.Empty<IncidentTask>(),
            Array.Empty<Building>(),
            Array.Empty<Dwelling>(),
            Array.Empty<InvolvedParty>(),
            commandNodes: nodes);
        store.Save(IncidentPath, incident);
        return LocalIncidentSession.Open(store, Clock, IncidentPath, Op);
    }

    internal static CommandNode Node(CommandNodeKind kind, string name, Guid? parent = null, DateTimeOffset? dissolvedAt = null) =>
        CommandNode.Rehydrate(Guid.NewGuid(), kind, name, parent, null, null, null, null, null, dissolvedAt);

    internal static ForceUnit Unit(string brigade, Guid? nodeId) =>
        ForceUnit.Rehydrate(Guid.NewGuid(), brigade, null, 6, 0, null, null, 0, nodeId: nodeId);
}
