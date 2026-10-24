using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LageBuch.Acceptance.Tests;

// The module-tabs style in Theme/Styles.axaml: a module's own tabs inside one rail entry. They lie
// across the top, as wide as their label, and the open one is underlined in the signal colour.
public class ModuleTabsStyleTests
{
    [AvaloniaFact]
    public void Module_tabs_lie_across_the_top_and_underline_only_the_open_tab()
    {
        var strip = new TabControl
        {
            Classes = { "module-tabs" },
            Items =
            {
                new TabItem { Header = "EINS", Content = new TextBlock { Text = "1" } },
                new TabItem { Header = "ZWEI", Content = new TextBlock { Text = "2" } },
            },
            SelectedIndex = 1,
        };
        var window = new Window { Content = strip, Width = 800, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Dock.Top, strip.TabStripPlacement);

        var tabs = strip.Items.OfType<TabItem>().ToList();
        Assert.True(tabs[0].Bounds.Right <= tabs[1].Bounds.Left + 1.0, "the tabs sit side by side");
        Assert.True(tabs[1].Bounds.Width < 400, "a tab is as wide as its label, not the strip");

        Assert.Equal(Colors.Transparent, UnderlineOf(tabs[0]));
        Assert.NotEqual(Colors.Transparent, UnderlineOf(tabs[1]));
    }

    private static Color UnderlineOf(TabItem tab) =>
        tab.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Underline").Background is ISolidColorBrush brush
            ? brush.Color
            : Colors.Transparent;
}
