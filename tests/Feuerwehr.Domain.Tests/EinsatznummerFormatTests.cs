using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Domain.Tests;

public class EinsatznummerFormatTests
{
    [Fact]
    public void Composes_all_parts_around_the_fixed_leitstelle_segment()
        => Assert.Equal("B 1.2 260715 1297", EinsatznummerFormat.Compose("B", "260715", "1297"));

    [Fact]
    public void Trims_each_part()
        => Assert.Equal("B 1.2 260715 1297", EinsatznummerFormat.Compose("  B ", " 260715", "1297 "));

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("   ", "", "  ")]
    public void All_blank_parts_yield_null(string? art, string? date, string? number)
        => Assert.Null(EinsatznummerFormat.Compose(art, date, number));

    [Fact]
    public void Blank_parts_are_dropped_but_the_constant_stays()
    {
        Assert.Equal("B 1.2 1297", EinsatznummerFormat.Compose("B", "", "1297"));
        Assert.Equal("1.2 260715", EinsatznummerFormat.Compose(null, "260715", null));
    }
}
