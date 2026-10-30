using LageBuch.Documents.Sections;
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
        incident.AssignRole(Clock, Op, "Einsatzleiter", "Max Mustermann", callSign: "Florian Emmering 1", slot: RoleSlot.Leiter);

    private static Incident FullBoard()
    {
        var incident = Fresh();
        El(incident);
        incident.SetFuehrungsstufe(Clock, Op, Fuehrungsstufe.B);
        incident.UpdateLeadershipPost(Clock, Op, null, null, null, new Funkgruppen(["FW_EMMERING 1000"], ["307_F"]));
        incident.AssignRole(Clock, Op, "S2 Lage", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);

        incident.AddCommandNode(Clock, Op, CommandNodeKind.Bereitstellung, "Bereitstellung Marktplatz");
        var nord = incident.AddCommandNode(
            Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord", auftrag: "Brandbekämpfung", funkgruppe: "DMO 301_F");
        incident.AssignRole(
            Clock, Op, "Abschnittsleiter", "Hans Huber", callSign: "Florian Aich 1", nodeId: nord.Id, slot: RoleSlot.Leiter);
        incident.AssignRole(Clock, Op, "Lagemelder", "Lena Lehner", nodeId: nord.Id, slot: RoleSlot.Melder);
        incident.AssignRole(Clock, Op, "Gerätewart", "Anton Reiter", nodeId: nord.Id);
        var dach = incident.AddCommandNode(Clock, Op, CommandNodeKind.Unterabschnitt, "Dach", nord.Id);
        var sued = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 2 Süd", funkgruppe: "DMO 302_F");
        incident.AssignRole(Clock, Op, "Abschnittsleiter", "Josef Bauer", nodeId: sued.Id, slot: RoleSlot.Leiter);
        var rettungsdienst = incident.AddCommandNode(
            Clock, Op, CommandNodeKind.Partner, "Rettungsdienst", funkgruppen: new Funkgruppen(["RD_FFB 3000"], null));
        incident.AssignRole(
            Clock, Op, "Einsatzleiter", "Sabine Schmid", callSign: "Rotkreuz Fürstenfeldbruck 1", nodeId: rettungsdienst.Id, slot: RoleSlot.Leiter);
        incident.AssignRole(Clock, Op, "Fachberater THW", "Peter Maier", slot: RoleSlot.Fachberater);

        var emmering = incident.AddForceUnit(Clock, Op, "Emmering", 6, callSign: "Florian Emmering 40/1");
        var aich = incident.AddForceUnit(Clock, Op, "Aich", 4, callSign: "Florian Aich 44/1");
        var onRoof = incident.AddForceUnit(Clock, Op, "Emmering", 3, callSign: "Florian Emmering 30/1");
        incident.AddForceUnit(Clock, Op, "Emmering", 3, callSign: "Florian Emmering 11/2");
        incident.MoveForceUnit(Clock, Op, emmering.Id, nord.Id);
        incident.MoveForceUnit(Clock, Op, aich.Id, sued.Id);
        incident.MoveForceUnit(Clock, Op, onRoof.Id, dach.Id);

        incident.AssignRole(Clock, Op, "Zugführer", "Maria Musterfrau", section: "Wache Süd");
        return incident;
    }

    [Fact]
    public void Nothing_is_printed_without_nodes_and_without_a_slotted_assignment()
    {
        var incident = Fresh();
        incident.AssignRole(Clock, Op, "Gerätewart", "Anton Reiter");

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
        Assert.DoesNotContain("TMO", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DMO", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Einsatzleitungs_Melder_and_a_further_Leiter_typed_on_FUNKTIONEN_are_printed()
    {
        var incident = Fresh();
        El(incident);
        incident.AssignRole(Clock, Op, "Melder", "Tobias Wagner", slot: RoleSlot.Melder);
        incident.AssignRole(Clock, Op, "Einsatzabschnittsleiter", "Hans Huber", slot: RoleSlot.Leiter);
        incident.AssignRole(Clock, Op, "S2 Lage", "Erika Musterfrau", slot: RoleSlot.Fuehrungsassistent);
        incident.SetFuehrungsstufe(Clock, Op, Fuehrungsstufe.C);

        PdfText.AssertInOrder(
            Print(incident),
            Heading,
            "Einsatzleitung Feuerwehr – EL Max Mustermann (Florian Emmering 1) – Melder: Tobias Wagner · Stufe C",
            "Führungsunterstützung: S2 Lage Erika Musterfrau",
            "Weitere: Einsatzabschnittsleiter Hans Huber");
    }

    [Fact]
    public void A_Melder_alone_at_the_Einsatzleitung_prints_the_section_with_him()
    {
        var incident = Fresh();
        incident.AssignRole(Clock, Op, "Melder", "Tobias Wagner", slot: RoleSlot.Melder);

        PdfText.AssertInOrder(Print(incident), Heading, "Einsatzleitung Feuerwehr – Melder: Tobias Wagner");
    }

    [Theory]
    [InlineData(RoleSlot.Leiter, null)]
    [InlineData(RoleSlot.Melder, null)]
    [InlineData(RoleSlot.Fuehrungsassistent, null)]
    [InlineData(RoleSlot.Fachberater, null)]
    [InlineData(RoleSlot.Leiter, "Wache Süd")]
    [InlineData(RoleSlot.Melder, "Wache Süd")]
    [InlineData(RoleSlot.Fuehrungsassistent, "Wache Süd")]
    [InlineData(RoleSlot.Fachberater, "Wache Süd")]
    public void Whoever_makes_the_section_print_is_in_it(RoleSlot slot, string? section)
    {
        // FUNKTIONEN stamps the Stammdaten slot on whatever is typed there, with or without an
        // Abschnitt; at the Einsatzleitung a second Leiter has to show as well as the first.
        var incident = Fresh();
        incident.AssignRole(Clock, Op, "Funktion", "Tobias Wagner", section: section, slot: slot);
        if (slot == RoleSlot.Leiter && section is null)
        {
            incident.AssignRole(Clock, Op, "Einsatzabschnittsleiter", "Hans Huber", slot: RoleSlot.Leiter);
        }

        Assert.True(FuehrungsorganisationSection.HasContent(incident));
        var text = Print(incident);
        Assert.Contains("Tobias Wagner", text, StringComparison.Ordinal);
        if (slot == RoleSlot.Leiter && section is null)
        {
            Assert.Contains("Hans Huber", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_Leiter_whose_node_is_missing_does_not_print_an_empty_section()
    {
        var lost = RoleAssignment.Create("Abschnittsleiter", "Tobias Wagner", nodeId: Guid.NewGuid(), slot: RoleSlot.Leiter);
        var incident = Incident.Rehydrate(
            Guid.NewGuid(),
            Started,
            IncidentState.Open,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ChecklistList>(),
            Array.Empty<Domain.Etb.EtbEntry>(),
            [lost],
            Array.Empty<ForceUnit>(),
            Array.Empty<Domain.Atemschutz.AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<IncidentTimerState>(),
            Array.Empty<Domain.Files.IncidentFile>(),
            Array.Empty<Domain.Tasks.IncidentTask>(),
            Array.Empty<Domain.CoMeasurement.Building>(),
            Array.Empty<Domain.CoMeasurement.Dwelling>(),
            Array.Empty<Domain.Involved.InvolvedParty>());

        Assert.False(FuehrungsorganisationSection.HasContent(incident));
        Assert.DoesNotContain(Heading, Print(incident), StringComparison.Ordinal);
    }

    [Fact]
    public void The_tree_prints_in_board_order_without_doubled_kind_labels()
    {
        var text = Print(FullBoard());

        PdfText.AssertInOrder(
            text,
            Heading,
            "Einsatzleitung Feuerwehr – EL Max Mustermann (Florian Emmering 1) · Stufe B · TMO FW_EMMERING 1000 · DMO 307_F",
            "Führungsunterstützung: S2 Lage Erika Musterfrau",
            "Bereitstellung Marktplatz",
            "Abschnitt 1 Nord – Auftrag: Brandbekämpfung – Leiter: Hans Huber – Melder: Lena Lehner – Funkgruppe: DMO 301_F – Stärke 0/0/9/9, AGT 0",
            "Einheiten: Florian Emmering 40/1",
            "Weitere: Gerätewart Anton Reiter",
            "Unterabschnitt Dach – Stärke 0/0/3/3, AGT 0",
            "Einheiten: Florian Emmering 30/1",
            "Abschnitt 2 Süd – Leiter: Josef Bauer – Funkgruppe: DMO 302_F – Stärke 0/0/4/4, AGT 0",
            "Einheiten: Florian Aich 44/1",
            "Einsatzleitung Rettungsdienst – Leiter: Sabine Schmid – Funkgruppen: TMO RD_FFB 3000",
            "Fachberater: Fachberater THW (Peter Maier)",
            "Nicht zugeordnet: Florian Emmering 11/2",
            "Abschnitt (Freitext) Wache Süd: Zugführer Maria Musterfrau");
        foreach (var doubled in new[] { "Bereitstellung Bereitstellung", "Abschnitt Abschnitt", "Unterabschnitt Abschnitt", "Partner" })
        {
            Assert.DoesNotContain(doubled, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_fuehrungsunterstuetzung_is_set_in_body_weight_under_the_bold_einsatzleitung()
    {
        using var document = PdfDocument.Open(IncidentPdf.Generate(FullBoard(), Started, sections: IncidentPdfSections.Fuehrungsorganisation));
        var words = document.GetPages().SelectMany(p => p.GetWords()).ToList();
        string FontOf(string text) => words.First(w => w.Text == text).Letters[0].FontName ?? string.Empty;

        Assert.NotEqual(FontOf("Einsatzleitung"), FontOf("Führungsunterstützung:"));
        Assert.Equal(FontOf("Einheiten:"), FontOf("Führungsunterstützung:"));
    }

    [Fact]
    public void A_nested_Unterabschnitt_is_a_deeper_headline_after_its_Abschnitts_details()
    {
        var lines = LinePositions(FullBoard());

        var el = lines["Einsatzleitung Feuerwehr – EL"];
        var assistenz = lines["Führungsunterstützung: S2 Lage Erika"];
        var bereitstellung = lines["Bereitstellung Marktplatz"];
        var nord = lines["Abschnitt 1 Nord –"];
        var nordUnits = lines["Einheiten: Florian Emmering 40/1"];
        var dach = lines["Unterabschnitt Dach – Stärke"];
        var dachUnits = lines["Einheiten: Florian Emmering 30/1"];
        var partner = lines["Einsatzleitung Rettungsdienst – Leiter:"];

        Assert.True(assistenz.Left > el.Left + 5);
        Assert.True(bereitstellung.Left > el.Left + 5);
        Assert.True(Math.Abs(nord.Left - bereitstellung.Left) < 1);
        Assert.True(nordUnits.Left > nord.Left + 5);
        Assert.True(dach.Left > nord.Left + 5);
        Assert.True(dachUnits.Left > dach.Left + 5);
        Assert.True(nordUnits.Y > dach.Y, "Abschnitt 1's Einheiten are printed before the Dach headline.");
        Assert.True(Math.Abs(partner.Left - el.Left) < 1);
    }

    [Fact]
    public void Optional_clauses_appear_only_when_they_have_a_value()
    {
        var incident = Fresh();
        El(incident);
        incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 1 Nord");

        var text = Print(incident);

        Assert.Contains("Abschnitt 1 Nord", text, StringComparison.Ordinal);
        foreach (var clause in new[] { "Auftrag", "Leiter:", "Melder", "Funkgruppe", "Einheiten", "Weitere", "Fachberater", "Nicht zugeordnet", "Freitext", "Stufe", "Stärke", "AGT" })
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
        Assert.Contains("Abschnitt 1 Nord", text, StringComparison.Ordinal);
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

    // Left edge and baseline of each line's first word, keyed by the line's first words.
    private static Dictionary<string, (double Left, double Y)> LinePositions(Incident incident)
    {
        using var document = PdfDocument.Open(IncidentPdf.Generate(incident, Started, sections: IncidentPdfSections.Fuehrungsorganisation));
        var lines = new Dictionary<string, (double Left, double Y)>();
        foreach (var line in document.GetPages().SelectMany(p => p.GetWords())
            .GroupBy(w => Math.Round(w.Letters[0].StartBaseLine.Y)))
        {
            var words = line.OrderBy(w => w.BoundingBox.Left).ToList();
            var key = string.Join(' ', words.Take(Math.Min(words.Count, 4)).Select(w => w.Text));
            lines.TryAdd(key, (words[0].BoundingBox.Left, line.Key));
        }

        return lines;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
