using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Controls;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.Acceptance.Tests;

public sealed class MapPacksRenderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public void The_Karten_section_lists_the_installed_pack_and_shows_it_with_its_attribution()
    {
        var root = Path.Join(_dir, "regions");
        InstallFictionalPack(root);
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

    // A made-up pack around Fürstenfeldbruck: pale squares with a road through them, zoom 12 and 13.
    private static void InstallFictionalPack(string root)
    {
        var folder = Directory.CreateDirectory(Path.Join(root, "ffb")).FullName;
        var info = new RegionPackInfo("Landkreis Fürstenfeldbruck", "ffb", "https://example.org/ffb.zip", 15_782_458, 48.0877, 10.9930, 48.2967, 11.4129, "2026-08-31", "© OpenStreetMap contributors (ODbL).");
        File.WriteAllText(Path.Join(folder, "pack.json"), JsonSerializer.Serialize(info));

        using var cn = SqliteConnectionFactory.OpenReadWrite(Path.Join(folder, "region.mbtiles"));
        using (var create = cn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);";
            create.ExecuteNonQuery();
        }

        foreach (var zoom in new[] { 12, 13 })
        {
            var scale = 1 << (zoom - 12);
            for (var x = 2174 * scale; x <= 2178 * scale; x++)
            {
                for (var y = 1419 * scale; y <= 1423 * scale; y++)
                {
                    using var insert = cn.CreateCommand();
                    insert.CommandText = "INSERT INTO tiles VALUES ($z, $x, $y, $d);";
                    insert.Parameters.AddWithValue("$z", zoom);
                    insert.Parameters.AddWithValue("$x", x);
                    insert.Parameters.AddWithValue("$y", (1 << zoom) - 1 - y);
                    insert.Parameters.AddWithValue("$d", Tile((x + y) % 2 == 0));
                    insert.ExecuteNonQuery();
                }
            }
        }
    }

    private static byte[] Tile(bool even)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 256));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(new SolidColorBrush(even ? Color.FromRgb(0xF2, 0xEF, 0xE9) : Color.FromRgb(0xE8, 0xF0, 0xE0)), new Rect(0, 0, 256, 256));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0xF7, 0xC9, 0x6B)), 6), new Point(0, 40), new Point(256, 216));
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xCC, 0xC4)), 1), new Rect(0, 0, 256, 256));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
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
