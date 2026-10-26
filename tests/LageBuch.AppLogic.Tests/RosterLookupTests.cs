using LageBuch.AppLogic.Services;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class RosterLookupTests
{
    private static readonly Person[] Personnel =
    [
        new Person("Mustermann", "Max", null, "Florian Emmering 11/1", "0151 1111111"),
        new Person("Musterfrau", "Erika", null, null, "0151 2222222"),
    ];

    [Fact]
    public void Find_matches_the_display_name_ignoring_case()
    {
        var name = Personnel[0].DisplayName;

        Assert.Same(Personnel[0], RosterLookup.Find(Personnel, name.ToUpperInvariant()));
        Assert.Null(RosterLookup.Find(Personnel, "Niemand"));
        Assert.Null(RosterLookup.Find(Personnel, null));
    }

    [Fact]
    public void Refill_writes_a_blank_field_and_owns_the_value()
    {
        string? written = "untouched";

        var owned = RosterLookup.Refill(null, null, "0151 1111111", v => written = v);

        Assert.Equal("0151 1111111", written);
        Assert.Equal("0151 1111111", owned);
    }

    [Fact]
    public void Refill_swaps_its_own_last_value_for_the_new_persons()
    {
        string? written = null;

        var owned = RosterLookup.Refill("0151 1111111", "0151 1111111", "0151 2222222", v => written = v);

        Assert.Equal("0151 2222222", written);
        Assert.Equal("0151 2222222", owned);
    }

    [Fact]
    public void Refill_leaves_a_hand_typed_value_alone()
    {
        var calls = 0;

        var owned = RosterLookup.Refill("0170 9999999", "0151 1111111", "0151 2222222", _ => calls++);

        Assert.Equal(0, calls);
        Assert.Null(owned);
    }
}
