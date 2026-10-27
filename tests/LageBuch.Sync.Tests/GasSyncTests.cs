using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Sync.Tests;

// #410: the gas list and the per-gas values on the wire. Everything a peer sends is hostile.
public class GasSyncTests
{
    private static readonly OperatorDto Huber = new("Huber", "FFB 12/1");
    private static readonly GasDefinitionDto H2S = new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m);
    private static readonly GasDefinitionDto O2 = new("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m);

    private static Incident NewIncident(FixedClock clock) =>
        Incident.Start(clock, new SessionOperator("Host", "FFB 1"), keyword: null, incidentNumber: null);

    private static void ApplyOverWire(SyncCommand command, Incident incident, FixedClock clock) =>
        CommandApplier.Apply(SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize(command)), incident, clock);

    private static Incident WithHaus(FixedClock clock, params GasDefinitionDto[] gases)
    {
        var incident = NewIncident(clock);
        ApplyOverWire(new AddCoBuildingCommand(Huber, "Haus A", 2, 3, Gases: gases), incident, clock);
        return incident;
    }

    [Fact]
    public void The_first_Haus_from_a_peer_seeds_the_gas_list_it_carries()
    {
        var clock = new FixedClock();

        var incident = WithHaus(clock, H2S, O2);

        Assert.Equal(new[] { "H2S", "O2" }, incident.Gases.Select(g => g.Name));
        Assert.Equal(GasDirection.Falling, incident.Gases[1].Definition.Direction);
    }

    [Fact]
    public void A_later_Haus_from_a_peer_does_not_change_the_list()
    {
        var clock = new FixedClock();
        var incident = WithHaus(clock, H2S);

        ApplyOverWire(new AddCoBuildingCommand(Huber, "Haus B", 2, 3, Gases: [O2]), incident, clock);

        Assert.Equal("H2S", Assert.Single(incident.Gases).Name);
    }

    [Fact]
    public void A_null_entry_in_a_peer_list_is_skipped_rather_than_crashing_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var json = SyncJson.Serialize<SyncCommand>(new AddCoBuildingCommand(Huber, "Haus A", 2, 3, Gases: [H2S]));
        Assert.Contains("\"gases\":[{", json, StringComparison.Ordinal);

        CommandApplier.Apply(SyncJson.Deserialize<SyncCommand>(json.Replace("\"gases\":[{", "\"gases\":[null,{", StringComparison.Ordinal)), incident, clock);

        Assert.Equal("H2S", Assert.Single(incident.Gases).Name);
    }

    [Fact]
    public void A_peer_list_longer_than_eight_is_cut_at_eight()
    {
        var clock = new FixedClock();

        var incident = WithHaus(clock, Enumerable.Range(1, 20).Select(i => H2S with { Name = $"G{i}" }).ToArray());

        Assert.Equal(GasDefinition.MaxCount, incident.Gases.Count);
    }

    [Fact]
    public void One_command_records_every_changed_gas_with_the_host_clock()
    {
        var clock = new FixedClock();
        var incident = WithHaus(clock, H2S, O2);
        var building = incident.Buildings[0].Id;

        ApplyOverWire(
            new RecordCoValuesCommand(Huber, building, 0, 1, [new(incident.Gases[0].Id, 12), new(incident.Gases[1].Id, 20.9m)]),
            incident,
            clock);

        var dwelling = incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(20.9m, dwelling.ValueOf(incident.Gases[1].Id));
        Assert.All(dwelling.Readings, r => Assert.Equal(clock.Now, r.MeasuredAt));
        Assert.Equal("Gasmessung Haus A, EG, Links: H2S 12 ppm, O2 20,9 Vol-%", incident.Journal[^1].Text);
    }

    [Fact]
    public void A_resent_command_adds_no_second_reading()
    {
        var clock = new FixedClock();
        var incident = WithHaus(clock, H2S);
        var cmd = new RecordCoValuesCommand(Huber, incident.Buildings[0].Id, 0, 1, [new(incident.Gases[0].Id, 12)]);

        ApplyOverWire(cmd, incident, clock);
        ApplyOverWire(cmd, incident, clock);

        Assert.Single(incident.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).Readings);
    }

    [Fact]
    public void A_value_for_a_gas_the_Einsatz_does_not_have_is_refused()
    {
        var clock = new FixedClock();
        var incident = WithHaus(clock, H2S);

        Assert.Throws<ArgumentException>(() => ApplyOverWire(
            new RecordCoValuesCommand(Huber, incident.Buildings[0].Id, 0, 1, [new(Guid.NewGuid(), 1)]), incident, clock));
    }

    [Fact]
    public void More_than_eight_values_are_refused()
    {
        var clock = new FixedClock();
        var incident = WithHaus(clock, H2S);
        var values = Enumerable.Range(0, 9).Select(_ => new GasValueDto(incident.Gases[0].Id, 1)).ToList();

        Assert.Throws<ArgumentOutOfRangeException>(() => ApplyOverWire(
            new RecordCoValuesCommand(Huber, incident.Buildings[0].Id, 0, 1, values), incident, clock));
    }

    [Fact]
    public void The_same_gas_twice_in_one_command_is_refused()
    {
        var clock = new FixedClock();
        var incident = WithHaus(clock, H2S);
        var gas = incident.Gases[0].Id;

        Assert.Throws<ArgumentException>(() => ApplyOverWire(
            new RecordCoValuesCommand(Huber, incident.Buildings[0].Id, 0, 1, [new(gas, 1), new(gas, 2)]), incident, clock));
    }

    [Fact]
    public void A_negative_value_is_refused()
    {
        var clock = new FixedClock();
        var incident = WithHaus(clock, H2S);

        Assert.Throws<ArgumentOutOfRangeException>(() => ApplyOverWire(
            new RecordCoValuesCommand(Huber, incident.Buildings[0].Id, 0, 1, [new(incident.Gases[0].Id, -5)]), incident, clock));
    }

    [Fact]
    public void A_joined_device_shows_the_hosts_gas_list_and_values()
    {
        var clock = new FixedClock();
        var original = WithHaus(clock, H2S, O2);
        ApplyOverWire(
            new RecordCoValuesCommand(Huber, original.Buildings[0].Id, 0, 1, [new(original.Gases[1].Id, 18.5m)]),
            original,
            clock);

        var restored = SnapshotMapper.FromSnapshot(
            SyncJson.Deserialize<IncidentSnapshot>(SyncJson.Serialize(SnapshotMapper.ToSnapshot(original))));

        Assert.Equal(original.Gases, restored.Gases);
        var dwelling = restored.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(18.5m, dwelling.ValueOf(original.Gases[1].Id));
        Assert.Equal(original.Gases[1].Id, Assert.Single(dwelling.Readings).GasId);
    }

    [Fact]
    public void A_snapshot_with_out_of_order_bands_shows_the_gas_without_colour()
    {
        var clock = new FixedClock();
        var original = WithHaus(clock, H2S);
        var snapshot = SnapshotMapper.ToSnapshot(original);
        var hostile = snapshot with
        {
            Gases = [new IncidentGasDto(original.Gases[0].Id, H2S with { Elevated = 100, Lethal = 5 })],
        };

        var restored = SnapshotMapper.FromSnapshot(hostile);

        Assert.Null(Assert.Single(restored.Gases).Definition.Lethal);
    }

    [Fact]
    public void The_record_command_round_trips_with_its_decimal_values()
    {
        var gas = Guid.NewGuid();
        var cmd = new RecordCoValuesCommand(Huber, Guid.NewGuid(), 2, 1, [new(gas, 20.9m), new(Guid.NewGuid(), null)]);

        var back = Assert.IsType<RecordCoValuesCommand>(SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize<SyncCommand>(cmd)));

        Assert.Equal(cmd.Values, back.Values);
    }
}
