namespace LageBuch.Domain.Tests;

public class SessionOperatorTests
{
    [Fact]
    public void Requires_a_non_blank_name()
    {
        Assert.Throws<ArgumentException>(() => new SessionOperator("  "));
    }

    [Fact]
    public void Display_includes_callsign_when_present()
    {
        Assert.Equal("Müller (FFB 12/1)", new SessionOperator("Müller", "FFB 12/1").Display);
    }

    [Fact]
    public void Display_is_just_the_name_without_callsign()
    {
        Assert.Equal("Müller", new SessionOperator("Müller").Display);
    }
}
