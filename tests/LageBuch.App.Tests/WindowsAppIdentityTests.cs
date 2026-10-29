using System.Xml.Linq;
using LageBuch.App.Services;

namespace LageBuch.App.Tests;

public class WindowsAppIdentityTests
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";

    [Fact]
    public void Msi_shortcuts_carry_the_app_user_model_id_the_app_sets()
    {
        var wxs = XDocument.Load(Path.Join(RepositoryRoot(), "packaging", "windows", "Lagebuch.wxs"));

        var shortcuts = wxs.Descendants(Wix + "Shortcut").ToList();

        Assert.NotEmpty(shortcuts);
        Assert.All(shortcuts, shortcut => Assert.Equal(WindowsAppIdentity.UserModelId, UserModelIdOf(shortcut)));
    }

    [Fact]
    public void Msi_puts_lagebuch_on_the_desktop_and_in_the_start_menu()
    {
        var wxs = XDocument.Load(Path.Join(RepositoryRoot(), "packaging", "windows", "Lagebuch.wxs"));

        var folders = wxs.Descendants(Wix + "Shortcut")
            .Select(s => (string?)s.Parent?.Attribute("Directory"))
            .ToList();

        Assert.Contains("ProgramMenuFolder", folders);
        Assert.Contains("DesktopFolder", folders);
    }

    private static string? UserModelIdOf(XElement shortcut) =>
        shortcut.Elements(Wix + "ShortcutProperty")
            .SingleOrDefault(p => string.Equals((string?)p.Attribute("Key"), "System.AppUserModel.ID", StringComparison.Ordinal))
            ?.Attribute("Value")?.Value;

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "LageBuch.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
