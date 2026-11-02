using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// Changing a Trupp's Trupp-Art from its row (#835). The ETB wording is asserted literally, as in
/// <see cref="ScbaSafetyTruppTests"/>: the line is persisted and read back off a printed
/// Einsatzbericht.
/// </summary>
public class ScbaTruppArtChangeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
        TruppTypes = new[]
        {
            new TruppType("Angriffstrupp"),
            new TruppType("Sicherheitstrupp"),
            new TruppType("CSA-Trupp", MemberCount: 2, MaxDurationMinutes: 20),
        },
    };

    private static LocalIncidentSession NewSession(FixedClock clock) =>
        TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static ScbaViewModel Vm(FixedClock clock, LocalIncidentSession session) =>
        new(session, Md(), clock, new FakeTicker(), new FakeAlarmService(), () => { });

    private static ScbaTruppRow Register(ScbaViewModel vm, string designation, string? callSign = null)
    {
        vm.NewDesignation = designation;
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        vm.NewZweiterTruppmann = string.Empty;
        vm.NewCallSign = callSign ?? string.Empty;
        vm.AddTruppCommand.Execute(null);
        return vm.Trupps[^1];
    }

    private static ScbaTruppRow Row(ScbaViewModel vm, Guid id) => vm.Trupps.Single(r => r.Id == id);

    /// <summary>Invokes the flyout entry for <paramref name="name"/>, as a click would.</summary>
    private static void Change(ScbaViewModel vm, Guid truppId, string name) =>
        Row(vm, truppId).TruppTypeChoices.Single(c => c.Name == name).SelectCommand.Execute(null);

    private static Domain.Atemschutz.AtemschutzTrupp Trupp(LocalIncidentSession session, Guid id) =>
        session.Incident.ScbaTrupps.Single(t => t.Id == id);

    [Fact]
    public void Choices_are_todays_trupp_arten_with_the_current_one_marked()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        var row = Register(vm, "Sicherheitstrupp");

        Assert.Equal(new[] { "Angriffstrupp", "Sicherheitstrupp", "CSA-Trupp" }, row.TruppTypeChoices.Select(c => c.Name));
        Assert.Equal("Sicherheitstrupp", Assert.Single(row.TruppTypeChoices, c => c.IsCurrent).Name);
        Assert.True(row.CanChangeTruppType);
    }

    [Fact]
    public void A_trupp_art_the_stammdaten_no_longer_list_stays_listed_and_marked()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        var row = Register(vm, "Wassertrupp");

        Assert.Equal(new[] { "Angriffstrupp", "Sicherheitstrupp", "CSA-Trupp", "Wassertrupp" }, row.TruppTypeChoices.Select(c => c.Name));
        Assert.Equal("Wassertrupp", Assert.Single(row.TruppTypeChoices, c => c.IsCurrent).Name);
    }

    [Fact]
    public void A_waiting_trupp_takes_the_new_einsatzzeit_and_the_etb_says_so()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm, "Sicherheitstrupp", "Florian Musterdorf 42/1");

        Change(vm, row.Id, "CSA-Trupp");

        var trupp = Trupp(session, row.Id);
        Assert.Equal("CSA-Trupp", trupp.Designation);
        Assert.Equal(20, trupp.MaxDurationMinutes);
        Assert.Equal(6, trupp.PressureControlIntervalMinutes);
        var entry = session.Incident.Journal[^1];
        Assert.Equal(
            "Trupp-Art von Florian Musterdorf 42/1 gewechselt: bisher Sicherheitstrupp, jetzt CSA-Trupp, Einsatzzeit 20 Min.",
            entry.Text);
        Assert.Equal("Florian Musterdorf 42/1", entry.From);
        Assert.Equal("Florian Musterdorf 42/1 · CSA-Trupp", Row(vm, row.Id).DisplayName);
        Assert.Equal("CSA-Trupp", Assert.Single(Row(vm, row.Id).TruppTypeChoices, c => c.IsCurrent).Name);
    }

    // The row is re-pointed in place (#294), so the view only learns of the new name if the row says so.
    [Fact]
    public void The_row_announces_the_new_name_and_choices()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        var row = Register(vm, "Sicherheitstrupp");
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Change(vm, row.Id, "Angriffstrupp");

        Assert.Same(row, Row(vm, row.Id));
        Assert.Contains(nameof(ScbaTruppRow.DisplayName), raised);
        Assert.Contains(nameof(ScbaTruppRow.Designation), raised);
        Assert.Contains(nameof(ScbaTruppRow.TruppTypeChoices), raised);
        Assert.Contains(nameof(ScbaTruppRow.CanChangeTruppType), raised);
        Assert.Equal("Angriffstrupp", row.Designation);
    }

    [Fact]
    public void A_trupp_art_with_the_same_einsatzzeit_leaves_it_out_of_the_line()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm, "Sicherheitstrupp", "Florian Musterdorf 42/1");

        Change(vm, row.Id, "Angriffstrupp");

        Assert.Equal(
            "Trupp-Art von Florian Musterdorf 42/1 gewechselt: bisher Sicherheitstrupp, jetzt Angriffstrupp",
            session.Incident.Journal[^1].Text);
    }

    [Fact]
    public void A_trupp_under_air_keeps_its_einsatzzeit_and_is_named_by_number_without_a_funkrufname()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm, "Sicherheitstrupp");
        row.StartCommand.Execute(null);

        Change(vm, row.Id, "CSA-Trupp");

        var trupp = Trupp(session, row.Id);
        Assert.Equal("CSA-Trupp", trupp.Designation);
        Assert.Equal(30, trupp.MaxDurationMinutes);
        Assert.Equal(10, trupp.PressureControlIntervalMinutes);
        Assert.Equal(
            "Trupp-Art von Trupp 1 gewechselt: bisher Sicherheitstrupp, jetzt CSA-Trupp",
            session.Incident.Journal[^1].Text);
    }

    [Fact]
    public void Re_choosing_the_current_trupp_art_writes_nothing()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm, "Sicherheitstrupp");
        var before = session.Incident.Journal.Count;

        Change(vm, row.Id, "Sicherheitstrupp");

        Assert.Equal(before, session.Incident.Journal.Count);
    }

    [Fact]
    public void An_abgenommen_trupp_cannot_change_its_trupp_art()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm, "Sicherheitstrupp");
        row.StartCommand.Execute(null);
        row.WithdrawCommand.Execute(null);
        row.MarkRemovedCommand.Execute(null);
        var before = session.Incident.Journal.Count;

        Assert.False(Row(vm, row.Id).CanChangeTruppType);
        Change(vm, row.Id, "Angriffstrupp");

        Assert.Equal("Sicherheitstrupp", Trupp(session, row.Id).Designation);
        Assert.Equal(before, session.Incident.Journal.Count);
    }

    [Fact]
    public void A_read_only_workspace_cannot_change_a_trupp_art()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var row = Register(Vm(clock, session), "Sicherheitstrupp");
        session.Close();

        var readOnly = Vm(clock, session);
        var before = session.Incident.Journal.Count;

        Assert.False(Row(readOnly, row.Id).CanChangeTruppType);
        Change(readOnly, row.Id, "Angriffstrupp");

        Assert.Equal("Sicherheitstrupp", Trupp(session, row.Id).Designation);
        Assert.Equal(before, session.Incident.Journal.Count);
    }
}
