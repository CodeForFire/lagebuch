namespace LageBuch.Osm.Tests;

public class OverpassQueryTests
{
    [Fact]
    public void The_query_selects_the_gemeinde_by_its_key()
    {
        var query = OverpassQuery.ForMunicipalityKey("09179121");

        Assert.Contains("""["de:amtlicher_gemeindeschluessel"="09179121"]""", query, StringComparison.Ordinal);
        Assert.Contains("out tags;", query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("09179121\"];way(1);out;//")] // the key is interpolated into the query text
    [InlineData("0917912")]
    [InlineData("091791210")]
    [InlineData("0917912a")]
    [InlineData("")]
    [InlineData("０９１７９１２１")] // full-width digits are digits to char.IsDigit
    public void A_key_that_is_not_eight_ascii_digits_never_reaches_the_query(string key)
    {
        var e = Assert.Throws<ArgumentException>(() => OverpassQuery.ForMunicipalityKey(key));
        Assert.Contains("acht Ziffern", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_query_selects_a_boundary_relation_by_id()
    {
        Assert.Contains("rel(62428)->.boundary;", OverpassQuery.ForRelation(62428), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_relation_id_must_be_positive(long id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OverpassQuery.ForRelation(id));
    }
}
