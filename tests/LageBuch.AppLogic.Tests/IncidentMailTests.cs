using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

public class IncidentMailTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 22, 17, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident() => Incident.Start(new FixedClock(T0), new SessionOperator("Müller"));

    [Fact]
    public void Subject_lists_number_keyword_address_and_start()
    {
        var incident = NewIncident();
        incident.SetIncidentNumber(new IncidentNumber("2026-0815"));
        incident.SetKeyword("B3 Wohnungsbrand");
        incident.SetAddress("Hauptstr. 5", "Nord");

        Assert.Equal(
            "Einsatzbericht 2026-0815 · B3 Wohnungsbrand · Hauptstr. 5, Nord · 19.09.2026 22:17",
            IncidentMail.Subject(incident));
    }

    [Fact]
    public void Subject_skips_blank_fields()
    {
        var incident = NewIncident();
        incident.SetKeyword("TH Ölspur");

        Assert.Equal("Einsatzbericht TH Ölspur · 19.09.2026 22:17", IncidentMail.Subject(incident));
    }

    [Fact]
    public void Subject_of_an_unnamed_incident_is_the_start_alone()
    {
        Assert.Equal("Einsatzbericht 19.09.2026 22:17", IncidentMail.Subject(NewIncident()));
    }

    // A line break in a subject becomes a header break in a mailto: or xdg-email hand-off.
    [Fact]
    public void Subject_strips_line_breaks_and_control_characters()
    {
        var incident = NewIncident();
        incident.SetKeyword("B3\r\nBcc: x@example.org\tFeuer");

        var subject = IncidentMail.Subject(incident);

        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\t', subject);
        Assert.Equal("Einsatzbericht B3 Bcc: x@example.org Feuer · 19.09.2026 22:17", subject);
    }

    // Bidi overrides reorder what the inbox shows; U+2028/U+2029 render as line breaks in some clients.
    [Fact]
    public void Subject_strips_format_and_separator_characters()
    {
        var incident = NewIncident();
        incident.SetKeyword("B3\u202EdnarB\u200B\u2028Feuer\u2029");

        Assert.Equal("Einsatzbericht B3 dnarB Feuer · 19.09.2026 22:17", IncidentMail.Subject(incident));
    }

    [Fact]
    public void Body_names_start_and_close_but_not_the_lagebuchfuehrer()
    {
        var incident = NewIncident();
        incident.Close(new FixedClock(T0.AddHours(2)), new SessionOperator("Müller"));

        var body = IncidentMail.Body(incident);

        Assert.Contains("Anbei der Einsatzbericht als PDF.", body, StringComparison.Ordinal);
        Assert.Contains("Beginn: 19.09.2026 22:17", body, StringComparison.Ordinal);
        Assert.Contains("Abschluss: 20.09.2026 00:17", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Müller", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Body_of_an_open_incident_has_no_close_line()
    {
        Assert.DoesNotContain("Abschluss", IncidentMail.Body(NewIncident()), StringComparison.Ordinal);
    }
}
