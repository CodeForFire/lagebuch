using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;

namespace Feuerwehr.AppLogic.Tests;

public class ChecklistViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Toggle_marks_item_done_and_fires_onchanged()
    {
        var changes = 0;
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", new[] { "A?" });
        var vm = new ChecklistViewModel(session, () => changes++);

        Assert.False(vm.Items[0].IsDone);
        vm.Items[0].ToggleCommand.Execute(null);

        Assert.True(vm.Items[0].IsDone);
        Assert.True(session.Incident.Checklist[0].IsDone);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ReadOnly_session_disables_toggle()
    {
        var clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", new[] { "A?" });
        session.Close(clock);
        var vm = new ChecklistViewModel(session, () => { });

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.Items[0].ToggleCommand.CanExecute(null));
    }
}
