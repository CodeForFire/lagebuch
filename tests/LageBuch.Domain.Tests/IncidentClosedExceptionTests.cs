namespace LageBuch.Domain.Tests;

public class IncidentClosedExceptionTests
{
    [Fact]
    public void Is_an_invalid_operation_exception()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(new IncidentClosedException());
    }

    [Fact]
    public void Has_a_default_message()
    {
        Assert.False(string.IsNullOrWhiteSpace(new IncidentClosedException().Message));
    }
}
