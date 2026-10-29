using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Names the rail modules that hold work in progress in one Einsatz, and so must stay visible
/// whatever the Einsatz's overrides say.
/// </summary>
/// <remarks>
/// Hiding a module is a tidy-up, never a way to lose sight of a Trupp under Atemschutz or an open
/// Aufgabe. A pure function of the incident, so the rule can be tested on its own.
/// </remarks>
public static class NavLiveness
{
    /// <summary>
    /// The <see cref="NavModules"/> keys with live work: Atemschutz while a Trupp is active or
    /// withdrawing, Aufgaben while a task is not yet completed.
    /// </summary>
    public static IReadOnlySet<string> LiveModules(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var live = new HashSet<string>(StringComparer.Ordinal);
        if (incident.ScbaTrupps.Any(t => t.IsActive || t.IsWithdrawing))
        {
            live.Add(NavModules.Scba);
        }

        if (incident.Tasks.Any(t => !t.IsCompleted))
        {
            live.Add(NavModules.Tasks);
        }

        return live;
    }
}
