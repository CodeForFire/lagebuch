using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Time;
using UglyToad.PdfPig;

namespace LageBuch.Documents.Tests;

public class FuehrungsorganisationSectionTests
{
    private const string Heading = "Führungsorganisation";

    private static readonly DateTimeOffset Started = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly FixedClock Clock = new(Started);
    private static readonly SessionOperator Op = new("Huber");

    private static Incident Fresh() => Incident.Start(Clock, Op);

    private static string Print(Incident incident) =>
        PdfText.Of(incident, Started, IncidentPdfSections.Fuehrungsorganisation);

    private static RoleAssignment El(Incident incident) =>
        incident.AssignRole(Clock, Op, "EL", "Max Mustermann", callSign: "Florian Emmering 1", slot: RoleSlot.Leiter);

    private static Incident FullBoard()
    {
        var incident = Fresh();
        El(incident);
        incident.SetFuehrungsstufe(Clock, Op, Fuehrungsstufe.B);
        incident.UpdateLeadershipPost(Clock, Op, null, null, null, "TMO FW_EMMERING 1000");
        incident.AssignRole(Clock, Op, "Führungsassistent", "Anna Beispiel", slot: RoleSlot.Fuehrungsassistent);

        incident.AddCommandNode(Clock, Op, CommandNodeKind.Bereitstellung, "Bereitstellung Marktplatz");
        var nord = incident.AddCommandNode(
            Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord", auftrag: "Brandbekämpfung", funkgruppe: "TMO FW_EMMERING 1001");
        incident.AssignRole(Clock, Op, "Abschnittsleiter", "Erika Muster", nodeId: nord.Id, slot: RoleSlot.Leiter);
        incident.AssignRole(Clock, Op, "Melder", "Paul Probe", nodeId: nord.Id, slot: RoleSlot.Melder);
        incident.AssignRole(Clock, Op, "Sichter", "Tim Test", nodeId: nord.Id);
        incident.AddCommandNode(Clock, Op, CommandNodeKind.Unterabschnitt, "Dach", nord.Id);
        var sued = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 2 Süd");
        incident.AddCommandNode(Clock, Op, CommandNodeKind.Partner, "Rettungsdienst");
        incident.AssignRole(Clock, Op, "Fachberater THW", "Peter Maier", slot: RoleSlot.Fachberater);

        var emmering = incident.AddForceUnit(Clock, Op, "Emmering", 6, callSign: "Florian Emmering 11/1");
        var aich = incident.AddForceUnit(Clock, Op, "Aich", 4);
        incident.AddForceUnit(Clock, Op, "Emmering", 3, callSign: "Florian Emmering 11/2");
        incident.MoveForceUnit(Clock, Op, emmering.Id, nord.Id);
        incident.MoveForceUnit(Clock, Op, aich.Id, sued.Id);
        incident.AddForceUnit(Clock, Op, "Steinhöring", 3, callSign: "Florian Steinhöring 20/1");

        incident.AssignRole(Clock, Op, "Verkehrslenkung", "Karl Kurz", section: "Zufahrt");
        return incident;
    }

    [Fact]
    public void Nothing_is_printed_without_nodes_and_without_a_slotted_assignment()
    {
        var incident = Fresh();
        incident.AssignRole(Clock, Op, "Sichter", "Tim Test");

        Assert.DoesNotContain(Heading, Print(incident), StringComparison.Ordinal);
    }

    [Fact]
    public void An_Einsatzleiter_alone_is_enough_to_print_the_section()
    {
        var incident = Fresh();
        El(incident);

        var text = Print(incident);

        PdfText.AssertInOrder(text, Heading, "Einsatzleitung Feuerwehr – EL Max Mustermann (Florian Emmering 1)");
        Assert.DoesNotContain("Stufe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Funkgruppe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tree_prints_in_board_order()
    {
        var text = Print(FullBoard());

        PdfText.AssertInOrder(
            text,
            Heading,
            "Einsatzleitung Feuerwehr – EL Max Mustermann (Florian Emmering 1) · Stufe B · Funkgruppe TMO FW_EMMERING 1000",
            "Führungsassistenz: Führungsassistent Anna Beispiel",
            "Bereitstellung Bereitstellung Marktplatz",
            "Abschnitt Abschnitt 1 Nord – Auftrag: Brandbekämpfung – Leiter: Erika Muster – Melder: Paul Probe – Funkgruppe: TMO FW_EMMERING 1001",
            "Einheiten: Florian Emmering 11/1",
            "Weitere: Sichter Tim Test",
            "Unterabschnitt Abschnitt 1 Nord / Dach",
            "Abschnitt Abschnitt 2 Süd",
            "Einheiten: Aich",
            "Partner Rettungsdienst",
            "Fachberater: Fachberater THW (Peter Maier)",
            "Nicht zugeordnet: Florian Emmering 11/2, Florian Steinhöring 20/1",
            "Abschnitt (Freitext) Zufahrt: Verkehrslenkung Karl Kurz");
    }

    [Fact]
    public void A_nested_Unterabschnitt_is_indented_deeper_than_its_Abschnitt()
    {
        var lefts = LineLefts(FullBoard());

        var el = lefts["Einsatzleitung Feuerwehr –"];
        var assistenz = lefts["Führungsassistenz: Führungsassistent Anna"];
        var bereitstellung = lefts["Bereitstellung Bereitstellung Marktplatz"];
        var nord = lefts["Abschnitt Abschnitt 1"];
        var dach = lefts["Unterabschnitt Abschnitt 1"];
        var nordUnits = lefts["Einheiten: Florian Emmering"];
        var partner = lefts["Partner Rettungsdienst"];

        Assert.True(bereitstellung > el + 5);
        Assert.True(assistenz > el + 5);
        Assert.True(Math.Abs(nord - bereitstellung) < 1);
        Assert.True(dach > nord + 5);
        Assert.True(nordUnits > nord + 5);
        Assert.True(Math.Abs(partner - el) < 1);
    }

    [Fact]
    public void Optional_clauses_appear_only_when_they_have_a_value()
    {
        var incident = Fresh();
        El(incident);
        incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");

        var text = Print(incident);

        Assert.Contains("Abschnitt Abschnitt 1 Nord", text, StringComparison.Ordinal);
        foreach (var clause in new[] { "Auftrag", "Leiter:", "Melder", "Funkgruppe", "Einheiten", "Weitere", "Fachberater", "Nicht zugeordnet", "Freitext", "Stufe" })
        {
            Assert.DoesNotContain(clause, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_dissolved_Abschnitt_and_an_ended_Leiter_are_not_printed()
    {
        var incident = Fresh();
        var el = El(incident);
        var ost = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 3 Ost");
        incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");
        incident.DissolveCommandNode(Clock, Op, ost.Id);
        incident.EndRoleAssignment(el.Id, Started.AddMinutes(30));

        var text = Print(incident);

        Assert.DoesNotContain("Abschnitt 3 Ost", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mustermann", text, StringComparison.Ordinal);
        Assert.Contains("Abschnitt Abschnitt 1 Nord", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_is_part_of_All_and_skipped_when_deselected()
    {
        var incident = FullBoard();

        Assert.True(IncidentPdfSections.All.HasFlag(IncidentPdfSections.Fuehrungsorganisation));
        Assert.Contains(Heading, PdfText.Of(incident, Started, IncidentPdfSections.All), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Heading,
            PdfText.Of(incident, Started, IncidentPdfSections.All & ~IncidentPdfSections.Fuehrungsorganisation),
            StringComparison.Ordinal);
    }

    // The left edge of each line's first word, keyed by the line's first words, to compare indents.
    private static Dictionary<string, double> LineLefts(Incident incident)
    {
        using var document = PdfDocument.Open(IncidentPdf.Generate(incident, Started, sections: IncidentPdfSections.Fuehrungsorganisation));
        var lefts = new Dictionary<string, double>();
        foreach (var line in document.GetPages().SelectMany(p => p.GetWords())
            .GroupBy(w => Math.Round(w.Letters[0].StartBaseLine.Y)))
        {
            var words = line.OrderBy(w => w.BoundingBox.Left).ToList();
            var key = string.Join(' ', words.Take(words.Count > 3 ? 3 : words.Count).Select(w => w.Text));
            lefts.TryAdd(key, words[0].BoundingBox.Left);
        }

        return lefts;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
