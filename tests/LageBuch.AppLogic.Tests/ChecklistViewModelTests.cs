using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.AppLogic.Tests;

public class ChecklistViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static LocalIncidentSession NewSession(
        IEnumerable<(string, bool)>? aufbau = null, IEnumerable<(string, bool)>? abbau = null) =>
        LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident",
            aufbau ?? new[] { ("A?", false) }, abbau ?? Array.Empty<(string, bool)>());

    [Fact]
    public void Setting_isdone_marks_item_done_and_fires_onchanged()
    {
        var changes = 0;
        var session = NewSession();
        var vm = new ChecklistViewModel(session, ChecklistKind.Aufbau, () => changes++);

        Assert.False(vm.Items[0].IsDone);
        // Simulates the CheckBox two-way IsChecked binding pushing the new value.
        vm.Items[0].IsDone = true;

        Assert.True(vm.Items[0].IsDone);
        Assert.True(session.Incident.ChecklistAufbau[0].IsDone);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Toggling_isdone_off_again_clears_the_item()
    {
        var session = NewSession();
        var vm = new ChecklistViewModel(session, ChecklistKind.Aufbau, () => { });

        vm.Items[0].IsDone = true;
        vm.Items[0].IsDone = false;

        Assert.False(vm.Items[0].IsDone);
        Assert.False(session.Incident.ChecklistAufbau[0].IsDone);
    }

    [Fact]
    public void ReadOnly_session_does_not_mutate_domain()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", new[] { ("A?", false) }, Array.Empty<(string, bool)>());
        session.Close();
        var vm = new ChecklistViewModel(session, ChecklistKind.Aufbau, () => Assert.Fail("onChanged must not fire when read-only"));

        Assert.True(vm.IsReadOnly);
        Assert.True(vm.Items[0].IsReadOnly);
        // Even if a value change slips through, the domain stays untouched.
        vm.Items[0].IsDone = true;
        Assert.False(session.Incident.ChecklistAufbau[0].IsDone);
    }

    [Fact]
    public void Items_carry_the_mandatory_flag_from_the_domain()
    {
        var session = NewSession(aufbau: new[] { ("Pflicht", true), ("Optional", false) });
        var vm = new ChecklistViewModel(session, ChecklistKind.Aufbau, () => { });

        Assert.True(vm.Items[0].IsMandatory);
        Assert.False(vm.Items[1].IsMandatory);
    }

    [Fact]
    public void AllMandatoryDone_flips_true_once_every_mandatory_item_is_checked()
    {
        var session = NewSession(aufbau: new[] { ("Pflicht", true), ("Optional", false) });
        var vm = new ChecklistViewModel(session, ChecklistKind.Aufbau, () => { });

        Assert.False(vm.AllMandatoryDone);

        vm.Items[1].IsDone = true; // optional item: still not complete
        Assert.False(vm.AllMandatoryDone);

        vm.Items[0].IsDone = true; // the mandatory item: now complete
        Assert.True(vm.AllMandatoryDone);
    }

    [Fact]
    public void AllMandatoryDone_starts_true_when_the_list_has_no_mandatory_items()
    {
        var session = NewSession(aufbau: new[] { ("Optional", false) });
        var vm = new ChecklistViewModel(session, ChecklistKind.Aufbau, () => { });

        Assert.True(vm.AllMandatoryDone);
    }

    [Fact]
    public void Aufbau_and_abbau_view_models_are_independent()
    {
        var session = NewSession(
            aufbau: new[] { ("Aufbau Pflicht", true) },
            abbau: new[] { ("Abbau Pflicht", true) });
        var aufbau = new ChecklistViewModel(session, ChecklistKind.Aufbau, () => { });
        var abbau = new ChecklistViewModel(session, ChecklistKind.Abbau, () => { });

        aufbau.Items[0].IsDone = true;

        Assert.True(aufbau.AllMandatoryDone);
        Assert.False(abbau.AllMandatoryDone);
    }
}
