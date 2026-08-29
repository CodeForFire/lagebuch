using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class WasserfoerderungSectionTests
{
    [Fact]
    public void Pdf_contains_wasserfoerderung_section_when_leitungen_planned()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.FromHours(2)));
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddWasserfoerderungLeitung("TLF 20/8", "FFB 1/44/1", 2000, 100);

        var pdf = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]>());

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}