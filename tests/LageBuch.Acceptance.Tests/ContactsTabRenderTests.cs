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

// Mockup render for the Kontakte issue. Doubles as the screenshot capture (RENDER_OUT), same
// idiom as LinksTabRenderTests.
public class ContactsTabRenderTests
{
    // A fictional Kreisbrandinspektion, shaped like the real roster: a Funkrufname, a Dienstgrad,
    // and a Notiz that is either a Fachgebiet or the Feuerwehren somebody covers. Two entries
    // deliberately carry no address and no Notiz -- the roster's xltm-sourced people don't, and
    // that is the path the per-field visibility has to survive.
    private static Person[] Roster() => new[]
    {
        new Person("Mustermann", "Max", "KBR", "Land 1", "01 71 / 1 23 45 67", "max.mustermann@example.org", "Kreisbrandrat, KBI Vertretung"),
        new Person("Musterfrau", "Erika", "KBI", "Land 2", "01 71 / 7 65 43 21", "erika.musterfrau@example.org", "Inspektionsbereich West"),
        new Person("Beispiel", "Bernd", "KBM", "Land 2/3", "01 71 / 2 34 56 78", "bernd.beispiel@example.org", "KBM Gefahrgut, Fachberater Arbeits- und Gesundheitsschutz"),
        new Person("Musterhuber", "Sepp", "KBM", "Land 3/1", "01 71 / 3 45 67 89", "sepp.musterhuber@example.org", "Musterdorf, Beispielried, Vorlagenhofen, Schemastetten"),
        new Person("Musterschmid", "Anna", "Fachberater", "Land 9/1", "01 71 / 4 56 78 90", "anna.musterschmid@example.org", "Fachberater EDV"),
        new Person("Beispielmeier", "Klara", "PSNV", "Land 9/4", "01 71 / 5 67 89 01", "klara.beispielmeier@example.org", "PSNV-E Team"),
        new Person("Musterlechner", "Hans", "SBI", "Musterstadt 1", "01 71 / 6 78 90 12", null, "SBI Musterstadt; Musterstadt, Beispielbach"),
        new Person("Vorlage", "Kim", "Jugend", null, "01 71 / 7 89 01 23", null, null),
    };

    private static MasterDataSet MasterData() =>
        WorkspaceRenderHelper.MasterData() with { Personnel = Roster() };

    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace(MasterDataSet md)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            new[] { ("Blaulicht aus?", false) },
            Array.Empty<(string, bool)>());
        var vm = new IncidentWorkspaceViewModel(
            session,
            new FixedClock(),
            new NoopTicker(),
            md,
            new FakeDialogs(),
            new NoopAlarmService(),
            new NoopIncidentHostController());
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = 1920, Height = 1032 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

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
    public void The_kontakte_tab_lists_the_roster()
    {
        var (window, vm) = ShowWorkspace(MasterData());
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");

        Assert.Equal(8, vm.Contacts.VisibleContacts.Count);
        Capture(window, "kontakte-liste.png");
    }

    [AvaloniaFact]
    public void Searching_matches_the_notiz_field()
    {
        var (window, vm) = ShowWorkspace(MasterData());
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");

        vm.Contacts.FilterText = "gefahrgut";
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Contacts.VisibleContacts);
        Assert.Equal("Beispiel, Bernd", vm.Contacts.VisibleContacts[0].DisplayName);
        Capture(window, "kontakte-suche.png");
    }

    [AvaloniaFact]
    public void Searching_matches_a_covered_feuerwehr()
    {
        var (_, vm) = ShowWorkspace(MasterData());

        vm.Contacts.FilterText = "Beispielried";

        Assert.Single(vm.Contacts.VisibleContacts);
        Assert.Equal("Musterhuber, Sepp", vm.Contacts.VisibleContacts[0].DisplayName);
    }

    [AvaloniaFact]
    public void Terms_may_arrive_in_any_order_and_across_fields()
    {
        var (_, vm) = ShowWorkspace(MasterData());

        vm.Contacts.FilterText = "gefahrgut kbm";
        Assert.Single(vm.Contacts.VisibleContacts);

        vm.Contacts.FilterText = "beispiel gefahrgut";
        Assert.Single(vm.Contacts.VisibleContacts);
    }

    // The fields are joined with a separator, so a term can never match the concatenation of two
    // adjacent ones: "Fachberater" sits directly above "Land 9/1" in the haystack.
    [AvaloniaFact]
    public void A_term_does_not_match_across_a_field_boundary()
    {
        var (_, vm) = ShowWorkspace(MasterData());

        vm.Contacts.FilterText = "FachberaterLand";

        Assert.Empty(vm.Contacts.VisibleContacts);
    }

    [AvaloniaFact]
    public void The_dialled_digits_find_a_number_written_with_separators()
    {
        var (_, vm) = ShowWorkspace(MasterData());

        vm.Contacts.FilterText = "01712345678";

        Assert.Single(vm.Contacts.VisibleContacts);
    }

    [AvaloniaFact]
    public void An_unmatched_term_leaves_the_roster_intact_behind_a_hint()
    {
        var (window, vm) = ShowWorkspace(MasterData());
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");

        vm.Contacts.FilterText = "Höhenrettung";
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Contacts.VisibleContacts);
        Assert.Equal(8, vm.Contacts.Contacts.Count);
        Assert.Equal("Kein Kontakt passt zu „Höhenrettung“.", vm.Contacts.NoMatchesMessage);
        Capture(window, "kontakte-kein-treffer.png");
    }

    [AvaloniaFact]
    public void An_empty_roster_shows_the_hint_and_hides_the_search_box()
    {
        var (window, _) = ShowWorkspace(
            WorkspaceRenderHelper.MasterData() with { Personnel = Array.Empty<Person>() });
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");

        var view = WorkspaceRenderHelper.SelectedTabContent(window)
            .GetVisualDescendants().OfType<ContactsView>().Single();
        Assert.True(view.GetControl<TextBlock>("EmptyText").IsEffectivelyVisible);
        Assert.False(view.GetControl<TextBox>("ContactSearchBox").IsEffectivelyVisible);
        Capture(window, "kontakte-leer.png");
    }

    // The Stammdaten editor has to round-trip the two new fields, or an import -> edit -> save
    // cycle deletes hand-transcribed roster data. This renders the two-line personnel row that
    // carries them; MasterDataSectionTests pins the round-trip itself.
    [AvaloniaFact]
    public void The_stammdaten_personnel_editor_carries_email_and_notiz()
    {
        var vm = new MasterDataEditorViewModel(
            new ContactsSampleProvider(), new FakeDialogs(), new ContactsNoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var categories = view.GetControl<ListBox>("CategoryList");
        var personal = Enumerable.Range(0, categories.ItemCount)
            .First(i => (vm.Sections[i] as EditorSection)?.Title == "Personal");
        categories.SelectedIndex = personal;
        Dispatcher.UIThread.RunJobs();

        var boxes = view.GetVisualDescendants().OfType<TextBox>().ToArray();
        Assert.Contains(boxes, b => b.Text == "max.mustermann@ff-musterstadt.example");
        Assert.Contains(boxes, b => b.Text == AnonymizedExampleData.PersonNote);
        Capture(window, "stammdaten-personal.png");
    }

    private sealed class ContactsSampleProvider : IMasterDataProvider
    {
        public MasterDataSet Get() => WorkspaceRenderHelper.MasterData();

        public void Save(MasterDataSet set)
        {
        }
    }

    private sealed class ContactsNoFiles : IMasterDataFileService
    {
        public MasterDataImportResult Read(string path) => new(MasterDataSet.Empty, Array.Empty<string>());

        public void Write(string path, MasterDataSet set)
        {
        }
    }
}
