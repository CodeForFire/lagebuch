using System.Xml.Linq;

namespace LageBuch.App.Tests;

// The Android head has no test project of its own (it needs the Android workload), so its
// launcher icon is checked here as resource files, the way the voice clips are.
public class AndroidLauncherIconTests
{
    private static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";

    // Android 13+ tints a launcher icon to the wallpaper theme only when it is an adaptive icon
    // with a <monochrome> layer; a plain PNG stays in its own colours next to every themed icon.
    // The manifest and MainActivity both name @drawable/icon, so API 26+ must resolve that name
    // to the adaptive icon, with icon.png left as the fallback below it.
    [Fact]
    public void Launcher_icon_is_an_adaptive_icon_with_a_monochrome_layer_for_themed_icons()
    {
        var resources = Path.Join(RepoRoot(), "src", "LageBuch.App.Android", "Resources");
        var adaptive = Path.Join(resources, "drawable-anydpi-v26", "icon.xml");
        Assert.True(File.Exists(adaptive), $"{adaptive} is missing");

        var root = XDocument.Load(adaptive).Root;
        Assert.NotNull(root);
        Assert.Equal("adaptive-icon", root.Name.LocalName);

        foreach (var layer in new[] { "background", "foreground", "monochrome" })
        {
            var element = root.Element(layer);
            Assert.True(element is not null, $"<{layer}> layer is missing");
            var drawable = (string?)element.Attribute(AndroidNs + "drawable");
            Assert.NotNull(drawable);
            Assert.StartsWith("@drawable/", drawable, StringComparison.Ordinal);
            var name = drawable["@drawable/".Length..];
            Assert.True(
                File.Exists(Path.Join(resources, "drawable", name + ".xml")),
                $"<{layer}> names {drawable}, which is not in Resources/drawable");
        }

        Assert.True(File.Exists(Path.Join(resources, "drawable", "icon.png")), "the pre-API-26 fallback icon.png is missing");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "LageBuch.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("LageBuch.sln not found above " + AppContext.BaseDirectory);
    }
}
