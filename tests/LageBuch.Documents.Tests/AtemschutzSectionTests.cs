using LageBuch.Documents.Sections;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

// #426: the Atemschutz table printed neither a Trupp's status nor when "Druck akt." was measured,
// and the Druckabfragen reached the PDF only as scattered ETB lines. The sub-line's wording is
// pinned through DetailLine directly; the Pdf_ tests check that the table actually prints it.
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
    public void Detail_line_puts_each_druckabfrages_ort_beside_its_pressure()
    {
        var (incident, trupp, clock) = UnderAir();
        clock.Now = Started.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, 260, "EG");
        clock.Now = Started.AddMinutes(10);
        incident.RecordScbaPressure(clock, trupp.Id, 220);

        Assert.Equal(
            "Status: Im Einsatz · Messreihe: 08:15 260 bar (EG) · 08:20 220 bar",
            AtemschutzSection.DetailLine(trupp, Started.AddMinutes(11)));
    }

    // One reading with an Ort says something "Druck akt." does not, so it is not left out.
    [Fact]
    public void Detail_line_keeps_a_single_reading_that_has_an_ort()
    {
        var (incident, trupp, clock) = UnderAir();
        clock.Now = Started.AddMinutes(4);
        incident.RecordScbaPressure(clock, trupp.Id, 260, "2. OG");

        Assert.Equal(
            "Status: Im Einsatz · Messreihe: 08:14 260 bar (2. OG)",
            AtemschutzSection.DetailLine(trupp, Started.AddMinutes(5)));
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
    public void Pdf_prints_each_trupp_with_its_times_pressures_sicherheitstrupp_and_status()
    {
        var clock = new FixedClock(Started);
        var incident = Incident.Start(clock, new SessionOperator("Huber"));
        var trupp = incident.AddScbaTrupp(clock, "AT", TruppMember.Crew("Ott", "Ast"), entryPressure: 300, callSign: "1/40");
        var waiting = incident.AddScbaTrupp(clock, "ST", TruppMember.Crew("Kurz", "Lang"), entryPressure: 290, callSign: "1/44");
        incident.SetScbaSafetyTrupp(trupp.Id, waiting.Id);
        clock.Now = Started.AddMinutes(2);
        incident.StartScbaTrupp(clock, trupp.Id);
        clock.Now = Started.AddMinutes(7);
        incident.RecordScbaPressure(clock, trupp.Id, 260);
        clock.Now = Started.AddMinutes(12);
        incident.RecordScbaPressure(clock, trupp.Id, 220);
        clock.Now = Started.AddMinutes(15);
        incident.WithdrawScbaTrupp(clock, trupp.Id);
        clock.Now = Started.AddMinutes(18);
        incident.MarkScbaRemoved(clock, trupp.Id);

        var text = PdfText.Of(incident, Started.AddMinutes(20), IncidentPdfSections.Atemschutz);

        PdfText.AssertInOrder(
            text,
            "Atemschutzüberwachung",
            "Trupp Mannschaft",
            "Start Rückzug Ende Druck Start Druck akt.",
            "AT Ott / Ast 1/40",
            "22.06.2026 08:12 22.06.2026 08:25 22.06.2026 08:28 300 bar 220 bar 08:22",
            "Sicherheitstrupp: 1/44 · ST",
            "Status: Abgenommen · Messreihe: 08:17 260 bar · 08:22 220 bar",
            "ST Kurz / Lang 1/44 — — — 290 bar 290 bar Status: Bereitgestellt");
        Assert.Equal(1, PdfText.Count(text, "Sicherheitstrupp:"));
        Assert.Equal(0, PdfText.Count(text, "Si.-Trupp"));
    }

    [Fact]
    public void Safety_line_names_the_posted_trupp_and_is_absent_without_one()
    {
        var clock = new FixedClock(Started);
        var incident = Incident.Start(clock, new SessionOperator("Huber"));
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Ott", "Ast"), entryPressure: 300);
        var waiting = incident.AddScbaTrupp(clock, "Sicherheitstrupp", TruppMember.Crew("Kurz", "Lang"), entryPressure: 290, callSign: "1/44");

        Assert.Null(AtemschutzSection.SafetyLine(incident, trupp));

        incident.SetScbaSafetyTrupp(trupp.Id, waiting.Id);

        Assert.Equal("Sicherheitstrupp: 1/44 · Sicherheitstrupp", AtemschutzSection.SafetyLine(incident, trupp));
        Assert.Null(AtemschutzSection.SafetyLine(incident, waiting));
    }

    [Fact]
    public void Without_trupps_the_section_says_so_instead_of_an_empty_table()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Huber"));

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Atemschutz);

        Assert.Contains("Atemschutzüberwachung — keine Einträge —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mannschaft", text, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
