using LageBuch.Domain.SituationReports;

namespace LageBuch.Domain.Tests;

public class SituationReportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));
    private static readonly SessionOperator Op = new("Muster", "ELW 1");

    [Fact]
    public void Funkspruch_joins_the_filled_in_parts_in_spoken_order_and_skips_blank_ones()
    {
        var report = SituationReport.Create(T0, "ELW 1", " Blumenstraße 13 ", "Schuppen im Vollbrand", "  ", null, "1 DLK", Op);

        Assert.Equal("Blumenstraße 13 · Schuppen im Vollbrand · Nachforderung: 1 DLK", report.Funkspruch);
    }

    [Fact]
    public void Meldender_is_not_part_of_the_funkspruch()
    {
        var report = SituationReport.Create(T0, "ELW 1", null, "Lage unverändert", null, null, null, Op);

        Assert.Equal("Lage unverändert", report.Funkspruch);
    }

    [Fact]
    public void Funkspruch_is_empty_while_nothing_is_filled_in()
    {
        var report = SituationReport.Create(T0, "ELW 1", null, null, null, null, null, Op);

        Assert.Equal(string.Empty, report.Funkspruch);
    }

    [Fact]
    public void Fields_are_trimmed_and_blank_ones_become_null()
    {
        var report = SituationReport.Create(T0, "  ELW 1 ", string.Empty, " Lage ", null, null, null, Op);

        Assert.Equal("ELW 1", report.Reporter);
        Assert.Null(report.Location);
        Assert.Equal("Lage", report.Situation);
        Assert.Equal(Op.Display, report.CreatedBy);
    }

    [Fact]
    public void A_field_over_its_cap_is_rejected()
    {
        var tooLong = new string('x', SituationReport.MaxFieldLength + 1);

        Assert.Throws<ArgumentException>(() => SituationReport.Create(T0, null, null, tooLong, null, null, null, Op));
    }

    [Fact]
    public void The_longest_possible_funkspruch_still_fits_one_etb_entry()
    {
        var field = new string('x', SituationReport.MaxFieldLength);
        var report = SituationReport.Create(
            T0, null, new string('x', SituationReport.MaxLocationLength), field, field, field, field, Op);

        Assert.True($"Lagemeldung Nr. 999: {report.Funkspruch}".Length <= Etb.EtbEntry.MaxTextLength);
    }
}
