using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Feuerwehr.App.Shared.Views;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.Acceptance.Tests;

// Hosting no longer requires Tailscale (#59 follow-up): the share panel binds a status line that
// tells the user which address to dial (LAN + localhost). This pins that the toggle flips the
// button label and surfaces the hint in the view — and doubles as the PR screenshot capture.
public class SharePanelRenderTests
{
    private sealed class FakeHost : IIncidentHostController
    {
        public bool CanHost => true;
        public bool IsHosting { get; private set; }
        public string? ShareHint => "Erreichbar unter 192.168.0.5:5859 · auf diesem Gerät: localhost:5859";
        public Task StartAsync(LocalIncidentSession session) { IsHosting = true; return Task.CompletedTask; }
        public Task StopAsync() { IsHosting = false; return Task.CompletedTask; }
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
            return;
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(dir, name));
    }

    private static TextBlock ShareStatus(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text != null && t.Text.StartsWith("Erreichbar unter"));

    [AvaloniaFact]
    public void Before_sharing_the_button_invites_sharing_and_no_status_is_shown()
    {
        var (window, vm) = ShowWorkspace();

        Assert.Equal("IM NETZWERK FREIGEBEN", vm.ShareButtonText);
        Assert.Null(vm.ShareStatus);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text != null && t.Text.StartsWith("Erreichbar unter"));
        Capture(window, "share-before.png");
    }

    [AvaloniaFact]
    public async Task Toggling_on_flips_the_label_and_shows_the_reachable_at_hint()
    {
        var (window, vm) = ShowWorkspace();

        await vm.ToggleSharingCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("FREIGABE BEENDEN", vm.ShareButtonText);
        Assert.Contains("localhost:5859", ShareStatus(window).Text!);
        Capture(window, "share-after.png");
    }
}
