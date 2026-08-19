using Feuerwehr.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Feuerwehr.Documents.Sections;

public static class ChecklistSection
{
    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Checkliste").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.ChecklistAufbau.Count == 0 && incident.ChecklistAbbau.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            ComposeList(column, "Aufbau", incident.ChecklistAufbau);
            ComposeList(column, "Abbau", incident.ChecklistAbbau);
        });
    }

    private static void ComposeList(QuestPDF.Fluent.ColumnDescriptor column, string title, IReadOnlyList<ChecklistItem> items)
    {
        if (items.Count == 0)
            return;

        column.Item().Text(title).FontSize(11).SemiBold();

        foreach (var item in items)
        {
            column.Item().Row(row =>
            {
                row.ConstantItem(20).Text(item.IsDone ? "[x]" : "[ ]");
                row.RelativeItem().Text(t =>
                {
                    if (item.IsMandatory)
                        t.Span("Pflicht: ").SemiBold().FontColor(Colors.Red.Darken1);
                    t.Span(item.Text);
                    if (!string.IsNullOrWhiteSpace(item.Note))
                        t.Span($"  ({item.Note})").FontColor(Colors.Grey.Darken1);
                });
            });
        }
    }
}
