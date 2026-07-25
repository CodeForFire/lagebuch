using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Persistence.MasterData;
using Feuerwehr.App.Views;

namespace Feuerwehr.Acceptance.Tests;

public class MasterDataEditorRenderTests
{
    private sealed class SampleProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Roles = new[] { "EL", "EAL", "ZF", "GF" },
            Status = new[] { "aufgenommen", "übermittelt", "abgearbeitet" },
            UnitStatus = new[] { "Alarmiert", "Auf Anfahrt", "Im Einsatz" },
            Equipment = new[] { "Mobilteil 1", "Digitalkamera" },
            Districts = new[] { "Aich", "FFB", "Puch" },
            Brigades = new[] { "FFB Wache 1", "Aich", "Puch" },
            RadioCallSigns = new[] { "FFB 1/10/1", "Aich 42/1", "Land 1" },
            TruppTypes = new[] { "Angriffstrupp", "Wassertrupp", "CSA-Trupp" },
            ChecklistTemplate = new[] { "Aufstellort ELW frei?", "Kennleuchte ein, Blaulicht aus?" },
            Personnel = new[]
            {
                new Person("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67"),
                new Person("Musterfrau", "Erika", "GF", null, "01 71 / 7 65 43 21"),
            },
        };
        public void Save(MasterDataSet set) { }
    }

    private sealed class NoFiles : IMasterDataFileService
    {
        public MasterDataSet Read(string path) => MasterDataSet.Empty;
        public void Write(string path, MasterDataSet set) { }
    }

    [AvaloniaFact]
    public void The_editor_renders_with_every_category()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var list = view.GetControl<ListBox>("CategoryList");
        Assert.Equal(10, list.ItemCount);
        Assert.True(view.GetControl<Button>("SaveButton").IsVisible);

        // Capture the PR screenshot (real Skia backend rasterizes the embedded fonts).
        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "master-data-editor.png");
        using var frame = window.CaptureRenderedFrame()!;
        frame.Save(path);
        Assert.True(new FileInfo(path).Length > 0);
    }
}
