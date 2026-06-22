using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Documents.Tests;

public class FormattingTests
{
    [Fact]
    public void Timestamp_uses_german_day_first_format()
    {
        var t = new DateTimeOffset(2026, 6, 22, 9, 5, 0, TimeSpan.FromHours(2));
        Assert.Equal("22.06.2026 09:05", Formatting.Timestamp(t));
    }

    [Theory]
    [InlineData(EtbDirection.Incoming, "Eingang")]
    [InlineData(EtbDirection.Outgoing, "Ausgang")]
    [InlineData(EtbDirection.Internal, "Intern")]
    public void Direction_is_german(EtbDirection direction, string expected)
    {
        Assert.Equal(expected, Formatting.Direction(direction));
    }

    [Theory]
    [InlineData(IncidentState.Open, "Offen")]
    [InlineData(IncidentState.Closed, "Abgeschlossen")]
    public void State_is_german(IncidentState state, string expected)
    {
        Assert.Equal(expected, Formatting.State(state));
    }

    [Fact]
    public void OrDash_returns_dash_for_blank()
    {
        Assert.Equal("—", Formatting.OrDash(null));
        Assert.Equal("—", Formatting.OrDash("  "));
        Assert.Equal("EL", Formatting.OrDash("EL"));
    }
}
