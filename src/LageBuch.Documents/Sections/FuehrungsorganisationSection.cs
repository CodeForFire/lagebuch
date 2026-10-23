using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

/// <summary>
/// The Führungsorganisation as an indented text tree: who leads what and who reports to whom (#703).
/// Everything comes from <see cref="ReportingLine"/>, so it cannot drift from the board.
/// </summary>
public static class FuehrungsorganisationSection
{
    private const float IndentPoints = 14;

    /// <summary>
    /// Whether the incident has anything to show: a live node or a running assignment in a slot.
    /// A section with nothing in it is left out, heading included.
    /// </summary>
    public static bool HasContent(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return incident.CommandNodes.Any(n => n.IsLive)
            || incident.Roles.Any(r => r.To is null && r.Slot != RoleSlot.None);
    }

    public static void Compose(IContainer container, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (!HasContent(incident))
        {
            return;
        }

        container.Column(column =>
        {
            column.Spacing(2);
            column.Item().PaddingBottom(2).Text("Führungsorganisation").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            foreach (var entry in ReportingLine.Of(incident))
            {
                foreach (var (level, text) in Lines(incident, entry))
                {
                    column.Item().PaddingLeft(level * IndentPoints).Text(text);
                }
            }

            var unplaced = ReportingLine.UnplacedUnits(incident);
            if (unplaced.Count > 0)
            {
                column.Item().Text($"Nicht zugeordnet: {string.Join(", ", unplaced.Select(Title))}");
            }

            foreach (var (section, assignments) in ReportingLine.FreeTextSections(incident))
            {
                column.Item().Text($"Abschnitt (Freitext) {section}: {People(assignments, withParentheses: false)}");
            }
        });
    }

    private static IEnumerable<(int Level, string Text)> Lines(Incident incident, ReportingEntry entry)
    {
        if (entry.Kind == ReportingEntryKind.Fachberater)
        {
            yield return (entry.Depth, $"Fachberater: {People(entry.Others, withParentheses: true)}");
            yield break;
        }

        yield return (entry.Depth, Headline(incident, entry));

        var sub = entry.Depth + 1;
        if (entry.Kind == ReportingEntryKind.Einsatzleitung)
        {
            foreach (var assistant in entry.Others)
            {
                yield return (sub, $"Führungsassistenz: {assistant.Role} {assistant.PersonName}");
            }

            yield break;
        }

        if (entry.Units.Count > 0)
        {
            yield return (sub, $"Einheiten: {string.Join(", ", entry.Units.Select(Title))}");
        }

        if (entry.Others.Count > 0)
        {
            yield return (sub, $"Weitere: {People(entry.Others, withParentheses: false)}");
        }
    }

    private static string Headline(Incident incident, ReportingEntry entry)
    {
        if (entry.Kind == ReportingEntryKind.Einsatzleitung)
        {
            var line = "Einsatzleitung Feuerwehr";
            if (entry.Leiter is { } leiter)
            {
                line += $" – EL {leiter.PersonName}";
                if (!string.IsNullOrWhiteSpace(leiter.CallSign))
                {
                    line += $" ({leiter.CallSign})";
                }
            }

            if (incident.Fuehrungsstufe is { } stufe)
            {
                line += $" · Stufe {stufe}";
            }

            if (!string.IsNullOrWhiteSpace(incident.LeadershipPost.Funkgruppe))
            {
                line += $" · Funkgruppe {incident.LeadershipPost.Funkgruppe}";
            }

            return line;
        }

        var node = entry.Node!;
        var label = entry.Kind switch
        {
            ReportingEntryKind.Bereitstellung => "Bereitstellung",
            ReportingEntryKind.Unterabschnitt => "Unterabschnitt",
            ReportingEntryKind.Partner => "Partner",
            _ => "Abschnitt",
        };
        var name = entry.Kind == ReportingEntryKind.Unterabschnitt ? incident.NodeLabel(node.Id) ?? node.Name : node.Name;
        var text = $"{label} {name}";
        if (!string.IsNullOrWhiteSpace(node.Auftrag))
        {
            text += $" – Auftrag: {node.Auftrag}";
        }

        if (entry.Leiter is { } nodeLeiter)
        {
            text += $" – Leiter: {nodeLeiter.PersonName}";
        }

        if (entry.Melder is { } melder)
        {
            text += $" – Melder: {melder.PersonName}";
        }

        if (!string.IsNullOrWhiteSpace(node.Funkgruppe))
        {
            text += $" – Funkgruppe: {node.Funkgruppe}";
        }

        return text;
    }

    private static string Title(ForceUnit unit) => string.IsNullOrWhiteSpace(unit.CallSign) ? unit.Brigade : unit.CallSign;

    private static string People(IEnumerable<RoleAssignment> assignments, bool withParentheses) =>
        string.Join(", ", assignments.Select(a => withParentheses ? $"{a.Role} ({a.PersonName})" : $"{a.Role} {a.PersonName}"));
}
