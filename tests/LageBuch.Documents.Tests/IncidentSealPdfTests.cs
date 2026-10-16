using LageBuch.Documents.Sections;
using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

// The seal of a closed Einsatz (#598) on paper: its short form in every page's footer, the full
// hash in the header.
public class IncidentSealPdfTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset ExportedAt = new(2026, 6, 22, 12, 0, 0, TimeSpan.FromHours(2));

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }

    private static Incident Closed(bool withSeal)
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.Close(clock, op);
        if (withSeal)
        {
            incident.ApplySeal(new IncidentSeal(IncidentSeal.CurrentVersion, Hash, new Dictionary<Guid, string?>()));
        }

        return incident;
    }

    [Fact]
    public void A_sealed_reports_footer_carries_the_short_form_of_the_seal()
    {
        Assert.Equal("Siegel: 04HM-ASW9-NF6Y", IncidentReportDocument.SealMarker(Closed(withSeal: true)));
    }

    [Fact]
    public void A_closed_report_without_a_seal_prints_no_seal_line()
    {
        var incident = Closed(withSeal: false);

        Assert.Null(IncidentReportDocument.SealMarker(incident));
        Assert.Null(IncidentHeaderSection.SealText(incident));
    }

    [Fact]
    public void An_open_reports_footer_keeps_its_Zwischenstand_marker_and_no_seal()
    {
        var incident = Incident.Start(new Clock(), new SessionOperator("Müller"));

        Assert.Null(IncidentReportDocument.SealMarker(incident));
        Assert.NotNull(IncidentReportDocument.InterimMarker(incident, ExportedAt));
    }

    [Fact]
    public void The_header_carries_the_full_hash_once_the_incident_is_sealed()
    {
        Assert.Equal(Hash, IncidentHeaderSection.SealText(Closed(withSeal: true)));
    }

    [Fact]
    public void A_sealed_report_renders()
    {
        PdfAssert.IsPdf(IncidentPdf.Generate(Closed(withSeal: true), ExportedAt));
    }
}
