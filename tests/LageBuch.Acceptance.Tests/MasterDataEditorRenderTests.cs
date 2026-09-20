using Avalonia;
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
            TruppTypes = new[]
            {
                new TruppType("Angriffstrupp"),
                new TruppType("Wassertrupp"),
                new TruppType("CSA-Trupp", 3, 20),

                // Three people like the CSA-Trupp but the full 30 minutes (#418) -- the pair no
                // rule keyed off a designation could ever have expressed.
                new TruppType("Strahlenschutztrupp", 3, 30),
            },

            // Wachen and Funkrufnamen derive from these rows (plus the roster's "Land 1").
            Vehicles = new[]
            {
                new Vehicle("FFB Wache 1", "FFB 1/10/1", 9),
                new Vehicle("Aich", "Aich 42/1", 6),
                new Vehicle("Puch", "Puch 40/1", 9),
            },
            ChecklistTemplates = ChecklistTemplate.AufbauAbbau(
                new[]
                {
                    new ChecklistTemplateItem("Aufstellort ELW frei?", true),
                    new ChecklistTemplateItem("Kennleuchte ein, Blaulicht aus?", false),
                },
                new[] { new ChecklistTemplateItem("Fahrzeug abgerüstet?", true) }),
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

        // Two rails: the app's own categories, then the brigade's own Checklisten under their
        // own heading. Einstellungen + Navigation + 6 data categories, and this fixture's 2.
        Assert.Equal(8, view.GetControl<ListBox>("CategoryList").ItemCount);
        Assert.Equal(2, view.GetControl<ListBox>("ChecklistList").ItemCount);
        Assert.False(view.GetControl<TextBlock>("NoChecklistsText").IsVisible);
        Assert.True(view.GetControl<Button>("SaveButton").IsVisible);

        // Capture the PR screenshot (real Skia backend rasterizes the embedded fonts).
        var dir = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        var path = Path.Join(dir, "master-data-editor.png");
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(path);
        Assert.True(new FileInfo(path).Length > 0);
    }

    // The ETB's checkbox being disabled is a promise the UI makes, not just a view-model flag:
    // it is the legally relevant record and every Systemmeldung lands there. The resolver ignores
    // a hidden ETB anyway, but this is where an operator is told why they cannot switch it off.
    [AvaloniaFact]
    public void Navigation_section_lists_every_entry_and_locks_the_etb_checkbox()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Navigation");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var labels = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("ETB", labels);
        Assert.Contains("Atemschutz", labels);
        Assert.Contains("Aufbau", labels);
        Assert.Contains("Abbau", labels);

        // One checkbox per row; exactly one of them -- the ETB's -- is disabled.
        var checkBoxes = view.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(NavModules.All.Count + 2, checkBoxes.Count);
        var locked = Assert.Single(checkBoxes, c => !c.IsEnabled);
        Assert.True(locked.IsChecked);
    }

    // A Checkliste's editor gives each row a mandatory checkbox alongside the text (#72) --
    // distinct from the single-string-per-row EditableListSection the other categories use.
    [AvaloniaFact]
    public void Checkliste_aufbau_section_renders_text_and_mandatory_checkbox_per_row()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Checklists.Single(c => c.Title == "Aufbau");
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

        var dir = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, "master-data-editor-checkliste-aufbau.png"));
    }

    // The Trupp-Typen editor gained a Staerke and an Einsatzzeit per row (#398), so it is no
    // longer the single-string-per-row EditableListSection the simple categories use.
    [AvaloniaFact]
    public void Trupp_typen_section_renders_staerke_and_einsatzzeit_per_row()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        vm.SelectedSection = vm.Sections.Single(s => s.Title == "Trupp-Typen");
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var names = view.GetVisualDescendants().OfType<TextBox>()
            .Where(t => t.Text is "Angriffstrupp" or "Wassertrupp" or "CSA-Trupp" or "Strahlenschutztrupp")
            .ToList();
        Assert.Equal(4, names.Count);

        // One Staerke and one Einsatzzeit spinner per row, carrying the row's own values. Two
        // types are three people, and they carry *different* Einsatzzeiten (20 and 30) -- which
        // is exactly what a rule keyed off the designation could not express.
        var numbers = view.GetVisualDescendants().OfType<NumericUpDown>().ToList();
        Assert.Equal(8, numbers.Count);
        Assert.Equal(2, numbers.Count(n => n.Value == 3));
        Assert.Contains(numbers, n => n.Value == 20);
        Assert.Contains(numbers, n => n.Value == 30);

        var dir = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, "master-data-editor-trupp-typen.png"));
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

        var dir = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, "master-data-editor-links.png"));
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

        var dir = Path.Join(Path.GetTempPath(), "lagebuch-shots");
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.SavePng(Path.Join(dir, "master-data-editor-fahrzeuge-after.png"));
    }

    // The rail is two ListBoxes over one SelectedSection, which only works because that property
    // refuses a null write. This has to be driven through the controls: Avalonia's SelectedItem is
    // a two-way direct property with no re-entrancy guard, so picking in one list makes the other
    // clear itself and write null back. A view-model-only test passes even when the XAML is wrong.
    [AvaloniaFact]
    public void Picking_a_checklist_does_not_let_the_category_rail_clear_the_selection()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var categories = view.GetControl<ListBox>("CategoryList");
        var checklists = view.GetControl<ListBox>("ChecklistList");

        checklists.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(vm.Checklists[0], vm.SelectedSection);
        Assert.Null(categories.SelectedItem);

        // ...and back again, so neither direction leaves both rails blank.
        categories.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(vm.Sections[0], vm.SelectedSection);
        Assert.Null(checklists.SelectedItem);
    }

    // A brigade may keep no Checklisten at all. The group then shows a line saying so rather than
    // an empty box, and the list is collapsed so it is not a dead tab stop.
    [AvaloniaFact]
    public void An_editor_without_checklisten_shows_the_empty_state()
    {
        var vm = new MasterDataEditorViewModel(new EmptyProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.GetControl<TextBlock>("NoChecklistsText").IsVisible);
        Assert.False(view.GetControl<ListBox>("ChecklistList").IsVisible);
        Assert.Equal(8, view.GetControl<ListBox>("CategoryList").ItemCount);

        // Laid out, not merely IsVisible -- that property stays true for a button clipped to
        // nothing, which is exactly the failure a docked action is supposed to rule out.
        Assert.True(view.GetControl<Button>("AddChecklistButton").Bounds.Height > 0);
    }

    // The rail used to be two ListBoxes each scrolling itself. Eight fixed categories ate the
    // height, so the Checklisten got a ~90px viewport, grew their own scrollbar for a single
    // entry, and clipped a row mid-card. One ScrollViewer owns the rail now; these pin that it
    // stays that way, because the symptom only appears once the rail is too short.
    [AvaloniaFact]
    public void The_rail_has_exactly_one_scroll_region_when_it_overflows()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 480 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Captured before the assertions: on a build with the old nested scrollers this frame is
        // the "before" screenshot, showing the inner scrollbar and the half-row.
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame()!;
            frame.SavePng(Path.Join(dir, "stammdaten-rail-scrolling.png"));
        }

        var rail = view.GetControl<ScrollViewer>("RailScroll");
        var railMessage = $"the rail is {rail.Extent.Height:F0}px in a {rail.Viewport.Height:F0}px "
            + "viewport -- it does not overflow at 480px, so this fixture proves nothing.";
        Assert.True(rail.Extent.Height > rail.Viewport.Height, railMessage);

        // Each list is as tall as its own rows. This is the assertion that matters: because the
        // lists are set to Disabled they clip rather than scroll, so a squeezed list shows no
        // scrollbar and an Extent-vs-Viewport check would pass while rows quietly vanished.
        foreach (var name in new[] { "CategoryList", "ChecklistList" })
        {
            var list = view.GetControl<ListBox>(name);
            var listRows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.NotEmpty(listRows);

            var last = listRows[^1];
            var needed = last.TranslatePoint(new Point(0, last.Bounds.Height), list)!.Value.Y;
            var squeezedMessage = $"{name} is {list.Bounds.Height:F0}px tall but its rows need "
                + $"{needed:F0}px -- it is squeezed inside the rail, which is how the Checklisten "
                + "ended up with a viewport too small to show one entry.";
            Assert.True(needed <= list.Bounds.Height + 0.5, squeezedMessage);

            var inner = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
            var innerMessage = $"{name} is {inner.Extent.Height:F0}px in a "
                + $"{inner.Viewport.Height:F0}px viewport -- it scrolls itself, so the rail has "
                + "two scroll regions again.";
            Assert.True(inner.Extent.Height <= inner.Viewport.Height + 0.5, innerMessage);
        }

        // No row cut in half: the last category's bottom edge lies inside the scrolled extent.
        var rows = view.GetControl<ListBox>("CategoryList").GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Assert.Equal(8, rows.Length);
        var lastBottom = rows[^1].TranslatePoint(new Point(0, rows[^1].Bounds.Height), rail)!.Value.Y;
        var clipMessage = $"the last category ends {lastBottom:F0}px into a "
            + $"{rail.Extent.Height:F0}px extent -- it is clipped.";
        Assert.True(lastBottom <= rail.Extent.Height + 0.5, clipMessage);

        // The docked action survives a rail too short for its content.
        var button = view.GetControl<Button>("AddChecklistButton");
        Assert.True(button.Bounds.Height > 0);
        Assert.True(
            button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value.Y <= window.Height,
            "«+ NEUE CHECKLISTE» is pushed off the bottom of a short rail.");
    }

    [AvaloniaFact]
    public void A_rail_that_fits_shows_no_scrollbar_at_all()
    {
        var vm = new MasterDataEditorViewModel(new SampleProvider(), new FakeDialogs(), new NoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rail = view.GetControl<ScrollViewer>("RailScroll");
        var message = $"the rail wants {rail.Extent.Height:F0}px in a "
            + $"{rail.Viewport.Height:F0}px viewport -- it scrolls even though there is room "
            + "for every entry.";
        Assert.True(rail.Extent.Height <= rail.Viewport.Height + 0.5, message);
    }

    private sealed class EmptyProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => MasterDataSet.Empty;

        public void Save(MasterDataSet set)
        {
        }
    }
}
