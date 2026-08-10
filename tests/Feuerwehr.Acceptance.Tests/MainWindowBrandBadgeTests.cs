using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Feuerwehr.App.Shared.Views;

namespace Feuerwehr.Acceptance.Tests;

// The command bar's brand mark used to be a placeholder letter "L" in a colored tile.
// It now shows the app's own flame icon (the same artwork baked into every installer),
// so the in-app brand mark matches the icon users see in their taskbar/dock.
public class MainWindowBrandBadgeTests
{
    [AvaloniaFact]
    public void Brand_badge_shows_the_flame_icon_not_a_letter()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
        Assert.DoesNotContain("L", texts);

        var badgeImage = window.GetVisualDescendants().OfType<Image>().FirstOrDefault();
        Assert.NotNull(badgeImage);
        Assert.IsType<Bitmap>(badgeImage!.Source);
    }
}
