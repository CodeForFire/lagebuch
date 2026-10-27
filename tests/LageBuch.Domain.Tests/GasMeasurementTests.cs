using System.Text.Json;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

// #410: up to eight gases per Wohnung, each with its own unit and bands, copied from the
// Stammdaten into the Einsatz when its first Haus is opened.
public class GasMeasurementTests
{
    private static readonly GasDefinition H2S = new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m);
    private static readonly GasDefinition O2 = new("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m);
    private static readonly GasDefinition Co2 = new("CO2", "Vol-%", GasDirection.Rising, null, null, null, null);

    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 10, 27, 8, 14, 0, TimeSpan.Zero));
    private readonly SessionOperator _op = new("Huber", null);

    [Fact]
    public void The_first_Haus_copies_the_Stammdaten_gases_in_their_order()
    {
        var incident = Incident.Start(_clock, _op);

        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: [GasDefinition.DefaultCo, H2S, O2]);

        Assert.Equal(new[] { "CO", "H2S", "O2" }, incident.Gases.Select(g => g.Name));
        Assert.Equal(O2, incident.Gases[2].Definition);
        Assert.Equal(3, incident.Gases.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void A_later_Haus_keeps_the_list_the_Einsatz_already_has()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: [GasDefinition.DefaultCo, H2S]);
        var before = incident.Gases.ToList();

        incident.AddCoBuilding(_clock, _op, "Haus B", 2, 3, gases: [O2]);

        Assert.Equal(before, incident.Gases);
    }

    [Fact]
    public void A_Haus_opened_without_Stammdaten_gases_measures_CO_as_before()
    {
        var incident = Incident.Start(_clock, _op);

        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3);

        Assert.Equal(GasDefinition.DefaultCo, Assert.Single(incident.Gases).Definition);
    }

    [Fact]
    public void The_copied_list_is_capped_at_eight_gases()
    {
        var incident = Incident.Start(_clock, _op);
        var ten = Enumerable.Range(1, 10).Select(i => H2S with { Name = $"G{i}" });

        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: ten);

        Assert.Equal(GasDefinition.MaxCount, incident.Gases.Count);
    }

    [Fact]
    public void One_FERTIG_writes_one_reading_per_changed_gas_and_one_ETB_entry_naming_them_all()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: [GasDefinition.DefaultCo, H2S, O2]);
        var (co, h2s) = (incident.Gases[0].Id, incident.Gases[1].Id);
        var journalBefore = incident.Journal.Count;

        incident.RecordCoValues(_clock, _op, incident.Buildings[0].Id, 2, 1, new Dictionary<Guid, decimal?> { [co] = 120, [h2s] = 3 });

        var dwelling = incident.Dwellings.Single(d => d.FloorOrdinal == 2 && d.ApartmentNumber == 1);
        Assert.Equal(2, dwelling.Readings.Count);
        Assert.All(dwelling.Readings, r => Assert.Equal(_clock.Now, r.MeasuredAt));
        Assert.Equal(journalBefore + 1, incident.Journal.Count);
        var entry = incident.Journal[^1];
        Assert.Equal(EtbDirection.Measurement, entry.Direction);
        Assert.Equal("Gasmessung Haus A, 2. OG, Links: CO 120 ppm, H2S 3 ppm", entry.Text);
    }

    [Fact]
    public void Unchanged_gases_are_left_out_of_the_reading_and_the_entry()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: [GasDefinition.DefaultCo, H2S]);
        var (co, h2s) = (incident.Gases[0].Id, incident.Gases[1].Id);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValues(_clock, _op, haus, 0, 1, new Dictionary<Guid, decimal?> { [co] = 120, [h2s] = 3 });

        incident.RecordCoValues(_clock, _op, haus, 0, 1, new Dictionary<Guid, decimal?> { [co] = 120, [h2s] = null });

        Assert.Equal(3, incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).Readings.Count);
        Assert.EndsWith(": H2S gelöscht", incident.Journal[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decimal_value_is_kept_and_written_the_German_way()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: [O2]);

        incident.RecordCoValues(_clock, _op, incident.Buildings[0].Id, 0, 1, new Dictionary<Guid, decimal?> { [incident.Gases[0].Id] = 20.9m });

        Assert.Equal(20.9m, incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).ValueOf(incident.Gases[0].Id));
        Assert.EndsWith(": O2 20,9 Vol-%", incident.Journal[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_is_rounded_to_two_decimals()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: [O2]);
        var o2 = incident.Gases[0].Id;

        incident.RecordCoValues(_clock, _op, incident.Buildings[0].Id, 0, 1, new Dictionary<Guid, decimal?> { [o2] = 20.555m });

        Assert.Equal(20.56m, incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).ValueOf(o2));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100000.01")]
    public void A_value_outside_the_range_is_refused(string value)
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => incident.RecordCoValues(
            _clock, _op, incident.Buildings[0].Id, 0, 1, new Dictionary<Guid, decimal?> { [incident.Gases[0].Id] = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture) }));
    }

    [Fact]
    public void A_gas_the_Einsatz_does_not_have_is_refused_and_nothing_is_recorded()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3);
        var journalBefore = incident.Journal.Count;

        Assert.Throws<ArgumentException>(() => incident.RecordCoValues(
            _clock, _op, incident.Buildings[0].Id, 0, 1, new Dictionary<Guid, decimal?> { [incident.Gases[0].Id] = 40, [Guid.NewGuid()] = 1 }));

        Assert.Empty(incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).Readings);
        Assert.Equal(journalBefore, incident.Journal.Count);
    }

    [Fact]
    public void More_than_eight_values_at_once_are_refused()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3);
        var values = Enumerable.Range(0, 9).ToDictionary(_ => Guid.NewGuid(), _ => (decimal?)1);

        Assert.Throws<ArgumentOutOfRangeException>(() => incident.RecordCoValues(_clock, _op, incident.Buildings[0].Id, 0, 1, values));
    }

    [Theory]
    [InlineData("20.9", CoSeverity.Normal)]
    [InlineData("19", CoSeverity.Elevated)]
    [InlineData("17", CoSeverity.Dangerous)]
    [InlineData("12", CoSeverity.Dangerous)]
    [InlineData("10", CoSeverity.Lethal)]
    public void A_falling_gas_gets_worse_as_it_drops(string value, CoSeverity expected)
    {
        Assert.Equal(expected, GasSeverityClassifier.SeverityOf(O2, decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_gas_without_thresholds_is_never_coloured()
    {
        Assert.Equal(CoSeverity.Normal, GasSeverityClassifier.SeverityOf(Co2, 99_999m));
        Assert.False(GasSeverityClassifier.IsImplausible(Co2, 99_999m));
    }

    [Fact]
    public void Plausibility_is_an_upper_bound_for_a_falling_gas_too()
    {
        Assert.True(GasSeverityClassifier.IsImplausible(O2, 209m));
        Assert.False(GasSeverityClassifier.IsImplausible(O2, 5m));
    }

    [Fact]
    public void The_worst_gas_decides_the_colour()
    {
        var gases = Gases(GasDefinition.DefaultCo, H2S, O2);
        var values = new Dictionary<Guid, decimal> { [gases[0].Id] = 40, [gases[1].Id] = 12, [gases[2].Id] = 20.9m };

        var worst = GasSeverityClassifier.Worst(gases, values);

        Assert.NotNull(worst);
        Assert.Equal("H2S", worst.Gas.Name);
        Assert.Equal(CoSeverity.Dangerous, worst.Severity);
    }

    [Fact]
    public void On_a_tie_the_gas_earlier_in_the_list_wins()
    {
        var gases = Gases(GasDefinition.DefaultCo, H2S);
        var values = new Dictionary<Guid, decimal> { [gases[0].Id] = 250, [gases[1].Id] = 12 };

        Assert.Equal("CO", GasSeverityClassifier.Worst(gases, values)?.Gas.Name);
    }

    [Fact]
    public void While_every_gas_is_normal_the_first_measured_gas_is_shown()
    {
        var gases = Gases(GasDefinition.DefaultCo, H2S, O2);
        var values = new Dictionary<Guid, decimal> { [gases[1].Id] = 1, [gases[2].Id] = 20.9m };

        Assert.Equal("H2S", GasSeverityClassifier.Worst(gases, values)?.Gas.Name);
    }

    [Fact]
    public void Nothing_measured_has_no_worst_gas()
    {
        Assert.Null(GasSeverityClassifier.Worst(Gases(GasDefinition.DefaultCo), new Dictionary<Guid, decimal>()));
    }

    [Fact]
    public void A_Messreihe_line_names_the_gas_only_when_there_is_more_than_one()
    {
        var single = Gases(GasDefinition.DefaultCo);
        var several = Gases(GasDefinition.DefaultCo, H2S);
        var at = new DateTimeOffset(2026, 10, 27, 8, 41, 0, TimeSpan.Zero);

        Assert.Equal("08:41 40 ppm", CoMeasurementLabels.ReadingLabel(new CoReading(at, single[0].Id, 40, "x"), single));
        Assert.Equal("08:41 H2S 3 ppm", CoMeasurementLabels.ReadingLabel(new CoReading(at, several[1].Id, 3, "x"), several));
        Assert.Equal("08:41 H2S gelöscht", CoMeasurementLabels.ReadingLabel(new CoReading(at, several[1].Id, null, "x"), several));
        Assert.Equal("08:41 gelöscht", CoMeasurementLabels.ReadingLabel(new CoReading(at, single[0].Id, null, "x"), single));
    }

    [Fact]
    public void Rehydrate_caps_and_sanitises_a_hostile_gas_list()
    {
        var hostile = Enumerable.Range(1, 12)
            .Select(i => new IncidentGas(Guid.NewGuid(), H2S with { Name = $"  Gas{i} sehr langer Name  " }))
            .Prepend(new IncidentGas(Guid.NewGuid(), H2S with { Name = "  " }))
            .ToList();

        var incident = Rehydrate(hostile, buildings: true);

        Assert.Equal(GasDefinition.MaxCount, incident.Gases.Count);
        Assert.All(incident.Gases, g => Assert.True(g.Name.Length <= GasDefinition.MaxNameLength));
    }

    [Fact]
    public void Rehydrate_drops_thresholds_that_are_out_of_order_so_the_gas_stays_neutral()
    {
        var reversed = new IncidentGas(Guid.NewGuid(), H2S with { Elevated = 100, Lethal = 5 });

        var incident = Rehydrate([reversed], buildings: true);

        var gas = Assert.Single(incident.Gases).Definition;
        Assert.Null(gas.Elevated);
        Assert.Null(gas.Dangerous);
        Assert.Null(gas.Lethal);
    }

    [Fact]
    public void Rehydrate_of_a_file_with_Haeuser_but_no_gas_list_gets_the_legacy_CO_gas_back()
    {
        var incident = Rehydrate([], buildings: true);

        Assert.Equal(IncidentGas.LegacyCo, Assert.Single(incident.Gases));
    }

    [Fact]
    public void Rehydrate_drops_readings_and_values_of_a_gas_the_list_does_not_have()
    {
        var known = new IncidentGas(Guid.NewGuid(), GasDefinition.DefaultCo);
        var stranger = Guid.NewGuid();
        var building = Building.Create("Haus A", 1, 1, 0);
        var at = _clock.Now;
        var values = new Dictionary<Guid, decimal> { [known.Id] = 40, [stranger] = 7 };
        CoReading[] readings = [new(at, known.Id, 40, "x"), new(at, stranger, 7, "x")];
        var dwelling = Dwelling.Rehydrate(Guid.NewGuid(), building.Id, 0, 1, null, DwellingStatus.NotSearched, KeyAvailability.Unknown, values, readings);

        var incident = Rehydrate([known], buildings: true, building, dwelling);

        var loaded = Assert.Single(incident.Dwellings);
        Assert.Equal(known.Id, Assert.Single(loaded.Values).Key);
        Assert.Equal(known.Id, Assert.Single(loaded.Readings).GasId);
    }

    [Fact]
    public void The_editor_suggests_the_four_usual_units()
    {
        Assert.Equal(new[] { "ppm", "%UEG", "Vol-%", "mg/m³" }, GasDefinition.UnitSuggestions);
    }

    [Fact]
    public void A_gas_whose_name_is_only_whitespace_sanitises_to_nothing()
    {
        Assert.Null((H2S with { Name = "   " }).Sanitized());
    }

    [Fact]
    public void Sanitising_keeps_a_falling_direction_and_turns_an_unknown_one_rising()
    {
        var unknown = (O2 with { Direction = (GasDirection)7 }).Sanitized();

        Assert.NotNull(unknown);
        Assert.Equal(GasDirection.Rising, unknown.Direction);
        Assert.Equal(GasDirection.Falling, O2.Sanitized()?.Direction);
    }

    [Fact]
    public void Sanitising_a_gas_without_a_unit_leaves_the_unit_empty()
    {
        var fromFile = JsonSerializer.Deserialize<GasDefinition>("""{"Name":"H2S","Unit":null,"Direction":0}""");

        Assert.NotNull(fromFile);
        Assert.Equal(string.Empty, fromFile.Sanitized()?.Unit);
    }

    [Fact]
    public void Zero_and_the_maximum_are_both_valid_readings()
    {
        Assert.Equal(0m, GasDefinition.NormalizeValue(0m));
        Assert.Equal(GasDefinition.MaxValue, GasDefinition.NormalizeValue(GasDefinition.MaxValue));
    }

    [Fact]
    public void A_measurement_drops_trailing_zeros_and_an_empty_unit_adds_no_space()
    {
        Assert.Equal("20,5 Vol-%", Formatting.Measurement(20.50m, "Vol-%"));
        Assert.Equal("20,5", Formatting.Measurement(20.50m, string.Empty));
    }

    [Fact]
    public void A_Wohnung_rehydrated_without_values_has_none()
    {
        var dwelling = Dwelling.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), 0, 1, null, DwellingStatus.NotSearched, KeyAvailability.Unknown, values: null);

        Assert.Empty(dwelling.Values);
    }

    [Fact]
    public void A_Wohnung_measuring_only_known_gases_is_kept_as_it_is()
    {
        var known = Guid.NewGuid();
        var dwelling = Wohnung(new Dictionary<Guid, decimal> { [known] = 40 }, [new(_clock.Now, known, 40, "x")]);

        Assert.Same(dwelling, dwelling.KeepingGases(new HashSet<Guid> { known }));
    }

    [Fact]
    public void A_reading_of_an_unknown_gas_is_dropped_even_when_every_value_is_known()
    {
        var known = Guid.NewGuid();
        var dwelling = Wohnung(new Dictionary<Guid, decimal> { [known] = 40 }, [new(_clock.Now, known, 40, "x"), new(_clock.Now, Guid.NewGuid(), 7, "x")]);

        var kept = dwelling.KeepingGases(new HashSet<Guid> { known });

        Assert.Equal(known, Assert.Single(kept.Readings).GasId);
    }

    [Fact]
    public void A_value_of_an_unknown_gas_is_dropped_even_when_every_reading_is_known()
    {
        var known = Guid.NewGuid();
        var dwelling = Wohnung(new Dictionary<Guid, decimal> { [known] = 40, [Guid.NewGuid()] = 7 }, [new(_clock.Now, known, 40, "x")]);

        var kept = dwelling.KeepingGases(new HashSet<Guid> { known });

        Assert.Equal(known, Assert.Single(kept.Values).Key);
    }

    [Fact]
    public void A_Messreihe_line_of_a_gas_the_list_lacks_shows_the_bare_number()
    {
        var gases = Gases(GasDefinition.DefaultCo);
        var at = new DateTimeOffset(2026, 10, 27, 8, 41, 0, TimeSpan.Zero);

        Assert.Equal("08:41 7", CoMeasurementLabels.ReadingLabel(new CoReading(at, Guid.NewGuid(), 7, "x"), gases));
    }

    [Fact]
    public void Removing_a_Gasmessprotokoll_is_written_to_the_ETB()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3);

        incident.RemoveCoBuilding(_clock, _op, incident.Buildings[0].Id);

        Assert.Equal("Gasmessprotokoll entfernt: Haus A", incident.Journal[^1].Text);
    }

    [Fact]
    public void Eight_values_at_once_are_accepted()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: Enumerable.Range(1, 8).Select(i => H2S with { Name = $"G{i}" }));

        incident.RecordCoValues(_clock, _op, incident.Buildings[0].Id, 0, 1, incident.Gases.ToDictionary(g => g.Id, _ => (decimal?)1));

        Assert.Equal(8, incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).Values.Count);
    }

    [Fact]
    public void A_refused_set_of_values_says_why()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3);
        var haus = incident.Buildings[0].Id;
        var co = incident.Gases[0].Id;

        var tooMany = Assert.Throws<ArgumentOutOfRangeException>(() => incident.RecordCoValues(
            _clock, _op, haus, 0, 1, Enumerable.Range(0, 9).ToDictionary(_ => Guid.NewGuid(), _ => (decimal?)1)));
        var unknown = Assert.Throws<ArgumentException>(() => incident.RecordCoValues(
            _clock, _op, haus, 0, 1, new Dictionary<Guid, decimal?> { [Guid.NewGuid()] = 1 }));
        var outOfRange = Assert.Throws<ArgumentOutOfRangeException>(() => incident.RecordCoValues(
            _clock, _op, haus, 0, 1, new Dictionary<Guid, decimal?> { [co] = -1 }));

        Assert.StartsWith("Höchstens 8 Messgase.", tooMany.Message, StringComparison.Ordinal);
        Assert.StartsWith("Unbekanntes Messgas.", unknown.Message, StringComparison.Ordinal);
        Assert.StartsWith("Messwert CO liegt außerhalb von 0 bis 100000 ppm.", outOfRange.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gas_left_out_of_a_FERTIG_keeps_its_value()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3, gases: [GasDefinition.DefaultCo, H2S]);
        var (co, h2s) = (incident.Gases[0].Id, incident.Gases[1].Id);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValues(_clock, _op, haus, 0, 1, new Dictionary<Guid, decimal?> { [co] = 120, [h2s] = 3 });

        incident.RecordCoValues(_clock, _op, haus, 0, 1, new Dictionary<Guid, decimal?> { [co] = 200 });

        Assert.Equal(3m, incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).ValueOf(h2s));
    }

    [Fact]
    public void Rehydrate_keeps_only_the_first_of_two_gases_with_the_same_id()
    {
        var id = Guid.NewGuid();

        var incident = Rehydrate([new IncidentGas(id, H2S), new IncidentGas(id, O2)], buildings: true);

        Assert.Equal("H2S", Assert.Single(incident.Gases).Name);
    }

    [Fact]
    public void Rehydrate_of_a_file_without_Haeuser_or_gases_measures_nothing_yet()
    {
        Assert.Empty(Rehydrate([], buildings: false).Gases);
    }

    [Fact]
    public void Removing_a_Wohnung_whose_value_was_cleared_still_names_its_Messreihe()
    {
        var incident = Incident.Start(_clock, _op);
        incident.AddCoBuilding(_clock, _op, "Haus A", 2, 3);
        var haus = incident.Buildings[0].Id;
        var co = incident.Gases[0].Id;
        incident.RecordCoValues(_clock, _op, haus, 0, 1, new Dictionary<Guid, decimal?> { [co] = 40 });
        incident.RecordCoValues(_clock, _op, haus, 0, 1, new Dictionary<Guid, decimal?> { [co] = null });

        incident.RemoveDwellings(_clock, _op, haus, 0, [1]);

        Assert.EndsWith("entfernt: Links (Messreihe)", incident.Journal[^1].Text, StringComparison.Ordinal);
    }

    private static Dwelling Wohnung(Dictionary<Guid, decimal> values, CoReading[] readings) =>
        Dwelling.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), 0, 1, null, DwellingStatus.NotSearched, KeyAvailability.Unknown, values, readings);

    private static List<IncidentGas> Gases(params GasDefinition[] definitions) =>
        definitions.Select(d => new IncidentGas(Guid.NewGuid(), d)).ToList();

    private Incident Rehydrate(IEnumerable<IncidentGas> gases, bool buildings, Building? building = null, Dwelling? dwelling = null)
    {
        var haus = building ?? Building.Create("Haus A", 1, 1, 0);
        return Incident.Rehydrate(
            id: Guid.NewGuid(),
            startedAt: _clock.Now,
            state: IncidentState.Open,
            incidentNumber: null,
            keyword: null,
            street: null,
            district: null,
            status: null,
            closedAt: null,
            closedBy: null,
            checklists: [],
            journal: [],
            roles: [],
            forces: [],
            scbaTrupps: [],
            audit: [],
            timers: [],
            files: [],
            tasks: [],
            buildings: buildings ? [haus] : [],
            dwellings: dwelling is null ? [] : [dwelling],
            involvedParties: [],
            gases: gases);
    }
}
