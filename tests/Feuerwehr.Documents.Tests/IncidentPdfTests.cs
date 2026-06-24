using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;
using QuestPDF.Fluent;

namespace Feuerwehr.Documents.Tests;

public class IncidentPdfTests
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }

    private static Incident BuildFullIncident()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op, "Brand");
        incident.SetIncidentNumber(new IncidentNumber("B 4242"));
        incident.SetIlsNumber(IlsNumber.Parse("4242"));
        incident.SetAddress("Hauptstr. 12", "FFB");
        incident.SetStatus("aufgenommen");
        incident.SeedChecklist(new[] { "Blaulicht aus?", "Bei ILS gemeldet?" });
        incident.ToggleChecklistItem(incident.Checklist[0].Id);
        clock.Now = clock.Now.AddMinutes(5);
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung erhalten", from: "ILS");
        incident.AssignRole("EL", "Müller", callSign: "FFB 12/1");
        incident.AddForceUnit("FFB", 12, callSign: "FFB 1/40/1");
        incident.AddForceUnit("Emmering", 9);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300, callSign: "FFB 1/40/1");
        incident.RecordScbaPressure(clock, trupp.Id, 220);
        return incident;
    }

    [Fact]
    public void Generate_produces_a_valid_pdf_for_a_full_incident()
    {
        var bytes = IncidentPdf.Generate(BuildFullIncident());
        PdfAssert.IsPdf(bytes);
    }

    [Fact]
    public void Generate_produces_a_valid_pdf_for_a_minimal_incident()
    {
        var incident = Incident.Start(new Clock(), new SessionOperator("Müller"));
        var bytes = IncidentPdf.Generate(incident);
        PdfAssert.IsPdf(bytes);
    }

    [Fact]
    public void Generate_works_for_a_closed_incident()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        clock.Now = clock.Now.AddHours(2);
        incident.Close(clock, op);

        var bytes = IncidentPdf.Generate(incident);
        PdfAssert.IsPdf(bytes);
    }

    [Fact]
    public void Direct_document_generation_ensures_license_and_produces_a_valid_pdf()
    {
        var bytes = new IncidentReportDocument(BuildFullIncident()).GeneratePdf();
        PdfAssert.IsPdf(bytes);
    }
}
