using LageBuch.Persistence.MasterData;

namespace LageBuch.Persistence.Tests;

public class NavLayoutDefaultTests
{
    [Fact]
    public void Default_layout_shows_Fuehrung_between_Atemschutz_and_CO_Messung()
    {
        var keys = NavLayout.Default.Select(e => e.ModuleKey).ToList();
        var index = keys.IndexOf(NavModules.Leadership);

        Assert.True(index > 0);
        Assert.Equal(NavModules.Scba, keys[index - 1]);
        Assert.Equal(NavModules.Co, keys[index + 1]);
        Assert.True(NavLayout.Default[index].IsVisible);
        Assert.Null(NavLayout.Default[index].ChecklistId);
    }

    [Fact]
    public void Every_default_entry_is_visible_and_Fuehrung_is_a_built_in_module()
    {
        Assert.All(NavLayout.Default, e => Assert.True(e.IsVisible));
        Assert.Contains(NavModules.Leadership, NavModules.All);
    }
}
