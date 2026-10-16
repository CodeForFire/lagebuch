using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class CoMessprotokollSectionTests
{
    // The moment the export is taken. Fixed so a task's "FÄLLIG" rendering does not depend on
    // when the suite happens to run.
    private static readonly DateTimeOffset ExportedAt = new(2026, 6, 22, 12, 0, 0, TimeSpan.FromHours(2));

    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));

    private static Incident CreateIncidentWithBuilding()
    {
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(Clock, op);
        incident.AddCoBuilding(Clock, op, "Haus A", 2, 3);
        incident.RecordCoValue(Clock, op, incident.Buildings[0].Id, 0, 1, 45);
        incident.SetDwellingStatus(Clock, op, incident.Buildings[0].Id, 0, 2, DwellingStatus.Affected);
        return incident;
    }

    [Fact]
    public void Pdf_Contains_CO_Section_With_Buildings()
    {
        var incident = CreateIncidentWithBuilding();
        var pdf = IncidentPdf.Generate(incident, ExportedAt, new Dictionary<Guid, byte[]>());

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    // #424: a Wohnung with a Messreihe renders the series line; one with a single reading omits
    // it, and a cleared value prints as "gelöscht" rather than vanishing. Text is not extracted in
    // this suite (see PdfAssert), so this pins that composing does not throw across those shapes --
    // the wording was checked by eye on a generated PDF.
    [Fact]
    public void Pdf_Contains_CO_Section_With_A_Measurement_Series()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Huber", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 3);
        var haus = incident.Buildings[0].Id;

        // Three readings, falling.
        incident.RecordCoValue(clock, op, haus, 0, 1, 250);
        clock.Now = clock.Now.AddMinutes(27);
        incident.RecordCoValue(clock, op, haus, 0, 1, 40);
        clock.Now = clock.Now.AddMinutes(21);
        incident.RecordCoValue(clock, op, haus, 0, 1, 5);

        // Two readings ending in a deletion.
        incident.RecordCoValue(clock, op, haus, 0, 2, 120);
        incident.RecordCoValue(clock, op, haus, 0, 2, null);

        // A single reading: no series line.
        incident.RecordCoValue(clock, op, haus, 1, 1, 8);

        var pdf = IncidentPdf.Generate(incident, ExportedAt, new Dictionary<Guid, byte[]>());

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    [Fact]
    public void Pdf_Contains_CO_Section_With_Underground_Floors()
    {
        // #218: a building with Untergeschosse must render without throwing, including the
        // negative-ordinal rows and the extended floor-range label.
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(Clock, op);
        incident.AddCoBuilding(Clock, op, "Haus A", 2, 3, undergroundFloorCount: 2);
        incident.RecordCoValue(Clock, op, incident.Buildings[0].Id, -1, 1, 45);

        var pdf = IncidentPdf.Generate(incident, ExportedAt, new Dictionary<Guid, byte[]>());

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    [Fact]
    public void Pdf_Contains_CO_Section_With_EveryPpmSeverityBand()
    {
        // ppm severity coloring is a separate span from the status color (see
        // CoMessprotokollSection.GetSeverityColor) -- must render without throwing across every
        // band, including a value at/above the implausible-value threshold.
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(Clock, op);
        incident.AddCoBuilding(Clock, op, "Haus A", 1, 4);
        incident.RecordCoValue(Clock, op, incident.Buildings[0].Id, 0, 1, 5); // Normal
        incident.RecordCoValue(Clock, op, incident.Buildings[0].Id, 0, 2, 30); // Elevated
        incident.RecordCoValue(Clock, op, incident.Buildings[0].Id, 0, 3, 200); // Dangerous
        incident.RecordCoValue(Clock, op, incident.Buildings[0].Id, 0, 4, 2001); // Lethal + implausible

        var pdf = IncidentPdf.Generate(incident, ExportedAt, new Dictionary<Guid, byte[]>());

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    [Fact]
    public void Pdf_Contains_CO_Section_EmptyState()
    {
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(Clock, op);
        var pdf = IncidentPdf.Generate(incident, ExportedAt, new Dictionary<Guid, byte[]>());

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
