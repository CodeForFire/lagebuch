namespace LageBuch.Domain.Tests;

public class ForceUnitTests
{
    [Fact]
    public void Strength_format_is_GF_Mann_Gesamt()
    {
        var unit = ForceUnit.Create("FF Musterheim", personnelCount: 2, scbaCount: 0, officerCount: 1);
        Assert.Equal("1/1/2", unit.StrengthText);
    }

    [Fact]
    public void Without_officer_the_strength_reads_0_n_n()
    {
        var unit = ForceUnit.Create("Aich", personnelCount: 6);
        Assert.Equal("0/6/6", unit.StrengthText);
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
}
