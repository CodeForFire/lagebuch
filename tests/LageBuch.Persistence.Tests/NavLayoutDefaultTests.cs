using LageBuch.Persistence.MasterData;

namespace LageBuch.Persistence.Tests;

public class NavLayoutDefaultTests
{
    // Ctrl+1…9 open the rail by position, so a module added later goes last: Ausgabe after
    // Kontakte keeps Ctrl+7 on Atemschutz.
    [Fact]
    public void The_default_layout_shows_every_module_once_with_ausgabe_after_kontakte()
    {
        var modules = NavLayout.Default.Where(e => !e.IsChecklist).ToList();

        Assert.Equal(NavModules.All, modules.Select(e => e.ModuleKey));
        Assert.All(NavLayout.Default, e => Assert.True(e.IsVisible));
        Assert.Equal(NavModules.IssuedItems, modules[^1].ModuleKey);
        Assert.Equal(NavModules.Contacts, modules[^2].ModuleKey);
    }
}
