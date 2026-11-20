using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// The Strahlenschutzüberwachung of a Strahlenschutztrupp in the Atemschutz tab (#994): the row,
/// the dialog, and the close refusal. The ETB wording is asserted literally: it is persisted and
/// read back off the Einsatzbericht, so it is a data format.
/// </summary>
public class ScbaRadiationTests
{
    private const string Strahlenschutztrupp = "Strahlenschutztrupp";

    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        TruppTypes = new[] { new TruppType("Angriffstrupp"), new TruppType(Strahlenschutztrupp, 3, 30, RadiationMonitoring: true) },
    };

    private static LocalIncidentSession NewSession(FixedClock clock) =>
        TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static ScbaViewModel Vm(FixedClock clock, LocalIncidentSession session, List<RadiationDialogViewModel>? opened = null) =>
        new(session, Md(), clock, new FakeTicker(), new FakeAlarmService(), () => { }, d => opened?.Add(d));

    private static ScbaTruppRow Register(ScbaViewModel vm, string designation = Strahlenschutztrupp)
    {
        vm.NewDesignation = designation;
        vm.NewCallSign = "HLF 1";
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        vm.NewZweiterTruppmann = "Huber";
        vm.AddTruppCommand.Execute(null);
        return vm.Trupps[^1];
    }

    private static RadiationDialogViewModel Open(ScbaTruppRow row, List<RadiationDialogViewModel> opened)
    {
        row.OpenRadiationCommand.Execute(null);
        return opened[^1];
    }

    private static void Number(RadiationDialogViewModel dialog)
    {
        foreach (var (m, i) in dialog.Members.Select((m, i) => (m, i)))
        {
            m.OslNumber = $"OSL {i + 1}";
            m.AlarmNumber = $"EPD {i + 1}";
        }
    }

    [Fact]
    public void A_strahlenschutztrupp_is_registered_under_the_ueberwachung_an_ordinary_one_is_not()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        using var vm = Vm(clock, session);

        var row = Register(vm);
        var ordinary = Register(vm, "Angriffstrupp");

        Assert.True(session.Incident.ScbaTrupps[0].RadiationMonitoring);
        Assert.True(row.IsRadiationMonitored);
        Assert.False(ordinary.IsRadiationMonitored);
        Assert.Null(ordinary.RadiationHint);
    }

    [Fact]
    public void Im_einsatz_waits_for_the_dosimeter_numbers()
    {
        var clock = new FixedClock(T0);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, NewSession(clock), opened);
        var row = Register(vm);

        Assert.Equal("Dosimeter-Nr. fehlen", row.RadiationHint);
        Assert.False(row.StartCommand.CanExecute(null));

        var dialog = Open(row, opened);
        Number(dialog);
        dialog.SaveCommand.Execute(null);

        Assert.True(row.StartCommand.CanExecute(null));
        Assert.Null(row.RadiationHint);
    }

    [Fact]
    public void Saving_numbers_writes_one_etb_line_per_crew_member()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, session, opened);
        var dialog = Open(Register(vm), opened);
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        dialog.Members[0].OslNumber = "4711";
        dialog.Members[0].AlarmNumber = "0815";
        dialog.Members[0].MaskNumber = "12";
        dialog.Members[0].PaNumber = "3";
        dialog.SaveCommand.Execute(null);

        Assert.True(closed);
        var entry = Assert.Single(session.Incident.Journal, e => e.Text.Contains("Strahlenschutz Müller", StringComparison.Ordinal));
        Assert.Equal(
            "HLF 1 · Strahlenschutztrupp: Strahlenschutz Müller – OSL-Dosimeter 4711, Alarmdosimeter 0815, Maske 12, PA 3",
            entry.Text);
        Assert.Equal("HLF 1", entry.From);
        Assert.DoesNotContain(session.Incident.Journal, e => e.Text.Contains("Strahlenschutz Schmidt", StringComparison.Ordinal));
    }

    // Declaring a dosimeter missing breaks the documentation chain, so it asks first.
    [Fact]
    public void Nicht_vorhanden_asks_first_and_cancelling_leaves_the_dosimeter_alone()
    {
        var clock = new FixedClock(T0);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, NewSession(clock), opened);
        var dialog = Open(Register(vm), opened);
        var member = dialog.Members[1];

        member.DeclareAlarmMissingCommand.Execute(null);

        Assert.True(dialog.IsMissingPending);
        Assert.Contains("Alarmdosimeter von Schmidt nicht vorhanden?", dialog.MissingWarning, StringComparison.Ordinal);
        Assert.Contains("Dokumentationskette unterbrochen", dialog.MissingWarning, StringComparison.Ordinal);
        Assert.False(member.AlarmMissing);

        dialog.CancelMissingCommand.Execute(null);

        Assert.False(dialog.IsMissingPending);
        Assert.False(member.AlarmMissing);
    }

    [Fact]
    public void A_confirmed_missing_dosimeter_is_recorded_with_an_achtung_line()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, session, opened);
        var row = Register(vm);
        var dialog = Open(row, opened);
        Number(dialog);
        dialog.Members[1].DoseText = "0,5";

        dialog.Members[1].DeclareAlarmMissingCommand.Execute(null);
        dialog.ConfirmMissingCommand.Execute(null);
        dialog.SaveCommand.Execute(null);

        var record = session.Incident.ScbaTrupps[0].Members[1].Radiation;
        Assert.True(record?.AlarmDosimeterMissing);
        Assert.Null(record?.AlarmReading); // a missing Alarmdosimeter has nothing to read off
        Assert.Contains(
            session.Incident.Journal,
            e => e.Text == "ACHTUNG HLF 1 · Strahlenschutztrupp: Alarmdosimeter von Schmidt nicht vorhanden – Dokumentationskette unterbrochen");
    }

    [Theory]
    [InlineData("0,12", DoseUnit.MilliSievert, "0,12 mSv")]
    [InlineData("0.12", DoseUnit.MilliSievert, "0,12 mSv")]
    [InlineData("35", DoseUnit.MicroSievert, "35 µSv")]
    public void A_reading_is_kept_with_its_unit_and_logged(string typed, DoseUnit unit, string expected)
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, session, opened);
        var dialog = Open(Register(vm), opened);
        Number(dialog);

        dialog.Members[0].DoseText = typed;
        dialog.Members[0].DoseUnit = unit;
        dialog.SaveCommand.Execute(null);

        Assert.Equal(expected, session.Incident.ScbaTrupps[0].Members[0].Radiation?.AlarmReading?.Display);
        Assert.Contains(
            session.Incident.Journal,
            e => e.Text == $"HLF 1 · Strahlenschutztrupp: Ablesewert Alarmdosimeter Müller: {expected}");
    }

    [Fact]
    public void The_unit_defaults_to_msv()
    {
        var clock = new FixedClock(T0);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, NewSession(clock), opened);
        var dialog = Open(Register(vm), opened);

        Assert.All(dialog.Members, m => Assert.Equal(DoseUnit.MilliSievert, m.DoseUnit));
        Assert.Equal("mSv", dialog.Members[0].DoseUnitLabel);

        dialog.Members[0].DoseUnitLabel = "µSv";
        Assert.Equal(DoseUnit.MicroSievert, dialog.Members[0].DoseUnit);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.000,5")]
    [InlineData("999999")]
    public void A_reading_that_is_no_number_stays_in_the_field_with_a_message(string typed)
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, session, opened);
        var dialog = Open(Register(vm), opened);
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        dialog.Members[0].DoseText = typed;
        dialog.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.Equal("Ablesewert als Zahl eingeben, z. B. 0,12", dialog.Members[0].DoseError);
        Assert.Equal(typed, dialog.Members[0].DoseText);
        Assert.Equal(RadiationRecord.Empty, session.Incident.ScbaTrupps[0].Members[0].Radiation);
    }

    [Fact]
    public void An_abgenommen_trupp_without_its_readings_says_so()
    {
        var clock = new FixedClock(T0);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, NewSession(clock), opened);
        var row = Register(vm);
        var dialog = Open(row, opened);
        Number(dialog);
        dialog.SaveCommand.Execute(null);

        row.StartCommand.Execute(null);
        row.WithdrawCommand.Execute(null);
        row.MarkRemovedCommand.Execute(null);

        Assert.True(row.IsReturned);
        Assert.Equal("Ablesewert offen", row.RadiationHint);
    }

    [Fact]
    public void An_unchanged_dialog_writes_nothing()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, session, opened);
        var dialog = Open(Register(vm), opened);
        var before = session.Incident.Journal.Count;

        dialog.SaveCommand.Execute(null);

        Assert.Equal(before, session.Incident.Journal.Count);
    }

    private static readonly RadiationRecord Numbered = new("4711", false, "0815", false, "12", "3");

    private static (LocalIncidentSession Session, Guid TruppId) SessionWithStrahlenschutztrupp()
    {
        var session = NewSession(new FixedClock(T0));
        session.AddScbaTrupp(Strahlenschutztrupp, TruppMember.Crew("Müller", "Schmidt"), 300, callSign: "HLF 1", radiationMonitoring: true);
        return (session, session.Incident.ScbaTrupps[0].Id);
    }

    [Theory]
    [InlineData("4799", "0815", "12", "3")]
    [InlineData("4711", "0899", "12", "3")]
    [InlineData("4711", "0815", "99", "3")]
    [InlineData("4711", "0815", "12", "99")]
    public void Any_changed_number_writes_the_numbers_line(string osl, string alarm, string mask, string pa)
    {
        var after = new RadiationRecord(osl, false, alarm, false, mask, pa);

        var line = Assert.Single(RadiationDialogViewModel.JournalLines("T", "Müller", Numbered, after));

        Assert.Equal($"T: Strahlenschutz Müller – {after.NumbersDisplay}", line);
    }

    [Fact]
    public void Unchanged_numbers_and_an_unchanged_reading_write_nothing()
    {
        var withReading = Numbered.WithAlarmReading(new DoseReading(1m, DoseUnit.MilliSievert));

        Assert.Empty(RadiationDialogViewModel.JournalLines("T", "Müller", withReading, withReading));
    }

    [Fact]
    public void A_missing_osl_dosimeter_writes_its_achtung_line_once()
    {
        var missing = new RadiationRecord("4711", true, "0815", false, "12", "3");

        Assert.Equal(
            new[] { "ACHTUNG T: OSL-Dosimeter von Müller nicht vorhanden – Dokumentationskette unterbrochen" },
            RadiationDialogViewModel.JournalLines("T", "Müller", Numbered, missing));
        Assert.Empty(RadiationDialogViewModel.JournalLines("T", "Müller", missing, missing));
        Assert.Empty(RadiationDialogViewModel.JournalLines("T", "Müller", missing, Numbered));
    }

    [Fact]
    public void A_missing_alarmdosimeter_already_declared_is_not_logged_again()
    {
        var missing = new RadiationRecord("4711", false, "0815", true, "12", "3");

        Assert.Empty(RadiationDialogViewModel.JournalLines("T", "Müller", missing, missing));
    }

    [Fact]
    public void A_confirmed_osl_declaration_lands_on_the_osl_dosimeter()
    {
        var (session, truppId) = SessionWithStrahlenschutztrupp();
        var dialog = new RadiationDialogViewModel(session, truppId);

        dialog.Members[0].DeclareOslMissingCommand.Execute(null);
        Assert.StartsWith("OSL-Dosimeter von Müller nicht vorhanden?", dialog.MissingWarning, StringComparison.Ordinal);
        dialog.ConfirmMissingCommand.Execute(null);

        Assert.True(dialog.Members[0].OslMissing);
        Assert.False(dialog.Members[0].OslPresent);
        Assert.True(dialog.Members[0].AlarmPresent);
        Assert.False(dialog.IsMissingPending);
    }

    [Fact]
    public void A_saved_change_is_reported_and_an_unchanged_reopen_is_not()
    {
        var (session, truppId) = SessionWithStrahlenschutztrupp();
        var changes = 0;
        var dialog = new RadiationDialogViewModel(session, truppId, () => changes++);
        dialog.Members[0].OslNumber = "4711";
        dialog.Members[0].AlarmNumber = "0815";
        dialog.SaveCommand.Execute(null);
        Assert.Equal(1, changes);
        var journal = session.Incident.Journal.Count;

        var reopened = new RadiationDialogViewModel(session, truppId, () => changes++);
        var closed = false;
        reopened.Closed += (_, _) => closed = true;
        reopened.SaveCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal(1, changes);
        Assert.Equal(journal, session.Incident.Journal.Count);
    }

    [Fact]
    public void A_refused_save_keeps_the_dialog_open_with_the_reason()
    {
        var (session, truppId) = SessionWithStrahlenschutztrupp();
        var dialog = new RadiationDialogViewModel(session, truppId);
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        dialog.Members[0].OslNumber = new string('9', RadiationRecord.MaxNumberLength + 1);
        dialog.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.Equal("Nicht möglich: Eine Nummer darf höchstens 50 Zeichen lang sein.", dialog.SaveError);
    }

    [Fact]
    public void A_closed_einsatz_opens_the_dialog_to_read_but_not_to_save()
    {
        var (session, truppId) = SessionWithStrahlenschutztrupp();
        session.Close();

        var dialog = new RadiationDialogViewModel(session, truppId);

        Assert.False(dialog.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void A_row_shows_what_is_recorded()
    {
        var member = new TruppMember(
            TruppRole.Truppmann, "Schmidt", Numbered.WithAlarmReading(new DoseReading(0.125m, DoseUnit.MicroSievert)));

        var row = new RadiationMemberRow(member, (_, _) => { });

        Assert.Equal(("4711", "0815", "12", "3"), (row.OslNumber, row.AlarmNumber, row.MaskNumber, row.PaNumber));
        Assert.Equal("0,125", row.DoseText);
        Assert.Equal(DoseUnit.MicroSievert, row.DoseUnit);
        Assert.Equal("Truppmann", row.RoleDisplay);

        row.DoseUnitLabel = "mSv";
        Assert.Equal(DoseUnit.MilliSievert, row.DoseUnit);
        row.DoseUnitLabel = null;
        Assert.Equal(DoseUnit.MilliSievert, row.DoseUnit);
    }

    [Fact]
    public void Undo_and_un_declaring_restore_a_dosimeter_and_keep_the_reading()
    {
        var row = new RadiationMemberRow(new TruppMember(TruppRole.Truppfuehrer, "Müller", Numbered), (_, _) => { });
        row.DoseText = "0,5";

        row.SetMissing(DosimeterKind.Osl, true);
        row.SetMissing(DosimeterKind.Alarm, true);
        Assert.True(row.OslMissing);
        Assert.False(row.AlarmPresent);
        Assert.Null(row.DoseText);

        row.UndoOslMissingCommand.Execute(null);
        row.UndoAlarmMissingCommand.Execute(null);
        Assert.True(row.OslPresent);
        Assert.True(row.AlarmPresent);

        row.DoseText = "0,5";
        row.SetMissing(DosimeterKind.Alarm, false);
        Assert.Equal("0,5", row.DoseText);
    }

    [Theory]
    [InlineData("100000", true)]
    [InlineData("100000.001", false)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    public void The_reading_limit_itself_is_a_number(string? text, bool expected)
    {
        Assert.Equal(expected, RadiationMemberRow.TryParseDose(text, out _));
    }

    [Fact]
    public void The_row_hint_updates_when_the_numbers_come_in()
    {
        var clock = new FixedClock(T0);
        var opened = new List<RadiationDialogViewModel>();
        using var vm = Vm(clock, NewSession(clock), opened);
        var row = Register(vm);
        Assert.True(row.HasRadiationHint);
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var dialog = Open(row, opened);
        Number(dialog);
        dialog.SaveCommand.Execute(null);

        Assert.Contains(nameof(ScbaTruppRow.RadiationHint), raised);
        Assert.Contains(nameof(ScbaTruppRow.HasRadiationHint), raised);
        Assert.False(row.HasRadiationHint);
    }
}
