using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

// The "Einsatzdaten" overlay: Stichwort, Einsatznummer, Straße and Ortsteil edited together and
// buffered until SPEICHERN. Replaces the inline Einsatznummer editor (#69) and gives the address
// its first UI -- until now the PDF's "Adresse" line was always empty.
public class IncidentDataDialogViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static LocalIncidentSession NewSession(FakeStore store, string? keyword = null, IncidentNumber? number = null)
    {
        return TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>(),
            number,
            keyword: keyword);
    }

    [Fact]
    public void Fields_are_seeded_from_the_incident()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P", number: new IncidentNumber("B 1.2 260715 123"));
        session.SetAddress("Hauptstr. 12", "FFB");

        var vm = new IncidentDataDialogViewModel(session, () => { });

        Assert.Equal("B3P", vm.Keyword);
        Assert.Equal("B 1.2 260715 123", vm.IncidentNumber);
        Assert.Equal("Hauptstr. 12", vm.Street);
        Assert.Equal("FFB", vm.District);
    }

    [Fact]
    public void Save_writes_all_four_fields_and_persists_them()
    {
        var store = new FakeStore();
        var session = NewSession(store);
        var changed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => changed++);

        vm.Keyword = " B3P ";
        vm.IncidentNumber = " B 1.2 260715 123 ";
        vm.Street = "Hauptstr. 12";
        vm.District = "FFB";
        vm.SaveCommand.Execute(null);

        var saved = store.Load("/x.fwincident");
        Assert.Equal("B3P", saved.Keyword);
        Assert.Equal("B 1.2 260715 123", saved.IncidentNumber!.Value);
        Assert.Equal("Hauptstr. 12", saved.Street);
        Assert.Equal("FFB", saved.District);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Blank_fields_clear_the_values()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P", number: new IncidentNumber("B 99"));
        session.SetAddress("Hauptstr. 12", "FFB");
        var vm = new IncidentDataDialogViewModel(session, () => { });

        vm.Keyword = "   ";
        vm.IncidentNumber = string.Empty;
        vm.Street = null;
        vm.District = " ";
        vm.SaveCommand.Execute(null);

        var saved = store.Load("/x.fwincident");
        Assert.Null(saved.Keyword);
        Assert.Null(saved.IncidentNumber);
        Assert.Null(saved.Street);
        Assert.Null(saved.District);
    }

    [Fact]
    public void Nothing_is_written_before_save()
    {
        var store = new FakeStore();
        var session = NewSession(store);
        var vm = new IncidentDataDialogViewModel(session, () => { });
        var before = store.SaveCount;

        vm.Keyword = "B3P";
        vm.Street = "Hauptstr. 12";

        Assert.Equal(before, store.SaveCount);
        Assert.Null(store.Load("/x.fwincident").Keyword);
    }

    [Fact]
    public void Cancel_discards_and_closes()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P");
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => { });
        vm.Closed += (_, _) => closed++;
        var before = store.SaveCount;

        vm.Keyword = "B4";
        vm.CancelCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal(before, store.SaveCount);
        Assert.Equal("B3P", store.Load("/x.fwincident").Keyword);
    }

    [Fact]
    public void Save_closes_the_dialog()
    {
        var session = NewSession(new FakeStore());
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => { });
        vm.Closed += (_, _) => closed++;

        vm.SaveCommand.Execute(null);

        Assert.Equal(1, closed);
    }

    [Fact]
    public void Unchanged_fields_do_not_trigger_a_save()
    {
        // A no-op SPEICHERN must not fan out into three redundant saves / sync commands.
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P", number: new IncidentNumber("B 99"));
        session.SetAddress("Hauptstr. 12", "FFB");
        var changed = 0;
        var closed = 0;
        var vm = new IncidentDataDialogViewModel(session, () => changed++);
        vm.Closed += (_, _) => closed++;
        var before = store.SaveCount;

        vm.SaveCommand.Execute(null);

        Assert.Equal(before, store.SaveCount);
        Assert.Equal(0, changed); // nothing saved, so the host's "gespeichert" line must not tick
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Only_the_changed_field_is_saved()
    {
        var store = new FakeStore();
        var session = NewSession(store, keyword: "B3P", number: new IncidentNumber("B 99"));
        var vm = new IncidentDataDialogViewModel(session, () => { });
        var before = store.SaveCount;

        vm.Street = "Hauptstr. 12";
        vm.SaveCommand.Execute(null);

        Assert.Equal(before + 1, store.SaveCount);
        Assert.Equal("Hauptstr. 12", store.Load("/x.fwincident").Street);
    }

    [Fact]
    public void Save_is_blocked_on_a_readonly_session()
    {
        var store = new FakeStore();
        NewSession(store);
        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.fwincident");

        var vm = new IncidentDataDialogViewModel(ro, () => { });

        Assert.False(vm.SaveCommand.CanExecute(null));
    }
}
