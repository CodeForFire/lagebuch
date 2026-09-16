using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class JsonLastPdfExportStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"last-pdf-export-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Missing_file_returns_null()
    {
        var store = new JsonLastPdfExportStore(_path);
        Assert.Null(store.GetLastExport());
    }

    [Fact]
    public void SetLastExport_persists_and_overwrites()
    {
        var exportedAt = new DateTimeOffset(2026, 6, 22, 9, 30, 0, TimeSpan.FromHours(2));
        new JsonLastPdfExportStore(_path).SetLastExport("/einsaetze/2026-06-22.pdf", exportedAt);

        var first = new JsonLastPdfExportStore(_path).GetLastExport();
        Assert.Equal("/einsaetze/2026-06-22.pdf", first?.Path);
        Assert.Equal(exportedAt, first?.ExportedAt);

        var overwrittenAt = exportedAt.AddMinutes(10);
        new JsonLastPdfExportStore(_path).SetLastExport("/einsaetze/2026-06-22-v2.pdf", overwrittenAt);

        var second = new JsonLastPdfExportStore(_path).GetLastExport();
        Assert.Equal("/einsaetze/2026-06-22-v2.pdf", second?.Path);
        Assert.Equal(overwrittenAt, second?.ExportedAt);
    }
}
