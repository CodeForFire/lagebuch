using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// A module that has live work in it must not be hideable: an Atemschutz-Trupp under Atemschutz
// or an open Aufgabe cannot vanish from the rail because someone unticked it earlier.
public class NavLivenessTests
{
    private static readonly SessionOperator Op = new("Host");

    private static AtemschutzTrupp Trupp(Incident incident, IClock clock) =>
        incident.AddScbaTrupp(
            clock,
            "Trupp 1",
            new[] { TruppMember.Create(TruppRole.Truppfuehrer, "A"), TruppMember.Create(TruppRole.Truppmann, "B") },
            300);

    [Fact]
    public void An_active_or_withdrawing_trupp_makes_atemschutz_live_and_a_returned_one_does_not()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var incident = Incident.Start(clock, Op, "Brand");
        Assert.DoesNotContain(NavModules.Scba, NavLiveness.LiveModules(incident));

        var trupp = Trupp(incident, clock);
        Assert.DoesNotContain(NavModules.Scba, NavLiveness.LiveModules(incident));

        incident.StartScbaTrupp(clock, trupp.Id);
        Assert.Contains(NavModules.Scba, NavLiveness.LiveModules(incident));

        incident.WithdrawScbaTrupp(clock, trupp.Id);
        Assert.Contains(NavModules.Scba, NavLiveness.LiveModules(incident));

        incident.MarkScbaRemoved(clock, trupp.Id);
        Assert.DoesNotContain(NavModules.Scba, NavLiveness.LiveModules(incident));
    }

    [Fact]
    public void An_open_task_makes_aufgaben_live_and_a_completed_one_does_not()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var incident = Incident.Start(clock, Op, "Brand");
        Assert.DoesNotContain(NavModules.Tasks, NavLiveness.LiveModules(incident));

        var task = incident.AddTask(clock, Op, "Strom abstellen", null, TaskImportance.High, TaskUrgency.High, 5);
        Assert.Contains(NavModules.Tasks, NavLiveness.LiveModules(incident));

        incident.SetTaskCompleted(task.Id, true, clock, Op);
        Assert.DoesNotContain(NavModules.Tasks, NavLiveness.LiveModules(incident));
    }
}
