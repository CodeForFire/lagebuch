using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace LageBuch.Documents.Tests;

public class SmokeTest
{
    [Fact]
    public void Can_generate_a_trivial_pdf_under_community_license()
    {
        PdfLicense.Ensure();

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Content().Text("Test");
            });
        }).GeneratePdf();

        PdfAssert.IsPdf(bytes);
    }
}
