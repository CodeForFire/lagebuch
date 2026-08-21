using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Persistence.MasterData;
using Feuerwehr.App.Shared.Views;

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
            Einsatzarten = new[] { "B", "THL", "R" },
            ChecklistTemplateAufbau = new[]
            {
                new ChecklistTemplateItem("Aufstellort ELW frei?", true),
                new ChecklistTemplateItem("Kennleuchte ein, Blaulicht aus?", false),
            },
            ChecklistTemplateAbbau = new[] { new ChecklistTemplateItem("Fahrzeug abgerüstet?", true) },
            Links = new[]
            {
                new Link("Wetterdienst", "https://dwd.de"),
                new Link("Kartendienst", "https://example.org/karte"),
            },
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
        Assert.Equal(14, list.ItemCount);
        Assert.True(view.GetControl<Button>("SaveButton").IsVisible);

        // Capture the PR screenshot (real Skia backend rasterizes the embedded fonts).
        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "master-data-editor.png");
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(path);
        Assert.True(new FileInfo(path).Length > 0);
    }

    // The Checkliste template editor split into Aufbau/Abbau sections, each row gaining a
    // mandatory checkbox alongside the text (#72) -- distinct from the single-string-per-row
    // EditableListSection the other categories still use.
    [AvaloniaFact]
    public void Checkliste_aufbau_section_renders_text_and_mandatory_checkbox_per_row()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Checkliste Aufbau");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var textBoxes = view.GetVisualDescendants().OfType<TextBox>()
            .Where(t => t.Text is "Aufstellort ELW frei?" or "Kennleuchte ein, Blaulicht aus?").ToList();
        Assert.Equal(2, textBoxes.Count);
        var checkBoxes = view.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Content as string == "Pflicht").ToList();
        Assert.Equal(2, checkBoxes.Count);
        Assert.Contains(checkBoxes, c => c.IsChecked == true);
        Assert.Contains(checkBoxes, c => c.IsChecked == false);

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, "master-data-editor-checkliste-aufbau.png"));
    }

    [AvaloniaFact]
    public void Links_section_renders_name_and_url_per_row()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Links");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var textBoxes = view.GetVisualDescendants().OfType<TextBox>()
            .Where(t => t.Text is "Wetterdienst" or "https://dwd.de" or "Kartendienst" or "https://example.org/karte")
            .ToList();
        Assert.Equal(4, textBoxes.Count);

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, "master-data-editor-links.png"));
    }
}
