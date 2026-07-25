using System.Text;
using System.Text.Json;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.Persistence.Tests;

public class MasterDataJsonTests
{
    private static MasterDataSet Parse(string json) =>
        MasterDataJson.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Parse_reads_every_category_from_a_full_file()
    {
        var set = Parse("""
            {
              "roles": ["EL", "ZF"],
              "status": ["aufgenommen"],
              "unitStatus": ["Alarmiert"],
              "equipment": ["Mobilteil 1"],
              "districts": ["FFB"],
              "radioCallSigns": ["Land 1"],
              "brigades": ["FFB Wache 1"],
              "truppTypes": ["Angriffstrupp"],
              "checklistTemplate": ["Schritt 1"],
              "streets": [{ "name": "Bahnhofstr.", "district": "FFB" }],
              "personnel": [{ "lastName": "Mustermann", "firstName": "Max", "role": "ZF", "callSign": "Land 1", "phone": "0171" }]
            }
            """);

        Assert.Equal(new[] { "EL", "ZF" }, set.Roles);
        Assert.Equal(new[] { "Alarmiert" }, set.UnitStatus);
        Assert.Contains(set.Streets, s => s.Name == "Bahnhofstr." && s.District == "FFB");
        var max = set.Personnel.Single();
        Assert.Equal("Max", max.FirstName);
        Assert.Equal("Land 1", max.CallSign);
    }

    [Fact]
    public void Parse_treats_missing_keys_as_empty_categories()
    {
        var set = Parse("""{ "roles": ["EL"] }""");
        Assert.Equal(new[] { "EL" }, set.Roles);
        Assert.Empty(set.Status);
        Assert.Empty(set.Streets);
        Assert.Empty(set.Personnel);
    }

    [Fact]
    public void Parse_accepts_a_personnel_only_file()
    {
        var set = Parse("""{ "personnel": [{ "lastName": "Musterfrau", "firstName": "Erika" }] }""");
        Assert.Empty(set.Roles);
        Assert.Equal("Musterfrau", set.Personnel.Single().LastName);
    }

    [Fact]
    public void Parse_personnel_optional_fields_default_to_null()
    {
        var set = Parse("""{ "personnel": [{ "lastName": "Musterfrau", "firstName": "Erika" }] }""");
        var p = set.Personnel.Single();
        Assert.Null(p.Role);
        Assert.Null(p.CallSign);
        Assert.Null(p.Phone);
    }

    [Fact]
    public void Parse_throws_on_malformed_json()
        => Assert.ThrowsAny<JsonException>(() => Parse("{ not valid"));

    [Fact]
    public void Serialize_round_trips_through_Parse()
    {
        var original = MasterDataSet.Empty with
        {
            Roles = new[] { "EL", "ZF" },
            UnitStatus = new[] { "Alarmiert", "Im Einsatz" },
            Streets = new[] { new Street("Bahnhofstr.", "FFB") },
            ChecklistTemplate = new[] { "Ä ö ü / ß Schritt" }, // relaxed escaping must survive the round trip
            Personnel = new[]
            {
                new Person("Mustermann", "Max", "ZF", "Land 1", "0171"),
                new Person("Musterfrau", "Erika", null, null, null),
            },
        };

        var reparsed = Parse(MasterDataJson.Serialize(original));

        Assert.Equal(original.Roles, reparsed.Roles);
        Assert.Equal(original.UnitStatus, reparsed.UnitStatus);
        Assert.Equal(original.ChecklistTemplate, reparsed.ChecklistTemplate);
        Assert.Equal(original.Streets, reparsed.Streets);
        Assert.Equal(original.Personnel, reparsed.Personnel);
    }

    [Fact]
    public void IsEmpty_is_true_only_when_no_category_has_content()
    {
        Assert.True(MasterDataSet.Empty.IsEmpty);
        Assert.False((MasterDataSet.Empty with { Roles = new[] { "EL" } }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Personnel = new[] { new Person("X", "Y", null, null, null) } }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Streets = new[] { new Street("S", "D") } }).IsEmpty);
    }
}
