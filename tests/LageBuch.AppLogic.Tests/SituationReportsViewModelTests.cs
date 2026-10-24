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

    // A draft with a Lage, saved, then opened read-only the way a closed Einsatz is reopened.
    private static LocalIncidentSession ReadOnlySessionWithADraft()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Muster", "Florian Musterstadt 11/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.AddSituationReport("ELW 1", null, "Schuppen im Vollbrand", null, null, null);
        seed.Close();
        return LocalIncidentSession.OpenReadOnly(store, clock, "/x.lagebuch");
    }

    [Fact]
    public void Without_stammdaten_there_is_nothing_to_suggest()
    {
        var (session, _) = NewSession();

        using var vm = NewVm(session);

        Assert.Empty(vm.CallSignOptions);
        Assert.Empty(vm.LocationOptions);
    }

    [Fact]
    public void Opening_the_tab_with_only_sent_reports_shows_the_last_one_sent()
    {
        var (session, _) = NewSession();
        session.AddSituationReport(null, null, "erste", null, null, null);
        session.SendSituationReport(session.Incident.SituationReports[0].Id, "ILS");
        session.AddSituationReport(null, null, "zweite", null, null, null);
        session.SendSituationReport(session.Incident.SituationReports[1].Id, "ILS");

        using var vm = NewVm(session);

        Assert.Equal("zweite", vm.Situation);
        Assert.Equal("LAGEMELDUNG NR. 2", vm.EditorHeader);
    }

    [Fact]
    public void Opening_the_tab_prefers_an_older_draft_over_a_newer_sent_report()
    {
        var (session, _) = NewSession();
        session.AddSituationReport(null, null, "Entwurf", null, null, null);
        session.AddSituationReport(null, null, "gesendet", null, null, null);
        session.SendSituationReport(session.Incident.SituationReports[1].Id, "ILS");

        using var vm = NewVm(session);

        Assert.Equal("Entwurf", vm.Situation);
        Assert.Equal("LAGEMELDUNG · ENTWURF", vm.EditorHeader);
    }

    [Fact]
    public void Opening_the_tab_on_no_report_selects_nothing()
    {
        var (session, _) = NewSession();

        using var vm = NewVm(session);

        Assert.Null(vm.SelectedReport);
        Assert.False(vm.HasSelection);
        Assert.False(vm.HasReports);
        Assert.Equal(string.Empty, vm.EditorHeader);
    }

    [Fact]
    public void The_list_shows_the_newest_report_first_and_announces_that_it_has_reports()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.NewReportCommand.Execute(null);
        vm.Situation = "älter";
        vm.NewReportCommand.Execute(null);
        vm.Situation = "neuer";
        vm.CommitCommand.Execute(null);

        Assert.True(vm.HasReports);
        Assert.Contains(nameof(SituationReportsViewModel.HasReports), changed);
        Assert.Equal(["neuer", "älter"], vm.Reports.Select(r => r.Summary));
    }

    [Fact]
    public void A_new_report_saves_the_draft_being_typed_first()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);
        vm.Situation = "noch nicht gespeichert";

        vm.NewReportCommand.Execute(null);

        Assert.Equal("noch nicht gespeichert", session.Incident.SituationReports[0].Situation);
    }

    [Fact]
    public void A_refresh_from_elsewhere_does_not_save_the_typing_in_progress()
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

        // Saved when the field is left, not because someone else wrote to the Einsatz.
        Assert.Null(session.Incident.SituationReports[0].Measures);
    }

    [Fact]
    public void A_field_changed_elsewhere_shows_the_new_value_and_keeps_the_typing_in_the_others()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);
        var id = session.Incident.SituationReports[0].Id;
        vm.Measures = "C-Rohr vor";

        session.UpdateSituationReport(id, "Florian Musterstadt 11/1", null, "Feuer aus", null, null, null);

        Assert.Equal("Feuer aus", vm.Situation);
        Assert.Equal("C-Rohr vor", vm.Measures);
    }

    [Fact]
    public void A_report_sent_elsewhere_locks_the_editor_and_announces_it()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);
        vm.Situation = "Schuppen im Vollbrand";
        vm.CommitCommand.Execute(null);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var send = 0;
        var delete = 0;
        var refresh = 0;
        vm.SendCommand.CanExecuteChanged += (_, _) => send++;
        vm.DeleteDraftCommand.CanExecuteChanged += (_, _) => delete++;
        vm.RefreshUnitsCommand.CanExecuteChanged += (_, _) => refresh++;

        session.SendSituationReport(session.Incident.SituationReports[0].Id, "ILS");

        Assert.False(vm.IsEditable);
        Assert.Equal("LAGEMELDUNG NR. 1", vm.EditorHeader);
        Assert.Equal("Gesendet 14:00 von Muster (Florian Musterstadt 11/1)", vm.SentInfo);
        Assert.Contains(nameof(SituationReportsViewModel.SentInfo), changed);
        Assert.Contains(nameof(SituationReportsViewModel.IsEditable), changed);
        Assert.Contains(nameof(SituationReportsViewModel.EditorHeader), changed);
        Assert.True(send > 0);
        Assert.True(delete > 0);
        Assert.True(refresh > 0);
        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Choosing_a_row_whose_report_is_gone_empties_the_editor_and_saves_nothing()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        vm.NewReportCommand.Execute(null);
        vm.Situation = "Schuppen im Vollbrand";
        vm.CommitCommand.Execute(null);
        var stale = vm.SelectedReport!;
        session.RemoveSituationReport(stale.Id);

        vm.SelectedReport = stale;
        vm.CommitCommand.Execute(null);

        Assert.Null(vm.Situation);
        Assert.Empty(session.Incident.SituationReports);
    }

    [Fact]
    public void Send_without_a_change_since_the_last_save_still_asks_to_send()
    {
        var (session, _) = NewSession();
        var asked = 0;
        using var vm = NewVm(session, (_, _, _) => asked++);
        vm.NewReportCommand.Execute(null);
        vm.Situation = "Schuppen im Vollbrand";
        vm.CommitCommand.Execute(null);

        vm.SendCommand.Execute(null);

        Assert.Equal(1, asked);
    }

    [Fact]
    public void Send_with_an_overlong_field_explains_it_and_does_not_ask()
    {
        var (session, _) = NewSession();
        var asked = 0;
        using var vm = NewVm(session, (_, _, _) => asked++);
        vm.NewReportCommand.Execute(null);
        vm.Situation = "Schuppen im Vollbrand";
        vm.Measures = new string('x', SituationReportsViewModel.MaxFieldLength + 1);

        vm.SendCommand.Execute(null);

        Assert.Equal(0, asked);
        Assert.Equal($"Maßnahmen ist zu lang (höchstens {SituationReportsViewModel.MaxFieldLength} Zeichen)", vm.Error);
    }

    [Fact]
    public void A_field_exactly_at_its_cap_is_fine()
    {
        Assert.Null(SituationReportsViewModel.Validate(
            new string('x', SituationReportsViewModel.MaxReporterLength),
            new string('x', SituationReportsViewModel.MaxLocationLength),
            new string('x', SituationReportsViewModel.MaxFieldLength),
            null,
            null,
            null));
    }

    [Fact]
    public void A_read_only_incident_offers_no_new_report_and_never_writes()
    {
        var session = ReadOnlySessionWithADraft();
        using var vm = NewVm(session);
        vm.Situation = "geändert";

        vm.CommitCommand.Execute(null);
        vm.OpenNewestDraft();

        Assert.False(vm.NewReportCommand.CanExecute(null));
        Assert.False(vm.IsEditable);
        Assert.Equal("Schuppen im Vollbrand", session.Incident.SituationReports[0].Situation);
        Assert.Single(session.Incident.SituationReports);
    }

    [Fact]
    public void The_helper_opens_and_closes_on_a_phone_and_is_always_there_when_wide()
    {
        var (session, _) = NewSession();
        using var vm = NewVm(session);
        Assert.True(vm.ShowHelper);
        Assert.False(vm.ShowHelperButton);

        vm.IsNarrow = true;
        Assert.False(vm.ShowHelper);
        Assert.True(vm.ShowHelperButton);

        vm.OpenHelperCommand.Execute(null);
        Assert.True(vm.ShowHelper);
        Assert.False(vm.ShowHelperButton);

        vm.CloseHelperCommand.Execute(null);
        Assert.False(vm.ShowHelper);
        Assert.True(vm.ShowHelperButton);
    }

    [Fact]
    public void No_kraefte_means_no_eingesetzte_einheiten()
    {
        Assert.Null(SituationReportsViewModel.DescribeUnits([]));
    }

    [Fact]
    public void Units_exactly_as_long_as_the_field_are_not_cut()
    {
        // 290 + " 0/0/9/9" + ", " + 292 + " 0/0/9/9" = 600 characters.
        ForceUnit[] forces = [ForceUnit.Create(new string('A', 290), 9), ForceUnit.Create(new string('B', 292), 9)];

        var text = SituationReportsViewModel.DescribeUnits(forces);

        Assert.Equal(SituationReportsViewModel.MaxFieldLength, text?.Length);
        Assert.EndsWith("B 0/0/9/9", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cut_never_leaves_the_text_longer_than_the_field()
    {
        // The second separator sits at 599, just past where a cut may still start.
        ForceUnit[] forces =
        [
            ForceUnit.Create(new string('A', 291), 9),
            ForceUnit.Create(new string('B', 290), 9),
            ForceUnit.Create("C", 9),
        ];

        var text = SituationReportsViewModel.DescribeUnits(forces);

        Assert.NotNull(text);
        Assert.True(text.Length <= SituationReportsViewModel.MaxFieldLength, $"{text.Length} characters");
        Assert.Equal(new string('A', 291) + " 0/0/9/9 …", text);
    }

    [Fact]
    public void A_single_unit_longer_than_the_field_is_cut_inside_its_name()
    {
        var text = SituationReportsViewModel.DescribeUnits([ForceUnit.Create(new string('A', 700), 9)]);

        Assert.Equal(new string('A', SituationReportsViewModel.MaxFieldLength - 2) + " …", text);
    }

    [Fact]
    public void A_row_shows_when_it_was_sent_or_else_when_it_was_started()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "ELW 1");
        var incident = Incident.Start(clock, op, "Brand");
        var draft = incident.AddSituationReport(clock, op, "ELW 1", null, null, null, null, null);
        var report = incident.AddSituationReport(clock, op, "ELW 1", null, "Feuer aus", null, null, null);
        clock.Now = T0.AddMinutes(12);
        var sent = incident.SendSituationReport(clock, op, report.Id, "ILS");

        var draftRow = new SituationReportRow(draft);
        var sentRow = new SituationReportRow(sent);

        Assert.Equal("14:00", draftRow.TimeText);
        Assert.Equal("— noch leer —", draftRow.Summary);
        Assert.Equal("14:12", sentRow.TimeText);
        Assert.Equal("Feuer aus", sentRow.Summary);
    }
}
