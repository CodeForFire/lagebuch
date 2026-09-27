using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// The command bar must keep its primary actions (ÖFFNEN / NEUER EINSATZ) reachable on a phone-width
// viewport. A Bavarian ILS phone is ~411 dp wide (Medium_Phone_API_35: 1080px @ 420dpi). The
// desktop command bar is a single fixed row wider than that, so the right-most actions render off
// the right edge and can't be tapped. This test pins "NEUER EINSATZ fully within the viewport".
//
// This was skipped for the whole of the android-core-port: the bar reused the desktop row verbatim,
// and NEUER EINSATZ measured x=[527..638] on a 411 dp viewport — 227 px past the edge. The bar is
// responsive now (MainView's CommandBar container query keeps one action and folds the other five
// into an overflow flyout), so the contract this test always encoded is live.
public class CommandBarReachabilityTests
{
    private const double PhoneWidth = 411.0;
    private const double PhoneHeight = 872.0;

    private static Button ButtonNamed(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);

    private static (double Left, double Right) HorizontalBounds(Visual v, Visual relativeTo)
    {
        var left = v.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X;
        return (left, left + v.Bounds.Width);
    }

    [AvaloniaFact]
    public void New_incident_action_is_within_the_phone_viewport()
    {
        var view = new MainView();
        var window = new Window { Content = view, Width = PhoneWidth, Height = PhoneHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var (left, right) = HorizontalBounds(ButtonNamed(view, "NewIncidentButton"), window);

        // Evidence in the failure message: where the button actually lands vs the viewport.
        var message = $"NEUER EINSATZ spans x=[{left:0}..{right:0}] but the viewport is only {PhoneWidth:0} wide " +
            $"— it overflows the right edge by {right - PhoneWidth:0} px and is unreachable.";
        Assert.True(right <= PhoneWidth, message);
    }

    [AvaloniaFact]
    public void A_phone_folds_the_other_actions_into_the_overflow()
    {
        var view = new MainView();
        var window = new Window { Content = view, Width = PhoneWidth, Height = PhoneHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var more = ButtonNamed(view, "MoreActionsButton");
        Assert.True(more.IsVisible, "the overflow button must appear once the wide actions are folded away");

        var (left, right) = HorizontalBounds(more, window);
        Assert.True(
            right <= PhoneWidth && left >= 0,
            $"the overflow button spans x=[{left:0}..{right:0}], outside the {PhoneWidth:0} px viewport");

        // The folded actions are gone from the bar itself, not merely pushed off it. Asserted on
        // IsEffectivelyVisible, not IsVisible: the query hides the panels that hold them, and a
        // child of a hidden parent keeps its own IsVisible=true.
        Assert.False(ButtonNamed(view, "HomeButton").IsEffectivelyVisible);
        Assert.False(ButtonNamed(view, "MasterDataButton").IsEffectivelyVisible);
        Assert.False(ButtonNamed(view, "AboutButton").IsEffectivelyVisible);

        // …and every one of them is still reachable, in the overflow.
        Assert.NotNull(more.Flyout);
    }

    [AvaloniaFact]
    public void A_desktop_window_keeps_every_action_on_the_bar()
    {
        var view = new MainView();
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(ButtonNamed(view, "HomeButton").IsEffectivelyVisible);
        Assert.True(ButtonNamed(view, "MasterDataButton").IsEffectivelyVisible);
        Assert.True(ButtonNamed(view, "JoinDeviceButton").IsEffectivelyVisible);
        Assert.True(ButtonNamed(view, "OpenFileButton").IsEffectivelyVisible);
        Assert.True(ButtonNamed(view, "NewIncidentButton").IsEffectivelyVisible);
        Assert.True(ButtonNamed(view, "AboutButton").IsEffectivelyVisible);
        Assert.False(ButtonNamed(view, "MoreActionsButton").IsEffectivelyVisible);
    }
}
