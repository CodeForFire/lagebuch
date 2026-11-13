using System.Text;
using System.Text.Json;
using LageBuch.Persistence.MasterData;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

// The Fahrzeugkennungen: the middle number of a Funkrufname mapped to a Fahrzeugtyp, so a vehicle
// gets its type from "FFB 1/12/1" instead of being picked from a list one by one.
public sealed class VehicleTypeCodesTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"md-codes-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static MasterDataSet Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return MasterDataJson.Parse(stream);
    }

    [Fact]
    public void Parse_reads_the_kennungen_trimmed()
    {
        var set = Parse("""{ "vehicleTypeCodes": [{ "kennung": " 12 ", "vehicleType": " MZF " }] }""");

        Assert.Equal(new VehicleTypeCode("12", "MZF"), Assert.Single(set.VehicleTypeCodes));
    }

    [Fact]
    public void A_file_without_kennungen_reads_as_none()
    {
        var set = Parse("""{ "roles": ["EL"] }""");

        Assert.Empty(set.VehicleTypeCodes);
    }

    [Theory]
    [InlineData("""{ "vehicleTypeCodes": 5 }""")]
    [InlineData("""{ "vehicleTypeCodes": { "kennung": "12" } }""")]
    public void Kennungen_that_are_no_list_read_as_none(string json)
    {
        Assert.Empty(Parse(json).VehicleTypeCodes);
    }

    // A hand-edited file is hostile input: the fields are required and capped.
    [Theory]
    [InlineData("""{ "vehicleTypeCodes": [{ "vehicleType": "MZF" }] }""")]
    [InlineData("""{ "vehicleTypeCodes": [{ "kennung": "12" }] }""")]
    public void Parse_refuses_a_kennung_without_both_fields(string json)
    {
        Assert.Throws<KeyNotFoundException>(() => Parse(json));
    }

    [Theory]
    [InlineData("""{ "vehicleTypeCodes": [{ "kennung": null, "vehicleType": "MZF" }] }""", "Fahrzeugkennung: \"kennung\" darf nicht null sein.")]
    [InlineData("""{ "vehicleTypeCodes": [{ "kennung": "12", "vehicleType": null }] }""", "Fahrzeugkennung: \"vehicleType\" darf nicht null sein.")]
    public void Parse_refuses_a_null_field_and_says_which(string json, string message)
    {
        Assert.Equal(message, Assert.Throws<JsonException>(() => Parse(json)).Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Parse_refuses_a_kennung_over_the_cap(bool longKennung, bool longType)
    {
        var tooLong = new string('1', VehicleTypeCode.MaxLength + 1);
        var json = $$"""{ "vehicleTypeCodes": [{ "kennung": "{{(longKennung ? tooLong : "12")}}", "vehicleType": "{{(longType ? tooLong : "MZF")}}" }] }""";

        Assert.Equal(
            $"Fahrzeugkennung: höchstens {VehicleTypeCode.MaxLength} Zeichen.",
            Assert.Throws<JsonException>(() => Parse(json)).Message);
    }

    [Fact]
    public void A_kennung_at_the_cap_is_read()
    {
        var atCap = new string('1', VehicleTypeCode.MaxLength);
        var json = $$"""{ "vehicleTypeCodes": [{ "kennung": "{{atCap}}", "vehicleType": "{{atCap}}" }] }""";

        var read = Assert.Single(Parse(json).VehicleTypeCodes);

        Assert.Equal(atCap, read.Kennung);
        Assert.Equal(atCap, read.VehicleType);
    }

    [Fact]
    public void The_cap_is_the_one_a_fahrzeugtyp_has_everywhere()
    {
        Assert.Equal(Domain.VehicleTypes.MaxLength, VehicleTypeCode.MaxLength);
    }

    [Fact]
    public void Serialize_writes_the_kennungen_back()
    {
        var set = MasterDataSet.Empty with { VehicleTypeCodes = [new VehicleTypeCode("12", "MZF")] };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MasterDataJson.Serialize(set)));
        Assert.Equal(set.VehicleTypeCodes, MasterDataJson.Parse(stream).VehicleTypeCodes);
    }

    [Fact]
    public void Kennungen_alone_make_the_stammdaten_non_empty()
    {
        Assert.False((MasterDataSet.Empty with { VehicleTypeCodes = [new VehicleTypeCode("12", "MZF")] }).IsEmpty);
    }

    [Fact]
    public void The_store_keeps_the_kennungen_in_order_and_a_second_save_replaces_them()
    {
        MasterDataStore.Save(_path, MasterDataSet.Empty with { VehicleTypeCodes = [new VehicleTypeCode("12", "MZF")] });
        MasterDataStore.Save(_path, MasterDataSet.Empty with { VehicleTypeCodes = [new VehicleTypeCode("44", "LF 20"), new VehicleTypeCode("11", "ELW 1")] });

        Assert.Equal(
            [new VehicleTypeCode("44", "LF 20"), new VehicleTypeCode("11", "ELW 1")],
            MasterDataStore.GetOrCreate(_path).VehicleTypeCodes);
    }

    private static readonly VehicleTypeCode[] Codes =
    [
        new("10", "KdoW"),
        new("12", "mzf"),
        new("44", "LF 20"),
        new("44", "HLF 20"),
        new(" 11 ", " elw 1 "),
    ];

    [Theory]
    [InlineData("FFB 1/12/1", "MZF")] // the catalogue's spelling, as the Fahrzeuge store it
    [InlineData("Florian Emmering 1/44/1", "LF 20")] // the first entry for a Kennung wins
    [InlineData("FFB 2/10/1", "KdoW")]
    [InlineData("FFB 2/11/1", "ELW 1")]
    public void A_funkrufname_finds_the_type_of_its_kennung(string callSign, string type)
    {
        Assert.Equal(type, VehicleTypeCode.TypeFor(callSign, Codes));
    }

    [Theory]
    [InlineData("FFB 1/99/1")] // a Kennung nobody listed
    [InlineData("Florian Musterdorf 40/1")] // no Kennung to read
    [InlineData("")]
    [InlineData(null)]
    public void A_funkrufname_without_a_listed_kennung_finds_no_type(string? callSign)
    {
        Assert.Null(VehicleTypeCode.TypeFor(callSign, Codes));
    }

    [Fact]
    public void An_entry_without_a_type_finds_none()
    {
        Assert.Null(VehicleTypeCode.TypeFor("FFB 1/12/1", [new VehicleTypeCode("12", "  ")]));
    }
}
