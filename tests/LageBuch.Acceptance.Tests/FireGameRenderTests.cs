using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.Acceptance.Tests;

// The easter egg behind the Über card's logo. Pins the house front and the tap-to-extinguish
// path through the real buttons, and doubles as the PR screenshot capture (set RENDER_OUT).
public class FireGameRenderTests
{
    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, name));
    }

    private static Window Show(FireGameViewModel vm)
    {
        var window = new Window { Content = new FireGameView { DataContext = vm }, Width = 640, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button[] WindowButtons(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("firewindow")).ToArray();

    [AvaloniaFact]
    public void The_house_front_shows_twelve_named_windows_and_a_start_button()
    {
        var window = Show(new FireGameViewModel(new StepTimeProvider(), new FirstChoiceRandom()));

        var buttons = WindowButtons(window);
        Assert.Equal(12, buttons.Length);
        Assert.All(buttons, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
        Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StartButton").IsEffectivelyVisible);
        Capture(window, "fire-game-start.png");
    }

    [AvaloniaFact]
    public void Tapping_a_burning_window_puts_it_out_and_scores()
    {
        var time = new StepTimeProvider();
        var vm = new FireGameViewModel(time, new FirstChoiceRandom());
        var window = Show(vm);
        vm.StartCommand.Execute(null);

        // Window 1 at full size, window 2 at stage 2 and window 3 just lit.
        time.Tick(17);
        Dispatcher.UIThread.RunJobs();
        var buttons = WindowButtons(window);
        Assert.Contains("large", buttons[0].Classes);
        Assert.Contains("medium", buttons[1].Classes);
        Assert.Contains("small", buttons[2].Classes);
        Assert.Equal("Fenster 1, brennt", AutomationProperties.GetName(buttons[0]));
        Capture(window, "fire-game-running.png");

        buttons[2].Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, vm.Score);
        Assert.DoesNotContain("small", buttons[2].Classes);
        Assert.Equal("3", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ScoreText").Text);
    }

    [AvaloniaFact]
    public void A_finished_round_shows_the_result_and_offers_another()
    {
        var time = new StepTimeProvider();
        var vm = new FireGameViewModel(time, new FirstChoiceRandom());
        var window = Show(vm);
        vm.StartCommand.Execute(null);
        vm.Windows[0].ExtinguishCommand.Execute(null);
        time.Tick(240);
        Dispatcher.UIThread.RunJobs();

        var result = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ResultText");
        Assert.True(result.IsEffectivelyVisible);
        Assert.Contains("Vollbrand", result.Text, StringComparison.Ordinal);
        Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StartButton").IsEffectivelyVisible);
        Capture(window, "fire-game-result.png");
    }

    [AvaloniaFact]
    public void The_about_badge_is_a_named_button_outside_the_tab_order()
    {
        var window = new Window { Content = new AboutView { DataContext = new AboutViewModel(new FakeDialogs(), "0.1.0") }, Width = 640, Height = 640 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var badge = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BrandBadgeButton");
        Assert.Equal("Lagebuch-Logo", AutomationProperties.GetName(badge));
        Assert.False(badge.Focusable);
    }

    private sealed class FirstChoiceRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }

    // Timers fire only on Tick, inline: one Tick is one game step.
    private sealed class StepTimeProvider : TimeProvider
    {
        private readonly List<StepTimer> _timers = new();

        public void Tick(int times)
        {
            for (var i = 0; i < times; i++)
            {
                foreach (var timer in _timers.ToArray())
                {
                    timer.Fire();
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new StepTimer(this, callback, state);
            _timers.Add(timer);
            return timer;
        }

        private sealed class StepTimer(StepTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public void Fire()
            {
                if (owner._timers.Contains(this))
                {
                    callback(state);
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => owner._timers.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
