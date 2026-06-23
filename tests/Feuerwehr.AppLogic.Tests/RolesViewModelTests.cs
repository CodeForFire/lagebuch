using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class RolesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => new(
        Roles: new[] { "EL", "ZF" }, Status: Array.Empty<string>(), Equipment: Array.Empty<string>(),
        Districts: Array.Empty<string>(), RadioCallSigns: new[] { "FFB 12/1" },
        Streets: Array.Empty<Street>(), ChecklistTemplate: Array.Empty<string>());

    [Fact]
    public void AddRole_appends_and_clears_and_fires_onchanged()
    {
        var changes = 0;
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var vm = new RolesViewModel(session, Md(), () => changes++)
        {
            NewRole = "EL",
            NewPersonName = "Müller",
            NewCallSign = "FFB 12/1"
        };

        Assert.Equal(new[] { "EL", "ZF" }, vm.RoleOptions);
        Assert.True(vm.AddRoleCommand.CanExecute(null));
        vm.AddRoleCommand.Execute(null);

        Assert.Single(session.Incident.Roles);
        Assert.Single(vm.Roles);
        Assert.Equal("", vm.NewPersonName);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddRole_disabled_when_role_or_person_blank()
    {
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var vm = new RolesViewModel(session, Md(), () => { }) { NewRole = "EL", NewPersonName = "" };
        Assert.False(vm.AddRoleCommand.CanExecute(null));
    }

    [Fact]
    public void ReadOnly_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        session.Close(clock);
        var vm = new RolesViewModel(session, Md(), () => { }) { NewRole = "EL", NewPersonName = "Müller" };
        Assert.False(vm.AddRoleCommand.CanExecute(null));
    }
}
