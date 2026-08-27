using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class CoMessprotokollSectionTests
{
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
        var pdf = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]>());

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    [Fact]
    public void Pdf_Contains_CO_Section_EmptyState()
    {
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(Clock, op);
        var pdf = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]>());

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
