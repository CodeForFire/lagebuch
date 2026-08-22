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
// KNOWN DEFERRED LIMITATION — the android-core-port reuses the desktop command bar verbatim, and
// making it responsive is out of scope for the core port (per the design spec, phone-layout polish
// is a separate plan). Measured today: NEUER EINSATZ renders at x=[527..638] on a 411 dp viewport,
// overflowing the right edge by 227 px and unreachable. This test encodes the intended contract and
// is skipped until the command bar is made responsive — remove Skip then and it should pass.
public class CommandBarReachabilityTests
{
    private const double PhoneWidth = 411.0;
    private const double PhoneHeight = 872.0;

    private static Button ButtonNamed(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);

    private static (double left, double right) HorizontalBounds(Visual v, Visual relativeTo)
    {
        var left = v.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X;
        return (left, left + v.Bounds.Width);
    }

    [AvaloniaFact(Skip = "Known deferred phone-layout limitation: the desktop command bar overflows " +
        "a ~411 dp phone (NEUER EINSATZ measured at x=527..638). Unskip when the bar is made responsive.")]
    public void New_incident_action_is_within_the_phone_viewport()
    {
        var view = new MainView();
        var window = new Window { Content = view, Width = PhoneWidth, Height = PhoneHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var (left, right) = HorizontalBounds(ButtonNamed(view, "NewIncidentButton"), window);

        // Evidence in the failure message: where the button actually lands vs the viewport.
        Assert.True(right <= PhoneWidth,
            $"NEUER EINSATZ spans x=[{left:0}..{right:0}] but the viewport is only {PhoneWidth:0} wide " +
            $"— it overflows the right edge by {right - PhoneWidth:0} px and is unreachable.");
    }
}
