using Feuerwehr.Domain;
using QuestPDF.Fluent;

namespace Feuerwehr.Documents;

public static class IncidentPdf
{
    public static byte[] Generate(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        PdfLicense.Ensure();
        return new IncidentReportDocument(incident).GeneratePdf();
    }
}
