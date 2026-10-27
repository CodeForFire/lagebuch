using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class CoMeasurementPersistenceTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"co-{Guid.NewGuid():N}.lagebuch");

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static Incident CreateIncidentWithBuilding()
    {
        var clock = new Clock();
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        incident.RecordCoValue(clock, op, incident.Buildings[0].Id, 0, 1, 45);
        incident.SetDwellingStatus(clock, op, incident.Buildings[0].Id, 0, 2, DwellingStatus.Searched);
        incident.SetDwellingDetails(incident.Buildings[0].Id, 0, 1, "Müller", KeyAvailability.Available);
        incident.SetFloorDescription(incident.Buildings[0].Id, 1, "rechts");
        return incident;
    }

    // --- Issue #424: the Messreihe ---
    [Fact]
    public void SaveLoad_RoundTrips_TheReadingSeries()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var haus = incident.Buildings[0].Id;

        incident.RecordCoValue(clock, op, haus, 0, 1, 120);
        clock.Now = clock.Now.AddMinutes(27);
        incident.RecordCoValue(clock, op, haus, 0, 1, 40);
        clock.Now = clock.Now.AddMinutes(21);
        incident.RecordCoValue(clock, op, haus, 0, 1, null);

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        var dwelling = loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(new decimal?[] { 120, 40, null }, dwelling.Readings.Select(r => r.Value));
        Assert.All(dwelling.Readings, r => Assert.Equal("Huber (FFB 12/1)", r.RecordedBy));
        Assert.Equal(
            new[] { "10:00", "10:27", "10:48" },
            dwelling.Readings.Select(r => Formatting.TimeOfDay(r.MeasuredAt.ToUniversalTime())));
        Assert.Null(dwelling.CoValue());
    }

    // The ApplyV21 lesson: force_unit_edits needed a cleanup migration because Save's DELETE list
    // did not list it, so every save stacked another copy of the history onto the file. This is
    // the test that keeps co_readings from repeating it.
    [Fact]
    public void Save_Twice_DoesNotDuplicateTheSeries()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValue(clock, op, haus, 0, 1, 120);
        clock.Now = clock.Now.AddMinutes(27);
        incident.RecordCoValue(clock, op, haus, 0, 1, 40);

        IncidentRepository.Save(_path, incident);
        IncidentRepository.Save(_path, incident);

        var loaded = IncidentRepository.Load(_path);
        var dwelling = loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(new decimal?[] { 120, 40 }, dwelling.Readings.Select(r => r.Value));
    }

    [Fact]
    public void SaveLoad_KeepsEachDwellingsSeriesApart()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValue(clock, op, haus, 0, 1, 120);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordCoValue(clock, op, haus, 0, 1, 40);
        incident.RecordCoValue(clock, op, haus, 1, 2, 8);

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        Assert.Equal(2, loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).Readings.Count);
        Assert.Single(loaded.Dwellings.Single(d => d.FloorOrdinal == 1 && d.ApartmentNumber == 2).Readings);
        Assert.Empty(loaded.Dwellings.Single(d => d.FloorOrdinal == 2 && d.ApartmentNumber == 1).Readings);
    }

    // #419: removing a Wohnung renumbers the survivors. That has to move the records rather than
    // rebuild them — co_readings rows hang off Dwelling.Id, so a survivor recreated under a new id
    // would come back from disk with an empty Messreihe.
    [Fact]
    public void SaveLoad_AfterRemovingAWohnung_KeepsTheSurvivorsMessreihen()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 4);
        var haus = incident.Buildings[0].Id;
        incident.SetApartmentLabel(haus, 0, 4, "Kiosk");
        incident.RecordCoValue(clock, op, haus, 0, 4, 120);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordCoValue(clock, op, haus, 0, 4, 40);

        incident.RemoveDwellings(clock, op, haus, 0, new[] { 2 });

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        // Whg. 4 is Whg. 3 now, with both readings and its Bezeichnung still attached.
        var moved = loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 3);
        Assert.Equal(40, moved.CoValue());
        Assert.Equal(2, moved.Readings.Count);
        Assert.Equal("Kiosk", CoMeasurementLabels.ApartmentLabel(loaded.Buildings[0], 0, 3));
        Assert.Equal(3, loaded.Buildings[0].ApartmentsFor(0));
        Assert.Equal(3, loaded.Dwellings.Count(d => d.FloorOrdinal == 0));
    }

    [Fact]
    public void SaveLoad_RoundTrip_BuildingsAndDwellings()
    {
        var original = CreateIncidentWithBuilding();

        IncidentRepository.Save(_path, original);

        var loaded = IncidentRepository.Load(_path);

        Assert.Single(loaded.Buildings);
        Assert.Equal("Haus A", loaded.Buildings[0].Name);
        Assert.Equal(2, loaded.Buildings[0].FloorCount);
        Assert.Equal(3, loaded.Buildings[0].ApartmentsPerFloor);
        Assert.Equal(9, loaded.Dwellings.Count);

        var dwelling = loaded.Dwellings.First(d =>
            d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(45, dwelling.CoValue());
        Assert.Equal("Müller", dwelling.ResidentName);
        Assert.Equal(KeyAvailability.Available, dwelling.KeyAvailable);

        var searched = loaded.Dwellings.First(d =>
            d.FloorOrdinal == 0 && d.ApartmentNumber == 2);
        Assert.Equal(DwellingStatus.Searched, searched.Status);

        Assert.Equal("rechts", loaded.Buildings[0].FloorDescriptions[1]);
    }

    [Fact]
    public void SaveLoad_RoundTrip_NullableFields()
    {
        var clock = new Clock();
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 1);

        IncidentRepository.Save(_path, incident);

        var loaded = IncidentRepository.Load(_path);

        var dwelling = loaded.Dwellings[0];
        Assert.Null(dwelling.CoValue());
        Assert.Null(dwelling.ResidentName);
        Assert.Equal(KeyAvailability.Unknown, dwelling.KeyAvailable);
    }

    private static Incident CreateIncidentWithEveryKeyState()
    {
        var clock = new Clock();
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 3);
        var haus = incident.Buildings[0].Id;
        incident.SetDwellingDetails(haus, 0, 1, null, KeyAvailability.Available);
        incident.SetDwellingDetails(haus, 0, 2, null, KeyAvailability.NotAvailable);
        incident.SetDwellingDetails(haus, 0, 3, null, KeyAvailability.Unknown);
        return incident;
    }

    [Fact]
    public void SaveLoad_round_trip_keeps_every_key_availability_state()
    {
        IncidentRepository.Save(_path, CreateIncidentWithEveryKeyState());

        var loaded = IncidentRepository.Load(_path);

        var keys = loaded.Dwellings.Where(d => d.FloorOrdinal == 0).OrderBy(d => d.ApartmentNumber).Select(d => d.KeyAvailable);
        Assert.Equal(
            new[] { KeyAvailability.Available, KeyAvailability.NotAvailable, KeyAvailability.Unknown },
            keys);
    }

    // The enum is not what lands on disk: key_available stays NULL/1/0, so a file written by
    // this build still opens in an older one, and an older file needs no migration.
    [Fact]
    public void Key_availability_is_stored_as_the_pre_enum_null_one_zero_encoding()
    {
        IncidentRepository.Save(_path, CreateIncidentWithEveryKeyState());

        var stored = new List<long?>();
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT key_available FROM co_dwellings WHERE floor_ordinal = 0 ORDER BY apartment_number;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                stored.Add(reader.IsDBNull(0) ? null : reader.GetInt64(0));
            }
        }

        Assert.Equal(new long?[] { 1, 0, null }, stored);
    }

    [Fact]
    public void SaveLoad_round_trip_keeps_the_Schluesselnummer()
    {
        var incident = CreateIncidentWithEveryKeyState();
        incident.SetDwellingDetails(incident.Buildings[0].Id, 0, 1, null, KeyAvailability.Available, "B-14");
        IncidentRepository.Save(_path, incident);

        var loaded = IncidentRepository.Load(_path);

        var numbers = loaded.Dwellings.Where(d => d.FloorOrdinal == 0).OrderBy(d => d.ApartmentNumber).Select(d => d.KeyNumber);
        Assert.Equal(new[] { "B-14", null, null }, numbers);
    }

    // --- Issue #410: up to eight gases ---
    [Fact]
    public void Several_gases_with_decimals_round_trip_with_their_list_and_their_series()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", null);
        var incident = Incident.Start(clock, op);
        var o2 = new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m);
        var co2 = new GasDefinition("CO2", "Vol-%", GasDirection.Rising, null, null, null, null);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3, gases: [GasDefinition.DefaultCo, o2, co2]);
        var (co, oxygen) = (incident.Gases[0].Id, incident.Gases[1].Id);
        incident.RecordCoValues(clock, op, incident.Buildings[0].Id, 0, 1, new Dictionary<Guid, decimal?> { [co] = 120, [oxygen] = 20.9m });
        clock.Now = clock.Now.AddMinutes(10);
        incident.RecordCoValues(clock, op, incident.Buildings[0].Id, 0, 1, new Dictionary<Guid, decimal?> { [oxygen] = 17.25m });

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        Assert.Equal(incident.Gases, loaded.Gases);
        var dwelling = loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(120m, dwelling.ValueOf(co));
        Assert.Equal(17.25m, dwelling.ValueOf(oxygen));
        Assert.Equal(
            new (Guid, decimal?)[] { (co, 120m), (oxygen, 20.9m), (oxygen, 17.25m) },
            dwelling.Readings.Select(r => (r.GasId, r.Value)));
    }

    // A file from before #410 has one CO value per Wohnung and a Messreihe of integers. It must
    // come back with exactly one gas, CO in ppm with the old bands, and every value on it.
    [Fact]
    public void A_file_from_before_the_gas_list_comes_back_with_one_CO_gas_and_nothing_lost()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        incident.RecordCoValue(clock, op, incident.Buildings[0].Id, 0, 1, 120);
        clock.Now = clock.Now.AddMinutes(27);
        incident.RecordCoValue(clock, op, incident.Buildings[0].Id, 0, 1, 40);
        IncidentRepository.Save(_path, incident);
        RollBackToV35(incident.Gases[0].Id);

        var loaded = IncidentRepository.Load(_path);

        var gas = Assert.Single(loaded.Gases);
        Assert.Equal(IncidentGas.LegacyCo, gas);
        var dwelling = loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(40m, dwelling.ValueOf(IncidentGas.LegacyCoId));
        Assert.Equal(new decimal?[] { 120, 40 }, dwelling.Readings.Select(r => r.Value));
        Assert.All(dwelling.Readings, r => Assert.Equal(IncidentGas.LegacyCoId, r.GasId));
    }

    // The V35 shape: no gas table, the integer co_value and reading value, no gas ids.
    private void RollBackToV35(Guid gasId)
    {
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                UPDATE co_dwellings SET co_value = CAST(json_extract(gas_values, '$."' || $gas || '"') AS INTEGER);
                UPDATE co_readings SET value = CAST(measured_value AS INTEGER);
                DROP TABLE co_gases;
                ALTER TABLE co_dwellings DROP COLUMN gas_values;
                ALTER TABLE co_readings DROP COLUMN gas_id;
                ALTER TABLE co_readings DROP COLUMN measured_value;
                DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (35);
                """;
            cmd.Parameters.AddWithValue("$gas", gasId.ToString());
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }
}
