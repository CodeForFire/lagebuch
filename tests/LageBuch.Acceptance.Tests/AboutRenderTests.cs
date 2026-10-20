using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

// The About dialog (project badge, CodeForFire publisher mark with repository link, MIT license
// and copyright) and its ÜBER entry point in the command bar. Pins the rendered content and
// doubles as the PR screenshot capture (set RENDER_OUT to a directory to emit PNGs).
public class AboutRenderTests
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

    [AvaloniaFact]
    public void About_dialog_shows_brand_publisher_and_license()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");
        var window = new Window { Content = new AboutView { DataContext = vm }, Width = 640, Height = 640 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Both brand marks render as real bitmaps: the project badge and the CodeForFire avatar.
        var images = window.GetVisualDescendants().OfType<Image>().ToArray();
        Assert.Equal(2, images.Length);
        Assert.All(images, i => Assert.IsType<Bitmap>(i.Source));

        // The badge carries the wordmark and the slogan as artwork rather than as TextBlocks, so
        // assert it is actually laid out — a missing asset or a zero-width brand block would
        // otherwise leave the card looking empty with every text assertion below still passing.
        var badge = images.Single(i => i.Name == "BrandBadge");
        Assert.True(badge.Bounds.Width > 0, "the About badge has zero width -- nothing is drawn");

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(vm.RepositoryUrl, texts);
        Assert.Contains(texts, t => t!.Contains("MIT", StringComparison.Ordinal));
        Assert.Contains(texts, t => t!.Contains("Thomas Müller", StringComparison.Ordinal));

        Capture(window, "about-dialog.png");
    }

    // Issue #197: the ⚠ error banner glyph used to be Unicode text on a TextBlock, which defaults
    // to Barlow -- a font that doesn't carry it. Now a PathIcon like the ETB grid's row actions,
    // so the icon is drawn from bundled vector data.
    [AvaloniaFact]
    public void Error_banner_renders_a_laid_out_icon()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0") { ErrorMessage = "Fehler beim Öffnen." };
        var window = new Window { Content = new AboutView { DataContext = vm }, Width = 640, Height = 640 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // By name, not by Single(): the card scrolls when the window is short, and the scroll
        // bar's own template contributes PathIcons of its own.
        var icon = window.GetVisualDescendants().OfType<PathIcon>().Single(i => i.Name == "ErrorIcon");
        Assert.True(icon.Bounds.Width > 0, "the error banner icon has zero width -- nothing is drawn");
    }

    // #603: the sound check, through the real button. NoopAlarmService plays nothing, like an
    // Android tablet or a Linux install without aplay, so the result has to say so in amber.
    [AvaloniaFact]
    public void A_sound_test_that_plays_nothing_says_so_in_the_dialog()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", new NoopAlarmService());
        var window = new Window { Content = new AboutView { DataContext = vm }, Width = 640, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(window, "about-sound-before-test.png");

        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TestSoundButton");
        Assert.True(button.IsEffectivelyVisible);
        button.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var result = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SoundTestResult");
        Assert.True(result.IsEffectivelyVisible);
        Assert.Contains("nur auf dem Bildschirm", result.Text, StringComparison.Ordinal);
        Assert.Contains("failed", result.Classes);
        Capture(window, "about-sound-failed.png");
    }

    // #699: the mute switch, through the real checkbox. Its hint has to say plainly that the
    // Rückzugsalarm goes silent too.
    [AvaloniaFact]
    public void Ueber_mutes_the_alarms_and_says_the_Rueckzugsalarm_goes_silent_too()
    {
        var mute = new AlarmMute();
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0", new NoopAlarmService(), mute);
        var window = new Window { Content = new AboutView { DataContext = vm }, Width = 640, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(window, "about-mute-off.png");

        var checkBox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "AlarmMuteCheckBox");
        Assert.True(checkBox.IsEffectivelyVisible);
        Assert.Equal("Alarmtöne stumm schalten", checkBox.Content);
        var hint = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "AlarmMuteHint");
        Assert.Contains("Rückzugsalarm", hint.Text, StringComparison.Ordinal);

        checkBox.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(mute.IsMuted);
        Assert.Contains("muted", hint.Classes);
        Capture(window, "about-mute-on.png");
    }

    [AvaloniaFact]
    public void Without_an_alarm_service_the_sound_test_is_hidden()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");
        var window = new Window { Content = new AboutView { DataContext = vm }, Width = 640, Height = 1000 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "SoundTest").IsVisible);
    }

    [AvaloniaFact]
    public void Command_bar_has_an_about_entry_point()
    {
        var mainVm = BuildMainWindowViewModel();
        var window = new Window { Content = new MainView { DataContext = mainVm }, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AboutButton");
        Assert.True(button.IsVisible);

        Capture(window, "about-header.png");
    }

    [AvaloniaFact]
    public void Show_about_opens_the_overlay_and_escape_closes_it()
    {
        var mainVm = BuildMainWindowViewModel();
        var view = new MainView { DataContext = mainVm };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        mainVm.ShowAboutCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var about = Assert.IsType<AboutViewModel>(mainVm.PendingAbout);
        Capture(window, "about-open.png");

        window.Press(PhysicalKey.Escape);

        Assert.Null(mainVm.PendingAbout);

        GC.KeepAlive(about);
    }

    private static MainWindowViewModel BuildMainWindowViewModel()
    {
        var dialogs = new FakeDialogs();
        var masterData = new StaticMasterData(WorkspaceRenderHelper.MasterData());
        var home = new HomeViewModel(
            new FakeStore(),
            masterData,
            new EmptyRecent(),
            dialogs,
            new FixedClock(),
            new NoopTicker(),
            new NoopAlarmService(),
            new NoopIncidentHostController(),
            "0.1.0");
        var editor = new MasterDataEditorViewModel(masterData, dialogs, new NoFiles());
        return new MainWindowViewModel(home, editor, dialogs, "0.1.0");
    }

    private sealed class StaticMasterData(MasterDataSet set) : IMasterDataProvider
    {
        public MasterDataSet Get() => set;

        public void Save(MasterDataSet s)
        {
        }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

        public void Write(string path, MasterDataSet set)
        {
        }
    }

    private sealed class EmptyRecent : IRecentFilesStore
    {
        private readonly List<string> _list = new();

        public IReadOnlyList<string> GetRecent() => _list;

        public void Add(string path)
        {
        }

        public void Remove(string path)
        {
        }
    }
}
