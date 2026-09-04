using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// The join prompt ("Mit Gerät verbinden") gained a PIN field (#64): a joining device must enter the
// host's 4-digit share PIN alongside the address. This pins that the field is present in join mode
// and doubles as the PR screenshot capture.
public class JoinPromptRenderTests
{
    private static (Window Window, OperatorPromptViewModel Vm) ShowJoinPrompt()
    {
        var vm = new OperatorPromptViewModel(collectHost: true, callSignOptions: new[] { "FFB 1/40/1" })
        {
            Host = "elw-1",
            Pin = "1234",
            OperatorName = "Müller",
        };
        var window = new Window { Content = new OperatorPromptView { DataContext = vm }, Width = 640, Height = 560 };
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

    [AvaloniaFact]
    public void Join_prompt_shows_a_pin_field()
    {
        var (window, vm) = ShowJoinPrompt();

        var pinBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PinBox");
        Assert.True(pinBox.IsVisible);
        Assert.Equal(4, pinBox.MaxLength);
        Assert.True(vm.ConfirmCommand.CanExecute(null)); // host + PIN + name all present
        Capture(window, "join-prompt.png");
    }

    // #182: a failed join now reports its error inline instead of closing the dialog.
    [AvaloniaFact]
    public void Join_prompt_shows_the_error_inline_after_a_failed_attempt()
    {
        var (window, vm) = ShowJoinPrompt();

        vm.ReportJoinFailure("Falsche PIN.", certificateChanged: false);
        Dispatcher.UIThread.RunJobs();

        var errorText = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Name == "PromptErrorMessage");
        Assert.True(errorText.IsVisible);
        Assert.Equal("Falsche PIN.", errorText.Text);

        var resetTrustButton = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PromptResetTrustButton");
        Assert.False(resetTrustButton.IsVisible); // not a certificate-changed failure
        Capture(window, "join-prompt-wrong-pin.png");
    }

    // #182: a TOFU certificate-changed failure additionally offers a reset-trust action in-dialog.
    [AvaloniaFact]
    public void Join_prompt_offers_reset_trust_after_a_certificate_changed_failure()
    {
        var (window, vm) = ShowJoinPrompt();

        vm.ReportJoinFailure("Zertifikat für elw-1 hat sich geändert.", certificateChanged: true);
        Dispatcher.UIThread.RunJobs();

        var resetTrustButton = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PromptResetTrustButton");
        Assert.True(resetTrustButton.IsVisible);
        Capture(window, "join-prompt-cert-changed.png");
    }

    // #196: the capability to cancel a connection attempt lives in this dialog now, not in a
    // Home-page banner behind it -- a "Verbindung wird hergestellt…" status plus an always-abortable
    // Cancel button, both visible without leaving the prompt.
    [AvaloniaFact]
    public void Join_prompt_shows_a_connecting_status_and_stays_cancellable_while_busy()
    {
        var (window, vm) = ShowJoinPrompt();

        var cancelButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton");
        Assert.True(cancelButton.IsVisible);
        Assert.True(cancelButton.IsEnabled);

        var banner = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ConnectingBanner");
        Assert.False(banner.IsVisible); // idle: no connection attempt in flight yet

        vm.IsBusy = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(banner.IsVisible);
        Assert.True(cancelButton.IsEnabled); // still abortable while connecting
        Capture(window, "join-prompt-connecting.png");
    }
}
