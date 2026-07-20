using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;
using Feuerwehr.Domain;
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
        // Exercise the widest Funktionszuweisung row: every one of the seven columns filled, so
        // the A4 portrait layout is proven not to overflow.
        var assigned = incident.AssignRole("EL", "Müller", callSign: "FFB 12/1", from: clock.Now,
            section: "Abschnitt Nord", phone: "01 71 / 1 23 45 67");
        incident.EndRoleAssignment(assigned.Id, clock.Now.AddHours(1));
        incident.AssignRole("ZF", "Schmidt");
        incident.AddForceUnit("FFB Wache 1", 12, callSign: "FFB 1/40/1", status: "Im Einsatz",
            notes: "über Drehleiter angefordert", scbaCount: 6);
        incident.AddForceUnit("Emmering", 9, scbaCount: 4);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), callSign: "FFB 1/40/1");
        incident.StartScbaTrupp(clock, trupp.Id, 300);
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
