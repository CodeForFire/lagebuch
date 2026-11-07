using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.App.Shared;

namespace LageBuch.Acceptance.Tests;

public class ViewLocatorTests
{
    [AvaloniaFact]
    public void Null_data_builds_the_placeholder()
    {
        var view = new ViewLocator().Build(null);

        Assert.Equal("—", Assert.IsType<TextBlock>(view).Text);
    }

    [AvaloniaFact]
    public void A_view_model_without_a_view_builds_the_missing_view_message()
    {
        var view = new ViewLocator().Build(new MissingViewModel());

        Assert.Equal("Nicht gefunden: MissingView", Assert.IsType<TextBlock>(view).Text);
    }

    [Fact]
    public void Match_accepts_observable_objects_but_not_plain_objects_or_null()
    {
        var locator = new ViewLocator();

        Assert.True(locator.Match(new MissingViewModel()));
        Assert.False(locator.Match(new object()));
        Assert.False(locator.Match(null));
    }

    private sealed class MissingViewModel : ObservableObject;
}
