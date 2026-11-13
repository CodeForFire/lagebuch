using System.Text.Json;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Persistence.MasterData;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

// #410: the Messgase list in the Stammdaten -- a file is hostile input, the store is the same list.
public class MasterDataGasesTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"md-gases-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static MasterDataSet Parse(string json) => MasterDataJson.Parse(json);

    [Fact]
    public void A_file_without_gases_measures_CO_at_the_DGUV_bands()
    {
        Assert.Equal(GasDefinition.DefaultCo, Assert.Single(Parse("{}").Gases));
        Assert.Equal(GasDefinition.DefaultCo, Assert.Single(Parse("""{ "gases": [] }""").Gases));
        var gas = Assert.Single(Parse("{}").Gases);
        Assert.Equal(("CO", "ppm", 20m, 60m, 200m, 2000m), (gas.Name, gas.Unit, gas.Elevated, gas.Dangerous, gas.Lethal, gas.PlausibleUpTo));
    }

    [Fact]
    public void A_file_reads_each_gas_with_unit_direction_and_decimal_bands()
    {
        var set = Parse("""
            { "gases": [
                { "name": "CO", "unit": "ppm", "elevated": 30, "dangerous": 200, "lethal": 800, "plausibleUpTo": 2000 },
                { "name": "O2", "unit": "Vol-%", "direction": "fallend", "elevated": 19, "dangerous": 17, "lethal": 10.5 },
                { "name": "CO2", "unit": "Vol-%" }
            ] }
            """);

        Assert.Equal(
            new[]
            {
                GasDefinition.LegacyCo,
                new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10.5m, null),
                new GasDefinition("CO2", "Vol-%", GasDirection.Rising, null, null, null, null),
            },
            set.Gases);
    }

    [Fact]
    public void More_than_eight_gases_are_cut_at_eight()
    {
        var gases = string.Join(",", Enumerable.Range(1, 12).Select(i => $$"""{ "name": "G{{i}}" }"""));

        Assert.Equal(GasDefinition.MaxCount, Parse($$"""{ "gases": [{{gases}}] }""").Gases.Count);
    }

    [Fact]
    public void An_overlong_name_and_unit_are_cut_to_their_caps()
    {
        var gas = Assert.Single(Parse("""{ "gases": [{ "name": "Kohlenmonoxid-Messung", "unit": "Milligramm pro m³" }] }""").Gases);

        Assert.Equal(GasDefinition.MaxNameLength, gas.Name.Length);
        Assert.True(gas.Unit.Length <= GasDefinition.MaxUnitLength);
    }

    [Fact]
    public void Thresholds_out_of_order_reject_the_file()
    {
        var ex = Assert.Throws<JsonException>(() =>
            Parse("""{ "gases": [{ "name": "H2S", "elevated": 100, "dangerous": 10 }] }"""));

        Assert.Contains("H2S", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_falling_gas_is_in_order_when_its_bands_fall()
    {
        var gas = Assert.Single(Parse("""{ "gases": [{ "name": "O2", "direction": "fallend", "elevated": 19, "dangerous": 17, "lethal": 10 }] }""").Gases);

        Assert.Equal(GasDirection.Falling, gas.Direction);
    }

    // The same exception types a sync join already catches: anything else would leak the hub
    // connection (HomeViewModel).
    [Theory]
    [InlineData("""{ "gases": [{ "name": "CO", "elevated": "30" }] }""")]
    [InlineData("""{ "gases": [{ "name": "CO", "elevated": 1e40 }] }""")]
    [InlineData("""{ "gases": [{ "name": "CO", "elevated": -1 }] }""")]
    [InlineData("""{ "gases": [{ "name": null }] }""")]
    [InlineData("""{ "gases": [{ "unit": "ppm" }] }""")]
    public void A_hostile_gas_fails_only_with_an_exception_the_join_catches(string json)
    {
        var ex = Record.Exception(() => Parse(json));

        Assert.True(ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException, ex?.GetType().Name);
    }

    [Fact]
    public void An_unknown_direction_reads_as_rising()
    {
        Assert.Equal(GasDirection.Rising, Assert.Single(Parse("""{ "gases": [{ "name": "CO", "direction": "seitwärts" }] }""").Gases).Direction);
    }

    [Fact]
    public void Gases_round_trip_through_the_file_format()
    {
        var set = MasterDataSet.Empty with
        {
            Gases = [GasDefinition.DefaultCo, new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m)],
        };

        Assert.Equal(set.Gases, Parse(MasterDataJson.Serialize(set)).Gases);
    }

    [Fact]
    public void Gases_are_not_content_that_makes_the_stammdaten_non_empty()
    {
        Assert.True((MasterDataSet.Empty with { Gases = [GasDefinition.DefaultCo] }).IsEmpty);
    }

    [Fact]
    public void Gases_round_trip_through_the_store_in_their_order_with_decimals()
    {
        var gases = new[]
        {
            new GasDefinition("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m),
            new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19.5m, 17m, 10m, null),
            GasDefinition.DefaultCo,
        };

        MasterDataStore.Save(_path, MasterDataSet.Empty with { Gases = gases });

        Assert.Equal(gases, MasterDataStore.GetOrCreate(_path).Gases);
    }

    // #927: the enrichment limit of a falling gas.
    [Fact]
    public void A_falling_gas_reads_its_upper_alarm_from_the_file()
    {
        var gas = Assert.Single(Parse("""{ "gases": [{ "name": "O2", "direction": "fallend", "elevated": 19, "plausibleUpTo": 25, "upperAlarm": 23 }] }""").Gases);

        Assert.Equal(23m, gas.UpperAlarm);
    }

    [Fact]
    public void An_upper_alarm_on_a_rising_gas_is_dropped_rather_than_rejected()
    {
        var gas = Assert.Single(Parse("""{ "gases": [{ "name": "CO", "elevated": 30, "upperAlarm": 5 }] }""").Gases);

        Assert.Null(gas.UpperAlarm);
        Assert.Equal(30m, gas.Elevated);
    }

    [Theory]
    [InlineData("19")]
    [InlineData("26")]
    public void An_upper_alarm_out_of_order_rejects_the_file(string upper)
    {
        var ex = Assert.Throws<JsonException>(() => Parse(
            $$"""{ "gases": [{ "name": "O2", "direction": "fallend", "elevated": 19, "plausibleUpTo": 25, "upperAlarm": {{upper}} }] }"""));

        Assert.Contains("O2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hostile_upper_alarm_fails_only_with_an_exception_the_join_catches()
    {
        Assert.Throws<FormatException>(() => Parse("""{ "gases": [{ "name": "O2", "direction": "fallend", "upperAlarm": "23" }] }"""));
        Assert.Throws<JsonException>(() => Parse("""{ "gases": [{ "name": "O2", "direction": "fallend", "upperAlarm": 1e9 }] }"""));
    }

    [Fact]
    public void The_upper_alarm_round_trips_through_the_file_format_and_the_store()
    {
        var gases = new[]
        {
            new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19m, 17m, 10m, 25m, UpperAlarm: 23.5m),
            GasDefinition.DefaultCo,
        };
        var set = MasterDataSet.Empty with { Gases = gases };

        Assert.Equal(gases, Parse(MasterDataJson.Serialize(set)).Gases);
        Assert.Contains("\"upperAlarm\": 23.5", MasterDataJson.Serialize(set), StringComparison.Ordinal);
        MasterDataStore.Save(_path, set);
        Assert.Equal(gases, MasterDataStore.GetOrCreate(_path).Gases);
    }

    [Fact]
    public void A_store_from_before_the_upper_alarm_gains_the_column_and_reads_none()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with { Gases = [new GasDefinition("O2", "Vol-%", GasDirection.Falling, 19m, null, null, null, UpperAlarm: 23m)] });
        using (var cn = new SqliteConnection($"Data Source={_path}"))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "ALTER TABLE md_gases DROP COLUMN upper_alarm;";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var gas = Assert.Single(MasterDataStore.GetOrCreate(_path).Gases);
        Assert.Equal(("O2", 19m), (gas.Name, gas.Elevated));
        Assert.Null(gas.UpperAlarm);
    }

    [Fact]
    public void A_store_without_gases_reads_as_CO_alone()
    {
        Assert.Equal(GasDefinition.DefaultCo, Assert.Single(MasterDataStore.GetOrCreate(_path).Gases));
    }
}
