using System.Text;
using System.Text.Json;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Persistence.Tests;

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
              "einsatzarten": ["B", "THL"],
              "checklistTemplateAufbau": [{ "text": "Schritt 1", "mandatory": true }],
              "checklistTemplateAbbau": [{ "text": "Abbauschritt", "mandatory": false }],
              "streets": [{ "name": "Bahnhofstr.", "district": "FFB" }],
              "links": [{ "name": "Wetterdienst", "url": "https://dwd.de" }],
              "personnel": [{ "lastName": "Mustermann", "firstName": "Max", "role": "ZF", "callSign": "Land 1", "phone": "0171" }]
            }
            """);

        Assert.Equal(new[] { "EL", "ZF" }, set.Roles);
        Assert.Equal(new[] { "Alarmiert" }, set.UnitStatus);
        Assert.Equal(new[] { "B", "THL" }, set.Einsatzarten);
        Assert.Equal(new ChecklistTemplateItem("Schritt 1", true), Assert.Single(set.ChecklistTemplateAufbau));
        Assert.Equal(new ChecklistTemplateItem("Abbauschritt", false), Assert.Single(set.ChecklistTemplateAbbau));
        Assert.Contains(set.Streets, s => s.Name == "Bahnhofstr." && s.District == "FFB");
        Assert.Equal(new Link("Wetterdienst", "https://dwd.de"), Assert.Single(set.Links));
        var max = set.Personnel.Single();
        Assert.Equal("Max", max.FirstName);
        Assert.Equal("Land 1", max.CallSign);
    }

    [Fact]
    public void Parse_maps_the_legacy_flat_checklistTemplate_array_to_optional_aufbau_items()
    {
        var set = Parse("""{ "checklistTemplate": ["Schritt 1", "Schritt 2"] }""");

        Assert.Equal(
            new[] { new ChecklistTemplateItem("Schritt 1", false), new ChecklistTemplateItem("Schritt 2", false) },
            set.ChecklistTemplateAufbau);
        Assert.Empty(set.ChecklistTemplateAbbau);
    }

    [Fact]
    public void Parse_prefers_the_split_keys_over_the_legacy_flat_array_when_both_are_present()
    {
        var set = Parse("""
            {
              "checklistTemplate": ["Ignoriert"],
              "checklistTemplateAufbau": [{ "text": "Neu", "mandatory": true }]
            }
            """);

        Assert.Equal(new ChecklistTemplateItem("Neu", true), Assert.Single(set.ChecklistTemplateAufbau));
    }

    [Fact]
    public void Parse_treats_missing_keys_as_empty_categories()
    {
        var set = Parse("""{ "roles": ["EL"] }""");
        Assert.Equal(new[] { "EL" }, set.Roles);
        Assert.Empty(set.Status);
        Assert.Empty(set.Streets);
        Assert.Empty(set.Links);
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
            Einsatzarten = new[] { "B", "THL", "R" },
            Streets = new[] { new Street("Bahnhofstr.", "FFB") },
            Links = new[] { new Link("Ä ö ü Dienst", "https://example.org/ä") },
            // relaxed escaping must survive the round trip
            ChecklistTemplateAufbau = new[] { new ChecklistTemplateItem("Ä ö ü / ß Schritt", true) },
            ChecklistTemplateAbbau = new[] { new ChecklistTemplateItem("Abbau Ä ö ü", false) },
            Personnel = new[]
            {
                new Person("Mustermann", "Max", "ZF", "Land 1", "0171"),
                new Person("Musterfrau", "Erika", null, null, null),
            },
        };

        var reparsed = Parse(MasterDataJson.Serialize(original));

        Assert.Equal(original.Roles, reparsed.Roles);
        Assert.Equal(original.UnitStatus, reparsed.UnitStatus);
        Assert.Equal(original.ChecklistTemplateAufbau, reparsed.ChecklistTemplateAufbau);
        Assert.Equal(original.ChecklistTemplateAbbau, reparsed.ChecklistTemplateAbbau);
        Assert.Equal(original.Einsatzarten, reparsed.Einsatzarten);
        Assert.Equal(original.Streets, reparsed.Streets);
        Assert.Equal(original.Links, reparsed.Links);
        Assert.Equal(original.Personnel, reparsed.Personnel);
    }

    // #76: vehicles hang off their Wache with a seat count, so the Kräfte entry can offer the
    // Funkrufname and a Stärke preset per selected Wache.
    [Fact]
    public void Parse_reads_vehicles_with_wache_callsign_and_seats()
    {
        var set = Parse("""
            {
              "vehicles": [
                { "wache": "FFB Wache 1", "callSign": "FFB 1/40/1", "seats": 9 },
                { "wache": "Aich", "callSign": "Aich 42/1", "seats": 6 }
              ]
            }
            """);

        Assert.Equal(
            new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9), new Vehicle("Aich", "Aich 42/1", 6) },
            set.Vehicles);
    }

    [Fact]
    public void A_file_without_vehicles_parses_as_an_empty_list()
    {
        var set = Parse("""{ "brigades": ["FFB Wache 1"] }""");
        Assert.Empty(set.Vehicles);
    }

    [Fact]
    public void Serialize_round_trips_vehicles()
    {
        var original = MasterDataSet.Empty with
        {
            Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
        };

        var reparsed = Parse(MasterDataJson.Serialize(original));

        Assert.Equal(original.Vehicles, reparsed.Vehicles);
    }

    [Fact]
    public void Parse_reads_the_settings_object()
    {
        var set = Parse("""
            {
              "settings": {
                "ilsReminderIntervalMinutes": 12,
                "ilsReminderFollowUpIntervalMinutes": 33,
                "agtMaxDurationMinutes": 25,
                "csaMaxDurationMinutes": 18,
                "lpaMaxDurationMinutes": 40,
                "pressureControlIntervalMinutes": 4,
                "returnPressureBar": 55
              }
            }
            """);

        Assert.Equal(new IncidentSettings(12, 33, 25, 18, 40, 4, 55), set.Settings);
    }

    [Fact]
    public void Parse_uses_default_settings_when_the_object_is_absent()
        => Assert.Equal(IncidentSettings.Defaults, Parse("""{ "roles": ["EL"] }""").Settings);

    [Fact]
    public void Parse_fills_missing_settings_fields_from_the_defaults()
    {
        var set = Parse("""{ "settings": { "agtMaxDurationMinutes": 25 } }""");

        Assert.Equal(25, set.Settings.AgtMaxDurationMinutes);
        Assert.Equal(IncidentSettings.Defaults.IlsReminderFollowUpIntervalMinutes, set.Settings.IlsReminderFollowUpIntervalMinutes);
        Assert.Equal(IncidentSettings.Defaults.LpaMaxDurationMinutes, set.Settings.LpaMaxDurationMinutes);
        Assert.Equal(IncidentSettings.Defaults.ReturnPressureBar, set.Settings.ReturnPressureBar);
        Assert.Equal(IncidentSettings.Defaults.IlsReminderIntervalMinutes, set.Settings.IlsReminderIntervalMinutes);
    }

    [Fact]
    public void Serialize_round_trips_settings()
    {
        var original = MasterDataSet.Empty with { Settings = new IncidentSettings(12, 33, 25, 18, 40, 4, 55) };

        Assert.Equal(original.Settings, Parse(MasterDataJson.Serialize(original)).Settings);
    }

    [Fact]
    public void Parse_reads_the_einsatzgebiet_object()
    {
        var set = Parse("""
            {
              "einsatzgebiet": { "name": "Landkreis Fürstenfeldbruck", "folderPath": "/data/ffb" }
            }
            """);

        Assert.Equal(new Einsatzgebiet("Landkreis Fürstenfeldbruck", "/data/ffb"), set.Einsatzgebiet);
    }

    [Fact]
    public void Parse_uses_an_empty_einsatzgebiet_when_the_object_is_absent()
        => Assert.Equal(Einsatzgebiet.Empty, Parse("""{ "roles": ["EL"] }""").Einsatzgebiet);

    [Fact]
    public void Serialize_round_trips_the_einsatzgebiet()
    {
        var original = MasterDataSet.Empty with { Einsatzgebiet = new Einsatzgebiet("FFB", "/data/ffb") };

        Assert.Equal(original.Einsatzgebiet, Parse(MasterDataJson.Serialize(original)).Einsatzgebiet);
    }

    [Fact]
    public void IsEmpty_is_true_only_when_no_category_has_content()
    {
        Assert.True(MasterDataSet.Empty.IsEmpty);
        // Settings always carry values, so they must not count toward emptiness (else Import hides).
        Assert.True((MasterDataSet.Empty with { Settings = new IncidentSettings(1, 2, 3, 4, 5, 6, 7) }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Roles = new[] { "EL" } }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Personnel = new[] { new Person("X", "Y", null, null, null) } }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Streets = new[] { new Street("S", "D") } }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Links = new[] { new Link("N", "U") } }).IsEmpty);
    }
}
