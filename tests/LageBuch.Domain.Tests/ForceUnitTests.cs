namespace LageBuch.Domain.Tests;

public class ForceUnitTests
{
    [Fact]
    public void Strength_format_is_ZF_GF_Mann_Gesamt()
    {
        var unit = ForceUnit.Create("FF Musterheim", personnelCount: 2, scbaCount: 0, officerCount: 1);
        Assert.Equal("0/1/1/2", unit.StrengthText);
    }

    [Fact]
    public void Without_officer_or_zugfuehrer_the_strength_reads_0_0_n_n()
    {
        var unit = ForceUnit.Create("Aich", personnelCount: 6);
        Assert.Equal("0/0/6/6", unit.StrengthText);
    }

    [Fact]
    public void Zugfuehrer_is_counted_and_leads_the_format()
    {
        var unit = ForceUnit.Create("FF Musterheim", personnelCount: 21, officerCount: 2, zugfuehrerCount: 1);
        Assert.Equal("1/2/18/21", unit.StrengthText);
    }

    [Fact]
    public void Officer_count_may_not_exceed_the_total()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ForceUnit.Create("FF Musterheim", personnelCount: 2, officerCount: 3));
    }

    [Fact]
    public void Negative_officer_count_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ForceUnit.Create("FF Musterheim", personnelCount: 2, officerCount: -1));
    }

    [Fact]
    public void Negative_zugfuehrer_count_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ForceUnit.Create("FF Musterheim", personnelCount: 2, zugfuehrerCount: -1));
    }

    [Fact]
    public void Officer_and_zugfuehrer_counts_together_may_not_exceed_the_total()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ForceUnit.Create("FF Musterheim", personnelCount: 2, officerCount: 1, zugfuehrerCount: 2));
    }
}
