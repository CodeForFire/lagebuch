using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The Home screen must report a failed open instead of taking the app down with it. These pin
// the banner's visibility and, just as importantly, that its message wraps inside the content
// column -- a horizontal StackPanel measures children at infinite width, which silently let the
// banner run off the right edge of the window.
public class HomeOpenErrorTests
{
    private sealed class Md : IMasterDataProvider
    {
        // `Empty with` rather than the positional constructor: master data gains fields often
        // enough that spelling every one out here just breaks the build on the next addition.
        public MasterDataSet Get() => MasterDataSet.Empty with { Roles = new[] { "EL" } };

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class SeededRecent : IRecentFilesStore
    {
        private readonly List<string> _list = new()
        {
            "/home/operator/Einsaetze/Einsatz-1234.fwincident",
            "/home/operator/Einsaetze/Einsatz-6666.fwincident",
            "/home/operator/Einsaetze/Einsatz5.fwincident",
        };

        public IReadOnlyList<string> GetRecent() => _list;

        public void Add(string path)
        {
        }
    }

    // Fails the way the real repository does for a file written by a newer build.
    private sealed class BrokenStore : IIncidentStore
    {
        public void Save(string path, Incident incident)
        {
        }

        public Task FlushAsync() => Task.CompletedTask;

        public Incident Load(string path) =>
            throw new LageBuch.Persistence.Sqlite.UnsupportedSchemaVersionException(6, 5);

        public IncidentState? TryReadState(string path) => null;

        public void SaveFileBytes(string path, string storageFileName, byte[] bytes)
        {
        }

        public byte[]? TryReadFileBytes(string path, string storageFileName) => null;

        public event Action<Exception>? SaveFailed
        {
            add { }
            remove { }
        }
    }

    private static (Window Window, HomeViewModel Vm) ShowHome(bool triggerError, string? renderTo = null)
    {
        var vm = new HomeViewModel(
            new BrokenStore(),
            new Md(),
            new SeededRecent(),
            new FakeDialogs(),
            new FixedClock(),
            new ManualTicker(),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            "1.0.0");
        if (triggerError)
        {
            vm.OpenRecentCommand.Execute("/home/operator/Einsaetze/Einsatz-1234.fwincident");
        }

        var window = new Window { Content = new HomeView { DataContext = vm }, Width = 1100, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Opt-in PNG capture for PR screenshots; skipped in a normal test run.
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (renderTo is not null && !string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Combine(dir, renderTo));
        }

        return (window, vm);
    }

    private static Border Banner(Window window) =>
        window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "OpenErrorBanner");

    [AvaloniaFact]
    public void No_banner_until_an_open_fails()
    {
        var (window, vm) = ShowHome(triggerError: false, renderTo: "home-before.png");

        Assert.Null(vm.OpenError);
        Assert.False(Banner(window).IsVisible);
    }

    [AvaloniaFact]
    public void A_failed_open_shows_the_banner_without_taking_the_app_down()
    {
        var (window, vm) = ShowHome(triggerError: true, renderTo: "home-after.png");

        Assert.NotNull(vm.OpenError);
        var banner = Banner(window);
        Assert.True(banner.IsVisible);

        // The window survived and still shows the recent list underneath.
        Assert.Equal(3, window.GetVisualDescendants().OfType<ListBox>().First().ItemCount);
    }

    [AvaloniaFact]
    public void The_message_wraps_inside_the_content_column()
    {
        var (window, _) = ShowHome(triggerError: true);
        var banner = Banner(window);
        var message = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Name == "OpenErrorMessage");

        // Measure the text, not the Border: the Border is capped by the column's MaxWidth and
        // stays 720px wide even when the text spills straight out of it, so asserting on the
        // Border passes against the very layout this guards.
        var messageRight = message.TranslatePoint(new Point(message.Bounds.Width, 0), window)!.Value.X;
        var bannerRight = banner.TranslatePoint(new Point(banner.Bounds.Width, 0), window)!.Value.X;

        Assert.True(
            messageRight <= bannerRight,
            $"message runs to x={messageRight} but the banner ends at x={bannerRight} — it is not wrapping");
        Assert.True(
            message.Bounds.Height > 30,
            $"message is {message.Bounds.Height}px tall — it did not wrap onto a second line");
    }
}
