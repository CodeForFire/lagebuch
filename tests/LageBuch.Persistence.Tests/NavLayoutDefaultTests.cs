using LageBuch.Persistence.MasterData;

namespace LageBuch.Persistence.Tests;

public class NavLayoutDefaultTests
{
    [Fact]
    public void Default_layout_shows_Fuehrung_between_Aufgaben_and_Beteiligte()
    {
        var keys = NavLayout.Default.Select(e => e.ModuleKey).ToList();
        var index = keys.IndexOf(NavModules.Leadership);

        Assert.True(index > 0);
        Assert.Equal(NavModules.Tasks, keys[index - 1]);
        Assert.Equal(NavModules.InvolvedParties, keys[index + 1]);
        Assert.True(NavLayout.Default[index].IsVisible);
        Assert.Null(NavLayout.Default[index].ChecklistId);
    }

    [Fact]
    public void Every_default_entry_is_visible_and_Fuehrung_is_a_built_in_module()
    {
        Assert.All(NavLayout.Default, e => Assert.True(e.IsVisible));
        Assert.Contains(NavModules.Leadership, NavModules.All);
    }

    // Funktionen and Kräfte are tabs of Führung now. Their old keys must read as unknown, so only
    // the fold in NavigationLayout ever gives them a meaning.
    [Theory]
    [InlineData("roles")]
    [InlineData("forces")]
    public void Funktionen_and_Kraefte_are_no_longer_modules_of_their_own(string key)
    {
        Assert.False(NavModules.IsKnown(key));
        Assert.DoesNotContain(NavLayout.Default, e => e.ModuleKey == key);
    }
}
