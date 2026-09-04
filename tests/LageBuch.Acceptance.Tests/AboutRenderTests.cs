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

// The About dialog (flame logo, CodeForFire publisher mark with repository link, MIT license and
// copyright) and its ÜBER entry point in the command bar. Pins the rendered content and doubles
// as the PR screenshot capture (set RENDER_OUT to a directory to emit PNGs).
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
        frame.SavePng(Path.Combine(dir, name));
    }

    [AvaloniaFact]
    public void About_dialog_shows_brand_publisher_and_license()
    {
        var vm = new AboutViewModel(new FakeDialogs(), "0.1.0");
        var window = new Window { Content = new AboutView { DataContext = vm }, Width = 640, Height = 640 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Both brand marks render as real bitmaps: the flame and the CodeForFire org avatar.
        var images = window.GetVisualDescendants().OfType<Image>().Select(i => i.Source).ToArray();
        Assert.Equal(2, images.Length);
        Assert.All(images, s => Assert.IsType<Bitmap>(s));

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Lagebuch", texts);
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

        var icon = Assert.Single(window.GetVisualDescendants().OfType<PathIcon>());
        Assert.True(icon.Bounds.Width > 0, "the error banner icon has zero width -- nothing is drawn");
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

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

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
        public MasterDataSet Read(string path) => MasterDataSet.Empty;

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
    }
}
