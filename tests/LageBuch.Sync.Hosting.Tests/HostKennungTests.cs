using System.Text.RegularExpressions;

namespace LageBuch.Sync.Hosting.Tests;

public class HostKennungTests
{
    [Fact]
    public void The_kennung_is_three_groups_of_four_unambiguous_characters()
    {
        using var cert = SyncCertificate.Generate().Cert;

        Assert.Matches(new Regex("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$"), HostKennung.Of(cert));
    }

    [Fact]
    public void The_kennung_spells_the_first_sixty_bits_of_the_pin_in_crockford_base32()
    {
        // 00 44 32 14 C7 00 00 0F = 00000 00001 00010 00011 00100 00101 00110 00111 00000 ... 0000|1111
        // The last four bits fall outside the 60 and must not show.
        var pin = HostKennung.PinPrefix + "00443214C700000F" + new string('F', 48);

        Assert.Equal("0123-4567-0000", HostKennung.FromPin(pin));
    }

    [Fact]
    public void Different_keys_give_different_pins()
    {
        using var a = SyncCertificate.Generate().Cert;
        using var b = SyncCertificate.Generate().Cert;

        Assert.NotEqual(HostKennung.Pin(a), HostKennung.Pin(b));
    }

    [Fact]
    public void A_host_certificate_says_it_shows_a_kennung()
    {
        using var cert = SyncCertificate.Generate().Cert;

        Assert.True(HostKennung.ShowsKennung(cert));
    }

    [Fact]
    public void A_host_without_a_kennung_is_told_to_update_instead_of_to_compare()
    {
        var ex = new CertificateChangedException("elw-1", "SPKI:00", "7K2Q-M9XD-4HPA", hostShowsKennung: false);

        Assert.Contains("aktualisieren", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Klick auf dessen PIN", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.PresentedPin);
    }

    [Fact]
    public void A_first_contact_names_itself_and_puts_the_kennung_on_a_line_of_its_own()
    {
        var ex = CertificateChangedException.FirstContact("elw-1", "SPKI:00", "7K2Q-M9XD-4HPA", hostShowsKennung: true);

        Assert.True(ex.IsFirstContact);
        Assert.StartsWith("Erste Verbindung zu elw-1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("\n7K2Q-M9XD-4HPA\n", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("neuen Kennung", ex.Message, StringComparison.Ordinal);
        Assert.Equal("SPKI:00", ex.PresentedPin);
        Assert.Equal("7K2Q-M9XD-4HPA", ex.Kennung);
    }

    [Fact]
    public void A_first_contact_with_a_host_without_a_kennung_still_asks_and_says_to_update()
    {
        // The certificate subject that says "shows a Kennung" is chosen by whoever answers, so an
        // older host gets no silent pass: the user is still asked, and told why there is nothing to
        // compare.
        var ex = CertificateChangedException.FirstContact("elw-1", "SPKI:00", "7K2Q-M9XD-4HPA", hostShowsKennung: false);

        Assert.True(ex.IsFirstContact);
        Assert.Contains("ältere Lagebuch-Version", ex.Message, StringComparison.Ordinal);
        Assert.Contains("aktualisieren", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Klick auf dessen PIN", ex.Message, StringComparison.Ordinal);
        Assert.Equal("SPKI:00", ex.PresentedPin);
    }

    [Fact]
    public void A_changed_key_is_not_a_first_contact()
    {
        Assert.False(new CertificateChangedException("elw-1", "SPKI:00", "7K2Q-M9XD-4HPA").IsFirstContact);
        Assert.False(CertificateChangedException.Forged("elw-1").IsFirstContact);
    }

    [Fact]
    public void A_forged_kennung_offers_nothing_to_trust()
    {
        var ex = CertificateChangedException.Forged("elw-1");

        Assert.Null(ex.PresentedPin);
        Assert.Contains("nachgemachten Kennung", ex.Message, StringComparison.Ordinal);
    }
}
