using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class CommandNodeKindLabelsTests
{
    [Theory]
    [InlineData(CommandNodeKind.Abschnitt, "Abschnitt")]
    [InlineData(CommandNodeKind.Unterabschnitt, "Unterabschnitt")]
    [InlineData(CommandNodeKind.Bereitstellung, "Bereitstellung")]
    [InlineData(CommandNodeKind.Partner, "Einsatzleitung")]
    public void Each_kind_has_the_label_the_board_and_the_Bericht_share(CommandNodeKind kind, string label)
    {
        Assert.Equal(label, CommandNodeKindLabels.Of(kind));
    }
}
