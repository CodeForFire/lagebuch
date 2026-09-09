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
        Assert.True(LageBuch.App.Shared.Behaviors.IntegerOnly.GetIsEnabled(pinBox));
        Assert.True(vm.ConfirmCommand.CanExecute(null)); // host + PIN + name all present
        Capture(window, "join-prompt.png");
    }

    // #262 UX: the PIN field was a plain unmasked TextBox with no digit-only filtering, easy to
    // mistype under time pressure. IntegerOnly (already used for Forces' count fields, #76) refuses
    // any input containing a non-digit character wholesale rather than mutilating it.
    [AvaloniaTheory]
    [InlineData("12a4")]
    [InlineData("12.4")]
    [InlineData("abcd")]
    public void Non_digit_characters_are_refused_in_the_pin_field(string typed)
    {
        var (window, vm) = ShowJoinPrompt();
        var pinBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PinBox");

        pinBox.Focus();
        pinBox.SelectAll();
        window.KeyTextInput(typed);
        Dispatcher.UIThread.RunJobs();

        // Refused wholesale -- the original PIN stands rather than a mutilated mix.
        Assert.Equal("1234", pinBox.Text);
        Assert.Equal("1234", vm.Pin);
    }

    [AvaloniaFact]
    public void Digit_entry_still_goes_through_the_pin_field()
    {
        var (window, vm) = ShowJoinPrompt();
        var pinBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PinBox");

        pinBox.Focus();
        pinBox.SelectAll();
        window.KeyTextInput("5678");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("5678", pinBox.Text);
        Assert.Equal("5678", vm.Pin);
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
        Assert.Equal("ABBRECHEN", cancelButton.Content); // idle: this closes the dialog

        var banner = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ConnectingBanner");
        Assert.False(banner.IsVisible); // idle: no connection attempt in flight yet

        vm.IsBusy = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(banner.IsVisible);

        // #196: while busy, the same button only aborts the attempt (dialog stays up) -- its label
        // must say so, since a bare "ABBRECHEN" would misleadingly read as "close this dialog".
        Assert.Equal("VERBINDUNG ABBRECHEN", cancelButton.Content);
        Assert.True(cancelButton.IsEnabled); // still abortable while connecting
        Capture(window, "join-prompt-connecting.png");
    }
}
