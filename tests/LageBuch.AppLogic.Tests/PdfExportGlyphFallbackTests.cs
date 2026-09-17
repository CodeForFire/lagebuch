using LageBuch.Domain;
using LageBuch.Domain.Tasks;

namespace LageBuch.AppLogic.Tests;

// An export carries text the operator typed under time pressure -- ETB entries, task
// titles, attachment names. The bundled font cannot possibly cover every character
// someone might reach for, and QuestPDF's default is to abort the whole document over
// one it cannot draw. Losing the Einsatz documentation because of a single emoji is a
// far worse outcome than a gap where that emoji was, so PdfLicense.Ensure turns that
// off for production and these hold it that way.
//
// This has to live here rather than in LageBuch.Documents.Tests: that assembly pins the
// opposite setting to police the text LageBuch itself writes, and each test assembly
// runs in its own process, so the two rules do not collide.
public class PdfExportGlyphFallbackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData("Lüfter aufbauen 🔥")] // emoji -- no font LageBuch ships covers these
    [InlineData("Tür öffnen ✔")] // the checkmark that started all this
    [InlineData("Проверить подвал")] // Cyrillic, e.g. a transcribed name
    public async Task An_unrenderable_character_in_typed_text_still_produces_a_pdf(string title)
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller");
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        session.Incident.AddTask(clock, op, title, null, TaskImportance.High, TaskUrgency.High, 5);

        var bytes = await session.ExportPdfAsync(new TestPdfExporter());

        Assert.True(bytes.Length > 100);
        Assert.Equal(0x25, bytes[0]); // '%' -- a real PDF came back rather than an exception
    }
}
