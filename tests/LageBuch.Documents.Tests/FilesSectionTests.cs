using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class FilesSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    // The list is what makes a PDF attachment's name visible anywhere in the export, so every file
    // is listed by its display name, bytes supplied or not.
    [Fact]
    public void Every_file_is_listed_by_display_name_author_and_date_in_the_order_it_was_added()
    {
        var clock = new FixedClock(Started);
        var incident = Incident.Start(clock, new SessionOperator("Huber"));
        clock.Now = Started.AddMinutes(5);
        var pdf = incident.AddFile(clock, new SessionOperator("Huber"), "bericht.pdf", "application/pdf", 100);
        incident.RenameFile(pdf.Id, "Lagebericht");
        clock.Now = Started.AddMinutes(7);
        incident.AddFile(clock, new SessionOperator("Mayr"), "nord.jpg", "image/jpeg", 100);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Files);

        PdfText.AssertInOrder(
            text,
            "Angehängte Dateien",
            "Name Hinzugefügt von Datum",
            "Lagebericht Huber 22.06.2026 09:05",
            "nord.jpg Mayr 22.06.2026 09:07");
        Assert.DoesNotContain("bericht.pdf", text, StringComparison.Ordinal);

        // All three cells on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "Lagebericht Huber 22.06.2026 09:05",
            PdfText.LinesOf(incident, Started, IncidentPdfSections.Files));
    }

    [Fact]
    public void Only_an_image_whose_bytes_were_supplied_is_shown_inline_under_its_name()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Huber");
        var incident = Incident.Start(clock, op);
        var shown = incident.AddFile(clock, op, "nord.jpg", "image/jpeg", IncidentPdfTests.TinyJpeg.Length);
        incident.AddFile(clock, op, "sued.jpg", "image/jpeg", IncidentPdfTests.TinyJpeg.Length);

        var bytes = IncidentPdf.Generate(
            incident, Started, new Dictionary<Guid, byte[]> { [shown.Id] = IncidentPdfTests.TinyJpeg }, sections: IncidentPdfSections.Files);
        var text = PdfText.Of(bytes);

        // Listed in the table, then captioned again above the image itself.
        PdfText.AssertInOrder(text, "nord.jpg Huber", "sued.jpg Huber", "nord.jpg");
        Assert.Equal(2, PdfText.Count(text, "nord.jpg"));
        Assert.Equal(1, PdfText.Count(text, "sued.jpg"));

        // The header's brand mark, plus the one image.
        Assert.Equal([2], PdfText.ImagesPerPage(bytes));
    }

    // A PDF attachment is appended after the report (PdfAttachmentMerger), never drawn inline,
    // even when its bytes are at hand.
    [Fact]
    public void A_pdf_attachment_is_not_drawn_inline_even_with_its_bytes()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Huber");
        var incident = Incident.Start(clock, op);
        var attachment = incident.AddFile(clock, op, "plan.pdf", "application/pdf", 100);

        var bytes = IncidentPdf.Generate(
            incident, Started, new Dictionary<Guid, byte[]> { [attachment.Id] = IncidentPdfTests.TinyJpeg }, sections: IncidentPdfSections.Files);

        Assert.Equal(1, PdfText.Count(PdfText.Of(bytes), "plan.pdf"));
        Assert.Equal([1], PdfText.ImagesPerPage(bytes));
    }

    [Fact]
    public void Without_files_the_section_says_so_instead_of_an_empty_table()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Huber"));

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Files);

        Assert.Contains("Angehängte Dateien — keine Einträge —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hinzugefügt von", text, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
