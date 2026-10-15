using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.AppLogic.Tests;

public class SituationReportComposerViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static LocalIncidentSession NewSession() =>
        TestSession.StartNew(
            new FakeStore(),
            new FixedClock(T0),
            new SessionOperator("Muster", "Florian Musterstadt 11/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static SituationReportComposerViewModel NewVm(
        LocalIncidentSession session, Action<string, string, Action>? requestConfirm = null) =>
        new(session, () => { }, "ILS", requestConfirm);

    [Fact]
    public void Opening_starts_a_draft_prefilled_from_the_funkrufname_the_address_and_the_kraefte()
    {
        var session = NewSession();
        session.SetAddress("Blumenstraße 13", "Nord");
        session.AddForceUnit("FF Musterstadt", 9, callSign: "HLF 1", officerCount: 1);
        session.AddForceUnit("FF Beispieldorf", 6, officerCount: 1);
        using var vm = NewVm(session);

        vm.OpenCommand.Execute(null);

        Assert.True(vm.IsOpen);
        Assert.False(Assert.Single(session.Incident.SituationReports).IsSent);
        Assert.Equal("Florian Musterstadt 11/1", vm.Reporter);
        Assert.Equal("Blumenstraße 13, Nord", vm.Location);
        Assert.Equal("FF Musterstadt (HLF 1) 0/1/8/9, FF Beispieldorf 0/1/5/6", vm.Units);
        Assert.Null(vm.Situation);
        Assert.True(vm.IsEditable);
    }

    [Fact]
    public void Closing_keeps_the_draft_and_opening_again_carries_on_with_it()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);
        vm.Situation = "Schuppen im Vollbrand";

        vm.CloseCommand.Execute(null);
        vm.OpenCommand.Execute(null);

        Assert.False(Assert.Single(session.Incident.SituationReports).IsSent);
        Assert.Equal("Schuppen im Vollbrand", vm.Situation);
    }

    [Fact]
    public void Opening_resumes_a_draft_started_elsewhere_instead_of_starting_another()
    {
        var session = NewSession();
        session.AddSituationReport("ELW 1", null, "vom anderen Gerät", null, null, null);
        using var vm = NewVm(session);

        vm.OpenCommand.Execute(null);

        Assert.Single(session.Incident.SituationReports);
        Assert.Equal("vom anderen Gerät", vm.Situation);
    }

    [Fact]
    public void The_funkspruch_reads_one_part_per_line_with_the_einsatzstelle_spelled_under_it()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);
        vm.Location = "Ab 1";
        vm.Situation = "Feuer aus";
        vm.Requests = "1 RTW";

        Assert.Equal(
            new[]
            {
                new FunkspruchLine("Ab 1", "Anton – Berta / eins"),
                new FunkspruchLine("Feuer aus", null),
                new FunkspruchLine("Nachforderung: 1 RTW", null),
            },
            vm.FunkspruchLines);
        Assert.Null(session.Incident.SituationReports[0].Situation);
    }

    [Fact]
    public void The_einsatzstelle_is_spelled_in_the_alphabet_chosen_in_the_helper()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);
        vm.Location = "Ab";
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Helper.IsNato = true;

        Assert.Equal("Alfa – Bravo", vm.FunkspruchLines[0].Spelling);
        Assert.Contains(nameof(SituationReportComposerViewModel.FunkspruchLines), changed);
    }

    [Fact]
    public void Commit_writes_the_draft_once_and_a_second_commit_without_a_change_writes_nothing()
    {
        var session = NewSession();
        var changes = 0;
        using var vm = new SituationReportComposerViewModel(session, () => changes++, "ILS");
        vm.OpenCommand.Execute(null);
        vm.Situation = "Schuppen im Vollbrand";
        changes = 0;

        vm.CommitCommand.Execute(null);
        vm.CommitCommand.Execute(null);

        Assert.Equal("Schuppen im Vollbrand", session.Incident.SituationReports[0].Situation);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_refresh_from_elsewhere_keeps_the_typing_in_progress()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);

        vm.Measures = "C-Rohr vor";
        session.AddJournalEntry(EtbDirection.Internal, "Notiz von einem anderen Gerät");

        Assert.Equal("C-Rohr vor", vm.Measures);
    }

    [Fact]
    public void Send_asks_first_then_writes_the_funkspruch_to_the_etb_and_closes_the_panel()
    {
        var session = NewSession();
        string? confirmLabel = null;
        Action? confirm = null;
        using var vm = NewVm(session, (_, label, onConfirmed) =>
        {
            confirmLabel = label;
            confirm = onConfirmed;
        });
        vm.OpenCommand.Execute(null);
        vm.Location = "Blumenstraße 13";
        vm.Situation = "Schuppen im Vollbrand";

        vm.SendCommand.Execute(null);

        Assert.Equal("SENDEN", confirmLabel);
        Assert.True(vm.IsOpen);
        Assert.NotNull(confirm);
        confirm();

        Assert.False(vm.IsOpen);
        Assert.Equal(1, session.Incident.SituationReports[0].Number);
        var entry = session.Incident.Journal[^1];
        Assert.Equal(EtbDirection.Outgoing, entry.Direction);
        Assert.Equal("Lagemeldung Nr. 1: Blumenstraße 13 · Schuppen im Vollbrand", entry.Text);
    }

    [Fact]
    public void After_a_send_the_next_open_starts_a_fresh_draft()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);
        vm.Situation = "Erste Lage";
        vm.SendCommand.Execute(null);

        vm.OpenCommand.Execute(null);

        Assert.Equal(2, session.Incident.SituationReports.Count);
        Assert.Null(vm.Situation);
        Assert.True(vm.IsEditable);
    }

    [Fact]
    public void A_draft_sent_on_another_device_closes_the_panel_here()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);
        vm.Situation = "Lage";
        vm.CommitCommand.Execute(null);

        session.SendSituationReport(session.Incident.SituationReports[0].Id, "ILS");

        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void An_empty_draft_cannot_be_sent()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);

        Assert.False(vm.SendCommand.CanExecute(null));
        Assert.False(vm.HasFunkspruch);
    }

    [Fact]
    public void RefreshUnits_takes_over_the_kraefte_as_they_stand_now()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);
        session.AddForceUnit("FF Musterstadt", 9, callSign: "HLF 1", officerCount: 1);

        vm.RefreshUnitsCommand.Execute(null);

        Assert.Equal("FF Musterstadt (HLF 1) 0/1/8/9", session.Incident.SituationReports[0].Units);
    }

    [Fact]
    public void Discarding_asks_first_takes_the_draft_out_and_closes_the_panel()
    {
        var session = NewSession();
        string? label = null;
        using var vm = NewVm(session, (_, l, onConfirmed) =>
        {
            label = l;
            onConfirmed();
        });
        vm.OpenCommand.Execute(null);

        vm.DiscardCommand.Execute(null);

        Assert.Equal("VERWERFEN", label);
        Assert.Empty(session.Incident.SituationReports);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void An_overlong_field_is_explained_instead_of_thrown()
    {
        var session = NewSession();
        using var vm = NewVm(session);
        vm.OpenCommand.Execute(null);
        vm.Situation = new string('x', SituationReportComposerViewModel.MaxFieldLength + 1);

        vm.CommitCommand.Execute(null);

        Assert.Equal($"Lage ist zu lang (höchstens {SituationReportComposerViewModel.MaxFieldLength} Zeichen)", vm.Error);
        Assert.Null(session.Incident.SituationReports[0].Situation);
    }

    [Fact]
    public void Units_longer_than_the_field_are_cut_at_the_last_whole_unit()
    {
        var forces = Enumerable.Range(1, 60).Select(i => ForceUnit.Create($"FF Musterstadt {i}", 9)).ToList();

        var text = SituationReportComposerViewModel.DescribeUnits(forces);

        Assert.NotNull(text);
        Assert.True(text.Length <= SituationReportComposerViewModel.MaxFieldLength);
        Assert.EndsWith("0/0/9/9 …", text, StringComparison.Ordinal);
    }
}
