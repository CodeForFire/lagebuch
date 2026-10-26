using LageBuch.Persistence.MasterData;

namespace LageBuch.Persistence.Tests;

public class NavLayoutDefaultTests
{
    // Ctrl+1…9 open the rail by position, so a module added later goes last: Ausgabe after
    // Kontakte leaves every earlier entry on its Ctrl digit.
    [Fact]
    public void The_default_layout_shows_every_module_once_with_ausgabe_after_kontakte()
    {
        var modules = NavLayout.Default.Where(e => !e.IsChecklist).ToList();

        Assert.Equal(NavModules.All, modules.Select(e => e.ModuleKey));
        Assert.All(NavLayout.Default, e => Assert.True(e.IsVisible));
        Assert.Equal(NavModules.IssuedItems, modules[^1].ModuleKey);
        Assert.Equal(NavModules.Contacts, modules[^2].ModuleKey);
    }

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
