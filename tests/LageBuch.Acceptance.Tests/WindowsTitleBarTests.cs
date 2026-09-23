using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// Which DWM attribute darkens the caption on which Windows build. Windows 11 is left to Avalonia,
// which only handles it from build 22000 on — the gap below that is what this helper fills.
public class WindowsTitleBarTests
{
    [Theory]
    [InlineData(17134, null)] // 1803: no dark caption at all
    [InlineData(17763, 19)] // 1809: pre-20H1 attribute id
    [InlineData(18362, 19)] // 1903
    [InlineData(18985, 20)] // first build with the documented id
    [InlineData(19045, 20)] // 22H2, the last Windows 10
    [InlineData(22000, null)] // Windows 11: Avalonia's own path
    [InlineData(26100, null)]
    public void AttributeFor_PicksTheIdTheBuildUnderstands(int build, int? expected) =>
        Assert.Equal(expected, WindowsTitleBar.AttributeFor(build));
}
