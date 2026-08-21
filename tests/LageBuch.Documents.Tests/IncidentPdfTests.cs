using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;
using LageBuch.Domain;
using QuestPDF.Fluent;

namespace LageBuch.Documents.Tests;

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
        incident.SetIncidentNumber(new IncidentNumber("B 1.2 260715 4242"));
        incident.SetAddress("Hauptstr. 12", "FFB");
        incident.SetStatus("aufgenommen");
        incident.SeedChecklist(
            new[] { ("Blaulicht aus?", true), ("Bei ILS gemeldet?", false) },
            new[] { ("Fahrzeug abgerüstet?", true) });
        incident.ToggleChecklistItem(clock, op, incident.ChecklistAufbau[0].Id);
        clock.Now = clock.Now.AddMinutes(5);
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lagemeldung erhalten", from: "ILS");
        // Exercise the widest Funktionszuweisung row: every one of the seven columns filled, so
        // the A4 portrait layout is proven not to overflow.
        var assigned = incident.AssignRole(clock, op, "EL", "Müller", callSign: "FFB 12/1", from: clock.Now,
            section: "Abschnitt Nord", phone: "01 71 / 1 23 45 67");
        incident.EndRoleAssignment(assigned.Id, clock.Now.AddHours(1));
        incident.AssignRole(clock, op, "ZF", "Schmidt");
        incident.AddForceUnit(clock, op, "FFB Wache 1", 12, callSign: "FFB 1/40/1", status: "Im Einsatz",
            notes: "über Drehleiter angefordert", scbaCount: 6);
        incident.AddForceUnit(clock, op, "Emmering", 9, scbaCount: 4);
        var trupp = incident.AddScbaTrupp(
            clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), callSign: "FFB 1/40/1");
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

    // A minimal valid 1x1 JPEG — small enough to inline, real enough for QuestPDF's Skia-backed
    // Image() to decode. No qpdf/native-merge dependency on this path (see PdfAttachmentMergerTests
    // for the parts of #62 that do need it).
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=");

    [Fact]
    public void Generate_embeds_an_attached_image_as_a_new_section()
    {
        // A minimal incident, not BuildFullIncident's richer one: with that much content already
        // flowing across the page, adding the "Angehängte Bilder" section can shift pagination
        // (fewer/more lines before a break) and swing the total byte count either way — a confound
        // unrelated to whether the image itself was embedded. A minimal incident keeps layout stable
        // so the size delta below actually isolates the image's effect.
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", TinyJpeg.Length);

        var withoutImage = IncidentPdf.Generate(incident);
        var withImage = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]> { [file.Id] = TinyJpeg });

        PdfAssert.IsPdf(withImage);
        // Embedding a real image adds real bytes to the stream — a cheap, dependency-free proxy for
        // "the section actually rendered something" without parsing PDF content streams.
        Assert.True(withImage.Length > withoutImage.Length,
            $"Expected embedding the image to grow the PDF (without={withoutImage.Length}, with={withImage.Length}).");
    }

    [Fact]
    public void Generate_lists_a_renamed_pdf_attachment_in_the_file_table()
    {
        // A minimal incident, same rationale as the image test above: keeps pagination stable so a
        // size delta can be attributed to the new table row rather than an unrelated layout shift.
        // No qpdf/native-merge dependency here — the table lists file metadata only, independent of
        // whether bytes were supplied or the file is a PDF vs. an image.
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        var withoutFile = IncidentPdf.Generate(incident);

        var file = incident.AddFile(clock, op, "bericht.pdf", "application/pdf", 100);
        incident.RenameFile(file.Id, "Lagebericht Erdgeschoss");
        var withFile = IncidentPdf.Generate(incident);

        PdfAssert.IsPdf(withFile);
        // A PDF attachment's pages are appended separately and carry no caption of their own — the
        // table row is the only place its (renamed) label shows up anywhere in the report.
        Assert.True(withFile.Length > withoutFile.Length,
            $"Expected the file table to grow the PDF even without embedded bytes (without={withoutFile.Length}, with={withFile.Length}).");
    }

    [Fact]
    public void Generate_skips_a_file_whose_bytes_were_not_supplied_rather_than_failing()
    {
        var incident = BuildFullIncident();
        incident.AddFile(new Clock(), new SessionOperator("Müller"), "brand.jpg", "image/jpeg", 123);

        // No entry for the file's id in the dictionary — simulates a moved/missing sibling folder.
        var bytes = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]>());

        PdfAssert.IsPdf(bytes);
    }

    [Fact]
    public void Generate_with_no_files_dictionary_at_all_still_works()
    {
        // The optional-parameter default (null -> empty) covers every pre-#62 caller unchanged.
        var bytes = IncidentPdf.Generate(BuildFullIncident());
        PdfAssert.IsPdf(bytes);
    }

    // Needs the native qpdf library (see PdfAttachmentMergerTests' remarks).
    [Fact]
    public void Generate_appends_an_attached_pdfs_pages_after_the_report()
    {
        var incident = BuildFullIncident();
        var withoutAttachment = IncidentPdf.Generate(incident);
        var reportPages = PdfAssert.CountPages(withoutAttachment);

        var attachmentPdf = IncidentPdf.Generate(Incident.Start(new Clock(), new SessionOperator("Müller")));
        var file = incident.AddFile(new Clock(), new SessionOperator("Müller"), "bericht.pdf", "application/pdf", attachmentPdf.Length);

        var withAttachment = IncidentPdf.Generate(incident, new Dictionary<Guid, byte[]> { [file.Id] = attachmentPdf });

        PdfAssert.IsPdf(withAttachment);
        Assert.Equal(reportPages + PdfAssert.CountPages(attachmentPdf), PdfAssert.CountPages(withAttachment));
    }
}
