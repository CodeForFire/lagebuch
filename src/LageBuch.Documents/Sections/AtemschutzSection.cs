using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

public static class AtemschutzSection
{
    private static readonly string[] HeaderTitles =
        ["Trupp", "Mannschaft", "Funkrufname", "Start", "Rückzug", "Ende", "Druck Start", "Druck akt."];

    public static void Compose(IContainer container, Incident incident, DateTimeOffset asOf)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Atemschutzüberwachung").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (incident.ScbaTrupps.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2); // Trupp
                    columns.RelativeColumn(3); // Mannschaft
                    columns.RelativeColumn(2); // Funkrufname
                    columns.ConstantColumn(75); // Einstieg
                    columns.ConstantColumn(75); // Rückzug
                    columns.ConstantColumn(75); // Ausstieg
                    columns.ConstantColumn(60); // Einstiegsdruck
                    columns.ConstantColumn(60); // letzter Druck
                });

                table.Header(header =>
                {
                    foreach (var title in HeaderTitles)
                    {
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                    }
                });

                foreach (var trupp in incident.ScbaTrupps)
                {
                    // The Sicherheitstrupp (#399) and the status with its Messreihe (#426) ride as
                    // sub-lines here rather than taking columns of their own. A4 portrait less the
                    // 1.5 cm margins leaves ~510pt; the five constant columns already claim 345 of
                    // it, so the remaining 165pt splits 2/3/2 into roughly 47/71/47pt and Mannschaft
                    // already wraps. A ninth column would cut it to 55pt (relative) or 45pt
                    // (constant) and the crew — the thing this table exists to record — would stop
                    // being legible. A sub-line costs no width at all. A dangling id prints nothing,
                    // which is the right degradation.
                    table.Cell().Element(RowTop).Column(cell =>
                    {
                        cell.Item().Text(trupp.DisplayName);
                        if (trupp.SafetyTruppId is { } safetyId
                            && incident.FindScbaTruppOrDefault(safetyId) is { } safety)
                        {
                            cell.Item().Text($"Si.-Trupp: Trupp {safety.TruppNumber}")
                                .FontSize(8).FontColor(Colors.Grey.Darken1);
                        }
                    });

                    table.Cell().Element(RowTop).Text(trupp.MembersDisplay);
                    table.Cell().Element(RowTop).Text(Formatting.OrDash(trupp.CallSign));
                    table.Cell().Element(RowTop).Text(trupp.StartTime is { } s ? Formatting.Timestamp(s) : "—");
                    table.Cell().Element(RowTop).Text(trupp.WithdrawTime is { } w ? Formatting.Timestamp(w) : "—");
                    table.Cell().Element(RowTop).Text(trupp.ExitTime is { } e ? Formatting.Timestamp(e) : "—");
                    table.Cell().Element(RowTop).Text(trupp.EntryPressure is { } ep ? $"{ep} bar" : "—");
                    table.Cell().Element(RowTop).Column(cell =>
                    {
                        cell.Item().Text(trupp.LatestPressure is { } lp ? $"{lp} bar" : "—");
                        if (LastReadingTime(trupp) is { } at)
                        {
                            cell.Item().Text(at).FontSize(8).FontColor(Colors.Grey.Darken1);
                        }
                    });

                    // Grey like the CO Messreihe (#424): the page is scanned for red, and a past
                    // reading must not announce a danger that has already passed.
                    table.Cell().ColumnSpan((uint)HeaderTitles.Length).Element(Cells.Body)
                        .Text(DetailLine(trupp, asOf)).FontSize(8).FontColor(Colors.Grey.Darken1);
                }
            });
        });
    }

    /// <summary>
    /// The sub-line under a Trupp's row: its status at <paramref name="asOf"/>, then its Messreihe.
    /// The series appears only from two readings up — with one it just repeats "Druck akt." and the
    /// time beneath it, the same rule the CO Messreihe follows (#424).
    /// </summary>
    public static string DetailLine(AtemschutzTrupp trupp, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(trupp);
        var status = $"Status: {trupp.StatusLabel(asOf)}";
        if (trupp.PressureReadings.Count < 2)
        {
            return status;
        }

        var series = string.Join(" · ", trupp.PressureReadings.Select(r => $"{Formatting.TimeOfDay(r.Time)} {r.Bar} bar"));
        return $"{status} · Messreihe: {series}";
    }

    /// <summary>
    /// When "Druck akt." was measured, or null while it is still only the entry pressure — that
    /// value was not a Druckabfrage, and its moment is already in the Start column.
    /// </summary>
    public static string? LastReadingTime(AtemschutzTrupp trupp)
    {
        ArgumentNullException.ThrowIfNull(trupp);
        return trupp.PressureReadings.Count > 0 ? Formatting.TimeOfDay(trupp.PressureReadings[^1].Time) : null;
    }

    // A Trupp's main row carries no separator of its own: the sub-line beneath it closes the
    // block, so the two read as one entry rather than as two rows.
    private static IContainer RowTop(IContainer c) => c.PaddingTop(2).PaddingHorizontal(4);
}
