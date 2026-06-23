using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;

namespace Feuerwehr.AppLogic.Tests;

public class ChecklistViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Setting_isdone_marks_item_done_and_fires_onchanged()
    {
        var changes = 0;
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", new[] { "A?" });
        var vm = new ChecklistViewModel(session, () => changes++);

        Assert.False(vm.Items[0].IsDone);
        // Simulates the CheckBox two-way IsChecked binding pushing the new value.
        vm.Items[0].IsDone = true;

        Assert.True(vm.Items[0].IsDone);
        Assert.True(session.Incident.Checklist[0].IsDone);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Toggling_isdone_off_again_clears_the_item()
    {
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", new[] { "A?" });
        var vm = new ChecklistViewModel(session, () => { });

        vm.Items[0].IsDone = true;
        vm.Items[0].IsDone = false;

        Assert.False(vm.Items[0].IsDone);
        Assert.False(session.Incident.Checklist[0].IsDone);
    }

    [Fact]
    public void ReadOnly_session_does_not_mutate_domain()
    {
        var clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", new[] { "A?" });
        session.Close(clock);
        var vm = new ChecklistViewModel(session, () => Assert.Fail("onChanged must not fire when read-only"));

        Assert.True(vm.IsReadOnly);
        Assert.True(vm.Items[0].IsReadOnly);
        // Even if a value change slips through, the domain stays untouched.
        vm.Items[0].IsDone = true;
        Assert.False(session.Incident.Checklist[0].IsDone);
    }
}
