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

            if (incident.Checklist.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            foreach (var item in incident.Checklist)
            {
                column.Item().Row(row =>
                {
                    row.ConstantItem(20).Text(item.IsDone ? "[x]" : "[ ]");
                    row.RelativeItem().Text(t =>
                    {
                        t.Span(item.Text);
                        if (!string.IsNullOrWhiteSpace(item.Note))
                            t.Span($"  ({item.Note})").FontColor(Colors.Grey.Darken1);
                    });
                });
            }
        });
    }
}
