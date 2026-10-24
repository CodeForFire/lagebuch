using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
    // that is the path the per-field visibility has to survive. The SBI belongs to a neighbouring
    // brigade (#458), which the list has to mark.
    private static Person[] Roster() => new[]
    {
        new Person("Mustermann", "Max", "KBR", "Land 1", "01 71 / 1 23 45 67", true, "max.mustermann@example.org", "Kreisbrandrat, KBI Vertretung"),
        new Person("Musterfrau", "Erika", "KBI", "Land 2", "01 71 / 7 65 43 21", true, "erika.musterfrau@example.org", "Inspektionsbereich West"),
        new Person("Beispiel", "Bernd", "KBM", "Land 2/3", "01 71 / 2 34 56 78", true, "bernd.beispiel@example.org", "KBM Gefahrgut, Fachberater Arbeits- und Gesundheitsschutz"),
        new Person("Musterhuber", "Sepp", "KBM", "Land 3/1", "01 71 / 3 45 67 89", true, "sepp.musterhuber@example.org", "Musterdorf, Beispielried, Vorlagenhofen, Schemastetten"),
        new Person("Musterschmid", "Anna", "Fachberater", "Land 9/1", "01 71 / 4 56 78 90", true, "anna.musterschmid@example.org", "Fachberater EDV"),
        new Person("Beispielmeier", "Klara", "PSNV", "Land 9/4", "01 71 / 5 67 89 01", true, "klara.beispielmeier@example.org", "PSNV-E Team"),
        new Person("Musterlechner", "Hans", "SBI", "Musterstadt 1", "01 71 / 6 78 90 12", false, null, "SBI Musterstadt; Musterstadt, Beispielbach"),
        new Person("Vorlage", "Kim", "Jugend", null, "01 71 / 7 89 01 23", true, null, null),
    };

    private static MasterDataSet MasterData() =>
        WorkspaceRenderHelper.MasterData() with { Personnel = Roster() };

    private static (Window Window, IncidentWorkspaceViewModel Vm) ShowWorkspace(
        MasterDataSet md, double width = 1920, double height = 1032)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(),
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
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
        var window = new Window { Content = new IncidentWorkspaceView { DataContext = vm }, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static ContactsView ContactsView(Window window) =>
        WorkspaceRenderHelper.SelectedTabContent(window).GetVisualDescendants().OfType<ContactsView>().Single();

    private static void Capture(Window window, string name)
    {
        var dir = Environment.GetEnvironmentVariable("RENDER_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        // Let the buttons' 150ms brush transitions finish. They run on the wall clock, not on render
        // ticks, so a frame captured at once shows a background still halfway out of Transparent
        // (#00FFFFFF) as a grey it never settles on. Screenshot path only: no assertion waits.
        var settle = Stopwatch.StartNew();
        while (settle.ElapsedMilliseconds < 250)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
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
        Assert.Equal("Beispiel, Bernd", vm.Contacts.VisibleContacts[0].Entry.Name);

        // The hit is drawn, not just computed: the Notiz carries exactly one highlighted run.
        var note = ContactsView(window).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ContactNote");
        var hit = Assert.Single(note.Inlines!.OfType<Run>(), r => r.Background is not null);
        Assert.Equal("Gefahrgut", hit.Text);
        Capture(window, "kontakte-suche.png");
    }

    [AvaloniaFact]
    public void A_member_of_a_neighbouring_brigade_carries_the_fremd_tag()
    {
        var (window, _) = ShowWorkspace(MasterData());
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");

        var tags = ContactsView(window).GetVisualDescendants().OfType<Border>()
            .Where(b => b.Name == "ForeignTag" && b.IsEffectivelyVisible);
        Assert.Single(tags);
    }

    // A Fahrzeugtelefon is listed after the people, and tagged, so its Funkrufname in the name slot
    // is not read as a person's name. A vehicle without a number stays out of the list.
    [AvaloniaFact]
    public void A_vehicle_with_a_number_is_listed_and_carries_the_fahrzeug_tag()
    {
        var md = MasterData() with
        {
            Vehicles = new[]
            {
                new Vehicle("FF Musterstadt", "Florian Musterstadt 11/1", 4, true, Phone: "01 71 / 2 34 56 78"),
                new Vehicle("FF Musterstadt", "Florian Musterstadt 40/1", 9),
            },
        };
        var (window, vm) = ShowWorkspace(md);
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");

        Assert.Equal(9, vm.Contacts.VisibleContacts.Count);
        Assert.Equal("Florian Musterstadt 11/1", vm.Contacts.VisibleContacts[^1].Entry.Name);
        var tags = ContactsView(window).GetVisualDescendants().OfType<Border>()
            .Where(b => b.Name == "VehicleTag" && b.IsEffectivelyVisible);
        Assert.Single(tags);
        Capture(window, "kontakte-fahrzeug.png");
    }

    // #700: the roster writes "01 71 / 1 23 45 67"; the card reads it the DIN 5008 way, and a
    // number with no global tel: form is plain text rather than a button that can only fail.
    [AvaloniaFact]
    public void A_card_shows_the_number_in_its_reading_form_and_dials_only_what_it_can()
    {
        var roster = Roster().Append(new Person("Wache", "Florian", null, null, "112")).ToArray();
        var (window, _) = ShowWorkspace(WorkspaceRenderHelper.MasterData() with { Personnel = roster });
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");
        var view = ContactsView(window);

        var calls = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("contact-call") && b.IsEffectivelyVisible).ToList();
        Assert.Equal(8, calls.Count);
        Assert.Contains(calls, b => AutomationProperties.GetName(b) == "0171 1234567 wählen");
        Assert.Contains(
            view.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "0171 1234567" && t.IsEffectivelyVisible);

        var plain = Assert.Single(
            view.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Name == "ContactPhoneText" && t.IsEffectivelyVisible);
        Assert.Equal("112", plain.Text);
    }

    // Opening the tab by click puts the caret in the search box: WorkspaceFocusTests (#542).

    // 412x915 is the phone PhoneLayoutTests renders at. The overflow check itself lives there;
    // this is the screenshot.
    [AvaloniaFact]
    public void The_kontakte_tab_renders_on_a_phone()
    {
        var (window, vm) = ShowWorkspace(MasterData(), 412, 915);
        WorkspaceRenderHelper.SelectTab(window, "KONTAKTE");

        vm.Contacts.FilterText = "kbm";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.Contacts.VisibleContacts.Count);
        Capture(window, "kontakte-phone.png");
    }

    [AvaloniaFact]
    public void Searching_matches_a_covered_feuerwehr()
    {
        var (_, vm) = ShowWorkspace(MasterData());

        vm.Contacts.FilterText = "Beispielried";

        Assert.Single(vm.Contacts.VisibleContacts);
        Assert.Equal("Musterhuber, Sepp", vm.Contacts.VisibleContacts[0].Entry.Name);
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

        var view = ContactsView(window);
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
            .First(i => vm.Sections[i].Title == "Personal");
        categories.SelectedIndex = personal;
        Dispatcher.UIThread.RunJobs();

        var boxes = view.GetVisualDescendants().OfType<TextBox>().ToArray();
        Assert.Contains(boxes, b => b.Text == "max.mustermann@ff-musterstadt.example");
        Assert.Contains(boxes, b => b.Text == AnonymizedExampleData.PersonNote);
        Capture(window, "stammdaten-personal.png");
    }

    // An address Lagebuch would refuse to open would be a dead button in the Kontakte tab. So the
    // editor says so where it is typed in and refuses to save until then -- exactly as it does
    // for duplicate Funkrufnamen.
    [AvaloniaFact]
    public void An_unusable_address_names_the_person_and_blocks_saving()
    {
        var vm = new MasterDataEditorViewModel(
            new ContactsSampleProvider(), new FakeDialogs(), new ContactsNoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var categories = view.GetControl<ListBox>("CategoryList");
        categories.SelectedIndex = Enumerable.Range(0, categories.ItemCount)
            .First(i => vm.Sections[i].Title == "Personal");
        Dispatcher.UIThread.RunJobs();

        var banner = view.GetControl<Border>("PersonnelConflictsBanner");
        Assert.False(banner.IsEffectivelyVisible);

        vm.Sections.OfType<PersonnelSection>().Single().Rows[0].Email = "max@@ff-musterstadt.example";
        Dispatcher.UIThread.RunJobs();

        Assert.True(banner.IsEffectivelyVisible);

        // IsEffectivelyEnabled, not IsEnabled: a command whose CanExecute is false leaves the local
        // property alone and greys the button out through the effective state.
        Assert.False(view.GetControl<Button>("SaveButton").IsEffectivelyEnabled);
        Capture(window, "stammdaten-email-ungueltig.png");
    }

    // #650: a row with a Vorname but no Nachname used to vanish on SPEICHERN.
    [AvaloniaFact]
    public void A_row_without_a_last_name_is_named_and_blocks_saving()
    {
        var vm = new MasterDataEditorViewModel(
            new ContactsSampleProvider(), new FakeDialogs(), new ContactsNoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1080, Height = 680 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var categories = view.GetControl<ListBox>("CategoryList");
        categories.SelectedIndex = Enumerable.Range(0, categories.ItemCount)
            .First(i => vm.Sections[i].Title == "Personal");
        Dispatcher.UIThread.RunJobs();

        var section = vm.Sections.OfType<PersonnelSection>().Single();
        section.AddCommand.Execute(null);
        section.Rows[^1].FirstName = "Erika";
        Dispatcher.UIThread.RunJobs();
        Capture(window, "stammdaten-ohne-nachname.png");

        Assert.True(view.GetControl<Border>("PersonnelConflictsBanner").IsEffectivelyVisible);
        Assert.False(view.GetControl<Button>("SaveButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void The_stammdaten_personnel_editor_renders_on_a_phone()
    {
        var vm = new MasterDataEditorViewModel(
            new ContactsSampleProvider(), new FakeDialogs(), new ContactsNoFiles());
        var view = new MasterDataEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 412, Height = 915 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var categories = view.GetControl<ListBox>("CategoryList");
        categories.SelectedIndex = Enumerable.Range(0, categories.ItemCount)
            .First(i => vm.Sections[i].Title == "Personal");
        Dispatcher.UIThread.RunJobs();

        var boxes = view.GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible).ToArray();
        Assert.Contains(boxes, b => b.Text == "max.mustermann@ff-musterstadt.example");
        Capture(window, "stammdaten-personal-phone.png");
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
