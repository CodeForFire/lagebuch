using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class TasksSection
{
    private static readonly string[] HeaderTitles = [string.Empty, "Wichtig", "Dringlich", "Fällig", "Zugeteilt", "Aufgabe", "Erledigt"];

    public static void Compose(IContainer container, Incident incident)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Aufgaben").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.Tasks.Count == 0)
            {
                column.Item().Text("— keine Aufgaben —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            var sorted = incident.Tasks
                .OrderBy(t => t.IsCompleted ? 1 : 0)
                .ThenByDescending(t => t.Urgency)
                .ThenByDescending(t => t.Importance)
                .ThenBy(t => t.CreatedAt);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(22);
                    columns.ConstantColumn(42);
                    columns.ConstantColumn(42);
                    columns.ConstantColumn(60);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    foreach (var title in HeaderTitles)
                    {
                        header.Cell().Element(HeaderCell).Text(title).SemiBold();
                    }
                });

                foreach (var task in sorted)
                {
                    var overdue = !task.IsCompleted && task.DueAt <= DateTimeOffset.Now;
                    table.Cell().Element(BodyCell).Text(task.IsCompleted ? "✔" : "○");
                    table.Cell().Element(BodyCell).Text(Formatting.Level(task.Importance));
                    table.Cell().Element(BodyCell).Text(Formatting.Level(task.Urgency));
                    table.Cell().Element(BodyCell).Column(col =>
                    {
                        col.Item().Text(overdue ? "FÄLLIG" : Formatting.Timestamp(task.DueAt));
                        if (overdue)
                        {
                            col.Item().Text($"fällig {Formatting.Timestamp(task.DueAt)}")
                                .FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                        }
                    });
                    table.Cell().Element(BodyCell).Text(Formatting.OrDash(task.Assignee));
                    table.Cell().Element(BodyCell).Column(col =>
                    {
                        col.Item().Text(task.Text);
                        col.Item().Text($"erstellt {Formatting.Timestamp(task.CreatedAt)} von {task.CreatedBy}")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Medium);
                    });
                    table.Cell().Element(BodyCell).Text(
                        task.CompletedAt is { } done ? Formatting.Timestamp(done) : "—");
                }
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(4).BorderBottom(1).BorderColor(Colors.Grey.Medium);

    private static IContainer BodyCell(IContainer c) =>
        c.PaddingVertical(2).PaddingHorizontal(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
}
