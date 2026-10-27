using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

public sealed class MapPacksRenderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public void The_Karten_section_lists_the_installed_pack_and_shows_it_with_its_attribution()
    {
        var root = Path.Join(_dir, "regions");
        FictionalRegionPack.Install(root);
        using var http = new HttpClient();
        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles(), new RegionPackLibrary(http, root));
        vm.SelectedSection = vm.Sections.OfType<MapPacksSection>().Single();
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(vm.Sections, s => s.Title == "Karten");
        Assert.Equal(1, view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "InstalledMapPacks").ItemCount);
        Assert.False(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NoMapPacksHint").IsVisible);
        var map = view.GetVisualDescendants().OfType<MapCanvasControl>().Single();
        Assert.NotNull(map.TileSource);
        Assert.True(map.WheelZoomNeedsControl);
        Assert.Equal("© OpenStreetMap contributors (ODbL).", view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MapAttribution").Text);

        // The PR screenshot.
        var shots = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(shots);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(shots, "stammdaten-karten.png"));
    }

    [AvaloniaFact]
    public void Without_a_pack_the_Karten_section_says_so_and_shows_no_map()
    {
        using var http = new HttpClient();
        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles(), new RegionPackLibrary(http, Path.Join(_dir, "none")));
        vm.SelectedSection = vm.Sections.OfType<MapPacksSection>().Single();
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NoMapPacksHint").IsEffectivelyVisible);
        Assert.False(view.GetVisualDescendants().OfType<MapCanvasControl>().Single().IsEffectivelyVisible);
    }

    private sealed class EmptyProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty;

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, []);

        public void Write(string path, MasterDataSet set)
        {
        }
    }
}
