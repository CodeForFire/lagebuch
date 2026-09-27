using System.Diagnostics.CodeAnalysis;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

[SuppressMessage(
    "Design",
    "CA1054",
    Justification = "The theories feed deliberately malformed strings; a parsed Uri cannot express them.")]
public class MailAddressValidatorTests
{
    [Theory]
    [InlineData("max.mustermann@ff-musterstadt.example")]
    [InlineData("a-b_c+d@sub.domain.de")]
    [InlineData("kbi@xn--mnchen-3ya.de")]
    public void An_ordinary_address_is_accepted(string address)
    {
        Assert.True(MailAddressValidator.TryGetMailtoUri(address, out var uri));
        Assert.Equal("mailto", uri.Scheme);
    }

    // Trailing whitespace is trimmed, and Trim covers CR, LF and the Unicode separators, so a
    // roster value that picked one up on the way out of a spreadsheet still works. That is safe
    // precisely because it is trimmed rather than escaped: what is left holds no control character
    // at all, so nothing can be injected. An *embedded* one is a different matter and is refused
    // by the theory below.
    [Theory]
    [InlineData("  kbi@example.org  ")]
    [InlineData("kbi@example.org\r\n")]
    [InlineData("\u0085kbi@example.org")]
    public void Surrounding_whitespace_is_trimmed_rather_than_refused(string address)
    {
        Assert.True(MailAddressValidator.TryGetMailtoUri(address, out var uri));
        Assert.Equal("mailto:kbi@example.org", uri.AbsoluteUri);
    }

    // The address is concatenated, not escaped: everything with a meaning in a mailto: URI is
    // refused below, so the concatenation cannot change the URI's structure.
    [Fact]
    public void The_address_reaches_the_uri_unchanged()
    {
        Assert.True(MailAddressValidator.TryGetMailtoUri("a.b+c@example.org", out var uri));
        Assert.Equal("mailto:a.b+c@example.org", uri.AbsoluteUri);
    }

    [Theory]

    // Header injection: some clients parse a mailto: line by line, so a CR or LF would let an
    // imported Stammdaten file add a recipient the operator never saw.
    [InlineData("a@b.de\r\nBcc: opfer@example.org")]
    [InlineData("a@b.de\nBcc: opfer@example.org")]
    [InlineData("a@b.de\rBcc: opfer@example.org")]
    [InlineData("a@b.de\u0000")]
    [InlineData("a\u0085b@c.de")]

    // mailto: carries a query string, so these would compose a message nobody wrote.
    [InlineData("a@b.de?subject=Alarm")]
    [InlineData("a@b.de&bcc=opfer@example.org")]
    [InlineData("a@b.de#frag")]
    [InlineData("a@b.de%0Abcc:opfer@example.org")]

    // Not an address at all.
    [InlineData("javascript:x@y.de")]
    [InlineData("file:///etc/passwd")]
    [InlineData("Max <a@b.de>")]
    [InlineData("\"a b\"@c.de")]
    [InlineData("a b@c.de")]
    [InlineData("a@b .de")]
    [InlineData("a@[192.0.2.1]")]
    [InlineData("a@münchen.de")]
    [InlineData("a@b.de,c@d.de")]
    [InlineData("a@b.de;c@d.de")]

    // Structurally broken.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nodomain")]
    [InlineData("@b.de")]
    [InlineData("a@")]
    [InlineData("a@@b.de")]
    [InlineData("a@b@c.de")]
    [InlineData(".a@b.de")]
    [InlineData("a.@b.de")]
    [InlineData("a..b@c.de")]
    [InlineData("a@b")]
    [InlineData("a@b.")]
    [InlineData("a@.b.de")]
    [InlineData("a@-b.de")]
    [InlineData("a@b-.de")]
    [InlineData("a@b.d")]
    [InlineData("a@b.d1")]
    public void A_dangerous_or_malformed_address_is_refused(string? address)
    {
        Assert.False(MailAddressValidator.TryGetMailtoUri(address, out _));
    }

    [Fact]
    public void An_over_long_local_part_is_refused()
    {
        Assert.False(MailAddressValidator.TryGetMailtoUri(new string('a', 65) + "@example.org", out _));
    }

    [Fact]
    public void An_over_long_address_is_refused()
    {
        Assert.False(
            MailAddressValidator.TryGetMailtoUri("a@" + new string('b', 250) + ".de", out _));
    }
}
