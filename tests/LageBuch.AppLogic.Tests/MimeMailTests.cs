using LageBuch.AppLogic.Services.Dispatch;

namespace LageBuch.AppLogic.Tests;

public class MimeMailTests
{
    private static readonly TimeZoneInfo Cet = TimeZoneInfo.CreateCustomTimeZone("Test/CET", TimeSpan.FromHours(1), "CET", "CET");

    [Fact]
    public void A_saved_ff_agent_mail_yields_its_html_body()
    {
        Assert.True(MimeMail.TryGetText(DispatchSamples.Eml(), out var text));
        Assert.Equal(DispatchSamples.Html(), text);
    }

    [Fact]
    public void A_forwarded_multipart_mail_yields_its_html_part_decoded()
    {
        Assert.True(MimeMail.TryGetText(DispatchSamples.MultipartEml(), out var text));
        Assert.Equal(DispatchSamples.Html(), text);
    }

    [Fact]
    public void A_saved_mail_parses_as_the_alarm_it_carries()
    {
        Assert.True(FfAgentDispatchParser.TryParse(DispatchSamples.Eml(), Cet, out var alarm));
        Assert.Equal(DispatchSamples.IncidentNumber, alarm.IncidentNumber);
        Assert.Equal(["Florian Musterstadt Wache 2", "Florian Musterstadt 1", "Florian Musterstadt 2/40/1"], alarm.RequestedUnits);
    }

    [Fact]
    public void A_forwarded_mail_parses_as_the_alarm_it_carries()
    {
        Assert.True(FfAgentDispatchParser.TryParse(DispatchSamples.MultipartEml(), Cet, out var alarm));
        Assert.Equal("Hauptstraße", alarm.Street);
    }

    [Theory]
    [InlineData("<html><body>kein Mail-Kopf</body></html>")]
    [InlineData("<?xml version=\"1.0\"?><einsatz/>")]
    [InlineData("Subject: ohne Inhaltstyp\r\n\r\nText")]
    [InlineData("")]
    public void Content_that_is_no_mail_is_left_alone(string content)
    {
        Assert.False(MimeMail.TryGetText(content, out _));
    }

    [Fact]
    public void A_mail_with_only_an_attachment_has_no_text()
    {
        const string mail = "Content-Type: application/pdf\r\nContent-Transfer-Encoding: base64\r\n\r\nJVBERi0=\r\n";

        Assert.False(MimeMail.TryGetText(mail, out _));
    }

    [Fact]
    public void A_plain_text_mail_without_an_html_part_yields_its_text()
    {
        const string mail = "Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: 8bit\r\n\r\nAlarm für Musterstadt\r\n";

        Assert.True(MimeMail.TryGetText(mail, out var text));
        Assert.Equal("Alarm für Musterstadt\r\n", text);
    }

    [Fact]
    public void A_latin1_part_is_decoded_in_its_charset()
    {
        var body = Convert.ToBase64String(System.Text.Encoding.Latin1.GetBytes("Straße"));
        var mail = $"Content-Type: text/html; charset=iso-8859-1\r\nContent-Transfer-Encoding: base64\r\n\r\n{body}\r\n";

        Assert.True(MimeMail.TryGetText(mail, out var text));
        Assert.Equal("Straße", text);
    }

    [Fact]
    public void An_unknown_charset_falls_back_to_utf8()
    {
        var body = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Straße"));
        var mail = $"Content-Type: text/html; charset=x-erfunden\r\nContent-Transfer-Encoding: base64\r\n\r\n{body}\r\n";

        Assert.True(MimeMail.TryGetText(mail, out var text));
        Assert.Equal("Straße", text);
    }

    [Fact]
    public void Broken_base64_is_no_text()
    {
        const string mail = "Content-Type: text/html\r\nContent-Transfer-Encoding: base64\r\n\r\n!!kein base64!!\r\n";

        Assert.False(MimeMail.TryGetText(mail, out _));
    }

    // Appending each folded line to the header copied the whole header again: quadratic, seconds
    // of CPU on every poll for one file of continuation lines under the size cap.
    [Fact]
    public void A_header_folded_over_thousands_of_lines_is_read_in_one_pass()
    {
        var folded = string.Concat(Enumerable.Repeat("\r\n x", FfAgentDispatchParser.MaxContentLength / 4));
        var mail = "Content-Type: text/html" + folded + "\r\n\r\n<p>innen</p>";
        var watch = System.Diagnostics.Stopwatch.StartNew();

        _ = MimeMail.TryGetText(mail, out _);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.Elapsed}");
    }

    [Fact]
    public void A_header_block_over_the_cap_is_no_mail()
    {
        var mail = "Content-Type: text/html\r\nX-Polster: " + new string('x', MimeMail.MaxHeaderLength) + "\r\n\r\n<p>innen</p>";

        Assert.False(MimeMail.TryGetText(mail, out _));
    }

    [Fact]
    public void Folded_headers_are_joined_with_one_space()
    {
        const string mail = "Content-Type: multipart/alternative;\r\n\tboundary=\"b\"\r\n\r\n--b\r\nContent-Type: text/html\r\n\r\n<p>innen</p>\r\n--b--\r\n";

        Assert.True(MimeMail.TryGetText(mail, out var text));
        Assert.Equal("<p>innen</p>", text);
    }

    // A hostile mail can nest multiparts without end; each level costs a pass over the rest.
    [Fact]
    public void Multiparts_nested_past_the_depth_cap_are_not_followed()
    {
        var mail = "Content-Type: text/html\r\n\r\n<p>innen</p>\r\n";
        for (var i = 0; i <= MimeMail.MaxDepth; i++)
        {
            mail = $"Content-Type: multipart/mixed; boundary=\"b{i}\"\r\n\r\n--b{i}\r\n{mail}--b{i}--\r\n";
        }

        Assert.False(MimeMail.TryGetText(mail, out _));
    }

    [Fact]
    public void Multiparts_nested_up_to_the_cap_are_followed()
    {
        var mail = "Content-Type: text/html\r\n\r\n<p>innen</p>\r\n";
        for (var i = 0; i < MimeMail.MaxDepth; i++)
        {
            mail = $"Content-Type: multipart/mixed; boundary=\"b{i}\"\r\n\r\n--b{i}\r\n{mail}--b{i}--\r\n";
        }

        Assert.True(MimeMail.TryGetText(mail, out var text));

        // The line break before a boundary belongs to the boundary, not to the part (RFC 2046).
        Assert.Equal("<p>innen</p>", text);
    }
}
