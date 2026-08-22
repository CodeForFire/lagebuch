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
            return;
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
}
