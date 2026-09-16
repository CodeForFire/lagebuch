using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

public class IncidentNumberTests
{
    [Fact]
    public void Trims_and_stores_value()
    {
        Assert.Equal("B 1234", new IncidentNumber("  B 1234  ").Value);
    }

    [Fact]
    public void Rejects_blank()
    {
        Assert.Throws<ArgumentException>(() => new IncidentNumber("   "));
    }

    [Fact]
    public void ToString_returns_the_value()
    {
        Assert.Equal("B 1234", new IncidentNumber("B 1234").ToString());
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.Throws<ArgumentException>(() => new IncidentNumber(null!));
    }

    [Fact]
    public void Equal_after_trimming()
    {
        Assert.Equal(new IncidentNumber(" B 1 "), new IncidentNumber("B 1"));
    }
}