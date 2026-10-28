using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class AppVersionTextTests
{
    [Fact]
    public void Pr_build_keeps_its_pr_number_and_commit()
    {
        var text = AppVersionText.Display("0.9.1-pr804.abc1234+abc1234def5678abc1234def5678abc1234def56", new Version(0, 9, 1, 0));

        Assert.Equal("0.9.1-pr804.abc1234", text);
    }

    [Fact]
    public void Beta_keeps_its_prerelease_tag()
    {
        Assert.Equal("0.10.0-beta.1", AppVersionText.Display("0.10.0-beta.1+abc", new Version(0, 10, 0, 0)));
    }

    [Fact]
    public void Release_drops_the_source_link_commit()
    {
        Assert.Equal("0.9.1", AppVersionText.Display("0.9.1+abc1234", new Version(0, 9, 1, 0)));
    }

    [Fact]
    public void Missing_informational_version_falls_back_to_the_assembly_version()
    {
        Assert.Equal("0.9.1.0", AppVersionText.Display(null, new Version(0, 9, 1, 0)));
        Assert.Equal("0.9.1.0", AppVersionText.Display("+abc", new Version(0, 9, 1, 0)));
    }

    [Fact]
    public void Nothing_known_reads_0_0_0()
    {
        Assert.Equal("0.0.0", AppVersionText.Display(null, null));
    }

    [Fact]
    public void Of_reads_the_informational_version_of_an_assembly()
    {
        var text = AppVersionText.Of(typeof(AppVersionText).Assembly);

        Assert.DoesNotContain("+", text, StringComparison.Ordinal);
        Assert.NotEqual("0.0.0", text);
    }
}
