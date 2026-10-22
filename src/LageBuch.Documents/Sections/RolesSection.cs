using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class RolesSection
{
    private static readonly string[] HeaderTitles = ["Funktion", "Abschnitt", "Name", "Funkrufname", "Handynummer", "Von", "Bis"];

    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Funktionszuweisung").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.Roles.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                // The app's order (#696): Funktion, Abschnitt, Name, Funkrufname, Handynummer, then
                // the times. Seven columns on A4 portrait: only Name still gets meaningful slack, and
                // the two timestamps keep fixed widths so they never wrap mid-value.
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1); // Funktion
                    columns.RelativeColumn(1); // Abschnitt
                    columns.RelativeColumn(2); // Name
                    columns.RelativeColumn(1); // Funkrufname
                    columns.RelativeColumn(1); // Handynummer
                    columns.ConstantColumn(95); // Von
                    columns.ConstantColumn(95); // Bis
                });

                table.Header(header =>
                {
                    foreach (var title in HeaderTitles)
                    {
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                    }
                });

                foreach (var role in incident.Roles)
                {
                    table.Cell().Element(Cells.Body).Text(role.Role);
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(role.Section));
                    table.Cell().Element(Cells.Body).Text(role.PersonName);
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(role.CallSign));
                    table.Cell().Element(Cells.Body).Text(Formatting.OrDash(PhoneNumber.Display(role.Phone)));
                    table.Cell().Element(Cells.Body).Text(role.From is { } f ? Formatting.Timestamp(f) : "—");
                    table.Cell().Element(Cells.Body).Text(role.To is { } t ? Formatting.Timestamp(t) : "—");
                }
            });

            // Who reported to whom (#703), as a tree rather than an eighth column: a column would
            // squeeze the table past A4 portrait, and it would flatten exactly what the reader wants
            // to see. Only printed once someone actually reported to someone.
            if (incident.Roles.Any(r => r.ReportsToId is not null))
            {
                column.Item().PaddingTop(6).Text("Führungsstruktur").SemiBold();
                foreach (var (role, depth) in Hierarchy(incident.Roles))
                {
                    var label = role.Section is { } section ? $"{role.Label} · {section}" : role.Label;
                    var line = depth == 0 ? label : $"– {label}";
                    column.Item().PaddingLeft(14f * depth).Text(line);
                }
            }
        });
    }

    // Depth-first, in the order the assignments were made. A Funktion whose superior is not in the
    // file, or one caught in a loop a foreign build wrote, is printed as a root rather than lost.
    private static List<(RoleAssignment Role, int Depth)> Hierarchy(IReadOnlyList<RoleAssignment> roles)
    {
        var ids = roles.Select(r => r.Id).ToHashSet();
        var result = new List<(RoleAssignment, int)>();
        var printed = new HashSet<Guid>();

        void Visit(RoleAssignment role, int depth)
        {
            if (!printed.Add(role.Id))
            {
                return;
            }

            result.Add((role, depth));
            foreach (var child in roles.Where(r => r.ReportsToId == role.Id))
            {
                Visit(child, depth + 1);
            }
        }

        foreach (var root in roles.Where(r => r.ReportsToId is not { } up || !ids.Contains(up)))
        {
            Visit(root, 0);
        }

        foreach (var leftover in roles)
        {
            Visit(leftover, 0);
        }

        return result;
    }
}
