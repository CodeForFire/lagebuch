using QuestPDF.Infrastructure;

namespace Feuerwehr.Documents;

/// <summary>
/// Configures the QuestPDF license. Community license is free and valid for
/// open-source projects and small organisations. Idempotent and thread-safe.
/// </summary>
public static class PdfLicense
{
    private static int _configured;

    public static void Ensure()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 0)
            QuestPDF.Settings.License = LicenseType.Community;
    }
}
