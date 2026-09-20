using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class ChecklistSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Checkliste").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.Checklists.Sum(l => l.Items.Count) == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            // Each list under its own name, taken from the incident file rather than from
            // Stammdaten: exporting an Einsatz months later must not depend on a template that
            // may since have been renamed or deleted.
            foreach (var list in incident.Checklists)
            {
                ComposeList(column, ChecklistDefaults.TitleOrFallback(list.Title), list.Items);
            }
        });
    }

    private static void ComposeList(QuestPDF.Fluent.ColumnDescriptor column, string title, IReadOnlyList<ChecklistItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        column.Item().Text(title).FontSize(11).SemiBold();

        foreach (var item in items)
        {
            column.Item().Row(row =>
            {
                row.ConstantItem(20).Text(item.IsDone ? "[x]" : "[ ]");
                row.RelativeItem().Text(t =>
                {
                    if (item.IsMandatory)
                    {
                        t.Span("Pflicht: ").SemiBold().FontColor(Colors.Red.Darken1);
                    }

                    t.Span(item.Text);
                    if (!string.IsNullOrWhiteSpace(item.Note))
                    {
                        t.Span($"  ({item.Note})").FontColor(Colors.Grey.Darken1);
                    }
                });
            });
        }
    }
}
