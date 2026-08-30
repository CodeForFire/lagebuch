using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Documents.Tests;

public class WasserfoerderungSectionTests
{
    // Smallest possible valid PNG (1x1, transparent) -- real, decodable bytes so QuestPDF's
    // .Image(...) composition genuinely exercises the image path rather than a placeholder blob.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==");

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

    [Fact]
    public void Pdf_embeds_the_route_overview_image_when_supplied_for_a_route_based_leitung()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.FromHours(2)));
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        var route = new[] { new GeoPoint(48.0, 11.0), new GeoPoint(48.002, 11.0) };
        var profile = new[] { new ElevationProfileSample(0, 0), new ElevationProfileSample(400, 0) };
        var leitung = incident.AddWasserfoerderungLeitungFromRoute("TLF 20/8", "FFB 1/44/1", route, profile);

        var withImage = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]>(),
            new Dictionary<Guid, byte[]> { [leitung.Id] = TinyPng });
        var withoutImage = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]>());

        Assert.True(withImage.Length > 1000);
        Assert.Equal(0x25, withImage[0]); // '%'
        Assert.True(withImage.Length > withoutImage.Length); // the embedded PNG adds real bytes
    }

    [Fact]
    public void Explicit_null_route_overview_dictionary_still_generates_a_valid_pdf()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.FromHours(2)));
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddWasserfoerderungLeitung("TLF 20/8", "FFB 1/44/1", 2000, 100);

        var pdf = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]>(), null);

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}