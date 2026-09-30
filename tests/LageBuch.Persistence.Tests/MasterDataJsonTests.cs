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
              "unitStatus": ["Alarmiert"],
              "truppTypes": [{ "name": "Angriffstrupp", "memberCount": 2, "maxDurationMinutes": 30 }],
              "checklistTemplateAufbau": [{ "text": "Schritt 1", "mandatory": true }],
              "checklistTemplateAbbau": [{ "text": "Abbauschritt", "mandatory": false }],
              "links": [{ "name": "Wetterdienst", "url": "https://dwd.de" }],
              "personnel": [{ "lastName": "Mustermann", "firstName": "Max", "role": "ZF", "callSign": "Land 1", "phone": "0171" }]
            }
            """);

        Assert.Equal(new[] { new Role("EL"), new Role("ZF") }, set.Roles);
        Assert.Equal(new[] { "Alarmiert" }, set.UnitStatus);
        Assert.Equal(new TruppType("Angriffstrupp", 2, 30), Assert.Single(set.TruppTypes));
        Assert.Equal(new ChecklistTemplateItem("Schritt 1", true), Assert.Single(set.ChecklistTemplates[0].Items));
        Assert.Equal(new ChecklistTemplateItem("Abbauschritt", false), Assert.Single(set.ChecklistTemplates[1].Items));
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
            Assert.Single(set.ChecklistTemplates).Items);
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

        Assert.Equal(new ChecklistTemplateItem("Neu", true), Assert.Single(set.ChecklistTemplates[0].Items));
    }

    [Fact]
    public void Parse_treats_missing_keys_as_empty_categories()
    {
        var set = Parse("""{ "roles": ["EL"] }""");
        Assert.Equal(new[] { new Role("EL") }, set.Roles);
        Assert.Empty(set.UnitStatus);
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
            Roles = new[] { new Role("EL"), new Role("ZF") },
            UnitStatus = new[] { "Alarmiert", "Im Einsatz" },
            Links = new[] { new Link("Ä ö ü Dienst", "https://example.org/ä"), new Link("ERICard", "https://example.org/ericard", "Gefahrgut") },

            // relaxed escaping must survive the round trip
            ChecklistTemplates = ChecklistTemplate.AufbauAbbau(
                new[] { new ChecklistTemplateItem("Ä ö ü / ß Schritt", true) },
                new[] { new ChecklistTemplateItem("Abbau Ä ö ü", false) }),
            Personnel = new[]
            {
                new Person("Mustermann", "Max", "ZF", "Land 1", "0171", true, "max@example.org", "KBM Gefahrgut"),
                new Person("Musterfrau", "Erika", null, null, null, true, null, null),
            },
        };

        var reparsed = Parse(MasterDataJson.Serialize(original));

        Assert.Equal(original.Roles, reparsed.Roles);
        Assert.Equal(original.UnitStatus, reparsed.UnitStatus);
        Assert.Equal(original.ChecklistTemplates[0].Items, reparsed.ChecklistTemplates[0].Items);
        Assert.Equal(original.ChecklistTemplates[1].Items, reparsed.ChecklistTemplates[1].Items);
        Assert.Equal(original.Links, reparsed.Links);
        Assert.Equal(original.Personnel, reparsed.Personnel);
    }

    [Fact]
    public void Parse_reads_a_link_group_trimmed()
    {
        var set = Parse("""{ "links": [{ "name": "ERICard", "url": "https://example.org/ericard", "group": " Gefahrgut " }] }""");

        Assert.Equal(new Link("ERICard", "https://example.org/ericard", "Gefahrgut"), Assert.Single(set.Links));
    }

    [Theory]
    [InlineData("""{ "links": [{ "name": "Wetterdienst", "url": "https://dwd.de" }] }""")]
    [InlineData("""{ "links": [{ "name": "Wetterdienst", "url": "https://dwd.de", "group": null }] }""")]
    [InlineData("""{ "links": [{ "name": "Wetterdienst", "url": "https://dwd.de", "group": 42 }] }""")]
    public void Parse_reads_a_missing_or_unusable_link_group_as_ungrouped(string json)
    {
        // A file or sync host from before #518 carries no group; a hand-edited one may carry
        // anything. Neither is a reason to refuse the whole Stammdaten set.
        Assert.Equal(string.Empty, Assert.Single(Parse(json).Links).Group);
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
        var set = Parse("""{ "roles": ["EL"] }""");
        Assert.Empty(set.Vehicles);
        Assert.Empty(set.Brigades);
        Assert.Empty(set.RadioCallSigns);
    }

    // Wachen and Funkrufnamen are derived from the Fahrzeuge (and Personal) rather than kept as
    // separate lists, so maintaining vehicles alone is sufficient and nothing can drift apart.
    [Fact]
    public void Brigades_are_the_distinct_vehicle_waches_in_first_seen_order()
    {
        var set = MasterDataSet.Empty with
        {
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
                new Vehicle("Aich", "Aich 42/1", 6),
                new Vehicle("ffb wache 1", "FFB ELW 1", 4, HasZugfuehrer: true),
                new Vehicle(" Puch ", "Puch 40/1", 9),
            },
        };

        Assert.Equal(new[] { "FFB Wache 1", "Aich", "Puch" }, set.Brigades);
    }

    [Fact]
    public void RadioCallSigns_are_vehicle_callsigns_followed_by_personal_callsigns_distinct()
    {
        var set = MasterDataSet.Empty with
        {
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
                new Vehicle("Aich", "Aich 42/1", 6),
            },
            Personnel = new[]
            {
                new Person("Mustermann", "Max", "ZF", "Land 1", null),
                new Person("Musterfrau", "Erika", "GF", null, null),
                new Person("Muster", "Moritz", "GF", "  ", null),
                new Person("Doppelt", "Dora", "GF", "aich 42/1", null),
            },
        };

        Assert.Equal(new[] { "FFB 1/40/1", "Aich 42/1", "Land 1" }, set.RadioCallSigns);
    }

    [Fact]
    public void Parse_ignores_the_legacy_brigades_and_radioCallSigns_keys()
    {
        var set = Parse("""
            {
              "brigades": ["Alt-Wache"],
              "radioCallSigns": ["Leitstelle"],
              "vehicles": [{ "wache": "FFB Wache 1", "callSign": "FFB 1/40/1", "seats": 9 }]
            }
            """);

        Assert.Equal(new[] { "FFB Wache 1" }, set.Brigades);
        Assert.Equal(new[] { "FFB 1/40/1" }, set.RadioCallSigns);
    }

    [Fact]
    public void Serialize_writes_no_brigades_or_radioCallSigns_keys()
    {
        var json = MasterDataJson.Serialize(MasterDataSet.Empty with
        {
            Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
        });

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("brigades", out _));
        Assert.False(doc.RootElement.TryGetProperty("radioCallSigns", out _));
    }

    [Fact]
    public void ParseForImport_reports_legacy_entries_that_no_vehicle_or_person_covers()
    {
        var result = MasterDataJson.ParseForImport(new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "brigades": ["FFB Wache 1", "Alt-Wache", " ffb wache 1 "],
              "radioCallSigns": ["FFB 1/40/1", "Land 1", "Leitstelle"],
              "vehicles": [{ "wache": "FFB Wache 1", "callSign": "FFB 1/40/1", "seats": 9 }],
              "personnel": [{ "lastName": "Mustermann", "firstName": "Max", "callSign": "Land 1" }]
            }
            """)));

        Assert.Equal(new[] { "FFB Wache 1" }, result.Set.Brigades);
        Assert.Equal(new[] { "Alt-Wache", "Leitstelle" }, result.DroppedLegacyEntries);
    }

    [Fact]
    public void ParseForImport_reports_nothing_for_a_current_format_file()
    {
        var result = MasterDataJson.ParseForImport(new MemoryStream(Encoding.UTF8.GetBytes("""
            { "vehicles": [{ "wache": "FFB Wache 1", "callSign": "FFB 1/40/1", "seats": 9 }] }
            """)));

        Assert.Empty(result.DroppedLegacyEntries);
    }

    /// <summary>ZF vehicles are command vehicles (ELW/KdoW), not seat-derived like Officer/Mannschaft.</summary>
    [Fact]
    public void Parse_reads_hasZugfuehrer_and_defaults_a_missing_field_to_false()
    {
        var set = Parse("""
            {
              "vehicles": [
                { "wache": "FFB Wache 1", "callSign": "FFB ELW 1", "seats": 4, "hasZugfuehrer": true },
                { "wache": "FFB Wache 1", "callSign": "FFB 1/40/1", "seats": 9 }
              ]
            }
            """);

        Assert.Equal(
            new[]
            {
                new Vehicle("FFB Wache 1", "FFB ELW 1", 4, HasZugfuehrer: true),
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
            },
            set.Vehicles);
    }

    /// <summary>
    /// Everything written before #458 is the brigade's own, so a missing isOwn must read as true --
    /// the opposite of hasZugfuehrer's default.
    /// </summary>
    [Fact]
    public void Parse_reads_isOwn_and_defaults_a_missing_field_to_true()
    {
        var set = Parse("""
            {
              "vehicles": [
                { "wache": "FF Nachbarort", "callSign": "Florian Nachbarort 40/1", "seats": 9, "isOwn": false },
                { "wache": "FFB Wache 1", "callSign": "FFB 1/40/1", "seats": 9 }
              ],
              "personnel": [
                { "lastName": "Nachbar", "firstName": "Nora", "isOwn": false },
                { "lastName": "Mustermann", "firstName": "Max" }
              ]
            }
            """);

        Assert.Equal(
            new[]
            {
                new Vehicle("FF Nachbarort", "Florian Nachbarort 40/1", 9, IsOwn: false),
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
            },
            set.Vehicles);
        Assert.Equal(
            new[]
            {
                new Person("Nachbar", "Nora", null, null, null, IsOwn: false),
                new Person("Mustermann", "Max", null, null, null),
            },
            set.Personnel);
    }

    [Fact]
    public void Serialize_round_trips_vehicles()
    {
        var original = MasterDataSet.Empty with
        {
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB 1/40/1", 9),
                new Vehicle("FFB Wache 1", "FFB ELW 1", 4, HasZugfuehrer: true),
                new Vehicle("FF Nachbarort", "Florian Nachbarort 40/1", 9, IsOwn: false),
            },
        };

        var reparsed = Parse(MasterDataJson.Serialize(original));

        Assert.Equal(original.Vehicles, reparsed.Vehicles);
    }

    [Fact]
    public void Serialize_round_trips_the_own_flag_of_personnel()
    {
        var original = MasterDataSet.Empty with
        {
            Personnel = new[]
            {
                new Person("Mustermann", "Max", "ZF", null, null),
                new Person("Nachbar", "Nora", null, null, null, IsOwn: false),
            },
        };

        var reparsed = Parse(MasterDataJson.Serialize(original));

        Assert.Equal(original.Personnel, reparsed.Personnel);
    }

    [Fact]
    public void Parse_reads_the_settings_object()
    {
        var set = Parse("""
            {
              "settings": {
                "ilsReminderIntervalMinutes": 12,
                "ilsReminderFollowUpIntervalMinutes": 33,
                "returnPressureBar": 55
              }
            }
            """);

        Assert.Equal(new IncidentSettings(12, 33, 55), set.Settings);
    }

    [Fact]
    public void Parse_uses_default_settings_when_the_object_is_absent()
        => Assert.Equal(IncidentSettings.Defaults, Parse("""{ "roles": ["EL"] }""").Settings);

    [Fact]
    public void Parse_fills_missing_settings_fields_from_the_defaults()
    {
        var set = Parse("""{ "settings": { "returnPressureBar": 25 } }""");

        Assert.Equal(25, set.Settings.ReturnPressureBar);
        Assert.Equal(IncidentSettings.Defaults.IlsReminderFollowUpIntervalMinutes, set.Settings.IlsReminderFollowUpIntervalMinutes);
        Assert.Equal(IncidentSettings.Defaults.IlsReminderIntervalMinutes, set.Settings.IlsReminderIntervalMinutes);
    }

    [Fact]
    public void Parse_maps_a_legacy_bare_string_trupp_type_the_way_the_store_migration_does()
    {
        // A file exported before #398 lists names only, because the crew size and Einsatzzeit were
        // still decided by comparing those names against compiled-in literals. Both entrances --
        // this parser and MasterDataStore's widening -- must translate them identically, or a
        // brigade restoring from a JSON backup would get different rules than one just reopening
        // its masterdata.db. See MasterDataStoreTests for the other half of this pair.
        // No settings object here, so each falls back to what IncidentSettings used to default to.
        var set = Parse("""
            { "truppTypes": ["Angriffstrupp", "CSA-Trupp", "LPA-Trupp", " csa-trupp "] }
            """);

        Assert.Equal(
            new[]
            {
                new TruppType("Angriffstrupp", 2, 30),
                new TruppType("CSA-Trupp", 3, 20),
                new TruppType("LPA-Trupp", 2, 60),

                // Trimmed and case-insensitive, matching what the old runtime rule accepted.
                new TruppType("csa-trupp", 3, 20),
            },
            set.TruppTypes);
    }

    [Fact]
    public void A_legacy_file_keeps_the_einsatzzeiten_it_had_configured()
    {
        // The same document that still lists bare names also still carries that brigade's own
        // Einsatzzeiten. ParseSettings no longer reads them -- they left IncidentSettings -- but the
        // Trupp-Typ translation must, or restoring a backup silently lengthens the CSA countdown.
        // Mirrors MasterDataStoreTests.The_migration_carries_over_the_einsatzzeiten_the_brigade_had_configured:
        // the two entrances have to agree, and agreeing on the wrong number is not agreement.
        var set = Parse("""
            {
              "truppTypes": ["Angriffstrupp", "CSA-Trupp", "LPA-Trupp"],
              "settings": {
                "agtMaxDurationMinutes": 25,
                "csaMaxDurationMinutes": 15,
                "lpaMaxDurationMinutes": 45
              }
            }
            """);

        Assert.Equal(
            new[]
            {
                new TruppType("Angriffstrupp", 2, 25),
                new TruppType("CSA-Trupp", 3, 15),
                new TruppType("LPA-Trupp", 2, 45),
            },
            set.TruppTypes);
    }

    [Fact]
    public void Parse_clamps_an_unrunnable_einsatzzeit_rather_than_throwing()
    {
        // Same trust boundary as the crew size, and the same reason not to throw. Left unclamped,
        // a zero would reach AtemschutzTrupp.Register's ThrowIfNegativeOrZero out of a
        // fire-and-forget mutation and take the UI down -- the #217 crash shape.
        var set = Parse("""
            {
              "truppTypes": [
                { "name": "Null", "maxDurationMinutes": 0 },
                { "name": "Negativ", "maxDurationMinutes": -5 },
                { "name": "Lang", "maxDurationMinutes": 240 }
              ]
            }
            """);

        // No ceiling on the last one: a four-hour LPA is a real thing and must survive untouched.
        Assert.Equal(new[] { 1, 1, 240 }, set.TruppTypes.Select(t => t.MaxDurationMinutes));
    }

    [Fact]
    public void Parse_clamps_an_impossible_crew_size_rather_than_throwing()
    {
        // This runs on a payload that crossed a trust boundary, and HomeViewModel catches only the
        // JSON exception types around a sync host's /masterdata -- throwing here would escape that
        // catch and leak the open hub connection instead of failing the join cleanly.
        var set = Parse("""
            {
              "truppTypes": [
                { "name": "Zu gross", "memberCount": 9 },
                { "name": "Zu klein", "memberCount": 1 }
              ]
            }
            """);

        Assert.Equal(new[] { 3, 2 }, set.TruppTypes.Select(t => t.MemberCount));
    }

    [Fact]
    public void Serialize_round_trips_trupp_types()
    {
        var original = MasterDataSet.Empty with
        {
            TruppTypes = new[] { new TruppType("Angriffstrupp"), new TruppType("Chemietrupp", 3, 20) },
        };

        Assert.Equal(original.TruppTypes, Parse(MasterDataJson.Serialize(original)).TruppTypes);
    }

    [Fact]
    public void Serialize_round_trips_settings()
    {
        var original = MasterDataSet.Empty with { Settings = new IncidentSettings(12, 33, 55) };

        Assert.Equal(original.Settings, Parse(MasterDataJson.Serialize(original)).Settings);
    }

    [Fact]
    public void IsEmpty_is_true_only_when_no_category_has_content()
    {
        Assert.True(MasterDataSet.Empty.IsEmpty);

        // Settings always carry values, so they must not count toward emptiness (else Import hides).
        Assert.True((MasterDataSet.Empty with { Settings = new IncidentSettings(1, 2, 3) }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Roles = new[] { new Role("EL") } }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Personnel = new[] { new Person("X", "Y", null, null, null) } }).IsEmpty);
        Assert.False((MasterDataSet.Empty with { Links = new[] { new Link("N", "U") } }).IsEmpty);
    }

    [Fact]
    public void Parse_from_a_string_matches_parse_from_a_stream()
    {
        const string Json = """
            {
              "vehicles": [{ "wache": "FFB Wache 1", "callSign": "FFB 1/40/1", "seats": 9 }],
              "personnel": [{ "lastName": "Mustermann", "firstName": "Max" }],
              "settings": { "returnPressureBar": 70 }
            }
            """;

        var fromStream = MasterDataJson.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Json)));
        var fromString = MasterDataJson.Parse(Json);

        Assert.Equal(fromStream.Brigades, fromString.Brigades);
        Assert.Equal(fromStream.Vehicles, fromString.Vehicles);
        Assert.Equal(fromStream.Personnel, fromString.Personnel);
        Assert.Equal(fromStream.Settings, fromString.Settings);
        Assert.Equal(70, fromString.Settings.ReturnPressureBar);
    }

    [Fact]
    public void Personnel_email_and_note_survive_a_round_trip()
    {
        var set = MasterDataSet.Empty with
        {
            Personnel = new[]
            {
                new Person(
                    "Mustermann",
                    "Max",
                    "KBM",
                    "Land 2/3",
                    "01 71 / 6 53 58 23",
                    true,
                    "max.mustermann@example.org",
                    "KBM Gefahrgut, Fachberater Arbeits- und Gesundheitsschutz"),
            },
        };

        var person = Assert.Single(Parse(MasterDataJson.Serialize(set)).Personnel);

        Assert.Equal("max.mustermann@example.org", person.Email);
        Assert.Equal("KBM Gefahrgut, Fachberater Arbeits- und Gesundheitsschutz", person.Note);
    }

    // Both keys are optional, as every other personnel field is: a Stammdaten file written before
    // the Kontakte module existed still has to parse.
    [Fact]
    public void Personnel_without_the_new_keys_parse_as_absent()
    {
        var person = Assert.Single(
            Parse("""{"personnel":[{"lastName":"Mustermann","firstName":"Max"}]}""").Personnel);

        Assert.Null(person.Email);
        Assert.Null(person.Note);
        Assert.False(person.HasEmail);
        Assert.False(person.HasNote);
    }

    // #470: a Funktion carries how often it may be held. The object form is what a current file
    // holds; the bare string is what every file written before #470 holds, and must keep working.
    [Fact]
    public void Parse_reads_a_bare_funktions_name_as_multiple()
    {
        var set = Parse("""{ "roles": ["EL", "ZF"] }""");

        Assert.Equal(new Role("EL"), Assert.Single(set.Roles, r => r.Name == "EL"));
        Assert.Equal(RoleUniqueness.Multiple, set.Roles[0].Uniqueness);
    }

    [Fact]
    public void Parse_reads_the_uniqueness_mode_off_a_funktions_object()
    {
        var set = Parse("""
            {
              "roles": [
                { "name": "EL", "uniqueness": "uniquePerIncident" },
                { "name": "Abschnittsleiter", "uniqueness": "uniquePerSection" },
                { "name": "ZF" }
              ]
            }
            """);

        Assert.Equal(
            new[]
            {
                new Role("EL", RoleUniqueness.UniquePerIncident),
                new Role("Abschnittsleiter", RoleUniqueness.UniquePerSection),
                new Role("ZF", RoleUniqueness.Multiple),
            },
            set.Roles);
    }

    // A file crosses a trust boundary and HomeViewModel catches only the JSON exception types
    // around this parse, so an unrecognised mode must degrade rather than throw -- the same
    // reasoning as TruppType.ClampMemberCount.
    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData("1")]
    public void Parse_falls_back_to_multiple_for_an_unusable_uniqueness_mode(string mode)
    {
        var set = Parse($$"""{ "roles": [{ "name": "EL", "uniqueness": "{{mode}}" }] }""");

        Assert.Equal(RoleUniqueness.Multiple, Assert.Single(set.Roles).Uniqueness);
    }

    [Fact]
    public void Parse_defaults_a_missing_uniqueness_mode_to_multiple()
    {
        var set = Parse("""{ "roles": [{ "name": "EL" }] }""");

        Assert.Equal(new Role("EL"), Assert.Single(set.Roles));
    }

    // The positive shape assertion no test made before: the exporter always writes the object,
    // so a current file never carries a bare string -- which is what makes the bare-string branch
    // above a legacy path rather than a second live format.
    [Fact]
    public void Serialize_writes_every_funktion_as_an_object()
    {
        var json = MasterDataJson.Serialize(MasterDataSet.Empty with
        {
            Roles = new[]
            {
                new Role("EL", RoleUniqueness.UniquePerIncident),
                new Role("ZF"),
            },
        });

        using var doc = JsonDocument.Parse(json);
        var roles = doc.RootElement.GetProperty("roles");
        Assert.Equal(JsonValueKind.Object, roles[0].ValueKind);
        Assert.Equal("EL", roles[0].GetProperty("name").GetString());
        Assert.Equal("uniquePerIncident", roles[0].GetProperty("uniqueness").GetString());
        Assert.Equal("multiple", roles[1].GetProperty("uniqueness").GetString());
    }

    [Fact]
    public void Round_trip_preserves_every_uniqueness_mode()
    {
        var original = MasterDataSet.Empty with
        {
            Roles = new[]
            {
                new Role("EL", RoleUniqueness.UniquePerIncident),
                new Role("Abschnittsleiter", RoleUniqueness.UniquePerSection),
                new Role("ZF"),
            },
        };

        Assert.Equal(original.Roles, Parse(MasterDataJson.Serialize(original)).Roles);
    }

    // The trust boundary HomeViewModel.JoinDeviceAsync guards: it catches exactly these four types
    // and nothing else, turns them into a German banner, and -- crucially -- disposes the session
    // first. A Stammdaten file is host-controlled data that crossed a LAN and a PIN, so a
    // well-formed document whose *shape* is wrong is a normal hostile input, not a programming
    // error, and every shape must land inside that set. An explicit JSON null for a required name
    // used to raise a NullReferenceException out of GetString()!.Trim(), which escaped the filter,
    // skipped the DisposeAsync and killed the joining app mid-Einsatz with no handler to catch it.
    // Two call sites trim that name and both were reachable: roles and truppTypes.
    [Theory]
    [InlineData("""{ "roles": [{ "name": null }] }""")]
    [InlineData("""{ "truppTypes": [{ "name": null }] }""")]
    [InlineData("""{ "roles": [{ "name": 7 }] }""")]
    [InlineData("""{ "truppTypes": [{ "name": 7 }] }""")]
    [InlineData("""{ "roles": [null] }""")]
    [InlineData("""{ "roles": [7] }""")]
    [InlineData("""{ "roles": [true] }""")]
    [InlineData("""{ "roles": [{}] }""")]
    [InlineData("""{ "truppTypes": [null] }""")]
    [InlineData("""{ "truppTypes": [{}] }""")]
    public void Parse_raises_nothing_a_join_does_not_already_catch_for_a_wrong_shaped_named_entry(string json)
    {
        var thrown = Record.Exception(() => Parse(json));

        Assert.True(
            thrown is null
                or JsonException
                or InvalidOperationException
                or KeyNotFoundException
                or FormatException,
            thrown is null ? "expected this shape to be rejected" : $"escaped the filter as {thrown.GetType().Name}");
    }

    // The one shape that needs its own assertion, because it is the one a reader will change
    // again: an explicit null for a property that *is* present is malformed, not absent, so
    // KeyNotFoundException would be a lie and only the JSON exception tells the caller the truth.
    // The message is German because JoinError shows it to the Lagebuchführer verbatim.
    [Theory]
    [InlineData("""{ "roles": [{ "name": null }] }""")]
    [InlineData("""{ "truppTypes": [{ "name": null }] }""")]
    public void Parse_rejects_an_explicitly_null_name_as_a_german_json_error(string json)
    {
        var ex = Assert.Throws<JsonException>(() => Parse(json));

        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
        Assert.Contains("null", ex.Message, StringComparison.Ordinal);
    }
}
