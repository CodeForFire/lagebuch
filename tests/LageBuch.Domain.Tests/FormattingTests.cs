using LageBuch.Domain.Tasks;

namespace LageBuch.Domain.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(1600, "1600")]
    [InlineData(-1600, "-1600")]
    [InlineData(80, "80")]
    public void A_whole_number_on_a_marker_is_ungrouped(int value, string expected) =>
        Assert.Equal(expected, Formatting.UngroupedNumber(value));

    [Theory]
    [InlineData(554.4, "554 m")]
    [InlineData(1234.6, "1235 m")]
    public void Metres_are_whole_and_ungrouped_as_a_map_labels_them(double value, string expected) =>
        Assert.Equal(expected, Formatting.Metres(value));

    [Theory]
    [InlineData(515.4, "515 m")]
    [InlineData(999.4, "999 m")]
    [InlineData(999.5, "1,0 km")]
    [InlineData(1449, "1,4 km")]
    [InlineData(3000, "3,0 km")]
    public void A_distance_switches_to_kilometres_at_one(double metres, string expected) =>
        Assert.Equal(expected, Formatting.Distance(metres));

    [Fact]
    public void A_height_has_one_decimal_with_a_German_comma() =>
        Assert.Equal("554,6 m", Formatting.Height(554.62));

    [Theory]
    [InlineData(15.2, "+15 m")]
    [InlineData(-5, "−5 m")]
    [InlineData(0.3, "±0 m")]
    [InlineData(-0.4, "±0 m")]
    public void A_height_difference_carries_its_sign(double metres, string expected) =>
        Assert.Equal(expected, Formatting.HeightDifference(metres));

    [Fact]
    public void Timestamp_uses_german_day_first_format()
    {
        var t = new DateTimeOffset(2026, 6, 22, 9, 5, 0, TimeSpan.FromHours(2));
        Assert.Equal("22.06.2026 09:05", Formatting.Timestamp(t));
    }

    [Fact]
    public void TimeOfDay_is_clock_time_only()
    {
        var t = new DateTimeOffset(2026, 6, 22, 9, 5, 0, TimeSpan.FromHours(2));
        Assert.Equal("09:05", Formatting.TimeOfDay(t));
    }

    [Theory]
    [InlineData(180, "Geräteuhr geht 3 min nach")]
    [InlineData(-180, "Geräteuhr geht 3 min vor")]
    [InlineData(45, "Geräteuhr geht 45 s nach")]
    [InlineData(-59, "Geräteuhr geht 59 s vor")]
    [InlineData(90, "Geräteuhr geht 2 min nach")]
    [InlineData(3600, "Geräteuhr geht 60 min nach")]
    public void ClockSkew_says_which_way_this_devices_clock_is_off(int hostAheadSeconds, string expected)
    {
        Assert.Equal(expected, Formatting.ClockSkew(TimeSpan.FromSeconds(hostAheadSeconds)));
    }

    [Theory]
    [InlineData(IncidentState.Open, "Offen")]
    [InlineData(IncidentState.Closed, "Abgeschlossen")]
    public void State_is_german(IncidentState state, string expected)
    {
        Assert.Equal(expected, Formatting.State(state));
    }

    [Fact]
    public void OrDash_returns_dash_for_blank()
    {
        Assert.Equal("—", Formatting.OrDash(null));
        Assert.Equal("—", Formatting.OrDash("  "));
        Assert.Equal("EL", Formatting.OrDash("EL"));
    }

    [Theory]
    [InlineData("Hauptstr. 12", "FFB", "Hauptstr. 12, FFB")]
    [InlineData("Hauptstr. 12", null, "Hauptstr. 12")]
    [InlineData(null, "FFB", "FFB")]
    [InlineData("  ", "FFB", "FFB")]
    [InlineData(null, null, null)]
    [InlineData(" ", "", null)]
    public void Address_joins_the_non_blank_parts_and_is_null_when_both_are_blank(string? street, string? district, string? expected)
    {
        Assert.Equal(expected, Formatting.Address(street, district));
    }

    [Theory]
    [InlineData("Hauptstr.", "12", "82256", "Fürstenfeldbruck", "Puch", "Hauptstr. 12, Puch, 82256 Fürstenfeldbruck")]
    [InlineData("Hauptstr.", "12", null, "Fürstenfeldbruck", null, "Hauptstr. 12, Fürstenfeldbruck")]
    [InlineData(null, null, "82256", null, null, "82256")]
    [InlineData(null, "12", null, null, null, "12")]
    [InlineData(" Hauptstr. ", " ", " ", " Dachau ", null, "Hauptstr., Dachau")]
    [InlineData(null, null, null, null, null, null)]
    public void Address_joins_street_and_house_number_then_ortsteil_then_plz_and_ort(
        string? street, string? houseNumber, string? postalCode, string? place, string? district, string? expected)
    {
        Assert.Equal(expected, Formatting.Address(street, district, houseNumber, postalCode, place));
    }

    [Fact]
    public void StreetAddress_is_street_and_house_number_without_ortsteil_plz_or_ort()
    {
        var incident = Incident.Start(new FixedClock(DateTimeOffset.UnixEpoch), new SessionOperator("Muster"));
        incident.SetAddress("Buchenauerstraße", "Puch", houseNumber: "12", postalCode: "82256", place: "Fürstenfeldbruck");

        Assert.Equal("Buchenauerstraße 12", Formatting.StreetAddress(incident));
    }

    [Fact]
    public void StreetAddress_without_a_street_falls_back_to_the_rest_of_the_address()
    {
        var incident = Incident.Start(new FixedClock(DateTimeOffset.UnixEpoch), new SessionOperator("Muster"));
        incident.SetAddress(null, "Puch", houseNumber: "12", place: "Fürstenfeldbruck");

        Assert.Equal("12, Puch, Fürstenfeldbruck", Formatting.StreetAddress(incident));
    }

    [Fact]
    public void StreetAddress_is_null_without_an_address()
    {
        var incident = Incident.Start(new FixedClock(DateTimeOffset.UnixEpoch), new SessionOperator("Muster"));

        Assert.Null(Formatting.StreetAddress(incident));
    }

    public static TheoryData<TaskImportance, string> ImportanceLabels => new()
    {
        { TaskImportance.High, "Hoch" },
        { TaskImportance.Medium, "Mittel" },
        { TaskImportance.Low, "Niedrig" },
    };

    public static TheoryData<TaskUrgency, string> UrgencyLabels => new()
    {
        { TaskUrgency.High, "Hoch" },
        { TaskUrgency.Medium, "Mittel" },
        { TaskUrgency.Low, "Niedrig" },
    };

    [Theory]
    [MemberData(nameof(ImportanceLabels))]
    public void Level_importance_is_german(TaskImportance importance, string expected)
    {
        Assert.Equal(expected, Formatting.Level(importance));
    }

    [Fact]
    public void Level_importance_rows_cover_every_enum_member()
    {
        var covered = ImportanceLabels.Select(row => (TaskImportance)row[0]);
        Assert.Equal(Enum.GetValues<TaskImportance>().Order(), covered.Order());
    }

    [Theory]
    [MemberData(nameof(UrgencyLabels))]
    public void Level_urgency_is_german(TaskUrgency urgency, string expected)
    {
        Assert.Equal(expected, Formatting.Level(urgency));
    }

    [Fact]
    public void Level_urgency_rows_cover_every_enum_member()
    {
        var covered = UrgencyLabels.Select(row => (TaskUrgency)row[0]);
        Assert.Equal(Enum.GetValues<TaskUrgency>().Order(), covered.Order());
    }
}
