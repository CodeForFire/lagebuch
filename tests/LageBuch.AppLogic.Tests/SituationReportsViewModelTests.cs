using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.AppLogic.Tests;

public class SituationReportsViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession Session, FixedClock Clock) NewSession()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Muster", "Florian Musterstadt 11/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (session, clock);
    }

    private static SituationReportsViewModel NewVm(
        LocalIncidentSession session, Action<string, string, Action>? requestConfirm = null) =>
        new(session, () => { }, "ILS", requestConfirm);

    [Fact]
    public void A_new_report_is_prefilled_from_the_funkrufname_the_address_and_the_kraefte()
    {
        var (session, _) = NewSession();
        session.SetAddress("Blumenstraße", "Nord", "13", "12345", "Musterstadt");
        session.AddForceUnit("FF Musterstadt", 9, callSign: "HLF 1", officerCount: 1);
        session.AddForceUnit("FF Beispieldorf", 6, officerCount: 1);
        using var vm = NewVm(session);

        vm.NewReportCommand.Execute(null);

        Assert.Equal("Entwurf", Assert.Single(vm.Reports).Title);
        Assert.Same(vm.Reports[0], vm.SelectedReport);
        Assert.Equal("Florian Musterstadt 11/1", vm.Reporter);
        Assert.Equal("Blumenstraße 13, Nord, 12345 Musterstadt", vm.Location);
        Assert.Equal("FF Musterstadt (HLF 1) 0/1/8/9, FF Beispieldorf 0/1/5/6", vm.Units);
        Assert.Null(vm.Situation);
        Assert.True(vm.IsEditable);
    }

    [Fact]
    public void Meldender_suggests_the_stammdaten_funkrufnamen()
    {
        var (session, _) = NewSession();
        string[] callSigns = ["Florian Musterstadt 11/1", "Florian Musterstadt 40/1"];

        using var vm = new SituationReportsViewModel(session, () => { }, "ILS", callSigns: callSigns);

        Assert.Equal(callSigns, vm.CallSignOptions);
    }

    [Fact]
    public void Einsatzstelle_suggests_the_einsatzdaten_address_first_then_the_stammdaten_strassen()
    {
        var (session, _) = NewSession();
        session.SetAddress("Blumenstraße", "Nord", "13", "12345", "Musterstadt");
        string[] streets = ["Am Markt, 12345 Musterstadt", "Blumenstraße 13, Nord, 12345 Musterstadt", "Lindenweg, Süd, 12345 Musterstadt"];

        using var vm = new SituationReportsViewModel(session, () => { }, "ILS", addresses: streets);

        Assert.Equal(
            ["Blumenstraße 13, Nord, 12345 Musterstadt", "Am Markt, 12345 Musterstadt", "Lindenweg, Süd, 12345 Musterstadt"],
            vm.LocationOptions);
    }

    [Fact]
    public void Einsatzstelle_follows_a_change_of_the_einsatzdaten_address()
    {
        var (session, _) = NewSession();
        using var vm = new SituationReportsViewModel(session, () => { }, "ILS", addresses: ["Am Markt, 12345 Musterstadt"]);
        Assert.Equal(["Am Markt, 12345 Musterstadt"], vm.LocationOptions);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        session.SetAddress("Lindenweg", "Süd", "4", "12345", "Musterstadt");

        Assert.Contains(nameof(SituationReportsViewModel.LocationOptions), raised);
        Assert.Equal("Lindenweg 4, Süd, 12345 Musterstadt", vm.LocationOptions[0]);
    }

    [Fact]
    public void The_funkspruch_preview_follows_every_keystroke_without_writing_the_incident()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);

        vm.Situation = "Schuppen im Vollbrand";

        Assert.Equal("Schuppen im Vollbrand", vm.FunkspruchPreview);
        Assert.Null(session.Incident.SituationReports[0].Situation);
    }

    [Fact]
    public void Commit_writes_the_draft_once_and_a_second_commit_without_a_change_writes_nothing()
    {
        var (session, _) = NewSession();
        var changes = 0;
        using var vm = new SituationReportsViewModel(session, () => changes++, "ILS");
        vm.NewReportCommand.Execute(null);
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
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);

        vm.Measures = "C-Rohr vor";
        session.AddJournalEntry(EtbDirection.Internal, "Notiz von einem anderen Tab");

        Assert.Equal("C-Rohr vor", vm.Measures);
    }

    // A ListBox bound to SelectedReport drops its selection when its items are cleared, so the
    // view model sees null mid-refresh. That must not reload the editor over the typing.
    [Fact]
    public void A_refresh_keeps_the_typing_even_while_the_list_drops_its_selection()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);
        vm.Reports.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                vm.SelectedReport = null;
            }
        };

        vm.Measures = "C-Rohr vor";
        session.AddJournalEntry(EtbDirection.Internal, "Notiz von einem anderen Gerät");

        Assert.Equal("C-Rohr vor", vm.Measures);
        Assert.NotNull(vm.SelectedReport);
    }

    [Fact]
    public void Send_asks_first_then_numbers_the_report_and_writes_the_funkspruch_to_the_etb()
    {
        var (session, _) = NewSession();
        string? asked = null;
        string? confirmLabel = null;
        Action? confirm = null;
        using var vm = NewVm(session, (message, label, onConfirmed) =>
        {
            asked = message;
            confirmLabel = label;
            confirm = onConfirmed;
        });
        vm.NewReportCommand.Execute(null);
        vm.Location = "Blumenstraße 13";
        vm.Situation = "Schuppen im Vollbrand";

        vm.SendCommand.Execute(null);

        Assert.NotNull(asked);
        Assert.Equal("SENDEN", confirmLabel);
        Assert.False(session.Incident.SituationReports[0].IsSent);

        Assert.NotNull(confirm);
        confirm();

        var row = Assert.Single(vm.Reports);
        Assert.Equal("Nr. 1", row.Title);
        Assert.False(vm.IsEditable);
        var entry = session.Incident.Journal[^1];
        Assert.Equal(EtbDirection.Outgoing, entry.Direction);
        Assert.Equal("ILS", entry.To);
        Assert.Equal("Lagemeldung Nr. 1: Blumenstraße 13 · Schuppen im Vollbrand", entry.Text);
    }

    [Fact]
    public void An_empty_draft_cannot_be_sent()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);

        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void RefreshUnits_takes_over_the_kraefte_as_they_stand_now()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);
        Assert.Null(vm.Units);
        session.AddForceUnit("FF Musterstadt", 9, callSign: "HLF 1", officerCount: 1);

        vm.RefreshUnitsCommand.Execute(null);

        Assert.Equal("FF Musterstadt (HLF 1) 0/1/8/9", vm.Units);
        Assert.Equal("FF Musterstadt (HLF 1) 0/1/8/9", session.Incident.SituationReports[0].Units);
    }

    [Fact]
    public void Discarding_a_draft_asks_first_and_takes_it_out()
    {
        var (session, _) = NewSession();
        string? label = null;
        using var vm = NewVm(session, (_, l, onConfirmed) =>
        {
            label = l;
            onConfirmed();
        });
        vm.NewReportCommand.Execute(null);

        vm.DeleteDraftCommand.Execute(null);

        Assert.Equal("VERWERFEN", label);
        Assert.Empty(vm.Reports);
        Assert.Null(vm.SelectedReport);
        Assert.Empty(session.Incident.SituationReports);
    }

    [Fact]
    public void Switching_to_another_report_saves_the_one_left()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);
        var first = vm.SelectedReport;
        vm.NewReportCommand.Execute(null);
        vm.Situation = "Lage im zweiten Entwurf";

        vm.SelectedReport = first;

        Assert.Equal("Lage im zweiten Entwurf", session.Incident.SituationReports[1].Situation);
        Assert.Null(vm.Situation);
    }

    [Fact]
    public void Opening_the_tab_shows_the_newest_draft()
    {
        var (session, _) = NewSession();
        session.AddSituationReport(null, null, "älter", null, null, null);
        session.AddSituationReport(null, null, "neuer", null, null, null);

        using var vm = NewVm(session);

        Assert.Equal("neuer", vm.Situation);
    }

    [Fact]
    public void An_overlong_field_is_explained_instead_of_thrown()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);
        vm.Situation = new string('x', SituationReportsViewModel.MaxFieldLength + 1);

        vm.CommitCommand.Execute(null);

        Assert.Equal($"Lage ist zu lang (höchstens {SituationReportsViewModel.MaxFieldLength} Zeichen)", vm.Error);
        Assert.Null(session.Incident.SituationReports[0].Situation);
    }

    [Fact]
    public void A_field_handed_to_the_helper_is_spelled_and_opens_it_on_a_phone()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.IsNarrow = true;
        Assert.False(vm.ShowHelper);

        vm.SpellFieldCommand.Execute("Blumenstraße");

        Assert.True(vm.ShowHelper);
        Assert.StartsWith("Berta – Ludwig", vm.Helper.Spelled, StringComparison.Ordinal);
    }

    [Fact]
    public void Units_longer_than_the_field_are_cut_at_the_last_whole_unit()
    {
        var forces = Enumerable.Range(1, 60).Select(i => ForceUnit.Create($"FF Musterstadt {i}", 9)).ToList();

        var text = SituationReportsViewModel.DescribeUnits(forces);

        Assert.NotNull(text);
        Assert.True(text.Length <= SituationReportsViewModel.MaxFieldLength);
        Assert.EndsWith("0/0/9/9 …", text, StringComparison.Ordinal);
    }
}
