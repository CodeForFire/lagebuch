using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class ForcesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => new(
        Roles: Array.Empty<string>(), Status: Array.Empty<string>(), Equipment: Array.Empty<string>(),
        Districts: Array.Empty<string>(), RadioCallSigns: new[] { "FFB 1/40/1" },
        Streets: Array.Empty<Street>(), ChecklistTemplate: Array.Empty<string>());

    [Fact]
    public void AddForce_appends_and_updates_total()
    {
        var changes = 0;
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var vm = new ForcesViewModel(session, Md(), () => changes++)
        {
            NewBrigade = "FFB",
            NewPersonnelCount = 12
        };

        Assert.True(vm.AddForceCommand.CanExecute(null));
        vm.AddForceCommand.Execute(null);
        vm.NewBrigade = "Emmering";
        vm.NewPersonnelCount = 9;
        vm.AddForceCommand.Execute(null);

        Assert.Equal(2, vm.Forces.Count);
        Assert.Equal(21, vm.TotalPersonnel);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void AddForce_disabled_when_brigade_blank()
    {
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var vm = new ForcesViewModel(session, Md(), () => { }) { NewBrigade = "  ", NewPersonnelCount = 5 };
        Assert.False(vm.AddForceCommand.CanExecute(null));
    }
}
