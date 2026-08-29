using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;

namespace LageBuch.Acceptance.Tests;

// The Atemschutz pressure-control reminder banner ("Nächste Druckabfrage: ...") uses an
// unconstrained horizontal StackPanel with no wrapping/trimming, unlike DisconnectedBanner and
// ScbaAlarmBar which put their (also dynamic-length) text in a Grid star column. #78 lengthened
// the banner text from bare "{Designation}" to "Trupp {N} ({Designation})", and on a phone-width
// viewport the banner now runs off the right edge instead of fitting or wrapping.
public class ScbaControlBarReachabilityTests
{
    private const double PhoneWidth = 411.0;
    private const double PhoneHeight = 872.0;

    private static (double left, double right) HorizontalBounds(Visual v, Visual relativeTo)
    {
        var left = v.TranslatePoint(new Point(0, 0), relativeTo)!.Value.X;
        return (left, left + v.Bounds.Width);
    }

    [AvaloniaFact]
    public void Pressure_control_banner_text_stays_within_a_phone_viewport()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = PhoneWidth,
            Height = PhoneHeight,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var text = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ScbaControlText");
        var (left, right) = HorizontalBounds(text, window);

        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Combine(dir, "scba-control-bar-phone.png"));
        }

        Assert.True(right <= PhoneWidth,
            $"ScbaControlText '{text.Text}' spans x=[{left:0}..{right:0}] but the viewport is only " +
            $"{PhoneWidth:0} wide — it overflows the right edge by {right - PhoneWidth:0} px.");
    }

    // Same root cause, same fix needed: AlarmDisplay went through the same DisplayName
    // lengthening (#78), and ScbaAlarmBar's Grid star column alone doesn't stop overflow without
    // TextWrapping on the text itself.
    [AvaloniaFact]
    public void Rueckzugsalarm_banner_text_stays_within_a_phone_viewport()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars();
        var window = new Window
        {
            Content = new IncidentWorkspaceView { DataContext = vm },
            Width = PhoneWidth,
            Height = PhoneHeight,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ScbaAlarmBar");
        var text = bar.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text != null && t.Text.StartsWith("RÜCKZUGSALARM"));
        var (left, right) = HorizontalBounds(text, window);

        // The Grid star column already keeps the text's arranged bounds within the viewport (an
        // overflow-based check alone would pass even when broken) — Avalonia clips a NoWrap
        // TextBlock to its column width by default, so with no TextWrapping the tail of the
        // message is silently cut off with no ellipsis, no visual cue. Multi-line height is the
        // signal that the full message actually rendered instead of being clipped.
        Assert.True(right <= PhoneWidth,
            $"AlarmDisplay text '{text.Text}' spans x=[{left:0}..{right:0}] but the viewport is only " +
            $"{PhoneWidth:0} wide — it overflows the right edge by {right - PhoneWidth:0} px.");
        Assert.True(text.Bounds.Height > text.FontSize * 1.5,
            $"AlarmDisplay rendered only {text.Bounds.Height:0}px tall (~one line) for '{text.Text}' " +
            "at a phone width — the message does not fit on one line here and is being silently " +
            "clipped instead of wrapping onto a second line.");
    }
}
