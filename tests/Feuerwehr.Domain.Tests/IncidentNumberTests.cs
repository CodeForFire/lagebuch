using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Domain.Tests;

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
}
