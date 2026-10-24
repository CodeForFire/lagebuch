using LageBuch.Persistence.MasterData;

namespace LageBuch.Persistence.Tests;

// #420: the Objekt record itself -- its address line, its value equality and Normalize.
public class PremisesTests
{
    private static readonly Premises Full = new("Haus A", "Lindenallee", "12", "12345", "Musterstadt", 4, 3, 1)
    {
        ApartmentLabels = new Dictionary<string, string?> { ["0:1"] = "Hausmeister" },
    };

    [Theory]
    [InlineData("Lindenallee", "12", "12345", "Musterstadt", "Lindenallee 12, 12345 Musterstadt")]
    [InlineData("Lindenallee", "", "", "Musterstadt", "Lindenallee, Musterstadt")]
    [InlineData("", "", "12345", "", "12345")]
    [InlineData("", "", "", "", "")]
    public void The_address_line_leaves_out_blank_parts(string street, string houseNumber, string postalCode, string place, string expected) =>
        Assert.Equal(expected, new Premises("Haus A", street, houseNumber, postalCode, place).AddressLine);

    [Fact]
    public void Two_objekte_with_the_same_values_and_labels_are_equal()
    {
        var copy = Full with { ApartmentLabels = new Dictionary<string, string?> { ["0:1"] = "Hausmeister" } };

        Assert.Equal(Full, copy);
        Assert.Equal(Full.GetHashCode(), copy.GetHashCode());
    }

    public static TheoryData<Premises> OneFieldDifferent() => new()
    {
        Full with { Name = "Haus B" },
        Full with { Street = "Mühlweg" },
        Full with { HouseNumber = "13" },
        Full with { PostalCode = "54321" },
        Full with { Place = "Musterdorf" },
        Full with { FloorCount = 5 },
        Full with { ApartmentsPerFloor = 4 },
        Full with { UndergroundFloorCount = 0 },
        Full with { ApartmentLabels = new Dictionary<string, string?> { ["0:1"] = "Kiosk" } },
        Full with { ApartmentLabels = new Dictionary<string, string?> { ["0:2"] = "Hausmeister" } },
        Full with { ApartmentLabels = new Dictionary<string, string?>() },
        Full with { ApartmentLabels = new Dictionary<string, string?> { ["0:1"] = "Hausmeister", ["0:2"] = "Kiosk" } },
    };

    [Theory]
    [MemberData(nameof(OneFieldDifferent))]
    public void Objekte_differing_in_one_field_are_not_equal(Premises other) =>
        Assert.NotEqual(Full, other);

    [Fact]
    public void An_objekt_is_not_equal_to_null() =>
        Assert.False(Full.Equals(null));

    [Fact]
    public void Normalize_trims_every_text_field()
    {
        var premises = Assert.Single(Premises.Normalize(new[] { new Premises(" Haus A ", " Lindenallee ", " 12 ", " 12345 ", " Musterstadt ") }));

        Assert.Equal(new Premises("Haus A", "Lindenallee", "12", "12345", "Musterstadt"), premises);
    }
}
