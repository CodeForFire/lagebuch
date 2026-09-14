using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LageBuch.App.Shared.Views;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Acceptance.Tests;

public class MasterDataEditorRenderTests
{
    private sealed class SampleProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty with
        {
            Roles = new[] { "EL", "EAL", "ZF", "GF" },
            UnitStatus = new[] { "Alarmiert", "Auf Anfahrt", "Im Einsatz" },
            TruppTypes = new[] { "Angriffstrupp", "Wassertrupp", "CSA-Trupp" },

            // Wachen and Funkrufnamen derive from these rows (plus the roster's "Land 1").
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB 1/10/1", 9),
                new Vehicle("Aich", "Aich 42/1", 6),
                new Vehicle("Puch", "Puch 40/1", 9),
            },
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

        public void Save(MasterDataSet set)
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

    [AvaloniaFact]
    public void The_editor_renders_with_every_category()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var list = view.GetControl<ListBox>("CategoryList");

        // 8 categories plus #76's Fahrzeuge, which also supply the Wachen and Funkrufnamen.
        Assert.Equal(9, list.ItemCount);
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

    // #76: the Fahrzeuge section — Wache + Funkrufname + Sitzplätze per row, Wache and
    // Funkrufname as AutoCompleteBoxes fed from the master data (free text still allowed).
    // Issue #197: the ▲/▼/✕ row-action buttons used to be Unicode text on a Button, which
    // defaults to Oswald -- a font that carries none of the three codepoints, so the affordance
    // only ever appeared where the OS happened to supply a fallback face. Now PathIcons like the
    // ETB grid's row actions, so the icons come from bundled vector data.
    [AvaloniaFact]
    public void Links_section_row_actions_render_laid_out_icons()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Links");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rowButtons = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("icon-btn"))
            .ToList();

        // Two Links rows, each with up/down/remove.
        Assert.Equal(6, rowButtons.Count);
        foreach (var button in rowButtons)
        {
            var icon = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
            Assert.True(
                icon.Bounds.Width > 0,
                $"the icon in the {button.GetValue(ToolTip.TipProperty)} button has zero width -- nothing is drawn");
        }
    }

    [AvaloniaFact]
    public void Fahrzeuge_section_renders_wache_callsign_and_seats_per_row()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        var section = (VehiclesSection)vm.Sections.Single(s => s.Title == "Fahrzeuge");
        section.Rows[0].Wache = "FFB Wache 1";
        section.Rows[0].CallSign = "FFB 1/44/1";
        section.Rows[0].Seats = 9;
        section.Rows[0].HasZugfuehrer = true;
        vm.SelectedSection = section;

        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var boxes = view.GetVisualDescendants().OfType<AutoCompleteBox>().ToList();

        // PlaceholderText: Watermark is obsolete in C# under Avalonia 12 (XAML keeps the old name).
        // #137: placeholders now show anonymized examples instead of restating the label.
        Assert.Contains(boxes, b => b.Text == "FFB Wache 1" && b.PlaceholderText == AnonymizedExampleData.BrigadePlaceholder);
        Assert.Contains(boxes, b => b.Text == "FFB 1/44/1" && b.PlaceholderText == AnonymizedExampleData.CallSignPlaceholder);

        // The suggestions are derived from the vehicles and roster as loaded; free text stays possible.
        var wacheBox = boxes.First(b => b.PlaceholderText == AnonymizedExampleData.BrigadePlaceholder);
        Assert.Equal(new[] { "FFB Wache 1", "Aich", "Puch" }, wacheBox.ItemsSource);
        Assert.Equal(
            new[] { "FFB 1/10/1", "Aich 42/1", "Puch 40/1", "Land 1" },
            boxes.First(b => b.PlaceholderText == AnonymizedExampleData.CallSignPlaceholder).ItemsSource);
        Assert.Contains(view.GetVisualDescendants().OfType<NumericUpDown>(), n => n.Value == 9);

        // The ZF checkbox (#missing-stammdaten-field) marks a command vehicle carrying the Zugführer.
        var zfCheckBox = Assert.Single(
            view.GetVisualDescendants().OfType<CheckBox>(),
            c => ToolTip.GetTip(c) as string == "Zugführerfahrzeug" && c.IsChecked == true);
        Assert.Equal(new Vehicle("FFB Wache 1", "FFB 1/44/1", 9, HasZugfuehrer: true), section.ToValues()[0]);
        Assert.Equal(3, section.ToValues().Count);

        // The header Grid and the row template's Grid are separate layout passes; the header's
        // trailing columns used to size to "Auto" against its own (empty) content instead of the
        // row template's buttons, so the header's star columns came out wider and dragged ZF (and
        // SITZPLÄTZE) to the right of their actual data columns. Both Grids now share fixed pixel
        // widths for those columns, so the header and its column line up exactly.
        var zfHeader = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ZF");
        Assert.Equal(zfHeader.Bounds.X, zfCheckBox.Bounds.X, 1);

        var dir = Path.Combine(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Combine(dir, "master-data-editor-fahrzeuge-after.png"));
    }
}
