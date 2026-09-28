using LageBuch.Documents.Sections;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

// #426: the Atemschutz table printed neither a Trupp's status nor when "Druck akt." was measured,
// and the Druckabfragen reached the PDF only as scattered ETB lines. Text is not extracted in this
// suite (see PdfAssert), so the sub-line's wording is pinned through DetailLine directly.
public class AtemschutzSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 6, 22, 8, 10, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, AtemschutzTrupp Trupp, FixedClock Clock) UnderAir()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Huber", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        var trupp = incident.AddScbaTrupp(
            clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);
        return (incident, trupp, clock);
    }

    [Fact]
    public void Detail_line_names_the_status_at_export_time()
    {
        var (_, trupp, _) = UnderAir();

        Assert.Equal("Status: Im Einsatz", AtemschutzSection.DetailLine(trupp, Started.AddMinutes(1)));
        Assert.Equal("Status: Druckabfrage", AtemschutzSection.DetailLine(trupp, Started.AddMinutes(10)));
    }

    [Fact]
    public void Detail_line_lists_the_messreihe_from_two_readings()
    {
        var (incident, trupp, clock) = UnderAir();
        clock.Now = Started.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, 260);
        clock.Now = Started.AddMinutes(10);
        incident.RecordScbaPressure(clock, trupp.Id, 220);
        clock.Now = Started.AddMinutes(12);
        incident.WithdrawScbaTrupp(clock, trupp.Id);
        clock.Now = Started.AddMinutes(20);
        incident.MarkScbaRemoved(clock, trupp.Id);

        Assert.Equal(
            "Status: Abgenommen · Messreihe: 08:15 260 bar · 08:20 220 bar",
            AtemschutzSection.DetailLine(trupp, Started.AddHours(3)));
    }

    // With one reading the series would only repeat "Druck akt." and its time -- the CO
    // Messreihe follows the same rule (#424).
    [Fact]
    public void Detail_line_omits_the_messreihe_for_a_single_reading()
    {
        var (incident, trupp, clock) = UnderAir();
        clock.Now = Started.AddMinutes(4);
        incident.RecordScbaPressure(clock, trupp.Id, 260);

        Assert.Equal("Status: Im Einsatz", AtemschutzSection.DetailLine(trupp, Started.AddMinutes(5)));
    }

    [Fact]
    public void Last_reading_time_is_that_of_the_latest_druckabfrage()
    {
        var (incident, trupp, clock) = UnderAir();
        Assert.Null(AtemschutzSection.LastReadingTime(trupp));

        clock.Now = Started.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, 260);
        clock.Now = Started.AddMinutes(9);
        incident.RecordScbaPressure(clock, trupp.Id, 230);

        Assert.Equal("08:19", AtemschutzSection.LastReadingTime(trupp));
    }

    [Fact]
    public void Pdf_renders_an_atemschutz_table_with_status_and_messreihe()
    {
        var (incident, trupp, clock) = UnderAir();
        var waiting = incident.AddScbaTrupp(
            clock, "Sicherheitstrupp", TruppMember.Crew("Huber", "Bauer"), entryPressure: 300);
        incident.SetScbaSafetyTrupp(trupp.Id, waiting.Id);
        clock.Now = Started.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, 260);
        clock.Now = Started.AddMinutes(10);
        incident.RecordScbaPressure(clock, trupp.Id, 220);

        var pdf = IncidentPdf.Generate(incident, Started.AddMinutes(30), new Dictionary<Guid, byte[]>());

        PdfAssert.IsPdf(pdf);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
