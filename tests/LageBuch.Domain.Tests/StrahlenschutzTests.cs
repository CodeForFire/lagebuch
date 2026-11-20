using LageBuch.Domain.Atemschutz;

namespace LageBuch.Domain.Tests;

/// <summary>Strahlenschutzüberwachung of a Strahlenschutztrupp (#994).</summary>
public class StrahlenschutzTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static readonly RadiationRecord Numbered = new(oslDosimeterNumber: "OSL 1", alarmDosimeterNumber: "EPD 7");

    private static Incident NewIncident(out FixedClock clock)
    {
        clock = new FixedClock(T0);
        return Incident.Start(clock, new SessionOperator("Müller", "FFB 12/1"));
    }

    private static AtemschutzTrupp AddStrahlenschutztrupp(Incident incident, FixedClock clock) =>
        incident.AddScbaTrupp(
            clock,
            "Strahlenschutztrupp",
            TruppMember.Crew("Müller", "Schmidt", "Huber"),
            entryPressure: 300,
            callSign: "HLF 1",
            radiationMonitoring: true);

    private static void NumberEveryone(Incident incident, AtemschutzTrupp trupp)
    {
        foreach (var m in trupp.Members.ToList())
        {
            incident.RecordScbaRadiation(trupp.Id, m.Role, Numbered);
        }
    }

    [Fact]
    public void Every_crew_member_of_a_strahlenschutztrupp_starts_with_an_empty_record()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddStrahlenschutztrupp(incident, clock);

        Assert.True(trupp.RadiationMonitoring);
        Assert.All(trupp.Members, m => Assert.Equal(RadiationRecord.Empty, m.Radiation));
    }

    [Fact]
    public void An_ordinary_trupp_carries_no_record_even_if_handed_one()
    {
        var incident = NewIncident(out var clock);
        var crew = TruppMember.Crew("Müller", "Schmidt").Select(m => m with { Radiation = Numbered });
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", crew, entryPressure: 300);

        Assert.False(trupp.RadiationMonitoring);
        Assert.All(trupp.Members, m => Assert.Null(m.Radiation));
        Assert.Throws<InvalidOperationException>(
            () => incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppfuehrer, Numbered));
    }

    [Fact]
    public void A_strahlenschutztrupp_without_dosimeter_numbers_cannot_go_under_atemschutz()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddStrahlenschutztrupp(incident, clock);
        incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppfuehrer, Numbered);
        incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppmann, Numbered);

        Assert.True(trupp.LacksDosimeterNumbers);
        var ex = Assert.Throws<InvalidOperationException>(() => incident.StartScbaTrupp(clock, trupp.Id));
        Assert.Contains("Dosimeter-Nummern fehlen", ex.Message, StringComparison.Ordinal);
        Assert.False(trupp.HasStarted);
    }

    // Maske and PA are optional; a dosimeter declared missing counts as accounted for.
    [Fact]
    public void Numbers_or_a_declared_missing_dosimeter_let_the_trupp_start()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddStrahlenschutztrupp(incident, clock);
        incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppfuehrer, Numbered);
        incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppmann, Numbered);
        incident.RecordScbaRadiation(
            trupp.Id, TruppRole.ZweiterTruppmann, new RadiationRecord(oslDosimeterMissing: true, alarmDosimeterNumber: "EPD 9"));

        incident.StartScbaTrupp(clock, trupp.Id);

        Assert.True(trupp.HasStarted);
    }

    [Fact]
    public void An_osl_number_alone_is_not_enough()
    {
        Assert.False(new RadiationRecord(oslDosimeterNumber: "OSL 1").HasDosimeterNumbers);
        Assert.False(new RadiationRecord(alarmDosimeterNumber: "EPD 1").HasDosimeterNumbers);
        Assert.True(new RadiationRecord(oslDosimeterMissing: true, alarmDosimeterMissing: true).HasDosimeterNumbers);
    }

    [Fact]
    public void A_started_strahlenschutztrupp_awaits_a_reading_until_every_alarmdosimeter_is_answered()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddStrahlenschutztrupp(incident, clock);
        NumberEveryone(incident, trupp);
        Assert.False(trupp.AwaitsDoseReading); // not under Atemschutz yet: no dose to report

        incident.StartScbaTrupp(clock, trupp.Id);
        Assert.True(trupp.AwaitsDoseReading);

        incident.RecordScbaRadiation(
            trupp.Id, TruppRole.Truppfuehrer, Numbered.WithAlarmReading(new DoseReading(0.12m, DoseUnit.MilliSievert)));
        incident.RecordScbaRadiation(
            trupp.Id, TruppRole.Truppmann, Numbered.WithAlarmReading(new DoseReading(35m, DoseUnit.MicroSievert)));
        Assert.True(trupp.AwaitsDoseReading);

        incident.RecordScbaRadiation(
            trupp.Id, TruppRole.ZweiterTruppmann, new RadiationRecord("OSL 3", false, "EPD 3", alarmDosimeterMissing: true));
        Assert.False(trupp.AwaitsDoseReading);
    }

    // The Dekon-Platz reports late, so the reading must still be recordable after Abnehmen.
    [Fact]
    public void A_reading_can_be_recorded_after_abnehmen()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddStrahlenschutztrupp(incident, clock);
        NumberEveryone(incident, trupp);
        incident.StartScbaTrupp(clock, trupp.Id);
        incident.WithdrawScbaTrupp(clock, trupp.Id);
        incident.MarkScbaRemoved(clock, trupp.Id);

        incident.RecordScbaRadiation(
            trupp.Id, TruppRole.Truppfuehrer, Numbered.WithAlarmReading(new DoseReading(0.5m, DoseUnit.MilliSievert)));

        Assert.Equal(new DoseReading(0.5m, DoseUnit.MilliSievert), trupp.Members[0].Radiation?.AlarmReading);
    }

    [Fact]
    public void The_einsatz_cannot_be_closed_while_a_reading_is_missing()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddStrahlenschutztrupp(incident, clock);
        NumberEveryone(incident, trupp);
        incident.StartScbaTrupp(clock, trupp.Id);

        var ex = Assert.Throws<InvalidOperationException>(
            () => incident.Close(clock, new SessionOperator("Müller", "FFB 12/1")));
        Assert.Equal("Ablesewerte fehlen: HLF 1 · Strahlenschutztrupp.", ex.Message);
        Assert.Equal(IncidentState.Open, incident.State);
        Assert.Equal(new[] { trupp }, incident.ScbaTruppsAwaitingDoseReading);

        foreach (var m in trupp.Members.ToList())
        {
            incident.RecordScbaRadiation(
                trupp.Id, m.Role, Numbered.WithAlarmReading(new DoseReading(0m, DoseUnit.MicroSievert)));
        }

        incident.Close(clock, new SessionOperator("Müller", "FFB 12/1"));
        Assert.Equal(IncidentState.Closed, incident.State);
    }

    [Fact]
    public void A_missing_alarmdosimeter_has_no_reading()
    {
        Assert.Throws<ArgumentException>(
            () => new RadiationRecord(alarmDosimeterMissing: true, alarmReading: new DoseReading(1m, DoseUnit.MilliSievert)));
    }

    [Fact]
    public void Numbers_are_trimmed_and_capped()
    {
        var record = new RadiationRecord(oslDosimeterNumber: "  OSL 1 ", maskNumber: "   ");

        Assert.Equal("OSL 1", record.OslDosimeterNumber);
        Assert.Null(record.MaskNumber);
        Assert.Throws<ArgumentException>(
            () => new RadiationRecord(paNumber: new string('9', RadiationRecord.MaxNumberLength + 1)));
    }

    [Theory]
    [InlineData(0.12, DoseUnit.MilliSievert, "0,12 mSv")]
    [InlineData(35, DoseUnit.MicroSievert, "35 µSv")]
    [InlineData(0.003, DoseUnit.MilliSievert, "0,003 mSv")]
    public void A_reading_reads_the_way_the_display_showed_it(double value, DoseUnit unit, string expected)
    {
        Assert.Equal(expected, new DoseReading((decimal)value, unit).Display);
    }

    [Fact]
    public void A_negative_or_absurd_reading_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DoseReading(-0.1m, DoseUnit.MilliSievert));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DoseReading(DoseReading.MaxValue + 1, DoseUnit.MilliSievert));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DoseReading(1m, (DoseUnit)7));
    }

    // Every Trupp still owing a reading is named, in Trupp order, so the refusal says where to look.
    [Fact]
    public void The_refusal_names_every_trupp_still_owing_a_reading_in_trupp_order()
    {
        var incident = NewIncident(out var clock);
        var second = incident.AddScbaTrupp(
            clock, "Strahlenschutztrupp", TruppMember.Crew("Maier", "Wagner"), entryPressure: 300, truppNumber: 2, callSign: "B", radiationMonitoring: true);
        var first = incident.AddScbaTrupp(
            clock, "Strahlenschutztrupp", TruppMember.Crew("Bauer", "Klein"), entryPressure: 300, truppNumber: 1, callSign: "A", radiationMonitoring: true);
        foreach (var trupp in new[] { second, first })
        {
            NumberEveryone(incident, trupp);
            incident.StartScbaTrupp(clock, trupp.Id);
        }

        Assert.Equal(new[] { first, second }, incident.ScbaTruppsAwaitingDoseReading);
        var ex = Assert.Throws<InvalidOperationException>(
            () => incident.Close(clock, new SessionOperator("Müller", "FFB 12/1")));
        Assert.Equal("Ablesewerte fehlen: A · Strahlenschutztrupp, B · Strahlenschutztrupp.", ex.Message);
    }

    [Fact]
    public void A_closed_einsatz_takes_no_more_records()
    {
        var incident = NewIncident(out var clock);
        var trupp = AddStrahlenschutztrupp(incident, clock);
        incident.Close(clock, new SessionOperator("Müller", "FFB 12/1"));

        Assert.Throws<IncidentClosedException>(
            () => incident.RecordScbaRadiation(trupp.Id, TruppRole.Truppfuehrer, Numbered));
        Assert.Equal(RadiationRecord.Empty, trupp.Members[0].Radiation);
    }

    // A record handed in at registration is kept, not overwritten by the empty one.
    [Fact]
    public void A_record_handed_in_at_registration_is_kept()
    {
        var incident = NewIncident(out var clock);
        var crew = TruppMember.Crew("Müller", "Schmidt").Select(m => m with { Radiation = Numbered });

        var trupp = incident.AddScbaTrupp(clock, "Strahlenschutztrupp", crew, entryPressure: 300, radiationMonitoring: true);

        Assert.All(trupp.Members, m => Assert.Equal(Numbered, m.Radiation));
    }

    [Fact]
    public void Refusals_say_why_in_german()
    {
        var incident = NewIncident(out var clock);
        var ordinary = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 300);
        var twoPerson = incident.AddScbaTrupp(
            clock, "Strahlenschutztrupp", TruppMember.Crew("Bauer", "Klein"), entryPressure: 300, radiationMonitoring: true);

        Assert.Equal(
            "Dieser Trupp steht nicht unter Strahlenschutzüberwachung.",
            Assert.Throws<InvalidOperationException>(() => ordinary.RecordRadiation(TruppRole.Truppfuehrer, Numbered)).Message);
        Assert.StartsWith(
            "Diese Truppfunktion ist nicht besetzt.",
            Assert.Throws<ArgumentException>(() => twoPerson.RecordRadiation(TruppRole.ZweiterTruppmann, Numbered)).Message,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "Der Ablesewert muss zwischen 0 und 100.000 liegen.",
            Assert.Throws<ArgumentOutOfRangeException>(() => new DoseReading(-1m, DoseUnit.MilliSievert)).Message,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "Unbekannte Einheit.",
            Assert.Throws<ArgumentOutOfRangeException>(() => new DoseReading(1m, (DoseUnit)7)).Message,
            StringComparison.Ordinal);
        var reading = new DoseReading(1m, DoseUnit.MilliSievert);
        var readingBesideMissing = Assert.Throws<ArgumentException>(
            () => new RadiationRecord(alarmDosimeterMissing: true, alarmReading: reading));
        Assert.StartsWith(
            "Ein Alarmdosimeter, das nicht vorhanden ist, hat keinen Ablesewert.", readingBesideMissing.Message, StringComparison.Ordinal);
        Assert.StartsWith(
            "Eine Nummer darf höchstens 50 Zeichen lang sein.",
            Assert.Throws<ArgumentException>(() => new RadiationRecord(maskNumber: new string('9', 51))).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_limits_themselves_are_accepted()
    {
        Assert.Equal(DoseReading.MaxValue, new DoseReading(DoseReading.MaxValue, DoseUnit.MicroSievert).Value);
        Assert.Equal(0m, new DoseReading(0m, DoseUnit.MicroSievert).Value);
        var longest = new string('9', RadiationRecord.MaxNumberLength);
        Assert.Equal(longest, new RadiationRecord(paNumber: longest).PaNumber);
    }

    [Theory]
    [InlineData(DoseUnit.MilliSievert, "mSv")]
    [InlineData(DoseUnit.MicroSievert, "µSv")]
    public void Unit_labels(DoseUnit unit, string label) => Assert.Equal(label, DoseReading.UnitLabel(unit));

    [Fact]
    public void The_numbers_read_with_dashes_and_missing_marks()
    {
        Assert.Equal(
            "OSL-Dosimeter 4711, Alarmdosimeter 0815, Maske 12, PA 3",
            new RadiationRecord("4711", false, "0815", false, "12", "3").NumbersDisplay);
        Assert.Equal(
            "OSL-Dosimeter — (nicht vorhanden), Alarmdosimeter 0815 (nicht vorhanden), Maske —, PA —",
            new RadiationRecord(oslDosimeterMissing: true, alarmDosimeterNumber: "0815", alarmDosimeterMissing: true).NumbersDisplay);
    }

    [Fact]
    public void The_reading_line_says_value_missing_or_open()
    {
        Assert.Equal("Ablesewert 0,12 mSv", Numbered.WithAlarmReading(new DoseReading(0.12m, DoseUnit.MilliSievert)).ReadingDisplay);
        Assert.Equal("Ablesewert: nicht vorhanden", new RadiationRecord(alarmDosimeterMissing: true).ReadingDisplay);
        Assert.Equal("Ablesewert offen", Numbered.ReadingDisplay);
    }

    [Fact]
    public void Either_missing_dosimeter_is_a_gap()
    {
        Assert.False(Numbered.HasGap);
        Assert.True(new RadiationRecord(oslDosimeterMissing: true).HasGap);
        Assert.True(new RadiationRecord(alarmDosimeterMissing: true).HasGap);
    }

    // FromUntrusted is how a handed-over file and a host's snapshot are read: it cuts and drops
    // where the checking constructor would throw.
    [Fact]
    public void An_untrusted_record_is_cut_and_cleaned_never_refused()
    {
        var longest = new string('9', RadiationRecord.MaxNumberLength);
        var record = RadiationRecord.FromUntrusted(longest + "99", false, longest, false, "12", "3", 0.5m, (int)DoseUnit.MicroSievert);

        Assert.Equal(new RadiationRecord(longest, false, longest, false, "12", "3", new DoseReading(0.5m, DoseUnit.MicroSievert)), record);
    }

    [Theory]
    [InlineData(-0.001, 0)]
    [InlineData(100000.001, 0)]
    [InlineData(1.0, 7)]
    public void An_untrusted_reading_out_of_range_or_unit_is_dropped(double value, int unit)
    {
        Assert.Null(RadiationRecord.FromUntrusted(null, false, null, false, null, null, (decimal)value, unit).AlarmReading);
    }

    [Fact]
    public void An_untrusted_reading_at_the_limits_or_beside_a_missing_dosimeter()
    {
        Assert.Equal(
            new DoseReading(0m, DoseUnit.MilliSievert),
            RadiationRecord.FromUntrusted(null, false, null, false, null, null, 0m, 0).AlarmReading);
        Assert.Equal(
            new DoseReading(DoseReading.MaxValue, DoseUnit.MilliSievert),
            RadiationRecord.FromUntrusted(null, false, null, false, null, null, DoseReading.MaxValue, 0).AlarmReading);
        Assert.Null(RadiationRecord.FromUntrusted(null, false, null, true, null, null, 1m, 0).AlarmReading);
        Assert.Null(RadiationRecord.FromUntrusted(null, false, null, false, null, null, null, 0).AlarmReading);
        Assert.Null(RadiationRecord.FromUntrusted(null, false, null, false, null, null, 1m, null).AlarmReading);
    }
}
