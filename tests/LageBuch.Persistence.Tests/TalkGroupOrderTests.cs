using LageBuch.Persistence.MasterData;

namespace LageBuch.Persistence.Tests;

public class TalkGroupOrderTests
{
    private static string[] Ordered(params TalkGroup[] groups) =>
        [.. TalkGroup.FavouritesFirst(groups, g => g.Display).Select(g => g.Display)];

    [Fact]
    public void Favourites_come_first_and_each_part_is_alphabetical()
    {
        var order = Ordered(
            new TalkGroup("Zulu", null, TalkGroupKind.Tmo),
            new TalkGroup("Yankee", null, TalkGroupKind.Tmo, IsFavorite: true),
            new TalkGroup("Alpha", null, TalkGroupKind.Tmo),
            new TalkGroup("Bravo", null, TalkGroupKind.Tmo, IsFavorite: true));

        Assert.Equal(["TMO Bravo", "TMO Yankee", "TMO Alpha", "TMO Zulu"], order);
    }

    [Fact]
    public void Order_ignores_case_and_reads_numbers_by_value_and_umlauts_in_german()
    {
        var order = Ordered(
            new TalkGroup("fw", "10", TalkGroupKind.Tmo),
            new TalkGroup("FW", "2", TalkGroupKind.Tmo),
            new TalkGroup("Zentrale", null, TalkGroupKind.Tmo),
            new TalkGroup("Übung", null, TalkGroupKind.Tmo));

        Assert.Equal(["TMO FW 2", "TMO fw 10", "TMO Übung", "TMO Zentrale"], order);
    }

    [Fact]
    public void Groups_of_the_same_label_keep_the_order_they_were_written_in()
    {
        var first = new TalkGroup("A", null, TalkGroupKind.Tmo, "erste");
        var second = new TalkGroup("a", null, TalkGroupKind.Tmo, "zweite");

        Assert.Equal([first, second], TalkGroup.FavouritesFirst([first, second], g => g.Name));
    }
}
