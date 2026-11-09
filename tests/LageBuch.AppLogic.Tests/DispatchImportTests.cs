using LageBuch.AppLogic.Services.Dispatch;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class DispatchImportTests
{
    private static readonly TimeZoneInfo Cet = TimeZoneInfo.CreateCustomTimeZone("Test/CET", TimeSpan.FromHours(1), "CET", "CET");
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 17, 42, 0, TimeSpan.FromHours(1));

    private static readonly Vehicle Hlf = new("Wache 2", "Florian Musterstadt 2/40/1", 9, VehicleType: "HLF 20");
    private static readonly Vehicle Dlk = new("Wache 1", "Florian Musterstadt 1/30/1", 3, VehicleType: "DLK 23/12");

    private static DispatchAlarm Alarm(string? xml = null)
    {
        Assert.True(FfAgentDispatchParser.TryParse(xml ?? DispatchSamples.Xml(), Cet, out var alarm));
        return alarm;
    }

    private static LocalIncidentSession Session() =>
        LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(Now), new SessionOperator("Müller"), "/x.lagebuch", []);

    [Theory]
    [InlineData("Florian Musterstadt 2/40/1")]
    [InlineData("2/40/1")]
    [InlineData("florian  musterstadt 2/40/1")]
    public void A_requested_funkrufname_finds_its_stammdaten_vehicle(string requested)
    {
        Assert.Same(Hlf, DispatchImport.Match(requested, [Hlf, Dlk]));
    }

    [Theory]
    [InlineData("Florian Musterstadt Wache 2")]
    [InlineData("Florian Musterstadt 1")]
    [InlineData("40/1")]
    [InlineData(" ")]
    public void A_name_that_is_no_vehicle_matches_none(string requested)
    {
        Assert.Null(DispatchImport.Match(requested, [Hlf, Dlk]));
    }

    [Fact]
    public void A_short_name_that_ends_two_vehicles_matches_neither()
    {
        var other = new Vehicle("Nachbarwehr", "Florian Beispielried 2/40/1", 9);

        Assert.Null(DispatchImport.Match("2/40/1", [Hlf, other]));
    }

    [Fact]
    public void The_file_is_named_after_the_alarm_time_and_stichwort()
    {
        var import = new DispatchImport(Alarm(), []);

        Assert.Equal("20261108-1717 THL 2 T2917" + Services.IncidentFileExtension.Current, import.SuggestedFileName(Now));
    }

    [Fact]
    public void A_stichwort_that_is_no_file_name_is_made_safe()
    {
        var alarm = Alarm() with { Keyword = "B 3/../..\\Brand" };

        var name = new DispatchImport(alarm, []).SuggestedFileName(Now);

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.StartsWith("20261108-1717", name, StringComparison.Ordinal);
    }

    [Fact]
    public void The_alarm_entry_says_when_and_what_was_alarmed()
    {
        Assert.Equal(
            "Alarmierung 17:17: THL 2 VU eCall – VU eCall - ohne Spracherwiderung. Hinweis: 2 PKW, 3 Betroffene. "
            + "Alarmiert: Florian Musterstadt Wache 2, Florian Musterstadt 1, Florian Musterstadt 2/40/1",
            new DispatchImport(Alarm(), []).JournalText());
    }

    [Fact]
    public void A_probealarm_says_so_in_its_entry()
    {
        Assert.StartsWith("Alarmierung 17:17 (Probealarm):", new DispatchImport(Alarm(DispatchSamples.Xml(test: "1")), []).JournalText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Applying_the_alarm_fills_address_location_and_the_first_entry()
    {
        var session = Session();

        new DispatchImport(Alarm(), []).ApplyTo(session, []);

        var incident = session.Incident;
        Assert.Equal("Hauptstraße", incident.Street);
        Assert.Equal("12", incident.HouseNumber);
        Assert.Equal("12345", incident.PostalCode);
        Assert.Equal("Musterstadt Nord", incident.Place);
        Assert.Equal(new GeoPoint(48.1, 11.5), incident.Location);
        var entry = Assert.Single(incident.Journal, e => e.Direction == EtbDirection.Incoming);
        Assert.Equal(DispatchImport.JournalSender, entry.From);
        Assert.StartsWith("Alarmierung 17:17", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_street_the_stammdaten_know_brings_its_ortsteil_and_place()
    {
        var session = Session();
        var streets = new[] { new Street("Hauptstraße", "Nord", "12345", "Musterstadt") };

        new DispatchImport(Alarm(), []).ApplyTo(session, streets);

        Assert.Equal("Nord", session.Incident.District);
        Assert.Equal("Musterstadt", session.Incident.Place);
        Assert.Equal("12345", session.Incident.PostalCode);
    }

    [Fact]
    public void A_matched_vehicle_comes_in_with_its_seats_and_type()
    {
        var session = Session();

        new DispatchImport(Alarm(), [new DispatchUnit("Florian Musterstadt 2/40/1", Hlf)]).ApplyTo(session, []);

        var unit = Assert.Single(session.Incident.Forces);
        Assert.Equal("Wache 2", unit.Brigade);
        Assert.Equal("Florian Musterstadt 2/40/1", unit.CallSign);
        Assert.Equal("0/1/8/9", unit.StrengthText);
        Assert.Equal("HLF 20", unit.VehicleType);
    }

    [Fact]
    public void An_unknown_unit_comes_in_under_the_alarmed_feuerwehr_with_no_strength_yet()
    {
        var session = Session();

        new DispatchImport(Alarm(), [new DispatchUnit("Florian Musterstadt 1", null)]).ApplyTo(session, []);

        var unit = Assert.Single(session.Incident.Forces);
        Assert.Equal("Feuerwehr Musterstadt", unit.Brigade);
        Assert.Equal("Florian Musterstadt 1", unit.CallSign);
        Assert.Equal(0, unit.PersonnelCount);
    }

    // "2/40/1" and the full Funkrufname both find the HLF; a Leitstelle listing a unit twice in
    // another case is the same unit. Recording either twice would double the Stärke.
    [Fact]
    public void A_unit_ticked_under_two_names_is_recorded_once()
    {
        var session = Session();

        new DispatchImport(
            Alarm(),
            [
                new DispatchUnit("Florian Musterstadt 2/40/1", Hlf),
                new DispatchUnit("2/40/1", Hlf),
                new DispatchUnit("Florian Musterstadt 1", null),
                new DispatchUnit("florian musterstadt 1", null),
            ]).ApplyTo(session, []);

        Assert.Equal(["Florian Musterstadt 2/40/1", "Florian Musterstadt 1"], session.Incident.Forces.Select(f => f.CallSign));
    }

    [Fact]
    public void Units_left_unticked_are_not_recorded()
    {
        var session = Session();

        new DispatchImport(Alarm(), []).ApplyTo(session, []);

        Assert.Empty(session.Incident.Forces);
    }

    [Fact]
    public void A_blank_name_never_matches_a_vehicle_without_funkrufname()
    {
        Assert.Null(DispatchImport.Match(" ", [new Vehicle("Wache 3", string.Empty, 6)]));
    }

    [Fact]
    public void An_alarm_with_nothing_but_its_time_says_only_that()
    {
        var alarm = Alarm() with { Keyword = null, IncidentType = null, Report = null, Hint = null, RequestedUnits = [] };

        Assert.Equal("Alarmierung 17:17", new DispatchImport(alarm, []).JournalText());
    }

    [Fact]
    public void Without_an_einsatztyp_the_entry_names_the_stichwort_and_the_objekt()
    {
        var alarm = Alarm() with { IncidentType = null, Report = null, Hint = null, Building = "Grundschule Nord", RequestedUnits = [] };

        Assert.Equal("Alarmierung 17:17: THL 2 T2917. Objekt: Grundschule Nord", new DispatchImport(alarm, []).JournalText());
    }

    [Fact]
    public void Without_an_alarm_time_the_entry_and_the_file_name_go_by_now()
    {
        var import = new DispatchImport(Alarm() with { AlarmedAt = null, Keyword = null }, []);

        Assert.StartsWith("Alarmierung: ", import.JournalText(), StringComparison.Ordinal);
        Assert.Equal("20261108-1742" + Services.IncidentFileExtension.Current, import.SuggestedFileName(Now));
    }

    // The Stammdaten spell the street as the Lagebuch knows it, and their PLZ is the precise one.
    [Fact]
    public void The_stammdaten_spelling_and_plz_of_a_known_street_win()
    {
        var session = Session();
        var streets = new[] { new Street("Hauptstraße", "Nord", "12346", "Musterstadt") };

        new DispatchImport(Alarm(DispatchSamples.Xml(street: "hauptstraße 12")), []).ApplyTo(session, streets);

        Assert.Equal("Hauptstraße", session.Incident.Street);
        Assert.Equal("12346", session.Incident.PostalCode);
    }

    [Fact]
    public void A_vehicle_type_in_another_case_comes_in_as_the_catalogue_writes_it()
    {
        Assert.Equal("HLF", ViewModels.VehiclePreset.Of(new Vehicle("Wache 2", "2/40/1", 9, VehicleType: " hlf ")).VehicleType);
    }
}
