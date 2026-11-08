using LageBuch.AppLogic.Services.Dispatch;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class FfAgentDispatchParserTests
{
    // Central European winter time: the alarm's iso stamp carries no offset of its own.
    private static readonly TimeZoneInfo Cet = TimeZoneInfo.CreateCustomTimeZone("Test/CET", TimeSpan.FromHours(1), "CET", "CET");

    private static DispatchAlarm Parse(string content)
    {
        Assert.True(FfAgentDispatchParser.TryParse(content, Cet, out var alarm));
        return alarm;
    }

    [Fact]
    public void The_mail_body_yields_the_head_data_of_the_alarm()
    {
        var alarm = Parse(DispatchSamples.Html());

        Assert.Equal(DispatchSamples.IncidentNumber, alarm.IncidentNumber);
        Assert.Equal("THL 2 T2917", alarm.Keyword);
        Assert.Equal("THL 2 VU eCall", alarm.IncidentType);
        Assert.Equal("VU eCall - ohne Spracherwiderung", alarm.Report);
        Assert.Equal("2 PKW, 3 Betroffene", alarm.Hint);
        Assert.Equal("Feuerwehr Musterstadt", alarm.Organisation);
        Assert.False(alarm.IsTest);
    }

    [Fact]
    public void The_bare_xml_parses_the_same_as_the_mail_body()
    {
        Assert.Equal(Parse(DispatchSamples.Html()).Key, Parse(DispatchSamples.Xml()).Key);
        Assert.Equal(Parse(DispatchSamples.Html()).RequestedUnits, Parse(DispatchSamples.Xml()).RequestedUnits);
    }

    [Fact]
    public void The_address_is_split_into_street_house_number_postal_code_and_place()
    {
        var alarm = Parse(DispatchSamples.Html());

        Assert.Equal("Hauptstraße", alarm.Street);
        Assert.Equal("12", alarm.HouseNumber);
        Assert.Equal("12345", alarm.PostalCode);
        Assert.Equal("Musterstadt Nord", alarm.Place);
    }

    [Theory]
    [InlineData("Hauptstraße 12a", "Hauptstraße", "12a")]
    [InlineData("Hauptstraße 12-14", "Hauptstraße", "12-14")]
    [InlineData("Maisacher Straße", "Maisacher Straße", null)]
    [InlineData("Straße des 17. Juni", "Straße des 17. Juni", null)]
    public void A_trailing_house_number_is_split_off_the_street(string street, string expectedStreet, string? expectedNumber)
    {
        var alarm = Parse(DispatchSamples.Html(DispatchSamples.Xml(street: street)));

        Assert.Equal(expectedStreet, alarm.Street);
        Assert.Equal(expectedNumber, alarm.HouseNumber);
    }

    [Fact]
    public void A_place_without_a_postal_code_is_kept_whole()
    {
        var alarm = Parse(DispatchSamples.Html(DispatchSamples.Xml(place: "Musterstadt")));

        Assert.Null(alarm.PostalCode);
        Assert.Equal("Musterstadt", alarm.Place);
    }

    [Fact]
    public void The_wgs84_coordinates_become_the_location()
    {
        Assert.Equal(new GeoPoint(48.1, 11.5), Parse(DispatchSamples.Html()).Location);
    }

    [Theory]
    [InlineData("<wgs84 lat=\"\" lng=\"\"/>")]
    [InlineData("<wgs84 lat=\"123\" lng=\"11.5\"/>")]
    [InlineData("")]
    public void Missing_or_impossible_coordinates_leave_the_location_empty(string coordinates)
    {
        Assert.Null(Parse(DispatchSamples.Html(DispatchSamples.Xml(coordinates: coordinates))).Location);
    }

    [Fact]
    public void The_alarm_time_is_read_in_the_local_time_zone()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 17, 17, 0, TimeSpan.FromHours(1)), Parse(DispatchSamples.Html()).AlarmedAt);
    }

    // Half past midnight on 1 January of year 1, an hour east of UTC, is before the first instant
    // a DateTimeOffset holds; it must cost the alarm its time, not throw out of the whole scan.
    [Fact]
    public void An_alarm_time_off_the_calendar_is_left_empty()
    {
        Assert.Null(Parse(DispatchSamples.Xml(alarmedAt: "0001-01-01 00:30")).AlarmedAt);
    }

    [Fact]
    public void The_first_instant_on_the_calendar_is_still_a_time()
    {
        Assert.Equal(DateTimeOffset.MinValue.ToOffset(TimeSpan.FromHours(1)), Parse(DispatchSamples.Xml(alarmedAt: "0001-01-01 01:00")).AlarmedAt);
    }

    [Fact]
    public void An_unreadable_alarm_time_is_left_empty()
    {
        Assert.Null(Parse(DispatchSamples.Html(DispatchSamples.Xml(alarmedAt: string.Empty))).AlarmedAt);
    }

    [Fact]
    public void Each_requested_unit_is_listed_once_in_the_order_given()
    {
        Assert.Equal(
            ["Florian Musterstadt Wache 2", "Florian Musterstadt 1", "Florian Musterstadt 2/40/1"],
            Parse(DispatchSamples.Html()).RequestedUnits);
    }

    [Fact]
    public void Without_requested_units_the_vehicles_are_listed()
    {
        var alarm = Parse(DispatchSamples.Html(DispatchSamples.Xml(requested: " ", vehicles: "2/40/1")));

        Assert.Equal(["2/40/1"], alarm.RequestedUnits);
    }

    [Fact]
    public void A_filled_test_element_marks_a_probealarm()
    {
        Assert.True(Parse(DispatchSamples.Html(DispatchSamples.Xml(test: "1"))).IsTest);
    }

    [Fact]
    public void Two_alarms_with_one_number_but_different_times_have_different_keys()
    {
        var first = Parse(DispatchSamples.Xml(alarmedAt: "2026-11-08 17:17"));
        var second = Parse(DispatchSamples.Xml(alarmedAt: "2026-11-08 18:02"));

        Assert.NotEqual(first.Key, second.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>Hallo, keine Alarmierung.</body></html>")]
    [InlineData("<einsatz version=\"1\"><kennung><einsatznummer> </einsatznummer></kennung></einsatz>")]
    [InlineData("<einsatz version=\"1\"><kennung>")]
    public void Content_without_a_numbered_einsatz_is_not_an_alarm(string content)
    {
        Assert.False(FfAgentDispatchParser.TryParse(content, Cet, out _));
    }

    // A file in the folder is hostile input: an entity must never be expanded or fetched.
    [Fact]
    public void An_entity_declaration_is_refused()
    {
        var xml = "<!DOCTYPE einsatz [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>"
            + "<einsatz version=\"1\"><kennung><einsatznummer>&x;</einsatznummer></kennung></einsatz>";

        Assert.False(FfAgentDispatchParser.TryParse(xml, Cet, out _));
        Assert.False(FfAgentDispatchParser.TryParse(DispatchSamples.Html(xml), Cet, out _));
    }

    [Fact]
    public void Content_over_the_size_cap_is_refused_unread()
    {
        var padded = DispatchSamples.Html() + new string(' ', FfAgentDispatchParser.MaxContentLength);

        Assert.False(FfAgentDispatchParser.TryParse(padded, Cet, out _));
    }

    // Angle brackets that never close made a tag regex rescan the rest of the content from every
    // one of them: quadratic, minutes of CPU for a file under the cap. One pass reads it in no time.
    [Fact]
    public void Unclosed_angle_brackets_are_read_in_one_pass()
    {
        var hostile = "&lt;einsatz" + new string('<', FfAgentDispatchParser.MaxContentLength - 40) + "&lt;/einsatz&gt;";
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(FfAgentDispatchParser.TryParse(hostile, Cet, out _));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public void Content_right_at_the_size_cap_is_still_read()
    {
        var html = DispatchSamples.Html();
        var padded = html + new string(' ', FfAgentDispatchParser.MaxContentLength - html.Length);

        Assert.True(FfAgentDispatchParser.TryParse(padded, Cet, out _));
    }

    [Fact]
    public void Xml_that_begins_with_the_einsatz_element_is_read()
    {
        var xml = DispatchSamples.Xml();

        Assert.True(FfAgentDispatchParser.TryParse(xml[xml.IndexOf("<einsatz", StringComparison.Ordinal)..], Cet, out _));
    }

    [Fact]
    public void An_einsatz_that_never_closes_after_other_text_is_no_alarm()
    {
        Assert.False(FfAgentDispatchParser.TryParse("Weitergeleitete Nachricht: <einsatz version=\"1\"><kennung>", Cet, out _));
    }

    [Fact]
    public void An_alarm_without_a_place_or_street_leaves_the_address_empty()
    {
        const string xml = "<einsatz version=\"1\"><kennung><einsatznummer>T 1</einsatznummer></kennung></einsatz>";

        var alarm = Parse(xml);

        Assert.Null(alarm.Street);
        Assert.Null(alarm.HouseNumber);
        Assert.Null(alarm.PostalCode);
        Assert.Null(alarm.Place);
        Assert.Empty(alarm.RequestedUnits);
    }

    [Fact]
    public void An_overlong_field_is_cut_to_the_field_cap()
    {
        var alarm = Parse(DispatchSamples.Xml(street: new string('a', 5000)));

        Assert.Equal(FfAgentDispatchParser.MaxFieldLength, alarm.Street!.Length);
    }

    // The mail names who called and who is hurt; neither belongs in the Lagebuch by way of an alarm.
    [Fact]
    public void Caller_and_patient_are_never_read()
    {
        var alarm = Parse(DispatchSamples.Html());

        var everything = string.Join("|", alarm.IncidentNumber, alarm.Keyword, alarm.IncidentType, alarm.Report, alarm.Hint, alarm.Street, alarm.Place, alarm.Organisation, string.Join("|", alarm.RequestedUnits));
        Assert.DoesNotContain("Musterfrau", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Max Mustermann", everything, StringComparison.Ordinal);
    }
}
