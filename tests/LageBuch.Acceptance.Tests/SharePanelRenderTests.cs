using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// Hosting no longer requires Tailscale (#59 follow-up): the share panel binds a status line that
// tells the user which address to dial (LAN + localhost). This pins that the toggle flips the
// button label and surfaces the hint in the view — and doubles as the PR screenshot capture.
public class SharePanelRenderTests
{
    private sealed class FakeHost : IIncidentHostController
    {
        public bool CanHost => true;

        public bool IsHosting { get; private set; }

        public string? ShareHint => "Erreichbar unter https://192.168.0.5:5859 · auf diesem Gerät: https://localhost:5859";

        public string? SharePin => IsHosting ? "1234" : null;

        public Task StartAsync(LocalIncidentSession session, MasterDataSet masterData)
        {
            IsHosting = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsHosting = false;
            return Task.CompletedTask;
        }
    }

    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace()
    {
        var vm = WorkspaceRenderHelper.BuildEditableWorkspaceWithAllBars(new FakeHost());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, name));
    }

    private static TextBlock ShareStatus(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text != null && t.Text.StartsWith("Erreichbar unter", StringComparison.Ordinal));

    [AvaloniaFact]
    public void Before_sharing_the_button_invites_sharing_and_no_status_is_shown()
    {
        var (window, vm) = ShowWorkspace();

        Assert.Equal("IM NETZWERK FREIGEBEN", vm.ShareButtonText);
        Assert.Null(vm.ShareStatus);
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text != null && t.Text.StartsWith("Erreichbar unter", StringComparison.Ordinal));
        Capture(window, "share-before.png");
    }

    [AvaloniaFact]
    public async Task Toggling_on_flips_the_label_and_shows_the_reachable_at_hint()
    {
        var (window, vm) = ShowWorkspace();

        await vm.ToggleSharingCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("FREIGABE BEENDEN", vm.ShareButtonText);
        Assert.Contains("localhost:5859", ShareStatus(window).Text!, StringComparison.Ordinal);
        Assert.Equal("1234", vm.SharePin);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text != null && t.Text.Contains("1234", StringComparison.Ordinal));
        Capture(window, "share-after.png");
    }

    // Regression (#66 follow-up): the first time an incident is shared the PIN pill appeared but its
    // number was blank. The pill's Border gates IsVisible on SharePin while the value lives in a
    // nested child TextBlock; when the Border un-collapses and the child's Text is set in the same
    // notification, the child is left with a stale zero-width measure and only recovers on a later
    // relayout (window resize, a timer tick) — so on a fresh share, nothing forced it and it stayed
    // blank. Existing-in-the-tree is not enough (the old assertion above passed while nothing showed):
    // the value must actually be laid out with a non-zero width right after the first share.
    [AvaloniaFact]
    public async Task First_share_lays_out_the_pin_value_with_a_visible_width()
    {
        var (window, vm) = ShowWorkspace();

        await vm.ToggleSharingCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var pinValue = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PinValue");
        Assert.Equal(vm.SharePin, pinValue.Text);   // the number itself, no static prefix to mask a 0-width bug
        Assert.True(
            pinValue.Bounds.Width > 0,
            $"PIN number '{pinValue.Text}' rendered at {pinValue.Bounds.Width:F0}px wide — it exists in the tree but is not laid out, so the number is invisible on first share.");
    }
}
