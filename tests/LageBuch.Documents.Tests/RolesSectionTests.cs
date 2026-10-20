using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class RolesSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly FixedClock Clock = new(Started);
    private static readonly SessionOperator Op = new("Huber");

    [Fact]
    public void Each_assignment_prints_every_column_in_the_order_it_was_made()
    {
        var incident = Incident.Start(Clock, Op);
        var el = incident.AssignRole(
            Clock, Op, "EL", "Mayr", callSign: "1/40", from: Started.AddMinutes(5), section: "Nord", phone: "0171");
        incident.EndRoleAssignment(el.Id, Started.AddMinutes(65));
        incident.AssignRole(Clock, Op, "ZF", "Bauer");

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Roles);

        PdfText.AssertInOrder(
            text,
            "Funktionszuweisung",
            "Funktion Abschnitt Name",
            "Von Bis",
            "EL Nord Mayr 1/40 0171 22.06.2026 09:05 22.06.2026 10:05",
            "ZF — Bauer — — — —");

        // All seven cells on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "EL Nord Mayr 1/40 0171 22.06.2026 09:05 22.06.2026 10:05",
            PdfText.LinesOf(incident, Started, IncidentPdfSections.Roles));
    }

    [Fact]
    public void A_running_assignment_has_a_start_but_no_end()
    {
        var incident = Incident.Start(Clock, Op);
        incident.AssignRole(Clock, Op, "EL", "Mayr", from: Started.AddMinutes(5));

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Roles);

        Assert.Contains("EL — Mayr — — 22.06.2026 09:05 —", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_assignments_the_section_says_so_instead_of_an_empty_table()
    {
        var incident = Incident.Start(Clock, Op);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Roles);

        Assert.Contains("Funktionszuweisung — keine Einträge —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Funktion Abschnitt", text, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
